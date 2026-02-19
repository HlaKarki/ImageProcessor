namespace ImageProcessor.ApiService.Models.DTOs;

public record JobMetadataResponse(
    int Width,
    int Height,
    string Format,
    long FileSize,
    Dictionary<string, string?> Exif,
    string[] DominantColors
);

public record JobResponse(
    Guid Id,
    Guid UserId,
    string Status,
    string OriginalUrl,
    string OriginalFilename,
    long FileSize,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    int RetryCount,
    Dictionary<string, string>? Thumbnails,
    Dictionary<string, string>? Optimized,
    JobMetadataResponse? Metadata
);
