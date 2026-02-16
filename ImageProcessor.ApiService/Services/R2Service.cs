using Amazon.S3;
using Amazon.S3.Model;
using ImageProcessor.ApiService.Repositories.Storage;

namespace ImageProcessor.ApiService.Services;

public class R2Service(IAmazonS3 s3, IConfiguration configuration) : IStorageService
{
    private readonly string _bucket = configuration["CF:BucketName"]!;

    public async Task<string> UploadAsync(IFormFile file, string userId, string jobId)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"originals/{userId}/{jobId}{extension}";

        await using var stream = file.OpenReadStream();

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = file.ContentType,
            UseChunkEncoding = false
        };
        
        await s3.PutObjectAsync(request);

        return $"{configuration["CF:ServiceURL"]}/{_bucket}/{key}";
    }
}