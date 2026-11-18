using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("doctors")]
public class Doctor
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("pmdc_number")]
    [JsonPropertyName("pmdc_number")]
    public string? PmdcNumber { get; set; }

    [Column("hospital_clinic")]
    [JsonPropertyName("hospital_clinic")]
    public string? HospitalClinic { get; set; }

    [Column("specialization")]
    [JsonPropertyName("specialization")]
    public string? Specialization { get; set; }

    [Column("phone")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [Column("address")]
    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [Column("is_active")]
    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public virtual ICollection<SaleInvoice> SaleInvoices { get; set; } = new List<SaleInvoice>();
}
