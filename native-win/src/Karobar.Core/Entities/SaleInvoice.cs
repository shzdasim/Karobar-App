using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("sale_invoices")]
public class SaleInvoice
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [Column("customer_id")]
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [Column("posted_number")]
    [JsonPropertyName("posted_number")]
    public string PostedNumber { get; set; } = string.Empty;

    [Column("date")]
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [Column("remarks")]
    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [Column("doctor_name")]
    [JsonPropertyName("doctor_name")]
    public string? DoctorName { get; set; }

    [Column("patient_name")]
    [JsonPropertyName("patient_name")]
    public string? PatientName { get; set; }

    [Column("discount_percentage")]
    [JsonPropertyName("discount_percentage")]
    public decimal? DiscountPercentage { get; set; } = 0m;

    [Column("discount_amount")]
    [JsonPropertyName("discount_amount")]
    public decimal? DiscountAmount { get; set; } = 0m;

    [Column("tax_percentage")]
    [JsonPropertyName("tax_percentage")]
    public decimal? TaxPercentage { get; set; } = 0m;

    [Column("tax_amount")]
    [JsonPropertyName("tax_amount")]
    public decimal? TaxAmount { get; set; } = 0m;

    [Column("item_discount")]
    [JsonPropertyName("item_discount")]
    public decimal? ItemDiscount { get; set; } = 0m;

    [Column("gross_amount")]
    [JsonPropertyName("gross_amount")]
    public decimal GrossAmount { get; set; }

    [Column("total")]
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("total_receive")]
    [JsonPropertyName("total_receive")]
    public decimal TotalReceive { get; set; }

    [Column("invoice_type")]
    [JsonPropertyName("invoice_type")]
    public string InvoiceType { get; set; } = "debit";

    [Column("sale_type")]
    [JsonPropertyName("sale_type")]
    public string SaleType { get; set; } = "retail";

    [Column("wholesale_type")]
    [JsonPropertyName("wholesale_type")]
    public string WholesaleType { get; set; } = "unit";

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "posted";

    [Column("reversed_at")]
    [JsonPropertyName("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by_id")]
    [JsonPropertyName("reversed_by_id")]
    public int? ReversedById { get; set; }

    [Column("reversal_reason")]
    [JsonPropertyName("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("payment_method")]
    [JsonPropertyName("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("due_date")]
    [JsonPropertyName("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("account_id")]
    [JsonPropertyName("account_id")]
    public int? AccountId { get; set; }

    [Column("salesman_id")]
    [JsonPropertyName("salesman_id")]
    public int? SalesmanId { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [Column("doctor_pmdc")]
    [JsonPropertyName("doctor_pmdc")]
    public string? DoctorPmdc { get; set; }

    [Column("doctor_hospital")]
    [JsonPropertyName("doctor_hospital")]
    public string? DoctorHospital { get; set; }

    [Column("patient_cnic")]
    [JsonPropertyName("patient_cnic")]
    public string? PatientCnic { get; set; }

    [Column("patient_address")]
    [JsonPropertyName("patient_address")]
    public string? PatientAddress { get; set; }

    [Column("prescription_no")]
    [JsonPropertyName("prescription_no")]
    public string? PrescriptionNo { get; set; }

    [Column("pharmacist_name")]
    [JsonPropertyName("pharmacist_name")]
    public string? PharmacistName { get; set; }

    [Column("doctor_id")]
    [JsonPropertyName("doctor_id")]
    public int? DoctorId { get; set; }

    [Column("fbr_invoice_number")]
    [JsonPropertyName("fbr_invoice_number")]
    public string? FbrInvoiceNumber { get; set; }

    [Column("fbr_usin")]
    [JsonPropertyName("fbr_usin")]
    public string? FbrUsin { get; set; }

    [Column("fbr_qr_data")]
    [JsonPropertyName("fbr_qr_data")]
    public string? FbrQrData { get; set; }

    [Column("fbr_sync_status")]
    [JsonPropertyName("fbr_sync_status")]
    public string FbrSyncStatus { get; set; } = "not_applicable";

    [Column("fbr_synced_at")]
    [JsonPropertyName("fbr_synced_at")]
    public DateTime? FbrSyncedAt { get; set; }

    [Column("fbr_error_message")]
    [JsonPropertyName("fbr_error_message")]
    public string? FbrErrorMessage { get; set; }

    [Column("fbr_response_payload")]
    [JsonPropertyName("fbr_response_payload")]
    public string? FbrResponsePayload { get; set; }

    [Column("fbr_pos_fee")]
    [JsonPropertyName("fbr_pos_fee")]
    public decimal FbrPosFee { get; set; }

    [Column("further_tax_amount")]
    [JsonPropertyName("further_tax_amount")]
    public decimal FurtherTaxAmount { get; set; }

    [Column("advance_wht_percentage")]
    [JsonPropertyName("advance_wht_percentage")]
    public decimal AdvanceWhtPercentage { get; set; }

    [Column("advance_wht_amount")]
    [JsonPropertyName("advance_wht_amount")]
    public decimal AdvanceWhtAmount { get; set; }

    [Column("prescription_image_path")]
    [JsonPropertyName("prescription_image_path")]
    public string? PrescriptionImagePath { get; set; }

    [NotMapped]
    [JsonPropertyName("prescription_image_url")]
    public string? PrescriptionImageUrl =>
        string.IsNullOrWhiteSpace(PrescriptionImagePath)
            ? null
            : (PrescriptionImagePath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase)
                ? PrescriptionImagePath
                : "/storage/" + PrescriptionImagePath.TrimStart('/'));

    [ForeignKey(nameof(ReversedById))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? ReversedBy { get; set; }

    [ForeignKey(nameof(CustomerId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Customer? Customer { get; set; }

    [ForeignKey(nameof(DoctorId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Doctor? Doctor { get; set; }

    [ForeignKey(nameof(AccountId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Account? Account { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }

    [ForeignKey(nameof(SalesmanId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Employee? Salesman { get; set; }

    [ForeignKey(nameof(TerminalId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Terminal? Terminal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleInvoiceItem> Items { get; set; } = new List<SaleInvoiceItem>();
}
