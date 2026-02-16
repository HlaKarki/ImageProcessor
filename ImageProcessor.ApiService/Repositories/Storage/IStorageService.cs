namespace ImageProcessor.ApiService.Repositories.Storage;

public interface IStorageService
{
    Task<string> UploadAsync(IFormFile file, string userId, string jobId);
    Task DeleteJobFilesAsync(string userId, string jobId);
}