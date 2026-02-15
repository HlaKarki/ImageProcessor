namespace ImageProcessor.ApiService.Models.DTOs;

public record JobResponse(
    Guid Id,
    Guid UserId,
    string Status,
    string OriginalUrl,
    string OriginalFilename,
    long FileSize,
    DateTime CreatedAt
);

public record PaginatedJobResponse(
    IEnumerable<JobResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);