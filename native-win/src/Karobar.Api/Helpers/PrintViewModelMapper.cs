using System.Globalization;
using System.Text.Json;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Api.Helpers;

public static class PrintViewModelMapper
{
    public static async Task<PrintSettingViewModel> LoadPrintSettingAsync(KarobarDb db)
    {
        if (!await db.HasTableAsync("settings"))
            return new PrintSettingViewModel();

        var row = await db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
        return MapPrintSetting(row);
    }

    public static PrintSettingViewModel MapPrintSetting(IDictionary<string, object?>? row)
    {
        var vm = new PrintSettingViewModel();
        if (row == null) return vm;

        var storeName = Str(row, "store_name");
        if (!string.IsNullOrWhiteSpace(storeName)) vm.StoreName = storeName;
        vm.PhoneNumber = Str(row, "phone_number");
        vm.Address = Str(row, "address");
        vm.LicenseNumber = Str(row, "license_number");
        vm.Note = Str(row, "note");

        var logoPath = Str(row, "logo_path");
        if (string.IsNullOrWhiteSpace(logoPath)) logoPath = Str(row, "logo");
        if (!string.IsNullOrWhiteSpace(logoPath))
        {
            vm.LogoUrl = logoPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || logoPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || logoPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase)
                ? logoPath
                : "/storage/" + logoPath.TrimStart('/');
        }

        var pw = NullableInt(row, "print_paper_width_mm");
        if (pw is > 0) vm.PrintPaperWidthMm = pw;
        vm.PrintMarginTopMm = Int(row, "print_margin_top_mm", 0);
        vm.PrintMarginBottomMm = Int(row, "print_margin_bottom_mm", 4);
        vm.PrintMarginLeftMm = Int(row, "print_margin_left_mm", 0);
        vm.PrintMarginRightMm = Int(row, "print_margin_right_mm", 0);
        vm.PrintFontScale = Int(row, "print_font_scale", 100);
        vm.PrintContrastMode = NonEmpty(row, "print_contrast_mode", "standard");
        vm.PrintScaleMode = NonEmpty(row, "print_scale_mode", "auto");
        vm.PrintShowLogo = Bool(row, "print_show_logo", true);
        vm.SharePaperSize = NonEmpty(row, "share_paper_size", "a4");
        vm.ShareShowLogo = Bool(row, "share_show_logo", true);
        vm.ThermalTemplate = NonEmpty(row, "thermal_template", "standard");
        vm.A4Template = NonEmpty(row, "a4_template", "standard");

        vm.FbrEnabled = Bool(row, "fbr_enabled", false);
        vm.FbrEnvironment = NonEmpty(row, "fbr_environment", "sandbox");
        vm.FbrMode = NonEmpty(row, "fbr_mode", "tier1_pos_cloud");
        vm.FbrSellerNtn = Str(row, "fbr_seller_ntn");
        vm.FbrSellerStrn = Str(row, "fbr_seller_strn");
        vm.FbrPosId = NonEmpty(row, "fbr_pos_id", "966130");
        vm.FbrPosFeeEnabled = Bool(row, "fbr_pos_fee_enabled", false);
        vm.FbrPosFeeAmount = Dec(row, "fbr_pos_fee_amount", 1.00m);

