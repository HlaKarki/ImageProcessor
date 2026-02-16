namespace ImageProcessor.Data.Models;
using System.Text.Json;

public enum JobStatus { Pending, Processing, Completed, Finished, Error }

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
}

// Create index on UserId
// Create index on Status
// Create index on CreatedAt
