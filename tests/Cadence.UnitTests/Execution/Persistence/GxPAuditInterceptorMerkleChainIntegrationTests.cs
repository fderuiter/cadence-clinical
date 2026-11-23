using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// MIG-SUBJ-027 item 6 (`02_technical_design.md` §6.2's final row): the slice-specific re-check that
/// `GxPAuditInterceptorMerkleChainTests` (MIG-SUBJ-007's own coverage) does not perform, because that file's
/// fixtures are a minimal purpose-built <c>DbContext</c> over a generic test-double aggregate
/// (<c>GuidKeyedAggregate</c>/<c>RawAuditableEntity</c>), never this slice's actual
/// <see cref="ClinicalSubject"/>/<see cref="SubjectConsent"/>/<see cref="SiteComplianceCache"/> aggregates
/// persisted through the real <see cref="ExecutionDbContext"/>. This file re-proves the same guarantee — two
/// sequential <c>SaveChangesAsync</c> calls against the same tracked instance produce a second
/// <see cref="AuditLogEntry"/> whose <see cref="AuditLogEntry.PredecessorHash"/> equals the first's
/// <see cref="AuditLogEntry.BlockHash"/> — independently for each of the three aggregates, confirming
/// <c>GxPAuditInterceptor</c>'s per-<c>EntityName</c> chaining design (`packages/database/src/GxPAuditInterceptor.cs`)
/// actually holds for this slice's real entity types, not just the abstract case.
///
/// <para>
/// Provider choice per aggregate matches this same directory's own established, documented convention:
/// <see cref="ClinicalSubject"/> and <see cref="SiteComplianceCache"/> use the InMemory provider, exactly as
/// <c>ClinicalSubjectConfigurationTests</c>/<c>SiteComplianceCacheConfigurationTests</c> already do for their
/// own round-trip/interceptor tests. <see cref="SubjectConsent"/> instead uses SQLite, because its
/// <see cref="SubjectConsent.ProtocolVersion"/> <c>ComplexProperty</c> (with a <c>HasConversion</c> member)
/// throws <see cref="System.Collections.Generic.KeyNotFoundException"/> against the InMemory provider's
/// materialization pipeline — <c>SubjectConsentConfigurationTests</c>'s own documented reason for the
/// identical choice.
/// </para>
/// </summary>
public class GxPAuditInterceptorMerkleChainIntegrationTests
{
    private static readonly StudyCode Study = new("STUDY-MERKLE");
    private static readonly SiteCode Site = new("SITE-MERKLE");

