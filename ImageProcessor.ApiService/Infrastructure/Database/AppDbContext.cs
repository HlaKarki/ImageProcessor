using ImageProcessor.ApiService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.ApiService.Infrastructure.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    
    public DbSet<Job> Jobs { get; set; }
}
