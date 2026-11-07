using JustInTime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Database;

public sealed class JustInTimeDbContext(DbContextOptions<JustInTimeDbContext> options) : DbContext(options)
{
    public DbSet<ScheduledTask> Tasks => Set<ScheduledTask>();

    public DbSet<ExecutionHistory> ExecutionHistories => Set<ExecutionHistory>();

    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JustInTimeDbContext).Assembly);
    }
}
