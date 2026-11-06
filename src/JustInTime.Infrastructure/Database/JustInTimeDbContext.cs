using JustInTime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Database;

public sealed class JustInTimeDbContext(DbContextOptions<JustInTimeDbContext> options) : DbContext(options)
{
    public DbSet<ScheduledTask> Tasks => Set<ScheduledTask>();

    public DbSet<ExecutionHistory> ExecutionHistories => Set<ExecutionHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JustInTimeDbContext).Assembly);
    }
}
