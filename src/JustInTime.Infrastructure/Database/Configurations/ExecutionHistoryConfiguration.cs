using JustInTime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JustInTime.Infrastructure.Database.Configurations;

public sealed class ExecutionHistoryConfiguration : IEntityTypeConfiguration<ExecutionHistory>
{
    public void Configure(EntityTypeBuilder<ExecutionHistory> builder)
    {
        builder.ToTable("ExecutionHistory");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Result).HasConversion<string>().HasMaxLength(50);
        builder.Property(h => h.ErrorMessage).HasMaxLength(4000);

        builder.Property(h => h.StartedAt).HasColumnType("timestamp with time zone");
        builder.Property(h => h.FinishedAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(h => h.StartedAt);
        builder.HasIndex(h => h.TaskId);
    }
}
