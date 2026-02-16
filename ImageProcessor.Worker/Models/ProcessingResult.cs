namespace ImageProcessor.Worker.Models;

public record ImageMetadataResult(
    int Width,
    int Height,
    string Format,
    long FileSize,
    Dictionary<string, string?> Exif,
    string[] DominantColors
);

public record ProcessingResult (
    Dictionary<string, Stream> Thumbnails,
    Dictionary<string, Stream> Optimized,
    ImageMetadataResult Metadata
);