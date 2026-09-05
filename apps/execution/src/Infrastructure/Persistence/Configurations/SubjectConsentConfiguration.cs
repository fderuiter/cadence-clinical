using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 Fluent API mapping for the <c>subject_consents</c> table: the strongly-typed-ID conversion,
/// the <see cref="ProtocolVersionRef"/> Complex Type mapping (EF Core 10's <c>ComplexProperty</c>, not
/// <c>OwnsOne</c> — <see cref="ProtocolVersionRef"/> has no independent primary key, per the design's
/// explicit choice), and the shadow <c>_version</c> EF optimistic-concurrency token (mirroring
/// <see cref="ClinicalSubjectConfiguration"/>'s pattern from MIG-SUBJ-011). Mirrors
/// <c>02_technical_design.md</c> §4.1/§4.3.
/// Replaces <c>apps/execution/database/models/subject.py</c> (<c>SubjectConsent</c> SQLAlchemy model).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectConsentConfiguration : IEntityTypeConfiguration<SubjectConsent>
{
    public void Configure(EntityTypeBuilder<SubjectConsent> builder)
    {
        builder.ToTable("subject_consents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new SubjectConsentId(v));

        builder.Property(x => x.SubjectCode)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();

        // ProtocolVersionRef is an EF Core 10 Complex Type (ComplexProperty) — it has no independent PK,
        // so OwnsOne (which models an owned *entity* with its own key/table-splitting semantics) does not
        // apply here (§4.3). Its three scalar members are configured individually within the builder.
        builder.ComplexProperty(x => x.ProtocolVersion, pv =>
        {
            pv.Property(p => p.StudyCode)
                .HasConversion(c => c.Value, v => new StudyCode(v))
                .HasMaxLength(255)
                .IsRequired();
            pv.Property(p => p.VersionTag).IsRequired();
            pv.Property(p => p.VersionIndex);
        });

        // Shadow EF concurrency token — distinct from SiteComplianceCache.Version (MIG-SUBJ-012), which is
        // domain data, not an EF concurrency token. Mirrors ClinicalSubjectConfiguration's `_version`
        // (MIG-SUBJ-011): HasDefaultValue(0L) supplies the starting value Npgsql/SQLite don't auto-compute
        // the way SQL Server's native rowversion does, so the non-nullable shadow column doesn't violate
        // its own NOT NULL constraint on the very first insert. See that configuration's comment for the
        // full rationale.
        builder.Property<long>("_version").IsRowVersion().HasDefaultValue(0L);
    }
}
