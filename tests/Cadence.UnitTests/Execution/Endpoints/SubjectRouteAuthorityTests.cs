using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// #4943 through the real composition root: only site roles change a participant's record (not data managers, #4981),
/// only an unblinding authority breaks the blind, and nobody sees the treatment group without the gateway-signed
/// unblinded-access claim.
/// </summary>
public sealed class SubjectRouteAuthorityTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string Reason = "Subject authority check: SAE needs the allocation to decide the treatment.";
    private const string Study = "STUDY-SUBJ-AUTH";
    private const string Arm = "ARM-B";

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    [Theory]
    [InlineData("CRA")]
    [InlineData("Monitor")]
    [InlineData("Subject")]
    [InlineData("Data Manager")]
    [InlineData("Sponsor Admin")]
    public async Task ParticipantWrites_ByARoleWithoutSiteAuthority_Return403_AndChangeNothing(string role)
    {
        string subject = await SeedAsync(SubjectState.Screening);
        using var client = factory.CreateSignedClient(Caller($"no-site-{role}", role));

        var create = await client.PostAsJsonAsync(
            "/api/v1/execution/subjects/",
            new { subject_id = $"NEW-{_run}", study_id = Study },
            Ct);
        var consent = await client.PostAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/consent",
            new { protocol_version = new { study_id = Study, version_tag = "1.0", version_index = 1 }, icf_signed = false },
            Ct);
        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "WITHDRAWN" }, Ct);
        var update = await client.PutAsJsonAsync(
            $"/api/v1/execution/subjects/{subject}/demographics",
            new { strat_factors = new { site = "B" } },
            Ct);
        var delete = await client.DeleteAsync($"/api/v1/execution/subjects/{subject}/demographics", Ct);

        foreach (var response in new[] { create, consent, state, update, delete })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
            (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("UNAUTHORIZED_SUBJECT_ROLE");
        }

        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Screening);
        stored.EncryptedDemographics.Should().Be("seed-ciphertext");
        (await SubjectExistsAsync($"NEW-{_run}")).Should().BeFalse();
    }

    [Fact]
    public async Task StateChange_ByASiteCoordinator_Succeeds()
    {
        string subject = await SeedAsync(SubjectState.Screening);
        using var client = factory.CreateSignedClient(Caller("crc", "Site Coordinator"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "WITHDRAWN" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.OK, await state.Content.ReadAsStringAsync(Ct));
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Withdrawn);
    }

    [Theory]
    [InlineData("Site Coordinator")]
    [InlineData("Site Investigator")]
    [InlineData("Data Manager")]
    [InlineData("Emergency Unblinder")]
    [InlineData("Lead Investigator")]
    public async Task Unblinding_WithoutUnblindingAuthority_Returns403_AndTheParticipantStaysBlinded(string role)
    {
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller($"no-unblind-{role}", role));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await state.Content.ReadAsStringAsync(Ct)).Should().Contain("UNAUTHORIZED_UNBLIND_ROLE");
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Theory]
    [InlineData("Principal Investigator")]
    [InlineData("Authorized ER Physician")]
    public async Task Unblinding_ByThePiOrAnErPhysician_RecordsWhoUnblindedAndWhy(string role)
    {
        string subject = await SeedAsync(SubjectState.Randomized);
        var unblinder = Caller($"unblinder-{role}", role);
        using var client = factory.CreateSignedClient(unblinder);

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.OK, await state.Content.ReadAsStringAsync(Ct));
        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Unblinded);
        stored.IsUnblinded.Should().BeTrue();
        stored.UnblindedBy.Should().Be(unblinder.UserId);
        stored.UnblindedReason.Should().Be(Reason);
        stored.UnblindedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Unblinding_WithoutReasonForChange_Returns400()
    {
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("unblinder-noreason", "Principal Investigator") with { ChangeReason = null });

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Fact]
    public async Task Unblinding_WithAReasonUnder50Characters_Returns400_AndTheParticipantStaysBlinded()
    {
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("unblinder-short", "Principal Investigator") with { ChangeReason = "SAE" });

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await state.Content.ReadAsStringAsync(Ct)).Should().Contain("UNBLINDING_REASON_TOO_SHORT");
        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Randomized);
        stored.IsUnblinded.Should().BeFalse();
    }

    [Fact]
    public async Task SubjectDetail_IsBlindedWithoutTheUnblindedAccessClaim()
    {
        string subject = await SeedAsync(SubjectState.Randomized);

        using var blinded = factory.CreateSignedClient(Caller("pi-blinded", "Principal Investigator"));
        using var unblinded = factory.CreateSignedClient(Caller("pharmacist", "Pharmacist") with { UnblindedAccess = true });

        using var blindedBody = await ReadAsync(blinded, $"/api/v1/execution/subjects/{subject}");
        using var unblindedBody = await ReadAsync(unblinded, $"/api/v1/execution/subjects/{subject}");

        blindedBody.RootElement.GetProperty("treatment_group").ValueKind.Should().Be(JsonValueKind.Null);
        unblindedBody.RootElement.GetProperty("treatment_group").GetString().Should().Be(Arm);
    }

    [Fact]
    public async Task StateChangeResponse_WithholdsTheTreatmentGroupFromABlindedCaller()
    {
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("crc-active", "Site Coordinator"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "ACTIVE" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.OK, await state.Content.ReadAsStringAsync(Ct));
        using var body = JsonDocument.Parse(await state.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("treatment_group").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name.Replace(' ', '-')}-{_run}", role) with { ChangeReason = Reason };

    /// <summary>Seeds a subject in <paramref name="state"/> with a treatment group, walking the transition graph.</summary>
    private async Task<string> SeedAsync(SubjectState state)
    {
        string code = $"SUBJ-{Guid.NewGuid().ToString("N")[..10]}";
        var subject = ClinicalSubject.Create(
            new SubjectCode(code), new StudyCode(Study), siteCode: null, enrollmentIndex: 0,
            encryptedDemographics: "seed-ciphertext", stratFactors: null, treatmentGroup: Arm);
        if (state == SubjectState.Randomized)
        {
            subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
            subject.TransitionTo(SubjectState.Randomized).IsSuccess.Should().BeTrue();
        }

        using var scope = factory.Services.CreateSeedingScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        db.ClinicalSubjects.Add(subject);
        await db.SaveChangesAsync(Ct);
        return code;
    }

    private async Task<ClinicalSubject> ReadSubjectAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        return await db.ClinicalSubjects.AsNoTracking().SingleAsync(s => s.SubjectCode == new SubjectCode(code), Ct);
    }

    private async Task<bool> SubjectExistsAsync(string code)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        return await db.ClinicalSubjects.AsNoTracking().AnyAsync(s => s.SubjectCode == new SubjectCode(code), Ct);
    }

    private static async Task<JsonDocument> ReadAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }
}
