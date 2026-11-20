using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// A reason for change typed with an em dash and Japanese reaches the audit trail exactly as typed when the
/// client sends it in the RFC 8187 header form (ADR-2495, #4895). Covers both readers of the header: the
/// ambient audit context (the eTMF compliance webhook) and the subject routes that read it directly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ChangeReasonEncodingTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Typed = "IRB approval — 治験審査委員会の承認";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The site compliance audit entry stores the decoded reason, not its header encoding.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task EtmfComplianceUpdate_StoresTheTypedReasonInTheAuditEntry()
    {
        factory.SkipIfUnavailable();
        // eTMF calls the webhook as its signed service account (ADR-2516).
        var caller = TestSecurityContext.Identity($"etmf-{_run}", PermissionMatrix.EtmfServiceRole);
        using var client = factory.CreateSignedClient(caller);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/execution/webhooks/etmf")
        {
            Content = JsonContent.Create(new
            {
                study_id = "STUDY-ENC",
                site_id = $"SITE-{_run}",
                milestone = "SITE_ACTIVATION",
                is_complete = true,
                missing_artifacts = Array.Empty<string>(),
            }),
        };
        request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(Typed));

        var response = await client.SendAsync(request, Ct);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        // One entry for the new row, labelled with the action and carrying the reason (#4922).
        (await ReasonsForAsync(caller.UserId)).Should().ContainSingle().Which.Should().Be(Typed);
    }

    /// <summary>A subject state change, whose route reads the header itself, audits the typed reason.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectStateChange_StoresTheTypedReasonInTheAuditEntry()
    {
        factory.SkipIfUnavailable();
        string subject = $"SUBJ-ENC-{_run}";
        using (var scope = factory.Services.CreateSeedingScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            db.ClinicalSubjects.Add(ClinicalSubject.Create(
                new SubjectCode(subject), new StudyCode("STUDY-ENC"), siteCode: null, enrollmentIndex: 0,
                encryptedDemographics: "seed-ciphertext", stratFactors: null));
            await db.SaveChangesAsync(Ct);
        }

        var caller = TestSecurityContext.Identity($"coordinator-{_run}", "crc") with { SiteIds = ["SITE-01"] };
        using var client = factory.CreateSignedClient(caller);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/execution/subjects/{subject}/state")
        {
            Content = JsonContent.Create(new { status = "SCREEN_FAILED" }),
        };
        request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(Typed));

        var response = await client.SendAsync(request, Ct);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        (await ReasonsForAsync(caller.UserId)).Should().NotBeEmpty().And.AllBe(Typed);
    }

    /// <summary>A malformed RFC 8187 value is refused rather than stored as mojibake or dropped.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task MalformedEncodedReason_IsRefusedAndNothingIsAudited()
    {
        factory.SkipIfUnavailable();
        var caller = TestSecurityContext.Identity($"malformed-{_run}", "crc");
        using var client = factory.CreateSignedClient(caller);
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/execution/subjects/SUBJ-NONE-{_run}/state")
        {
            Content = JsonContent.Create(new { status = "SCREEN_FAILED" }),
        };
        request.Headers.Add(GatewayHeaders.ChangeReason, "UTF-8''bad%ZZ");

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReasonsForAsync(caller.UserId)).Should().BeEmpty();
    }

    private async Task<string[]> ReasonsForAsync(string userId, string? action = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var reasons = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.UserId == userId && (action == null || e.Action == action))
            .Select(e => e.ReasonForChange)
            .ToListAsync(Ct);
        return [.. reasons];
    }
}
