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

    /// <summary>Site roles may change a participant's state, but the handler refuses them an unblinding.</summary>
    [Theory]
    [InlineData("crc")]
    [InlineData("Site Investigator")]
    [InlineData("Lead Investigator")]
    public async Task Unblinding_WithoutUnblindingAuthority_Returns403_AndTheParticipantStaysBlinded(string role)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller($"no-unblind-{role}", role));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await state.Content.ReadAsStringAsync(Ct)).Should().Contain("UNAUTHORIZED_UNBLIND_ROLE");
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    /// <summary>
    /// A data manager and the RTSM emergency unblinder may not change a participant's state through this route at all,
    /// so the route refuses them before the handler (ADR-2516).
    /// </summary>
    [Theory]
    [InlineData("Data Manager")]
    [InlineData("Emergency Unblinder")]
    public async Task Unblinding_ByARoleWithoutTheRoutePermission_Returns403_AndTheParticipantStaysBlinded(string role)
    {
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller($"no-route-{role}", role));

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Theory]
    [InlineData("Principal Investigator")]
    [InlineData("Authorized ER Physician")]
    public async Task Unblinding_ByThePiOrAnErPhysician_RecordsWhoUnblindedAndWhy(string role)
    {
        factory.SkipIfUnavailable();
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
        factory.SkipIfUnavailable();
        string subject = await SeedAsync(SubjectState.Randomized);
        using var client = factory.CreateSignedClient(Caller("unblinder-noreason", "Principal Investigator") with { ChangeReason = null });

        var state = await client.PatchAsJsonAsync($"/api/v1/execution/subjects/{subject}/state", new { status = "UNBLINDED" }, Ct);

        state.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadSubjectAsync(subject)).Status.Should().Be(SubjectState.Randomized);
    }

    [Fact]
    public async Task Unblinding_WithAReasonUnder50Characters_Returns400_AndTheParticipantStaysBlinded()
    {
        factory.SkipIfUnavailable();
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

    [Fact]
    [GxPRequirement("PRD-SUB-003")]
    public async Task UnblindedStatistician_ListsCountsAndInspectsAllocatedSubjects_WhileDeniedCgtCustodyReads_AndTrialCoordinatorReadsCgt()
    {
        factory.SkipIfUnavailable();
        string kitNumber = $"KIT-{Guid.NewGuid().ToString("N")[..8]}";
        (string studyCode, string subjectCode) = await SeedAllocatedWithStudyAndKitAsync(kitNumber);

        var statisticianIdentity = Caller("unblinded-stat", RoleNormalization.ROLE_UNBLINDED_STATISTICIAN) with
        {
            StudyIds = [studyCode],
            UnblindedAccess = true,
        };
        using var statistician = factory.CreateSignedClient(statisticianIdentity);

        using var listDoc = await ReadAsync(statistician, $"/api/v1/execution/subjects?study_id={studyCode}");
        listDoc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        listDoc.RootElement.GetProperty("items")[0].GetProperty("subject_id").GetString().Should().Be(subjectCode);

        using var countsDoc = await ReadAsync(statistician, $"/api/v1/execution/subjects/counts?study_id={studyCode}");
        countsDoc.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        countsDoc.RootElement.GetProperty("counts").GetProperty("RANDOMIZED").GetInt32().Should().Be(1);

        using var detailDoc = await ReadAsync(statistician, $"/api/v1/execution/subjects/{subjectCode}");
        detailDoc.RootElement.GetProperty("treatment_group").GetString().Should().Be(Arm);
        detailDoc.RootElement.GetProperty("investigational_product_id").GetString().Should().Be(kitNumber);

        // Blinded callers remain strictly blinded under ADR-2640.
        foreach (string blindedRole in new[] { "crc", "Principal Investigator", "sponsor_dm" })
        {
            var blindedIdentity = Caller($"blinded-{blindedRole}", blindedRole) with { StudyIds = [studyCode] };
            using var blindedClient = factory.CreateSignedClient(blindedIdentity);
            using var blindedDetail = await ReadAsync(blindedClient, $"/api/v1/execution/subjects/{subjectCode}");
            blindedDetail.RootElement.GetProperty("treatment_group").ValueKind.Should().Be(JsonValueKind.Null);
            blindedDetail.RootElement.GetProperty("investigational_product_id").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // Unblinded statistician lacks cgt_chain_of_custody:read and receives 403 on CGT custody endpoints.
        foreach (string cgtPath in new[]
        {
            $"/api/v1/execution/rtsm/cgt/certificates/{studyCode}",
            $"/api/v1/execution/rtsm/cgt/reconciliations/{studyCode}",
            $"/api/v1/execution/rtsm/cgt/chain-of-custody/{studyCode}/{subjectCode}",
        })
        {
            using var cgtDenied = await statistician.GetAsync(cgtPath, Ct);
            cgtDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden, cgtPath);
        }

        // Trial coordinator holds cgt_chain_of_custody:read and receives 200 on CGT custody read endpoints.
        var trialCoordinatorIdentity = Caller("trial-coord", RoleNormalization.ROLE_TRIAL_COORDINATOR) with
        {
            StudyIds = [studyCode],
        };
        using var trialCoordinator = factory.CreateSignedClient(trialCoordinatorIdentity);
        foreach (string cgtPath in new[]
        {
            $"/api/v1/execution/rtsm/cgt/certificates/{studyCode}",
            $"/api/v1/execution/rtsm/cgt/reconciliations/{studyCode}",
            $"/api/v1/execution/rtsm/cgt/chain-of-custody/{studyCode}/{subjectCode}",
        })
        {
            using var cgtAllowed = await trialCoordinator.GetAsync(cgtPath, Ct);
            cgtAllowed.StatusCode.Should().Be(HttpStatusCode.OK, $"{cgtPath}: {await cgtAllowed.Content.ReadAsStringAsync(Ct)}");
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GatewayIdentity Caller(string name, string role) =>
        TestSecurityContext.Identity($"{name.Replace(' ', '-')}-{_run}", role) with
        {
            SiteIds = RoleNormalization.GetNormalizedRoles([role]).Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains) ? ["SITE-01"] : [],
            ChangeReason = Reason,
        };

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
        (_, string code) = await SeedAllocatedWithStudyAndKitAsync(kitNumber: null);
        return code;
    }

    private async Task<(string StudyCode, string SubjectCode)> SeedAllocatedWithStudyAndKitAsync(string? kitNumber)
    {
        var clock = SystemClock.Instance;
        var study = new StudyCode($"{Study}-{Guid.NewGuid().ToString("N")[..8]}");
        string code = $"SUBJ-{Guid.NewGuid().ToString("N")[..10]}";
        var subject = ClinicalSubject.Create(new SubjectCode(code), study, siteCode: new SiteCode("SITE-01"), enrollmentIndex: 1,
            encryptedDemographics: "seed-ciphertext", stratFactors: null);
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();

        var scheme = RandomizationScheme.Draft(study, 1, RandomizationMethod.Minimization,
            new Dictionary<string, int> { ["ARM-A"] = 1, [Arm] = 1 },
            new Dictionary<string, IReadOnlyList<string>> { ["sex"] = ["F", "M"] }, blockSizes: null,
            slotsPerStratum: 0, preferredArmProbability: 0.8, requiresKit: kitNumber is not null, "statistician-a", Reason, clock).Value!;
        scheme.Approve("statistician-b", Reason, clock).IsSuccess.Should().BeTrue();

        using var scope = factory.Services.CreateSeedingScope();
        var protector = scope.ServiceProvider.GetRequiredService<IAllocationProtector>();
        var allocationId = Guid.NewGuid();
        var allocation = RandomizationAllocation.Record(allocationId, scheme, subject, "sex=F",
            new Dictionary<string, string> { ["sex"] = "F" }, sequence: 1,
            protector.Protect(Arm, RandomizationAllocation.ArmPurpose(allocationId)), listSlotId: null, kitNumber: kitNumber,
            "crc-seed", clock.UtcNow, Reason);
        subject.Randomize(allocationId, kitReference: kitNumber, new Dictionary<string, object?>()).IsSuccess.Should().BeTrue();

        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        db.ClinicalSubjects.Add(subject);
        db.RandomizationSchemes.Add(scheme);
        db.RandomizationAllocations.Add(allocation);
        await db.SaveChangesAsync(Ct);
        return (study.Value, code);
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
