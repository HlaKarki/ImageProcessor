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
)
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
        
        await cache.RemoveByTagAsync($"user-jobs:{userId}"); // invalidate cache

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
        var cacheKey = $"job:by_job_id:{jobId}:{userId}";
        
        return await cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                logger.LogInformation("Cache miss for {cacheKey}, fetching from DB", cacheKey);
                var job = await jobs.GetByIdAndUserAsync(jobId, userId);

                return job is null ? null : mapper.ToResponse(job);
            },
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(30) }
        );
    }

    public async Task<PagedResponse<JobResponse>> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        var cacheKey = $"job:by_user_id:{userId}:{page}:{pageSize}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                logger.LogInformation("Cache miss for {cacheKey}, fetching from DB", cacheKey);
                var results = await jobs.GetAllByUserAsync(userId, page, pageSize);
                var items = results.Items.Select(mapper.ToResponse);

                return new PagedResponse<JobResponse>(
                    items,
                    page,
                    pageSize,
                    results.TotalCount,
                    (int)Math.Ceiling((double)results.TotalCount / pageSize)
                );
            },
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) },
            tags: [$"user-jobs:{userId}"]
        );
    }
}