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

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(x => x.SubjectCode)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.SubjectCode })
            .HasDatabaseName("IX_subject_consents_tenant_id_SubjectCode");

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
        // (MIG-SUBJ-011/013): `.IsConcurrencyToken()` alone, deliberately not `.IsRowVersion()`, so EF always
        // sends this property's value explicitly rather than treating it as store-generated (see that
        // configuration's comment for the full rationale) — required for MIG-SUBJ-013's
        // `ConcurrencyTokenIncrementInterceptor` app-level auto-increment-on-update fix to actually take
        // effect. HasDefaultValue(0L) supplies the starting value for a brand-new row's INSERT.
        builder.Property<long>("_version").IsConcurrencyToken().HasDefaultValue(0L);
    }
}
