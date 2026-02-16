using ImageProcessor.ApiService.Repositories.Storage;
using ImageProcessor.ApiService.Telemetry;
using ImageProcessor.Data;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.ApiService.Jobs;

public class CleanupJob(
    AppDbContext db,
    IStorageService storage,
    ILogger<CleanupJob> logger
) {
    public async Task RunAsync()
    {
        using var activity = ApiTelemetry.ActivitySource.StartActivity("cleanup.run");
        
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var oldJobs = await db.Jobs
            .Where(j => j.CreatedAt < cutoff)
            .ToListAsync();

        activity?.SetTag("cleanup.found", oldJobs.Count);
        logger.LogInformation("Cleanup: found {Count} jobs older than 30 days", oldJobs.Count);

        var cleaned = 0;
        foreach (var job in oldJobs)
        {
            try
            {
                await storage.DeleteJobFilesAsync(job.UserId.ToString(), job.Id.ToString());
                db.Jobs.Remove(job);
                cleaned++;
                logger.LogInformation("Cleanup: deleted job {jobId}", job.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cleanup: Failed to delete job {jobId}", job.Id);
            }
        }

        await db.SaveChangesAsync();
        ApiTelemetry.JobsCleaned.Add(cleaned);
        activity?.SetTag("cleanup.deleted", cleaned);
        logger.LogInformation("Cleanup: finished, deleted {Cleaned}/{Total} jobs", cleaned, oldJobs.Count);
    } 
}