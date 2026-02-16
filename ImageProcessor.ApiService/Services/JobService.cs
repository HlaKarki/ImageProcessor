using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data.Models.Domain;

namespace ImageProcessor.ApiService.Services;

public class JobService(IJobRepository jobs, MessagePublisher publisher)
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

        await publisher.PublishAsync(new ImageJobMessage(
            job.Id,
            job.UserId,
            job.OriginalUrl,
            job.OriginalFilename,
            job.MimeType)
        );
        
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

    public async Task<PagedResponse<JobResponse>> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        var results = await jobs.GetAllByUserAsync(userId, page, pageSize);
        var items = results.Items.Select(job => new JobResponse(
            job.Id,
            job.UserId,
            job.Status.ToString(),
            job.OriginalUrl,
            job.OriginalFilename,
            job.FileSize,
            job.CreatedAt
        ));
        
        return new PagedResponse<JobResponse>(
            items,
            page,
            pageSize,
            results.TotalCount,
            (int)Math.Ceiling((double)results.TotalCount / pageSize)
        );
    }
}