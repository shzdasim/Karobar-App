using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("stock_disposals")]
public class StockDisposal
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("disposal_number")]
    [JsonPropertyName("disposal_number")]
    public string DisposalNumber { get; set; } = string.Empty;

    [Column("disposal_date")]
    [JsonPropertyName("disposal_date")]
    public DateTime DisposalDate { get; set; }

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "quarantined";

    [Column("cause")]
    [JsonPropertyName("cause")]
    public string Cause { get; set; } = string.Empty;

    [Column("disposal_method")]
    [JsonPropertyName("disposal_method")]
    public string DisposalMethod { get; set; } = "quarantine_holding";

    [Column("reference_no")]
    [JsonPropertyName("reference_no")]
    public string? ReferenceNo { get; set; }

    [Column("witness_name")]
    [JsonPropertyName("witness_name")]
    public string? WitnessName { get; set; }

    [Column("witness_designation")]
    [JsonPropertyName("witness_designation")]
    public string? WitnessDesignation { get; set; }

    [Column("notes")]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [Column("total_qty")]
    [JsonPropertyName("total_qty")]
    public decimal TotalQty { get; set; }

    [Column("total_cost_value")]
    [JsonPropertyName("total_cost_value")]
    public decimal TotalCostValue { get; set; }

    [Column("total_retail_value")]
    [JsonPropertyName("total_retail_value")]
    public decimal TotalRetailValue { get; set; }

    [Column("has_narcotics")]
    [JsonPropertyName("has_narcotics")]
    public bool HasNarcotics { get; set; }

    [Column("record_expense")]
    [JsonPropertyName("record_expense")]
    public bool RecordExpense { get; set; } = true;

    [Column("stock_adjustment_id")]
    [JsonPropertyName("stock_adjustment_id")]
    public int? StockAdjustmentId { get; set; }

    [Column("expense_id")]
    [JsonPropertyName("expense_id")]
    public int? ExpenseId { get; set; }

    [Column("created_by")]
    [JsonPropertyName("created_by")]
    public int? CreatedBy { get; set; }

    [Column("disposed_by")]
    [JsonPropertyName("disposed_by")]
    public int? DisposedBy { get; set; }

    [Column("disposed_at")]
    [JsonPropertyName("disposed_at")]
    public DateTime? DisposedAt { get; set; }

    [Column("restored_by")]
    [JsonPropertyName("restored_by")]
    public int? RestoredBy { get; set; }

    [Column("restored_at")]
    [JsonPropertyName("restored_at")]
    public DateTime? RestoredAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    public const string StatusQuarantined = "quarantined";
    public const string StatusDisposed = "disposed";
    public const string StatusRestored = "restored";

    public static readonly IReadOnlyDictionary<string, string> Causes = new Dictionary<string, string>
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

    public static readonly IReadOnlyDictionary<string, string> Methods = new Dictionary<string, string>
    {
        ["incineration"] = "High-Temperature Incineration",
        ["chemical_neutralization"] = "Chemical Neutralization / Encapsulation",
        ["municipal_waste"] = "Authorized Bio-Hazard / Municipal Waste",
        ["supplier_destruction"] = "Handed to Supplier (No Credit)",
        ["drain_flush"] = "Authorized Dilution & Drain Flush",
        ["quarantine_holding"] = "In Quarantine Bay (Pending Destruction)",
        ["other"] = "Other Method"
    };

    [NotMapped]
    [JsonPropertyName("cause_label")]
    public string CauseLabel =>
        Causes.TryGetValue(Cause ?? string.Empty, out var label)
            ? label
            : (Cause ?? string.Empty).Replace('_', ' ');

    [NotMapped]
    [JsonPropertyName("disposal_method_label")]
    public string DisposalMethodLabel =>
        Methods.TryGetValue(DisposalMethod ?? string.Empty, out var label)
            ? label
            : (DisposalMethod ?? string.Empty).Replace('_', ' ');

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<StockDisposalItem> Items { get; set; } = new List<StockDisposalItem>();

    [ForeignKey(nameof(StockAdjustmentId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual StockAdjustment? StockAdjustment { get; set; }

    [ForeignKey(nameof(ExpenseId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual Expense? Expense { get; set; }

    [ForeignKey(nameof(CreatedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Creator { get; set; }

    [ForeignKey(nameof(DisposedBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Disposer { get; set; }

    [ForeignKey(nameof(RestoredBy))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? Restorer { get; set; }
}
