using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The eCRF correction through the real kernel host on PostgreSQL (ADR-2620): a gateway-signed caller, the host's
/// tenant directory and authority evaluator, the command pipeline, and the reason from the signed identity, read back
/// from the kernel audit chain. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelObservationHostPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Crc = "crc@observation-host.test";
    private const string Monitor = "cra@observation-host.test";
    private const string Stranger = "stranger@observation-host.test";
    private const string CorrectionReason = "Transcription error – source reads 128";
    private const string PreinstalledChromium = "/opt/pw-browsers/chromium";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelProtocolScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// A coordinator granted record and correct at their study site records a value, corrects it with a reason, and
    /// reads the correction back with the reason in the kernel audit trail. A stale version is 409, a missing reason 422,
    /// and after a study lock a correction is 423 and the read shows the lock. Each refusal carries its stable code.
    /// </summary>
    [Fact(DisplayName = "HostCorrection_RecordsCorrectsAndAuditsTheReason_AndRefusesStaleMissingReasonAndLocked")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostCorrectionRecordsCorrectsAndAuditsTheReasonAndRefusesStaleMissingReasonAndLocked()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);

        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = Systolic(182),
            observed_at = SystemClock.Instance.UtcNow.AddHours(-1),
        }, HttpStatusCode.Created, ct);
        recorded.Version.Should().Be(1);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();

        var corrected = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, $"observations/{observation}/corrections", CorrectionReason,
            Correction(enrollment, 128, 1), HttpStatusCode.OK, ct);
        corrected.Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));
        corrected.Version.Should().Be(2);

        var read = await SendAsync<KernelObservationResponse>(crc, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        read.Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));
        read.Revisions.Select(r => (r.Number, r.Value, r.ReasonForChange)).Should().Equal((1, Mmhg(182), "Initial entry"), (2, Mmhg(128), CorrectionReason));
        read.Audit.Should().Contain(a => a.Action == "Updated" && a.Version == 2 && a.ReasonForChange == CorrectionReason && a.ActorId == Crc);

        (await ProblemAsync(crc, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, 129, 1), ct))
            .Should().Be((HttpStatusCode.Conflict, ObservationErrors.VersionStale.Code));
        (await ProblemAsync(crc, $"observations/{observation}/corrections", null, Correction(enrollment, 129, 2), ct))
            .Should().Be(((HttpStatusCode)422, KernelErrors.ReasonForChangeRequired.Code));

        // Database lock, signed by the data manager through the kernel (ADR-2621); its HTTP surface is #5466.
        var observations = new KernelObservationScenario(fixture);
        var study = (await fixture.ObservationRecords.FindAsync(tenant, ObservationId.FromGuid(observation.Id), ct))!.StudyId;
        var locked = await _scenario.OkAsync(await observations.LockAsync(tenant, study, NodeRef.For(study), ct), ct);
        (await ProblemAsync(crc, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, 129, 2), ct))
            .Should().Be(((HttpStatusCode)423, ObservationErrors.Locked.Code));
        var lockedRead = await SendAsync<KernelObservationResponse>(crc, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        lockedRead.Locks.Should().ContainSingle().Which.LockId.Should().Be(locked.LockId);
    }

    /// <summary>
    /// A caller with no grant at the observation's study site can neither correct it (403, the denial audited) nor read
    /// it (403); the value is unchanged.
    /// </summary>
    [Fact(DisplayName = "HostCorrection_WithoutAGrant_IsDeniedAndNotVisible")]
    public async Task HostCorrectionWithoutAGrantIsDeniedAndNotVisible()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);
        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = Systolic(182),
            observed_at = SystemClock.Instance.UtcNow.AddHours(-1),
        }, HttpStatusCode.Created, ct);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();

        (await ProblemAsync(stranger, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, 128, 1), ct))
            .Should().Be((HttpStatusCode.Forbidden, KernelErrors.AuthorizationDenied.Code));
        using (var hidden = await stranger.GetAsync(new Uri($"/api/v1/kernel/observations/{observation}", UriKind.Relative), ct))
        {
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var read = await SendAsync<KernelObservationResponse>(crc, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        read.Value.Should().Be(new QuantityValue(182m, "mm[Hg]"));
        read.Version.Should().Be(1);
    }

    /// <summary>
    /// A monitor verifies revision 1 of an observation with a mandatory reason, sees a correction move the observation to an
    /// unverified revision 2 while preserving the append-only assertion on revision 1, verifies revision 2 under a Frozen lock,
    /// withdraws that verification under Frozen, and is refused with 423 (<c>K.Assertion.Locked</c>) under a Locked assertion.
    /// </summary>
    [Fact(DisplayName = "HostVerification_VerifiesRetractsClearsOnCorrection_AndDistinguishesFrozenFromLocked")]
    public async Task HostVerificationVerifiesRetractsClearsOnCorrectionAndDistinguishesFrozenFromLocked()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);
        using var stranger = Client(code, Stranger);

        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = Systolic(120),
            observed_at = Tick().AddHours(-1),
        }, HttpStatusCode.Created, ct);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();
        var enrollmentRef = NodeRef.For(enrollment).ToString();

        // Mandatory reason and capability checks on verification.
        (await ProblemAsync(monitor, $"observations/{observation}/verifications", null, new { enrollment_ref = enrollmentRef, revision = 1 }, ct))
            .Should().Be(((HttpStatusCode)422, KernelErrors.ReasonForChangeRequired.Code));
        (await ProblemAsync(crc, $"observations/{observation}/verifications", "Coordinator attempted SDV", new { enrollment_ref = enrollmentRef, revision = 1 }, ct))
            .Should().Be((HttpStatusCode.Forbidden, KernelErrors.AuthorizationDenied.Code));
        (await ProblemAsync(stranger, $"observations/{observation}/verifications", "Stranger attempted SDV", new { enrollment_ref = enrollmentRef, revision = 1 }, ct))
            .Should().Be((HttpStatusCode.Forbidden, KernelErrors.AuthorizationDenied.Code));

        // Verify revision 1.
        var verifiedRev1 = await SendAsync<KernelAssertionStateResponse>(
            monitor, HttpMethod.Post, $"observations/{observation}/verifications", "Verified against inpatient chart",
            new { enrollment_ref = enrollmentRef, revision = 1 }, HttpStatusCode.Created, ct);
        verifiedRev1.Kind.Should().Be("Verified");
        verifiedRev1.Revision.Should().Be(1);
        verifiedRev1.AssertedBy.Should().Be(Monitor);

        // Stale revision check when asking to verify a non-current revision.
        (await ProblemAsync(monitor, $"observations/{observation}/verifications", "Wrong revision", new { enrollment_ref = enrollmentRef, revision = 2 }, ct))
            .Should().Be((HttpStatusCode.Conflict, AssertionErrors.RevisionStale.Code));

        // Correcting to revision 2 leaves the revision 1 verification intact while revision 2 has no active verification.
        await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, $"observations/{observation}/corrections", CorrectionReason,
            Correction(enrollment, 128, 1), HttpStatusCode.OK, ct);
        var afterCorrection = await SendAsync<KernelObservationResponse>(monitor, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        afterCorrection.Version.Should().Be(2);
        afterCorrection.Assertions.Should().ContainSingle(a => a.Kind == AssertionKind.Verified && a.Revision == 1 && a.RetractedAt == null);
        afterCorrection.Assertions.Should().NotContain(a => a.Kind == AssertionKind.Verified && a.Revision == 2 && a.RetractedAt == null);

        // Frozen blocks data edits (423) while allowing SDV verification and withdrawal!
        var observations = new KernelObservationScenario(fixture);
        var study = (await fixture.ObservationRecords.FindAsync(tenant, ObservationId.FromGuid(observation.Id), ct))!.StudyId;
        var frozen = await _scenario.OkAsync(await observations.LockAsync(tenant, study, NodeRef.For(study), ct, LockKind.Frozen), ct);

        (await ProblemAsync(crc, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, 130, 2), ct))
            .Should().Be(((HttpStatusCode)423, ObservationErrors.Locked.Code));

        var verifiedRev2 = await SendAsync<KernelAssertionStateResponse>(
            monitor, HttpMethod.Post, $"observations/{observation}/verifications", "Verified corrected value during interim freeze",
            new { enrollment_ref = enrollmentRef, revision = 2 }, HttpStatusCode.Created, ct);
        var verifiedRev2Ref = NodeRef.Parse(verifiedRev2.Ref).Unversioned();

        var withdrawal = await SendAsync<KernelAssertionStateResponse>(
            monitor, HttpMethod.Post, $"assertions/{verifiedRev2Ref}/retraction", "Withdrawn after source addendum spotted",
            new { enrollment_ref = enrollmentRef }, HttpStatusCode.OK, ct);
        withdrawal.Kind.Should().Be("Verified");
        withdrawal.Retracts.Should().Be(verifiedRev2Ref.ToString());

        var afterRetraction = await SendAsync<KernelObservationResponse>(monitor, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        afterRetraction.Assertions.Single(a => a.AssertionId == AssertionId.FromGuid(verifiedRev2Ref.Id)).RetractedAt.Should().NotBeNull();

        // Release Frozen and apply Locked: Locked refuses both new verifications and verification withdrawals with 423 K.Assertion.Locked.
        await _scenario.OkAsync(await observations.ReleaseAsync(tenant, study, frozen.LockId, ct), ct);
        var reVerifiedRev2 = await SendAsync<KernelAssertionStateResponse>(
            monitor, HttpMethod.Post, $"observations/{observation}/verifications", "Re-verified against source addendum",
            new { enrollment_ref = enrollmentRef, revision = 2 }, HttpStatusCode.Created, ct);
        var reVerifiedRef = NodeRef.Parse(reVerifiedRev2.Ref).Unversioned();

        await _scenario.OkAsync(await observations.LockAsync(tenant, study, NodeRef.For(study), ct, LockKind.Locked), ct);
        (await ProblemAsync(monitor, $"observations/{observation}/verifications", "Attempted SDV while Locked", new { enrollment_ref = enrollmentRef, revision = 2 }, ct))
            .Should().Be(((HttpStatusCode)423, AssertionErrors.Locked.Code));
        (await ProblemAsync(monitor, $"assertions/{reVerifiedRef}/retraction", "Attempted withdrawal while Locked", new { enrollment_ref = enrollmentRef }, ct))
            .Should().Be(((HttpStatusCode)423, AssertionErrors.Locked.Code));
    }

    /// <summary>
    /// The browser-to-database check (ADR-2620): Chromium opens the real ObservationView on the Vite dev server, the
    /// user corrects the value and gives a reason, and the screen shows the correction as saved with the reason in the
    /// audit trail. The test then reads the revision and the kernel audit entry from PostgreSQL. Keycloak and the
    /// gateway are replaced by a signing proxy on port 8000 that signs the coordinator's identity onto each request,
    /// as the gateway does; the reason travels from the browser's own header. Opt in with <c>CADENCE_TEST_BROWSER=1</c>
    /// (needs Node, pnpm and a Chromium); otherwise skipped.
    /// </summary>
    [Fact(DisplayName = "BrowserCorrection_ReachesTheDatabaseWithItsReason")]
    public async Task BrowserCorrectionReachesTheDatabaseWithItsReason()
    {
        fixture.SkipIfUnavailable();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CADENCE_TEST_BROWSER") == "1", "Set CADENCE_TEST_BROWSER=1 to run the browser check.");
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);
        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = Systolic(182),
            observed_at = SystemClock.Instance.UtcNow.AddHours(-1),
        }, HttpStatusCode.Created, ct);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();
        const string Detail = "source reads 128 – transcription slip";

        await using (var proxy = await SigningProxy.StartAsync(crc, ct))
        {
            var (exitCode, output) = await RunBrowserCheckAsync(observation.ToString(), Detail, ct);
            exitCode.Should().Be(0, output);
            output.Should().Contain("1 passed", "the browser spec must run, not skip");
        }

        await using var db = fixture.CreateContext(tenant);
        var key = ObservationId.FromGuid(observation.Id).ToString();
        var audit = await db.AuditEntries.AsNoTracking()
            .Where(a => a.AggregateId == key && a.AggregateType == nameof(Observation) && a.Action == "Updated")
            .ToListAsync(ct);
        audit.Should().ContainSingle().Which.ReasonForChange.Should().Be($"Typographical Error: {Detail}");
        audit[0].ActorId.Should().Be(Crc);
        var revisions = await db.ObservationRevisions.AsNoTracking().Where(r => r.ObservationId == ObservationId.FromGuid(observation.Id))
            .OrderBy(r => r.Number).Select(r => new { r.Number, r.Value, r.ReasonForChange }).ToListAsync(ct);
        revisions.Select(r => (r.Number, r.Value, r.ReasonForChange)).Should()
            .Equal((1, Mmhg(182), "Initial entry"), (2, Mmhg(128), $"Typographical Error: {Detail}"));
    }

    private static object Correction(EnrollmentId enrollment, decimal systolic, long expectedVersion) =>
        new { enrollment_ref = NodeRef.For(enrollment).ToString(), value = Systolic(systolic), expected_version = expectedVersion };

    // A typed value as a client sends it (ADR-2623).
    private static object Systolic(decimal value) => new { type = "quantity", value, unit = "mm[Hg]" };

    private static ObservationValue Mmhg(decimal value) => new QuantityValue(value, "mm[Hg]");

    /// <summary>A tenant with a study, a study site and one screened participant; the coordinator granted at the site.</summary>
    private async Task<(string Code, TenantId Tenant, EnrollmentId Enrollment)> SeedAsync(bool grant, CancellationToken ct)
    {
        var code = $"T-OBS-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Observation host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        StudySiteId studySite;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", "US", KernelPostgresFixture.ActorId, "Seeded for test");
            var link = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
            await fixture.SeedAsync(site, link);
            studySite = link.Id;
        }

        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom("OBS-001", out var subject);
        var enrollment = await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subject, Tick(), KernelProtocolScenario.Key(), "Screening visit"), ct);
        if (grant)
        {
            foreach (var capability in new[] { "observation.record", "observation.correct" })
            {
                await _scenario.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(studySite), Tick(), null, false,
                        KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }

            foreach (var capability in new[] { "observation.verify", "assertion.retract" })
            {
                await _scenario.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(Monitor), capability, null, AuthorityScope.StudySite(studySite), Tick(), null, false,
                        KernelProtocolScenario.Key(), "Appointed monitor"),
                    ct);
            }
        }

        Tick();
        return (code, tenant, enrollment.EnrollmentId);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error an observation endpoint returns must be one its command declares (ADR-2600 §4.5).
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode? expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected ?? HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> ProblemAsync(
        HttpClient client, string path, string? reason, object body, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, path, reason, body), ct);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return (response.StatusCode, problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"/api/v1/kernel/{path}", UriKind.Relative));
        request.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Runs the Playwright half of the browser check (apps/web/playwright.correction-check.config.ts).</summary>
    private static async Task<(int ExitCode, string Output)> RunBrowserCheckAsync(string observationRef, string detail, CancellationToken ct)
    {
        var web = Path.Combine(RepositoryRoot(), "apps", "web");
        var start = new ProcessStartInfo("pnpm")
        {
            WorkingDirectory = web,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("playwright");
        start.ArgumentList.Add("test");
        start.ArgumentList.Add("--config");
        start.ArgumentList.Add("playwright.correction-check.config.ts");
        start.Environment["OBSERVATION_REF"] = observationRef;
        start.Environment["CORRECTION_DETAIL"] = detail;
        if (File.Exists(PreinstalledChromium) && Environment.GetEnvironmentVariable("PLAYWRIGHT_CHROMIUM_PATH") is null)
        {
            start.Environment["PLAYWRIGHT_CHROMIUM_PATH"] = PreinstalledChromium;
        }

        using var process = PosixScriptProcessHelper.StartWithPosixScriptFallback(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cadence.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Cadence.sln not found above the test output directory.");
    }

    /// <summary>
    /// Stands in for Keycloak and the gateway in the browser check: listens where the Vite dev server proxies
    /// <c>/api</c> (port 8000) and forwards each request to the kernel host through a client that signs the
    /// coordinator's identity, carrying the browser's own reason and idempotency key.
    /// </summary>
    private sealed class SigningProxy : IAsyncDisposable
    {
        private static readonly string[] ForwardedHeaders = [GatewayHeaders.ChangeReason, KernelObservationEndpoints.IdempotencyKeyHeader];

        private readonly WebApplication _app;

        private SigningProxy(WebApplication app) => _app = app;

        public static async Task<SigningProxy> StartAsync(HttpClient signed, CancellationToken ct)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://localhost:8000");
            var app = builder.Build();
            app.Map("/api/{**path}", async (HttpContext context) =>
            {
                using var forward = new HttpRequestMessage(
                    new HttpMethod(context.Request.Method),
                    new Uri($"{context.Request.Path}{context.Request.QueryString}", UriKind.Relative));
                if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                {
                    using var body = new MemoryStream();
                    await context.Request.Body.CopyToAsync(body, context.RequestAborted);
                    forward.Content = new ByteArrayContent(body.ToArray());
                    forward.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType ?? "application/json");
                }

                foreach (var name in ForwardedHeaders)
                {
                    if (context.Request.Headers.TryGetValue(name, out var value))
                    {
                        forward.Headers.TryAddWithoutValidation(name, value.ToString());
                    }
                }

                using var response = await signed.SendAsync(forward, context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            });
            await app.StartAsync(ct);
            return new SigningProxy(app);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));
}
