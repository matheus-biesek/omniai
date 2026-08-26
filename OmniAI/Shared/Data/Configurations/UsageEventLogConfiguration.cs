using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Entities;

namespace Shared.Data.Configurations;

public class UsageEventLogConfiguration : IEntityTypeConfiguration<UsageEventLog>
{
    public void Configure(EntityTypeBuilder<UsageEventLog> builder)
    {
        builder.ToTable("usage_event_logs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RedisEntryId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.NextRetryAt);
    }
}
