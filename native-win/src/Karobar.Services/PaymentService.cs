using System.Globalization;
using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class PaymentService
{
    private readonly KarobarDb _db;
    private readonly DocumentSequenceService _sequenceService;
    private readonly DocumentAuditService _auditService;

    public PaymentService(KarobarDb db)
    {
        _db = db;
        _sequenceService = new DocumentSequenceService(db);
        _auditService = new DocumentAuditService(db);
    }

    public async Task<string> GeneratePaymentNumberAsync(string partyType)
    {
        var prefix = string.Equals(partyType, "customer", StringComparison.OrdinalIgnoreCase) ? "CR" : "SD";
        var year = DateTime.UtcNow.ToString("yyyy");
        var fullPrefix = $"{prefix}-{year}-";

        return await _sequenceService.NextAsync(
            $"payment:{prefix}:{year}",
            fullPrefix,
            5,
            "payments",
            "payment_number");
    }

    public async Task<Dictionary<string, object?>> RecordCustomerPaymentAsync(
        long customerId,
        Dictionary<string, object?> data,
        long? userId = null)
    {
        var customer = await _db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId })
                       ?? throw new KeyNotFoundException("Customer not found.");

        var idempotencyKey = data.GetValueOrDefault("idempotency_key")?.ToString();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
                new { key = idempotencyKey });
            if (existing != null)
            {
                return await EnrichPaymentAsync(existing);
            }
        }

        var amount = Math.Round(AccountService.ToDecimal(data.GetValueOrDefault("amount")), 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        }

        var allocationsInput = ExtractAllocationsList(data.GetValueOrDefault("allocations"));
        decimal totalAllocated = 0m;
        var allocationsToCreate = new List<(Dictionary<string, object?> Invoice, decimal Amount)>();

        foreach (var alloc in allocationsInput)
        {
            var allocAmount = Math.Round(AccountService.ToDecimal(alloc.GetValueOrDefault("amount")), 2);
            if (allocAmount <= 0m) continue;

            var invoiceId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            var invoice = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM sale_invoices WHERE id = @invoiceId AND customer_id = @customerId",
                new { invoiceId, customerId });

            if (invoice == null)
            {
                throw new InvalidOperationException($"Sale invoice #{invoiceId} not found for this customer.");
            }

            if (string.Equals(invoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invoice {invoice.GetValueOrDefault("posted_number")} is reversed and cannot receive payments.");
            }

            var invTotal = AccountService.ToDecimal(invoice.GetValueOrDefault("total"));
            var invReceived = AccountService.ToDecimal(invoice.GetValueOrDefault("total_receive") ?? invoice.GetValueOrDefault("total_recieve"));
            var remainingDue = Math.Max(invTotal - invReceived, 0m);

            if (allocAmount > remainingDue + 0.001m)
            {
                throw new InvalidOperationException($"Allocated amount ({allocAmount}) exceeds invoice {invoice.GetValueOrDefault("posted_number")} due balance ({remainingDue}).");
            }

            totalAllocated += allocAmount;
            allocationsToCreate.Add((invoice, allocAmount));
        }

        if (totalAllocated > amount + 0.001m)
        {
            throw new InvalidOperationException($"Total allocated ({totalAllocated}) exceeds payment amount ({amount}).");
        }

        var unapplied = Math.Round(Math.Max(amount - totalAllocated, 0m), 2);
        var paymentNumber = await GeneratePaymentNumberAsync("customer");
        var payMethod = data.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var isCheque = payMethod.Equals("cheque", StringComparison.OrdinalIgnoreCase);
        var chequeDate = data.GetValueOrDefault("cheque_date")?.ToString();
        string? chequeStatus = null;
        if (isCheque)
        {
            var rawStatus = data.GetValueOrDefault("cheque_status")?.ToString();
            if (!string.IsNullOrWhiteSpace(rawStatus))
            {
                chequeStatus = rawStatus;
            }
            else if (DateTime.TryParse(chequeDate, out var cDt) && cDt.Date > DateTime.UtcNow.Date)
            {
                chequeStatus = "pending";
            }
            else
            {
                chequeStatus = "cleared";
            }
        }

        var paymentDate = data.GetValueOrDefault("payment_date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var reference = data.GetValueOrDefault("reference")?.ToString() ?? data.GetValueOrDefault("cheque_number")?.ToString();
        var notes = data.GetValueOrDefault("notes")?.ToString();

        var createPayload = new Dictionary<string, object?>
        {
            ["payment_number"] = paymentNumber,
            ["party_type"] = "customer",
            ["party_id"] = customerId,
            ["payment_date"] = paymentDate,
            ["amount"] = amount,
            ["allocated_amount"] = totalAllocated,
            ["unapplied_amount"] = unapplied,
            ["payment_method"] = payMethod,
            ["reference"] = reference,
            ["status"] = "posted",
            ["notes"] = notes,
            ["idempotency_key"] = idempotencyKey,
            ["created_by_id"] = userId
        };

        if (isCheque && await _db.HasColumnAsync("payments", "cheque_status"))
        {
            createPayload["cheque_number"] = data.GetValueOrDefault("cheque_number")?.ToString() ?? reference;
            createPayload["bank_name"] = data.GetValueOrDefault("bank_name")?.ToString();
            createPayload["cheque_date"] = !string.IsNullOrWhiteSpace(chequeDate) ? chequeDate : paymentDate;
            createPayload["cheque_status"] = chequeStatus;
            createPayload["cleared_at"] = chequeStatus == "cleared" ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : null;
        }

        var paymentId = await _db.InsertAsync("payments", createPayload);

        foreach (var item in allocationsToCreate)
        {
            var invId = AccountService.ToLong(item.Invoice["id"]);
            await _db.InsertAsync("payment_allocations", new Dictionary<string, object?>
            {
                ["payment_id"] = paymentId,
                ["invoice_type"] = "sale_invoice",
                ["invoice_id"] = invId,
                ["amount"] = item.Amount
            });

            var curReceived = AccountService.ToDecimal(item.Invoice.GetValueOrDefault("total_receive"));
            await _db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
            {
                ["total_receive"] = Math.Round(curReceived + item.Amount, 2)
            });
        }

        if (await _db.HasTableAsync("customer_ledgers"))
        {
            var methodLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(payMethod.Replace('_', ' '));
            if (isCheque && !string.IsNullOrWhiteSpace(chequeStatus) && chequeStatus != "cleared")
            {
                methodLabel += $" ({chequeStatus.ToUpperInvariant()})";
            }
            var notesText = !string.IsNullOrWhiteSpace(notes) ? $": {notes}" : "";

            await _db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["entry_date"] = paymentDate,
                ["entry_type"] = "payment",
                ["is_manual"] = false,
                ["credited_amount"] = amount,
                ["payment_ref"] = paymentNumber + (!string.IsNullOrWhiteSpace(reference) ? $" ({reference})" : ""),
                ["payment_id"] = paymentId,
                ["description"] = $"Payment via {methodLabel}{notesText}",
                ["created_by"] = userId
            });
        }

        var paymentRow = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }) ?? createPayload;
        await _auditService.RecordCreateAsync("payment", paymentId, paymentRow, userId, $"Customer payment {paymentNumber} recorded");

        var accountService = new AccountService(_db);
        var reqAccountId = AccountService.ToLong(data.GetValueOrDefault("account_id"));
        var account = await accountService.ResolveAccountAsync(reqAccountId > 0 ? reqAccountId : null, payMethod);
        if (account != null)
        {
            var accId = AccountService.ToLong(account["id"]);
            await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?> { ["account_id"] = accId });

            if (!isCheque || chequeStatus == "cleared")
            {
                var custName = customer.GetValueOrDefault("name")?.ToString() ?? "Customer";
                await accountService.DepositAsync(
                    account,
                    amount,
                    "customer_payment",
                    paymentId,
                    $"Customer payment {paymentNumber} ({custName})",
                    userId);
            }
        }

        if (accountService.IsDefaultCashDrawerAccount(account, payMethod) && !isCheque)
        {
            var cashDrawerService = new CashDrawerService(_db);
            await cashDrawerService.RecordTransactionAsync(
                null,
                "customer_payment",
                amount,
                "payment",
                paymentId,
                $"Customer payment {paymentNumber}",
                userId);
        }

        var finalPayment = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
        return await EnrichPaymentAsync(finalPayment);
    }

    public async Task<Dictionary<string, object?>> RecordSupplierPaymentAsync(
        long supplierId,
        Dictionary<string, object?> data,
        long? userId = null)
    {
        var supplier = await _db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id = supplierId })
                       ?? throw new KeyNotFoundException("Supplier not found.");

        var idempotencyKey = data.GetValueOrDefault("idempotency_key")?.ToString();
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
                new { key = idempotencyKey });
            if (existing != null)
            {
                return await EnrichPaymentAsync(existing);
            }
        }

        var amount = Math.Round(AccountService.ToDecimal(data.GetValueOrDefault("amount")), 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Payment amount must be greater than zero.");
        }

        var allocationsInput = ExtractAllocationsList(data.GetValueOrDefault("allocations"));
        decimal totalAllocated = 0m;
        var allocationsToCreate = new List<(Dictionary<string, object?> Invoice, decimal Amount)>();

        foreach (var alloc in allocationsInput)
        {
            var allocAmount = Math.Round(AccountService.ToDecimal(alloc.GetValueOrDefault("amount")), 2);
            if (allocAmount <= 0m) continue;

            var invoiceId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            var invoice = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM purchase_invoices WHERE id = @invoiceId AND supplier_id = @supplierId",
                new { invoiceId, supplierId });

            if (invoice == null)
            {
                throw new InvalidOperationException($"Purchase invoice #{invoiceId} not found for this supplier.");
            }

            if (string.Equals(invoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invoice {invoice.GetValueOrDefault("posted_number")} is reversed and cannot receive payments.");
            }

            var invTotal = AccountService.ToDecimal(invoice.GetValueOrDefault("total_amount"));
            var invPaid = AccountService.ToDecimal(invoice.GetValueOrDefault("total_paid"));
            var remainingDue = Math.Max(invTotal - invPaid, 0m);

            if (allocAmount > remainingDue + 0.001m)
            {
                throw new InvalidOperationException($"Allocated amount ({allocAmount}) exceeds purchase invoice {invoice.GetValueOrDefault("posted_number")} payable ({remainingDue}).");
            }

            totalAllocated += allocAmount;
            allocationsToCreate.Add((invoice, allocAmount));
        }

        if (totalAllocated > amount + 0.001m)
        {
            throw new InvalidOperationException($"Total allocated ({totalAllocated}) exceeds payment amount ({amount}).");
        }

        var unapplied = Math.Round(Math.Max(amount - totalAllocated, 0m), 2);
        var paymentNumber = await GeneratePaymentNumberAsync("supplier");
        var payMethod = data.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var isCheque = payMethod.Equals("cheque", StringComparison.OrdinalIgnoreCase);
        var chequeDate = data.GetValueOrDefault("cheque_date")?.ToString();
        string? chequeStatus = null;
        if (isCheque)
        {
            var rawStatus = data.GetValueOrDefault("cheque_status")?.ToString();
            if (!string.IsNullOrWhiteSpace(rawStatus))
            {
                chequeStatus = rawStatus;
            }
            else if (DateTime.TryParse(chequeDate, out var cDt) && cDt.Date > DateTime.UtcNow.Date)
            {
                chequeStatus = "pending";
            }
            else
            {
                chequeStatus = "cleared";
            }
        }

        var whtPercentage = Math.Round(Math.Max(0m, AccountService.ToDecimal(data.GetValueOrDefault("wht_percentage"))), 2);
        var rawWhtAmount = AccountService.ToDecimal(data.GetValueOrDefault("wht_amount"));
        var whtAmount = rawWhtAmount > 0m
            ? Math.Round(rawWhtAmount, 2)
            : (whtPercentage > 0m ? Math.Round(amount * (whtPercentage / 100m), 2) : 0m);
        whtAmount = Math.Min(whtAmount, amount);
        var netCashDisbursed = Math.Max(0m, Math.Round(amount - whtAmount, 2));

        var paymentDate = data.GetValueOrDefault("payment_date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var reference = data.GetValueOrDefault("reference")?.ToString() ?? data.GetValueOrDefault("cheque_number")?.ToString();
        var notes = data.GetValueOrDefault("notes")?.ToString();

        var createPayload = new Dictionary<string, object?>
        {
            ["payment_number"] = paymentNumber,
            ["party_type"] = "supplier",
            ["party_id"] = supplierId,
            ["payment_date"] = paymentDate,
            ["amount"] = amount,
            ["allocated_amount"] = totalAllocated,
            ["unapplied_amount"] = unapplied,
            ["payment_method"] = payMethod,
            ["reference"] = reference,
            ["status"] = "posted",
            ["notes"] = notes,
            ["idempotency_key"] = idempotencyKey,
            ["created_by_id"] = userId
        };

        if (await _db.HasColumnAsync("payments", "wht_amount"))
        {
            var whtRateId = AccountService.ToLong(data.GetValueOrDefault("wht_tax_rate_id"));
            createPayload["wht_tax_rate_id"] = whtRateId > 0 ? whtRateId : null;
            createPayload["wht_percentage"] = whtPercentage;
            createPayload["wht_amount"] = whtAmount;
        }

        if (isCheque && await _db.HasColumnAsync("payments", "cheque_status"))
        {
            createPayload["cheque_number"] = data.GetValueOrDefault("cheque_number")?.ToString() ?? reference;
            createPayload["bank_name"] = data.GetValueOrDefault("bank_name")?.ToString();
            createPayload["cheque_date"] = !string.IsNullOrWhiteSpace(chequeDate) ? chequeDate : paymentDate;
            createPayload["cheque_status"] = chequeStatus;
            createPayload["cleared_at"] = chequeStatus == "cleared" ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : null;
        }

        var paymentId = await _db.InsertAsync("payments", createPayload);

        foreach (var item in allocationsToCreate)
        {
            var invId = AccountService.ToLong(item.Invoice["id"]);
            await _db.InsertAsync("payment_allocations", new Dictionary<string, object?>
            {
                ["payment_id"] = paymentId,
                ["invoice_type"] = "purchase_invoice",
                ["invoice_id"] = invId,
                ["amount"] = item.Amount
            });

            var curPaid = AccountService.ToDecimal(item.Invoice.GetValueOrDefault("total_paid"));
            await _db.UpdateAsync("purchase_invoices", invId, new Dictionary<string, object?>
            {
                ["total_paid"] = Math.Round(curPaid + item.Amount, 2)
            });
        }

        if (await _db.HasTableAsync("supplier_ledgers"))
        {
            var methodLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(payMethod.Replace('_', ' '));
            if (isCheque && !string.IsNullOrWhiteSpace(chequeStatus) && chequeStatus != "cleared")
            {
                methodLabel += $" ({chequeStatus.ToUpperInvariant()})";
            }
            var notesText = !string.IsNullOrWhiteSpace(notes) ? $": {notes}" : "";

            await _db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
            {
                ["supplier_id"] = supplierId,
                ["entry_date"] = paymentDate,
                ["entry_type"] = "payment",
                ["is_manual"] = false,
                ["debited_amount"] = amount,
                ["payment_ref"] = paymentNumber + (!string.IsNullOrWhiteSpace(reference) ? $" ({reference})" : ""),
                ["payment_id"] = paymentId,
                ["description"] = $"Payment via {methodLabel}{notesText}",
                ["created_by"] = userId
            });
        }

        var paymentRow = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }) ?? createPayload;
        await _auditService.RecordCreateAsync("payment", paymentId, paymentRow, userId, $"Supplier payment {paymentNumber} recorded");

        var accountService = new AccountService(_db);
        var reqAccountId = AccountService.ToLong(data.GetValueOrDefault("account_id"));
        var account = await accountService.ResolveAccountAsync(reqAccountId > 0 ? reqAccountId : null, payMethod);
        if (account != null)
        {
            var accId = AccountService.ToLong(account["id"]);
            await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?> { ["account_id"] = accId });

            if ((!isCheque || chequeStatus == "cleared") && netCashDisbursed > 0m)
            {
                var supName = supplier.GetValueOrDefault("name")?.ToString() ?? "Supplier";
                var whtNote = whtAmount > 0m ? $" [Net of WHT: {whtAmount}]" : "";
                await accountService.WithdrawAsync(
                    account,
                    netCashDisbursed,
                    "supplier_payment",
                    paymentId,
                    $"Supplier payment {paymentNumber} ({supName}){whtNote}",
                    userId);
            }
        }

        if (accountService.IsDefaultCashDrawerAccount(account, payMethod) && !isCheque && netCashDisbursed > 0m)
        {
            var whtNote = whtAmount > 0m ? $" [Net of WHT: {whtAmount}]" : "";
            var cashDrawerService = new CashDrawerService(_db);
            await cashDrawerService.RecordTransactionAsync(
                null,
                "supplier_payment",
                netCashDisbursed,
                "payment",
                paymentId,
                $"Supplier payment {paymentNumber}{whtNote}",
                userId);
        }

        var finalPayment = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
        return await EnrichPaymentAsync(finalPayment);
    }

    public async Task<Dictionary<string, object?>> ReversePaymentAsync(long paymentId, string reason, long? userId = null)
    {
        var payment = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId })
                      ?? throw new KeyNotFoundException("Payment not found.");

        if (string.Equals(payment.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This payment has already been reversed.");
        }

        var allocations = await _db.HasTableAsync("payment_allocations")
            ? await _db.QueryListAsync("SELECT * FROM payment_allocations WHERE payment_id = @paymentId", new { paymentId })
            : new List<Dictionary<string, object?>>();

        foreach (var alloc in allocations)
        {
            var invType = alloc.GetValueOrDefault("invoice_type")?.ToString() ?? "";
            var invId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            var allocAmt = AccountService.ToDecimal(alloc.GetValueOrDefault("amount"));

            if (invType.Equals("sale_invoice", StringComparison.OrdinalIgnoreCase))
            {
                var invoice = await _db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @invId", new { invId });
                if (invoice != null)
                {
                    var curReceived = AccountService.ToDecimal(invoice.GetValueOrDefault("total_receive"));
                    await _db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
                    {
                        ["total_receive"] = Math.Round(Math.Max(curReceived - allocAmt, 0m), 2)
                    });
                }
            }
            else if (invType.Equals("purchase_invoice", StringComparison.OrdinalIgnoreCase))
            {
                var invoice = await _db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @invId", new { invId });
                if (invoice != null)
                {
                    var curPaid = AccountService.ToDecimal(invoice.GetValueOrDefault("total_paid"));
                    await _db.UpdateAsync("purchase_invoices", invId, new Dictionary<string, object?>
                    {
                        ["total_paid"] = Math.Round(Math.Max(curPaid - allocAmt, 0m), 2)
                    });
                }
            }
        }

        var payMethod = payment.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var partyType = payment.GetValueOrDefault("party_type")?.ToString() ?? "customer";
        var isCreditNote = payMethod.Equals("credit_note", StringComparison.OrdinalIgnoreCase);

        if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase) && await _db.HasTableAsync("customer_ledgers"))
        {
            if (isCreditNote && await _db.HasColumnAsync("customer_ledgers", "sale_return_id"))
            {
                await _db.ExecuteAsync("DELETE FROM customer_ledgers WHERE payment_id = @paymentId AND sale_return_id IS NULL", new { paymentId });
            }
            else
            {
                await _db.ExecuteAsync("DELETE FROM customer_ledgers WHERE payment_id = @paymentId", new { paymentId });
            }
        }
        else if (partyType.Equals("supplier", StringComparison.OrdinalIgnoreCase) && await _db.HasTableAsync("supplier_ledgers"))
        {
            if (isCreditNote && await _db.HasColumnAsync("supplier_ledgers", "purchase_return_id"))
            {
                await _db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE payment_id = @paymentId AND purchase_return_id IS NULL", new { paymentId });
            }
            else
            {
                await _db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE payment_id = @paymentId", new { paymentId });
            }
        }

        await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?>
        {
            ["status"] = "reversed",
            ["reversed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["reversed_by_id"] = userId,
            ["reversal_reason"] = reason
        });

        var chequeStatus = payment.GetValueOrDefault("cheque_status")?.ToString() ?? "";
        var clearedAt = payment.GetValueOrDefault("cleared_at")?.ToString();
        var wasUnclearedCheque = payMethod.Equals("cheque", StringComparison.OrdinalIgnoreCase) &&
                                 (chequeStatus is "pending" or "deposited" or "bounced") &&
                                 string.IsNullOrWhiteSpace(clearedAt);

        if (!isCreditNote && !wasUnclearedCheque)
        {
            var accountService = new AccountService(_db);
            var accId = AccountService.ToLong(payment.GetValueOrDefault("account_id"));
            var account = await accountService.ResolveAccountAsync(accId > 0 ? accId : null, payMethod);
            var amt = AccountService.ToDecimal(payment.GetValueOrDefault("amount"));
            var payNo = payment.GetValueOrDefault("payment_number")?.ToString() ?? $"#{paymentId}";

            if (account != null && amt > 0m)
            {
                if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase))
                {
                    await accountService.WithdrawAsync(
                        account,
                        amt,
                        "payment_reversal",
                        paymentId,
                        $"Reversal of Customer Payment {payNo}: {reason}",
                        userId);
                }
                else
                {
                    await accountService.DepositAsync(
                        account,
                        amt,
                        "payment_reversal",
                        paymentId,
                        $"Reversal of Supplier Payment {payNo}: {reason}",
                        userId);
                }
            }

            if (accountService.IsDefaultCashDrawerAccount(account, payMethod))
            {
                var cashDrawerService = new CashDrawerService(_db);
                await cashDrawerService.HandlePaymentReversalAsync(payment, reason, userId);
            }
        }

        await _auditService.RecordReversalAsync("payment", paymentId, payment, userId, reason);

        var updated = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
        return await EnrichPaymentAsync(updated);
    }

    public async Task<Dictionary<string, object?>> UpdateChequeStatusAsync(
        long paymentId,
        string newStatus,
        long? accountId = null,
        string? reason = null,
        long? userId = null)
    {
        var locked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId })
                     ?? throw new KeyNotFoundException("Payment not found.");

        var payMethod = locked.GetValueOrDefault("payment_method")?.ToString() ?? "";
        if (!payMethod.Equals("cheque", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cheque lifecycle transitions are only valid for cheque payments.");
        }

        var status = locked.GetValueOrDefault("status")?.ToString() ?? "posted";
        if (status.Equals("reversed", StringComparison.OrdinalIgnoreCase) && !newStatus.Equals("bounced", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot update cheque status on a reversed payment.");
        }

        var normalizedStatus = newStatus.ToLowerInvariant();
        if (normalizedStatus is not ("deposited" or "cleared" or "bounced"))
        {
            throw new InvalidOperationException("Invalid cheque status. Allowed: deposited, cleared, bounced.");
        }

        var currentChequeStatus = (locked.GetValueOrDefault("cheque_status")?.ToString() ?? "").ToLowerInvariant();

        if (normalizedStatus == "deposited")
        {
            if (currentChequeStatus is "cleared" or "bounced")
            {
                throw new InvalidOperationException($"Cannot mark a {currentChequeStatus} cheque as deposited.");
            }
            var upd = new Dictionary<string, object?> { ["cheque_status"] = "deposited" };
            if (accountId.HasValue && accountId.Value > 0)
            {
                upd["account_id"] = accountId.Value;
            }
            await _db.UpdateAsync("payments", paymentId, upd);
            var depRow = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
            return await EnrichPaymentAsync(depRow);
        }

        if (normalizedStatus == "cleared")
        {
            if (currentChequeStatus == "bounced")
            {
                throw new InvalidOperationException("Cannot clear a bounced cheque.");
            }
            if (currentChequeStatus == "cleared" && !string.IsNullOrWhiteSpace(locked.GetValueOrDefault("cleared_at")?.ToString()))
            {
                return await EnrichPaymentAsync(locked);
            }

            var accountService = new AccountService(_db);
            var resolvedAccId = accountId ?? AccountService.ToLong(locked.GetValueOrDefault("account_id"));
            var account = await accountService.ResolveAccountAsync(resolvedAccId > 0 ? resolvedAccId : null, "cheque");
            var amount = AccountService.ToDecimal(locked.GetValueOrDefault("amount"));
            var partyType = locked.GetValueOrDefault("party_type")?.ToString() ?? "customer";
            var partyId = AccountService.ToLong(locked.GetValueOrDefault("party_id"));
            var payNo = locked.GetValueOrDefault("payment_number")?.ToString() ?? $"#{paymentId}";

            var upd = new Dictionary<string, object?>
            {
                ["cheque_status"] = "cleared",
                ["cleared_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };

            if (account != null)
            {
                upd["account_id"] = AccountService.ToLong(account["id"]);
                if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase))
                {
                    var cust = await _db.QuerySingleOrDefaultAsync("SELECT name FROM customers WHERE id = @partyId", new { partyId });
                    var pName = cust?.GetValueOrDefault("name")?.ToString() ?? "Customer";
                    await accountService.DepositAsync(account, amount, "customer_payment", paymentId, $"Cheque cleared {payNo} ({pName})", userId);
                }
                else
                {
                    var sup = await _db.QuerySingleOrDefaultAsync("SELECT name FROM suppliers WHERE id = @partyId", new { partyId });
                    var pName = sup?.GetValueOrDefault("name")?.ToString() ?? "Supplier";
                    await accountService.WithdrawAsync(account, amount, "supplier_payment", paymentId, $"Cheque cleared {payNo} ({pName})", userId);
                }
            }

            await _db.UpdateAsync("payments", paymentId, upd);
            var clrRow = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
            return await EnrichPaymentAsync(clrRow);
        }

        // bounced
        var bounceReason = !string.IsNullOrWhiteSpace(reason) ? reason : "Cheque Dishonoured / Bounced";
        if (!status.Equals("reversed", StringComparison.OrdinalIgnoreCase))
        {
            await ReversePaymentAsync(paymentId, bounceReason, userId);
        }

        await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?>
        {
            ["cheque_status"] = "bounced",
            ["bounced_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        var bouncedRow = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
        return await EnrichPaymentAsync(bouncedRow);
    }

    public async Task<Dictionary<string, object?>> AllocatePaymentAsync(
        long paymentId,
        IEnumerable<Dictionary<string, object?>> allocationsInput,
        long? userId = null)
    {
        var lockedPayment = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId })
                            ?? throw new KeyNotFoundException("Payment not found.");

        if (!string.Equals(lockedPayment.GetValueOrDefault("status")?.ToString(), "posted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only posted payments or credit notes can be allocated.");
        }

        var availableUnapplied = Math.Round(AccountService.ToDecimal(lockedPayment.GetValueOrDefault("unapplied_amount")), 2);
        if (availableUnapplied <= 0.001m)
        {
            throw new InvalidOperationException("This payment has no unapplied credit remaining to allocate.");
        }

        var partyType = lockedPayment.GetValueOrDefault("party_type")?.ToString() ?? "customer";
        var partyId = AccountService.ToLong(lockedPayment.GetValueOrDefault("party_id"));

        decimal totalNewAllocated = 0m;
        var allocationsToApply = new List<(Dictionary<string, object?> Invoice, string InvoiceType, decimal Amount)>();

        foreach (var alloc in allocationsInput)
        {
            var allocAmount = Math.Round(AccountService.ToDecimal(alloc.GetValueOrDefault("amount")), 2);
            var invoiceId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            if (allocAmount <= 0m || invoiceId <= 0) continue;

            if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase))
            {
                var invoice = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM sale_invoices WHERE id = @invoiceId AND customer_id = @partyId",
                    new { invoiceId, partyId })
                    ?? throw new InvalidOperationException($"Sale invoice #{invoiceId} not found for this customer.");

                if (string.Equals(invoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Invoice {invoice.GetValueOrDefault("posted_number")} is reversed and cannot receive allocations.");
                }

                var invTotal = AccountService.ToDecimal(invoice.GetValueOrDefault("total"));
                var invReceived = AccountService.ToDecimal(invoice.GetValueOrDefault("total_receive") ?? invoice.GetValueOrDefault("total_recieve"));
                var remainingDue = Math.Max(invTotal - invReceived, 0m);

                if (allocAmount > remainingDue + 0.001m)
                {
                    throw new InvalidOperationException($"Allocation of {allocAmount} exceeds remaining balance ({remainingDue}) on invoice {invoice.GetValueOrDefault("posted_number")}.");
                }

                allocationsToApply.Add((invoice, "sale_invoice", allocAmount));
            }
            else
            {
                var invoice = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM purchase_invoices WHERE id = @invoiceId AND supplier_id = @partyId",
                    new { invoiceId, partyId })
                    ?? throw new InvalidOperationException($"Purchase invoice #{invoiceId} not found for this supplier.");

                if (string.Equals(invoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Invoice {invoice.GetValueOrDefault("posted_number")} is reversed and cannot receive allocations.");
                }

                var invTotal = AccountService.ToDecimal(invoice.GetValueOrDefault("total_amount"));
                var invPaid = AccountService.ToDecimal(invoice.GetValueOrDefault("total_paid"));
                var remainingDue = Math.Max(invTotal - invPaid, 0m);

                if (allocAmount > remainingDue + 0.001m)
                {
                    throw new InvalidOperationException($"Allocation of {allocAmount} exceeds remaining balance ({remainingDue}) on invoice {invoice.GetValueOrDefault("posted_number")}.");
                }

                allocationsToApply.Add((invoice, "purchase_invoice", allocAmount));
            }

            totalNewAllocated = Math.Round(totalNewAllocated + allocAmount, 2);
        }

        if (totalNewAllocated <= 0m)
        {
            throw new InvalidOperationException("Please enter a valid allocation amount greater than zero.");
        }

        if (totalNewAllocated > availableUnapplied + 0.001m)
        {
            throw new InvalidOperationException($"Total allocation ({totalNewAllocated}) exceeds available unapplied credit ({availableUnapplied}).");
        }

        foreach (var item in allocationsToApply)
        {
            var invId = AccountService.ToLong(item.Invoice["id"]);
            var existingAlloc = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM payment_allocations WHERE payment_id = @paymentId AND invoice_type = @invType AND invoice_id = @invId LIMIT 1",
                new { paymentId, invType = item.InvoiceType, invId });

            if (existingAlloc != null)
            {
                var prevAmt = AccountService.ToDecimal(existingAlloc.GetValueOrDefault("amount"));
                await _db.UpdateAsync("payment_allocations", AccountService.ToLong(existingAlloc["id"]), new Dictionary<string, object?>
                {
                    ["amount"] = Math.Round(prevAmt + item.Amount, 2)
                });
            }
            else
            {
                await _db.InsertAsync("payment_allocations", new Dictionary<string, object?>
                {
                    ["payment_id"] = paymentId,
                    ["invoice_type"] = item.InvoiceType,
                    ["invoice_id"] = invId,
                    ["amount"] = item.Amount
                });
            }

            if (item.InvoiceType == "sale_invoice")
            {
                var curRec = AccountService.ToDecimal(item.Invoice.GetValueOrDefault("total_receive"));
                await _db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?>
                {
                    ["total_receive"] = Math.Round(curRec + item.Amount, 2)
                });
            }
            else
            {
                var curPaid = AccountService.ToDecimal(item.Invoice.GetValueOrDefault("total_paid"));
                await _db.UpdateAsync("purchase_invoices", invId, new Dictionary<string, object?>
                {
                    ["total_paid"] = Math.Round(curPaid + item.Amount, 2)
                });
            }
        }

        var newAllocatedTotal = Math.Round(AccountService.ToDecimal(lockedPayment.GetValueOrDefault("allocated_amount")) + totalNewAllocated, 2);
        var payAmount = AccountService.ToDecimal(lockedPayment.GetValueOrDefault("amount"));
        var newUnapplied = Math.Round(Math.Max(payAmount - newAllocatedTotal, 0m), 2);

        await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?>
        {
            ["allocated_amount"] = newAllocatedTotal,
            ["unapplied_amount"] = newUnapplied
        });

        var updatedPayment = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }))!;
        await _auditService.RecordUpdateAsync("payment", paymentId, lockedPayment, updatedPayment, userId, $"Allocated {totalNewAllocated} of unapplied credit to invoices");
        return await EnrichPaymentAsync(updatedPayment);
    }

    public async Task SyncMissingReturnCreditPaymentsAsync(string? partyType = null, long? partyId = null)
    {
        if (!await _db.HasTableAsync("payments")) return;

        if ((partyType == null || partyType.Equals("customer", StringComparison.OrdinalIgnoreCase)) &&
            await _db.HasTableAsync("sale_returns") &&
            await _db.HasColumnAsync("sale_returns", "settlement_mode"))
        {
            var sql = @"SELECT * FROM sale_returns
                        WHERE settlement_mode IN ('credit_note', 'invoice_offset')
                          AND COALESCE(credited_amount, 0) > 0
                          AND customer_id IS NOT NULL";
            if (partyType != null && partyId.HasValue)
            {
                sql += " AND customer_id = @partyId";
            }
            var saleReturns = await _db.QueryListAsync(sql, new { partyId });
            foreach (var sr in saleReturns)
            {
                var srId = AccountService.ToLong(sr["id"]);
                var key = $"sale_return:{srId}";
                var exists = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE idempotency_key = @key", new { key }) > 0;
                if (!exists)
                {
                    var credAmt = Math.Round(AccountService.ToDecimal(sr.GetValueOrDefault("credited_amount")), 2);
                    var custId = AccountService.ToLong(sr.GetValueOrDefault("customer_id"));
                    var postedNo = sr.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";
                    var payNo = await GeneratePaymentNumberAsync("customer");
                    await _db.InsertAsync("payments", new Dictionary<string, object?>
                    {
                        ["payment_number"] = payNo,
                        ["party_type"] = "customer",
                        ["party_id"] = custId,
                        ["payment_date"] = sr.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ["amount"] = credAmt,
                        ["allocated_amount"] = 0m,
                        ["unapplied_amount"] = credAmt,
                        ["payment_method"] = "credit_note",
                        ["reference"] = postedNo,
                        ["status"] = "posted",
                        ["notes"] = $"Credit note from Sale Return {postedNo}",
                        ["idempotency_key"] = key
                    });
                }
            }
        }

        if ((partyType == null || partyType.Equals("supplier", StringComparison.OrdinalIgnoreCase)) &&
            await _db.HasTableAsync("purchase_returns") &&
            await _db.HasColumnAsync("purchase_returns", "settlement_mode"))
        {
            var sql = @"SELECT * FROM purchase_returns
                        WHERE settlement_mode IN ('credit_note', 'invoice_offset')
                          AND COALESCE(credited_amount, 0) > 0
                          AND supplier_id IS NOT NULL";
            if (partyType != null && partyId.HasValue)
            {
                sql += " AND supplier_id = @partyId";
            }
            var purchaseReturns = await _db.QueryListAsync(sql, new { partyId });
            foreach (var pr in purchaseReturns)
            {
                var prId = AccountService.ToLong(pr["id"]);
                var key = $"purchase_return:{prId}";
                var exists = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE idempotency_key = @key", new { key }) > 0;
                if (!exists)
                {
                    var credAmt = Math.Round(AccountService.ToDecimal(pr.GetValueOrDefault("credited_amount")), 2);
                    var supId = AccountService.ToLong(pr.GetValueOrDefault("supplier_id"));
                    var postedNo = pr.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";
                    var payNo = await GeneratePaymentNumberAsync("supplier");
                    await _db.InsertAsync("payments", new Dictionary<string, object?>
                    {
                        ["payment_number"] = payNo,
                        ["party_type"] = "supplier",
                        ["party_id"] = supId,
                        ["payment_date"] = pr.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        ["amount"] = credAmt,
                        ["allocated_amount"] = 0m,
                        ["unapplied_amount"] = credAmt,
                        ["payment_method"] = "credit_note",
                        ["reference"] = postedNo,
                        ["status"] = "posted",
                        ["notes"] = $"Credit note from Purchase Return {postedNo}",
                        ["idempotency_key"] = key
                    });
                }
            }
        }
    }

    public async Task<Dictionary<string, object?>> GetAvailableCreditsForPartyAsync(string partyType, long partyId)
    {
        await SyncMissingReturnCreditPaymentsAsync(partyType, partyId);

        var credits = await _db.HasTableAsync("payments")
            ? await _db.QueryListAsync(
                @"SELECT * FROM payments
                  WHERE party_type = @partyType AND party_id = @partyId AND status = 'posted' AND unapplied_amount > 0
                  ORDER BY payment_date ASC, id ASC",
                new { partyType, partyId })
            : new List<Dictionary<string, object?>>();

        for (int i = 0; i < credits.Count; i++)
        {
            credits[i] = await EnrichPaymentAsync(credits[i]);
        }

        var totalUnapplied = Math.Round(credits.Sum(c => AccountService.ToDecimal(c.GetValueOrDefault("unapplied_amount"))), 2);

        return new Dictionary<string, object?>
        {
            ["party_type"] = partyType,
            ["party_id"] = partyId,
            ["total_unapplied"] = totalUnapplied,
            ["credits"] = credits
        };
    }

    public async Task<Dictionary<string, object?>> ApplyPartyCreditsAsync(
        string partyType,
        long partyId,
        IEnumerable<Dictionary<string, object?>> allocationsInput,
        long? userId = null)
    {
        await SyncMissingReturnCreditPaymentsAsync(partyType, partyId);

        var credits = await _db.QueryListAsync(
            @"SELECT * FROM payments
              WHERE party_type = @partyType AND party_id = @partyId AND status = 'posted' AND unapplied_amount > 0
              ORDER BY payment_date ASC, id ASC",
            new { partyType, partyId });

        var totalAvailable = Math.Round(credits.Sum(c => AccountService.ToDecimal(c.GetValueOrDefault("unapplied_amount"))), 2);
        if (totalAvailable <= 0.001m)
        {
            throw new InvalidOperationException("No unapplied payments or credit notes are available for this party.");
        }

        var invoiceBuckets = new Dictionary<long, decimal>();
        decimal totalRequested = 0m;
        foreach (var alloc in allocationsInput)
        {
            var invId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            var amt = Math.Round(AccountService.ToDecimal(alloc.GetValueOrDefault("amount")), 2);
            if (invId <= 0 || amt <= 0m) continue;
            invoiceBuckets[invId] = Math.Round((invoiceBuckets.TryGetValue(invId, out var prev) ? prev : 0m) + amt, 2);
            totalRequested = Math.Round(totalRequested + amt, 2);
        }

        if (totalRequested <= 0m)
        {
            throw new InvalidOperationException("Please specify at least one invoice allocation amount greater than zero.");
        }

        if (totalRequested > totalAvailable + 0.001m)
        {
            throw new InvalidOperationException($"Requested credit application ({totalRequested}) exceeds total available unapplied credit ({totalAvailable}).");
        }

        var updatedPayments = new List<Dictionary<string, object?>>();
        decimal totalAllocatedAcrossAll = 0m;
        var invKeys = invoiceBuckets.Keys.ToList();

        foreach (var creditPayment in credits)
        {
            var creditRemaining = Math.Round(AccountService.ToDecimal(creditPayment.GetValueOrDefault("unapplied_amount")), 2);
            if (creditRemaining <= 0.001m) continue;

            var perPaymentAllocations = new List<Dictionary<string, object?>>();
            foreach (var invId in invKeys)
            {
                var neededAmt = invoiceBuckets[invId];
                if (neededAmt <= 0.001m || creditRemaining <= 0.001m) continue;

                var slice = Math.Round(Math.Min(creditRemaining, neededAmt), 2);
                if (slice > 0m)
                {
                    perPaymentAllocations.Add(new Dictionary<string, object?>
                    {
                        ["invoice_id"] = invId,
                        ["amount"] = slice
                    });
                    invoiceBuckets[invId] = Math.Round(neededAmt - slice, 2);
                    creditRemaining = Math.Round(creditRemaining - slice, 2);
                    totalAllocatedAcrossAll = Math.Round(totalAllocatedAcrossAll + slice, 2);
                }
            }

            if (perPaymentAllocations.Count > 0)
            {
                var updated = await AllocatePaymentAsync(AccountService.ToLong(creditPayment["id"]), perPaymentAllocations, userId);
                updatedPayments.Add(updated);
            }
        }

        return new Dictionary<string, object?>
        {
            ["allocated_total"] = totalAllocatedAcrossAll,
            ["payments"] = updatedPayments
        };
    }

    public async Task<List<Dictionary<string, object?>>> GetOpenInvoicesForCustomerAsync(long customerId)
    {
        if (!await _db.HasTableAsync("sale_invoices")) return new List<Dictionary<string, object?>>();

        var invoices = await _db.QueryListAsync(
            @"SELECT * FROM sale_invoices
              WHERE customer_id = @customerId
                AND invoice_type = 'credit'
                AND (status IS NULL OR status != 'reversed')
                AND (COALESCE(total, 0) - COALESCE(total_receive, 0)) > 0.01
              ORDER BY date ASC, id ASC",
            new { customerId });

        var open = new List<Dictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var id = AccountService.ToLong(inv.GetValueOrDefault("id"));
            var total = AccountService.ToDecimal(inv.GetValueOrDefault("total"));
            var received = AccountService.ToDecimal(inv.GetValueOrDefault("total_receive") ?? inv.GetValueOrDefault("total_recieve"));
            var remaining = Math.Round(Math.Max(total - received, 0m), 2);
            if (remaining > 0.01m)
            {
                var dateStr = inv.GetValueOrDefault("date")?.ToString();
                var dueStr = inv.GetValueOrDefault("due_date")?.ToString() ?? dateStr;
                open.Add(new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["posted_number"] = inv.GetValueOrDefault("posted_number")?.ToString() ?? $"#{id}",
                    ["date"] = dateStr,
                    ["due_date"] = dueStr,
                    ["total"] = total,
                    ["received"] = received,
                    ["due"] = remaining
                });
            }
        }
        return open;
    }

    public async Task<List<Dictionary<string, object?>>> GetOpenInvoicesForSupplierAsync(long supplierId)
    {
        if (!await _db.HasTableAsync("purchase_invoices")) return new List<Dictionary<string, object?>>();

        var invoices = await _db.QueryListAsync(
            @"SELECT * FROM purchase_invoices
              WHERE supplier_id = @supplierId
                AND invoice_type = 'credit'
                AND (status IS NULL OR status != 'reversed')
                AND (COALESCE(total_amount, 0) - COALESCE(total_paid, 0)) > 0.01
              ORDER BY posted_date ASC, id ASC",
            new { supplierId });

        var open = new List<Dictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var id = AccountService.ToLong(inv.GetValueOrDefault("id"));
            var total = AccountService.ToDecimal(inv.GetValueOrDefault("total_amount"));
            var paid = AccountService.ToDecimal(inv.GetValueOrDefault("total_paid"));
            var remaining = Math.Round(Math.Max(total - paid, 0m), 2);
            if (remaining > 0.01m)
            {
                var dateStr = inv.GetValueOrDefault("posted_date")?.ToString();
                var dueStr = inv.GetValueOrDefault("due_date")?.ToString() ?? dateStr;
                open.Add(new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["posted_number"] = inv.GetValueOrDefault("posted_number")?.ToString()
                                        ?? inv.GetValueOrDefault("invoice_number")?.ToString()
                                        ?? $"#{id}",
                    ["date"] = dateStr,
                    ["due_date"] = dueStr,
                    ["total"] = total,
                    ["paid"] = paid,
                    ["due"] = remaining
                });
            }
        }
        return open;
    }

    public async Task<Dictionary<string, object?>> GetPartyCreditsSummaryAsync(Dictionary<string, object?>? filters = null)
    {
        var partyTypeFilter = filters?.GetValueOrDefault("party_type")?.ToString() ?? "all";
        var statusFilter = filters?.GetValueOrDefault("status")?.ToString() ?? "all";
        var search = (filters?.GetValueOrDefault("search")?.ToString() ?? "").Trim();
        var minAmountRaw = filters?.GetValueOrDefault("min_amount");
        decimal? minAmount = minAmountRaw != null && !string.IsNullOrWhiteSpace(minAmountRaw.ToString())
            ? AccountService.ToDecimal(minAmountRaw)
            : null;
        var sort = filters?.GetValueOrDefault("sort")?.ToString() ?? "highest";
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var rows = new List<Dictionary<string, object?>>();

        if (partyTypeFilter.Equals("all", StringComparison.OrdinalIgnoreCase) || partyTypeFilter.Equals("customer", StringComparison.OrdinalIgnoreCase))
        {
            var customerBalances = await GetBulkCustomerNetBalancesAsync(_db);
            if (customerBalances.Count > 0)
            {
                var custIds = customerBalances.Keys.ToArray();
                var customers = (await _db.QueryListAsync(
                    "SELECT id, name, phone, email, credit_limit, credit_days FROM customers WHERE id IN @custIds",
                    new { custIds })).ToDictionary(c => AccountService.ToLong(c["id"]));

                var openSales = await _db.QueryListAsync(
                    @"SELECT id, customer_id, date, due_date, total, total_receive
                      FROM sale_invoices
                      WHERE customer_id IN @custIds
                        AND invoice_type = 'credit'
                        AND (status IS NULL OR status != 'reversed')
                        AND (COALESCE(total, 0) - COALESCE(total_receive, 0)) > 0.01",
                    new { custIds });
                var openByCust = openSales.GroupBy(s => AccountService.ToLong(s.GetValueOrDefault("customer_id")))
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kv in customerBalances)
                {
                    var custId = kv.Key;
                    var netDue = kv.Value;
                    if (netDue <= 0.01m) continue;

                    customers.TryGetValue(custId, out var customer);
                    var name = customer?.GetValueOrDefault("name")?.ToString() ?? $"Customer #{custId}";
                    var phone = customer?.GetValueOrDefault("phone")?.ToString() ?? "";
                    var email = customer?.GetValueOrDefault("email")?.ToString() ?? "";

                    openByCust.TryGetValue(custId, out var invList);
                    invList ??= new List<Dictionary<string, object?>>();
                    var openCount = invList.Count;
                    decimal overdueAmt = 0m;
                    int overdueCount = 0;
                    string? oldestDueDate = null;

                    foreach (var inv in invList)
                    {
                        var dueAmt = Math.Max(0m, Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("total")) - AccountService.ToDecimal(inv.GetValueOrDefault("total_receive")), 2));
                        var dDate = inv.GetValueOrDefault("due_date")?.ToString() ?? inv.GetValueOrDefault("date")?.ToString() ?? "";
                        if (dDate.Length >= 10) dDate = dDate[..10];
                        if (!string.IsNullOrEmpty(dDate) && (oldestDueDate == null || string.CompareOrdinal(dDate, oldestDueDate) < 0))
                        {
                            oldestDueDate = dDate;
                        }
                        if (!string.IsNullOrEmpty(dDate) && string.CompareOrdinal(dDate, today) < 0 && dueAmt > 0.01m)
                        {
                            overdueAmt = Math.Round(overdueAmt + dueAmt, 2);
                            overdueCount++;
                        }
                    }
                    overdueAmt = Math.Min(overdueAmt, netDue);

                    rows.Add(new Dictionary<string, object?>
                    {
                        ["party_type"] = "customer",
                        ["party_id"] = custId,
                        ["party_name"] = name,
                        ["party_phone"] = phone,
                        ["party_email"] = email,
                        ["credit_limit"] = customer?.GetValueOrDefault("credit_limit") != null ? AccountService.ToDecimal(customer["credit_limit"]) : null,
                        ["credit_days"] = customer?.GetValueOrDefault("credit_days") != null ? (int)AccountService.ToLong(customer["credit_days"]) : 30,
                        ["open_invoices_count"] = openCount,
                        ["overdue_count"] = overdueCount,
                        ["overdue_amount"] = Math.Round(overdueAmt, 2),
                        ["oldest_due_date"] = oldestDueDate,
                        ["total_due"] = Math.Round(netDue, 2)
                    });
                }
            }
        }

        if (partyTypeFilter.Equals("all", StringComparison.OrdinalIgnoreCase) || partyTypeFilter.Equals("supplier", StringComparison.OrdinalIgnoreCase))
        {
            var allSupRows = await _db.HasTableAsync("suppliers")
                ? await _db.QueryListAsync("SELECT id FROM suppliers")
                : new List<Dictionary<string, object?>>();
            var allSupIds = allSupRows.Select(r => AccountService.ToLong(r["id"])).Where(id => id > 0).ToArray();
            var supplierBalances = allSupIds.Length > 0 ? await GetBulkSupplierNetBalancesAsync(_db, allSupIds) : new Dictionary<long, decimal>();
            var positiveSupIds = supplierBalances.Where(kv => kv.Value > 0.01m).Select(kv => kv.Key).ToArray();

            if (positiveSupIds.Length > 0)
            {
                var suppliers = (await _db.QueryListAsync(
                    "SELECT id, name, phone, email, credit_limit, credit_days FROM suppliers WHERE id IN @positiveSupIds",
                    new { positiveSupIds })).ToDictionary(s => AccountService.ToLong(s["id"]));

                var openPurchases = await _db.QueryListAsync(
                    @"SELECT id, supplier_id, posted_date, due_date, total_amount, total_paid
                      FROM purchase_invoices
                      WHERE supplier_id IN @positiveSupIds
                        AND invoice_type = 'credit'
                        AND (status IS NULL OR status != 'reversed')
                        AND (COALESCE(total_amount, 0) - COALESCE(total_paid, 0)) > 0.01",
                    new { positiveSupIds });
                var openBySup = openPurchases.GroupBy(p => AccountService.ToLong(p.GetValueOrDefault("supplier_id")))
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var supId in positiveSupIds)
                {
                    var netDue = Math.Round(supplierBalances.GetValueOrDefault(supId), 2);
                    if (netDue <= 0.01m) continue;

                    suppliers.TryGetValue(supId, out var supplier);
                    var name = supplier?.GetValueOrDefault("name")?.ToString() ?? $"Supplier #{supId}";
                    var phone = supplier?.GetValueOrDefault("phone")?.ToString() ?? "";
                    var email = supplier?.GetValueOrDefault("email")?.ToString() ?? "";

                    openBySup.TryGetValue(supId, out var invList);
                    invList ??= new List<Dictionary<string, object?>>();
                    var openCount = invList.Count;
                    decimal overdueAmt = 0m;
                    int overdueCount = 0;
                    string? oldestDueDate = null;

                    foreach (var inv in invList)
                    {
                        var dueAmt = Math.Max(0m, Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("total_amount")) - AccountService.ToDecimal(inv.GetValueOrDefault("total_paid")), 2));
                        var dDate = inv.GetValueOrDefault("due_date")?.ToString() ?? inv.GetValueOrDefault("posted_date")?.ToString() ?? "";
                        if (dDate.Length >= 10) dDate = dDate[..10];
                        if (!string.IsNullOrEmpty(dDate) && (oldestDueDate == null || string.CompareOrdinal(dDate, oldestDueDate) < 0))
                        {
                            oldestDueDate = dDate;
                        }
                        if (!string.IsNullOrEmpty(dDate) && string.CompareOrdinal(dDate, today) < 0 && dueAmt > 0.01m)
                        {
                            overdueAmt = Math.Round(overdueAmt + dueAmt, 2);
                            overdueCount++;
                        }
                    }
                    overdueAmt = Math.Min(overdueAmt, netDue);

                    rows.Add(new Dictionary<string, object?>
                    {
                        ["party_type"] = "supplier",
                        ["party_id"] = supId,
                        ["party_name"] = name,
                        ["party_phone"] = phone,
                        ["party_email"] = email,
                        ["credit_limit"] = supplier?.GetValueOrDefault("credit_limit") != null ? AccountService.ToDecimal(supplier["credit_limit"]) : null,
                        ["credit_days"] = supplier?.GetValueOrDefault("credit_days") != null ? (int)AccountService.ToLong(supplier["credit_days"]) : 30,
                        ["open_invoices_count"] = openCount,
                        ["overdue_count"] = overdueCount,
                        ["overdue_amount"] = Math.Round(overdueAmt, 2),
                        ["oldest_due_date"] = oldestDueDate,
                        ["total_due"] = netDue
                    });
                }
            }
        }

        var filtered = rows.Where(row =>
        {
            if (!string.IsNullOrEmpty(search))
            {
                var hay = $"{row.GetValueOrDefault("party_name")} {row.GetValueOrDefault("party_phone")} {row.GetValueOrDefault("party_email")}";
                if (!hay.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
            }
            if (statusFilter.Equals("overdue", StringComparison.OrdinalIgnoreCase) && AccountService.ToDecimal(row.GetValueOrDefault("overdue_amount")) <= 0.01m)
            {
                return false;
            }
            if (minAmount.HasValue && AccountService.ToDecimal(row.GetValueOrDefault("total_due")) < minAmount.Value)
            {
                return false;
            }
            return true;
        }).ToList();

        filtered = sort.ToLowerInvariant() switch
        {
            "lowest" => filtered.OrderBy(r => AccountService.ToDecimal(r["total_due"])).ToList(),
            "name_asc" => filtered.OrderBy(r => r["party_name"]?.ToString(), StringComparer.OrdinalIgnoreCase).ToList(),
            "overdue" => filtered.OrderByDescending(r => AccountService.ToDecimal(r["overdue_amount"])).ThenByDescending(r => AccountService.ToDecimal(r["total_due"])).ToList(),
            _ => filtered.OrderByDescending(r => AccountService.ToDecimal(r["total_due"])).ToList()
        };

        decimal supplierTotal = 0m;
        int supplierCount = 0;
        decimal customerTotal = 0m;
        int customerCount = 0;
        decimal overdueTotal = 0m;

        foreach (var r in filtered)
        {
            overdueTotal = Math.Round(overdueTotal + AccountService.ToDecimal(r["overdue_amount"]), 2);
            var due = AccountService.ToDecimal(r["total_due"]);
            if (string.Equals(r["party_type"]?.ToString(), "supplier", StringComparison.OrdinalIgnoreCase))
            {
                supplierTotal = Math.Round(supplierTotal + due, 2);
                supplierCount++;
            }
            else
            {
                customerTotal = Math.Round(customerTotal + due, 2);
                customerCount++;
            }
        }

        return new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_due"] = Math.Round(supplierTotal + customerTotal, 2),
                ["supplier_total"] = supplierTotal,
                ["supplier_count"] = supplierCount,
                ["customer_total"] = customerTotal,
                ["customer_count"] = customerCount,
                ["overdue_total"] = overdueTotal,
                ["party_count"] = filtered.Count
            },
            ["rows"] = filtered
        };
    }

    public async Task<Dictionary<string, object?>> GetPartyDueDetailsAsync(string partyType, long partyId)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var todayDt = DateTime.UtcNow.Date;
        var items = new List<Dictionary<string, object?>>();

        Dictionary<string, object?> party;
        decimal netDue;

        if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase))
        {
            party = await _db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @partyId", new { partyId })
                    ?? throw new KeyNotFoundException("Customer not found.");
            netDue = await GetCustomerNetBalanceAsync(_db, partyId);
            var openInvoices = await GetOpenInvoicesForCustomerAsync(partyId);

            foreach (var inv in openInvoices)
            {
                var dateStr = (inv.GetValueOrDefault("date")?.ToString() ?? "") is var ds && ds.Length >= 10 ? ds[..10] : ds;
                var dueDate = (inv.GetValueOrDefault("due_date")?.ToString() ?? dateStr) is var dds && dds.Length >= 10 ? dds[..10] : dds;
                var isOverdue = !string.IsNullOrEmpty(dueDate) && string.CompareOrdinal(dueDate, today) < 0;
                var daysOverdue = isOverdue && DateTime.TryParse(dueDate, out var dDt) ? Math.Max(0, (int)(todayDt - dDt.Date).TotalDays) : 0;

                items.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"inv-{inv["id"]}",
                    ["type"] = "Sale Invoice",
                    ["reference_number"] = inv["posted_number"],
                    ["date"] = dateStr,
                    ["due_date"] = dueDate,
                    ["is_overdue"] = isOverdue,
                    ["days_overdue"] = daysOverdue,
                    ["total"] = Math.Round(AccountService.ToDecimal(inv["total"]), 2),
                    ["paid_or_received"] = Math.Round(AccountService.ToDecimal(inv["received"]), 2),
                    ["due"] = Math.Round(AccountService.ToDecimal(inv["due"]), 2)
                });
            }

            if (await _db.HasTableAsync("customer_ledgers"))
            {
                var creditDays = party.GetValueOrDefault("credit_days") != null ? (int)AccountService.ToLong(party["credit_days"]) : 30;
                var manualRows = await _db.QueryListAsync(
                    @"SELECT * FROM customer_ledgers
                      WHERE customer_id = @partyId AND sale_invoice_id IS NULL AND entry_type IN ('manual', 'invoice')
                      ORDER BY entry_date ASC",
                    new { partyId });

                foreach (var mRow in manualRows)
                {
                    var mTotal = Math.Round(AccountService.ToDecimal(mRow.GetValueOrDefault("invoice_total")), 2);
                    var mPaid = Math.Round(AccountService.ToDecimal(mRow.GetValueOrDefault("total_received")), 2);
                    var mDue = Math.Max(0m, Math.Round(mTotal - mPaid, 2));
                    if (mDue <= 0.01m) continue;

                    var eDate = (mRow.GetValueOrDefault("entry_date")?.ToString() ?? "") is var eds && eds.Length >= 10 ? eds[..10] : eds;
                    var rawDue = mRow.GetValueOrDefault("due_date")?.ToString();
                    string dDate = !string.IsNullOrWhiteSpace(rawDue) && rawDue.Length >= 10
                        ? rawDue[..10]
                        : (DateTime.TryParse(eDate, out var eDt) ? eDt.AddDays(creditDays).ToString("yyyy-MM-dd") : "");
                    var isOverdue = !string.IsNullOrEmpty(dDate) && string.CompareOrdinal(dDate, today) < 0;
                    var daysOverdue = isOverdue && DateTime.TryParse(dDate, out var dDt) ? Math.Max(0, (int)(todayDt - dDt.Date).TotalDays) : 0;

                    items.Add(new Dictionary<string, object?>
                    {
                        ["id"] = $"manual-{mRow["id"]}",
                        ["type"] = "Opening / Manual",
                        ["reference_number"] = mRow.GetValueOrDefault("description")?.ToString() ?? $"Entry #{mRow["id"]}",
                        ["date"] = eDate,
                        ["due_date"] = dDate,
                        ["is_overdue"] = isOverdue,
                        ["days_overdue"] = daysOverdue,
                        ["total"] = mTotal,
                        ["paid_or_received"] = mPaid,
                        ["due"] = mDue
                    });
                }
            }
        }
        else
        {
            party = await _db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @partyId", new { partyId })
                    ?? throw new KeyNotFoundException("Supplier not found.");
            netDue = await GetSupplierNetBalanceAsync(_db, partyId);
            var openInvoices = await GetOpenInvoicesForSupplierAsync(partyId);

            foreach (var inv in openInvoices)
            {
                var dateStr = (inv.GetValueOrDefault("date")?.ToString() ?? "") is var ds && ds.Length >= 10 ? ds[..10] : ds;
                var dueDate = (inv.GetValueOrDefault("due_date")?.ToString() ?? dateStr) is var dds && dds.Length >= 10 ? dds[..10] : dds;
                var isOverdue = !string.IsNullOrEmpty(dueDate) && string.CompareOrdinal(dueDate, today) < 0;
                var daysOverdue = isOverdue && DateTime.TryParse(dueDate, out var dDt) ? Math.Max(0, (int)(todayDt - dDt.Date).TotalDays) : 0;

                items.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"inv-{inv["id"]}",
                    ["type"] = "Purchase Invoice",
                    ["reference_number"] = inv["posted_number"],
                    ["date"] = dateStr,
                    ["due_date"] = dueDate,
                    ["is_overdue"] = isOverdue,
                    ["days_overdue"] = daysOverdue,
                    ["total"] = Math.Round(AccountService.ToDecimal(inv["total"]), 2),
                    ["paid_or_received"] = Math.Round(AccountService.ToDecimal(inv["paid"]), 2),
                    ["due"] = Math.Round(AccountService.ToDecimal(inv["due"]), 2)
                });
            }

            if (await _db.HasTableAsync("supplier_ledgers"))
            {
                var creditDays = party.GetValueOrDefault("credit_days") != null ? (int)AccountService.ToLong(party["credit_days"]) : 30;
                var manualRows = await _db.QueryListAsync(
                    @"SELECT * FROM supplier_ledgers
                      WHERE supplier_id = @partyId AND purchase_invoice_id IS NULL AND entry_type IN ('manual', 'invoice')
                      ORDER BY entry_date ASC",
                    new { partyId });

                foreach (var mRow in manualRows)
                {
                    var mTotal = Math.Round(AccountService.ToDecimal(mRow.GetValueOrDefault("invoice_total")), 2);
                    var mPaid = Math.Round(AccountService.ToDecimal(mRow.GetValueOrDefault("total_paid")), 2);
                    var mDue = Math.Max(0m, Math.Round(mTotal - mPaid, 2));
                    if (mDue <= 0.01m) continue;

                    var eDate = (mRow.GetValueOrDefault("entry_date")?.ToString() ?? "") is var eds && eds.Length >= 10 ? eds[..10] : eds;
                    var rawDue = mRow.GetValueOrDefault("due_date")?.ToString();
                    string dDate = !string.IsNullOrWhiteSpace(rawDue) && rawDue.Length >= 10
                        ? rawDue[..10]
                        : (DateTime.TryParse(eDate, out var eDt) ? eDt.AddDays(creditDays).ToString("yyyy-MM-dd") : "");
                    var isOverdue = !string.IsNullOrEmpty(dDate) && string.CompareOrdinal(dDate, today) < 0;
                    var daysOverdue = isOverdue && DateTime.TryParse(dDate, out var dDt) ? Math.Max(0, (int)(todayDt - dDt.Date).TotalDays) : 0;

                    items.Add(new Dictionary<string, object?>
                    {
                        ["id"] = $"manual-{mRow["id"]}",
                        ["type"] = "Opening / Manual",
                        ["reference_number"] = mRow.GetValueOrDefault("description")?.ToString() ?? $"Entry #{mRow["id"]}",
                        ["date"] = eDate,
                        ["due_date"] = dDate,
                        ["is_overdue"] = isOverdue,
                        ["days_overdue"] = daysOverdue,
                        ["total"] = mTotal,
                        ["paid_or_received"] = mPaid,
                        ["due"] = mDue
                    });
                }
            }
        }

        decimal totalOverdue = 0m;
        int overdueCount = 0;
        foreach (var it in items)
        {
            if (AccountService.IsTruthy(it.GetValueOrDefault("is_overdue")))
            {
                totalOverdue = Math.Round(totalOverdue + AccountService.ToDecimal(it["due"]), 2);
                overdueCount++;
            }
        }
        totalOverdue = Math.Min(totalOverdue, netDue);

        return new Dictionary<string, object?>
        {
            ["party_type"] = partyType.ToLowerInvariant(),
            ["party_id"] = partyId,
            ["party_name"] = party.GetValueOrDefault("name"),
            ["party_phone"] = party.GetValueOrDefault("phone"),
            ["party_email"] = party.GetValueOrDefault("email"),
            ["credit_limit"] = party.GetValueOrDefault("credit_limit") != null ? AccountService.ToDecimal(party["credit_limit"]) : null,
            ["credit_days"] = party.GetValueOrDefault("credit_days") != null ? (int)AccountService.ToLong(party["credit_days"]) : 30,
            ["total_due"] = Math.Round(netDue, 2),
            ["total_overdue"] = Math.Round(totalOverdue, 2),
            ["overdue_count"] = overdueCount,
            ["items"] = items
        };
    }

    public static async Task<decimal> GetCustomerNetBalanceAsync(KarobarDb db, long customerId, string? asOfDate = null)
    {
        var map = await GetBulkCustomerNetBalancesAsync(db, new[] { customerId }, asOfDate);
        return map.TryGetValue(customerId, out var bal) ? bal : 0m;
    }

    public static async Task<Dictionary<long, decimal>> GetBulkCustomerNetBalancesAsync(
        KarobarDb db,
        IEnumerable<long>? customerIds = null,
        string? asOfDate = null)
    {
        var result = new Dictionary<long, decimal>();
        if (!await db.HasTableAsync("customer_ledgers")) return result;

        var filterIds = customerIds?.Where(id => id > 0).Distinct().ToArray();
        if (filterIds is { Length: 0 }) return result;

        var walkInIds = new List<long>();
        if (await db.HasTableAsync("customers"))
        {
            var wiRows = await db.QueryListAsync(
                "SELECT id FROM customers WHERE LOWER(TRIM(name)) IN ('walk-in customer', 'walk in customer', 'walking customer', 'cash customer')");
            walkInIds.AddRange(wiRows.Select(r => AccountService.ToLong(r["id"])).Where(id => id > 0));
        }

        var invSql = @"
            SELECT cl.customer_id,
                   SUM(COALESCE(cl.invoice_total, 0)) as total_inv,
                   SUM(CASE WHEN COALESCE(cl.total_received, 0) < COALESCE(cl.invoice_total, 0) THEN COALESCE(cl.total_received, 0) ELSE COALESCE(cl.invoice_total, 0) END) as received_on_inv
            FROM customer_ledgers cl
            LEFT JOIN sale_invoices si ON si.id = cl.sale_invoice_id
            WHERE cl.customer_id IS NOT NULL
              AND cl.entry_type IN ('invoice', 'manual')
              AND (cl.sale_invoice_id IS NULL OR si.status IS NULL OR si.status != 'reversed')";

        if (filterIds != null) invSql += " AND cl.customer_id IN @filterIds";
        if (walkInIds.Count > 0) invSql += " AND cl.customer_id NOT IN @walkInIds";
        if (!string.IsNullOrWhiteSpace(asOfDate)) invSql += " AND DATE(cl.entry_date) <= @asOfDate";
        invSql += " GROUP BY cl.customer_id";

        var invRows = await db.QueryListAsync(invSql, new { filterIds, walkInIds, asOfDate });

        var paySql = @"
            SELECT customer_id, SUM(COALESCE(credited_amount, 0)) as total_credited
            FROM customer_ledgers
            WHERE customer_id IS NOT NULL
              AND entry_type = 'payment'";
        if (filterIds != null) paySql += " AND customer_id IN @filterIds";
        if (walkInIds.Count > 0) paySql += " AND customer_id NOT IN @walkInIds";
        if (!string.IsNullOrWhiteSpace(asOfDate)) paySql += " AND DATE(entry_date) <= @asOfDate";
        paySql += " GROUP BY customer_id";

        var payRows = await db.QueryListAsync(paySql, new { filterIds, walkInIds, asOfDate });

        var invMap = invRows.ToDictionary(r => AccountService.ToLong(r["customer_id"]));
        var payMap = payRows.ToDictionary(r => AccountService.ToLong(r["customer_id"]));

        foreach (var cid in invMap.Keys.Union(payMap.Keys))
        {
            if (cid <= 0) continue;
            invMap.TryGetValue(cid, out var inv);
            payMap.TryGetValue(cid, out var pay);
            var totalInv = AccountService.ToDecimal(inv?.GetValueOrDefault("total_inv"));
            var receivedOnInv = AccountService.ToDecimal(inv?.GetValueOrDefault("received_on_inv"));
            var totalCredited = AccountService.ToDecimal(pay?.GetValueOrDefault("total_credited"));
            var net = (totalInv - receivedOnInv) - totalCredited;
            if (net > 0.01m)
            {
                result[cid] = Math.Round(net, 2);
            }
        }

        return result;
    }

    public static async Task<decimal> GetTotalReceivablesBalanceAsync(KarobarDb db, string? asOfDate = null)
    {
        var map = await GetBulkCustomerNetBalancesAsync(db, null, asOfDate);
        return Math.Round(map.Values.Sum(), 2);
    }

    public static async Task<decimal> GetSupplierNetBalanceAsync(KarobarDb db, long supplierId, string? asOfDate = null)
    {
        var map = await GetBulkSupplierNetBalancesAsync(db, new[] { supplierId }, asOfDate);
        return map.TryGetValue(supplierId, out var bal) ? bal : 0m;
    }

    public static async Task<Dictionary<long, decimal>> GetBulkSupplierNetBalancesAsync(
        KarobarDb db,
        IEnumerable<long> supplierIds,
        string? asOfDate = null)
    {
        var ids = supplierIds.Where(id => id > 0).Distinct().ToArray();
        var result = new Dictionary<long, decimal>();
        if (ids.Length == 0 || !await db.HasTableAsync("supplier_ledgers")) return result;

        var ledgerSql = "SELECT * FROM supplier_ledgers WHERE supplier_id IN @ids";
        if (!string.IsNullOrWhiteSpace(asOfDate)) ledgerSql += " AND DATE(entry_date) <= @asOfDate";
        var ledgerRows = await db.QueryListAsync(ledgerSql, new { ids, asOfDate });
        var rowsBySupplier = ledgerRows.GroupBy(r => AccountService.ToLong(r.GetValueOrDefault("supplier_id")))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allLinkedInvIds = ledgerRows
            .Select(r => AccountService.ToLong(r.GetValueOrDefault("purchase_invoice_id")))
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        var pInvMap = new Dictionary<long, Dictionary<string, object?>>();
        var unlinkedBySupplier = new Dictionary<long, List<Dictionary<string, object?>>>();

        if (await db.HasTableAsync("purchase_invoices"))
        {
            if (allLinkedInvIds.Count > 0)
            {
                var linkedInvs = await db.QueryListAsync(
                    "SELECT id, supplier_id, invoice_number, posted_number, total_amount, total_paid, status, posted_date FROM purchase_invoices WHERE id IN @invIds",
                    new { invIds = allLinkedInvIds.ToArray() });
                foreach (var li in linkedInvs)
                {
                    pInvMap[AccountService.ToLong(li["id"])] = li;
                }
            }

            var unlinkedSql = @"
                SELECT id, supplier_id, total_amount, total_paid
                FROM purchase_invoices
                WHERE supplier_id IN @ids
                  AND invoice_type = 'credit'
                  AND (status IS NULL OR status != 'reversed')";
            if (!string.IsNullOrWhiteSpace(asOfDate)) unlinkedSql += " AND DATE(posted_date) <= @asOfDate";
            var candInvs = await db.QueryListAsync(unlinkedSql, new { ids, asOfDate });
            foreach (var uInv in candInvs)
            {
                var uId = AccountService.ToLong(uInv["id"]);
                if (allLinkedInvIds.Contains(uId)) continue;
                var sId = AccountService.ToLong(uInv["supplier_id"]);
                if (!unlinkedBySupplier.TryGetValue(sId, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    unlinkedBySupplier[sId] = list;
                }
                list.Add(uInv);
            }
        }

        var allInvIdsForAlloc = new HashSet<long>(allLinkedInvIds);
        foreach (var uList in unlinkedBySupplier.Values)
        {
            foreach (var uInv in uList)
            {
                allInvIdsForAlloc.Add(AccountService.ToLong(uInv["id"]));
            }
        }

        var allocatedByInvoice = new Dictionary<long, decimal>();
        if (allInvIdsForAlloc.Count > 0 && await db.HasTableAsync("payment_allocations") && await db.HasTableAsync("payments"))
        {
            var allocRows = await db.QueryListAsync(
                @"SELECT pa.invoice_id, SUM(pa.amount) as total_alloc
                  FROM payment_allocations pa
                  INNER JOIN payments p ON p.id = pa.payment_id
                  WHERE pa.invoice_type = 'purchase_invoice'
                    AND pa.invoice_id IN @invIds
                    AND p.status = 'posted'
                  GROUP BY pa.invoice_id",
                new { invIds = allInvIdsForAlloc.ToArray() });
            foreach (var ar in allocRows)
            {
                allocatedByInvoice[AccountService.ToLong(ar["invoice_id"])] = AccountService.ToDecimal(ar["total_alloc"]);
            }
        }

        foreach (var supplierId in ids)
        {
            rowsBySupplier.TryGetValue(supplierId, out var rows);
            rows ??= new List<Dictionary<string, object?>>();

            decimal totalInvoiced = 0m;
            decimal paidOnInvoice = 0m;
            decimal paymentsDebited = 0m;

            foreach (var row in rows)
            {
                var entryType = row.GetValueOrDefault("entry_type")?.ToString() ?? "";
                var pInvId = AccountService.ToLong(row.GetValueOrDefault("purchase_invoice_id"));

                if (entryType == "invoice" && pInvId > 0 && pInvMap.TryGetValue(pInvId, out var inv))
                {
                    if (string.Equals(inv.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var invTotal = AccountService.ToDecimal(inv.GetValueOrDefault("total_amount") ?? row.GetValueOrDefault("invoice_total"));
                    var rawPaid = AccountService.ToDecimal(inv.GetValueOrDefault("total_paid") ?? row.GetValueOrDefault("total_paid"));
                    var allocated = allocatedByInvoice.GetValueOrDefault(pInvId);
                    var paidOnInv = Math.Max(0m, Math.Min(rawPaid - allocated, invTotal));

                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType is "invoice" or "manual")
                {
                    var invTotal = AccountService.ToDecimal(row.GetValueOrDefault("invoice_total"));
                    var paidOnInv = Math.Min(AccountService.ToDecimal(row.GetValueOrDefault("total_paid")), invTotal);
                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType == "payment")
                {
                    paymentsDebited += AccountService.ToDecimal(row.GetValueOrDefault("debited_amount"));
                }
            }

            if (unlinkedBySupplier.TryGetValue(supplierId, out var unlinkedInvoices))
            {
                foreach (var uInv in unlinkedInvoices)
                {
                    var uId = AccountService.ToLong(uInv["id"]);
                    var uTotal = AccountService.ToDecimal(uInv.GetValueOrDefault("total_amount"));
                    var uRawPaid = AccountService.ToDecimal(uInv.GetValueOrDefault("total_paid"));
                    var uAlloc = allocatedByInvoice.GetValueOrDefault(uId);
                    var uPaidOnInv = Math.Max(0m, Math.Min(uRawPaid - uAlloc, uTotal));

                    totalInvoiced += uTotal;
                    paidOnInvoice += uPaidOnInv;
                }
            }

            var netBalance = (totalInvoiced - paidOnInvoice) - paymentsDebited;
            result[supplierId] = netBalance > 0m ? Math.Round(netBalance, 2) : 0m;
        }

        return result;
    }

    public async Task<Dictionary<string, object?>> EnrichPaymentAsync(Dictionary<string, object?> payment)
    {
        var paymentId = AccountService.ToLong(payment.GetValueOrDefault("id"));
        var partyType = payment.GetValueOrDefault("party_type")?.ToString() ?? "customer";
        var partyId = AccountService.ToLong(payment.GetValueOrDefault("party_id"));

        if (partyType.Equals("customer", StringComparison.OrdinalIgnoreCase) && partyId > 0 && await _db.HasTableAsync("customers"))
        {
            payment["customer"] = await _db.QuerySingleOrDefaultAsync("SELECT id, name, phone, address FROM customers WHERE id = @partyId", new { partyId });
            payment["supplier"] = null;
        }
        else if (partyType.Equals("supplier", StringComparison.OrdinalIgnoreCase) && partyId > 0 && await _db.HasTableAsync("suppliers"))
        {
            payment["supplier"] = await _db.QuerySingleOrDefaultAsync("SELECT id, name, phone, address FROM suppliers WHERE id = @partyId", new { partyId });
            payment["customer"] = null;
        }

        var createdById = AccountService.ToLong(payment.GetValueOrDefault("created_by_id"));
        payment["creator"] = createdById > 0 && await _db.HasTableAsync("users")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @createdById", new { createdById })
            : null;

        var reversedById = AccountService.ToLong(payment.GetValueOrDefault("reversed_by_id"));
        payment["reversed_by"] = reversedById > 0 && await _db.HasTableAsync("users")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @reversedById", new { reversedById })
            : null;

        var accountId = AccountService.ToLong(payment.GetValueOrDefault("account_id"));
        payment["account"] = accountId > 0 && await _db.HasTableAsync("accounts")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name, code, type FROM accounts WHERE id = @accountId", new { accountId })
            : null;

        var allocations = await _db.HasTableAsync("payment_allocations")
            ? await _db.QueryListAsync("SELECT * FROM payment_allocations WHERE payment_id = @paymentId ORDER BY id ASC", new { paymentId })
            : new List<Dictionary<string, object?>>();

        foreach (var alloc in allocations)
        {
            var invType = alloc.GetValueOrDefault("invoice_type")?.ToString() ?? "";
            var invId = AccountService.ToLong(alloc.GetValueOrDefault("invoice_id"));
            if (invType == "sale_invoice" && invId > 0 && await _db.HasTableAsync("sale_invoices"))
            {
                alloc["sale_invoice"] = await _db.QuerySingleOrDefaultAsync(
                    "SELECT id, posted_number, date, total FROM sale_invoices WHERE id = @invId",
                    new { invId });
            }
            else if (invType == "purchase_invoice" && invId > 0 && await _db.HasTableAsync("purchase_invoices"))
            {
                alloc["purchase_invoice"] = await _db.QuerySingleOrDefaultAsync(
                    "SELECT id, posted_number, invoice_number, posted_date, total_amount FROM purchase_invoices WHERE id = @invId",
                    new { invId });
            }
        }
        payment["allocations"] = allocations;

        return payment;
    }

    private static List<Dictionary<string, object?>> ExtractAllocationsList(object? raw)
    {
        var list = new List<Dictionary<string, object?>>();
        if (raw == null) return list;

        if (raw is IEnumerable<Dictionary<string, object?>> typedList)
        {
            return typedList.ToList();
        }

        if (raw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in item.EnumerateObject())
                    {
                        d[p.Name] = KarobarDb.PrepareDbValue(p.Value);
                    }
                    list.Add(d);
                }
            }
        }
        else if (raw is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is Dictionary<string, object?> dict)
                {
                    list.Add(dict);
                }
            }
        }
        return list;
    }
}
