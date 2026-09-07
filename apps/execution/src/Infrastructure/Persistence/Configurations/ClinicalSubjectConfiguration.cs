using System;
using System.Collections.Generic;
using System.Text.Json;

using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core 10 Fluent API mapping for the <c>clinical_subjects</c> table: strongly-typed-ID/value-object
/// conversions, Rule 5's composite unique index, the <c>strat_factors</c> jsonb column, the
/// <see cref="SubjectStateCodec"/>-driven <c>Status</c> conversion, and the shadow <c>_version</c> EF
/// optimistic-concurrency token. Mirrors <c>02_technical_design.md</c> §4.1 exactly.
/// Replaces <c>apps/execution/database/models/subject.py</c> (<c>ClinicalSubject</c> SQLAlchemy model +
/// Alembic column/index definitions).
/// @req:PRD-SUB-001
/// </summary>
public sealed class ClinicalSubjectConfiguration : IEntityTypeConfiguration<ClinicalSubject>
{
    public void Configure(EntityTypeBuilder<ClinicalSubject> builder)
    {
        builder.ToTable("clinical_subjects");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(id => id.Value, v => new SubjectId(v));

        builder.Property(x => x.SubjectCode)
            .HasConversion(c => c.Value, v => new SubjectCode(v))
            .HasMaxLength(255)
            .IsRequired();
        builder.HasIndex(x => x.SubjectCode).IsUnique();

        builder.Property(x => x.StudyCode)
            .HasConversion(c => c.Value, v => new StudyCode(v))
            .HasMaxLength(255)
            .IsRequired();

        // Rule 5: sequential enrollment-index invariant, enforced here as a DB-level backstop to the
        // application-level advisory lock in MIG-SUBJ-013's EnrollNextIndexAsync. See §4.2.
        builder.HasIndex(x => new { x.StudyCode, x.EnrollmentIndex }).IsUnique();

        builder.Property(x => x.SiteCode).HasConversion(
            c => c.HasValue ? c.Value.Value : null,
            v => v == null ? (SiteCode?)null : new SiteCode(v));

        // A corrupt stored value is a storage-integrity bug, not a normal domain-error path — the failed
        // TryParse throws directly rather than being wrapped in a Result<T>. The `out var`/`throw`
        // parsing logic is factored into ParseStatus rather than written inline because HasConversion's
        // lambdas are compiled as expression trees (for LINQ query translation), and expression trees may
        // not contain an `out` variable declaration or a `throw` expression directly — a plain method call
        // (to a method whose own body is ordinary IL, not an expression tree) is unaffected by that limit.
        builder.Property(x => x.Status)
            .HasConversion(s => SubjectStateCodec.ToCode(s), v => ParseStatus(v))
            .HasMaxLength(32);

        // Rule 2: strat_factors, an arbitrary untyped JSON dict, maps to a native jsonb column. The
        // `Dictionary<string, object?>` value shape has no concrete CLR contract for EF Core's typed
        // `.ToJson()` owned-entity mapping to project onto, so this instead maps the private backing field
        // directly through a value converter (round-tripping via System.Text.Json).
        builder.Property<Dictionary<string, object?>>("_stratFactors")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, object?>>(v, JsonSerializerOptions.Default) ?? new())
            .HasColumnName("strat_factors")
            .HasColumnType("jsonb");

        // Shadow EF concurrency token — distinct from Rule 5's enrollment-index lock above and from
        // SiteComplianceCache.Version (MIG-SUBJ-012), which is domain data, not an EF concurrency token.
        // Configured as `.IsConcurrencyToken()` alone — deliberately NOT `.IsRowVersion()` (shorthand for
        // `.IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()`) — because `ValueGeneratedOnAddOrUpdate`
        // tells EF the *database* produces this value on every write, so EF never includes it in the
        // generated UPDATE column list on a provider with no store-side mechanism to do so (neither
        // Npgsql/PostgreSQL nor SQLite auto-computes an arbitrary column on UPDATE the way SQL Server's
        // native rowversion does): any value an app-level mechanism assigned before saving would be silently
        // discarded by EF's own post-command store-generated-value read-back. Leaving off
        // `ValueGeneratedOnAddOrUpdate()` here leaves `ValueGenerated` at whatever EF's own convention infers
        // from `HasDefaultValue(0L)` below (`ValueGenerated.OnAdd` — a database default fills a brand-new
        // row's INSERT when the column is omitted) — which, unlike `OnAddOrUpdate`, has no effect on UPDATE
        // statements: EF always sends this property's current value explicitly there, which is what lets
        // MIG-SUBJ-013's `ConcurrencyTokenIncrementInterceptor` (the real per-write auto-increment mechanism
        // `IsRowVersion()` alone could not provide on these providers) actually take effect.
        builder.Property<long>("_version").IsConcurrencyToken().HasDefaultValue(0L);

        builder.Property(x => x.EncryptedDemographics).HasMaxLength(int.MaxValue);

        // Rule 1 (LOCKED_FACTOR_MUTATION) is enforced once in the aggregate itself
        // (ClinicalSubject.UpdateStratFactors/UpdateDemographics) — this configuration only maps storage
        // shape, it does not re-enforce the guard; EF Core has no ORM-level `@validates`-equivalent trigger,
        // so the aggregate remains the single enforcement point.
    }

    private static SubjectState ParseStatus(string v) =>
        SubjectStateCodec.TryParse(v, out var state)
            ? state
            : throw new InvalidOperationException($"Corrupt status '{v}' in storage.");
}
