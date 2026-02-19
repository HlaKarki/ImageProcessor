namespace ImageProcessor.Data.Models.Domain;
using System.Text.Json;

public enum JobStatus { Pending, Processing, Completed, Finished, Error }
public enum JobAiStatus { Pending, Processing, Completed, Error, Skipped }

public class Job
{
    public Guid Id { get; set; }
    public Guid UserId {  get; set; }
    public User User { get; set; } = null!;
    public JobStatus Status { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    
    // Timestamps
    public DateTime CreatedAt { get; set; } // created at
    public DateTime? StartedAt { get; set; } // started at
    public DateTime? CompletedAt { get; set; }  // completed at
    
    // Error handling
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    
    // Results
    public JsonDocument? Thumbnails { get; set; } // thumbnails
    public JsonDocument? Optimized { get; set; } // optimized images
    public JsonDocument? Metadata { get; set; } // metadata

    // AI enrichment
    public JobAiStatus AiStatus { get; set; } = JobAiStatus.Pending;
    public DateTime? AiStartedAt { get; set; }
    public DateTime? AiCompletedAt { get; set; }
    public string? AiErrorMessage { get; set; }
    public int AiRetryCount { get; set; }
    public JsonDocument? AiAnalysis { get; set; }
}

// Create index on UserId
// Create index on Status
// Create index on CreatedAt
