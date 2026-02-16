using Amazon.S3;
using Amazon.S3.Model;

namespace ImageProcessor.Worker.Services;

public class StorageService(IAmazonS3 s3, IConfiguration config, ILogger<StorageService> logger)
{
    private readonly string _bucket = config["AWS:BucketName"];
    private readonly string _serviceUrl = config["AWS:ServiceURL"];

    public string ExtractKey(string url)
    {
        var prefix = $"{_serviceUrl}/{_bucket}";
        return url.StartsWith(prefix) ? url[prefix.Length..] : url;
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        logger.LogInformation("Downloading {key}", key);
        var response = await s3.GetObjectAsync(_bucket, key);
        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        ms.Position = 0;
        return ms;
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        logger.LogInformation("Uploading {Key}", key);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            UseChunkEncoding = false
        };
        await s3.PutObjectAsync(request);
        return $"{_serviceUrl}/{_bucket}/{key}";
    }
}