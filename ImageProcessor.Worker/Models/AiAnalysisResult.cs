namespace ImageProcessor.Worker.Models;

public record AiTagResult(string Label, double Confidence);

public record AiSafetyResult(bool Adult, bool Violence, bool SelfHarm);

public record AiMetaResult(
    string Model,
    int LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    double? EstimatedCostUsd
);

public record AiAnalysisResult(
    string Summary,
    string? OcrText,
    IReadOnlyList<AiTagResult> Tags,
    AiSafetyResult Safety,
    AiMetaResult Meta
);
