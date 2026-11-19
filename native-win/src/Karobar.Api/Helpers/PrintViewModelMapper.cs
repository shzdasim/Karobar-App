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
