using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Karobar.Core.Entities;

[Table("job_batches")]
public class JobBatch
{
    [Key]
    [Column("id")]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [Column("total_jobs")]
    [JsonPropertyName("total_jobs")]
    public int TotalJobs { get; set; }

    [Column("pending_jobs")]
    [JsonPropertyName("pending_jobs")]
    public int PendingJobs { get; set; }

    [Column("failed_jobs")]
    [JsonPropertyName("failed_jobs")]
    public int FailedJobs { get; set; }

    [Column("failed_job_ids")]
    [JsonPropertyName("failed_job_ids")]
    public string FailedJobIds { get; set; } = string.Empty;

    [Column("options")]
    [JsonPropertyName("options")]
    public string? Options { get; set; }

    [Column("cancelled_at")]
    [JsonPropertyName("cancelled_at")]
    public long? CancelledAt { get; set; }

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [Column("finished_at")]
    [JsonPropertyName("finished_at")]
    public long? FinishedAt { get; set; }

}
