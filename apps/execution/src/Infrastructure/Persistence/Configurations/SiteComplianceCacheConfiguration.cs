using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 Fluent API mapping for the <c>site_compliance_caches</c> table (matching the legacy
/// <c>apps/execution/database/models/compliance.py</c> SQLAlchemy model's exact <c>__tablename__</c>,
/// which the ticket prose's "site_compliance_cache" singular reference does not — see the implementing
/// ticket's report for this deliberate divergence): the <c>_missingDocuments</c> backing field mapped to
/// a native Npgsql <c>text[]</c> column (correcting the legacy comma-joined-<c>string</c> storage
/// artifact — <c>01_spec.md</c> §1.1 documents that join as a storage artifact, not an observable
/// contract, since <c>ComplianceStatusResponse.MissingDocuments</c> is already <c>list&lt;string&gt;</c>
/// on both sides), <see cref="SiteComplianceCache.Version"/> as a plain <c>int</c> column (explicitly
/// <b>not</b> an EF concurrency token — it is the legacy system's own upsert-increment counter, domain
/// data being persisted, not infrastructure), and the separate shadow <c>_version</c> EF
/// optimistic-concurrency token that <i>is</i> the actual guard for <c>SaveChangesAsync</c> (mirroring
/// <see cref="ClinicalSubjectConfiguration"/>'s pattern from MIG-SUBJ-011). Mirrors
/// <c>02_technical_design.md</c> §4.1-§4.3.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SiteComplianceCacheConfiguration : IEntityTypeConfiguration<SiteComplianceCache>
{
    public void Configure(EntityTypeBuilder<SiteComplianceCache> builder)
    {
        builder.ToTable("site_compliance_caches");
        builder.HasKey(x => x.Id);
        // Id is a bare Guid (not a strongly-typed ID like SubjectId/SubjectConsentId) — this reflects the
        // aggregate's actual current shape (MIG-SUBJ-006); EF maps Guid primary keys natively, no
        // HasConversion needed. Introducing a SiteComplianceCacheId wrapper would touch the domain
        // aggregate itself, which is out of scope for this persistence-mapping ticket.

        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.SiteCode)
            .HasConversion(c => c.Value, v => new SiteCode(v))
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.Milestone).HasMaxLength(255).IsRequired();

        // `missing_documents` was a comma-joined string in the legacy SQLAlchemy model — a storage
        // artifact, not an observable API contract (ComplianceStatusResponse.MissingDocuments is already
        // list<string> on both sides). The public MissingDocuments property is a computed read-only view
        // (`_missingDocuments.AsReadOnly()`, not a trivial field return), so it is explicitly ignored here
        // and the private backing field is mapped directly — mirroring ClinicalSubjectConfiguration's
        // treatment of `_stratFactors`. EF Core's native primitive-collection support maps the List<string>
        // field to a genuine Postgres array (no manual delimiter/JSON conversion needed); HasColumnType
        // pins the exact column type string so the mapping is explicit rather than provider-inferred.
        builder.Ignore(x => x.MissingDocuments);
        builder.Property<List<string>>("_missingDocuments")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("missing_documents")
            .HasColumnType("text[]");

        // SiteComplianceCache.Version is the legacy system's OWN plain-integer upsert counter (§4.2 /
        // 01_spec.md §1.1: "if a row exists, its version is incremented by 1 ... not a concurrency token
        // check") — mapped as an ordinary int column. Deliberately NOT [Timestamp]/IsRowVersion(): mixing
        // this domain counter with EF's own concurrency-token mechanism would silently turn the legacy
        // "always overwrite and bump" upsert contract into a compare-and-swap rejection.
        builder.Property(x => x.Version).IsConcurrencyToken(false);

        // Shadow EF concurrency token — the ACTUAL optimistic-concurrency guard for SaveChangesAsync,
        // entirely separate from the domain Version counter above. Mirrors ClinicalSubjectConfiguration's
        // `_version` (MIG-SUBJ-011/013): `.IsConcurrencyToken()` alone, deliberately not `.IsRowVersion()`,
        // so EF always sends this property's value explicitly rather than treating it as store-generated
        // (see that configuration's comment for the full rationale) — required for MIG-SUBJ-013's
        // `ConcurrencyTokenIncrementInterceptor` app-level auto-increment-on-update fix to actually take
        // effect. HasDefaultValue(0L) supplies the starting value for a brand-new row's INSERT.
        builder.Property<long>("_version").IsConcurrencyToken().HasDefaultValue(0L);
    }
}
