using Cadence.Kernel.Persistence.Commands;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

internal sealed class ProvenanceActivityConfiguration : IEntityTypeConfiguration<ProvenanceActivityRecord>
{
    public void Configure(EntityTypeBuilder<ProvenanceActivityRecord> builder)
    {
        builder.ToTable("provenance_activities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CommandType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.RequestedBy).HasMaxLength(256);
        builder.Property(x => x.ReasonForChange).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(200);
        builder.Property(x => x.AggregateId).HasMaxLength(200);
        builder.Property(x => x.Authorization).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.RecordedAt });
        builder.HasIndex(x => new { x.TenantId, x.ProposalId }).HasFilter("proposal_id IS NOT NULL");
        builder.HasIndex(x => x.CorrelationId);
    }
}

internal sealed class DomainEventConfiguration : IEntityTypeConfiguration<DomainEventRecord>
{
    public void Configure(EntityTypeBuilder<DomainEventRecord> builder)
    {
        builder.ToTable("domain_events");
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Recorded });
        builder.HasIndex(x => x.ActivityId);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntryRecord>
{
    public void Configure(EntityTypeBuilder<AuditEntryRecord> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.Sequence }).IsUnique();
        builder.Property(x => x.ActorId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(200);
        builder.Property(x => x.AggregateId).HasMaxLength(200);
        builder.Property(x => x.ReasonForChange).HasMaxLength(2000).IsRequired();

        // text, not jsonb: the hash covers these exact bytes, and jsonb would re-normalise them.
        builder.Property(x => x.Changes).HasColumnType("text").IsRequired();
        builder.Property(x => x.PredecessorHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.ActivityId);
    }
}

internal sealed class AuditChainHeadConfiguration : IEntityTypeConfiguration<AuditChainHead>
{
    public void Configure(EntityTypeBuilder<AuditChainHead> builder)
    {
        builder.ToTable("audit_chain_heads");
        builder.HasKey(x => x.TenantId);
        builder.Property(x => x.LastHash).HasMaxLength(64).IsRequired();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.Property(x => x.Type).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Envelope).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.HasIndex(x => x.EnqueuedAt).HasFilter("dispatched_at IS NULL");

        // Serves the relay's per-tenant ordering check over undelivered messages.
        builder.HasIndex(x => new { x.TenantId, x.EnqueuedAt }).HasFilter("dispatched_at IS NULL");
    }
}

internal sealed class AuditChainSealConfiguration : IEntityTypeConfiguration<AuditChainSeal>
{
    public void Configure(EntityTypeBuilder<AuditChainSeal> builder)
    {
        builder.ToTable("audit_chain_seals");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.Sequence }).IsUnique();
        builder.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SealedBy).HasMaxLength(256).IsRequired();
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(x => new { x.TenantId, x.IdempotencyKey });
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.CommandType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultJson).HasColumnType("text").IsRequired();
    }
}

internal sealed class BiostatExportAuditLogConfiguration : IEntityTypeConfiguration<BiostatExportAuditLog>
{
    public void Configure(EntityTypeBuilder<BiostatExportAuditLog> builder)
    {
        builder.ToTable("biostat_export_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Format).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LockFilter).HasMaxLength(64);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Timestamp });
        builder.HasIndex(x => new { x.TenantId, x.Timestamp });
        builder.HasIndex(x => x.ActivityId);
    }
}
