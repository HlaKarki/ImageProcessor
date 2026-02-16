using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data.Models.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace ImageProcessor.ApiService.Services;

public class JobService(IJobRepository jobs, MessagePublisher publisher, HybridCache cache)
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
        
        return MapToResponse(job);
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
                _ => ValueTask.FromResult(MapToResponse(job)),
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromHours(1) }
            );
        }

        return MapToResponse(job);
    }

    public async Task<PagedResponse<JobResponse>> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        var results = await jobs.GetAllByUserAsync(userId, page, pageSize);
        var items = results.Items.Select(MapToResponse);
        
        return new PagedResponse<JobResponse>(
            items,
            page,
            pageSize,
            results.TotalCount,
            (int)Math.Ceiling((double)results.TotalCount / pageSize)
        );
    }
    
    private static JobResponse MapToResponse(Job job) => new (
        job.Id,
        job.UserId,
        job.Status.ToString(),
        job.OriginalUrl,
        job.OriginalFilename,
        job.FileSize,
        job.CreatedAt
    );
}