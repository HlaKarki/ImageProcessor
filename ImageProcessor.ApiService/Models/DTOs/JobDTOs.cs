namespace ImageProcessor.ApiService.Models.DTOs;

public record JobMetadataResponse(
    int Width,
    int Height,
    string Format,
    long FileSize,
    Dictionary<string, string?> Exif,
    string[] DominantColors
);

public record JobAiTagResponse(
    string Label,
    double Confidence
);

public record JobAiSafetyResponse(
    bool Adult,
    bool Violence,
    bool SelfHarm
);

public record JobAiMetaResponse(
    string Model,
    int LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    double? EstimatedCostUsd
);

public record JobAiAnalysisResponse(
    string Summary,
    string? OcrText,
    IEnumerable<JobAiTagResponse> Tags,
    JobAiSafetyResponse Safety,
    JobAiMetaResponse Meta
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
    JobMetadataResponse? Metadata,
    string AiStatus,
    DateTime? AiStartedAt,
    DateTime? AiCompletedAt,
    string? AiErrorMessage,
    int AiRetryCount,
    JobAiAnalysisResponse? AiAnalysis
);
