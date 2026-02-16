using ImageProcessor.Data.Models;

namespace ImageProcessor.ApiService.Repositories.Jobs;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid jobId);
    Task<(IEnumerable<Job> Items, int TotalCount)> GetAllByUserAsync(Guid userId, int page, int pageSize);
    Task<Job> AddAsync(Job job);
    Task<Job?> GetByIdAndUserAsync(Guid jobId, Guid userId);
}