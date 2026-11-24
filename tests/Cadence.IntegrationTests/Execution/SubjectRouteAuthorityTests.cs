using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Kernel.Time;
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
/// #4943 through the real composition root: only site roles change a participant's record (not data managers, #4981),
/// only an unblinding authority breaks the blind, and nobody sees the treatment group without the gateway-signed
/// unblinded-access claim. A permitted caller asking for a forbidden transition is refused with 422 (PRD-SUB-001).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubjectRouteAuthorityTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string Reason = "Subject authority check: SAE needs the allocation to decide the treatment.";
    private const string Study = "STUDY-SUBJ-AUTH";
    private const string Arm = "ARM-B";

    private static int _nextEnrollmentIndex;

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// These roles hold no <c>subject_enrollment</c> write permission, so the route refuses them before the handler's
    /// own <c>SubjectAuthority</c> check is reached (ADR-2516).
    /// </summary>
    [Theory]
    [InlineData("CRA")]
    [InlineData("Monitor")]
    [InlineData("Subject")]
    [InlineData("Data Manager")]
    [InlineData("Sponsor Admin")]
    public async Task ParticipantWrites_ByARoleWithoutSiteAuthority_Return403_AndChangeNothing(string role)
    {
        factory.SkipIfUnavailable();
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
        }

        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Screening);
        stored.EncryptedDemographics.Should().Be("seed-ciphertext");
        (await SubjectExistsAsync($"NEW-{_run}")).Should().BeFalse();
    }

    [Fact]
    public async Task StateChange_ByASiteCoordinator_Succeeds()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Screening);
        using var client = factory.CreateSignedClient(Caller("crc", "crc"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "WITHDRAWN" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.OK, await state.Content.ReadAsStringAsync(Ct));
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Withdrawn);
    }

    /// <summary>
    /// A site coordinator may change state, but not along a path the transition matrix forbids: a randomized
    /// participant cannot go back to screening, fail screening or skip to completion, and an unknown state name is
    /// refused the same way.
    /// The API answers 422 with the <c>INVALID_STATE_TRANSITION</c> code and the stored state does not move.
    /// </summary>
    [Theory]
    [InlineData("SCREENING")]
    [InlineData("SCREEN_FAILED")]
    [InlineData("COMPLETED")]
    [InlineData("ENROLED")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task StateChange_AlongAForbiddenPath_Returns422_AndLeavesTheStateUnchanged(string target)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("crc-bypass", "crc"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = target }, Ct);

        string body = await state.Content.ReadAsStringAsync(Ct);
        state.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("INVALID_STATE_TRANSITION");
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    /// <summary>The unsigned PATCH /state route refuses UNBLINDED and directs callers to POST /unblind.</summary>
    [Fact]
    public async Task StateRoute_WithUnblindedStatus_Returns400_AndTheParticipantStaysBlinded()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("pi-via-state", "Principal Investigator"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await state.Content.ReadAsStringAsync(Ct)).Should().Contain("UNBLIND_REQUIRES_DEDICATED_ROUTE");
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    /// <summary>Roles holding rtsm_unblind:write other than PI or Authorized ER Physician are refused by SubjectAuthority.AuthorizeUnblinding.</summary>
    [Theory]
    [InlineData("Site Investigator")]
    [InlineData("Lead Investigator")]
    [InlineData("Emergency Unblinder")]
    public async Task Unblinding_WithoutUnblindingAuthority_Returns403_AndTheParticipantStaysBlinded(string role)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        var caller = CallerSignedForUnblind($"no-unblind-{role}", role, subject);
        using var client = factory.CreateSignedClient(caller);

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("UNAUTHORIZED_UNBLIND_ROLE");
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    /// <summary>
    /// A CRC and a Data Manager hold no <c>rtsm_unblind:write</c> route permission,
    /// so the dedicated unblind route refuses them before the handler (ADR-2516).
    /// </summary>
    [Theory]
    [InlineData("crc")]
    [InlineData("Data Manager")]
    public async Task Unblinding_ByARoleWithoutTheRoutePermission_Returns403_AndTheParticipantStaysBlinded(string role)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        var caller = CallerSignedForUnblind($"no-route-{role}", role, subject);
        using var client = factory.CreateSignedClient(caller);

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Theory]
    [InlineData("Principal Investigator")]
    [InlineData("Authorized ER Physician")]
    public async Task Unblinding_ByThePiOrAnErPhysician_RecordsWhoUnblindedAndWhy(string role)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        var unblinder = CallerSignedForUnblind($"unblinder-{role}", role, subject);
        using var client = factory.CreateSignedClient(unblinder);

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Unblinded);
        stored.IsUnblinded.Should().BeTrue();
        stored.UnblindedBy.Should().Be(unblinder.UserId);
        stored.UnblindedReason.Should().Be(Reason);
        stored.UnblindedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Unblinding_WithoutElectronicSignature_Returns401_AndTheParticipantStaysBlinded()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("unblinder-nosig", "Principal Investigator"));

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Fact]
    public async Task Unblinding_WithoutReasonForChange_Returns400()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        var caller = CallerSignedForUnblind("unblinder-noreason", "Principal Investigator", subject) with { ChangeReason = null };
        using var client = factory.CreateSignedClient(caller);

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Fact]
    public async Task Unblinding_WithAReasonUnder50Characters_Returns400_AndTheParticipantStaysBlinded()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        var caller = CallerSignedForUnblind("unblinder-short", "Principal Investigator", subject) with { ChangeReason = "SAE" };
        using var client = factory.CreateSignedClient(caller);

        var response = await client.PostAsync(SubjectEndpoints.UnblindSubjectAction(subject), null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("UNBLINDING_REASON_TOO_SHORT");
        var stored = await ReadSubjectAsync(subject);
        stored.Status.Should().Be(SubjectState.Randomized);
        stored.IsUnblinded.Should().BeFalse();
    }

    [Fact]
    public async Task SubjectDetail_IsBlindedWithoutTheUnblindedAccessClaim()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAllocatedAsync();

        using var blinded = factory.CreateSignedClient(Caller("pi-blinded", "Principal Investigator"));
        using var unblinded = factory.CreateSignedClient(Caller("pharmacist", "Pharmacist") with { UnblindedAccess = true });

        using var blindedBody = await ReadAsync(blinded, $"/api/v1/execution/subjects/{subject}");
        using var unblindedBody = await ReadAsync(unblinded, $"/api/v1/execution/subjects/{subject}");

        blindedBody.RootElement.GetProperty("treatment_group").ValueKind.Should().Be(JsonValueKind.Null);
        unblindedBody.RootElement.GetProperty("treatment_group").GetString().Should().Be(Arm);
    }

    [Fact]
    public async Task StateChangeResponse_NeverCarriesTheTreatmentGroup()
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAllocatedAsync();
        using var client = factory.CreateSignedClient(Caller("crc-active", "crc"));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "ACTIVE" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.OK, await state.Content.ReadAsStringAsync(Ct));
        using var body = JsonDocument.Parse(await state.Content.ReadAsStringAsync(Ct));
        body.RootElement.TryGetProperty("treatment_group", out _).Should().BeFalse();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name.Replace(' ', '-')}-{_run}", role) with
        {
            SiteIds = RoleNormalization.GetNormalizedRoles([role]).Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains) ? ["SITE-01"] : [],
            ChangeReason = Reason,
        };

    private GatewayIdentity CallerSignedForUnblind(string name, string role, string subject)
    {
        var identity = Caller(name, role);
        return identity with
        {
            SigToken = TestSecurityContext.CreateSigToken(
                identity.UserId,
                SubjectEndpoints.UnblindSubjectAction(subject),
                SubjectEndpoints.UnblindSubjectSemanticAction,
                [role]),
        };
    }

    /// <summary>Seeds a subject in <paramref name="state"/>, walking the transition graph.</summary>
    private async Task<string> SeedAsync(SubjectState state)
    {
        string code = $"SUBJ-{Guid.NewGuid().ToString("N")[..10]}";
        // Each seeded participant takes the next index: (study, enrollment_index) is unique in PostgreSQL.
        var subject = ClinicalSubject.Create(
            new SubjectCode(code), new StudyCode(Study), siteCode: null, enrollmentIndex: Interlocked.Increment(ref _nextEnrollmentIndex),
            encryptedDemographics: "seed-ciphertext", stratFactors: null);
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

    /// <summary>
    /// Seeds a randomised subject the way the randomise route leaves one (#5607): its own study with an approved
    /// minimisation scheme, and an allocation whose arm is encrypted with the host's allocation key.
    /// </summary>
    private async Task<string> SeedAllocatedAsync()
    {
        var clock = SystemClock.Instance;
        var study = new StudyCode($"{Study}-{Guid.NewGuid().ToString("N")[..8]}");
        string code = $"SUBJ-{Guid.NewGuid().ToString("N")[..10]}";
        var subject = ClinicalSubject.Create(new SubjectCode(code), study, siteCode: null, enrollmentIndex: 1,
            encryptedDemographics: "seed-ciphertext", stratFactors: null);
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();

        var scheme = RandomizationScheme.Draft(study, 1, RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["ARM-A"] = 1, [Arm] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] }, blockSizes: null,
            slotsPerStratum: 0, preferredArmProbability: 0.8, requiresKit: false, "statistician-a", Reason, clock).Value!;
        scheme.Approve("statistician-b", Reason, clock).IsSuccess.Should().BeTrue();

        using var scope = factory.Services.CreateSeedingScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();
        var allocationId = Guid.NewGuid();
        var allocation = RandomizationAllocation.Record(allocationId, scheme, subject, "sex=F",
            new Dictionary<string, string> { ["sex"] = "F" }, sequence: 1,
            protector.Protect(Arm, RandomizationAllocation.ArmPurpose(allocationId)), listSlotId: null, kitNumber: null,
            "crc-seed", clock.UtcNow, Reason);
        subject.Randomize(allocationId, kitReference: null, new Dictionary<string, object?>()).IsSuccess.Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        db.ClinicalSubjects.Add(subject);
        db.RandomizationSchemes.Add(scheme);
        db.RandomizationAllocations.Add(allocation);
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
