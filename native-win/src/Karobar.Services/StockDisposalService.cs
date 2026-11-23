using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Karobar.Services;

public class StockDisposalValidationException : Exception
{
    public Dictionary<string, string[]> Errors { get; }

    public StockDisposalValidationException(string field, string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>
        {
            [field] = new[] { message }
        };
    }

    public StockDisposalValidationException(string message, Dictionary<string, string[]> errors) : base(message)
    {
        Errors = errors;
    }
}

public class StockDisposalService
{
    private readonly KarobarDb _db;

    public const string StatusQuarantined = "quarantined";
    public const string StatusDisposed = "disposed";
    public const string StatusRestored = "restored";

    public static readonly IReadOnlyDictionary<string, string> Causes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["shelf_expiry"] = "Shelf Expiry (Expired Batch)",
        ["breakage_damage"] = "Physical Breakage / Packaging Damage",
        ["temperature_spoilage"] = "Cold-Chain / Temperature Spoilage",
        ["customer_return_defect"] = "Defective Customer Return",
        ["manufacturer_recall"] = "Manufacturer / Regulatory Recall",
        ["water_pest_damage"] = "Moisture / Pest Contamination",
        ["theft_shrinkage"] = "Unrecoverable Shrinkage / Pilferage",
        ["other"] = "Other Cause"
    };

    public static readonly IReadOnlyDictionary<string, string> Methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["incineration"] = "High-Temperature Incineration",
        ["chemical_neutralization"] = "Chemical Neutralization / Encapsulation",
        ["municipal_waste"] = "Authorized Bio-Hazard / Municipal Waste",
        ["supplier_destruction"] = "Handed to Supplier (No Credit)",
        ["drain_flush"] = "Authorized Dilution & Drain Flush",
        ["quarantine_holding"] = "In Quarantine Bay (Pending Destruction)",
        ["other"] = "Other Method"
    };

    public StockDisposalService(KarobarDb db)
    {
        _db = db;
    }

    public static string GetCauseLabel(string? cause)
    {
        if (string.IsNullOrWhiteSpace(cause)) return "";
        if (Causes.TryGetValue(cause, out var label)) return label;
        return char.ToUpperInvariant(cause[0]) + cause[1..].Replace('_', ' ');
    }

    public static string GetMethodLabel(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return "";
        if (Methods.TryGetValue(method, out var label)) return label;
        return char.ToUpperInvariant(method[0]) + method[1..].Replace('_', ' ');
    }

    public async Task<string> GenerateDisposalNumberAsync(string? dateStr = null, bool preview = false)
    {
        var dt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dateStr) && DateTime.TryParse(dateStr, out var parsed))
        {
            dt = parsed;
        }

        var ym = dt.ToString("yyyyMM");
        var prefix = $"DSP-{ym}-";
        var seq = new DocumentSequenceService(_db);

        if (preview)
        {
            return await seq.PreviewAsync($"stock_disposal:{ym}", prefix, 4, "stock_disposals", "disposal_number");
        }

        return await seq.NextAsync($"stock_disposal:{ym}", prefix, 4, "stock_disposals", "disposal_number");
    }

    public async Task<Dictionary<string, object?>> CreateDisposalAsync(Dictionary<string, object?> data, long? userId)
    {
        var rawStatus = data.TryGetValue("status", out var stObj) ? stObj?.ToString()?.Trim().ToLowerInvariant() : null;
        var status = rawStatus is (StatusQuarantined or StatusDisposed) ? rawStatus : StatusQuarantined;

        var rawDate = data.TryGetValue("disposal_date", out var ddObj) ? ddObj?.ToString()?.Trim() : null;
        var disposalDate = !string.IsNullOrWhiteSpace(rawDate) && DateTime.TryParse(rawDate, out var parsedDate)
            ? parsedDate.ToString("yyyy-MM-dd")
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var requestedDisposalNumber = data.TryGetValue("disposal_number", out var dnObj) && !string.IsNullOrWhiteSpace(dnObj?.ToString())
            ? dnObj.ToString()!.Trim()
            : null;

        var cause = data.TryGetValue("cause", out var cObj) && !string.IsNullOrWhiteSpace(cObj?.ToString())
            ? cObj.ToString()!.Trim()
            : "shelf_expiry";
        var disposalMethod = data.TryGetValue("disposal_method", out var dmObj) && !string.IsNullOrWhiteSpace(dmObj?.ToString())
            ? dmObj.ToString()!.Trim()
            : (status == StatusQuarantined ? "quarantine_holding" : "incineration");

        bool recordExpense = true;
        if (data.TryGetValue("record_expense", out var reObj) && reObj != null)
        {
            recordExpense = ParseFlexibleBool(reObj, true);
        }

        var itemsInput = ExtractItemsList(data);
        if (itemsInput.Count == 0)
        {
            throw new StockDisposalValidationException("items", "At least one disposal item is required.");
        }

        var parsedItemRequests = new List<(Dictionary<string, object?> Row, long ProductId, decimal DisposeQty)>(itemsInput.Count);
        for (int idx = 0; idx < itemsInput.Count; idx++)
        {
            var row = itemsInput[idx];
            var disposeQty = ToDecimal(row.GetValueOrDefault("dispose_qty"));
            if (disposeQty <= 0m)
            {
                throw new StockDisposalValidationException($"items.{idx}.dispose_qty", "Disposal quantity must be greater than zero.");
            }

            if (!row.TryGetValue("product_id", out var pidObj) || pidObj == null || !long.TryParse(pidObj.ToString(), out var productId))
            {
                throw new StockDisposalValidationException($"items.{idx}.product_id", "Valid product is required.");
            }

            parsedItemRequests.Add((row, productId, disposeQty));
        }

        var witnessName = data.TryGetValue("witness_name", out var wnObj) && !string.IsNullOrWhiteSpace(wnObj?.ToString())
            ? wnObj.ToString()!.Trim()
            : null;
        var witnessDesignation = data.TryGetValue("witness_designation", out var wdObj) && !string.IsNullOrWhiteSpace(wdObj?.ToString())
            ? wdObj.ToString()!.Trim()
            : null;
        var referenceNo = data.TryGetValue("reference_no", out var rnObj) && !string.IsNullOrWhiteSpace(rnObj?.ToString())
            ? rnObj.ToString()!.Trim()
            : null;
        var notes = data.TryGetValue("notes", out var ntObj) && !string.IsNullOrWhiteSpace(ntObj?.ToString())
            ? ntObj.ToString()!.Trim()
            : null;

        var hasBatchesTable = await _db.HasTableAsync("batches");
        await new DocumentSequenceService(_db).EnsureTableExistsAsync();

        string disposalNumber;
        long disposalId;
        decimal totalCostValue = 0m;

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            if (!string.IsNullOrWhiteSpace(requestedDisposalNumber))
            {
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM stock_disposals WHERE disposal_number = @disposalNumber",
                    new { disposalNumber = requestedDisposalNumber },
                    tx);
                disposalNumber = exists == 0
                    ? requestedDisposalNumber
                    : await AllocateDisposalNumberInTxAsync(conn, tx, disposalDate);
            }
            else
            {
                disposalNumber = await AllocateDisposalNumberInTxAsync(conn, tx, disposalDate);
            }

            var preparedItems = new List<Dictionary<string, object?>>();
            bool hasNarcotics = false;
            decimal totalQty = 0m;
            decimal totalRetailValue = 0m;
            var pendingBatchDeductions = new Dictionary<long, decimal>();
            var pendingUnbatchedDeductions = new Dictionary<long, decimal>();

            for (int idx = 0; idx < parsedItemRequests.Count; idx++)
            {
                var (row, productId, disposeQty) = parsedItemRequests[idx];

                var product = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
                if (product == null)
                {
                    throw new StockDisposalValidationException($"items.{idx}.product_id", "Selected product does not exist.");
                }

                if (IsProductNarcotic(product))
                {
                    hasNarcotics = true;
                }

                var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{productId}";
                Dictionary<string, object?>? batch = null;
                var batchNumber = row.TryGetValue("batch_number", out var bnObj) && !string.IsNullOrWhiteSpace(bnObj?.ToString())
                    ? bnObj.ToString()!.Trim()
                    : null;

                if (hasBatchesTable)
                {
                    if (row.TryGetValue("batch_id", out var bidObj) && bidObj != null && long.TryParse(bidObj.ToString(), out var batchId) && batchId > 0)
                    {
                        batch = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM batches WHERE id = @batchId AND product_id = @productId",
                            new { batchId, productId }, conn, tx);
                    }
                    else if (batchNumber != null)
                    {
                        batch = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @batchNumber LIMIT 1",
                            new { productId, batchNumber }, conn, tx);
                    }
                }

                decimal previousQty;
                decimal remainingQty;
                string? expiryDate;

                if (batch != null)
                {
                    var bid = Convert.ToInt64(batch["id"]);
                    var alreadyDeducted = pendingBatchDeductions.GetValueOrDefault(bid, 0m);
                    previousQty = Math.Max(0m, ToDecimal(batch.GetValueOrDefault("quantity")) - alreadyDeducted);
                    var bNum = batch.GetValueOrDefault("batch_number")?.ToString() ?? batchNumber ?? "";
                    if (disposeQty > previousQty)
                    {
                        throw new StockDisposalValidationException(
                            $"items.{idx}.dispose_qty",
                            $"Insufficient stock in batch '{bNum}' for '{prodName}'. Available: {previousQty:0.##}, requested: {disposeQty:0.##}.");
                    }
                    remainingQty = Math.Max(0m, previousQty - disposeQty);
                    pendingBatchDeductions[bid] = alreadyDeducted + disposeQty;
                    batchNumber = bNum;
                    expiryDate = batch.GetValueOrDefault("expiry_date")?.ToString();
                }
                else
                {
                    var alreadyDeducted = pendingUnbatchedDeductions.GetValueOrDefault(productId, 0m);
                    previousQty = Math.Max(0m, StocktakeService.GetProductQty(product) - alreadyDeducted);
                    decimal totalBatchQty = 0m;
                    if (hasBatchesTable)
                    {
                        totalBatchQty = await _db.ExecuteScalarAsync<decimal?>(
                            "SELECT COALESCE(SUM(COALESCE(quantity, 0)), 0) FROM batches WHERE product_id = @productId",
                            new { productId }, conn, tx) ?? 0m;
                    }
                    var unbatchedAvailable = totalBatchQty > 0m ? Math.Max(0m, previousQty - totalBatchQty) : previousQty;
                    if (disposeQty > unbatchedAvailable)
                    {
                        var errMsg = totalBatchQty > 0m
                            ? $"Insufficient unbatched stock for '{prodName}' (unbatched available: {unbatchedAvailable:0.##}, batched stock: {totalBatchQty:0.##}). Please select a specific batch to dispose."
                            : $"Insufficient stock for '{prodName}'. Available: {previousQty:0.##}, requested: {disposeQty:0.##}.";
                        throw new StockDisposalValidationException($"items.{idx}.dispose_qty", errMsg);
                    }
                    remainingQty = Math.Max(0m, previousQty - disposeQty);
                    pendingUnbatchedDeductions[productId] = alreadyDeducted + disposeQty;
                    expiryDate = row.GetValueOrDefault("expiry_date")?.ToString();
                }

                int packSize = Math.Max(1, ToInt(row.GetValueOrDefault("pack_size") ?? product.GetValueOrDefault("pack_size"), 1));
                decimal unitCost = row.TryGetValue("unit_purchase_price", out var uppObj) && uppObj != null && !string.IsNullOrWhiteSpace(uppObj.ToString())
                    ? ToDecimal(uppObj)
                    : ResolveProductUnitCost(product);
                decimal unitSale = ToDecimal(product.GetValueOrDefault("unit_sale_price"));

                var costValue = Math.Round(disposeQty * unitCost, 2);
                var retailValue = Math.Round(disposeQty * unitSale, 2);

                totalQty += disposeQty;
                totalCostValue += costValue;
                totalRetailValue += retailValue;

                preparedItems.Add(new Dictionary<string, object?>
                {
                    ["product"] = product,
                    ["product_id"] = productId,
                    ["batch"] = batch,
                    ["batch_id"] = batch != null ? Convert.ToInt64(batch["id"]) : (long?)null,
                    ["batch_number"] = batchNumber,
                    ["expiry_date"] = expiryDate,
                    ["pack_size"] = packSize,
                    ["previous_qty"] = previousQty,
                    ["dispose_qty"] = disposeQty,
                    ["remaining_qty"] = remainingQty,
                    ["unit_purchase_price"] = Math.Round(unitCost, 2),
                    ["unit_sale_price"] = Math.Round(unitSale, 2),
                    ["cost_value"] = costValue,
                    ["retail_value"] = retailValue,
                    ["item_note"] = row.GetValueOrDefault("item_note")?.ToString()
                });
            }

            if (status == StatusDisposed && hasNarcotics && string.IsNullOrWhiteSpace(witnessName))
            {
                throw new StockDisposalValidationException(
                    "witness_name",
                    "A witness name (Pharmacist / Supervisor) is mandatory when disposing controlled or narcotic products.");
            }

            var causeLabel = GetCauseLabel(cause);
            var methodLabel = GetMethodLabel(disposalMethod);
            var adjNotePrefix = status == StatusDisposed
                ? $"[Stock Disposal {disposalNumber} - {causeLabel} via {methodLabel}]"
                : $"[Quarantine Hold {disposalNumber} - {causeLabel}]";
            var adjNote = string.IsNullOrWhiteSpace(notes) ? adjNotePrefix : $"{adjNotePrefix} {notes}";
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            var adjId = await _db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
            {
                ["posted_number"] = disposalNumber,
                ["posted_date"] = disposalDate,
                ["note"] = adjNote,
                ["total_worth"] = Math.Round(totalCostValue, 2),
                ["user_id"] = userId
            }, conn, tx);

            disposalId = await _db.InsertAsync("stock_disposals", new Dictionary<string, object?>
            {
                ["disposal_number"] = disposalNumber,
                ["disposal_date"] = disposalDate,
                ["status"] = status,
                ["cause"] = cause,
                ["disposal_method"] = disposalMethod,
                ["reference_no"] = referenceNo,
                ["witness_name"] = witnessName,
                ["witness_designation"] = witnessDesignation,
                ["notes"] = notes,
                ["total_qty"] = Math.Round(totalQty, 2),
                ["total_cost_value"] = Math.Round(totalCostValue, 2),
                ["total_retail_value"] = Math.Round(totalRetailValue, 2),
                ["has_narcotics"] = hasNarcotics ? 1 : 0,
                ["record_expense"] = recordExpense ? 1 : 0,
                ["stock_adjustment_id"] = adjId,
                ["expense_id"] = null,
                ["created_by"] = userId,
                ["disposed_by"] = status == StatusDisposed ? userId : null,
                ["disposed_at"] = status == StatusDisposed ? nowStr : null
            }, conn, tx);

            var stocktakeSvc = new StocktakeService(_db);

            foreach (var pItem in preparedItems)
            {
                var product = (Dictionary<string, object?>)pItem["product"]!;
                var productId = Convert.ToInt64(pItem["product_id"]);
                var batch = pItem["batch"] as Dictionary<string, object?>;
                var disposeQty = ToDecimal(pItem["dispose_qty"]);
                var remainingQty = ToDecimal(pItem["remaining_qty"]);

                await _db.InsertAsync("stock_disposal_items", new Dictionary<string, object?>
                {
                    ["stock_disposal_id"] = disposalId,
                    ["product_id"] = productId,
                    ["batch_id"] = pItem["batch_id"],
                    ["batch_number"] = pItem["batch_number"],
                    ["expiry_date"] = pItem["expiry_date"],
                    ["pack_size"] = pItem["pack_size"],
                    ["previous_qty"] = pItem["previous_qty"],
                    ["dispose_qty"] = disposeQty,
                    ["remaining_qty"] = remainingQty,
                    ["unit_purchase_price"] = pItem["unit_purchase_price"],
                    ["unit_sale_price"] = pItem["unit_sale_price"],
                    ["cost_value"] = pItem["cost_value"],
                    ["retail_value"] = pItem["retail_value"],
                    ["item_note"] = pItem["item_note"]
                }, conn, tx);

                await _db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
                {
                    ["stock_adjustment_id"] = adjId,
                    ["product_id"] = productId,
                    ["batch_number"] = pItem["batch_number"],
                    ["expiry"] = pItem["expiry_date"],
                    ["pack_size"] = pItem["pack_size"],
                    ["previous_qty"] = pItem["previous_qty"],
                    ["actual_qty"] = remainingQty,
                    ["diff_qty"] = -disposeQty,
                    ["unit_purchase_price"] = pItem["unit_purchase_price"],
                    ["worth_adjusted"] = pItem["cost_value"]
                }, conn, tx);

                if (batch != null)
                {
                    await _db.UpdateAsync("batches", Convert.ToInt64(batch["id"]), new Dictionary<string, object?>
                    {
                        ["quantity"] = remainingQty
                    }, "id", conn, tx);
                }

                var currentProd = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx) ?? product;
                var newProdQty = Math.Max(0m, StocktakeService.GetProductQty(currentProd) - disposeQty);
                await stocktakeSvc.UpdateProductQtyInDbAsync(currentProd, newProdQty, conn, tx);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (status == StatusDisposed && recordExpense && totalCostValue > 0m)
        {
            var expId = await CreateNonCashWriteOffExpenseAsync(
                disposalNumber,
                disposalDate,
                totalCostValue,
                cause,
                disposalMethod,
                witnessName,
                notes,
                userId);
            if (expId.HasValue)
            {
                await _db.UpdateAsync("stock_disposals", disposalId, new Dictionary<string, object?>
                {
                    ["expense_id"] = expId.Value
                });
            }
        }

        SyncPulseService.Bump("stock");
        return (await GetDisposalByIdAsync(disposalId))!;
    }

    public async Task<Dictionary<string, object?>> FinalizeDisposalAsync(long disposalId, Dictionary<string, object?> data, long? userId)
    {
        bool recordExpense;
        decimal totalCostValue;
        object? existingExpId;
        string disposalNumber;
        string disposalDate;
        string cause;
        string disposalMethod;
        string? witnessName;
        string? notes;

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            var disposal = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id = disposalId }, conn, tx);
            if (disposal == null) throw new KeyNotFoundException("Stock disposal not found.");

            var currentStatus = disposal.GetValueOrDefault("status")?.ToString() ?? "";
            if (!string.Equals(currentStatus, StatusQuarantined, StringComparison.OrdinalIgnoreCase))
            {
                throw new StockDisposalValidationException(
                    "status",
                    $"Only quarantined vouchers can be finalized for disposal. Current status: {currentStatus}.");
            }

            disposalMethod = data.TryGetValue("disposal_method", out var dmObj) && !string.IsNullOrWhiteSpace(dmObj?.ToString())
                ? dmObj.ToString()!.Trim()
                : (disposal.GetValueOrDefault("disposal_method")?.ToString() ?? "incineration");
            if (string.Equals(disposalMethod, "quarantine_holding", StringComparison.OrdinalIgnoreCase))
            {
                disposalMethod = "incineration";
            }

            witnessName = data.ContainsKey("witness_name")
                ? data["witness_name"]?.ToString()?.Trim()
                : disposal.GetValueOrDefault("witness_name")?.ToString()?.Trim();
            var witnessDesignation = data.ContainsKey("witness_designation")
                ? data["witness_designation"]?.ToString()?.Trim()
                : disposal.GetValueOrDefault("witness_designation")?.ToString()?.Trim();

            recordExpense = disposal.GetValueOrDefault("record_expense") is true or 1 or 1L;
            if (data.TryGetValue("record_expense", out var reObj) && reObj != null)
            {
                recordExpense = ParseFlexibleBool(reObj, recordExpense);
            }

            bool hasNarcotics = disposal.GetValueOrDefault("has_narcotics") is true or 1 or 1L;
            if (hasNarcotics && string.IsNullOrWhiteSpace(witnessName))
            {
                throw new StockDisposalValidationException(
                    "witness_name",
                    "A witness name (Pharmacist / Supervisor) is mandatory when disposing controlled or narcotic products.");
            }

            notes = data.TryGetValue("notes", out var ntObj) && ntObj != null
                ? ntObj.ToString()
                : disposal.GetValueOrDefault("notes")?.ToString();

            await _db.UpdateAsync("stock_disposals", disposalId, new Dictionary<string, object?>
            {
                ["status"] = StatusDisposed,
                ["disposal_method"] = disposalMethod,
                ["witness_name"] = string.IsNullOrWhiteSpace(witnessName) ? null : witnessName,
                ["witness_designation"] = string.IsNullOrWhiteSpace(witnessDesignation) ? null : witnessDesignation,
                ["notes"] = notes,
                ["record_expense"] = recordExpense ? 1 : 0,
                ["disposed_by"] = userId,
                ["disposed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            }, "id", conn, tx);

            disposalNumber = disposal.GetValueOrDefault("disposal_number")?.ToString() ?? $"DSP-{disposalId}";
            cause = disposal.GetValueOrDefault("cause")?.ToString() ?? "shelf_expiry";
            var causeLabel = GetCauseLabel(cause);
            var methodLabel = GetMethodLabel(disposalMethod);

            if (disposal.TryGetValue("stock_adjustment_id", out var adjObj) && adjObj != null && long.TryParse(adjObj.ToString(), out var adjId))
            {
                var adjNote = $"[Stock Disposal {disposalNumber} - {causeLabel} via {methodLabel}]" +
                              (!string.IsNullOrWhiteSpace(notes) ? $" {notes}" : "");
                await _db.UpdateAsync("stock_adjustments", adjId, new Dictionary<string, object?>
                {
                    ["note"] = adjNote.Trim()
                }, "id", conn, tx);
            }

            totalCostValue = ToDecimal(disposal.GetValueOrDefault("total_cost_value"));
            existingExpId = disposal.GetValueOrDefault("expense_id");
            disposalDate = disposal.GetValueOrDefault("disposal_date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (recordExpense && totalCostValue > 0m && (existingExpId == null || string.IsNullOrWhiteSpace(existingExpId.ToString())))
        {
            var expId = await CreateNonCashWriteOffExpenseAsync(
                disposalNumber,
                disposalDate,
                totalCostValue,
                cause,
                disposalMethod,
                witnessName,
                notes,
                userId);
            if (expId.HasValue)
            {
                await _db.UpdateAsync("stock_disposals", disposalId, new Dictionary<string, object?>
                {
                    ["expense_id"] = expId.Value
                });
            }
        }

        SyncPulseService.Bump("stock");
        return (await GetDisposalByIdAsync(disposalId))!;
    }

    public async Task<Dictionary<string, object?>> RestoreFromQuarantineAsync(long disposalId, string? reason, long? userId)
    {
        var hasBatchesTable = await _db.HasTableAsync("batches");
        var hasExpensesTable = await _db.HasTableAsync("expenses");
        var stocktakeSvc = new StocktakeService(_db);

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            var disposal = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id = disposalId }, conn, tx);
            if (disposal == null) throw new KeyNotFoundException("Stock disposal not found.");

            var currentStatus = disposal.GetValueOrDefault("status")?.ToString() ?? "";
            if (currentStatus is not (StatusQuarantined or StatusDisposed))
            {
                throw new StockDisposalValidationException(
                    "status",
                    $"Only quarantined or disposed vouchers can be restored to stock. Current status: {currentStatus}.");
            }

            var items = await _db.QueryListAsync(
                "SELECT * FROM stock_disposal_items WHERE stock_disposal_id = @disposalId ORDER BY id ASC",
                new { disposalId }, conn, tx);

            foreach (var item in items)
            {
                var productId = Convert.ToInt64(item["product_id"]);
                var disposeQty = ToDecimal(item.GetValueOrDefault("dispose_qty"));
                var product = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
                if (product == null) continue;

                Dictionary<string, object?>? batch = null;
                if (hasBatchesTable)
                {
                    if (item.TryGetValue("batch_id", out var bidObj) && bidObj != null && long.TryParse(bidObj.ToString(), out var batchId))
                    {
                        batch = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM batches WHERE id = @batchId AND product_id = @productId",
                            new { batchId, productId }, conn, tx);
                    }
                    else if (item.TryGetValue("batch_number", out var bnObj) && !string.IsNullOrWhiteSpace(bnObj?.ToString()))
                    {
                        batch = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @bn LIMIT 1",
                            new { productId, bn = bnObj.ToString()!.Trim() }, conn, tx);
                    }
                }

                if (batch != null)
                {
                    var newBatchQty = ToDecimal(batch.GetValueOrDefault("quantity")) + disposeQty;
                    await _db.UpdateAsync("batches", Convert.ToInt64(batch["id"]), new Dictionary<string, object?>
                    {
                        ["quantity"] = newBatchQty
                    }, "id", conn, tx);
                }

                var newProdQty = StocktakeService.GetProductQty(product) + disposeQty;
                await stocktakeSvc.UpdateProductQtyInDbAsync(product, newProdQty, conn, tx);
            }

            var updatedNotes = disposal.GetValueOrDefault("notes")?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(reason))
            {
                updatedNotes = string.IsNullOrWhiteSpace(updatedNotes)
                    ? $"[Restored to Shelf: {reason.Trim()}]"
                    : $"{updatedNotes}\n[Restored to Shelf: {reason.Trim()}]";
            }

            await _db.UpdateAsync("stock_disposals", disposalId, new Dictionary<string, object?>
            {
                ["status"] = StatusRestored,
                ["stock_adjustment_id"] = null,
                ["expense_id"] = null,
                ["restored_by"] = userId,
                ["restored_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["notes"] = string.IsNullOrWhiteSpace(updatedNotes) ? null : updatedNotes
            }, "id", conn, tx);

            if (disposal.TryGetValue("stock_adjustment_id", out var adjObj) && adjObj != null && long.TryParse(adjObj.ToString(), out var adjId))
            {
                await conn.ExecuteAsync("DELETE FROM stock_adjustment_items WHERE stock_adjustment_id = @adjId", new { adjId }, tx);
                await conn.ExecuteAsync("DELETE FROM stock_adjustments WHERE id = @adjId", new { adjId }, tx);
            }

            if (disposal.TryGetValue("expense_id", out var expObj) && expObj != null && long.TryParse(expObj.ToString(), out var expId) && hasExpensesTable)
            {
                await conn.ExecuteAsync("DELETE FROM expenses WHERE id = @expId", new { expId }, tx);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        SyncPulseService.Bump("stock");
        return (await GetDisposalByIdAsync(disposalId))!;
    }

    private static async Task<string> AllocateDisposalNumberInTxAsync(SqliteConnection conn, SqliteTransaction tx, string disposalDate)
    {
        var dt = DateTime.TryParse(disposalDate, out var parsed) ? parsed : DateTime.UtcNow;
        var ym = dt.ToString("yyyyMM");
        var prefix = $"DSP-{ym}-";
        var sequenceKey = $"stock_disposal:{ym}";
        const int padding = 4;

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM document_sequences WHERE sequence_key = @key",
            new { key = sequenceKey },
            tx);

        long existingMax = 0;
        var values = await conn.QueryAsync<string?>(
            "SELECT disposal_number FROM stock_disposals WHERE disposal_number LIKE @like ORDER BY id DESC LIMIT 25",
            new { like = prefix + "%" },
            tx);
        var regex = new Regex("^" + Regex.Escape(prefix) + @"(\d+)$", RegexOptions.IgnoreCase);
        foreach (var val in values)
        {
            if (string.IsNullOrEmpty(val)) continue;
            var m = regex.Match(val);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var num) && num > existingMax)
            {
                existingMax = num;
            }
        }

        if (row == null)
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await conn.ExecuteAsync(@"
                INSERT OR IGNORE INTO document_sequences (sequence_key, prefix, padding, last_number, created_at, updated_at)
                VALUES (@key, @prefix, @padding, @lastNum, @now, @now)",
                new { key = sequenceKey, prefix, padding, lastNum = existingMax, now = nowStr },
                tx);

            row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM document_sequences WHERE sequence_key = @key",
                new { key = sequenceKey },
                tx);
        }

        long storedLast = row != null ? Convert.ToInt64(row.last_number ?? 0) : 0;
        long nextNumber = Math.Max(storedLast, existingMax) + 1;

        while (true)
        {
            var candidate = prefix + nextNumber.ToString().PadLeft(padding, '0');
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM stock_disposals WHERE disposal_number = @cand",
                new { cand = candidate },
                tx);
            if (exists == 0) break;
            nextNumber++;
        }

        await conn.ExecuteAsync(@"
            UPDATE document_sequences
            SET last_number = @nextNum, prefix = @prefix, padding = @padding, updated_at = @now
            WHERE sequence_key = @key",
            new
            {
                nextNum = nextNumber,
                prefix,
                padding,
                now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                key = sequenceKey
            },
            tx);

        return prefix + nextNumber.ToString().PadLeft(padding, '0');
    }

    public async Task DeleteDisposalAsync(long disposalId, string? reason, long? userId)
    {
        var disposal = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id = disposalId });
        if (disposal == null) throw new KeyNotFoundException("Stock disposal not found.");

        var currentStatus = disposal.GetValueOrDefault("status")?.ToString() ?? "";
        if (currentStatus is (StatusQuarantined or StatusDisposed))
        {
            await RestoreFromQuarantineAsync(
                disposalId,
                string.IsNullOrWhiteSpace(reason) ? "Voucher deleted & reversed" : reason,
                userId);
        }

        await _db.ExecuteAsync("DELETE FROM stock_disposal_items WHERE stock_disposal_id = @disposalId", new { disposalId });
        await _db.ExecuteAsync("DELETE FROM stock_disposals WHERE id = @disposalId", new { disposalId });
        SyncPulseService.Bump("stock");
    }

    public async Task<Dictionary<string, object?>?> GetDisposalByIdAsync(long id)
    {
        var row = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id });
        if (row == null) return null;

        await EnrichDisposalsBatchAsync(new List<Dictionary<string, object?>> { row }, true);
        return row;
    }

    public async Task<List<Dictionary<string, object?>>> EnrichDisposalsBatchAsync(
        List<Dictionary<string, object?>> disposals,
        bool includeStockAdjustment = false)
    {
        if (disposals.Count == 0) return disposals;

        var disposalIds = disposals.Select(d => Convert.ToInt64(d["id"])).Distinct().ToList();
        var userIds = new HashSet<long>();
        var expenseIds = new HashSet<long>();
        var adjIds = new HashSet<long>();

        foreach (var d in disposals)
        {
            foreach (var uKey in new[] { "created_by", "disposed_by", "restored_by" })
            {
                if (d.TryGetValue(uKey, out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid))
                    userIds.Add(uid);
            }
            if (d.TryGetValue("expense_id", out var eObj) && eObj != null && long.TryParse(eObj.ToString(), out var eid))
                expenseIds.Add(eid);
            if (d.TryGetValue("stock_adjustment_id", out var aObj) && aObj != null && long.TryParse(aObj.ToString(), out var aid))
                adjIds.Add(aid);
        }

        var usersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Count > 0 && await _db.HasTableAsync("users"))
        {
            var rows = await _db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = userIds.ToArray() });
            foreach (var r in rows) usersMap[Convert.ToInt64(r["id"])] = r;
        }

        var expensesMap = new Dictionary<long, Dictionary<string, object?>>();
        if (expenseIds.Count > 0 && await _db.HasTableAsync("expenses"))
        {
            var rows = await _db.QueryListAsync("SELECT id, expense_number, amount, date FROM expenses WHERE id IN @ids", new { ids = expenseIds.ToArray() });
            foreach (var r in rows) expensesMap[Convert.ToInt64(r["id"])] = r;
        }

        var adjMap = new Dictionary<long, Dictionary<string, object?>>();
        if (includeStockAdjustment && adjIds.Count > 0 && await _db.HasTableAsync("stock_adjustments"))
        {
            var rows = await _db.QueryListAsync("SELECT id, posted_number, posted_date, total_worth FROM stock_adjustments WHERE id IN @ids", new { ids = adjIds.ToArray() });
            foreach (var r in rows) adjMap[Convert.ToInt64(r["id"])] = r;
        }

        var allItems = await _db.QueryListAsync(
            "SELECT * FROM stock_disposal_items WHERE stock_disposal_id IN @ids ORDER BY id ASC",
            new { ids = disposalIds });

        var productIds = allItems
            .Select(i => i.TryGetValue("product_id", out var p) && p != null && long.TryParse(p.ToString(), out var pid) ? (long?)pid : null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        var productsMap = new Dictionary<long, Dictionary<string, object?>>();
        if (productIds.Count > 0 && await _db.HasTableAsync("products"))
        {
            foreach (var chunk in productIds.Chunk(400))
            {
                var pRows = await _db.QueryListAsync(
                    "SELECT id, name, product_code, barcode, narcotic, pack_size, unit_purchase_price, unit_sale_price FROM products WHERE id IN @ids",
                    new { ids = chunk.ToArray() });
                foreach (var p in pRows) productsMap[Convert.ToInt64(p["id"])] = p;
            }
        }

        foreach (var item in allItems)
        {
            if (item.TryGetValue("product_id", out var pObj) && pObj != null && long.TryParse(pObj.ToString(), out var pid) && productsMap.TryGetValue(pid, out var pDict))
                item["product"] = pDict;
            else
                item["product"] = null;
        }

        var itemsByDisposal = allItems
            .GroupBy(i => Convert.ToInt64(i["stock_disposal_id"]))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var d in disposals)
        {
            var did = Convert.ToInt64(d["id"]);
            var cause = d.GetValueOrDefault("cause")?.ToString();
            var method = d.GetValueOrDefault("disposal_method")?.ToString();

            d["cause_label"] = GetCauseLabel(cause);
            d["disposal_method_label"] = GetMethodLabel(method);

            var dItems = itemsByDisposal.TryGetValue(did, out var list) ? list : new List<Dictionary<string, object?>>();
            d["items"] = dItems;
            d["items_count"] = dItems.Count;

            d["creator"] = d.TryGetValue("created_by", out var cb) && cb != null && long.TryParse(cb.ToString(), out var cbId) && usersMap.TryGetValue(cbId, out var cbU) ? cbU : null;
            d["disposer"] = d.TryGetValue("disposed_by", out var db) && db != null && long.TryParse(db.ToString(), out var dbId) && usersMap.TryGetValue(dbId, out var dbU) ? dbU : null;
            d["restorer"] = d.TryGetValue("restored_by", out var rb) && rb != null && long.TryParse(rb.ToString(), out var rbId) && usersMap.TryGetValue(rbId, out var rbU) ? rbU : null;
            d["expense"] = d.TryGetValue("expense_id", out var eb) && eb != null && long.TryParse(eb.ToString(), out var ebId) && expensesMap.TryGetValue(ebId, out var ebE) ? ebE : null;

            if (includeStockAdjustment)
            {
                var adj = d.TryGetValue("stock_adjustment_id", out var ab) && ab != null && long.TryParse(ab.ToString(), out var abId) && adjMap.TryGetValue(abId, out var abA) ? abA : null;
                d["stock_adjustment"] = adj;
                d["stockAdjustment"] = adj;
            }
        }

        return disposals;
    }

    private async Task<long?> CreateNonCashWriteOffExpenseAsync(
        string disposalNumber,
        string disposalDate,
        decimal totalCostValue,
        string cause,
        string disposalMethod,
        string? witnessName,
        string? notes,
        long? userId)
    {
        if (!await _db.HasTableAsync("expenses") || !await _db.HasTableAsync("expense_categories"))
        {
            return null;
        }

        var cat = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM expense_categories WHERE code = 'stock_disposal_writeoff' OR name = 'Stock Expiry, Damage & Scrap Write-Off' LIMIT 1");
        long catId;
        if (cat != null)
        {
            catId = Convert.ToInt64(cat["id"]);
        }
        else
        {
            catId = await _db.InsertAsync("expense_categories", new Dictionary<string, object?>
            {
                ["code"] = "stock_disposal_writeoff",
                ["name"] = "Stock Expiry, Damage & Scrap Write-Off",
                ["description"] = "Non-cash P&L write-off for expired, broken, spoiled, or recalled inventory disposed from stock",
                ["is_active"] = 1
            });
        }

        var dt = DateTime.TryParse(disposalDate, out var parsed) ? parsed : DateTime.UtcNow;
        var dateStr = dt.ToString("yyyy-MM-dd");
        var ymd = dt.ToString("yyyyMMdd");

        var expNumber = await new DocumentSequenceService(_db).NextAsync(
            $"expense:{ymd}",
            $"EXP-{ymd}-",
            4,
            "expenses",
            "expense_number");

        var causeLabel = GetCauseLabel(cause);
        var methodLabel = GetMethodLabel(disposalMethod);
        var desc = $"Stock Disposal {disposalNumber} ({causeLabel} via {methodLabel})" +
                   (!string.IsNullOrWhiteSpace(notes) ? $" — {notes}" : "");

        return await _db.InsertAsync("expenses", new Dictionary<string, object?>
        {
            ["expense_number"] = expNumber,
            ["expense_category_id"] = catId,
            ["account_id"] = null,
            ["user_id"] = userId,
            ["cash_drawer_session_id"] = null,
            ["amount"] = Math.Round(totalCostValue, 2),
            ["date"] = dateStr,
            ["payment_method"] = "other",
            ["reference_number"] = disposalNumber,
            ["payee"] = !string.IsNullOrWhiteSpace(witnessName) ? witnessName : "Inventory Write-Off",
            ["description"] = desc,
            ["is_recurring"] = 0
        });
    }

    private static bool IsProductNarcotic(Dictionary<string, object?> product)
    {
        var raw = product.GetValueOrDefault("narcotic")?.ToString()?.Trim().ToLowerInvariant() ?? "no";
        return raw is "yes" or "1" or "true";
    }

    private static decimal ResolveProductUnitCost(Dictionary<string, object?> product)
    {
        var unitCost = ToDecimal(product.GetValueOrDefault("unit_purchase_price"));
        if (unitCost <= 0m)
        {
            unitCost = ToDecimal(product.GetValueOrDefault("avg_price"));
        }
        if (unitCost <= 0m)
        {
            var packPurchase = ToDecimal(product.GetValueOrDefault("pack_purchase_price"));
            if (packPurchase > 0m)
            {
                var packSize = Math.Max(1, ToInt(product.GetValueOrDefault("pack_size"), 1));
                unitCost = packPurchase / packSize;
            }
        }
        return Math.Round(Math.Max(0m, unitCost), 2);
    }

    private static List<Dictionary<string, object?>> ExtractItemsList(Dictionary<string, object?> data)
    {
        var list = new List<Dictionary<string, object?>>();
        if (!data.TryGetValue("items", out var rawItems) || rawItems == null) return list;

        if (rawItems is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in je.EnumerateArray())
            {
                if (elem.ValueKind == JsonValueKind.Object)
                {
                    var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in elem.EnumerateObject())
                    {
                        d[p.Name] = KarobarDb.PrepareDbValue(p.Value);
                    }
                    list.Add(d);
                }
            }
        }
        else if (rawItems is IEnumerable<Dictionary<string, object?>> dictEnum)
        {
            foreach (var d in dictEnum)
            {
                list.Add(new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase));
            }
        }
        else if (rawItems is IEnumerable<Dictionary<string, object?>> listDict)
        {
            list.AddRange(listDict);
        }

        return list;
    }

    private static decimal ToDecimal(object? val) => StocktakeService.ToDecimal(val);

    private static int ToInt(object? val, int defaultVal = 0)
    {
        if (val == null) return defaultVal;
        if (val is int i) return i;
        if (val is long l) return (int)l;
        if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        return defaultVal;
    }

    private static bool ParseFlexibleBool(object? val, bool defaultVal = true)
    {
        if (val == null) return defaultVal;
        if (val is bool b) return b;
        if (val is int i) return i != 0;
        if (val is long l) return l != 0;
        if (val is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => je.TryGetDecimal(out var d) ? d != 0m : defaultVal,
                JsonValueKind.String => ParseFlexibleBool(je.GetString(), defaultVal),
                JsonValueKind.Null or JsonValueKind.Undefined => defaultVal,
                _ => defaultVal
            };
        }
        if (val is string s)
        {
            var trimmed = s.Trim();
            if (trimmed.Length == 0) return defaultVal;
            if (trimmed == "1" || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed == "0" || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return defaultVal;
    }
}
