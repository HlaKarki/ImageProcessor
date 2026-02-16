using Amazon.S3;
using Amazon.S3.Model;
using ImageProcessor.ApiService.Repositories.Storage;
using Polly.Registry;

namespace ImageProcessor.ApiService.Services;

public class S3Service(
    IAmazonS3 s3,
    IConfiguration configuration, 
    ResiliencePipelineProvider<string> pipelineProvider
) : IStorageService
{
    private readonly string _bucket = configuration["AWS:BucketName"]!;

    public async Task<string> UploadAsync(IFormFile file, string userId, string jobId)
    {
        var pipeline = pipelineProvider.GetPipeline("storage");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"originals/{userId}/{jobId}{extension}";

        await pipeline.ExecuteAsync(async ct =>
        {
            await using var stream = file.OpenReadStream();
            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = stream,
                ContentType = file.ContentType,
                UseChunkEncoding = false
            };
            return await s3.PutObjectAsync(request, ct);
        });

        return $"https://{_bucket}.s3.{configuration["AWS:Region"]}.amazonaws.com/{key}";
    }
    
    public async Task DeleteJobFilesAsync(string userId, string jobId)
    {
        var pipeline = pipelineProvider.GetPipeline("storage");
        var prefixes = new[] { $"originals/{userId}/{jobId}", $"processed/{userId}/{jobId}/" };

        foreach (var prefix in prefixes)
        {
            var listed = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix
            });

            if (listed.S3Objects.Count == 0) continue;

            await pipeline.ExecuteAsync(async ct =>
            {
                var request = new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = listed.S3Objects
                        .Select(o => new KeyVersion { Key = o.Key })
                        .ToList()
                };
                return await s3.DeleteObjectsAsync(request, ct);
            });
        }
    }
}