    private static readonly DateTimeOffset SignedDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ExecutionDbContext CreateInMemoryContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => "qa.reviewer@site.org", SystemClock.Instance))
            .Options);

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateSqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new GxPAuditInterceptor(() => "qa.reviewer@site.org", SystemClock.Instance))
            .Options);

    private static async Task<AuditLogEntry[]> LoadChainAsync(ExecutionDbContext context, string entityName)
    {
        var entries = await context.AuditLogEntries
            .Where(e => e.EntityName == entityName)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(TestContext.Current.CancellationToken);
        return entries.ToArray();
    }

    // ---- ClinicalSubject ---------------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenClinicalSubjectPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash()
    {
        await using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-MERKLE-001"), Study, siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: null, stratFactors: null);

        context.ClinicalSubjects.Add(subject);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await LoadChainAsync(context, nameof(ClinicalSubject));

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty("the first block for a fresh EntityName chain has no predecessor");
        entries[0].Action.Should().Be("Added");
        entries[0].EntityId.Should().Be(subject.Id.ToString());
        entries[1].Action.Should().Be("Modified");
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash,
            "the second save's audit entry must chain to the first save's block hash for the same real ClinicalSubject instance");
        entries[1].BlockHash.Should().NotBe(entries[0].BlockHash);
    }

    // ---- SubjectConsent -----------------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenSubjectConsentPersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash()
    {
        using var connection = OpenSqliteConnection();
        await using (var setupContext = CreateSqliteContext(connection))
        {
            await setupContext.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateSqliteContext(connection);
        var consent = SubjectConsent.Create(
            new SubjectCode("SUBJ-MERKLE-002"), new ProtocolVersionRef(Study, "v1.0", 1),
            icfSigned: true, icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);

        context.SubjectConsents.Add(consent);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        consent.ClearReconsentRequirement();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await LoadChainAsync(context, nameof(SubjectConsent));

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty("the first block for a fresh EntityName chain has no predecessor");
        entries[0].Action.Should().Be("Added");
        entries[0].EntityId.Should().Be(consent.Id.ToString());
        entries[1].Action.Should().Be("Modified");
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash,
            "the second save's audit entry must chain to the first save's block hash for the same real SubjectConsent instance");
        entries[1].BlockHash.Should().NotBe(entries[0].BlockHash);
    }

    // ---- SiteComplianceCache ------------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenSiteComplianceCachePersistedTwiceInSequence_ChainsSecondEntrysPredecessorHashToFirstsBlockHash()
    {
        await using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var cache = SiteComplianceCache.Create(Study, Site, "SITE_ACTIVATION", isComplete: false, new[] { "Form-A" });

        context.SiteComplianceCaches.Add(cache);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entries = await LoadChainAsync(context, nameof(SiteComplianceCache));

        entries.Should().HaveCount(2);
        entries[0].PredecessorHash.Should().BeEmpty("the first block for a fresh EntityName chain has no predecessor");
        entries[0].Action.Should().Be("Added");
        entries[0].EntityId.Should().Be(cache.Id.ToString());
        entries[1].Action.Should().Be("Modified");
        entries[1].PredecessorHash.Should().Be(entries[0].BlockHash,
            "the second save's audit entry must chain to the first save's block hash for the same real SiteComplianceCache instance");
        entries[1].BlockHash.Should().NotBe(entries[0].BlockHash);
    }

    // ---- Boundary: the three real per-EntityName chains never cross-pollinate ------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChangesAsync_GivenClinicalSubjectAndSiteComplianceCacheModifiedInSameCall_ChainsEachRealAggregateTypeIndependently()
    {
        // ClinicalSubject and SiteComplianceCache both tolerate the InMemory provider (unlike SubjectConsent,
        // see the class doc comment), so this is the pair that can share one context/call to prove the
        // per-EntityName isolation the AC calls out ("since each is a distinct EntityName chain") holds
        // between two genuine slice aggregates, not just the generic-test-double pair
        // GxPAuditInterceptorMerkleChainTests already covers.
        await using var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        var subject = ClinicalSubject.Create(
            new SubjectCode("SUBJ-MERKLE-003"), Study, siteCode: null,
            enrollmentIndex: 2, encryptedDemographics: null, stratFactors: null);
        var cache = SiteComplianceCache.Create(Study, Site, "SITE_ACTIVATION", isComplete: false, new[] { "Form-A" });
        context.ClinicalSubjects.Add(subject);
        context.SiteComplianceCaches.Add(cache);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
        cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: new[] { "Form-B" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var subjectEntries = await LoadChainAsync(context, nameof(ClinicalSubject));
        var cacheEntries = await LoadChainAsync(context, nameof(SiteComplianceCache));

        subjectEntries.Should().HaveCount(2);
        cacheEntries.Should().HaveCount(2);
        subjectEntries[1].PredecessorHash.Should().Be(subjectEntries[0].BlockHash);
        cacheEntries[1].PredecessorHash.Should().Be(cacheEntries[0].BlockHash);
        subjectEntries[1].PredecessorHash.Should().NotBe(cacheEntries[0].BlockHash,
            "ClinicalSubject's chain must never be seeded from SiteComplianceCache's block hash or vice versa");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-003")]
    public async Task SaveChangesAsync_GivenThreeDistinctEntityNameAggregatesInOneSqliteSave_ExecutesSingleIndexedTipLookupAndVerifiesChains()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = OpenSqliteConnection();
        var recorder = new AuditTipCommandRecorder();

        ExecutionDbContext CreateRecordedContext() =>
            new(new DbContextOptionsBuilder<ExecutionDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(recorder, new GxPAuditInterceptor(() => "qa.reviewer@site.org", SystemClock.Instance))
                .Options);

        await using (var setup = CreateSqliteContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(ct);
        }

        SubjectId subjectId;
        SubjectConsentId consentId;
        Guid cacheId;

        await using (var seedContext = CreateRecordedContext())
        {
            var subject = ClinicalSubject.Create(
                new SubjectCode("SUBJ-MERKLE-BATCH-1"), Study, siteCode: null,
                enrollmentIndex: 10, encryptedDemographics: null, stratFactors: null);
            var consent = SubjectConsent.Create(
                new SubjectCode("SUBJ-MERKLE-BATCH-1"), new ProtocolVersionRef(Study, "v1.0", 1),
                icfSigned: true, icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);
            var cache = SiteComplianceCache.Create(Study, Site, "SITE_ACTIVATION", isComplete: false, ["Form-A"]);

            subjectId = subject.Id;
            consentId = consent.Id;
            cacheId = cache.Id;

            seedContext.ClinicalSubjects.Add(subject);
            seedContext.SubjectConsents.Add(consent);
            seedContext.SiteComplianceCaches.Add(cache);

            recorder.Clear();
            await seedContext.SaveChangesAsync(ct);

            var genesisTipReads = recorder.Commands
                .Where(c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && c.CommandText.Contains("AuditLogEntries", StringComparison.Ordinal))
                .ToList();
            genesisTipReads.Should().ContainSingle("all three EntityName chains must be prefetched in one batched query on initial insert");
        }

        await using (var updateContext = CreateRecordedContext())
        {
            var subject = await updateContext.ClinicalSubjects.SingleAsync(s => s.Id == subjectId, ct);
            var consent = await updateContext.SubjectConsents.SingleAsync(c => c.Id == consentId, ct);
            var cache = await updateContext.SiteComplianceCaches.SingleAsync(c => c.Id == cacheId, ct);
            var secondSubject = ClinicalSubject.Create(
                new SubjectCode("SUBJ-MERKLE-BATCH-2"), Study, siteCode: null,
                enrollmentIndex: 11, encryptedDemographics: null, stratFactors: null);

            subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
            consent.ClearReconsentRequirement();
            cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: ["Form-B"]);
            updateContext.ClinicalSubjects.Add(secondSubject);

            recorder.Clear();
            await updateContext.SaveChangesAsync(ct);

            var tipReads = recorder.Commands
                .Where(c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && c.CommandText.Contains("AuditLogEntries", StringComparison.Ordinal))
                .ToList();
            tipReads.Should().ContainSingle("saving three distinct EntityName chains (including multiple ClinicalSubject mutations) must execute one tip-prefetch query");

            var planDetails = await AuditTipCommandRecorder.ExplainSqlitePlanAsync(connection, tipReads[0], ct);
            AuditTipCommandRecorder.AssertSqlitePlanUsesOnlyIndexSeeks(
                planDetails,
                "IX_AuditLogEntries_EntityName_Timestamp_VersionIndex_Id",
                "IX_AuditLogEntries_EntityName_PredecessorHash");
        }

        await using var verifyContext = CreateSqliteContext(connection);
        var allEntries = await verifyContext.AuditLogEntries.AsNoTracking().ToListAsync(ct);
        allEntries.Should().HaveCount(7);

        var chainResults = AuditLogChainVerifier.Verify(allEntries);
        chainResults.Should().HaveCount(3);
        chainResults.Should().OnlyContain(r => r.Valid);
    }
}
