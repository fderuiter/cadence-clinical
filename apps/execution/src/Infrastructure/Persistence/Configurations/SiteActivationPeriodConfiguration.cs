using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Database.Temporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SiteActivationPeriod"/> (<c>site_activation_periods</c>).
/// @req:PRD-SUB-001
/// @req:PRD-SYS-001
/// </summary>
public sealed class SiteActivationPeriodConfiguration(DbContext dbContext) : IEntityTypeConfiguration<SiteActivationPeriod>
{
    public void Configure(EntityTypeBuilder<SiteActivationPeriod> builder)
    {
        builder.ToTable("site_activation_periods");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SiteComplianceCacheId).IsRequired();

        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SiteCode)
            .HasConversion(c => c.Value, v => new SiteCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Milestone)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.MissingDocuments)
            .HasColumnName("missing_documents")
            .HasColumnType("text[]");

        builder.Property(x => x.IsActivated)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.ActivatedAt);

        builder.Property(x => x.ActivatedBy)
            .HasMaxLength(255);

        builder.Property(x => x.ReasonForChange)
            .HasMaxLength(1000);

        builder.HasOne<SiteComplianceCache>()
            .WithMany()
            .HasForeignKey(x => x.SiteComplianceCacheId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.SiteComplianceCacheId);
        builder.HasIndex(x => new { x.StudyCode, x.SiteCode, x.Milestone });
        builder.ConfigureStandaloneBitemporal(dbContext);
    }
}
