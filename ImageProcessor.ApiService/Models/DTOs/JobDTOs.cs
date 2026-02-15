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