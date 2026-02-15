using ImageProcessor.ApiService.Models.Domain;
using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Repositories.Jobs;

namespace ImageProcessor.ApiService.Services;

public class JobService(IJobRepository jobs)
{
    public async Task<JobResponse> CreateAsync(string jobId, string userId, string url, IFormFile file)
    {
        // create a Job record
        var job = new Job
        {
            Id = new Guid(jobId),
            UserId = new Guid(userId),
            
            Status = JobStatus.Pending,
            
            OriginalUrl = url,
            OriginalFilename = file.FileName,
            
            FileSize = file.Length,
            MimeType = file.ContentType,
            CreatedAt = DateTime.UtcNow
        };
        
        await jobs.AddAsync(job);
        return new JobResponse(
            job.Id,
            job.UserId,
            job.Status.ToString(),
            job.OriginalUrl,
            job.OriginalFilename,
            job.FileSize,
            job.CreatedAt
        );
    }
    
    public async Task<JobResponse?> GetByIdAsync(Guid jobId, Guid userId)
    {
        var job = await jobs.GetByIdAndUserAsync(jobId, userId);
        if (job is null) return null;

        return new JobResponse(
            job.Id,
            job.UserId,
            job.Status.ToString(),
            job.OriginalUrl,
            job.OriginalFilename,
            job.FileSize,
            job.CreatedAt
        );
    }

    public async Task<IEnumerable<JobResponse>> GetAllByUserAsync(Guid userId)
    {
        var results = await jobs.GetAllByUserAsync(userId);
        return results.Select(job => new JobResponse(
            job.Id,
            job.UserId,
            job.Status.ToString(),
            job.OriginalUrl,
            job.OriginalFilename,
            job.FileSize,
            job.CreatedAt
        ));
    }
    
    private async Task<Job> AddAsync(Job job)  => await jobs.AddAsync(job);
}