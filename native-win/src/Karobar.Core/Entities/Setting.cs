using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("settings")]
public class Setting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("store_name")]
    [JsonPropertyName("store_name")]
    public string? StoreName { get; set; }

    [Column("logo_path")]
    [JsonPropertyName("logo_path")]
    public string? LogoPath { get; set; }

    [Column("phone_number")]
    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [Column("address")]
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [Column("license_number")]
    [JsonPropertyName("license_number")]
    public string? LicenseNumber { get; set; }

    [Column("note")]
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [Column("printer_type")]
    [JsonPropertyName("printer_type")]
    public string PrinterType { get; set; } = "thermal";

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("thermal_template")]
    [JsonPropertyName("thermal_template")]
    public string ThermalTemplate { get; set; } = "standard";

    [Column("navigation_style")]
    [JsonPropertyName("navigation_style")]
    public string NavigationStyle { get; set; } = "sidebar";

    [Column("sale_system")]
    [JsonPropertyName("sale_system")]
    public string SaleSystem { get; set; } = "retail_wholesale";

    [Column("a4_template")]
    [JsonPropertyName("a4_template")]
    public string? A4Template { get; set; } = "standard";

    [Column("shop_type")]
    [JsonPropertyName("shop_type")]
    public string ShopType { get; set; } = "pharmacy";

    [Column("thermal_custom_layout")]
    [JsonPropertyName("thermal_custom_layout")]
    public string? ThermalCustomLayout { get; set; }

    [Column("currency_code")]
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = "PKR";

    [Column("currency_symbol")]
    [JsonPropertyName("currency_symbol")]
    public string CurrencySymbol { get; set; } = "Rs.";

    [Column("currency_position")]
    [JsonPropertyName("currency_position")]
    public string CurrencyPosition { get; set; } = "before";

    [Column("currency_decimal_places")]
    [JsonPropertyName("currency_decimal_places")]
    public int CurrencyDecimalPlaces { get; set; } = 2;

    [Column("expiry_warning_days")]
    [JsonPropertyName("expiry_warning_days")]
    public int ExpiryWarningDays { get; set; } = 90;

    [Column("smtp_enabled")]
    [JsonPropertyName("smtp_enabled")]
    public bool SmtpEnabled { get; set; }

    [Column("smtp_host")]
    [JsonPropertyName("smtp_host")]
    public string? SmtpHost { get; set; }

    [Column("smtp_port")]
    [JsonPropertyName("smtp_port")]
    public int? SmtpPort { get; set; } = 587;

    [Column("smtp_username")]
    [JsonPropertyName("smtp_username")]
    public string? SmtpUsername { get; set; }

    [Column("smtp_password")]
    [JsonPropertyName("smtp_password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmtpPassword { get; set; }

    [Column("smtp_encryption")]
    [JsonPropertyName("smtp_encryption")]
    public string? SmtpEncryption { get; set; } = "tls";

    [Column("smtp_from_address")]
    [JsonPropertyName("smtp_from_address")]
    public string? SmtpFromAddress { get; set; }

    [Column("smtp_from_name")]
    [JsonPropertyName("smtp_from_name")]
    public string? SmtpFromName { get; set; }

    [Column("cube_ai_enabled")]
    [JsonPropertyName("cube_ai_enabled")]
    public bool CubeAiEnabled { get; set; } = true;

    [Column("cube_groq_enabled")]
    [JsonPropertyName("cube_groq_enabled")]
    public bool CubeGroqEnabled { get; set; } = true;

    [Column("cube_groq_api_key")]
    [JsonPropertyName("cube_groq_api_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CubeGroqApiKey { get; set; }

    [Column("cube_groq_model")]
    [JsonPropertyName("cube_groq_model")]
    public string? CubeGroqModel { get; set; } = "llama-3.3-70b-versatile";

    [Column("cube_groq_daily_limit")]
    [JsonPropertyName("cube_groq_daily_limit")]
    public int? CubeGroqDailyLimit { get; set; } = 250;

    [Column("cube_gemini_enabled")]
    [JsonPropertyName("cube_gemini_enabled")]
    public bool CubeGeminiEnabled { get; set; } = true;

    [Column("cube_gemini_api_key")]
    [JsonPropertyName("cube_gemini_api_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CubeGeminiApiKey { get; set; }

    [Column("cube_gemini_model")]
    [JsonPropertyName("cube_gemini_model")]
    public string? CubeGeminiModel { get; set; } = "gemini-3.8-flash";

    [Column("cube_gemini_daily_limit")]
    [JsonPropertyName("cube_gemini_daily_limit")]
    public int? CubeGeminiDailyLimit { get; set; } = 250;

    [Column("whatsapp_enabled")]
    [JsonPropertyName("whatsapp_enabled")]
    public bool WhatsappEnabled { get; set; }

    [Column("whatsapp_provider")]
    [JsonPropertyName("whatsapp_provider")]
    public string? WhatsappProvider { get; set; } = "whapi_cloud";

    [Column("whatsapp_access_token")]
    [JsonPropertyName("whatsapp_access_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WhatsappAccessToken { get; set; }

    [Column("whatsapp_phone_number_id")]
    [JsonPropertyName("whatsapp_phone_number_id")]
    public string? WhatsappPhoneNumberId { get; set; }

    [Column("whatsapp_business_account_id")]
    [JsonPropertyName("whatsapp_business_account_id")]
    public string? WhatsappBusinessAccountId { get; set; }

    [Column("whatsapp_api_version")]
    [JsonPropertyName("whatsapp_api_version")]
    public string? WhatsappApiVersion { get; set; } = "v21.0";

    [Column("whatsapp_api_url")]
    [JsonPropertyName("whatsapp_api_url")]
    public string? WhatsappApiUrl { get; set; }

    [Column("whatsapp_default_country_code")]
    [JsonPropertyName("whatsapp_default_country_code")]
    public string? WhatsappDefaultCountryCode { get; set; } = "92";

    [Column("whatsapp_template_name")]
    [JsonPropertyName("whatsapp_template_name")]
    public string? WhatsappTemplateName { get; set; }

    [Column("whatsapp_template_language")]
    [JsonPropertyName("whatsapp_template_language")]
    public string? WhatsappTemplateLanguage { get; set; } = "en_US";

    [Column("whatsapp_auto_fallback")]
    [JsonPropertyName("whatsapp_auto_fallback")]
    public bool WhatsappAutoFallback { get; set; } = true;

    [Column("whatsapp_message_en")]
    [JsonPropertyName("whatsapp_message_en")]
    public string? WhatsappMessageEn { get; set; }

    [Column("whatsapp_message_ur")]
    [JsonPropertyName("whatsapp_message_ur")]
    public string? WhatsappMessageUr { get; set; }

    [Column("note_en")]
    [JsonPropertyName("note_en")]
    public string? NoteEn { get; set; }

    [Column("note_ur")]
    [JsonPropertyName("note_ur")]
    public string? NoteUr { get; set; }

    [Column("share_paper_size")]
    [JsonPropertyName("share_paper_size")]
    public string? SharePaperSize { get; set; } = "a4";

    [Column("share_show_logo")]
    [JsonPropertyName("share_show_logo")]
    public bool ShareShowLogo { get; set; } = true;

    [Column("is_setup_completed")]
    [JsonPropertyName("is_setup_completed")]
    public bool IsSetupCompleted { get; set; }

    [Column("setup_completed_at")]
    [JsonPropertyName("setup_completed_at")]
    public DateTime? SetupCompletedAt { get; set; }

    [Column("terms_accepted_at")]
    [JsonPropertyName("terms_accepted_at")]
    public DateTime? TermsAcceptedAt { get; set; }

    [Column("privacy_accepted_at")]
    [JsonPropertyName("privacy_accepted_at")]
    public DateTime? PrivacyAcceptedAt { get; set; }

    [Column("legal_accepted_version")]
    [JsonPropertyName("legal_accepted_version")]
    public string? LegalAcceptedVersion { get; set; }

    [Column("print_paper_width_mm")]
    [JsonPropertyName("print_paper_width_mm")]
    public int? PrintPaperWidthMm { get; set; }

    [Column("print_margin_top_mm")]
    [JsonPropertyName("print_margin_top_mm")]
    public int PrintMarginTopMm { get; set; }

    [Column("print_margin_bottom_mm")]
    [JsonPropertyName("print_margin_bottom_mm")]
    public int PrintMarginBottomMm { get; set; } = 4;

    [Column("print_margin_left_mm")]
    [JsonPropertyName("print_margin_left_mm")]
    public int PrintMarginLeftMm { get; set; }

    [Column("print_margin_right_mm")]
    [JsonPropertyName("print_margin_right_mm")]
    public int PrintMarginRightMm { get; set; }

    [Column("print_font_scale")]
    [JsonPropertyName("print_font_scale")]
    public int PrintFontScale { get; set; } = 100;

    [Column("print_contrast_mode")]
    [JsonPropertyName("print_contrast_mode")]
    public string PrintContrastMode { get; set; } = "standard";

    [Column("print_scale_mode")]
    [JsonPropertyName("print_scale_mode")]
    public string PrintScaleMode { get; set; } = "auto";

    [Column("print_show_logo")]
    [JsonPropertyName("print_show_logo")]
    public bool PrintShowLogo { get; set; } = true;

    [Column("expiry_critical_days")]
    [JsonPropertyName("expiry_critical_days")]
    public int ExpiryCriticalDays { get; set; } = 30;

    [Column("expiry_daily_flash_limit")]
    [JsonPropertyName("expiry_daily_flash_limit")]
    public int ExpiryDailyFlashLimit { get; set; } = 5;

    [Column("network_server_token")]
    [JsonPropertyName("network_server_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NetworkServerToken { get; set; }

    [Column("terminal_auto_authorize")]
    [JsonPropertyName("terminal_auto_authorize")]
    public bool TerminalAutoAuthorize { get; set; } = true;

    [Column("fbr_enabled")]
    [JsonPropertyName("fbr_enabled")]
    public bool FbrEnabled { get; set; }

    [Column("fbr_environment")]
    [JsonPropertyName("fbr_environment")]
    public string FbrEnvironment { get; set; } = "sandbox";

    [Column("fbr_mode")]
    [JsonPropertyName("fbr_mode")]
    public string FbrMode { get; set; } = "digital_invoicing";

    [Column("fbr_bearer_token")]
    [JsonPropertyName("fbr_bearer_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FbrBearerToken { get; set; }

    [Column("fbr_pos_id")]
    [JsonPropertyName("fbr_pos_id")]
    public string? FbrPosId { get; set; }

    [Column("fbr_seller_ntn")]
    [JsonPropertyName("fbr_seller_ntn")]
    public string? FbrSellerNtn { get; set; }

    [Column("fbr_seller_strn")]
    [JsonPropertyName("fbr_seller_strn")]
    public string? FbrSellerStrn { get; set; }

    [Column("fbr_seller_province")]
    [JsonPropertyName("fbr_seller_province")]
    public string FbrSellerProvince { get; set; } = "Punjab";

    [Column("fbr_sandbox_url")]
    [JsonPropertyName("fbr_sandbox_url")]
    public string? FbrSandboxUrl { get; set; }

    [Column("fbr_production_url")]
    [JsonPropertyName("fbr_production_url")]
    public string? FbrProductionUrl { get; set; }

    [Column("fbr_default_hs_code")]
    [JsonPropertyName("fbr_default_hs_code")]
    public string FbrDefaultHsCode { get; set; } = "3004.9099";

    [Column("fbr_scenario_id")]
    [JsonPropertyName("fbr_scenario_id")]
    public string FbrScenarioId { get; set; } = "SN001";

    [Column("fbr_pos_fee_enabled")]
    [JsonPropertyName("fbr_pos_fee_enabled")]
    public bool FbrPosFeeEnabled { get; set; }

    [Column("fbr_pos_fee_amount")]
    [JsonPropertyName("fbr_pos_fee_amount")]
    public decimal FbrPosFeeAmount { get; set; } = 1m;

    [Column("fbr_auto_sync")]
    [JsonPropertyName("fbr_auto_sync")]
    public bool FbrAutoSync { get; set; } = true;

    [Column("enable_dosage_stickers")]
    [JsonPropertyName("enable_dosage_stickers")]
    public bool EnableDosageStickers { get; set; } = true;

    [Column("enable_prescription_attachments")]
    [JsonPropertyName("enable_prescription_attachments")]
    public bool EnablePrescriptionAttachments { get; set; } = true;

    [Column("enable_drug_interaction_warnings")]
    [JsonPropertyName("enable_drug_interaction_warnings")]
    public bool EnableDrugInteractionWarnings { get; set; } = true;

    [Column("enable_cold_chain_log")]
    [JsonPropertyName("enable_cold_chain_log")]
    public bool EnableColdChainLog { get; set; } = true;

    [Column("dosage_sticker_config")]
    [JsonPropertyName("dosage_sticker_config")]
    public string? DosageStickerConfig { get; set; }

    [Column("require_operator_pin")]
    [JsonPropertyName("require_operator_pin")]
    public bool RequireOperatorPin { get; set; }

    [Column("operator_pin_auto_submit")]
    [JsonPropertyName("operator_pin_auto_submit")]
    public bool OperatorPinAutoSubmit { get; set; } = true;

    [Column("operator_pin_modules")]
    [JsonPropertyName("operator_pin_modules")]
    public string? OperatorPinModules { get; set; }

    [NotMapped]
    [JsonPropertyName("logo_url")]
    public string? LogoUrl =>
        string.IsNullOrWhiteSpace(LogoPath)
            ? null
            : (LogoPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase)
                ? LogoPath
                : "/storage/" + LogoPath.TrimStart('/'));

    public static Dictionary<string, object?> DefaultDosageStickerConfig() => new()
    {
        ["preset_key"] = "50x25",
        ["width_mm"] = 50,
        ["height_mm"] = 25,
        ["printer_mode"] = "receipt_roll",
        ["template"] = "classic",
        ["font_scale"] = "normal",
        ["font_size_header"] = 6.5,
        ["font_size_patient"] = 6.0,
        ["font_size_product"] = 8.0,
        ["font_size_dosage"] = 7.5,
        ["font_size_footer"] = 5.5,
        ["font_weight_header"] = 800,
        ["font_weight_patient"] = 600,
        ["font_weight_product"] = 900,
        ["font_weight_dosage"] = 800,
        ["font_weight_footer"] = 700,
        ["show_shop_name"] = true,
        ["show_invoice_meta"] = true,
        ["show_patient"] = true,
        ["show_quantity"] = true,
        ["show_batch"] = true,
        ["show_expiry"] = true,
        ["show_doctor"] = false,
        ["footer_note"] = "Keep out of reach of children",
        ["margin_top_mm"] = 0,
        ["margin_bottom_mm"] = 4,
        ["margin_left_mm"] = 0,
        ["margin_right_mm"] = 0,
        ["contrast_mode"] = "high_contrast",
        ["scale_mode"] = "auto",
        ["preferred_printer"] = ""
    };

    public static string[] DefaultOperatorPinModules() =>
    [
        "sale_invoice",
        "purchase_invoice",
        "sale_return",
        "purchase_return"
    ];
}
