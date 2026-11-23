using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Audit entries written through <see cref="GxPAuditInterceptor"/> must still verify after a round trip through
/// PostgreSQL, which stores timestamps to the microsecond (#4768). Skipped unless a PostgreSQL source is
/// configured (see <see cref="ExecutionPostgresFixture"/>).
/// </summary>
public sealed class AuditChainPostgresRoundTripTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    private sealed class PostgresCommandRecorder : DbCommandInterceptor
    {
        public sealed record CapturedCommand(string CommandText, IReadOnlyList<(string Name, object? Value)> Parameters);

        public List<CapturedCommand> Commands { get; } = [];

        public void Clear() => Commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var parameters = new List<(string Name, object? Value)>(command.Parameters.Count);
            foreach (DbParameter parameter in command.Parameters)
            {
                parameters.Add((parameter.ParameterName, parameter.Value));
            }

            Commands.Add(new CapturedCommand(command.CommandText, parameters));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private static ExecutionDbContext CreateRecordedContext(string connectionString, PostgresCommandRecorder recorder)
    {
        var tenantAccessor = new CurrentTenantAccessor(new DeterministicTenantCodeResolver());
        tenantAccessor.UseNonHttpSeedingFallbackTenant("tenant-test");

        var optionsBuilder = new DbContextOptionsBuilder<ExecutionDbContext>();
        optionsBuilder.UseNpgsql(CadenceDatabaseExtensions.WithModuleSchema(connectionString, ExecutionDbContextFactory.Schema));
        optionsBuilder.UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true);
        optionsBuilder.AddInterceptors(
            StandaloneTenantConnectionInterceptor.Instance,
            StandaloneTenantSaveChangesInterceptor.Instance,
            recorder,
            new GxPAuditInterceptor(() => "svc:integration-test", SystemClock.Instance));
        return new ExecutionDbContext(optionsBuilder.Options);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-003")]
    public async Task ChainWrittenToPostgres_VerifiesWhenReadBack()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string connectionString = fixture.ConnectionString!;
        string code = $"SUBJ-RT-{Guid.NewGuid():N}"[..20];

        await using (var context = ExecutionPostgresFixture.CreateDbContext(connectionString))
        {
            var subject = ClinicalSubject.Create(new SubjectCode(code), new StudyCode("STUDY-RT"), siteCode: null,
                enrollmentIndex: 0, encryptedDemographics: "cipher-1", stratFactors: null);
            context.ClinicalSubjects.Add(subject);
            await context.SaveChangesAsync(ct);

            subject.UpdateDemographics("cipher-2");
            await context.SaveChangesAsync(ct);
        }

        await using var verify = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var entries = await verify.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == nameof(ClinicalSubject))
            .ToListAsync(ct);

        entries.Should().HaveCountGreaterThanOrEqualTo(2);
        AuditLogChainVerifier.Verify(entries).Should().ContainSingle().Which.Valid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-003")]
    public async Task MultiEntitySaveToPostgres_ExecutesSingleIndexedTipPrefetchAndVerifiesAllChains()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string connectionString = fixture.ConnectionString!;
        var recorder = new PostgresCommandRecorder();

        var study = new StudyCode($"ST-{Guid.NewGuid():N}"[..12]);
        var site = new SiteCode($"SI-{Guid.NewGuid():N}"[..12]);
        string code1 = $"SUBJ-B1-{Guid.NewGuid():N}"[..20];
        string code2 = $"SUBJ-B2-{Guid.NewGuid():N}"[..20];
        var signedAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

        SubjectId subjectId;
        SubjectConsentId consentId;
        Guid cacheId;

        await using (var seed = CreateRecordedContext(connectionString, recorder))
        {
            var subject = ClinicalSubject.Create(new SubjectCode(code1), study, siteCode: null,
                enrollmentIndex: 1, encryptedDemographics: "cipher-a", stratFactors: null);
            var consent = SubjectConsent.Create(new SubjectCode(code1), new ProtocolVersionRef(study, "v1.0", 1),
                icfSigned: true, icfSignedDate: signedAt, requiresReconsent: true, isPaperOverride: false);
            var cache = SiteComplianceCache.Create(study, site, "SITE_ACTIVATION", isComplete: false, ["Form-A"]);

            subjectId = subject.Id;
            consentId = consent.Id;
            cacheId = cache.Id;

            seed.ClinicalSubjects.Add(subject);
            seed.SubjectConsents.Add(consent);
            seed.SiteComplianceCaches.Add(cache);

            recorder.Clear();
            await seed.SaveChangesAsync(ct);

            recorder.Commands
                .Where(c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && c.CommandText.Contains("\"AuditLogEntries\"", StringComparison.Ordinal))
                .Should().ContainSingle("initial multi-entity save must prefetch all three EntityName chain tips in one query");
        }

        PostgresCommandRecorder.CapturedCommand capturedTipQuery;
        await using (var update = CreateRecordedContext(connectionString, recorder))
        {
            var subject = await update.ClinicalSubjects.SingleAsync(s => s.Id == subjectId, ct);
            var consent = await update.SubjectConsents.SingleAsync(c => c.Id == consentId, ct);
            var cache = await update.SiteComplianceCaches.SingleAsync(c => c.Id == cacheId, ct);
            var secondSubject = ClinicalSubject.Create(new SubjectCode(code2), study, siteCode: null,
                enrollmentIndex: 2, encryptedDemographics: "cipher-b", stratFactors: null);

            subject.UpdateDemographics("cipher-a2");
            consent.ClearReconsentRequirement();
            cache.ApplyWebhookUpdate(isComplete: true, missingDocuments: ["Form-B"]);
            update.ClinicalSubjects.Add(secondSubject);

            recorder.Clear();
            await update.SaveChangesAsync(ct);

            var tipReads = recorder.Commands
                .Where(c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                    && c.CommandText.Contains("\"AuditLogEntries\"", StringComparison.Ordinal))
                .ToList();
            tipReads.Should().ContainSingle("updating three EntityName chains in one SaveChangesAsync must issue one batched tip query");
            capturedTipQuery = tipReads[0];
        }

        await using (var npgsql = new NpgsqlConnection(CadenceDatabaseExtensions.WithModuleSchema(connectionString, ExecutionDbContextFactory.Schema)))
        {
            await npgsql.OpenAsync(ct);
            await using var tx = await npgsql.BeginTransactionAsync(ct);

            await using (var setupSession = new NpgsqlCommand(
                "SELECT set_config('cadence.tenant_id', @tenantId, true); SET LOCAL enable_seqscan = off;",
                npgsql,
                tx))
            {
                var resolver = new DeterministicTenantCodeResolver();
                Guid tenantGuid = await resolver.ResolveTenantIdAsync("tenant-test", ct) ?? throw new InvalidOperationException("Missing test tenant");
                setupSession.Parameters.AddWithValue("tenantId", tenantGuid.ToString());
                await setupSession.ExecuteNonQueryAsync(ct);
            }

            await using var explainCmd = new NpgsqlCommand("EXPLAIN (COSTS OFF) " + capturedTipQuery.CommandText, npgsql, tx);
            foreach ((string name, object? value) in capturedTipQuery.Parameters)
            {
                explainCmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            var planLines = new List<string>();
            await using (var reader = await explainCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    planLines.Add(reader.GetString(0));
                }
            }

            string planText = string.Join('\n', planLines);
            planText.Should().Contain("IX_AuditLogEntries_EntityName_Timestamp_VersionIndex_Id");
            planText.Should().Contain("IX_AuditLogEntries_EntityName_PredecessorHash");
            planText.Should().NotContain("Sort", "the descending composite sequence index must satisfy ORDER BY without an explicit Sort node");
            planText.Should().NotContain("Seq Scan", "all tip branches must resolve via B-tree index scans");
        }

        await using var verify = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var entries = await verify.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == nameof(ClinicalSubject)
                || e.EntityName == nameof(SubjectConsent)
                || e.EntityName == nameof(SiteComplianceCache))
            .ToListAsync(ct);

        var chainResults = AuditLogChainVerifier.Verify(entries);
        chainResults.Should().HaveCount(3).And.OnlyContain(r => r.Valid);
    }
}
