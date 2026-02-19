using System.Diagnostics;
using ImageProcessor.ApiService.Mappings;
using ImageProcessor.ApiService.Models.DTOs;
using ImageProcessor.ApiService.Repositories.Jobs;
using ImageProcessor.ApiService.Repositories.Storage;
using ImageProcessor.ApiService.Telemetry;
using ImageProcessor.Contracts.Messages;
using ImageProcessor.Data.Models.Domain;
using Microsoft.Extensions.Caching.Hybrid;

namespace ImageProcessor.ApiService.Services;

public class JobService(
    IJobRepository jobs,
    MessagePublisher publisher,
    IStorageService storage,
    HybridCache cache,
    JobMapper mapper,
    IConfiguration configuration,
    ILogger<JobService> logger
)
{
    private readonly TimeSpan _readUrlTtl = TimeSpan.FromMinutes(
        Math.Clamp(configuration.GetValue<int?>("Storage:ReadUrlExpiryMinutes") ?? 10, 1, 60)
    );

    public async Task<JobResponse> CreateAsync(string jobId, string userId, string url, IFormFile file)
    {
        using var activity = ApiTelemetry.ActivitySource.StartActivity("job.create");
        activity?.SetTag("job.id", jobId);
        activity?.SetTag("user.id", userId);
        activity?.SetTag("file.size", file.Length);
        activity?.SetTag("file.mime_type", file.ContentType);

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["JobId"] = jobId,
                   ["UserId"] = userId,
               })
              )
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
        
            ApiTelemetry.JobsCreated.Add(1, new TagList{ { "user.id", userId } });
            logger.LogInformation("Job {jobid} created and queued", jobId);
            
            return WithSignedUrls(mapper.ToResponse(job));
        }
    }
    
    public async Task<JobResponse?> GetByIdAsync(Guid jobId, Guid userId)
    {
        using var activity = ApiTelemetry.ActivitySource.StartActivity("job.get_by_id");
        activity?.SetTag("job.id", jobId);
        activity?.SetTag("user.id", userId);
        
        var cacheKey = $"job:by_job_id:{jobId}:{userId}";

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["JobId"] = jobId,
                   ["UserId"] = userId,
               })
              )
        {
            var cached = await cache.GetOrCreateAsync(
                cacheKey,
                async _ =>
                {
                    ApiTelemetry.CacheMisses.Add(1);
                    logger.LogInformation("Cache miss for {cacheKey}, fetching from DB", cacheKey);
                    var job = await jobs.GetByIdAndUserAsync(jobId, userId);

                    return job is null ? null : mapper.ToResponse(job);
                },
                new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(30) }
            );

            return cached is null ? null : WithSignedUrls(cached);
        }
    }

    public async Task<PagedResponse<JobResponse>> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        using var activity = ApiTelemetry.ActivitySource.StartActivity("job.get_all_by_user");
        activity?.SetTag("user.id", userId);
        activity?.SetTag("page", page);
        
        var cacheKey = $"job:by_user_id:{userId}:{page}:{pageSize}";

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["UserId"] = userId,
                   ["Page"] = page,
                   ["PageSize"] = pageSize,
               })
              )
        {
            var cached = await cache.GetOrCreateAsync(
                cacheKey,
                async _ =>
                {
                    ApiTelemetry.CacheMisses.Add(1);
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

            return cached with
            {
                Items = cached.Items.Select(WithSignedUrls).ToList()
            };
        }
    }

    private JobResponse WithSignedUrls(JobResponse response)
    {
        var signedOriginal = TrySign(response.OriginalUrl, "original_url", response.Id);

        return response with
        {
            OriginalUrl = signedOriginal,
            Thumbnails = TrySignDictionary(response.Thumbnails, "thumbnails", response.Id),
            Optimized = TrySignDictionary(response.Optimized, "optimized", response.Id)
        };
    }

    private Dictionary<string, string>? TrySignDictionary(
        Dictionary<string, string>? entries,
        string field,
        Guid jobId
    )
    {
        if (entries is null)
        {
            return null;
        }

        return entries.ToDictionary(
            entry => entry.Key,
            entry => TrySign(entry.Value, $"{field}.{entry.Key}", jobId)
        );
    }

    private string TrySign(string url, string field, Guid jobId)
    {
        try
        {
            return storage.GetReadUrl(url, _readUrlTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sign {Field} for job {JobId}", field, jobId);
            return url;
        }
    }
}
