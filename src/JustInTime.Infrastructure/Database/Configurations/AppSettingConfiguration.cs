using JustInTime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JustInTime.Infrastructure.Database.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("Settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(4000);

        // Уникальный ключ нужен для безопасного upsert (INSERT ... ON CONFLICT).
        builder.HasIndex(s => s.Key).IsUnique();
    }
}
