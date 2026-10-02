using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Trust;
using Cadence.Security;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Drives every mutating execution route through the real composition root as a signed caller and checks that
/// each successful write leaves an audit entry naming that caller, in the same save. Nothing may be recorded
/// against an anonymous "system" actor (#4767, 21 CFR 11.10(e)).
/// @req:PRD-SYS-001
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionAuditAttributionTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Audit attribution check";
    private const string Study = "STUDY-AUDIT";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var coordinator = Caller("coordinator", "Site Coordinator");
        string subject = $"SUBJ-{_run}";

        await AuditedAsync(coordinator, c => c.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = subject, study_id = Study, demographics = new { name = "Audit Participant", birthdate = "1980-01-01" } },
            Ct));
        await AuditedAsync(coordinator, c => c.PostAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/consent",
            new { protocol_version = new { study_id = Study, version_tag = "1.0", version_index = 1 }, icf_signed = true, icf_signed_date = "2026-01-01" },
            Ct));
        await AuditedAsync(coordinator, c => c.PutAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/demographics",
            new { strat_factors = new { site = "B" } },
            Ct));
        await AuditedAsync(coordinator, c => c.DeleteAsync($"/api/v1/execution/subjects/{subject}/demographics", Ct));
        await AuditedAsync(coordinator, c => c.PatchAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/state",
            new { status = "SCREEN_FAILED" },
            Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SiteComplianceWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var etmf = Caller("etmf-integration", "Integration");
        var manager = Caller("site-manager", "Study Manager");
        string site = $"SITE-{_run}";

        await AuditedAsync(etmf, c => c.PostAsJsonAsync(
            "/api/v1/execution/webhooks/etmf",
            new { study_id = Study, site_id = site, milestone = "SITE_ACTIVATION", is_complete = true, missing_artifacts = Array.Empty<string>() },
            Ct));
        await AuditedAsync(manager, c => c.PostAsJsonAsync(
            $"/api/v1/execution/sites/{site}/activate",
            new { study_id = Study },
            Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ObservationWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var coordinator = Caller("obs-coordinator", "Site Coordinator");
        var monitor = Caller("obs-monitor", "CRA");
        object observation = new { subject_id = $"SUBJ-{_run}", domain = "VS", test_code = "SYSBP", test_name = "Systolic BP", study_id = Study, value = 120, unit = "mmHg", observation_date = "2026-01-01T08:00:00Z" };

        string verified = await ReadIdAsync(await AuditedAsync(coordinator, c => c.PostAsJsonAsync("/api/v1/execution/observations/", observation, Ct)));
        string flagged = await ReadIdAsync(await AuditedAsync(coordinator, c => c.PostAsJsonAsync("/api/v1/execution/observations/", observation, Ct)));
        await AuditedAsync(monitor, c => c.PostAsync($"/api/v1/execution/observations/{verified}/sdv", content: null, Ct));
        await AuditedAsync(monitor, c => c.PostAsJsonAsync($"/api/v1/execution/observations/{flagged}/sdv/flag", new { reason = "Source mismatch" }, Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task FormWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var investigator = Caller("form-pi", "Site Investigator");
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 130}";

        string form = await ReadIdAsync(await AuditedAsync(investigator, c => c.PostAsJsonAsync(
            "/api/v1/execution/forms/",
            new { study_id = Study, subject_id = $"SUBJ-{_run}", form_id = "VS_FORM", payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 129}" },
            Ct)));
        await AuditedAsync(investigator, c => c.PutAsJsonAsync($"/api/v1/execution/forms/{form}", new { payload }, Ct));
        await AuditedAsync(investigator, c => c.PostAsync($"/api/v1/execution/forms/{form}/complete", content: null, Ct));
        var signer = investigator with
        {
            SigToken = TestSecurityContext.CreateSigToken(investigator.UserId, FormEndpoints.ApproveAction(form), FormEndpoints.ApproveSemanticAction),
        };
        await AuditedAsync(signer, c => c.PostAsJsonAsync(
            $"/api/v1/execution/forms/{form}/approve",
            new
            {
                signing_reason = "PI approval and sign-off.",
                signature_manifest = new
                {
                    signer_id = investigator.UserId,
                    signer_name = "Dr Audit",
                    signer_role = "Site Investigator",
                    signing_reason = "PI approval and sign-off.",
                    payload_hash = JsonCanonicalization.Sha256Hex(payload),
                    hmac_signature = HmacSigner.ComputeSignature(JsonCanonicalization.Sha256Hex(payload), TestSecurityContext.GatewaySecret),
                    signed_at = FakeClock.DefaultStart.UtcDateTime,
                },
            },
            Ct));
        await AuditedAsync(investigator, c => c.PostAsJsonAsync($"/api/v1/execution/forms/{form}/unlock", new { reason = "Late lab result" }, Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task QueryWrites_AreEachAuditedAgainstTheCaller()
    {
        factory.SkipIfUnavailable();
        var monitor = Caller("query-cra", "CRA");
        var investigator = Caller("query-pi", "Site Investigator");

        string query = await ReadIdAsync(await AuditedAsync(monitor, c => c.PostAsJsonAsync(
            "/api/v1/execution/queries/",
            new { study_id = Study, subject_id = $"SUBJ-{_run}", test_code = "SYSBP", explanation = "Value out of range" },
            Ct)));
        await AuditedAsync(investigator, c => c.PostAsJsonAsync($"/api/v1/execution/queries/{query}/respond", new { response = "Confirmed with source" }, Ct));
        await AuditedAsync(monitor, c => c.PostAsJsonAsync($"/api/v1/execution/queries/{query}/close", new { resolver = monitor.UserId }, Ct));
        await AuditedAsync(monitor, c => c.PostAsJsonAsync($"/api/v1/execution/queries/{query}/reopen", new { reason = "Needs re-check" }, Ct));
        await AuditedAsync(monitor, c => c.PostAsJsonAsync($"/api/v1/execution/queries/{query}/cancel", new { reason = "Raised in error" }, Ct));

        await ShouldHaveNoAnonymousEntriesAsync();
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name}-{_run}", role) with { ChangeReason = Reason };

    /// <summary>Runs <paramref name="call"/> as <paramref name="caller"/> and requires a new audit entry naming them.</summary>
    private async Task<HttpResponseMessage> AuditedAsync(GatewayIdentity caller, Func<HttpClient, Task<HttpResponseMessage>> call)
    {
        int before = await CountEntriesAsync(caller.UserId);
        using var client = factory.CreateSignedClient(caller);

        HttpResponseMessage response = await call(client);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        (await CountEntriesAsync(caller.UserId)).Should().BeGreaterThan(before,
            "every successful write must leave an audit entry attributed to its caller");
        return response;
    }

    private async Task<int> CountEntriesAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        return await db.AuditLogEntries.AsNoTracking().CountAsync(e => e.UserId == userId, Ct);
    }

    private async Task ShouldHaveNoAnonymousEntriesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var anonymous = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.UserId == "system" || e.UserId == "")
            .CountAsync(Ct);
        anonymous.Should().Be(0);
    }

    private static async Task<string> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