        return vm;
    }

    public static PrintStickerConfigViewModel MapStickerConfig(IDictionary<string, object?>? settingRow)
    {
        var cfg = new PrintStickerConfigViewModel();
        if (settingRow == null || !settingRow.TryGetValue("dosage_sticker_config", out var raw) || raw == null)
            return cfg;

        Dictionary<string, JsonElement>? dict = null;
        try
        {
            if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in je.EnumerateObject()) dict[p.Name] = p.Value;
            }
            else if (raw is string s && !string.IsNullOrWhiteSpace(s))
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in doc.RootElement.EnumerateObject()) dict[p.Name] = p.Value.Clone();
                }
            }
            else if (raw is IDictionary<string, object?> map)
            {
                var json = JsonSerializer.Serialize(map);
                using var doc = JsonDocument.Parse(json);
                dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in doc.RootElement.EnumerateObject()) dict[p.Name] = p.Value.Clone();
            }
        }
        catch { return cfg; }

        if (dict == null) return cfg;

        string? JStr(string k) => dict.TryGetValue(k, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : (dict.TryGetValue(k, out el) && el.ValueKind != JsonValueKind.Null ? el.ToString() : null);
        int? JInt(string k) => dict.TryGetValue(k, out var el) ? (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var iv) ? iv : (int.TryParse(el.ToString(), out var iv2) ? iv2 : null)) : null;
        double? JDbl(string k) => dict.TryGetValue(k, out var el) ? (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var dv) ? dv : (double.TryParse(el.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dv2) ? dv2 : null)) : null;
        bool? JBool(string k)
        {
            if (!dict.TryGetValue(k, out var el)) return null;
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            var s = el.ToString().Trim().ToLowerInvariant();
            return s is "1" or "true" or "yes" or "on";
        }

        if (!string.IsNullOrWhiteSpace(JStr("template"))) cfg.Template = JStr("template")!;
        if (JInt("width_mm") is > 10 and <= 200) cfg.WidthMm = JInt("width_mm")!.Value;
        if (JInt("height_mm") is > 10 and <= 200) cfg.HeightMm = JInt("height_mm")!.Value;
        if (!string.IsNullOrWhiteSpace(JStr("printer_mode"))) cfg.PrinterMode = JStr("printer_mode")!;
        if (!string.IsNullOrWhiteSpace(JStr("preset_key"))) cfg.PresetKey = JStr("preset_key")!;
        if (!string.IsNullOrWhiteSpace(JStr("font_scale"))) cfg.FontScale = JStr("font_scale")!;
        if (!string.IsNullOrWhiteSpace(JStr("contrast_mode"))) cfg.ContrastMode = JStr("contrast_mode")!;
        if (!string.IsNullOrWhiteSpace(JStr("scale_mode"))) cfg.ScaleMode = JStr("scale_mode")!;
        if (JInt("margin_top") is int mt) cfg.MarginTopMm = mt;
        if (JInt("margin_bottom") is int mb) cfg.MarginBottomMm = mb;
        if (JInt("margin_left") is int ml) cfg.MarginLeftMm = ml;
        if (JInt("margin_right") is int mr) cfg.MarginRightMm = mr;
        if (JBool("show_shop_name") is bool b1) cfg.ShowShopName = b1;
        if (JBool("show_invoice_meta") is bool b2) cfg.ShowInvoiceMeta = b2;
        if (JBool("show_patient") is bool b3) cfg.ShowPatient = b3;
        if (JBool("show_quantity") is bool b4) cfg.ShowQuantity = b4;
        if (JBool("show_batch") is bool b5) cfg.ShowBatch = b5;
        if (JBool("show_expiry") is bool b6) cfg.ShowExpiry = b6;
        if (JBool("show_doctor") is bool b7) cfg.ShowDoctor = b7;
        if (JStr("footer_note") != null) cfg.FooterNote = JStr("footer_note")!;
        if (JDbl("font_size_header") is double fsh) cfg.FontSizeHeader = fsh;
        if (JDbl("font_size_patient") is double fsp) cfg.FontSizePatient = fsp;
        if (JDbl("font_size_product") is double fspr) cfg.FontSizeProduct = fspr;
        if (JDbl("font_size_dosage") is double fsd) cfg.FontSizeDosage = fsd;
        if (JDbl("font_size_footer") is double fsf) cfg.FontSizeFooter = fsf;
        if (JInt("font_weight_header") is int fwh) cfg.FontWeightHeader = fwh;
        if (JInt("font_weight_patient") is int fwp) cfg.FontWeightPatient = fwp;
        if (JInt("font_weight_product") is int fwpr) cfg.FontWeightProduct = fwpr;
        if (JInt("font_weight_dosage") is int fwd) cfg.FontWeightDosage = fwd;
        if (JInt("font_weight_footer") is int fwf) cfg.FontWeightFooter = fwf;

        return cfg;
    }

    public static Dictionary<string, string> QueryToDict(IQueryCollection query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            var val = kv.Value.ToString();
            if (!string.IsNullOrEmpty(val)) d[kv.Key] = val;
        }
        return d;
    }

    public static async Task<string> RenderDocumentHtmlAsync(
        KarobarDb db,
        PrintTemplateRenderer renderer,
        PrintInvoiceViewModel docVm,
        string? type,
        IDictionary<string, string>? queryOverrides = null)
    {
        var settingRow = await db.HasTableAsync("settings")
            ? await db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;
        var settingVm = MapPrintSetting(settingRow);
        var defaultPrinterType = settingRow?.GetValueOrDefault("printer_type")?.ToString()?.Trim().ToLowerInvariant() ?? "thermal";
        var effectiveType = string.IsNullOrWhiteSpace(type) ? defaultPrinterType : type.Trim().ToLowerInvariant();
        if (effectiveType == "a4")
        {
            return renderer.RenderA4SaleInvoice(settingVm.A4Template, docVm, settingVm, queryOverrides);
        }

        return renderer.RenderThermalSaleInvoice(settingVm.ThermalTemplate, docVm, settingVm, queryOverrides);
    }

    public static async Task<PrintInvoiceViewModel?> LoadPurchaseInvoicePrintVmAsync(KarobarDb db, long id)
    {
        if (!await db.HasTableAsync("purchase_invoices")) return null;
        var row = await db.QuerySingleOrDefaultAsync(
            """
            SELECT pi.*,
                   s.name AS supplier_name, s.phone AS supplier_phone, s.address AS supplier_address,
                   u.name AS user_name,
                   po.number AS po_number
            FROM purchase_invoices pi
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            LEFT JOIN users u ON u.id = pi.user_id
            LEFT JOIN purchase_orders po ON po.id = pi.purchase_order_id
            WHERE pi.id = @id
            LIMIT 1
            """,
            new { id });
        if (row == null) return null;

        var itemRows = await db.HasTableAsync("purchase_invoice_items")
            ? await db.QueryListAsync(
                """
                SELECT pii.*, p.name AS product_name, p.product_code, p.barcode
                FROM purchase_invoice_items pii
                LEFT JOIN products p ON p.id = pii.product_id
                WHERE pii.purchase_invoice_id = @id
                ORDER BY pii.id ASC
                """,
                new { id })
            : [];

        var invType = NonEmpty(row, "invoice_type", "credit");
        var vm = new PrintInvoiceViewModel
        {
            Id = Int(row, "id", (int)id),
            DocumentKind = "purchase_invoice",
            DocumentTitle = "PURCHASE INVOICE",
            PostedNumber = NonEmpty(row, "posted_number", $"PI-{id}"),
            Date = ParseDate(Str(row, "posted_date")),
            InvoiceType = invType,
            PaymentMethod = NonEmpty(row, "payment_method", "cash"),
            PartyLabel = "Supplier",
            PartyFallbackName = "Walk-in Supplier",
            StaffLabel = "Entered By",
            UserName = NullIfEmpty(Str(row, "user_name")),
            CustomerId = NullableInt(row, "supplier_id"),
            CustomerName = NullIfEmpty(Str(row, "supplier_name")),
            CustomerPhone = NullIfEmpty(Str(row, "supplier_phone")),
            CustomerAddress = NullIfEmpty(Str(row, "supplier_address")),
            ShowModeRow = true,
            ModeLabel = "Bill Type",
            ModeValue = $"{ToTitleLabel(invType)} Purchase",
            ReferenceLabel = "Supplier Bill",
            ReferenceValue = NullIfEmpty(Str(row, "invoice_number")),
            SecondaryRefLabel = "PO Ref",
            SecondaryRefValue = NullIfEmpty(Str(row, "po_number")),
            Remarks = NullIfEmpty(Str(row, "remarks")),
            GrossAmount = Dec(row, "invoice_amount", 0m),
            DiscountPercentage = Dec(row, "discount_percentage", 0m),
            DiscountAmount = Dec(row, "discount_amount", 0m),
            TaxPercentage = Dec(row, "tax_percentage", 0m),
            TaxAmount = Dec(row, "tax_amount", 0m),
            Total = Dec(row, "total_amount", 0m),
            TotalReceive = Dec(row, "total_paid", 0m),
            PaidLabel = "Amount Paid",
            BalanceLabel = "Balance Payable",
            TotalDueLabel = "Total Supplier Payable",
            A4TotalDueLabel = "Total Supplier Payable",
            ShowYouSaved = false,
            ClosingGreeting = "Goods received & verified against supplier bill."
        };

        decimal itemDiscSum = 0m;
        decimal grossSum = 0m;
        foreach (var it in itemRows)
        {
            var packQty = Dec(it, "pack_quantity", 0m);
            var unitQty = Dec(it, "unit_quantity", 0m);
            var totalQty = Dec(it, "quantity", 0m);
            var qty = packQty > 0 ? packQty : (unitQty > 0 ? unitQty : totalQty);
            var packPrice = Dec(it, "pack_purchase_price", 0m);
            var unitPrice = Dec(it, "unit_purchase_price", 0m);
            var price = packQty > 0 && packPrice > 0 ? packPrice : unitPrice;
            var packBonus = Dec(it, "pack_bonus", 0m);
            var unitBonus = Dec(it, "unit_bonus", 0m);
            var bonus = packBonus > 0 ? packBonus : unitBonus;
            var discPct = Dec(it, "item_discount_percentage", 0m);
            var rawLine = qty * price;
            var discAmt = Math.Round(rawLine * discPct / 100m, 2);
            var subTotal = Dec(it, "sub_total", Math.Round(rawLine - discAmt, 2));
            grossSum += rawLine;
            itemDiscSum += discAmt;

            vm.Items.Add(new PrintInvoiceItemViewModel
            {
                ProductName = NonEmpty(it, "product_name", "Item"),
                ProductCode = NullIfEmpty(Str(it, "product_code")),
                Barcode = NullIfEmpty(Str(it, "barcode")),
                BatchNumber = NullIfEmpty(Str(it, "batch")),
                Expiry = NullIfEmpty(Str(it, "expiry")),
                PackSize = Int(it, "pack_size", 1),
                Quantity = qty,
                BonusQuantity = bonus,
                Price = price,
                ItemDiscountPercentage = discPct,
                ItemDiscountAmount = discAmt,
                SubTotal = subTotal
            });
        }

        if (vm.GrossAmount <= 0 && grossSum > 0) vm.GrossAmount = grossSum;
        vm.ItemDiscountTotal = itemDiscSum;

        if (vm.CustomerId.HasValue && await db.HasTableAsync("supplier_ledgers"))
        {
            var balRow = await db.QuerySingleOrDefaultAsync(
                "SELECT COALESCE(SUM(invoice_total), 0) - COALESCE(SUM(total_paid), 0) - COALESCE(SUM(debited_amount), 0) AS net_bal FROM supplier_ledgers WHERE supplier_id = @sId",
                new { sId = vm.CustomerId.Value });
            var netBal = Dec(balRow ?? new Dictionary<string, object?>(), "net_bal", 0m);
            if (netBal > 0.009m) vm.CustomerTotalDue = Math.Round(netBal, 2);
        }

        return vm;
    }

    public static async Task<PrintInvoiceViewModel?> LoadSaleReturnPrintVmAsync(KarobarDb db, long id)
    {
        if (!await db.HasTableAsync("sale_returns")) return null;
        var row = await db.QuerySingleOrDefaultAsync(
            """
            SELECT sr.*,
                   c.name AS customer_name, c.phone AS customer_phone, c.address AS customer_address,
                   u.name AS user_name,
                   si.posted_number AS orig_invoice_number
            FROM sale_returns sr
            LEFT JOIN customers c ON c.id = sr.customer_id
            LEFT JOIN users u ON u.id = sr.user_id
            LEFT JOIN sale_invoices si ON si.id = sr.sale_invoice_id
            WHERE sr.id = @id
            LIMIT 1
            """,
            new { id });
        if (row == null) return null;

        var itemRows = await db.HasTableAsync("sale_return_items")
            ? await db.QueryListAsync(
                """
                SELECT sri.*, p.name AS product_name, p.product_code, p.barcode, p.pack_size
                FROM sale_return_items sri
                LEFT JOIN products p ON p.id = sri.product_id
                WHERE sri.sale_return_id = @id
                ORDER BY sri.id ASC
                """,
                new { id })
            : [];

        var settlementRaw = NonEmpty(row, "settlement_mode", "cash_refund");
        var total = Dec(row, "total", 0m);
        var credited = Dec(row, "credited_amount", 0m);
        var settled = credited > 0.009m
            ? credited
            : (string.Equals(settlementRaw, "cash_refund", StringComparison.OrdinalIgnoreCase) ? total : 0m);

        var vm = new PrintInvoiceViewModel
        {
            Id = Int(row, "id", (int)id),
            DocumentKind = "sale_return",
            DocumentTitle = "SALE RETURN / CREDIT NOTE",
            PostedNumber = NonEmpty(row, "posted_number", $"SR-{id}"),
            Date = ParseDate(Str(row, "date")),
            PaymentMethod = NonEmpty(row, "payment_method", "cash"),
            PartyLabel = "Customer",
            PartyFallbackName = "Walk-in Customer",
            StaffLabel = "Processed By",
            UserName = NullIfEmpty(Str(row, "user_name")),
            CustomerId = NullableInt(row, "customer_id"),
            CustomerName = NullIfEmpty(Str(row, "customer_name")),
            CustomerPhone = NullIfEmpty(Str(row, "customer_phone")),
            CustomerAddress = NullIfEmpty(Str(row, "customer_address")),
            ShowModeRow = true,
            ModeLabel = "Settlement",
            ModeValue = FormatSettlement(settlementRaw),
            ReferenceLabel = "Orig Invoice",
            ReferenceValue = NullIfEmpty(Str(row, "orig_invoice_number")),
            SecondaryRefLabel = "Refund Mode",
            SecondaryRefValue = FormatSettlement(settlementRaw),
            Remarks = NullIfEmpty(Str(row, "remarks")),
            GrossAmount = Dec(row, "gross_total", 0m),
            DiscountPercentage = Dec(row, "discount_percentage", 0m),
            DiscountAmount = Dec(row, "discount_amount", 0m),
            TaxPercentage = Dec(row, "tax_percentage", 0m),
            TaxAmount = Dec(row, "tax_amount", 0m),
            Total = total,
            TotalReceive = settled,
            PaidLabel = "Refunded / Credited",
            BalanceLabel = "Remaining Credit",
            NetTotalLabel = "NET RETURN TOTAL",
            A4NetTotalLabel = "NET CREDIT NOTE TOTAL",
            StatusOverrideText = string.Equals(settlementRaw, "cash_refund", StringComparison.OrdinalIgnoreCase) ? "REFUNDED" : "CREDITED",
            StatusOverrideKind = "paid",
            ShowYouSaved = false,
            FbrInvoiceNumber = NullIfEmpty(Str(row, "fbr_invoice_number")),
            FbrUsin = NullIfEmpty(Str(row, "fbr_usin")),
            FbrQrData = NullIfEmpty(Str(row, "fbr_qr_data")),
            ClosingGreeting = "Return processed. Please retain this credit note."
        };

        decimal itemDiscSum = 0m;
        decimal grossSum = 0m;
        foreach (var it in itemRows)
        {
            var qty = Dec(it, "unit_return_quantity", 0m);
            var price = Dec(it, "unit_sale_price", 0m);
            var discPct = Dec(it, "item_discount_percentage", 0m);
            var rawLine = qty * price;
            var discAmt = Math.Round(rawLine * discPct / 100m, 2);
            var subTotal = Dec(it, "sub_total", Math.Round(rawLine - discAmt, 2));
            grossSum += rawLine;
            itemDiscSum += discAmt;

            vm.Items.Add(new PrintInvoiceItemViewModel
            {
                ProductName = NonEmpty(it, "product_name", "Item"),
                ProductCode = NullIfEmpty(Str(it, "product_code")),
                Barcode = NullIfEmpty(Str(it, "barcode")),
                BatchNumber = NullIfEmpty(Str(it, "batch_number")),
                Expiry = NullIfEmpty(Str(it, "expiry")),
                PackSize = Int(it, "pack_size", 1),
                Quantity = qty,
                Price = price,
                ItemDiscountPercentage = discPct,
                ItemDiscountAmount = discAmt,
                SubTotal = subTotal
            });
        }

        if (vm.GrossAmount <= 0 && grossSum > 0) vm.GrossAmount = grossSum;
        vm.ItemDiscountTotal = itemDiscSum;
        return vm;
    }

    public static async Task<PrintInvoiceViewModel?> LoadPurchaseReturnPrintVmAsync(KarobarDb db, long id)
    {
        if (!await db.HasTableAsync("purchase_returns")) return null;
        var row = await db.QuerySingleOrDefaultAsync(
            """
            SELECT pr.*,
                   s.name AS supplier_name, s.phone AS supplier_phone, s.address AS supplier_address,
                   u.name AS user_name,
                   pi.posted_number AS orig_purchase_number
            FROM purchase_returns pr
            LEFT JOIN suppliers s ON s.id = pr.supplier_id
            LEFT JOIN users u ON u.id = pr.user_id
            LEFT JOIN purchase_invoices pi ON pi.id = pr.purchase_invoice_id
            WHERE pr.id = @id
            LIMIT 1
            """,
            new { id });
        if (row == null) return null;

        var itemRows = await db.HasTableAsync("purchase_return_items")
            ? await db.QueryListAsync(
                """
                SELECT pri.*, p.name AS product_name, p.product_code, p.barcode
                FROM purchase_return_items pri
                LEFT JOIN products p ON p.id = pri.product_id
                WHERE pri.purchase_return_id = @id
                ORDER BY pri.id ASC
                """,
                new { id })
            : [];

        var settlementRaw = NonEmpty(row, "settlement_mode", "cash_refund");
        var total = Dec(row, "total", 0m);
        var credited = Dec(row, "credited_amount", 0m);
        var settled = credited > 0.009m
            ? credited
            : (string.Equals(settlementRaw, "cash_refund", StringComparison.OrdinalIgnoreCase) ? total : 0m);

        var vm = new PrintInvoiceViewModel
        {
            Id = Int(row, "id", (int)id),
            DocumentKind = "purchase_return",
            DocumentTitle = "PURCHASE RETURN / DEBIT NOTE",
            PostedNumber = NonEmpty(row, "posted_number", $"PR-{id}"),
            Date = ParseDate(Str(row, "date")),
            PaymentMethod = NonEmpty(row, "payment_method", "cash"),
            PartyLabel = "Supplier",
            PartyFallbackName = "Walk-in Supplier",
            StaffLabel = "Returned By",
            UserName = NullIfEmpty(Str(row, "user_name")),
            CustomerId = NullableInt(row, "supplier_id"),
            CustomerName = NullIfEmpty(Str(row, "supplier_name")),
            CustomerPhone = NullIfEmpty(Str(row, "supplier_phone")),
            CustomerAddress = NullIfEmpty(Str(row, "supplier_address")),
            ShowModeRow = true,
            ModeLabel = "Settlement",
            ModeValue = FormatSettlement(settlementRaw),
            ReferenceLabel = "Orig Purchase",
            ReferenceValue = NullIfEmpty(Str(row, "orig_purchase_number")),
            SecondaryRefLabel = "Adjustment",
            SecondaryRefValue = FormatSettlement(settlementRaw),
            Remarks = NullIfEmpty(Str(row, "remarks")),
            GrossAmount = Dec(row, "gross_total", 0m),
            DiscountPercentage = Dec(row, "discount_percentage", 0m),
            DiscountAmount = Dec(row, "discount_amount", 0m),
            TaxPercentage = Dec(row, "tax_percentage", 0m),
            TaxAmount = Dec(row, "tax_amount", 0m),
            Total = total,
            TotalReceive = settled,
            PaidLabel = "Credited / Settled",
            BalanceLabel = "Pending Claim",
            NetTotalLabel = "NET DEBIT TOTAL",
            A4NetTotalLabel = "NET DEBIT NOTE TOTAL",
            StatusOverrideText = string.Equals(settlementRaw, "cash_refund", StringComparison.OrdinalIgnoreCase) ? "REFUNDED" : "CREDITED",
            StatusOverrideKind = "paid",
            ShowYouSaved = false,
            ClosingGreeting = "Debit note issued to supplier."
        };

        decimal itemDiscSum = 0m;
        decimal grossSum = 0m;
        foreach (var it in itemRows)
        {
            var packQty = Dec(it, "return_pack_quantity", 0m);
            var unitQty = Dec(it, "return_unit_quantity", 0m);
            var qty = packQty > 0 ? packQty : unitQty;
            var packPrice = Dec(it, "pack_purchase_price", 0m);
            var unitPrice = Dec(it, "unit_purchase_price", 0m);
            var price = packQty > 0 && packPrice > 0 ? packPrice : unitPrice;
            var discPct = Dec(it, "item_discount_percentage", 0m);
            var rawLine = qty * price;
            var discAmt = Math.Round(rawLine * discPct / 100m, 2);
            var subTotal = Dec(it, "sub_total", Math.Round(rawLine - discAmt, 2));
            grossSum += rawLine;
            itemDiscSum += discAmt;

            vm.Items.Add(new PrintInvoiceItemViewModel
            {
                ProductName = NonEmpty(it, "product_name", "Item"),
                ProductCode = NullIfEmpty(Str(it, "product_code")),
                Barcode = NullIfEmpty(Str(it, "barcode")),
                BatchNumber = NullIfEmpty(Str(it, "batch")),
                Expiry = NullIfEmpty(Str(it, "expiry")),
                PackSize = Int(it, "pack_size", 1),
                Quantity = qty,
                Price = price,
                ItemDiscountPercentage = discPct,
                ItemDiscountAmount = discAmt,
                SubTotal = subTotal
            });
        }

        if (vm.GrossAmount <= 0 && grossSum > 0) vm.GrossAmount = grossSum;
        vm.ItemDiscountTotal = itemDiscSum;
        return vm;
    }

    public static async Task<PrintInvoiceViewModel?> LoadPurchaseOrderPrintVmAsync(KarobarDb db, long id)
    {
        if (!await db.HasTableAsync("purchase_orders")) return null;
        var row = await db.QuerySingleOrDefaultAsync(
            """
            SELECT po.*, s.name AS supplier_name, s.phone AS supplier_phone, s.address AS supplier_address
            FROM purchase_orders po
            LEFT JOIN suppliers s ON s.id = po.supplier_id
            WHERE po.id = @id
            LIMIT 1
            """,
            new { id });
        if (row == null) return null;

        var itemRows = await db.HasTableAsync("purchase_order_items")
            ? await db.QueryListAsync(
                """
                SELECT poi.*, p.name AS product_name, p.product_code, p.barcode
                FROM purchase_order_items poi
                LEFT JOIN products p ON p.id = poi.product_id
                WHERE poi.purchase_order_id = @id
                ORDER BY poi.id ASC
                """,
                new { id })
            : [];

        var statusRaw = NonEmpty(row, "status", "ordered");
        var statusUpper = statusRaw.Trim().ToUpperInvariant();
        var statusKind = statusRaw.Trim().ToLowerInvariant() switch
        {
            "received" or "completed" => "paid",
            "cancelled" => "credit",
            _ => "partial"
        };

        var vm = new PrintInvoiceViewModel
        {
            Id = Int(row, "id", (int)id),
            DocumentKind = "purchase_order",
            DocumentTitle = "PURCHASE ORDER",
            PostedNumber = NonEmpty(row, "number", $"PO-{id}"),
            Date = ParseDate(Str(row, "date")),
            PartyLabel = "Supplier",
            PartyFallbackName = "Supplier",
            StaffLabel = "Prepared By",
            UserName = null,
            CustomerId = NullableInt(row, "supplier_id"),
            CustomerName = NullIfEmpty(Str(row, "supplier_name")),
            CustomerPhone = NullIfEmpty(Str(row, "supplier_phone")),
            CustomerAddress = NullIfEmpty(Str(row, "supplier_address")),
            ShowModeRow = true,
            ModeLabel = "Order Status",
            ModeValue = ToTitleLabel(statusRaw),
            ShowPayRow = false,
            ShowPaymentBreakdown = false,
            ShowYouSaved = false,
            ReferenceLabel = "Order Ref",
            ReferenceValue = NullIfEmpty(Str(row, "reference")),
            SecondaryRefLabel = "Expected",
            SecondaryRefValue = FormatShortDate(Str(row, "expected_received_date")),
            Remarks = NullIfEmpty(Str(row, "notes")),
            NetTotalLabel = "EXPECTED ORDER TOTAL",
            A4NetTotalLabel = "EXPECTED ORDER TOTAL",
            StatusOverrideText = statusUpper,
            StatusOverrideKind = statusKind,
            ClosingGreeting = "Authorized Purchase Order — subject to quantity & expiry verification upon delivery."
        };

        decimal grossSum = 0m;
        decimal itemDiscSum = 0m;
        decimal netSum = 0m;
        foreach (var it in itemRows)
        {
            var qty = Dec(it, "ordered_quantity", 0m);
            var price = Dec(it, "unit_purchase_price", 0m);
            var discPct = Dec(it, "item_discount_percentage", 0m);
            var rawLine = qty * price;
            var discAmt = Math.Round(rawLine * discPct / 100m, 2);
            var subTotal = Math.Round(rawLine - discAmt, 2);
            grossSum += rawLine;
            itemDiscSum += discAmt;
            netSum += subTotal;

            vm.Items.Add(new PrintInvoiceItemViewModel
            {
                ProductName = NonEmpty(it, "product_name", "Item"),
                ProductCode = NullIfEmpty(Str(it, "product_code")),
                Barcode = NullIfEmpty(Str(it, "barcode")),
                PackSize = Int(it, "pack_size", 1),
                Quantity = qty,
                BonusQuantity = Dec(it, "unit_bonus", 0m),
                Price = price,
                ItemDiscountPercentage = discPct,
                ItemDiscountAmount = discAmt,
                SubTotal = subTotal
            });
        }

        vm.GrossAmount = grossSum;
        vm.ItemDiscountTotal = itemDiscSum;
        vm.Total = netSum;
        return vm;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.Now;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt;
        return DateTime.Now;
    }

    private static string? FormatShortDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        return raw.Trim();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string ToTitleLabel(string? raw)
    {
        var s = (raw ?? "").Replace("_", " ").Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(s) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);
    }

    private static string FormatSettlement(string? raw)
    {
        var norm = (raw ?? "").Trim().ToLowerInvariant();
        return norm switch
        {
            "cash_refund" => "Cash Refund",
            "ledger_credit" => "Ledger Credit",
            "account_credit" => "Account Credit",
            _ => ToTitleLabel(raw)
        };
    }

    private static string Str(IDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "" : "";

    private static string NonEmpty(IDictionary<string, object?> row, string key, string fallback)
    {
        var s = Str(row, key).Trim();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    private static int Int(IDictionary<string, object?> row, string key, int fallback) =>
        NullableInt(row, key) ?? fallback;

    private static int? NullableInt(IDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var v) || v == null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is double d) return (int)d;
        if (v is decimal m) return (int)m;
        return int.TryParse(v.ToString(), out var parsed) ? parsed : null;
    }

    private static decimal Dec(IDictionary<string, object?> row, string key, decimal fallback)
    {
        if (!row.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is decimal m) return m;
        if (v is double d) return (decimal)d;
        if (v is long l) return l;
        if (v is int i) return i;
        return decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : fallback;
    }

    private static bool Bool(IDictionary<string, object?> row, string key, bool fallback)
    {
        if (!row.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is bool b) return b;
        if (v is int i) return i != 0;
        if (v is long l) return l != 0;
        var s = v.ToString()?.Trim().ToLowerInvariant() ?? "";
        if (s is "1" or "true" or "yes" or "on") return true;
        if (s is "0" or "false" or "no" or "off") return false;
        return fallback;
    }
}
