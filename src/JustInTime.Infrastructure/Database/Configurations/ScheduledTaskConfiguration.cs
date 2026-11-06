using JustInTime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JustInTime.Infrastructure.Database.Configurations;

public sealed class ScheduledTaskConfiguration : IEntityTypeConfiguration<ScheduledTask>
{
    public void Configure(EntityTypeBuilder<ScheduledTask> builder)
    {
        builder.ToTable("Tasks");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);

        builder.Property(t => t.ActionType).HasConversion<string>().HasMaxLength(50);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(50);

        builder.Property(t => t.TimeZone).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Payload).IsRequired().HasColumnType("jsonb");
        builder.Property(t => t.LastError).HasMaxLength(2000);

        builder.Property(t => t.ScheduledAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(t => t.LastExecutedAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(t => new { t.Status, t.ScheduledAt });

        // История задачи сохраняется как журнал, поэтому каскадного удаления нет.
        // Задачи не удаляются, а переводятся в статус Cancelled.
        builder.HasMany(t => t.History)
            .WithOne(h => h.ScheduledTask)
            .HasForeignKey(h => h.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
