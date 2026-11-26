using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Release benchmark of <c>GET /api/v1/execution/forms/queries</c> across 300 study forms (10 sites, 30 forms per
/// enrollment) and 30 enrollment forms under PostgreSQL Row-Level Security. Opt in with
/// <c>CADENCE_FORM_QUERY_BENCHMARK=1</c> alongside <c>CADENCE_TEST_POSTGRES</c>.
/// </summary>
[Collection(ExecutionFormPerfMemoryQuotaCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Benchmark")]
public sealed class FormQueryWorklistBenchmarkPostgresTests(ExecutionFormModulePostgresFixture fixture, ITestOutputHelper output)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@worklist-benchmark.test";
    private const string Monitor = "cra@worklist-benchmark.test";
    private const int SiteCount = 10;
    private const int FormsPerEnrollment = 30;
    private const int TotalStudyForms = SiteCount * FormsPerEnrollment;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private readonly SqlCommandCollector _sql = new();
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose()
    {
        _host?.Dispose();
        _sql.Dispose();
    }

    /// <summary>
    /// Seeds 300 study forms (10 sites x 30 forms) with active queries and measures SQL query count,
    /// <see cref="ProtocolDesignDocument.Read"/> invocations, latency, and managed heap allocations against the
    /// 1,825-query / 378.58 MB / 1,478.82 ms study worklist baseline and 241-query enrollment baseline.
    /// </summary>
    [Fact(DisplayName = "Benchmark_ThreeHundredStudyForms_WorklistSqlLatencyAndAllocations", Timeout = 600_000)]
    public async Task BenchmarkThreeHundredStudyFormsWorklistSqlLatencyAndAllocations()
    {
        fixture.SkipIfUnavailable();
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("CADENCE_FORM_QUERY_BENCHMARK") != "1",
            "Set CADENCE_FORM_QUERY_BENCHMARK=1 to run the 300-form query worklist benchmark.");

        var ct = TestContext.Current.CancellationToken;
        var code = $"T-BMK-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Worklist benchmark tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct, repeatingSpec: true);

        Guid sponsorId;
        await using (var db = fixture.CreateContext(tenant))
        {
            sponsorId = (await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct)).Value;
        }

        var sites = new List<Site>(SiteCount);
        var studySites = new List<StudySite>(SiteCount);
        for (var s = 1; s <= SiteCount; s++)
        {
            var siteCode = string.Create(CultureInfo.InvariantCulture, $"SITE-{s:D2}");
            var site = new Site(SiteId.New(), tenant, OrganizationId.FromGuid(sponsorId), siteCode, $"Benchmark Clinic {s:D2}", "US", KernelPostgresFixture.ActorId, "Seeded");
            var link = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded");
            sites.Add(site);
            studySites.Add(link);
        }

        await fixture.SeedAsync([.. sites.Cast<object>(), .. studySites.Cast<object>()]);

        foreach (var capability in new[] { "collection.open", "collection.record", "execution.query.answer" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.Study(study), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "CRC"),
                ct);
        }

        foreach (var capability in new[] { "collection.record_unasked", "execution.query.raise", "execution.query.manage" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Monitor), capability, null, AuthorityScope.Study(study), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Monitor"),
                ct);
        }

        foreach (var (service, capabilities) in new (string, string[])[]
        {
            ("execution-forms", ["execution.form.open"]),
            ("execution-queries", ["observation.query", "observation.query.answer", "assertion.retract"]),
            ("execution-sdv-alerts", ["execution.query.manage"]),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.ServiceAccount(service), capability, null, AuthorityScope.Tenant(), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Service grant"),
                    ct);
            }
        }

        using var monitor = Client(code, Monitor);
        using var crc = Client(code, Crc);

        var enrollments = new List<EnrollmentId>(SiteCount);
        var seedWatch = Stopwatch.StartNew();
        var formCounter = 0;
        for (var s = 0; s < SiteCount; s++)
        {
            var participant = await _scenario.Protocol.OkAsync(
                new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
            var subjectCodeRaw = string.Create(CultureInfo.InvariantCulture, $"SUBJ-{s + 1:D2}-01");
            SubjectCode.TryFrom(subjectCodeRaw, out var subjectCode);
            var enrollment = (await _scenario.Protocol.OkAsync(
                new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySites[s].Id, subjectCode, _scenario.Tick(), KernelProtocolScenario.Key(), "Screened"),
                ct)).EnrollmentId;
            enrollments.Add(enrollment);

            var subject = new KernelObservationScenario.Subject(tenant, study, studySites[s].Id, enrollment);
            for (var f = 0; f < FormsPerEnrollment; f++)
            {
                formCounter++;
                var formId = (await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct)).CollectionInstanceId;
                var saved = await _scenario.Protocol.OkAsync(
                    Save(new Form(subject, version, formId), Value(Systolic, Mmhg(115 + (formCounter % 35))), Value(Diastolic, Mmhg(70 + (formCounter % 20)))),
                    ct);
                var sbpObs = saved.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;

                await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{formId.Value:D}/queries", "SDV benchmark", new
                {
                    observation_id = sbpObs,
                    revision = 1,
                    text = $"Confirm systolic reading on form #{formCounter}",
                }, HttpStatusCode.Created, ct);

                if (formCounter % 2 == 0)
                {
                    await _scenario.Protocol.OkAsync(
                        Save(new Form(subject, version, formId), Value(Systolic, Mmhg(160), expectedRevision: 1)),
                        ct);
                }
            }
        }

        formCounter.Should().Be(TotalStudyForms);
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Seeded {TotalStudyForms} forms ({SiteCount} sites x {FormsPerEnrollment} forms) in {seedWatch.Elapsed.TotalSeconds:F2} s."));

        async Task<(int SqlCount, int ProtocolReads, double MedianMs, double MeanMs, double MedianAllocMb, int ResultCount)> MeasureScenarioAsync(
            string label,
            string relativeQuery,
            int expectedCount)
        {
            // Warmup runs (3 iterations)
            for (var w = 0; w < 3; w++)
            {
                var warm = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, relativeQuery, null, null, HttpStatusCode.OK, ct);
                warm.Should().HaveCount(expectedCount);
            }

            const int iterations = 11;
            var latenciesMs = new List<double>(iterations);
            var allocsMb = new List<double>(iterations);
            var sqlCount = 0;
            var protocolReads = 0;

            for (var i = 0; i < iterations; i++)
            {
                using var reads = ProtocolDesignDocument.MeasureReads();
                _sql.Reset();
                var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
                var sw = Stopwatch.StartNew();

                var result = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, relativeQuery, null, null, HttpStatusCode.OK, ct);

                sw.Stop();
                var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
                sqlCount = _sql.Snapshot().Count;
                protocolReads = reads.Count;

                result.Should().HaveCount(expectedCount);
                latenciesMs.Add(sw.Elapsed.TotalMilliseconds);
                allocsMb.Add((allocAfter - allocBefore) / (1024.0 * 1024.0));
            }

            latenciesMs.Sort();
            allocsMb.Sort();
            var medianMs = latenciesMs[latenciesMs.Count / 2];
            var meanMs = latenciesMs.Average();
            var medianAllocMb = allocsMb[allocsMb.Count / 2];

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BENCHMARK | {label} | items={expectedCount} | sql={sqlCount} | protocol_reads={protocolReads} | median_ms={medianMs:F2} | mean_ms={meanMs:F2} | alloc_mb={medianAllocMb:F2}"));

            return (sqlCount, protocolReads, medianMs, meanMs, medianAllocMb, expectedCount);
        }

        var studyUnfiltered = await MeasureScenarioAsync(
            "Study worklist (300 forms, unfiltered)",
            $"/queries?study_id={study.Value:D}",
            TotalStudyForms);

        var studyBySite = await MeasureScenarioAsync(
            "Study worklist (300 forms, site=SITE-03)",
            $"/queries?study_id={study.Value:D}&site=SITE-03",
            FormsPerEnrollment);

        var studyByParticipant = await MeasureScenarioAsync(
            "Study worklist (300 forms, participant=SUBJ-03-01)",
            $"/queries?study_id={study.Value:D}&participant=SUBJ-03-01",
            FormsPerEnrollment);

        var studyValueChanged = await MeasureScenarioAsync(
            "Study worklist (300 forms, value_changed=true)",
            $"/queries?study_id={study.Value:D}&value_changed=true",
            TotalStudyForms / 2);

        var studyEmptyPreFilter = await MeasureScenarioAsync(
            "Study worklist (300 forms, site=SITE-99 pre-filtered)",
            $"/queries?study_id={study.Value:D}&site=SITE-99",
            0);

        var enrollmentWorklist = await MeasureScenarioAsync(
            "Enrollment worklist (30 forms)",
            $"/queries?enrollment_id={enrollments[0].Value:D}",
            FormsPerEnrollment);

        // Guardrail assertions against the 1,825-query / 378.58 MB / 1,478.82 ms baseline and 241-query enrollment baseline:
        studyUnfiltered.ProtocolReads.Should().Be(0);
        studyUnfiltered.SqlCount.Should().BeLessThan(20, "300-form study worklist replaces 1,825 N+1 SQL queries with constant batch projections");
        studyUnfiltered.MedianAllocMb.Should().BeLessThan(50.0, "300-form study worklist eliminates 378.58 MB protocol/history allocations");
        studyUnfiltered.MedianMs.Should().BeLessThan(500.0, "300-form study worklist stays well below the 1,478.82 ms baseline");

        studyBySite.ProtocolReads.Should().Be(0);
        studyBySite.SqlCount.Should().Be(studyUnfiltered.SqlCount);
        studyByParticipant.ProtocolReads.Should().Be(0);
        studyValueChanged.ProtocolReads.Should().Be(0);
        studyEmptyPreFilter.ProtocolReads.Should().Be(0);
        studyEmptyPreFilter.SqlCount.Should().BeLessThan(studyUnfiltered.SqlCount);

        enrollmentWorklist.ProtocolReads.Should().Be(0);
        enrollmentWorklist.SqlCount.Should().BeLessThan(20, "30-form enrollment worklist replaces 241 N+1 SQL queries with constant batch projections");
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(_sql);
            })
            .ConfigureTestServices(services =>
            {
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions
                {
                    RelayInterval = TimeSpan.FromHours(1),
                    ActivityInterval = TimeSpan.FromHours(1),
                });
            }));
        _host.Server.PreserveExecutionContext = true;
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private sealed class SqlCommandCollector : ILoggerProvider
    {
        private readonly AsyncLocal<ConcurrentQueue<string>?> _activeCommands = new();

        public void Reset() => _activeCommands.Value = new ConcurrentQueue<string>();

        public IReadOnlyList<string> Snapshot() => _activeCommands.Value is { } active ? [.. active] : [];

        public ILogger CreateLogger(string categoryName) =>
            string.Equals(categoryName, DbLoggerCategory.Database.Command.Name, StringComparison.Ordinal)
                ? new CommandLogger(_activeCommands)
                : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CommandLogger(AsyncLocal<ConcurrentQueue<string>?> activeCommands) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (activeCommands.Value is not { } commands
                    || eventId.Id != 20101
                    || state is not IReadOnlyList<KeyValuePair<string, object?>> fields)
                {
                    return;
                }

                foreach (var (key, value) in fields)
                {
                    if (string.Equals(key, "commandText", StringComparison.Ordinal) && value is string sql)
                    {
                        commands.Enqueue(sql);
                        return;
                    }
                }
            }
        }

        private sealed class NullLogger : ILogger
        {
            public static readonly NullLogger Instance = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
