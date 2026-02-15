using ImageProcessor.ApiService.Data;
using ImageProcessor.ApiService.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.ApiService.Repositories.Jobs;

public class JobRepository(AppDbContext db) : IJobRepository
{
    public async Task<Job?> GetByIdAsync(Guid jobId)
    {
        return await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
    }

    public async Task<IEnumerable<Job>> GetAllByUserAsync(Guid userId)
    {
        return await db.Jobs.Where(j => j.UserId == userId).ToListAsync();
    }

    public async Task<Job> AddAsync(Job job)
    {
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    public async Task<Job?> GetByIdAndUserAsync(Guid jobId, Guid userId) => 
        await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);
    
}