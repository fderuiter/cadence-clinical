using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Persistence.Configurations;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Kernel.Persistence.Validation;

/// <summary>
/// EF Core mapping for <c>kernel.validation_runs</c> and its insert-only child tables (spec §10.3). Every reference is
/// a composite <c>(tenant_id, …)</c> foreign key (K-ID-1), so a detection can only name a pack and a dataset its own run
/// evaluated.
/// </summary>
internal sealed class ValidationRunConfiguration : IEntityTypeConfiguration<ValidationRun>
{
    public void Configure(EntityTypeBuilder<ValidationRun> builder)
    {
        builder.ToTable("validation_runs");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Standard).HasMaxLength(ValidationRun.MaxNameLength);
        builder.Property(x => x.StandardVersion).HasMaxLength(ValidationRun.MaxNameLength);
        builder.Property(x => x.Agency).HasMaxLength(ValidationRun.MaxNameLength);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Stage, x.CreatedAt });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Packs).WithOne()
            .HasForeignKey(p => new { p.TenantId, p.RunId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Datasets).WithOne()
            .HasForeignKey(d => new { d.TenantId, d.RunId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Inputs).WithOne()
            .HasForeignKey(i => new { i.TenantId, i.RunId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Detections).WithOne()
            .HasForeignKey(d => new { d.TenantId, d.RunId }).HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Packs).HasField("_packs");
        builder.Navigation(x => x.Datasets).HasField("_datasets");
        builder.Navigation(x => x.Inputs).HasField("_inputs");
        builder.Navigation(x => x.Detections).HasField("_detections");
        AuditColumns.Configure(builder);
    }
}

/// <summary>EF Core mapping for <c>kernel.validation_run_packs</c>.</summary>
internal sealed class ValidationRunPackConfiguration : IEntityTypeConfiguration<ValidationRunPack>
{
    public void Configure(EntityTypeBuilder<ValidationRunPack> builder)
    {
        builder.ToTable("validation_run_packs");
        builder.HasKey(x => new { x.TenantId, x.RunId, x.PackKey });
        builder.Property(x => x.PackKey).HasMaxLength(LibraryItem.MaxKeyLength);
        builder.Property(x => x.ContentHash).HasMaxLength(StandardsColumns.HashLength).IsRequired();
        builder.HasOne<LibraryItem>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.LibraryItemId }).HasPrincipalKey(i => new { i.TenantId, i.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>EF Core mapping for <c>kernel.validation_run_datasets</c>.</summary>
internal sealed class ValidationRunDatasetConfiguration : IEntityTypeConfiguration<ValidationRunDataset>
{
    public void Configure(EntityTypeBuilder<ValidationRunDataset> builder)
    {
        builder.ToTable("validation_run_datasets");
        builder.HasKey(x => new { x.TenantId, x.RunId, x.Dataset });
        builder.Property(x => x.Dataset).HasMaxLength(FindingTarget.MaxNameLength);
    }
}

/// <summary>EF Core mapping for <c>kernel.validation_run_inputs</c>.</summary>
internal sealed class ValidationRunInputConfiguration : IEntityTypeConfiguration<ValidationRunInput>
{
    public void Configure(EntityTypeBuilder<ValidationRunInput> builder)
    {
        builder.ToTable("validation_run_inputs");
        builder.HasKey(x => new { x.TenantId, x.RunId, x.Input });
        builder.Property(x => x.Input).HasMaxLength(ValidationColumns.NodeRefLength);
    }
}

/// <summary>
/// EF Core mapping for <c>kernel.validation_detections</c>: one row per finding a run detected. The
/// <c>(tenant_id, study_id, stage)</c> index serves the ledger over a study, the <c>(tenant_id, finding_id)</c> index
/// serves one finding's history.
/// </summary>
internal sealed class ValidationDetectionRecordConfiguration : IEntityTypeConfiguration<ValidationDetectionRecord>
{
    public void Configure(EntityTypeBuilder<ValidationDetectionRecord> builder)
    {
        builder.ToTable("validation_detections");
        builder.HasKey(x => new { x.TenantId, x.RunId, x.FindingId });
        builder.Property(x => x.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.PackKey).HasMaxLength(LibraryItem.MaxKeyLength).IsRequired();
        builder.Property(x => x.RuleId).HasMaxLength(ValidationDetection.MaxRuleIdLength).IsRequired();
        builder.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.TargetKind).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.Dataset).HasMaxLength(FindingTarget.MaxNameLength).IsRequired();
        builder.Property(x => x.TargetKey).HasMaxLength(FindingTarget.MaxKeyLength).IsRequired();
        builder.Property(x => x.Variable).HasMaxLength(FindingTarget.MaxNameLength);
        builder.Property(x => x.Message).HasMaxLength(ValidationDetection.MaxTextLength).IsRequired();
        builder.Property(x => x.Expected).HasMaxLength(ValidationDetection.MaxTextLength);
        builder.Property(x => x.Observed).HasColumnType("jsonb").IsRequired();
        builder.Ignore(x => x.Target);
        builder.Ignore(x => x.ObservedValues);
        builder.HasIndex(x => new { x.TenantId, x.StudyId, x.Stage });
        builder.HasIndex(x => new { x.TenantId, x.FindingId });

        // A detection names a pack and a dataset its own run evaluated.
        builder.HasOne<ValidationRunPack>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId, x.PackKey })
            .HasPrincipalKey(p => new { p.TenantId, p.RunId, p.PackKey })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ValidationRunDataset>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RunId, x.Dataset })
            .HasPrincipalKey(d => new { d.TenantId, d.RunId, d.Dataset })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>EF Core mapping for <c>kernel.validation_findings</c>: the decisions people made, one row per finding.</summary>
internal sealed class ValidationFindingConfiguration : IEntityTypeConfiguration<ValidationFinding>
{
    public void Configure(EntityTypeBuilder<ValidationFinding> builder)
    {
        builder.ToTable("validation_findings");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.DecidedBy).HasMaxLength(255);
        builder.Property(x => x.DecisionText).HasMaxLength(FindingText.MaxLength);
        builder.Property(x => x.Classification).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.TenantId, x.StudyId });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Study>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudyId }).HasPrincipalKey(s => new { s.TenantId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ValidationRun>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DecidedOnRun }).HasPrincipalKey(r => new { r.TenantId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Attestation>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.AttestationId }).HasPrincipalKey(a => new { a.TenantId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
        AuditColumns.Configure(builder);
    }
}

/// <summary>Shared column settings for the validation tables.</summary>
internal static class ValidationColumns
{
    /// <summary>A versioned NodeRef: kind, a UUID and a version index.</summary>
    public const int NodeRefLength = 128;
}
