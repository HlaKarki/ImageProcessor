using ImageProcessor.ApiService.Data;
using ImageProcessor.ApiService.Models.Domain;

namespace ImageProcessor.ApiService.Services;

public class JobService(AppDbContext db)
{
    public async Task<Job> CreateAsync(string jobId, string userId, string url, IFormFile file)
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

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return job;
    }
}