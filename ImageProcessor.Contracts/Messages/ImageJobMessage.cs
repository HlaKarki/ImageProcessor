namespace ImageProcessor.Contracts.Messages;

public record ImageJobMessage(
    Guid JobId,
    Guid UserId,
    string OriginalUrl,
    string OriginalFilename,
    string MimeType
);