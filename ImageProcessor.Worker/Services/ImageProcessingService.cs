using ImageProcessor.Worker.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageProcessor.Worker.Services;

public class ImageProcessingService(ILogger<ImageProcessingService> logger)
{
    private static readonly int[] ThumbnailSizes = [128, 512, 1024];

    public async Task<ProcessingResult> ProcessAsync(Stream imageStream, long fileSize)
    {
        logger.LogInformation($"Processing image, size: {fileSize} bytes");
        
        using var image = await Image.LoadAsync<Rgba32>(imageStream);

        var width = image.Width;
        var height = image.Height;
        var format = image.Metadata.DecodedImageFormat?.Name ?? "unknown";
        
        logger.LogInformation($"Extracting Exif");
        var exif = ExtractExif(image);
        logger.LogInformation($"Extracting DominantColors");
        var dominantColors = ExtractDominantColors(image);

        var thumbnails = new Dictionary<string, Stream>();
        foreach (var size in ThumbnailSizes)
        {
            logger.LogInformation($"Processing thumbnail: thumb-{size}");
            thumbnails[$"thumb-{size}"] = await GenerateThumbnailAsync(image, size);
        }

        var optimized = new Dictionary<string, Stream>();
        logger.LogInformation($"Processing optimized image");
        optimized["webp"] = await ConvertToWebPAsync(image);

        var metadata = new ImageMetadataResult(width, height, format, fileSize, exif, dominantColors);

        return new ProcessingResult(thumbnails, optimized, metadata);
    }

    private static Dictionary<string, string?> ExtractExif(Image image)
    {
        var exif = new Dictionary<string, string?>();
        var profile = image.Metadata.ExifProfile;
        if (profile is null) return exif;

        foreach (var value in profile.Values)
        {
            exif[value.Tag.ToString()] = value.GetValue()?.ToString();
        }
        
        return exif;
    }

    private static string[] ExtractDominantColors(Image<Rgba32> image, int colorCount = 5)
    {
        var colorCounts = new Dictionary<int, int>();
        var sampleStep = Math.Max(1, Math.Min(image.Width, image.Height) / 50);
        
        image.ProcessPixelRows(accessor =>
        {
            for (var row = 0; row < accessor.Height; row += sampleStep)
            {
                var rowSpan = accessor.GetRowSpan(row);
                for (var column = 0; column < rowSpan.Length; column += sampleStep)
                {
                    var pixel = rowSpan[column];
                    var red = (pixel.R / 32) * 32;
                    var green = (pixel.G / 32) * 32;
                    var blue = (pixel.B / 32) * 32;
                    var key = (red << 16) | (green << 8) | blue; // bit shift and merge
                    colorCounts[key] = colorCounts.GetValueOrDefault(key) + 1;
                }
            }
        });
        
        return colorCounts
            .OrderByDescending(kv => kv.Value)
            .Take(colorCount)
            .Select(kv => $"#{kv.Key:X6}") // X6; pad to 6 digits
            .ToArray();
    }

    private static async Task<Stream> GenerateThumbnailAsync(Image<Rgba32> source, int maxSize)
    {
        using var clone = source.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(maxSize, maxSize),
            Mode = ResizeMode.Max
        }));

        var ms = new MemoryStream();
        await clone.SaveAsWebpAsync(ms);
        ms.Position = 0;
        return ms;
    }

    private static async Task<Stream> ConvertToWebPAsync(Image<Rgba32> source)
    {
        var ms = new MemoryStream();
        await source.SaveAsWebpAsync(ms, new WebpEncoder { Quality = 80 });
        ms.Position = 0;
        return ms;
    }
}