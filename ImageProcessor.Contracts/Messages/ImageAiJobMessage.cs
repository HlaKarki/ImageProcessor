namespace ImageProcessor.Contracts.Messages;

public record ImageAiJobMessage(
    Guid JobId,
    Guid UserId,
    string SourceImageUrl
);
