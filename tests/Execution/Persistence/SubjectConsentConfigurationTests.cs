using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-012`'s <see cref="Cadence.Apps.Execution.Infrastructure.Persistence.Configurations.SubjectConsentConfiguration"/>:
/// EF Core mapping shape (table name, the shadow `_version` concurrency token) and the
/// <see cref="ProtocolVersionRef"/> Complex Type round-trip the ticket's acceptance criteria require.
/// Every test here uses SQLite (a real relational provider), not InMemory: the InMemory provider's
/// query-shaping pipeline throws a <see cref="System.Collections.Generic.KeyNotFoundException"/> when
/// materializing a <c>ComplexProperty</c> member that carries a <c>HasConversion</c> (reproduced against
/// EF Core 10.0.11's InMemory provider — a provider limitation, not a mapping defect, since SQLite/Npgsql
/// materialize the identical model correctly), so relational metadata and round-trip queries both need a
/// real relational provider here, unlike `ClinicalSubjectConfigurationTests`' split.
/// @req:PRD-SUB-001
/// </summary>
public class SubjectConsentConfigurationTests
{
    private static readonly ProtocolVersionRef SampleVersion = new(new StudyCode("STUDY-CONSENT-RT"), "v3.2", 4);

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateSqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);

    private static IEntityType RequireEntityType(ExecutionDbContext context) =>
        context.Model.FindEntityType(typeof(SubjectConsent))
            ?? throw new InvalidOperationException("SubjectConsent entity type not found in model.");

    private static IProperty RequireProperty(IEntityType entityType, string name) =>
        entityType.FindProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {entityType.Name}.");

    private static SubjectConsent CreateConsent(ProtocolVersionRef? version = null) =>
        SubjectConsent.Create(
            new SubjectCode("SUBJ-CONSENT-RT-001"), version ?? SampleVersion, icfSigned: true,
            icfSignedDate: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            requiresReconsent: false, isPaperOverride: false, new FakeClock());

    // ---- Configuration shape (metadata) ----------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Configure_MapsSubjectConsentToSubjectConsentsTable()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        entityType.GetTableName().Should().Be("subject_consents");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Configure_MapsShadowVersionPropertyAsConcurrencyToken()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, "_version");

        // The EF optimistic-concurrency token — distinct from SiteComplianceCache.Version (MIG-SUBJ-012),
        // which is domain data, not a concurrency token.
        property.IsConcurrencyToken.Should().BeTrue();
        property.IsShadowProperty().Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Configure_ProtocolVersionIsMappedAsComplexProperty()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        // EF Core 10's ComplexProperty, not OwnsOne — ProtocolVersionRef has no independent PK (§4.3), so
        // it must appear among the entity type's complex properties, not its navigations.
        entityType.GetComplexProperties().Select(p => p.Name).Should().Contain(nameof(SubjectConsent.ProtocolVersion));
        entityType.GetNavigations().Should().BeEmpty();
    }

    // ---- Round-trip persistence -------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task SubjectConsents_GivenPersistedConsent_RoundTripsProtocolVersionExactly()
    {
        // SQLite (a real relational provider), not InMemory: the InMemory provider's query-shaping
        // pipeline throws a KeyNotFoundException materializing a ComplexProperty member that carries a
        // HasConversion (a known InMemory-provider limitation, not a mapping defect — SQLite/Npgsql
        // materialize the identical model correctly).
        using var connection = OpenSqliteConnection();
        await using (var setupContext = CreateSqliteContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var original = CreateConsent();
        await using (var writeContext = CreateSqliteContext(connection))
        {
            writeContext.SubjectConsents.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateSqliteContext(connection);
        var reloaded = await readContext.SubjectConsents.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.ProtocolVersion.StudyCode.Should().Be(SampleVersion.StudyCode);
        reloaded.ProtocolVersion.VersionTag.Should().Be(SampleVersion.VersionTag);
        reloaded.ProtocolVersion.VersionIndex.Should().Be(SampleVersion.VersionIndex);
        reloaded.ProtocolVersion.Should().Be(original.ProtocolVersion);
        reloaded.SubjectCode.Should().Be(original.SubjectCode);
        reloaded.IcfSigned.Should().Be(original.IcfSigned);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task SubjectConsents_GivenResubmissionMergeBeforeSave_PersistsUpdatedProtocolVersion()
    {
        // SQLite, not InMemory — see the comment on the round-trip test above for why.
        using var connection = OpenSqliteConnection();
        await using (var setupContext = CreateSqliteContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var original = CreateConsent();
        var incoming = new ProtocolVersionRef(new StudyCode("STUDY-CONSENT-RT"), "v4.0", 5);
        original.MergeResubmission("v4.0", icfSigned: true, icfSignedDate: null,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: incoming, new FakeClock());

        await using (var writeContext = CreateSqliteContext(connection))
        {
            writeContext.SubjectConsents.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateSqliteContext(connection);
        var reloaded = await readContext.SubjectConsents.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.ProtocolVersion.Should().Be(incoming);
    }

    /// <summary>Deterministic <see cref="IClock"/> test double, matching `SubjectConsentTests`' own
    /// convention of a private fake rather than modifying <c>IClock</c> itself.</summary>
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
