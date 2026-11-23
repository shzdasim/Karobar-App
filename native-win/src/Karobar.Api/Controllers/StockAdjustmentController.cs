using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace Karobar.Api.Controllers;

[Route("api/stock-adjustments")]
[Authorize]
public class StockAdjustmentController : ApiControllerBase
{
    public StockAdjustmentController(KarobarDb db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int perPage = 50)
    {
        if (!await CanAsync("stock-adjustment.view"))
        {
            return Forbidden();
        }

        var search = (q ?? "").Trim();
        var whereSql = "";
        object? param = null;

        if (!string.IsNullOrEmpty(search))
        {
            whereSql = "WHERE (posted_number LIKE @like OR note LIKE @like)";
            param = new { like = $"%{search}%" };
        }

        var selectSql = $"SELECT * FROM stock_adjustments {whereSql} ORDER BY id DESC";
        var countSql = $"SELECT COUNT(1) FROM stock_adjustments {whereSql}";

        var result = await Db.PaginateAsync(
            selectSql,
            countSql,
            param,
            page,
            perPage <= 0 ? 50 : perPage,
            rows => EnrichAdjustmentsBatchAsync(rows, false),
            maxPerPage: 2000);

        return Ok(result);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> NewCode()
    {
        if (!await CanAsync("stock-adjustment.create"))
        {
            return Forbidden();
        }

        var code = await new DocumentSequenceService(Db).PreviewAsync(
            "stock_adjustment",
            "STADJ-",
            5,
            "stock_adjustments",
            "posted_number");

        return Ok(new { posted_number = code });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("stock-adjustment.view"))
        {
            return Forbidden();
        }

        var adj = await GetAdjustmentByIdAsync(id);
        if (adj == null)
        {
            return NotFound(new { message = "Stock adjustment not found." });
        }

        return Ok(adj);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("stock-adjustment.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var postedNumber = (GetString(body, "posted_number") ?? "").Trim();
        var postedDate = (GetString(body, "posted_date") ?? "").Trim();
        var note = (GetString(body, "note") ?? "").Trim();

        if (string.IsNullOrEmpty(postedDate))
        {
            return Unprocessable("The posted date field is required.", new Dictionary<string, string[]>
            {
                ["posted_date"] = new[] { "The posted date field is required." }
            });
        }

        if (string.IsNullOrEmpty(note))
        {
            return Unprocessable("The note field is required.", new Dictionary<string, string[]>
            {
                ["note"] = new[] { "The note field is required." }
            });
        }

        if (!body.TryGetValue("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
        {
            return Unprocessable("At least one stock adjustment item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one stock adjustment item is required." }
            });
        }

        var itemInputs = new List<Dictionary<string, JsonElement>>();
        int idx = 0;
        foreach (var el in itemsEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var rowDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
            {
                rowDict[prop.Name] = prop.Value;
            }

            var pid = GetNullableLong(rowDict, "product_id");
            if (pid == null || pid <= 0)
            {
                return Unprocessable("Product is required for each adjustment item.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.product_id"] = new[] { "The product_id field is required." }
                });
            }

            if (!rowDict.ContainsKey("actual_qty"))
            {
                return Unprocessable("Actual quantity is required for each adjustment item.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.actual_qty"] = new[] { "The actual_qty field is required." }
                });
            }

            var actualQty = GetDecimal(rowDict, "actual_qty", -1m);
            if (actualQty < 0m)
            {
                return Unprocessable("Actual quantity must be at least 0.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.actual_qty"] = new[] { "The actual_qty must be at least 0." }
                });
            }

            itemInputs.Add(rowDict);
            idx++;
        }

        if (itemInputs.Count == 0)
        {
            return Unprocessable("At least one stock adjustment item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one stock adjustment item is required." }
            });
        }

        await new DocumentSequenceService(Db).EnsureTableExistsAsync();
        var hasBatchesTable = await Db.HasTableAsync("batches");
        var stocktakeSvc = new StocktakeService(Db);

        long adjId;
        decimal totalWorth = 0m;
        decimal netSignedValue = 0m;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            if (!string.IsNullOrEmpty(postedNumber) && !postedNumber.StartsWith("STADJ-", StringComparison.OrdinalIgnoreCase))
            {
                var dup = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM stock_adjustments WHERE posted_number = @postedNumber",
                    new { postedNumber },
                    tx);
                if (dup > 0)
                {
                    await tx.RollbackAsync();
                    return Unprocessable("The posted number has already been taken.", new Dictionary<string, string[]>
                    {
                        ["posted_number"] = new[] { "The posted number has already been taken." }
                    });
                }
            }
            else if (string.IsNullOrEmpty(postedNumber) ||
                     (postedNumber.StartsWith("STADJ-", StringComparison.OrdinalIgnoreCase) &&
                      await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM stock_adjustments WHERE posted_number = @postedNumber", new { postedNumber }, tx) > 0))
            {
                postedNumber = await AllocateAdjustmentNumberInTxAsync(conn, tx);
            }

            adjId = await Db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
            {
                ["posted_number"] = postedNumber,
                ["posted_date"] = postedDate,
                ["note"] = note,
                ["total_worth"] = 0m,
                ["user_id"] = CurrentUserId
            }, conn, tx);

            for (int i = 0; i < itemInputs.Count; i++)
            {
                var row = itemInputs[i];
                var productId = GetNullableLong(row, "product_id")!.Value;
                var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
                if (product == null)
                {
                    await tx.RollbackAsync();
                    return Unprocessable($"Product #{productId} not found.", new Dictionary<string, string[]>
                    {
                        [$"items.{i}.product_id"] = new[] { "Selected product does not exist." }
                    });
                }

                var batchNumber = (GetString(row, "batch_number") ?? "").Trim();
                var expiry = GetString(row, "expiry");
                var packSize = row.ContainsKey("pack_size") && row["pack_size"].ValueKind != JsonValueKind.Null
                    ? (object?)GetDecimal(row, "pack_size")
                    : null;
                var actual = GetDecimal(row, "actual_qty");
                var unitCost = row.ContainsKey("unit_purchase_price") && row["unit_purchase_price"].ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(GetString(row, "unit_purchase_price"))
                    ? GetDecimal(row, "unit_purchase_price")
                    : StocktakeService.ToDecimal(product.GetValueOrDefault("unit_purchase_price"));

                if (!string.IsNullOrEmpty(batchNumber) && hasBatchesTable)
                {
                    var batch = await Db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @batchNumber LIMIT 1",
                        new { productId, batchNumber }, conn, tx);

                    decimal previous = batch != null ? StocktakeService.ToDecimal(batch.GetValueOrDefault("quantity")) : 0m;
                    decimal diff = actual - previous;
                    decimal worth = Math.Abs(diff) * unitCost;

                    await Db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
                    {
                        ["stock_adjustment_id"] = adjId,
                        ["product_id"] = productId,
                        ["batch_number"] = batchNumber,
                        ["expiry"] = expiry,
                        ["pack_size"] = packSize,
                        ["previous_qty"] = previous,
                        ["actual_qty"] = actual,
                        ["diff_qty"] = diff,
                        ["unit_purchase_price"] = unitCost,
                        ["worth_adjusted"] = worth
                    }, conn, tx);

                    totalWorth += worth;
                    netSignedValue += (diff * unitCost);

                    if (batch != null)
                    {
                        await Db.UpdateAsync("batches", Convert.ToInt64(batch["id"]), new Dictionary<string, object?>
                        {
                            ["quantity"] = actual
                        }, "id", conn, tx);
                    }
                    else
                    {
                        await Db.InsertAsync("batches", new Dictionary<string, object?>
                        {
                            ["product_id"] = productId,
                            ["batch_number"] = batchNumber,
                            ["expiry_date"] = string.IsNullOrWhiteSpace(expiry)
                                ? DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd")
                                : expiry.Trim(),
                            ["quantity"] = actual
                        }, conn, tx);
                    }

                    var newProdQty = Math.Max(0m, StocktakeService.GetProductQty(product) + diff);
                    await stocktakeSvc.UpdateProductQtyInDbAsync(product, newProdQty, conn, tx);
                }
                else
                {
                    decimal previous = StocktakeService.GetProductQty(product);
                    decimal diff = actual - previous;
                    decimal worth = Math.Abs(diff) * unitCost;

                    await Db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
                    {
                        ["stock_adjustment_id"] = adjId,
                        ["product_id"] = productId,
                        ["previous_qty"] = previous,
                        ["actual_qty"] = actual,
                        ["diff_qty"] = diff,
                        ["unit_purchase_price"] = unitCost,
                        ["worth_adjusted"] = worth
                    }, conn, tx);

                    totalWorth += worth;
                    netSignedValue += (diff * unitCost);

                    await stocktakeSvc.UpdateProductQtyInDbAsync(product, actual, conn, tx);
                }
            }

            await Db.UpdateAsync("stock_adjustments", adjId, new Dictionary<string, object?>
            {
                ["total_worth"] = totalWorth
            }, "id", conn, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await SyncShrinkageExpenseAsync(postedNumber, postedDate, note, netSignedValue, CurrentUserId);

        var created = await GetAdjustmentByIdAsync(adjId);
        if (created != null)
        {
            await new DocumentAuditService(Db).RecordCreateAsync("stock_adjustment", adjId, created, CurrentUserId, note);
        }

        SyncPulseService.Bump("stock");
        return Ok(created);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("stock-adjustment.delete"))
        {
            return Forbidden();
        }

        var hasDisposalsTable = await Db.HasTableAsync("stock_disposals");
        var hasStocktakeTable = await Db.HasTableAsync("stocktake_sessions");
        var hasExpensesTable = await Db.HasTableAsync("expenses");
        var hasBatchesTable = await Db.HasTableAsync("batches");
        var stocktakeSvc = new StocktakeService(Db);

        Dictionary<string, object?> adjSnapshot;
        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            var adj = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stock_adjustments WHERE id = @id", new { id }, conn, tx);
            if (adj == null)
            {
                await tx.RollbackAsync();
                return NotFound(new { message = "Stock adjustment not found." });
            }

            if (hasDisposalsTable)
            {
                var linkedDisp = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM stock_disposals WHERE stock_adjustment_id = @id",
                    new { id },
                    tx);
                if (linkedDisp > 0)
                {
                    await tx.RollbackAsync();
                    return Unprocessable("Cannot delete a stock adjustment that is linked to a stock disposal voucher.");
                }
            }

            if (hasStocktakeTable)
            {
                var linkedSt = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM stocktake_sessions WHERE stock_adjustment_id = @id",
                    new { id },
                    tx);
                if (linkedSt > 0)
                {
                    await tx.RollbackAsync();
                    return Unprocessable("Cannot delete a stock adjustment that is linked to a finalized stocktake session.");
                }
            }

            var items = await Db.QueryListAsync(
                "SELECT * FROM stock_adjustment_items WHERE stock_adjustment_id = @id ORDER BY id ASC",
                new { id },
                conn,
                tx);

            adjSnapshot = new Dictionary<string, object?>(adj, StringComparer.OrdinalIgnoreCase)
            {
                ["items"] = items
            };

            var postedNumber = adj.GetValueOrDefault("posted_number")?.ToString();
            if (!string.IsNullOrEmpty(postedNumber) && hasExpensesTable)
            {
                await conn.ExecuteAsync("DELETE FROM expenses WHERE reference_number = @postedNumber", new { postedNumber }, tx);
            }

            foreach (var item in items)
            {
                var productId = Convert.ToInt64(item["product_id"]);
                var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
                if (product == null) continue;

                var diffQty = item.TryGetValue("diff_qty", out var dq) && dq != null
                    ? StocktakeService.ToDecimal(dq)
                    : (StocktakeService.ToDecimal(item.GetValueOrDefault("actual_qty")) - StocktakeService.ToDecimal(item.GetValueOrDefault("previous_qty")));

                var batchNumber = item.GetValueOrDefault("batch_number")?.ToString();
                if (!string.IsNullOrWhiteSpace(batchNumber) && hasBatchesTable)
                {
                    var batch = await Db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @batchNumber LIMIT 1",
                        new { productId, batchNumber }, conn, tx);
                    if (batch != null)
                    {
                        var newBatchQty = Math.Max(0m, StocktakeService.ToDecimal(batch.GetValueOrDefault("quantity")) - diffQty);
                        await Db.UpdateAsync("batches", Convert.ToInt64(batch["id"]), new Dictionary<string, object?>
                        {
                            ["quantity"] = newBatchQty
                        }, "id", conn, tx);
                    }
                }

                var newProdQty = Math.Max(0m, StocktakeService.GetProductQty(product) - diffQty);
                await stocktakeSvc.UpdateProductQtyInDbAsync(product, newProdQty, conn, tx);
            }

            await conn.ExecuteAsync("DELETE FROM stock_adjustment_items WHERE stock_adjustment_id = @id", new { id }, tx);
            await conn.ExecuteAsync("DELETE FROM stock_adjustments WHERE id = @id", new { id }, tx);

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        await new DocumentAuditService(Db).RecordReversalAsync(
            "stock_adjustment",
            id,
            adjSnapshot,
            CurrentUserId,
            "Stock adjustment deleted");

        SyncPulseService.Bump("stock");
        return NoContent();
    }

    private static async Task<string> AllocateAdjustmentNumberInTxAsync(SqliteConnection conn, SqliteTransaction tx)
    {
        const string sequenceKey = "stock_adjustment";
        const string prefix = "STADJ-";
        const int padding = 5;

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM document_sequences WHERE sequence_key = @key",
            new { key = sequenceKey },
            tx);

        long existingMax = 0;
        var values = await conn.QueryAsync<string?>(
            "SELECT posted_number FROM stock_adjustments WHERE posted_number LIKE @like ORDER BY id DESC LIMIT 25",
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
                "SELECT COUNT(1) FROM stock_adjustments WHERE posted_number = @cand",
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

    private async Task SyncShrinkageExpenseAsync(
        string postedNumber,
        string postedDate,
        string? note,
        decimal netSignedValue,
        long? userId)
    {
        if (!await Db.HasTableAsync("expenses") || !await Db.HasTableAsync("expense_categories"))
        {
            return;
        }

        if (postedNumber.StartsWith("DISP-", StringComparison.OrdinalIgnoreCase) ||
            postedNumber.StartsWith("DSP-", StringComparison.OrdinalIgnoreCase) ||
            postedNumber.StartsWith("STOCKTAKE-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM expenses WHERE reference_number = @postedNumber LIMIT 1",
            new { postedNumber });

        if (netSignedValue >= -0.005m)
        {
            if (existing != null)
            {
                await Db.ExecuteAsync("DELETE FROM expenses WHERE id = @id", new { id = Convert.ToInt64(existing["id"]) });
            }
            return;
        }

        var lossAmount = Math.Round(Math.Abs(netSignedValue), 2);
        var resolvedUserId = userId ?? await Db.ExecuteScalarAsync<long?>("SELECT id FROM users ORDER BY id ASC LIMIT 1");
        if (!resolvedUserId.HasValue) return;

        var cat = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM expense_categories WHERE code = 'stock_shrinkage_writeoff' OR name = 'Inventory Shrinkage & Count Variance Write-Off' LIMIT 1");
        long catId;
        if (cat != null)
        {
            catId = Convert.ToInt64(cat["id"]);
        }
        else
        {
            catId = await Db.InsertAsync("expense_categories", new Dictionary<string, object?>
            {
                ["code"] = "stock_shrinkage_writeoff",
                ["name"] = "Inventory Shrinkage & Count Variance Write-Off",
                ["description"] = "Non-cash P&L write-off for inventory count shortages and stock adjustments",
                ["is_active"] = 1
            });
        }

        var dt = DateTime.TryParse(postedDate, out var parsed) ? parsed : DateTime.UtcNow;
        var dateStr = dt.ToString("yyyy-MM-dd");
        var ymd = dt.ToString("yyyyMMdd");
        var description = $"Stock Adjustment {postedNumber}" + (!string.IsNullOrWhiteSpace(note) ? $" — {note}" : "");

        if (existing != null)
        {
            await Db.UpdateAsync("expenses", Convert.ToInt64(existing["id"]), new Dictionary<string, object?>
            {
                ["expense_category_id"] = catId,
                ["amount"] = lossAmount,
                ["date"] = dateStr,
                ["reference_number"] = postedNumber,
                ["description"] = description,
                ["deleted_at"] = null
            });
        }
        else
        {
            var expNumber = await new DocumentSequenceService(Db).NextAsync(
                $"expense:{ymd}",
                $"EXP-{ymd}-",
                4,
                "expenses",
                "expense_number");

            await Db.InsertAsync("expenses", new Dictionary<string, object?>
            {
                ["expense_number"] = expNumber,
                ["expense_category_id"] = catId,
                ["account_id"] = null,
                ["user_id"] = resolvedUserId.Value,
                ["cash_drawer_session_id"] = null,
                ["amount"] = lossAmount,
                ["date"] = dateStr,
                ["payment_method"] = "other",
                ["reference_number"] = postedNumber,
                ["payee"] = "Inventory Shrinkage Write-Off",
                ["description"] = description,
                ["is_recurring"] = 0
            });
        }
    }

    private async Task<Dictionary<string, object?>?> GetAdjustmentByIdAsync(long id)
    {
        var adj = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stock_adjustments WHERE id = @id", new { id });
        if (adj == null) return null;

        await EnrichAdjustmentsBatchAsync(new List<Dictionary<string, object?>> { adj }, true);
        return adj;
    }

    private async Task<List<Dictionary<string, object?>>> EnrichAdjustmentsBatchAsync(
        List<Dictionary<string, object?>> rows,
        bool includeItems)
    {
        if (rows.Count == 0) return rows;

        var adjIds = rows.Select(r => Convert.ToInt64(r["id"])).Distinct().ToList();
        var postedNumbers = rows
            .Select(r => r.GetValueOrDefault("posted_number")?.ToString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct()
            .ToList();

        var userIds = rows
            .Select(r => r.TryGetValue("user_id", out var u) && u != null && long.TryParse(u.ToString(), out var uid) ? (long?)uid : null)
            .Where(u => u.HasValue)
            .Select(u => u!.Value)
            .Distinct()
            .ToList();

        var usersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Count > 0 && await Db.HasTableAsync("users"))
        {
            var uRows = await Db.QueryListAsync("SELECT id, name, email FROM users WHERE id IN @ids", new { ids = userIds });
            foreach (var u in uRows) usersMap[Convert.ToInt64(u["id"])] = u;
        }

        var countRows = await Db.QueryListAsync(@"
            SELECT stock_adjustment_id, COUNT(1) as cnt
            FROM stock_adjustment_items
            WHERE stock_adjustment_id IN @ids
            GROUP BY stock_adjustment_id",
            new { ids = adjIds });
        var countsMap = countRows.ToDictionary(
            r => Convert.ToInt64(r["stock_adjustment_id"]),
            r => Convert.ToInt32(r.GetValueOrDefault("cnt") ?? 0));

        var stocktakeMap = new Dictionary<long, Dictionary<string, object?>>();
        if (await Db.HasTableAsync("stocktake_sessions"))
        {
            var stRows = await Db.QueryListAsync(
                "SELECT id, reference_no, session_type, status, completed_at, stock_adjustment_id FROM stocktake_sessions WHERE stock_adjustment_id IN @ids",
                new { ids = adjIds });
            foreach (var st in stRows)
            {
                if (st.TryGetValue("stock_adjustment_id", out var aid) && aid != null)
                {
                    stocktakeMap[Convert.ToInt64(aid)] = st;
                }
            }
        }

        var disposalMap = new Dictionary<long, Dictionary<string, object?>>();
        if (await Db.HasTableAsync("stock_disposals"))
        {
            var dRows = await Db.QueryListAsync(
                "SELECT id, disposal_number, status, cause, disposal_method, stock_adjustment_id FROM stock_disposals WHERE stock_adjustment_id IN @ids",
                new { ids = adjIds });
            foreach (var d in dRows)
            {
                if (d.TryGetValue("stock_adjustment_id", out var aid) && aid != null)
                {
                    disposalMap[Convert.ToInt64(aid)] = d;
                }
            }
        }

        var expenseMap = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        if (postedNumbers.Count > 0 && await Db.HasTableAsync("expenses"))
        {
            var eRows = await Db.QueryListAsync(
                "SELECT id, expense_number, reference_number, amount, date FROM expenses WHERE reference_number IN @refs AND (deleted_at IS NULL OR deleted_at = '')",
                new { refs = postedNumbers });
            foreach (var e in eRows)
            {
                var refNo = e.GetValueOrDefault("reference_number")?.ToString();
                if (!string.IsNullOrEmpty(refNo)) expenseMap[refNo] = e;
            }
        }

        foreach (var r in rows)
        {
            var id = Convert.ToInt64(r["id"]);
            var postedNo = r.GetValueOrDefault("posted_number")?.ToString() ?? "";

            r["items_count"] = countsMap.TryGetValue(id, out var cnt) ? cnt : 0;
            r["user"] = r.TryGetValue("user_id", out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid) && usersMap.TryGetValue(uid, out var uDict) ? uDict : null;

            var st = stocktakeMap.TryGetValue(id, out var stDict) ? stDict : null;
            r["stocktake_session"] = st;
            r["stocktakeSession"] = st;

            var disp = disposalMap.TryGetValue(id, out var dDict) ? dDict : null;
            r["stock_disposal"] = disp;
            r["stockDisposal"] = disp;

            r["expense"] = !string.IsNullOrEmpty(postedNo) && expenseMap.TryGetValue(postedNo, out var expDict) ? expDict : null;

            if (includeItems)
            {
                var items = await Db.QueryListAsync(
                    "SELECT * FROM stock_adjustment_items WHERE stock_adjustment_id = @id ORDER BY id ASC",
                    new { id });

                var productIds = items
                    .Select(i => i.TryGetValue("product_id", out var p) && p != null && long.TryParse(p.ToString(), out var pid) ? (long?)pid : null)
                    .Where(p => p.HasValue)
                    .Select(p => p!.Value)
                    .Distinct()
                    .ToList();

                var productsMap = new Dictionary<long, Dictionary<string, object?>>();
                if (productIds.Count > 0 && await Db.HasTableAsync("products"))
                {
                    foreach (var chunk in productIds.Chunk(400))
                    {
                        var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = chunk.ToArray() });
                        foreach (var p in pRows) productsMap[Convert.ToInt64(p["id"])] = p;
                    }
                }

                foreach (var item in items)
                {
                    item["product"] = item.TryGetValue("product_id", out var pObj) && pObj != null && long.TryParse(pObj.ToString(), out var pid) && productsMap.TryGetValue(pid, out var pDict) ? pDict : null;
                }

                r["items"] = items;
            }
        }

        return rows;
    }
}
