namespace ImageProcessor.Worker.Repositories;

public interface IStorageService
{
    string ExtractKey(string url);
    Task<Stream> DownloadAsync(string key);
    Task<string> UploadAsync(Stream stream, string key, string contentType);
}