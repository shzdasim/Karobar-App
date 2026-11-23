using System.Data;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Karobar.Services;

public class StocktakeService
{
    private readonly KarobarDb _db;

    public StocktakeService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<string> GenerateReferenceNoAsync(bool preview = false)
    {
        var ym = DateTime.UtcNow.ToString("yyyyMM");
        var prefix = $"ST-{ym}-";
        var seq = new DocumentSequenceService(_db);

        if (preview)
        {
            return await seq.PreviewAsync($"stocktake:{ym}", prefix, 3, "stocktake_sessions", "reference_no");
        }

        return await seq.NextAsync($"stocktake:{ym}", prefix, 3, "stocktake_sessions", "reference_no");
    }

    public async Task<Dictionary<string, object?>> CreateSessionAsync(Dictionary<string, object?> data, long? userId)
    {
        var refNo = data.TryGetValue("reference_no", out var rn) && !string.IsNullOrWhiteSpace(rn?.ToString())
            ? rn.ToString()!.Trim()
            : await GenerateReferenceNoAsync(false);

        var exists = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM stocktake_sessions WHERE reference_no = @refNo",
            new { refNo });
        if (exists > 0)
        {
            refNo = await GenerateReferenceNoAsync(false);
        }

        var sessionType = data.TryGetValue("session_type", out var st) && !string.IsNullOrWhiteSpace(st?.ToString())
            ? st.ToString()!.Trim()
            : "full";

        var isBlind = true;
        if (data.TryGetValue("is_blind", out var ib) && ib != null)
        {
            isBlind = ib is true or 1 or 1L || (ib is string ibs && (ibs == "1" || ibs.Equals("true", StringComparison.OrdinalIgnoreCase)));
        }

        var scopeType = data.TryGetValue("scope_type", out var sct) && !string.IsNullOrWhiteSpace(sct?.ToString())
            ? sct.ToString()!.Trim()
            : null;
        long? scopeId = null;
        if (data.TryGetValue("scope_id", out var sci) && sci != null && long.TryParse(sci.ToString(), out var parsedScopeId))
        {
            scopeId = parsedScopeId;
        }
        var notes = data.TryGetValue("notes", out var nt) ? nt?.ToString() : null;

        var id = await _db.InsertAsync("stocktake_sessions", new Dictionary<string, object?>
        {
            ["reference_no"] = refNo,
            ["session_type"] = sessionType,
            ["is_blind"] = isBlind ? 1 : 0,
            ["status"] = "draft",
            ["scope_type"] = scopeType,
            ["scope_id"] = scopeId,
            ["notes"] = notes,
            ["user_id"] = userId
        });

