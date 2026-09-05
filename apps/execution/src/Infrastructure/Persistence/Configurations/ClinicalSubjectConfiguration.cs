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
        // `IsRowVersion()` alone is sufficient on SQL Server, where the database natively computes a new
        // value for every write; Npgsql/SQLite have no equivalent automatic mechanism, so with no starting
        // value at all EF's generated INSERT omits this store-generated column and the (non-nullable
        // `long`) column violates its own NOT NULL constraint on the very first insert. HasDefaultValue(0)
        // supplies that missing starting value so inserts succeed; it does not, by itself, make the token
        // auto-increment on UPDATE the way SQL Server's rowversion does — that requires a follow-up
        // mechanism (e.g. a Postgres trigger, an app-level pre-save increment, or Npgsql's native `xmin`
        // system column) that is not part of this ticket's acceptance criteria and is left to whichever
        // ticket first exercises concurrent-update conflict detection (§4.2 assigns the
        // DbUpdateConcurrencyException retry policy to MIG-SUBJ-013's repository layer).
        builder.Property<long>("_version").IsRowVersion().HasDefaultValue(0L);

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
