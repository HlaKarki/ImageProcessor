using Microsoft.AspNetCore.Mvc;

namespace ImageProcessor.ApiService.Models.DTOs;

public record ImageUploadRequest(IFormFile file);

public static class ImageUploadConstants
{
    public static readonly string[] AllowedExtensions = [ ".jpeg", ".jpg", ".png", ".webp" ];

    public static readonly string[] AllowedMimeTypes = [ "image/jpeg", "image/png", "image/webp" ];

    public const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
}
