using Amazon.S3;
using Amazon.S3.Model;

namespace ImageProcessor.ApiService.Services;

public class S3Service(IAmazonS3 s3, IConfiguration config)
{
    private readonly string _bucket = config["AWS:BucketName"]!;

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

        return $"{config["AWS:ServiceURL"]}/{_bucket}/{key}";
    }
}