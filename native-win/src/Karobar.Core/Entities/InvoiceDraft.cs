using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("invoice_drafts")]
public class InvoiceDraft
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("uuid")]
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [Column("type")]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [Column("title")]
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [Column("party_name")]
    [JsonPropertyName("party_name")]
    public string? PartyName { get; set; }

    [Column("total_amount")]
    [JsonPropertyName("total_amount")]
    public decimal TotalAmount { get; set; }

    [Column("items_count")]
    [JsonPropertyName("items_count")]
    public int ItemsCount { get; set; }

    [Column("version")]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [Column("payload")]
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = string.Empty;

    [Column("status")]
    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [Column("posted_invoice_id")]
    [JsonPropertyName("posted_invoice_id")]
    public int? PostedInvoiceId { get; set; }

    [Column("posted_invoice_type")]
    [JsonPropertyName("posted_invoice_type")]
    public string? PostedInvoiceType { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    [JsonPropertyName("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("terminal_id")]
    [JsonPropertyName("terminal_id")]
    public int? TerminalId { get; set; }

    [Column("terminal_name")]
    [JsonPropertyName("terminal_name")]
    public string? TerminalName { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual User? User { get; set; }
}
