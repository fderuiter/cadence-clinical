using Cadence.Core.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Database.Configurations;

/// <summary>
/// EF Core 10 Fluent API configuration for the transactional outbox table (PRD-EVT-001).
/// </summary>
public sealed class TransactionalOutboxMessageConfiguration : IEntityTypeConfiguration<TransactionalOutboxMessage>
{
    public void Configure(EntityTypeBuilder<TransactionalOutboxMessage> builder)
    {
        builder.ToTable("transactional_outbox");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AggregateId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.Attempts).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.HasIndex(x => new { x.CreatedAt, x.DispatchedAt });
        builder.HasIndex(x => x.EventId);
    }
}
