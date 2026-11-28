using Cadence.Apps.Interop.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Configurations;

public class StatisticalProgramConfiguration : IEntityTypeConfiguration<StatisticalProgram>
{
    public void Configure(EntityTypeBuilder<StatisticalProgram> builder)
    {
        builder.ToTable("sce_statistical_programs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LockType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LockDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetDomain).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class DatabaseLockAuthTokenConfiguration : IEntityTypeConfiguration<DatabaseLockAuthToken>
{
    public void Configure(EntityTypeBuilder<DatabaseLockAuthToken> builder)
    {
        builder.ToTable("sce_database_lock_auth_tokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TokenId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TokenId).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.LockId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Milestone).HasMaxLength(255).IsRequired();
        builder.Property(x => x.IssuedTo).HasMaxLength(255).IsRequired();
        builder.Property(x => x.AuthorizedRolesJson).HasColumnType("jsonb");
        builder.Property(x => x.SignatureHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CertificateId).HasMaxLength(128);
        builder.HasIndex(x => x.CertificateId);
        builder.Property(x => x.CertificateFingerprint).HasMaxLength(128);
        builder.Property(x => x.LockCertificateJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ReplicatedStudyLockConfiguration : IEntityTypeConfiguration<ReplicatedStudyLock>
{
    public void Configure(EntityTypeBuilder<ReplicatedStudyLock> builder)
    {
        builder.ToTable("sce_replicated_study_locks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.LockId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.LockId);
        builder.Property(x => x.StudyRef).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyRef);
        builder.Property(x => x.StudyCode).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyCode);
        builder.Property(x => x.ScopeKind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LockKind).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Milestone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CertificateId).HasMaxLength(128);
        builder.HasIndex(x => x.CertificateId);
        builder.Property(x => x.CertificateFingerprint).HasMaxLength(128);
        builder.Property(x => x.CertificateJson).HasColumnType("jsonb");
        builder.Property(x => x.AppliedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.RevokedBy).HasMaxLength(255);
        builder.Property(x => x.RevocationReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ExecutionRunRecordConfiguration : IEntityTypeConfiguration<ExecutionRunRecord>
{
    public void Configure(EntityTypeBuilder<ExecutionRunRecord> builder)
    {
        builder.ToTable("sce_execution_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.RunId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.RunId).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.ProgramId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ProgramId);
        builder.Property(x => x.SnapshotId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SnapshotId);
        builder.Property(x => x.Language).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ScriptCodeDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LockfileDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ContainerImageDigest).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OutputArtifactsJson).HasColumnType("jsonb");
        builder.Property(x => x.SignatureBlockJson).HasColumnType("jsonb");
        builder.Property(x => x.ManifestSha256Seal).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExecutedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class BiostatExportAuditLogConfiguration : IEntityTypeConfiguration<BiostatExportAuditLog>
{
    public void Configure(EntityTypeBuilder<BiostatExportAuditLog> builder)
    {
        builder.ToTable("sce_biostat_export_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");
        builder.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
    }
}
