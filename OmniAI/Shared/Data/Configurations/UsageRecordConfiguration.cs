using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Entities;

namespace Shared.Data.Configurations;

public class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Provider).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Model).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(20);
        builder.Property(r => r.CostUsd).HasPrecision(18, 8);

        builder.HasIndex(r => r.ProjectId);
        builder.HasIndex(r => r.OccurredAt);
        builder.HasIndex(r => r.Provider);

        builder.HasOne(r => r.Project)
            .WithMany()
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
