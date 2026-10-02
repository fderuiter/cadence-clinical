using Cadence.Apps.Org.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Org.Infrastructure.Persistence.Configurations;


public class PersonnelAssignmentConfiguration : IEntityTypeConfiguration<PersonnelAssignment>
{
    public void Configure(EntityTypeBuilder<PersonnelAssignment> builder)
    {
        builder.ToTable("personnel_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.PersonnelId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class DelegationOfAuthorityConfiguration : IEntityTypeConfiguration<DelegationOfAuthority>
{
    public void Configure(EntityTypeBuilder<DelegationOfAuthority> builder)
    {
        builder.ToTable("delegations_of_authority");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DelegatorId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.DelegateeId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.DutiesJson).HasColumnType("jsonb");
        builder.Property(x => x.Signature).HasMaxLength(512);
        builder.Property(x => x.SignedPayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.SignedBy).HasMaxLength(255);
        builder.Property(x => x.RevokedBy).HasMaxLength(255);
        builder.Property(x => x.RevocationReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class TrainingLogConfiguration : IEntityTypeConfiguration<TrainingLog>
{
    public void Configure(EntityTypeBuilder<TrainingLog> builder)
    {
        builder.ToTable("org_training_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.PersonnelId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.PersonnelId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.TrainingTopic).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SignatureManifestationJson).HasColumnType("jsonb");
        builder.Property(x => x.Signer).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class OrgAuditLogConfiguration : IEntityTypeConfiguration<OrgAuditLog>
{
    public void Configure(EntityTypeBuilder<OrgAuditLog> builder)
    {
        builder.ToTable("org_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ActorId);
        builder.Property(x => x.ActorRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RecordId).HasMaxLength(255);
        builder.HasIndex(x => x.RecordId);
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class UserOnboardingPreferenceConfiguration : IEntityTypeConfiguration<UserOnboardingPreference>
{
    public void Configure(EntityTypeBuilder<UserOnboardingPreference> builder)
    {
        builder.ToTable("user_onboarding_preferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.Persona).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.Persona }).IsUnique();
        builder.Property(x => x.EventsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}
