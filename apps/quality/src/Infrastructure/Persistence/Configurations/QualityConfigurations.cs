using Cadence.Apps.Quality.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Quality.Infrastructure.Persistence.Configurations;

public sealed class QualityDeviationConfiguration : IEntityTypeConfiguration<QualityDeviation>
{
    public void Configure(EntityTypeBuilder<QualityDeviation> builder)
    {
        builder.ToTable("quality_deviations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsProtocolViolation).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class QualityRootCauseAnalysisConfiguration : IEntityTypeConfiguration<QualityRootCauseAnalysis>
{
    public void Configure(EntityTypeBuilder<QualityRootCauseAnalysis> builder)
    {
        builder.ToTable("quality_root_cause_analyses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DeviationId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Methodology).HasMaxLength(255).IsRequired();
        builder.Property(x => x.InvestigationDetails).IsRequired();
        builder.Property(x => x.RootCauseSummary).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<QualityDeviation>()
            .WithMany()
            .HasForeignKey(x => x.DeviationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.DeviationId).IsUnique();
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class QualityCapaRecordConfiguration : IEntityTypeConfiguration<QualityCapaRecord>
{
    public void Configure(EntityTypeBuilder<QualityCapaRecord> builder)
    {
        builder.ToTable("quality_capa_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DeviationId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.RcaId).HasMaxLength(36);
        builder.Property(x => x.CapaType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ActionPlan).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<QualityDeviation>()
            .WithMany()
            .HasForeignKey(x => x.DeviationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<QualityRootCauseAnalysis>()
            .WithMany()
            .HasForeignKey(x => x.RcaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.DeviationId);
        builder.HasIndex(x => x.RcaId);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class QualityAuditLogConfiguration : IEntityTypeConfiguration<QualityAuditLog>
{
    public void Configure(EntityTypeBuilder<QualityAuditLog> builder)
    {
        builder.ToTable("quality_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RecordId).HasMaxLength(255);
        builder.Property(x => x.ChangeReason).HasMaxLength(1000);

        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
    }
}
