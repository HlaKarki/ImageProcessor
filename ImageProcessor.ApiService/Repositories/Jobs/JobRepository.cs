using ImageProcessor.Data;
using ImageProcessor.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.ApiService.Repositories.Jobs;

public class JobRepository(AppDbContext db) : IJobRepository
{
    public async Task<Job?> GetByIdAsync(Guid jobId)
    {
        return await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId);
    }
    
    public async Task<(IEnumerable<Job> Items, int TotalCount)> GetAllByUserAsync(Guid userId, int page, int pageSize)
    {
        var query = db.Jobs.Where(j => j.UserId == userId);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        
        return (items, total);
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