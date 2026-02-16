using ImageProcessor.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageProcessor.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    
    public DbSet<Job> Jobs { get; set; }
}
