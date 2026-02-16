using ImageProcessor.ApiService.Repositories.Storage;
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
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var oldJobs = await db.Jobs
            .Where(j => j.CreatedAt < cutoff)
            .ToListAsync();
        
        logger.LogInformation("Cleanup: found {Count} jobs older than 30 days", oldJobs.Count);

        foreach (var job in oldJobs)
        {
            try
            {
                await storage.DeleteJobFilesAsync(job.UserId.ToString(), job.Id.ToString());
                db.Jobs.Remove(job);
                logger.LogInformation("Cleanup: deleted job {jobId}", job.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cleanup: Failed to delete job {jobId}", job.Id);
            }
        }
        
        await db.SaveChangesAsync();
    } 
}