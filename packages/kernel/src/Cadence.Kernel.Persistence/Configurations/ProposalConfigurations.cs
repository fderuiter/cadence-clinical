using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Trust;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <c>kernel.proposals</c> (ADR-2489). The checks repeat the domain's rules: the intent, the counts
/// of affected objects and requested capabilities, the expiry window, a model exactly for an automation agent, the hash
/// shapes, and decision columns that fit the status. The migration fixes the content columns with the immutable-columns
/// trigger, so only the decision can change.
/// </summary>
internal sealed class ProposalConfiguration : IEntityTypeConfiguration<Proposal>
{
    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("proposals", t =>
        {
            t.HasCheckConstraint("ck_proposals_intent", $"char_length(intent) BETWEEN 1 AND {Propose.MaxIntentLength}");
            t.HasCheckConstraint("ck_proposals_affected", $"cardinality(affected) <= {Propose.MaxAffected}");
            t.HasCheckConstraint(
                "ck_proposals_requested_capabilities",
                $"cardinality(requested_capabilities) BETWEEN 1 AND {Propose.MaxRequestedCapabilities}");
            t.HasCheckConstraint(
                "ck_proposals_expiry",
                $"expires_at > proposed_at AND expires_at <= proposed_at + interval '{Propose.MaxLifetime.TotalDays:0} days'");
            t.HasCheckConstraint(
                "ck_proposals_model",
                "(proposed_by_kind = 'AutomationAgent' AND model_id IS NOT NULL AND char_length(model_id) > 0)"
                + " OR (proposed_by_kind <> 'AutomationAgent' AND model_id IS NULL)");
            t.HasCheckConstraint("ck_proposals_content_hash", $"content_hash ~ '{ContentHash.Pattern}'");
            t.HasCheckConstraint("ck_proposals_simulated_impact", $"simulated_impact IS NULL OR simulated_impact ~ '{ContentHash.Pattern}'");
            t.HasCheckConstraint(
                "ck_proposals_decision",
                "(status = 'Open' AND decided_at IS NULL AND decided_by_id IS NULL AND decided_by_kind IS NULL AND approval_attestation_id IS NULL AND decision_reason IS NULL)"
                + " OR (status = 'Approved' AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind = 'Person' AND approval_attestation_id IS NOT NULL)"
                + " OR (status IN ('Rejected', 'Withdrawn') AND decided_at IS NOT NULL AND decided_by_id IS NOT NULL AND decided_by_kind IS NOT NULL AND approval_attestation_id IS NULL AND decision_reason IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Intent).HasMaxLength(Propose.MaxIntentLength).IsRequired();
        builder.Property(x => x.Affected).IsRequired();
        builder.Property(x => x.SimulatedImpact).HasMaxLength(ContentHash.Length);
        builder.Property(x => x.RequestedCapabilities).IsRequired();
        builder.Property(x => x.ProposedById).HasMaxLength(256).IsRequired();
        builder.Property(x => x.ProposedByKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ModelId).HasMaxLength(Propose.MaxModelIdLength);
        builder.Property(x => x.ContentHash).HasMaxLength(ContentHash.Length).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.DecidedById).HasMaxLength(256);
        builder.Property(x => x.DecidedByKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DecisionReason).HasMaxLength(1000);
        builder.Ignore(x => x.ProposedBy);
        builder.Ignore(x => x.SignableTarget);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.PlanArtifactVersionId });
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ArtifactVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PlanArtifactVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ApprovalAttestationId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}