        SyncPulseService.Bump("stock");
        return (await GetSessionByIdAsync(id, false))!;
    }

    public async Task SnapshotItemsAsync(Dictionary<string, object?> session)
    {
        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            await SnapshotItemsCoreAsync(session, conn, tx);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task SnapshotItemsCoreAsync(Dictionary<string, object?> session, SqliteConnection conn, SqliteTransaction tx)
    {
        var sessionId = Convert.ToInt64(session["id"]);
        await _db.ExecuteAsync("DELETE FROM stocktake_items WHERE stocktake_session_id = @sessionId", new { sessionId }, conn, tx);

        var products = await ResolveProductsAsync(session, conn, tx);
        if (products.Count == 0) return;

        var sessionType = session.GetValueOrDefault("session_type")?.ToString() ?? "full";
        var scopeType = session.GetValueOrDefault("scope_type")?.ToString();
        long? filterLocationId = null;
        if (!string.Equals(sessionType, "full", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(scopeType, "storage_location", StringComparison.OrdinalIgnoreCase) &&
            session.TryGetValue("scope_id", out var sidObj) && sidObj != null && long.TryParse(sidObj.ToString(), out var sid))
        {
            filterLocationId = sid;
        }

        var productIds = products.Select(p => Convert.ToInt64(p["id"])).Distinct().ToList();
        var allBatches = new List<Dictionary<string, object?>>();

        if (await TableExistsAsync("batches", conn, tx))
        {
            var hasStorageLocCol = await ColumnExistsAsync("batches", "storage_location_id", conn, tx);
            foreach (var chunk in productIds.Chunk(400))
            {
                var sql = "SELECT * FROM batches WHERE product_id IN @ids";
                if (filterLocationId.HasValue && hasStorageLocCol)
                {
                    sql += " AND (storage_location_id = @locId OR storage_location_id IS NULL)";
                }
                var rows = await _db.QueryListAsync(sql, new { ids = chunk.ToArray(), locId = filterLocationId }, conn, tx);
                allBatches.AddRange(rows);
            }
        }

        var batchesByProduct = allBatches
            .GroupBy(b => Convert.ToInt64(b["product_id"]))
            .ToDictionary(g => g.Key, g => g.ToList());

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var product in products)
        {
            var pid = Convert.ToInt64(product["id"]);
            var prodQty = GetProductQty(product);
            var unitPurchasePrice = ToDecimal(product.GetValueOrDefault("unit_purchase_price"));

            if (batchesByProduct.TryGetValue(pid, out var pBatches) && pBatches.Count > 0)
            {
                decimal batchSum = 0m;
                foreach (var batch in pBatches)
                {
                    var bQty = ToDecimal(batch.GetValueOrDefault("quantity"));
                    batchSum += bQty;

                    await _db.InsertAsync("stocktake_items", new Dictionary<string, object?>
                    {
                        ["stocktake_session_id"] = sessionId,
                        ["product_id"] = pid,
                        ["batch_number"] = batch.GetValueOrDefault("batch_number")?.ToString(),
                        ["expiry_date"] = batch.GetValueOrDefault("expiry_date")?.ToString(),
                        ["system_qty"] = bQty,
                        ["counted_qty"] = null,
                        ["variance_qty"] = 0m,
                        ["variance_value"] = 0m,
                        ["unit_purchase_price"] = unitPurchasePrice,
                        ["status"] = "pending",
                        ["discrepancy_reason"] = null,
                        ["counted_by"] = null,
                        ["counted_at"] = null,
                        ["created_at"] = nowStr,
                        ["updated_at"] = nowStr
                    }, conn, tx);
                }

                var unbatched = prodQty - batchSum;
                if (unbatched > 0m)
                {
                    await _db.InsertAsync("stocktake_items", new Dictionary<string, object?>
                    {
                        ["stocktake_session_id"] = sessionId,
                        ["product_id"] = pid,
                        ["batch_number"] = null,
                        ["expiry_date"] = null,
                        ["system_qty"] = unbatched,
                        ["counted_qty"] = null,
                        ["variance_qty"] = 0m,
                        ["variance_value"] = 0m,
                        ["unit_purchase_price"] = unitPurchasePrice,
                        ["status"] = "pending",
                        ["discrepancy_reason"] = null,
                        ["counted_by"] = null,
                        ["counted_at"] = null,
                        ["created_at"] = nowStr,
                        ["updated_at"] = nowStr
                    }, conn, tx);
                }
            }
            else
            {
                await _db.InsertAsync("stocktake_items", new Dictionary<string, object?>
                {
                    ["stocktake_session_id"] = sessionId,
                    ["product_id"] = pid,
                    ["batch_number"] = null,
                    ["expiry_date"] = null,
                    ["system_qty"] = prodQty,
                    ["counted_qty"] = null,
                    ["variance_qty"] = 0m,
                    ["variance_value"] = 0m,
                    ["unit_purchase_price"] = unitPurchasePrice,
                    ["status"] = "pending",
                    ["discrepancy_reason"] = null,
                    ["counted_by"] = null,
                    ["counted_at"] = null,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, conn, tx);
            }
        }
    }

    public async Task<Dictionary<string, object?>> StartSessionAsync(long sessionId)
    {
        await _db.GetTableColumnsAsync("stocktake_items");
        await _db.GetTableColumnsAsync("stocktake_sessions");

        await using (var conn = await _db.OpenConnectionAsync())
        await using (var tx = await KarobarDb.BeginImmediateTransactionAsync(conn))
        {
            try
            {
                var session = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM stocktake_sessions WHERE id = @id",
                    new { id = sessionId },
                    conn,
                    tx);
                if (session == null) throw new KeyNotFoundException("Stocktake session not found.");

                var status = session.GetValueOrDefault("status")?.ToString() ?? "";
                if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Only draft sessions can be started. Current status: {status}");
                }

                await SnapshotItemsCoreAsync(session, conn, tx);

                await _db.UpdateAsync("stocktake_sessions", sessionId, new Dictionary<string, object?>
                {
                    ["status"] = "in_progress",
                    ["started_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                }, "id", conn, tx);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        SyncPulseService.Bump("stock");
        return (await GetSessionByIdAsync(sessionId, false))!;
    }

    public async Task<Dictionary<string, object?>> AddItemAsync(long sessionId, Dictionary<string, object?> data, long? userId = null)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = sessionId });
        if (session == null) throw new KeyNotFoundException("Stocktake session not found.");

        var status = session.GetValueOrDefault("status")?.ToString() ?? "";
        if (!string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Items can only be added to in-progress stocktake sessions.");
        }

        if (!data.TryGetValue("product_id", out var pidObj) || pidObj == null || !long.TryParse(pidObj.ToString(), out var productId))
        {
            throw new InvalidOperationException("Valid product_id is required.");
        }

        var product = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId });
        if (product == null) throw new InvalidOperationException("Product not found.");

        var batchNumber = data.TryGetValue("batch_number", out var bnObj) && !string.IsNullOrWhiteSpace(bnObj?.ToString())
            ? bnObj.ToString()!.Trim()
            : null;
        var expiryDate = data.TryGetValue("expiry_date", out var edObj) && !string.IsNullOrWhiteSpace(edObj?.ToString())
            ? edObj.ToString()!.Trim()
            : null;
        var discrepancyReason = data.TryGetValue("discrepancy_reason", out var drObj) && !string.IsNullOrWhiteSpace(drObj?.ToString())
            ? drObj.ToString()!.Trim()
            : null;

        bool hasCountedInput = data.TryGetValue("counted_qty", out var cqObj) && cqObj != null && !string.IsNullOrWhiteSpace(cqObj.ToString());
        decimal countedQtyVal = hasCountedInput ? ToDecimal(cqObj) : 0m;

        var existingSql = batchNumber != null
            ? "SELECT * FROM stocktake_items WHERE stocktake_session_id = @sessionId AND product_id = @productId AND batch_number = @batchNumber LIMIT 1"
            : "SELECT * FROM stocktake_items WHERE stocktake_session_id = @sessionId AND product_id = @productId AND (batch_number IS NULL OR batch_number = '') LIMIT 1";

        var existing = await _db.QuerySingleOrDefaultAsync(existingSql, new { sessionId, productId, batchNumber });
        if (existing != null)
        {
            var existingId = Convert.ToInt64(existing["id"]);
            if (hasCountedInput)
            {
                return await RecordCountAsync(
                    existingId,
                    countedQtyVal,
                    discrepancyReason ?? existing.GetValueOrDefault("discrepancy_reason")?.ToString(),
                    userId);
            }
            return (await GetItemWithRelationsAsync(existingId))!;
        }

        decimal systemQty;
        if (batchNumber != null && await _db.HasTableAsync("batches"))
        {
            var batch = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @batchNumber LIMIT 1",
                new { productId, batchNumber });
            systemQty = batch != null ? ToDecimal(batch.GetValueOrDefault("quantity")) : 0m;
            if (string.IsNullOrEmpty(expiryDate) && batch != null)
            {
                expiryDate = batch.GetValueOrDefault("expiry_date")?.ToString();
            }
        }
        else
        {
            systemQty = GetProductQty(product);
        }

        var unitPurchasePrice = ToDecimal(product.GetValueOrDefault("unit_purchase_price"));
        var itemId = await _db.InsertAsync("stocktake_items", new Dictionary<string, object?>
        {
            ["stocktake_session_id"] = sessionId,
            ["product_id"] = productId,
            ["batch_number"] = batchNumber,
            ["expiry_date"] = expiryDate,
            ["system_qty"] = systemQty,
            ["counted_qty"] = null,
            ["variance_qty"] = 0m,
            ["variance_value"] = 0m,
            ["unit_purchase_price"] = unitPurchasePrice,
            ["status"] = "pending",
            ["discrepancy_reason"] = discrepancyReason,
            ["counted_by"] = null,
            ["counted_at"] = null
        });

        if (hasCountedInput)
        {
            return await RecordCountAsync(
                itemId,
                countedQtyVal,
                discrepancyReason ?? "Unexpected stock found during count",
                userId);
        }

        return (await GetItemWithRelationsAsync(itemId))!;
    }

    public async Task<Dictionary<string, object?>> RecordCountAsync(long itemId, decimal countedQty, string? reason, long? userId)
    {
        var item = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_items WHERE id = @id", new { id = itemId });
        if (item == null) throw new KeyNotFoundException("Stocktake item not found.");

        var sessionId = Convert.ToInt64(item["stocktake_session_id"]);
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = sessionId });
        if (session != null)
        {
            var sStatus = session.GetValueOrDefault("status")?.ToString() ?? "";
            if (sStatus is not ("in_progress" or "pending_review"))
            {
                throw new InvalidOperationException($"Counts cannot be recorded for a session with status: {sStatus}");
            }
        }

        var systemQty = ToDecimal(item.GetValueOrDefault("system_qty"));
        var unitPrice = ToDecimal(item.GetValueOrDefault("unit_purchase_price"));
        var varianceQty = countedQty - systemQty;
        var varianceValue = Math.Abs(varianceQty) * unitPrice;

        await _db.UpdateAsync("stocktake_items", itemId, new Dictionary<string, object?>
        {
            ["counted_qty"] = countedQty,
            ["variance_qty"] = varianceQty,
            ["variance_value"] = varianceValue,
            ["status"] = "counted",
            ["discrepancy_reason"] = reason,
            ["counted_by"] = userId,
            ["counted_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return (await GetItemWithRelationsAsync(itemId))!;
    }

    public async Task<Dictionary<string, object?>> SubmitForReviewAsync(long sessionId)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = sessionId });
        if (session == null) throw new KeyNotFoundException("Stocktake session not found.");

        var status = session.GetValueOrDefault("status")?.ToString() ?? "";
        if (!string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only in-progress sessions can be submitted for review.");
        }

        var pendingCount = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM stocktake_items WHERE stocktake_session_id = @sessionId AND status = 'pending'",
            new { sessionId });
        if (pendingCount > 0)
        {
            throw new InvalidOperationException($"All items must be counted before submitting for review. {pendingCount} item(s) still pending.");
        }

        await _db.UpdateAsync("stocktake_sessions", sessionId, new Dictionary<string, object?>
        {
            ["status"] = "pending_review"
        });

        SyncPulseService.Bump("stock");
        return (await GetSessionByIdAsync(sessionId, false))!;
    }

    public async Task<Dictionary<string, object?>> FinalizeSessionAsync(long sessionId, long? userId)
    {
        await _db.GetTableColumnsAsync("stock_adjustments");
        await _db.GetTableColumnsAsync("stock_adjustment_items");
        await _db.GetTableColumnsAsync("stocktake_items");
        await _db.GetTableColumnsAsync("batches");
        await _db.GetTableColumnsAsync("products");
        await _db.GetTableColumnsAsync("stocktake_sessions");

        var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string referenceNo;
        string postedNumber;
        long adjId;
        decimal netSignedValue = 0m;

        await using (var conn = await _db.OpenConnectionAsync())
        await using (var tx = await KarobarDb.BeginImmediateTransactionAsync(conn))
        {
            try
            {
                var session = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM stocktake_sessions WHERE id = @id",
                    new { id = sessionId },
                    conn,
                    tx);
                if (session == null) throw new KeyNotFoundException("Stocktake session not found.");

                var status = session.GetValueOrDefault("status")?.ToString() ?? "";
                if (!string.Equals(status, "pending_review", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Only sessions in pending_review can be finalized.");
                }

                var pendingCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM stocktake_items WHERE stocktake_session_id = @sessionId AND status = 'pending'",
                    new { sessionId },
                    tx);
                if (pendingCount > 0)
                {
                    throw new InvalidOperationException($"All items must be counted before finalizing. {pendingCount} item(s) still pending.");
                }

                referenceNo = session.GetValueOrDefault("reference_no")?.ToString() ?? $"ST-{sessionId}";
                postedNumber = $"STOCKTAKE-{referenceNo}";

                adjId = await _db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
                {
                    ["posted_number"] = postedNumber,
                    ["posted_date"] = dateStr,
                    ["note"] = $"Stocktake finalization for session {referenceNo}",
                    ["total_worth"] = 0m,
                    ["user_id"] = userId
                }, conn, tx);

                var items = await _db.QueryListAsync(
                    "SELECT * FROM stocktake_items WHERE stocktake_session_id = @sessionId ORDER BY id ASC",
                    new { sessionId }, conn, tx);

                decimal totalWorth = 0m;
                var hasBatchesTable = await TableExistsAsync("batches", conn, tx);

                foreach (var item in items)
                {
                    var itemId = Convert.ToInt64(item["id"]);
                    var productId = Convert.ToInt64(item["product_id"]);
                    var product = await _db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
                    if (product == null) continue;

                    var batchNumber = item.GetValueOrDefault("batch_number")?.ToString();
                    bool hasBatch = !string.IsNullOrWhiteSpace(batchNumber);
                    Dictionary<string, object?>? batch = null;
                    decimal liveSystemQty;

                    if (hasBatch && hasBatchesTable)
                    {
                        batch = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM batches WHERE product_id = @productId AND batch_number = @batchNumber LIMIT 1",
                            new { productId, batchNumber }, conn, tx);
                        liveSystemQty = batch != null ? ToDecimal(batch.GetValueOrDefault("quantity")) : 0m;
                    }
                    else
                    {
                        decimal batchSum = 0m;
                        if (hasBatchesTable)
                        {
                            batchSum = await conn.ExecuteScalarAsync<decimal?>(
                                "SELECT COALESCE(SUM(COALESCE(quantity, 0)), 0) FROM batches WHERE product_id = @productId",
                                new { productId }, tx) ?? 0m;
                        }
                        bool hasBatchedSessionItem = items.Any(it =>
                            Convert.ToInt64(it["product_id"]) == productId &&
                            !string.IsNullOrWhiteSpace(it.GetValueOrDefault("batch_number")?.ToString()));

                        var prodQty = GetProductQty(product);
                        liveSystemQty = hasBatchedSessionItem ? Math.Max(0m, prodQty - batchSum) : prodQty;
                    }

                    var countedQty = item.TryGetValue("counted_qty", out var cq) && cq != null
                        ? ToDecimal(cq)
                        : liveSystemQty;
                    var liveVarianceQty = countedQty - liveSystemQty;

                    var itemSystemQty = ToDecimal(item.GetValueOrDefault("system_qty"));
                    var itemVarianceQty = ToDecimal(item.GetValueOrDefault("variance_qty"));
                    var unitCost = ToDecimal(item.GetValueOrDefault("unit_purchase_price"));
                    if (unitCost <= 0m) unitCost = ToDecimal(product.GetValueOrDefault("unit_purchase_price"));

                    if (Math.Abs(liveSystemQty - itemSystemQty) > 0.0001m || Math.Abs(liveVarianceQty - itemVarianceQty) > 0.0001m)
                    {
                        await _db.UpdateAsync("stocktake_items", itemId, new Dictionary<string, object?>
                        {
                            ["system_qty"] = liveSystemQty,
                            ["variance_qty"] = liveVarianceQty,
                            ["variance_value"] = Math.Abs(liveVarianceQty) * unitCost
                        }, "id", conn, tx);
                    }

                    if (liveVarianceQty == 0m) continue;

                    var worth = Math.Abs(liveVarianceQty) * unitCost;
                    totalWorth += worth;
                    netSignedValue += (liveVarianceQty * unitCost);

                    await _db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
                    {
                        ["stock_adjustment_id"] = adjId,
                        ["product_id"] = productId,
                        ["batch_number"] = hasBatch ? batchNumber : null,
                        ["expiry"] = item.GetValueOrDefault("expiry_date")?.ToString(),
                        ["pack_size"] = null,
                        ["previous_qty"] = liveSystemQty,
                        ["actual_qty"] = countedQty,
                        ["diff_qty"] = liveVarianceQty,
                        ["unit_purchase_price"] = unitCost,
                        ["worth_adjusted"] = worth
                    }, conn, tx);

                    if (hasBatch && hasBatchesTable)
                    {
                        if (batch != null)
                        {
                            var bUpdate = new Dictionary<string, object?>
                            {
                                ["quantity"] = Math.Max(0m, countedQty)
                            };
                            if (string.IsNullOrWhiteSpace(batch.GetValueOrDefault("expiry_date")?.ToString()) &&
                                !string.IsNullOrWhiteSpace(item.GetValueOrDefault("expiry_date")?.ToString()))
                            {
                                bUpdate["expiry_date"] = item.GetValueOrDefault("expiry_date")?.ToString();
                            }
                            await _db.UpdateAsync("batches", Convert.ToInt64(batch["id"]), bUpdate, "id", conn, tx);
                        }
                        else if (countedQty > 0m)
                        {
                            var itemExpiry = item.GetValueOrDefault("expiry_date")?.ToString()?.Trim();
                            await _db.InsertAsync("batches", new Dictionary<string, object?>
                            {
                                ["product_id"] = productId,
                                ["batch_number"] = batchNumber,
                                ["expiry_date"] = string.IsNullOrWhiteSpace(itemExpiry)
                                    ? DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd")
                                    : itemExpiry,
                                ["quantity"] = Math.Max(0m, countedQty)
                            }, conn, tx);
                        }

                        var newProdQty = Math.Max(0m, GetProductQty(product) + liveVarianceQty);
                        await UpdateProductQtyInDbAsync(product, newProdQty, conn, tx);
                    }
                    else
                    {
                        var newProdQty = Math.Max(0m, GetProductQty(product) + liveVarianceQty);
                        await UpdateProductQtyInDbAsync(product, newProdQty, conn, tx);
                    }
                }

                await _db.UpdateAsync("stock_adjustments", adjId, new Dictionary<string, object?>
                {
                    ["total_worth"] = totalWorth
                }, "id", conn, tx);

                await _db.UpdateAsync("stocktake_sessions", sessionId, new Dictionary<string, object?>
                {
                    ["status"] = "finalized",
                    ["completed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["stock_adjustment_id"] = adjId
                }, "id", conn, tx);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // Audit trail
        var adjSnapshot = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stock_adjustments WHERE id = @id", new { id = adjId });
        if (adjSnapshot != null)
        {
            await new DocumentAuditService(_db).RecordCreateAsync(
                "stock_adjustment",
                adjId,
                adjSnapshot,
                userId,
                $"Stocktake finalization for session {referenceNo}");
        }

        // Post non-cash P&L Shrinkage Write-Off expense if net variance value is negative
        if (netSignedValue < -0.005m && await _db.HasTableAsync("expenses") && await _db.HasTableAsync("expense_categories"))
        {
            var lossAmount = Math.Round(Math.Abs(netSignedValue), 2);
            var cat = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM expense_categories WHERE code = 'stock_shrinkage_writeoff' OR name = 'Inventory Shrinkage & Count Variance Write-Off' LIMIT 1");
            long catId;
            if (cat != null)
            {
                catId = Convert.ToInt64(cat["id"]);
            }
            else
            {
                catId = await _db.InsertAsync("expense_categories", new Dictionary<string, object?>
                {
                    ["code"] = "stock_shrinkage_writeoff",
                    ["name"] = "Inventory Shrinkage & Count Variance Write-Off",
                    ["description"] = "Non-cash P&L write-off for inventory count shortages and stock adjustments",
                    ["is_active"] = 1
                });
            }

            var ymd = DateTime.UtcNow.ToString("yyyyMMdd");
            var expNo = await new DocumentSequenceService(_db).NextAsync(
                $"expense:{ymd}",
                $"EXP-{ymd}-",
                4,
                "expenses",
                "expense_number");

            await _db.InsertAsync("expenses", new Dictionary<string, object?>
            {
                ["expense_number"] = expNo,
                ["expense_category_id"] = catId,
                ["account_id"] = null,
                ["user_id"] = userId,
                ["cash_drawer_session_id"] = null,
                ["amount"] = lossAmount,
                ["date"] = dateStr,
                ["payment_method"] = "other",
                ["reference_number"] = postedNumber,
                ["payee"] = "Stocktake Variance Write-Off",
                ["description"] = $"Stocktake {referenceNo} Shrinkage Write-Off ({postedNumber})",
                ["is_recurring"] = 0
            });
        }

        SyncPulseService.Bump("stock");
        return (await GetSessionByIdAsync(sessionId, false))!;
    }

    public async Task<Dictionary<string, object?>> CancelSessionAsync(long sessionId)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = sessionId });
        if (session == null) throw new KeyNotFoundException("Stocktake session not found.");

        var status = session.GetValueOrDefault("status")?.ToString() ?? "";
        if (status is not ("draft" or "in_progress"))
        {
            throw new InvalidOperationException($"Only draft or in-progress sessions can be cancelled. Current status: {status}");
        }

        await _db.UpdateAsync("stocktake_sessions", sessionId, new Dictionary<string, object?>
        {
            ["status"] = "cancelled"
        });

        SyncPulseService.Bump("stock");
        return (await GetSessionByIdAsync(sessionId, false))!;
    }

    public async Task<Dictionary<string, object?>?> GetSessionByIdAsync(long id, bool includeItems = true)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id });
        if (session == null) return null;

        await EnrichSessionsBatchAsync(new List<Dictionary<string, object?>> { session }, includeItems);
        return session;
    }

    public async Task<List<Dictionary<string, object?>>> EnrichSessionsBatchAsync(
        List<Dictionary<string, object?>> sessions,
        bool includeItems = false)
    {
        if (sessions.Count == 0) return sessions;

        var sessionIds = sessions.Select(s => Convert.ToInt64(s["id"])).Distinct().ToList();
        var userIds = sessions
            .Select(s => s.TryGetValue("user_id", out var u) && u != null && long.TryParse(u.ToString(), out var uid) ? (long?)uid : null)
            .Where(u => u.HasValue)
            .Select(u => u!.Value)
            .Distinct()
            .ToList();
        var adjIds = sessions
            .Select(s => s.TryGetValue("stock_adjustment_id", out var a) && a != null && long.TryParse(a.ToString(), out var aid) ? (long?)aid : null)
            .Where(a => a.HasValue)
            .Select(a => a!.Value)
            .Distinct()
            .ToList();

        var usersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Count > 0 && await _db.HasTableAsync("users"))
        {
            var uRows = await _db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = userIds });
            foreach (var u in uRows) usersMap[Convert.ToInt64(u["id"])] = u;
        }

        var adjMap = new Dictionary<long, Dictionary<string, object?>>();
        if (adjIds.Count > 0 && await _db.HasTableAsync("stock_adjustments"))
        {
            var aRows = await _db.QueryListAsync("SELECT id, posted_number FROM stock_adjustments WHERE id IN @ids", new { ids = adjIds });
            foreach (var a in aRows) adjMap[Convert.ToInt64(a["id"])] = a;
        }

        var countRows = await _db.QueryListAsync(@"
            SELECT stocktake_session_id,
                   COUNT(1) as items_count,
                   SUM(CASE WHEN status = 'counted' THEN 1 ELSE 0 END) as counted_items_count
            FROM stocktake_items
            WHERE stocktake_session_id IN @ids
            GROUP BY stocktake_session_id",
            new { ids = sessionIds });

        var countsMap = countRows.ToDictionary(
            r => Convert.ToInt64(r["stocktake_session_id"]),
            r => (
                total: Convert.ToInt32(r.GetValueOrDefault("items_count") ?? 0),
                counted: Convert.ToInt32(r.GetValueOrDefault("counted_items_count") ?? 0)
            ));

        // Batch-load scope_name lookups (category, brand, supplier, storage_location)
        var scopeIdsByType = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            var st = s.GetValueOrDefault("scope_type")?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(st) &&
                s.TryGetValue("scope_id", out var scObj) &&
                scObj != null &&
                long.TryParse(scObj.ToString(), out var scId) &&
                scId > 0)
            {
                if (!scopeIdsByType.TryGetValue(st, out var set))
                {
                    set = new HashSet<long>();
                    scopeIdsByType[st] = set;
                }
                set.Add(scId);
            }
        }

        var scopeNameMaps = new Dictionary<string, Dictionary<long, string>>(StringComparer.OrdinalIgnoreCase);
        var scopeTables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = "categories",
            ["brand"] = "brands",
            ["supplier"] = "suppliers",
            ["storage_location"] = "storage_locations"
        };
        foreach (var (sType, tableName) in scopeTables)
        {
            if (scopeIdsByType.TryGetValue(sType, out var ids) && ids.Count > 0 && await _db.HasTableAsync(tableName))
            {
                var rows = await _db.QueryListAsync($"SELECT id, name FROM {tableName} WHERE id IN @ids", new { ids = ids.ToList() });
                scopeNameMaps[sType] = rows.ToDictionary(
                    r => Convert.ToInt64(r["id"]),
                    r => r.GetValueOrDefault("name")?.ToString() ?? "");
            }
        }

        foreach (var s in sessions)
        {
            var sid = Convert.ToInt64(s["id"]);
            int totalItems = 0;
            int countedItems = 0;
            if (countsMap.TryGetValue(sid, out var c))
            {
                totalItems = c.total;
                countedItems = c.counted;
            }
            s["items_count"] = totalItems;
            s["counted_items_count"] = countedItems;
            s["progress"] = new Dictionary<string, object?>
            {
                ["counted"] = countedItems,
                ["total"] = totalItems
            };

            string? scopeName = null;
            var st = s.GetValueOrDefault("scope_type")?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(st) &&
                s.TryGetValue("scope_id", out var scObj) &&
                scObj != null &&
                long.TryParse(scObj.ToString(), out var scId) &&
                scopeNameMaps.TryGetValue(st, out var nameMap) &&
                nameMap.TryGetValue(scId, out var resolvedScopeName))
            {
                scopeName = resolvedScopeName;
            }
            s["scope_name"] = scopeName;

            if (s.TryGetValue("user_id", out var uObj) && uObj != null && long.TryParse(uObj.ToString(), out var uid) && usersMap.TryGetValue(uid, out var uDict))
            {
                s["user"] = uDict;
            }
            else
            {
                s["user"] = null;
            }

            if (s.TryGetValue("stock_adjustment_id", out var aObj) && aObj != null && long.TryParse(aObj.ToString(), out var aid) && adjMap.TryGetValue(aid, out var aDict))
            {
                s["stock_adjustment"] = aDict;
            }
            else
            {
                s["stock_adjustment"] = null;
            }

            if (includeItems)
            {
                var items = await _db.QueryListAsync(
                    "SELECT * FROM stocktake_items WHERE stocktake_session_id = @sid ORDER BY id ASC",
                    new { sid });
                s["items"] = await EnrichItemsBatchAsync(items);
            }
        }

        return sessions;
    }

    public async Task<Dictionary<string, object?>?> GetItemWithRelationsAsync(long itemId)
    {
        var item = await _db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_items WHERE id = @id", new { id = itemId });
        if (item == null) return null;
        await EnrichItemsBatchAsync(new List<Dictionary<string, object?>> { item });
        return item;
    }

    public async Task<List<Dictionary<string, object?>>> EnrichItemsBatchAsync(List<Dictionary<string, object?>> items)
    {
        if (items.Count == 0) return items;

        var productIds = items
            .Select(i => i.TryGetValue("product_id", out var p) && p != null && long.TryParse(p.ToString(), out var pid) ? (long?)pid : null)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        var counterIds = items
            .Select(i => i.TryGetValue("counted_by", out var c) && c != null && long.TryParse(c.ToString(), out var cid) ? (long?)cid : null)
            .Where(c => c.HasValue)
            .Select(c => c!.Value)
            .Distinct()
            .ToList();

        var productsMap = new Dictionary<long, Dictionary<string, object?>>();
        if (productIds.Count > 0 && await _db.HasTableAsync("products"))
        {
            foreach (var chunk in productIds.Chunk(400))
            {
                var pRows = await _db.QueryListAsync(
                    "SELECT id, name, product_code, barcode, rack, unit_purchase_price, avg_price, category_id, brand_id, supplier_id FROM products WHERE id IN @ids",
                    new { ids = chunk.ToArray() });
                foreach (var p in pRows) productsMap[Convert.ToInt64(p["id"])] = p;
            }

            var brandIds = productsMap.Values
                .Select(p => p.TryGetValue("brand_id", out var b) && b != null && long.TryParse(b.ToString(), out var bid) ? (long?)bid : null)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .Distinct()
                .ToList();

            var brandsMap = new Dictionary<long, Dictionary<string, object?>>();
            if (brandIds.Count > 0 && await _db.HasTableAsync("brands"))
            {
                var bRows = await _db.QueryListAsync("SELECT id, name FROM brands WHERE id IN @ids", new { ids = brandIds });
                foreach (var b in bRows) brandsMap[Convert.ToInt64(b["id"])] = b;
            }

            foreach (var p in productsMap.Values)
            {
                if (p.TryGetValue("brand_id", out var bObj) && bObj != null && long.TryParse(bObj.ToString(), out var bid) && brandsMap.TryGetValue(bid, out var bDict))
                {
                    p["brand"] = bDict;
                }
                else
                {
                    p["brand"] = null;
                }
            }
        }

        var countersMap = new Dictionary<long, Dictionary<string, object?>>();
        if (counterIds.Count > 0 && await _db.HasTableAsync("users"))
        {
            var uRows = await _db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = counterIds });
            foreach (var u in uRows) countersMap[Convert.ToInt64(u["id"])] = u;
        }

        foreach (var item in items)
        {
            if (item.TryGetValue("product_id", out var pObj) && pObj != null && long.TryParse(pObj.ToString(), out var pid) && productsMap.TryGetValue(pid, out var pDict))
            {
                item["product"] = pDict;
            }
            else
            {
                item["product"] = null;
            }

            if (item.TryGetValue("counted_by", out var cObj) && cObj != null && long.TryParse(cObj.ToString(), out var cid) && countersMap.TryGetValue(cid, out var cDict))
            {
                item["counter"] = cDict;
            }
            else
            {
                item["counter"] = null;
            }
        }

        return items;
    }

    private async Task<bool> TableExistsAsync(string tableName, IDbConnection? conn, IDbTransaction? tx)
    {
        if (conn != null)
        {
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = @name",
                new { name = tableName },
                tx);
            return count > 0;
        }
        return await _db.HasTableAsync(tableName);
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName, IDbConnection? conn, IDbTransaction? tx)
    {
        if (conn != null)
        {
            var escaped = tableName.Replace("\"", "\"\"");
            var rows = await conn.QueryAsync($"PRAGMA table_info(\"{escaped}\")", transaction: tx);
            foreach (var r in rows)
            {
                var dict = (IDictionary<string, object>)r;
                if (dict.TryGetValue("name", out var nameObj) &&
                    nameObj != null &&
                    string.Equals(nameObj.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        return await _db.HasColumnAsync(tableName, columnName);
    }

    private async Task<List<Dictionary<string, object?>>> ResolveProductsAsync(
        Dictionary<string, object?> session,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (!await TableExistsAsync("products", conn, tx)) return new List<Dictionary<string, object?>>();

        var sessionType = session.GetValueOrDefault("session_type")?.ToString() ?? "full";
        var scopeType = session.GetValueOrDefault("scope_type")?.ToString();
        long? scopeId = null;
        if (session.TryGetValue("scope_id", out var sidObj) && sidObj != null && long.TryParse(sidObj.ToString(), out var sid))
        {
            scopeId = sid;
        }

        var sql = "SELECT * FROM products";
        if (!string.Equals(sessionType, "full", StringComparison.OrdinalIgnoreCase) && scopeId.HasValue)
        {
            switch (scopeType?.ToLowerInvariant())
            {
                case "category":
                    sql += " WHERE category_id = @scopeId";
                    break;
                case "brand":
                    sql += " WHERE brand_id = @scopeId";
                    break;
                case "supplier":
                    sql += " WHERE supplier_id = @scopeId";
                    break;
                case "storage_location":
                    var hasProdLoc = await ColumnExistsAsync("products", "storage_location_id", conn, tx);
                    var hasBatches = await TableExistsAsync("batches", conn, tx) && await ColumnExistsAsync("batches", "storage_location_id", conn, tx);
                    if (hasProdLoc && hasBatches)
                    {
                        sql += " WHERE storage_location_id = @scopeId OR id IN (SELECT product_id FROM batches WHERE storage_location_id = @scopeId)";
                    }
                    else if (hasProdLoc)
                    {
                        sql += " WHERE storage_location_id = @scopeId";
                    }
                    else if (hasBatches)
                    {
                        sql += " WHERE id IN (SELECT product_id FROM batches WHERE storage_location_id = @scopeId)";
                    }
                    break;
            }
        }

        sql += " ORDER BY id ASC";
        return await _db.QueryListAsync(sql, new { scopeId }, conn, tx);
    }

    public static decimal GetProductQty(Dictionary<string, object?> p)
    {
        if (p.TryGetValue("available_units", out var au) && au != null) return ToDecimal(au);
        if (p.TryGetValue("quantity_on_hand", out var qoh) && qoh != null) return ToDecimal(qoh);
        if (p.TryGetValue("quantity", out var q) && q != null) return ToDecimal(q);
        return 0m;
    }

    public async Task UpdateProductQtyInDbAsync(
        Dictionary<string, object?> product,
        decimal qty,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var pid = Convert.ToInt64(product["id"]);
        var updates = new Dictionary<string, object?>();

        if (product.ContainsKey("available_units") && await _db.HasColumnAsync("products", "available_units"))
        {
            updates["available_units"] = qty;
        }
        else if (product.ContainsKey("quantity_on_hand") && await _db.HasColumnAsync("products", "quantity_on_hand"))
        {
            updates["quantity_on_hand"] = qty;
        }
        else
        {
            updates["quantity"] = qty;
        }

        if (qty <= 0m)
        {
            updates["avg_price"] = 0m;
            updates["margin"] = 0m;
            updates["whole_sale_margin"] = 0m;
        }

        await _db.UpdateAsync("products", pid, updates, "id", conn, tx);
    }

    public static decimal ToDecimal(object? val)
    {
        if (val == null) return 0m;
        if (val is decimal d) return d;
        if (val is double db) return (decimal)db;
        if (val is float f) return (decimal)f;
        if (val is long l) return l;
        if (val is int i) return i;
        if (decimal.TryParse(val.ToString(), out var parsed)) return parsed;
        return 0m;
    }
}
