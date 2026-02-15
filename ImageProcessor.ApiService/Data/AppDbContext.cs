using ImageProcessor.ApiService.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.ApiService.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    
    public DbSet<Job> Jobs { get; set; }
}
