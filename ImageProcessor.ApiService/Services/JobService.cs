using ImageProcessor.ApiService.Mappings;
using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data.Models.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace ImageProcessor.ApiService.Services;

public class JobService(
    IJobRepository jobs,
    MessagePublisher publisher,
    HybridCache cache,
    JobMapper mapper,
    ILogger<JobService> logger
) {
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
        
        return mapper.ToResponse(job);
    }
    
    public async Task<JobResponse?> GetByIdAsync(Guid jobId, Guid userId)
    {
        var job = await jobs.GetByIdAndUserAsync(jobId, userId);
        if (job is null) return null;

        var cacheKey = $"job:{jobId}:{userId}";

        if (job.Status == JobStatus.Completed || job.Status == JobStatus.Error)
        {
            return await cache.GetOrCreateAsync(
                cacheKey,
                _ =>
                {
                    logger.LogInformation("Cache miss for job {jobId}, fetching from DB", jobId);
                    return ValueTask.FromResult(mapper.ToResponse(job));
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }
            );
        }

        return mapper.ToResponse(job);
    }

    public async Task<PagedResponse<JobResponse>> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        var results = await jobs.GetAllByUserAsync(userId, page, pageSize);
        var items = results.Items.Select(mapper.ToResponse);
        
        return new PagedResponse<JobResponse>(
            items,
            page,
            pageSize,
            results.TotalCount,
            (int)Math.Ceiling((double)results.TotalCount / pageSize)
        );
    }
}