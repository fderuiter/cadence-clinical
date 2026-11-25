using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Application.Standards;
using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Persistence.Validation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Kernel.WorkQueues;
using Cadence.Rules;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Release benchmark of <c>kernel.validation_finding_projections</c> across 25, 100, and 500 runs of 200 detections
/// under PostgreSQL Row-Level Security (ADR-2462 §5, ADR-2465).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Benchmark")]
public sealed class ValidationFindingProjectionBenchmarkPostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output)
    : IClassFixture<KernelPostgresFixture>
{
    private const int FormCount = 200;
    private const string SpecId = "cs.vitals";
    private const string Systolic = "item.sbp";
    private const string Diastolic = "item.dbp";
    private const string PackKey = "vitals-collection-checks";

    /// <summary>
    /// Seeds 200 study forms and appends 500 validation runs of 200 detections (100,000 historical detections),
    /// verifying flat <c>O(DistinctFindings)</c> rows read, detection rows read, latency, and managed allocations across
    /// 25, 100, and 500 runs on <see cref="IValidationFindings.ListAsync"/>, Study Home, form signing, and saves.
    /// </summary>
    [Fact(DisplayName = "Benchmark_FiveHundredRunsOfTwoHundredDetections_ReadsSavesAndReadinessStayFlat", Timeout = 600_000)]
    public async Task BenchmarkFiveHundredRunsOfTwoHundredDetectionsReadsSavesAndReadinessStayFlat()
    {
        fixture.SkipIfUnavailable();

        var ct = TestContext.Current.CancellationToken;
        var interceptor = new RowCountingInterceptor();
        var vaultKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var services = new ServiceCollection()
            .AddSingleton<IClock>(fixture.Clock)
            .AddSingleton<ICurrentActor>(fixture.CurrentActor)
            .AddSingleton<IAuthorizer>(fixture.Authorizer)
            .AddSingleton<IAuthorizationProbe>(fixture.AuthorizationProbe)
            .AddSingleton<INodeVisibility>(fixture.Visibility)
            .AddSingleton<IKernelEventPublisher>(fixture.Publisher)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddKernelPersistence(fixture.ConnectionString!)
            .AddKernelScopeGraph()
            .AddKernelNavigation()
            .AddKernelSimulationInputs()
            .AddKernelSimulation()
            .AddKernelCommandPipeline()
            .AddKernelStandardsPersistence()
            .AddKernelStandards()
            .AddKernelValidationPersistence()
            .AddKernelValidation()
            .AddKernelWorkQueues()
            .AddCadenceRules()
            .AddCadenceCollectionValidation()
            .AddCadenceStandards()
            .AddSingleton<IProtocolFormCompiler, ProtocolFormCompiler>()
            .AddKernelIdentityVault()
            .AddKernelIdentityVaultPersistence(vaultKey, "EU");

        var optionsBuilder = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(optionsBuilder, fixture.ConnectionString!);
        optionsBuilder.AddInterceptors(new KernelTenantConnectionInterceptor(), interceptor);
        services.AddSingleton<IDbContextFactory<KernelDbContext>>(new BenchDbContextFactory(optionsBuilder.Options));

        await using var provider = services.BuildServiceProvider();

        var seedSw = Stopwatch.StartNew();
        var (tenant, study, packId, forms) = await SeedStudyWithFormsAsync(fixture, ct);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Seeded {FormCount} forms in {seedSw.Elapsed.TotalSeconds:F2}s"));

        var milestones = new[] { 25, 100, 500 };
        var currentRuns = 0;
        var listSamplesByMilestone = new Dictionary<int, BenchMetrics>();
        var studyHomeSamplesByMilestone = new Dictionary<int, BenchMetrics>();
        var signGateSamplesByMilestone = new Dictionary<int, BenchMetrics>();
        var saveSamplesByMilestone = new Dictionary<int, BenchMetrics>();

        output.WriteLine(string.Empty);
        output.WriteLine("### Scenario A: 200 Active Findings Across 25, 100, and 500 Historical Runs");
        output.WriteLine("| Runs (R) | Detections in DB | Operation | Total Rows Read | Detection Rows Read | Latency (ms) | Allocated (MB) |");
        output.WriteLine("|---:|---:|---|---:|---:|---:|---:|");

        foreach (var targetRuns in milestones)
        {
            while (currentRuns < targetRuns - 1)
            {
                await AppendCollectionRunAsync(provider, fixture.Clock, tenant, study, packId, forms, activeCount: FormCount, currentRuns + 1, ct);
                currentRuns++;
            }

            await WarmupReadsAsync(provider, tenant, study, forms[0], ct);

            var saveMetrics = await MeasureOnceAsync(interceptor, async () =>
            {
                using var scope = provider.CreateScope();
                var validation = scope.ServiceProvider.GetRequiredService<ICollectionValidationService>();
                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                var res = await validation.ValidateStudyCollectionsAsync(
                    tenant,
                    study,
                    new DateOnly(2026, 11, 25),
                    fixture.Clock.UtcNow,
                    $"bench-save-{targetRuns}",
                    "Post-commit collection validation benchmark",
                    cancellationToken: ct);
                if (!res.IsSuccess)
                {
                    throw new InvalidOperationException(res.Error?.Message);
                }
            });
            currentRuns++;

            var listMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
            {
                using var scope = provider.CreateScope();
                var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
                var page = await findings.ListAsync(
                    tenant,
                    new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = 20 },
                    ct);
                if (page.Findings.Count != 20)
                {
                    throw new InvalidOperationException($"Expected 20 findings, got {page.Findings.Count}");
                }
            });

            var studyHomeMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
            {
                using var scope = provider.CreateScope();
                var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
                var collections = scope.ServiceProvider.GetRequiredService<ICollectionInstanceRecords>();
                var workQueues = scope.ServiceProvider.GetRequiredService<IWorkQueues>();

                var attentionPage = await findings.ListAsync(
                    tenant,
                    new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = FindingQuery.MaxTake },
                    ct);
                var studyForms = await collections.ListForStudyAsync(tenant, study, ct);
                var queues = await workQueues.ReadAsync(tenant, new WorkQueueQuery(NodeRef.For(study)), ct);
                if (attentionPage.Total != FormCount || studyForms.Count != FormCount || !queues.IsSuccess)
                {
                    throw new InvalidOperationException("Unexpected Study Home counts.");
                }
            });

            var signGateMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
            {
                using var scope = provider.CreateScope();
                var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
                var collections = scope.ServiceProvider.GetRequiredService<ICollectionInstanceRecords>();
                var instance = await collections.FindAsync(tenant, forms[0], ct);
                var blockers = await findings.ListAsync(
                    tenant,
                    new FindingQuery(study)
                    {
                        Severity = ValidationSeverity.Error,
                        Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                        TargetKeys = [forms[0].Value.ToString("D")],
                    },
                    ct);
                if (instance is null || blockers.Findings.Count != 1)
                {
                    throw new InvalidOperationException("Unexpected form signing gate result.");
                }
            });

            listSamplesByMilestone[targetRuns] = listMetrics;
            studyHomeSamplesByMilestone[targetRuns] = studyHomeMetrics;
            signGateSamplesByMilestone[targetRuns] = signGateMetrics;
            saveSamplesByMilestone[targetRuns] = saveMetrics;

            var totalDbDetections = currentRuns * FormCount;
            PrintRow(output, currentRuns, totalDbDetections, "ListAsync (Open+Investigating, Take=20)", listMetrics);
            PrintRow(output, currentRuns, totalDbDetections, "Study Home (Attention + WorkQueues)", studyHomeMetrics);
            PrintRow(output, currentRuns, totalDbDetections, "Form Signing Gate (EvidenceReadiness)", signGateMetrics);
            PrintRow(output, currentRuns, totalDbDetections, "ValidateStudyCollectionsAsync (Save)", saveMetrics);
        }

        // Assert strictly flat O(DistinctFindings) row counts across 25 -> 100 -> 500 runs (100,000 detections in DB):
        listSamplesByMilestone[500].DetectionRows.Should().Be(20);
        listSamplesByMilestone[500].TotalRows.Should().Be(listSamplesByMilestone[25].TotalRows);
        studyHomeSamplesByMilestone[500].DetectionRows.Should().Be(studyHomeSamplesByMilestone[25].DetectionRows);
        studyHomeSamplesByMilestone[500].TotalRows.Should().Be(studyHomeSamplesByMilestone[25].TotalRows);
        signGateSamplesByMilestone[500].DetectionRows.Should().Be(1);
        signGateSamplesByMilestone[500].TotalRows.Should().Be(signGateSamplesByMilestone[25].TotalRows);
        saveSamplesByMilestone[500].DetectionRows.Should().Be(FormCount);
        saveSamplesByMilestone[500].TotalRows.Should().Be(saveSamplesByMilestone[25].TotalRows);

        await AppendCollectionRunAsync(provider, fixture.Clock, tenant, study, packId, forms, activeCount: 20, currentRuns + 1, ct);
        currentRuns++;
        var resolvedDbDetections = 500 * FormCount + 20;

        output.WriteLine(string.Empty);
        output.WriteLine("### Scenario B: 20 Active Findings + 180 Resolved Findings After 501 Runs (100,020 Detections)");
        output.WriteLine("| Runs (R) | Detections in DB | Operation | Total Rows Read | Detection Rows Read | Latency (ms) | Allocated (MB) |");
        output.WriteLine("|---:|---:|---|---:|---:|---:|---:|");

        var resolvedListMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
        {
            using var scope = provider.CreateScope();
            var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
            var page = await findings.ListAsync(
                tenant,
                new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = 20 },
                ct);
            if (page.Total != 20 || page.Findings.Count != 20)
            {
                throw new InvalidOperationException($"Expected 20 active findings, got {page.Total}");
            }
        });

        var resolvedStudyHomeMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
        {
            using var scope = provider.CreateScope();
            var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
            var collections = scope.ServiceProvider.GetRequiredService<ICollectionInstanceRecords>();
            var workQueues = scope.ServiceProvider.GetRequiredService<IWorkQueues>();

            var attentionPage = await findings.ListAsync(
                tenant,
                new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = FindingQuery.MaxTake },
                ct);
            var studyForms = await collections.ListForStudyAsync(tenant, study, ct);
            var queues = await workQueues.ReadAsync(tenant, new WorkQueueQuery(NodeRef.For(study)), ct);
            if (attentionPage.Total != 20 || studyForms.Count != FormCount || !queues.IsSuccess)
            {
                throw new InvalidOperationException("Unexpected Study Home counts in Scenario B.");
            }
        });

        var resolvedCleanSignMetrics = await MeasureMedianAsync(interceptor, 5, async () =>
        {
            using var scope = provider.CreateScope();
            var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
            var blockers = await findings.ListAsync(
                tenant,
                new FindingQuery(study)
                {
                    Severity = ValidationSeverity.Error,
                    Statuses = [FindingStatus.Open, FindingStatus.Investigating],
                    TargetKeys = [forms[50].Value.ToString("D")],
                },
                ct);
            if (blockers.Total != 0)
            {
                throw new InvalidOperationException("Expected 0 blockers on resolved form.");
            }
        });

        resolvedListMetrics.DetectionRows.Should().Be(20);
        resolvedCleanSignMetrics.DetectionRows.Should().Be(0);

        PrintRow(output, currentRuns, resolvedDbDetections, "ListAsync (Open+Investigating, Take=20)", resolvedListMetrics);
        PrintRow(output, currentRuns, resolvedDbDetections, "Study Home (20 Active / 180 Resolved)", resolvedStudyHomeMetrics);
        PrintRow(output, currentRuns, resolvedDbDetections, "Form Signing Gate (Resolved Form)", resolvedCleanSignMetrics);
    }

    private static void PrintRow(ITestOutputHelper output, int runs, int dbDetections, string op, BenchMetrics m) =>
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {runs} | {dbDetections:N0} | {op} | {m.TotalRows:N0} | {m.DetectionRows:N0} | {m.Milliseconds:F2} | {m.Megabytes:F2} |"));

    private static async Task WarmupReadsAsync(ServiceProvider provider, TenantId tenant, StudyId study, CollectionInstanceId formId, CancellationToken ct)
    {
        using var scope = provider.CreateScope();
        var findings = scope.ServiceProvider.GetRequiredService<IValidationFindings>();
        var collections = scope.ServiceProvider.GetRequiredService<ICollectionInstanceRecords>();
        var workQueues = scope.ServiceProvider.GetRequiredService<IWorkQueues>();
        await findings.ListAsync(tenant, new FindingQuery(study) { Statuses = [FindingStatus.Open, FindingStatus.Investigating], Take = 20 }, ct);
        await collections.ListForStudyAsync(tenant, study, ct);
        await workQueues.ReadAsync(tenant, new WorkQueueQuery(NodeRef.For(study)), ct);
        await collections.FindAsync(tenant, formId, ct);
    }

    private static async Task<BenchMetrics> MeasureOnceAsync(RowCountingInterceptor interceptor, Func<Task> action)
    {
        interceptor.Reset();
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        var afterBytes = GC.GetTotalAllocatedBytes(precise: true);

        return new BenchMetrics(
            interceptor.TotalRows,
            interceptor.DetectionRows,
            sw.Elapsed.TotalMilliseconds,
            (afterBytes - beforeBytes) / (1024.0 * 1024.0));
    }

    private static async Task<BenchMetrics> MeasureMedianAsync(RowCountingInterceptor interceptor, int iterations, Func<Task> action)
    {
        await action();
        var samples = new List<BenchMetrics>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            samples.Add(await MeasureOnceAsync(interceptor, action));
        }

        samples.Sort(static (a, b) => a.Milliseconds.CompareTo(b.Milliseconds));
        return samples[samples.Count / 2];
    }

    private static async Task<(TenantId Tenant, StudyId Study, LibraryItemId PackId, List<CollectionInstanceId> Forms)> SeedStudyWithFormsAsync(
        KernelPostgresFixture fixture,
        CancellationToken ct,
        bool waitDummy = false)
    {
        _ = waitDummy;
        var scenario = new KernelCollectionScenario(fixture);
        var (tenant, study) = await scenario.Protocol.NewStudyAsync(ct);
        var version = await scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await scenario.Observations.AddStudySiteAsync(tenant, study, ct);

        var checksUri = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "checks" }.Uri.AbsoluteUri;
        var citation = new SourceCitation("Protocol Edit Checks", "1.0", checksUri);
        var applicability = new RuleApplicability(ValidationStage.Collection, null, null, null, SpecId);
        using var condDoc = System.Text.Json.JsonDocument.Parse("""{"op":"<","args":[{"var":"item.sbp"},{"var":"item.dbp"}]}""");
        var rulePack = new RulePack(RuleAuthority.Sponsor, "Collection Vitals Checks", [
            new Rule("EC-SBP-LT-DBP", 1, RuleAuthority.Sponsor, citation, applicability, ValidationSeverity.Error,
                RuleExpressionJson.Parse(condDoc.RootElement), "Systolic BP is lower than diastolic BP.", "Verify cuff readings.", EffectivePeriod.Always),
        ]);
        var packDraft = await scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.RulePack, PackKey, "Collection Vitals Checks", RulePackJson.ToJson(rulePack), [], Guid.NewGuid().ToString("N"), "Bench"),
            ct);
        var packSig = await scenario.Protocol.OkAsync(
            new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, packDraft.Id.Value, packDraft.ContentHash), scenario.Tick().AddSeconds(-1), "pwd+otp", Guid.NewGuid().ToString("N"), "Bench"),
            ct);
        await scenario.Protocol.OkAsync(new ApproveLibraryItem(tenant, packDraft.Id, packSig, Guid.NewGuid().ToString("N"), "Bench"), ct);

        var sdtmigPkg = StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package;
        var sdtmig = await scenario.Protocol.OkAsync(new RegisterStandardVersion(tenant, StandardVersionId.New(), sdtmigPkg, null, Guid.NewGuid().ToString("N"), "Bench"), ct);
        var sdtmigHash = (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ContentHash;
        var sdtmigSig = await scenario.Protocol.OkAsync(
            new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, new AttestationTarget(NodeKinds.StandardVersion, sdtmig.Value, sdtmigHash), scenario.Tick().AddSeconds(-1), "pwd+otp", Guid.NewGuid().ToString("N"), "Bench"),
            ct);
        await scenario.Protocol.OkAsync(new ApproveStandardVersion(tenant, sdtmig, sdtmigSig, Guid.NewGuid().ToString("N"), "Bench"), ct);
        await scenario.Protocol.OkAsync(new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [sdtmig], [], [packDraft.Id], Guid.NewGuid().ToString("N"), "Bench"), ct);

        var forms = new List<CollectionInstanceId>(FormCount);
        for (var i = 0; i < FormCount; i++)
        {
            var enrollment = await scenario.Observations.ScreenAsync(tenant, studySite, ct);
            var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
            var opened = await scenario.Protocol.OkAsync(KernelCollectionScenario.Open(subject, CollectionInstanceId.New(), scenario.Tick()), ct);
            var form = new KernelCollectionScenario.Form(subject, version, opened.CollectionInstanceId);
            await scenario.Protocol.OkAsync(
                KernelCollectionScenario.Save(
                    form,
                    KernelCollectionScenario.Value(Systolic, KernelCollectionScenario.Mmhg(88m)),
                    KernelCollectionScenario.Value(Diastolic, KernelCollectionScenario.Mmhg(98m))),
                ct);
            forms.Add(opened.CollectionInstanceId);
        }

        return (tenant, study, packDraft.Id, forms);
    }

    private static async Task AppendCollectionRunAsync(
        ServiceProvider provider,
        FakeClock clock,
        TenantId tenant,
        StudyId study,
        LibraryItemId packId,
        List<CollectionInstanceId> forms,
        int activeCount,
        int runNumber,
        CancellationToken ct)
    {
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IKernelCommandSender>();
        clock.Advance(TimeSpan.FromSeconds(1));
        var now = clock.UtcNow;

        var detections = new List<ValidationDetection>(activeCount);
        for (var i = 0; i < activeCount; i++)
        {
            detections.Add(new ValidationDetection(
                PackKey,
                "EC-SBP-LT-DBP",
                1,
                ValidationSeverity.Error,
                new FindingTarget(FindingTargetKind.DatasetRecord, SpecId, forms[i].Value.ToString("D"), Systolic),
                "Systolic BP is lower than diastolic BP.",
                "Verify cuff readings.",
                [
                    new ObservedVariable(Systolic, "88"),
                    new ObservedVariable(Diastolic, "98"),
                ]));
        }

        var res = await sender.SendAsync(
            new RecordValidationRun(
                tenant,
                ValidationRunId.New(),
                study,
                ValidationStage.Collection,
                [packId],
                new DateOnly(2026, 11, 25),
                null,
                null,
                null,
                [],
                [SpecId],
                now,
                now,
                detections,
                $"bench-run-{runNumber}",
                "Benchmark validation run"),
            cancellationToken: ct);
        if (!res.IsSuccess)
        {
            throw new InvalidOperationException($"Run {runNumber} failed: {res.Error?.Code} {res.Error?.Message}");
        }
    }

    private readonly record struct BenchMetrics(long TotalRows, long DetectionRows, double Milliseconds, double Megabytes);

    private sealed class BenchDbContextFactory(DbContextOptions<KernelDbContext> options) : IDbContextFactory<KernelDbContext>
    {
        public KernelDbContext CreateDbContext() => new(options);
    }

    private sealed class RowCountingInterceptor : DbCommandInterceptor
    {
        private long _totalRows;
        private long _detectionRows;

        public long TotalRows => Interlocked.Read(ref _totalRows);

        public long DetectionRows => Interlocked.Read(ref _detectionRows);

        public void Reset()
        {
            Interlocked.Exchange(ref _totalRows, 0);
            Interlocked.Exchange(ref _detectionRows, 0);
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result) =>
            IsSelectQuery(command.CommandText) ? Wrap(command.CommandText, result) : result;

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default) =>
            new(IsSelectQuery(command.CommandText) ? Wrap(command.CommandText, result) : result);

        private static bool IsSelectQuery(string? sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return false;
            }

            var trimmed = sql.AsSpan().TrimStart();
            return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
        }

        private CountingReader Wrap(string sql, DbDataReader inner) =>
            new(inner, sql.Contains("validation_detections", StringComparison.OrdinalIgnoreCase), this);

        internal void OnRow(bool isDetection)
        {
            Interlocked.Increment(ref _totalRows);
            if (isDetection)
            {
                Interlocked.Increment(ref _detectionRows);
            }
        }

        private sealed class CountingReader(DbDataReader inner, bool isDetection, RowCountingInterceptor owner) : DbDataReader
        {
            public override object this[int ordinal] => inner[ordinal];
            public override object this[string name] => inner[name];
            public override int Depth => inner.Depth;
            public override int FieldCount => inner.FieldCount;
            public override bool HasRows => inner.HasRows;
            public override bool IsClosed => inner.IsClosed;
            public override int RecordsAffected => inner.RecordsAffected;
            public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
            public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
            public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
            public override char GetChar(int ordinal) => inner.GetChar(ordinal);
            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
            public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
            public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
            public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
            public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
            public override System.Collections.IEnumerator GetEnumerator() => inner.GetEnumerator();
            public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
            public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
            public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
            public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
            public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
            public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
            public override string GetName(int ordinal) => inner.GetName(ordinal);
            public override int GetOrdinal(string name) => inner.GetOrdinal(name);
            public override string GetString(int ordinal) => inner.GetString(ordinal);
            public override object GetValue(int ordinal) => inner.GetValue(ordinal);
            public override int GetValues(object[] values) => inner.GetValues(values);
            public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
            public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
            public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
            public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
            public override bool NextResult() => inner.NextResult();
            public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
            public override void Close() => inner.Close();
            public override Task CloseAsync() => inner.CloseAsync();

            public override bool Read()
            {
                var ok = inner.Read();
                if (ok)
                {
                    owner.OnRow(isDetection);
                }

                return ok;
            }

            public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
            {
                var ok = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (ok)
                {
                    owner.OnRow(isDetection);
                }

                return ok;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                }

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
