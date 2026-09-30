using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-011`'s <see cref="ExecutionDbContext"/>/<see cref="Cadence.Apps.Execution.Infrastructure.Persistence.Configurations.ClinicalSubjectConfiguration"/>:
/// EF Core mapping shape (indexes, conversions, the `_stratFactors` jsonb column, the shadow `_version`
/// concurrency token) and the round-trip/relational-provider behaviors the ticket's acceptance criteria
/// require. Round-trip and GxP-interceptor tests use the InMemory provider (cheap, and value converters
/// run identically regardless of provider). Metadata-shape assertions instead use SQLite — relational
/// metadata reads such as <c>GetColumnType()</c> resolve a <c>RelationalTypeMapping</c> internally and
/// throw <see cref="InvalidCastException"/> against the InMemory provider's non-relational type mapping —
/// as do the duplicate-enrollment-index and corrupted-status cases, the relational provider the ticket
/// names as sufficient for unique-index enforcement and raw-SQL corruption. Full Postgres-specific
/// behavior (e.g. `jsonb` operators) is exercised by MIG-SUBJ-028's Testcontainers integration tests, not
/// here.
/// </summary>
public class ClinicalSubjectConfigurationTests
{
    private static ExecutionDbContext CreateInMemoryContext(string databaseName, GxPAuditInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ExecutionDbContext>().UseInMemoryDatabase(databaseName);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new ExecutionDbContext(builder.Options);
    }

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateSqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);

    private static IEntityType RequireEntityType(ExecutionDbContext context) =>
        context.Model.FindEntityType(typeof(ClinicalSubject))
            ?? throw new InvalidOperationException("ClinicalSubject entity type not found in model.");

    private static IProperty RequireProperty(IEntityType entityType, string name) =>
        entityType.FindProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {entityType.Name}.");

    // ---- Configuration shape (metadata) ----------------------------------------------------------

    [Fact]
    public void Configure_MapsClinicalSubjectToClinicalSubjectsTable()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        entityType.GetTableName().Should().Be("clinical_subjects");
    }

    [Fact]
    public void Configure_HasUniqueIndexOnSubjectCode()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        entityType.GetIndexes().Should().Contain(i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "SubjectCode" }));
    }

    [Fact]
    public void Configure_HasCompositeUniqueIndexOnStudyCodeAndEnrollmentIndex()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        // Rule 5's sequential-enrollment-index invariant, DB-level backstop to the app-level advisory
        // lock (MIG-SUBJ-013).
        entityType.GetIndexes().Should().Contain(i =>
            i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StudyCode", "EnrollmentIndex" }));
    }

    [Fact]
    public void Configure_HasCompositeIndexOnStudyCodeSiteCodeStatusAndEnrollmentIndex()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        entityType.GetIndexes().Should().Contain(i =>
            !i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StudyCode", "SiteCode", "Status", "EnrollmentIndex" }));
    }

    [Fact]
    public void Configure_HasCompositeIndexOnStudyCodeStatusAndEnrollmentIndex()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        entityType.GetIndexes().Should().Contain(i =>
            !i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "StudyCode", "Status", "EnrollmentIndex" }));
    }

    [Fact]
    public void Configure_MapsStratFactorsBackingFieldToJsonbColumn()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, "_stratFactors");

        property.GetColumnName().Should().Be("strat_factors");
        property.GetColumnType().Should().Be("jsonb");
        property.GetPropertyAccessMode().Should().Be(PropertyAccessMode.Field);
    }

    [Fact]
    public void Configure_MapsShadowVersionPropertyAsConcurrencyToken()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, "_version");

        // The EF optimistic-concurrency token — distinct from Rule 5's enrollment-index lock and from
        // SiteComplianceCache.Version (MIG-SUBJ-012), which is domain data, not a concurrency token.
        property.IsConcurrencyToken.Should().BeTrue();
        property.IsShadowProperty().Should().BeTrue();
    }

    [Fact]
    public void Configure_MapsStatusWithMaxLength32()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, nameof(ClinicalSubject.Status));

        property.GetMaxLength().Should().Be(32);
    }

    [Fact]
    public void Configure_MapsEncryptedDemographicsWithUnboundedMaxLength()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, nameof(ClinicalSubject.EncryptedDemographics));

        property.GetMaxLength().Should().Be(int.MaxValue);
    }

    // ---- Round-trip persistence -------------------------------------------------------------------

    [Fact]
    public async Task ClinicalSubjects_GivenPersistedSubjectWithSiteCode_RoundTripsCoreFieldsExactly()
    {
        var databaseName = Guid.NewGuid().ToString();
        var stratFactors = new Dictionary<string, object?>
        {
            ["ageBand"] = "18-40",
            ["armPreference"] = 2,
        };
        var original = ClinicalSubject.Create(
            new SubjectCode("SUBJ-RT-001"), new StudyCode("STUDY-RT"), new SiteCode("SITE-09"),
            enrollmentIndex: 3, encryptedDemographics: "cipher-blob", stratFactors: stratFactors);
        var transition = original.TransitionTo(SubjectState.Enrolled);
        transition.IsSuccess.Should().BeTrue();

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.ClinicalSubjects.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.ClinicalSubjects.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.SubjectCode.Should().Be(original.SubjectCode);
        reloaded.StudyCode.Should().Be(original.StudyCode);
        reloaded.SiteCode.Should().Be(original.SiteCode);
        reloaded.Status.Should().Be(SubjectState.Enrolled);
        JsonNode.DeepEquals(
            JsonNode.Parse(JsonSerializer.Serialize(original.StratFactors)),
            JsonNode.Parse(JsonSerializer.Serialize(reloaded.StratFactors))).Should().BeTrue();
    }

    [Fact]
    public async Task ClinicalSubjects_GivenPersistedSubjectWithNullSiteCode_RoundTripsNullExactly()
    {
        var databaseName = Guid.NewGuid().ToString();
        var original = ClinicalSubject.Create(
            new SubjectCode("SUBJ-RT-002"), new StudyCode("STUDY-RT-NULL"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: null, stratFactors: null);

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.ClinicalSubjects.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.ClinicalSubjects.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.SiteCode.Should().BeNull();
        reloaded.SubjectCode.Should().Be(original.SubjectCode);
        reloaded.StudyCode.Should().Be(original.StudyCode);
        reloaded.Status.Should().Be(SubjectState.Screening);
    }

    [Fact]
    public async Task ClinicalSubjects_GivenStratFactorsWithNestedObjectsAndArrays_RoundTripsWithoutDataLoss()
    {
        var databaseName = Guid.NewGuid().ToString();
        var stratFactors = new Dictionary<string, object?>
        {
            ["ageBand"] = "40-65",
            ["armPreference"] = 2,
            ["site"] = new Dictionary<string, object?> { ["code"] = "SITE-07", ["tier"] = 2 },
            ["priorTreatments"] = new List<object?> { "DrugA", "DrugB" },
            ["flags"] = new Dictionary<string, object?> { ["highRisk"] = true, ["note"] = null },
        };
        var original = ClinicalSubject.Create(
            new SubjectCode("SUBJ-NESTED"), new StudyCode("STUDY-NESTED"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: null, stratFactors: stratFactors);

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.ClinicalSubjects.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.ClinicalSubjects.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // `_stratFactors` values round-trip through JSON, not through their original CLR types (an
        // untyped `Dictionary<string, object?>` has no CLR shape for System.Text.Json to restore beyond
        // `JsonElement`), so fidelity is asserted at the JSON-document level rather than via CLR
        // structural equality.
        var expected = JsonNode.Parse(JsonSerializer.Serialize(stratFactors));
        var actual = JsonNode.Parse(JsonSerializer.Serialize(reloaded.StratFactors));
        JsonNode.DeepEquals(expected, actual).Should().BeTrue();
    }

    // ---- GxP audit interceptor wiring (MIG-SUBJ-007 regression) -----------------------------------

    [Fact]
    public async Task SaveChangesAsync_GivenClinicalSubjectAddedWithGxPAuditInterceptorRegistered_PopulatesAuditFields()
    {
        await using var context = CreateInMemoryContext(
            Guid.NewGuid().ToString(), new GxPAuditInterceptor(() => "qa.reviewer@site.org", SystemClock.Instance));
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-AUDIT-001"), new StudyCode("STUDY-AUDIT"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: null, stratFactors: null);

        context.ClinicalSubjects.Add(subject);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        subject.CreatedAt.Should().NotBe(default);
        subject.CreatedBy.Should().Be("qa.reviewer@site.org");
        subject.VersionIndex.Should().Be(1);
    }

    // ---- Relational-provider behaviors (SQLite) ----------------------------------------------------

    [Fact]
    public async Task SaveChangesAsync_GivenTwoSubjectsWithSameStudyCodeAndEnrollmentIndex_FailsOnUniqueIndex()
    {
        using var connection = OpenSqliteConnection();
        await using (var setupContext = CreateSqliteContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateSqliteContext(connection);
        var first = ClinicalSubject.Create(
            new SubjectCode("SUBJ-DUP-100"), new StudyCode("STUDY-DUP"), siteCode: null,
            enrollmentIndex: 7, encryptedDemographics: null, stratFactors: null);
        var second = ClinicalSubject.Create(
            new SubjectCode("SUBJ-DUP-101"), new StudyCode("STUDY-DUP"), siteCode: null,
            enrollmentIndex: 7, encryptedDemographics: null, stratFactors: null);
        context.ClinicalSubjects.AddRange(first, second);

        var act = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Both inserts are staged in the same ChangeTracker/SaveChangesAsync call, i.e. the same
        // transaction; the second violates the (StudyCode, EnrollmentIndex) unique index.
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ClinicalSubjects_GivenCorruptedStatusValueInStorage_ThrowsInvalidOperationExceptionOnMaterialization()
    {
        using var connection = OpenSqliteConnection();
        await using (var writeContext = CreateSqliteContext(connection))
        {
            await writeContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var subject = ClinicalSubject.Create(
                new SubjectCode("SUBJ-CORRUPT"), new StudyCode("STUDY-CORRUPT"), siteCode: null,
                enrollmentIndex: 1, encryptedDemographics: null, stratFactors: null);
            writeContext.ClinicalSubjects.Add(subject);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var corruptingContext = CreateSqliteContext(connection))
        {
            // Bypasses the Status value converter entirely, simulating storage-level corruption (e.g. a
            // manual data fix or a bug elsewhere) rather than anything reachable through the domain API.
            await corruptingContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE clinical_subjects SET status = 'NOT_A_REAL_STATUS'",
                TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateSqliteContext(connection);
        var act = async () => await readContext.ClinicalSubjects.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Corrupt status *");
    }
}
