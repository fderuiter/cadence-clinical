using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-012`'s <see cref="Cadence.Apps.Execution.Infrastructure.Persistence.Configurations.SiteComplianceCacheConfiguration"/>:
/// EF Core mapping shape (table name, the native `text[]` `missing_documents` column, and the two
/// distinct version-like fields) plus the round-trip/boundary behaviors the ticket's acceptance criteria
/// require. Round-trip and boundary tests use the InMemory provider (cheap, and the primitive-collection
/// mapping for `_missingDocuments` runs identically regardless of provider). Metadata-shape assertions —
/// column name/type, concurrency-token flags — use SQLite, since relational metadata reads resolve a
/// `RelationalTypeMapping` internally and are meaningless (or throw) against the InMemory provider's
/// non-relational type mapping, matching `ClinicalSubjectConfigurationTests`' own convention.
/// </summary>
public class SiteComplianceCacheConfigurationTests
{
    private static readonly StudyCode Study = new("STUDY-COMPLIANCE-RT");
    private static readonly SiteCode Site = new("SITE-RT-01");
    private const string Milestone = "SITE_ACTIVATION";

    private static ExecutionDbContext CreateInMemoryContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateSqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);

    private static IEntityType RequireEntityType(ExecutionDbContext context) =>
        context.Model.FindEntityType(typeof(SiteComplianceCache))
            ?? throw new InvalidOperationException("SiteComplianceCache entity type not found in model.");

    private static IProperty RequireProperty(IEntityType entityType, string name) =>
        entityType.FindProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' not found on {entityType.Name}.");

    // ---- Configuration shape (metadata) ----------------------------------------------------------

    [Fact]
    public void Configure_MapsSiteComplianceCacheToSiteComplianceCachesTable()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        // Matches the legacy apps/execution/database/models/compliance.py __tablename__ exactly
        // ("site_compliance_caches", plural) — see this configuration's own doc comment for why this
        // diverges from the ticket prose's singular "site_compliance_cache".
        entityType.GetTableName().Should().Be("site_compliance_caches");
    }

    [Fact]
    public void Configure_MapsMissingDocumentsBackingFieldToNativeTextArrayColumn()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var property = RequireProperty(entityType, "_missingDocuments");

        // Not a delimited text scalar (the legacy comma-joined-string storage artifact) — a native array
        // column, matching the ticket's explicit correction.
        property.GetColumnName().Should().Be("missing_documents");
        property.GetColumnType().Should().Be("text[]");
        property.GetPropertyAccessMode().Should().Be(PropertyAccessMode.Field);
    }

    [Fact]
    public void Configure_IgnoresComputedMissingDocumentsPropertyItself()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);

        // Only the backing field is mapped; the computed IReadOnlyList<string> view property must not
        // also appear in the model (that would be an ambiguous double-mapping of the same data).
        entityType.FindProperty(nameof(SiteComplianceCache.MissingDocuments)).Should().BeNull();
    }

    [Fact]
    public void Configure_MapsVersionAsPlainIntThatIsNotAConcurrencyToken()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var domainVersion = RequireProperty(entityType, nameof(SiteComplianceCache.Version));

        // SiteComplianceCache.Version is the legacy system's own upsert-increment counter (domain data
        // being persisted) — explicitly NOT [Timestamp]/IsRowVersion(). This is the ticket's headline
        // acceptance criterion: distinguishing this field from the separate shadow `_version` EF token.
        domainVersion.IsConcurrencyToken.Should().BeFalse();
        domainVersion.IsShadowProperty().Should().BeFalse();
        domainVersion.ClrType.Should().Be(typeof(int));
    }

    [Fact]
    public void Configure_MapsShadowVersionPropertyAsTheActualConcurrencyToken()
    {
        using var connection = OpenSqliteConnection();
        using var context = CreateSqliteContext(connection);
        var entityType = RequireEntityType(context);
        var shadowVersion = RequireProperty(entityType, "_version");

        // The real EF optimistic-concurrency guard for SaveChangesAsync — entirely separate from the
        // domain Version counter asserted false above, proving the two are not conflated.
        shadowVersion.IsConcurrencyToken.Should().BeTrue();
        shadowVersion.IsShadowProperty().Should().BeTrue();
        shadowVersion.ClrType.Should().Be(typeof(long));
    }

    // ---- Round-trip persistence -------------------------------------------------------------------

    [Fact]
    public async Task SiteComplianceCaches_GivenMissingDocuments_RoundTripsAsNativeArrayInOrder()
    {
        var databaseName = Guid.NewGuid().ToString();
        var original = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "Form-A", "Form-B" });

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.SiteComplianceCaches.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.MissingDocuments.Should().Equal("Form-A", "Form-B");
        reloaded.StudyCode.Should().Be(original.StudyCode);
        reloaded.SiteCode.Should().Be(original.SiteCode);
        reloaded.Milestone.Should().Be(original.Milestone);
        reloaded.IsComplete.Should().Be(original.IsComplete);
        reloaded.Version.Should().Be(original.Version);
    }

    [Fact]
    public async Task SiteComplianceCaches_GivenEmptyMissingDocuments_RoundTripsAsEmptyArray()
    {
        var databaseName = Guid.NewGuid().ToString();
        var original = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: true, Array.Empty<string>());

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.SiteComplianceCaches.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.MissingDocuments.Should().BeEmpty();
    }

    // ---- Boundary: two ApplyWebhookUpdate calls, one SaveChangesAsync ------------------------------

    [Fact]
    public async Task SiteComplianceCaches_GivenVersionIncrementedTwiceBeforeOneSaveChangesAsync_PersistsFinalValueNotIntermediate()
    {
        var databaseName = Guid.NewGuid().ToString();
        var cache = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: false, new[] { "Form-A" });

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.SiteComplianceCaches.Add(cache);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var updateContext = CreateInMemoryContext(databaseName))
        {
            var tracked = await updateContext.SiteComplianceCaches.SingleAsync(TestContext.Current.CancellationToken);

            // Two separate calls, one SaveChangesAsync — the acceptance criterion's exact scenario.
            tracked.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
            tracked.ApplyWebhookUpdate(isComplete: false, missingDocuments: new[] { "Form-C" });

            await updateContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // 1 (Create) + 1 + 1 = 3, never the intermediate value (2) a mid-batch flush would leave behind.
        reloaded.Version.Should().Be(3);
        reloaded.MissingDocuments.Should().Equal("Form-C");
        reloaded.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task SiteComplianceCaches_GivenActivatedState_RoundTripsActivationMetadata()
    {
        var databaseName = Guid.NewGuid().ToString();
        var activatedAt = new DateTimeOffset(2026, 11, 20, 14, 30, 0, TimeSpan.Zero);
        var original = SiteComplianceCache.Create(Study, Site, Milestone, isComplete: true, Array.Empty<string>());
        original.MarkActivated(activatedAt, "sponsor-dm-42");

        await using (var writeContext = CreateInMemoryContext(databaseName))
        {
            writeContext.SiteComplianceCaches.Add(original);
            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = CreateInMemoryContext(databaseName);
        var reloaded = await readContext.SiteComplianceCaches.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        reloaded.IsActivated.Should().BeTrue();
        reloaded.ActivatedAt.Should().Be(activatedAt);
        reloaded.ActivatedBy.Should().Be("sponsor-dm-42");
    }
}
