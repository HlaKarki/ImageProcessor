using Amazon.S3;
using Amazon.S3.Model;
using ImageProcessor.Worker.Repositories;
using Polly.Registry;

namespace ImageProcessor.Worker.Services;

public class S3StorageService(
    IAmazonS3 s3,
    IConfiguration config,
    ILogger<S3StorageService> logger,
    ResiliencePipelineProvider<string> pipelineProvider) : IStorageService
{
    private readonly string _bucket = config["AWS:BucketName"]!;
    private readonly string _region = config["AWS:Region"]!;
    
    public string ExtractKey(string url)
    {
        var prefix = $"https://{_bucket}.s3.{_region}.amazonaws.com/";
        return url.StartsWith(prefix) ? url[prefix.Length..] : url;
    }
    
    public async Task<Stream> DownloadAsync(string key)
    {
        logger.LogInformation("Downloading {key}", key);
        var pipeline = pipelineProvider.GetPipeline("storage");

        return await pipeline.ExecuteAsync(async ct =>
        {
            var response = await s3.GetObjectAsync(_bucket, key, ct);
            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        });
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        logger.LogInformation("Uploading {Key}", key);
        var pipeline = pipelineProvider.GetPipeline("storage");
        await pipeline.ExecuteAsync(async ct =>
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                UseChunkEncoding = false
            };
            await s3.PutObjectAsync(request, ct);
        });
        
        return $"https://{_bucket}.s3.{_region}.amazonaws.com/{key}";
    }

}