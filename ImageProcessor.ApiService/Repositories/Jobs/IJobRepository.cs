using ImageProcessor.ApiService.Models.Domain;

namespace ImageProcessor.ApiService.Repositories.Jobs;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid jobId);
    Task<IEnumerable<Job>> GetAllByUserAsync(Guid userId);
    Task<Job> AddAsync(Job job);
    Task<Job?> GetByIdAndUserAsync(Guid jobId, Guid userId);
}