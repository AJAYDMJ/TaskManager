using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Data;

public class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
}
