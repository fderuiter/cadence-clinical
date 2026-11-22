using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Kernel;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

using ExecutionStudyCode = Cadence.Apps.Execution.Domain.Subjects.StudyCode;
using ExecutionSubjectCode = Cadence.Apps.Execution.Domain.Subjects.SubjectCode;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL verification of <c>apps/execution</c> synchronizing subject lifecycle transitions,
/// randomization, and Part 11 signed withdrawals over gateway-signed HTTP to <c>apps/kernel-api</c>
/// (<c>K-XS-1</c>, <c>K-XS-2</c>, <c>K-CONS-2</c>).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SubjectKernelTransitionSyncPostgresTests(ExecutionFormModulePostgresFixture kernelFixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IAsyncLifetime
{
    private const string SiteCodeValue = "SITE-SYNC-01";
    private const string Investigator = "pi.sync@cadence-clinical.test";
    private const string StatisticianA = "stat.a.sync@cadence-clinical.test";
    private const string StatisticianB = "stat.b.sync@cadence-clinical.test";
    private const string UnauthorizedCrc = "crc.nogrant.sync@cadence-clinical.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly ThrowawayPostgresDatabase _executionDb = new();
    private readonly KernelCollectionScenario _kernelScenario = new(kernelFixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _kernelHost;
    private WebApplicationFactory<Cadence.Apps.Execution.Program>? _executionHost;

    public async ValueTask InitializeAsync()
    {
        await _executionDb.InitializeAsync().ConfigureAwait(false);
        if (kernelFixture.ConnectionString is null || _executionDb.ConnectionString is null)
        {
            return;
        }

        _kernelHost = new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", kernelFixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));

        // Force TestServer startup so its in-memory handler is ready for Execution's HttpClient.
        using var _ = _kernelHost.CreateClient();

        _executionHost = new WebApplicationFactory<Cadence.Apps.Execution.Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseTestGatewaySecret()
                .UseTestDemographicsKey()
                .UseTestRandomizationKey()
                .UseSetting("ConnectionStrings:cadence", _executionDb.ConnectionString)
                .UseSetting("Database:MigrateOnStartup", "true")
                .UseSetting(HttpKernelEnrollmentSynchronizer.ConfigurationKey, "http://localhost");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<CurrentTenantAccessor>();
                services.AddSingleton<CurrentTenantAccessor>(sp =>
                {
                    var accessor = new CurrentTenantAccessor(
                        sp.GetRequiredService<ITenantCodeResolver>(),
                        sp.GetService<IHttpContextAccessor>());
                    accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
                    return accessor;
                });

                services.AddHttpClient<IKernelEnrollmentSynchronizer, HttpKernelEnrollmentSynchronizer>(client =>
                    {
                        client.BaseAddress = _kernelHost.Server.BaseAddress;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => _kernelHost.Server.CreateHandler());
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_executionHost is not null)
        {
            await _executionHost.DisposeAsync().ConfigureAwait(false);
        }

        if (_kernelHost is not null)
        {
            await _kernelHost.DisposeAsync().ConfigureAwait(false);
        }

        await _executionDb.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronizes single-arm lifecycle transitions (<c>SCREENING -&gt; ENROLLED -&gt; ACTIVE -&gt; COMPLETED</c>),
    /// randomization (<c>ENROLLED -&gt; RANDOMIZED</c>), Part 11 signed withdrawal (<c>WITHDRAWN</c> with
    /// <c>AttestationMeaning.Withdrawn</c>), screen failure (<c>SCREEN_FAILED</c>), post-terminal capture refusals
    /// (<c>K-CONS-2</c>), and rollback when the kernel refuses an unauthorized transition.
    /// </summary>
    [Fact(DisplayName = "ExecutionSubjectTransitions_SyncOverHttpToKernel_AllocateEnrollmentNumbers_EnforceSignedWithdrawalAndCaptureGuards")]
    public async Task ExecutionSubjectTransitionsSyncOverHttpToKernelAllocateEnrollmentNumbersEnforceSignedWithdrawalAndCaptureGuards()
    {
        kernelFixture.SkipIfUnavailable();
        _executionDb.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var (tenant, tenantCode, _, studyCode, studySite) = await SeedSynchronizedTenantAndStudyAsync(ct);

        var piIdentity = ExecutionCaller(Investigator, "pi", tenantCode, "Protocol lifecycle transition");
        using var execPi = _executionHost!.CreateSignedClient(piIdentity);
        using var kernelPi = _kernelHost!.CreateSignedClient(new GatewayIdentity(Investigator, [], [], TenantId: tenantCode));

        // 1. Single-arm participant: create -> ENROLLED (allocates EnrollmentNumber = 1) -> ACTIVE -> COMPLETED.
        var subj1 = await CreateSubjectAsync(execPi, studyCode, "SUBJ-SYNC-01", ct);
        var kernelSubj1Screening = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-01", ct);
        kernelSubj1Screening.EnrollmentStatus.Should().Be("Screening");
        kernelSubj1Screening.EnrollmentNumber.Should().BeNull();

        await PatchStateAsync(execPi, subj1, "ENROLLED", "Met eligibility criteria", ct);
        var kernelSubj1Enrolled = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-01", ct);
        kernelSubj1Enrolled.EnrollmentStatus.Should().Be("Enrolled");
        kernelSubj1Enrolled.EnrollmentNumber.Should().Be(1);

        // Single-arm study moves directly ENROLLED -> ACTIVE without randomization.
        await PatchStateAsync(execPi, subj1, "ACTIVE", "First dose administered in single-arm cohort", ct);
        (await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-01", ct)).EnrollmentStatus.Should().Be("Active");

        // Execution-only operational state RECONSENT_REQUIRED leaves canonical kernel status Active until resumed/completed.
        await PatchStateAsync(execPi, subj1, "RECONSENT_REQUIRED", "Amendment 2 requires re-consent", ct);
        (await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-01", ct)).EnrollmentStatus.Should().Be("Active");

        await PatchStateAsync(execPi, subj1, "ACTIVE", "Re-consent signed", ct);
        await PatchStateAsync(execPi, subj1, "COMPLETED", "Completed final study visit", ct);
        var kernelSubj1Completed = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-01", ct);
        kernelSubj1Completed.EnrollmentStatus.Should().Be("Completed");
        kernelSubj1Completed.EnrollmentNumber.Should().Be(1);

        // 2. Randomized participant: create -> ENROLLED (EnrollmentNumber = 2) -> RANDOMIZED -> capture form -> signed WITHDRAWN.
        await ApproveRandomizationSchemeAsync(studyCode, tenantCode, ct);
        var subj2 = await CreateSubjectAsync(execPi, studyCode, "SUBJ-SYNC-02", ct);
        await PatchStateAsync(execPi, subj2, "ENROLLED", "Eligible for randomized cohort", ct);
        await SeedExecutionConsentAsync(studyCode, "SUBJ-SYNC-02", ct);

        var kernelSubj2Enrolled = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-02", ct);
        kernelSubj2Enrolled.EnrollmentStatus.Should().Be("Enrolled");
        kernelSubj2Enrolled.EnrollmentNumber.Should().Be(2);

        // Randomize through Execution's step-up endpoint; verify Kernel enrollment moves to Randomized.
        var randomizePath = RandomizationEndpoints.RandomizeSubjectAction("SUBJ-SYNC-02");
        using (var randomizeReq = new HttpRequestMessage(HttpMethod.Post, randomizePath) { Content = JsonContent.Create(new { }) })
        {
            randomizeReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(
                    Investigator,
                    randomizePath,
                    RandomizationEndpoints.RandomizeSubjectSemanticAction,
                    roles: [RoleNormalization.NormalizeRole("pi")]));
            using var randomizeRes = await execPi.SendAsync(randomizeReq, ct);
            randomizeRes.StatusCode.Should().Be(HttpStatusCode.OK, await randomizeRes.Content.ReadAsStringAsync(ct));
        }

        var kernelSubj2Randomized = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-02", ct);
        kernelSubj2Randomized.EnrollmentStatus.Should().Be("Randomized");
        kernelSubj2Randomized.EnrollmentNumber.Should().Be(2);

        // Open an eCRF form on SUBJ-SYNC-02 in Kernel and record pre-withdrawal systolic BP = 126.
        Guid enrollment2Id = kernelSubj2Randomized.EnrollmentId;
        Guid form2Id = await OpenVitalSignsFormAsync(kernelPi, enrollment2Id, ct);
        await SaveFormSystolicAsync(kernelPi, form2Id, 126m, expectedRevision: 0, ct);

        // Withdraw SUBJ-SYNC-02 through Execution with withdrawal_kind + Part 11 step-up X-Sig-Token.
        using (var withdrawReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/execution/subjects/{subj2}/state")
        {
            Content = JsonContent.Create(new
            {
                status = "WITHDRAWN",
                withdrawal_kind = "WithdrawalBySubject",
            }),
        })
        {
            withdrawReq.Headers.Remove(GatewayHeaders.ChangeReason);
            withdrawReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Participant withdrew consent at Visit 2"));
            withdrawReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(
                    Investigator,
                    KernelExecutionEndpoints.WithdrawAction,
                    KernelExecutionEndpoints.WithdrawSemanticAction,
                    roles: [RoleNormalization.NormalizeRole("pi")]));
            using var withdrawRes = await execPi.SendAsync(withdrawReq, ct);
            withdrawRes.StatusCode.Should().Be(HttpStatusCode.OK, await withdrawRes.Content.ReadAsStringAsync(ct));
        }

        // Execution subject has Withdrawn status, WithdrawalReason, and WithdrawnAt populated.
        using (var scope = _executionHost!.Services.CreateScope())
        {
            var execDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var storedSubj2 = await execDb.ClinicalSubjects.AsNoTracking()
                .SingleAsync(s => s.SubjectCode == new ExecutionSubjectCode("SUBJ-SYNC-02"), ct);
            storedSubj2.Status.Should().Be(SubjectState.Withdrawn);
            storedSubj2.WithdrawalReason.Should().Be("Participant withdrew consent at Visit 2");
            storedSubj2.WithdrawnAt.Should().NotBeNull();
        }

        // Kernel enrollment is Withdrawn with an Attestation(Withdrawn), and new form/item capture is refused with 409 while pre-withdrawal values remain correctable!
        var kernelSubj2Withdrawn = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-02", ct);
        kernelSubj2Withdrawn.EnrollmentStatus.Should().Be("Withdrawn");
        kernelSubj2Withdrawn.EnrollmentNumber.Should().Be(2);

        await using (var kernelDb = kernelFixture.CreateContext(tenant))
        {
            var attestations = await kernelDb.Attestations.AsNoTracking()
                .Where(a => a.Meaning == AttestationMeaning.Withdrawn)
                .ToListAsync(ct);
            attestations.Should().ContainSingle(a => a.TargetKind == NodeKinds.Enrollment && a.TargetId == enrollment2Id && a.ActorId == Investigator);
        }

        using (var postWithdrawFormOpen = await SendKernelFormRequestAsync(kernelPi, HttpMethod.Post, string.Empty, "Attempted form after withdrawal", new
        {
            enrollment_id = enrollment2Id,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = SystemClock.Instance.UtcNow.AddMinutes(-1),
        }, ct))
        {
            postWithdrawFormOpen.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(postWithdrawFormOpen, ct)).Should().Be(ObservationErrors.ParticipantWithdrawn.Code);
        }

        // Correcting pre-withdrawal systolic BP (revision 1 -> revision 2) succeeds (K-CONS-2).
        await SaveFormSystolicAsync(kernelPi, form2Id, 122m, expectedRevision: 1, ct);

        // 3. Screen-failed participant: SCREENING -> SCREEN_FAILED syncs to Kernel and blocks new form capture with 409!
        var subj3 = await CreateSubjectAsync(execPi, studyCode, "SUBJ-SYNC-03", ct);
        await PatchStateAsync(execPi, subj3, "SCREEN_FAILED", "Did not meet inclusion criterion 4", ct);

        var kernelSubj3ScreenFailed = await LookupKernelEnrollmentAsync(kernelPi, studyCode, "SUBJ-SYNC-03", ct);
        kernelSubj3ScreenFailed.EnrollmentStatus.Should().Be("ScreenFailed");
        kernelSubj3ScreenFailed.EnrollmentNumber.Should().BeNull();

        using (var postScreenFailFormOpen = await SendKernelFormRequestAsync(kernelPi, HttpMethod.Post, string.Empty, "Attempted form after screen failure", new
        {
            enrollment_id = kernelSubj3ScreenFailed.EnrollmentId,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = SystemClock.Instance.UtcNow.AddMinutes(-1),
        }, ct))
        {
            postScreenFailFormOpen.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ReadProblemCodeAsync(postScreenFailFormOpen, ct)).Should().Be(ObservationErrors.ParticipantScreenFailed.Code);
        }

        // 4. Kernel refusal rollback: CRC with Execution role on SITE-SYNC-01 but no Kernel enrollment.change_status grant
        // receives 403 from Kernel and Execution does NOT commit the state change in edc.subjects.
        var subj4 = await CreateSubjectAsync(execPi, studyCode, "SUBJ-SYNC-04", ct);
        using var ungrantedCrc = _executionHost!.CreateSignedClient(ExecutionCaller(UnauthorizedCrc, "crc", tenantCode, "Unauthorized kernel transition"));
        using (var refusedPatch = await ungrantedCrc.PatchAsJsonAsync($"/api/v1/execution/subjects/{subj4}/state", new { status = "SCREEN_FAILED" }, ct))
        {
            refusedPatch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var scope = _executionHost!.Services.CreateScope())
        {
            var execDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            var storedSubj4 = await execDb.ClinicalSubjects.AsNoTracking()
                .SingleAsync(s => s.SubjectCode == new ExecutionSubjectCode("SUBJ-SYNC-04"), ct);
            storedSubj4.Status.Should().Be(SubjectState.Screening, "Execution must not commit a state transition that the Kernel refused");
        }

        // Verify live Kernel enrollments reflect Completed (1), Withdrawn (1), ScreenFailed (1), and Screening (1), plus domain events!
        await using (var kernelDb = kernelFixture.CreateContext(tenant))
        {
            var statuses = await kernelDb.Enrollments.AsNoTracking()
                .Where(e => e.StudySiteId == studySite)
                .Select(e => new { Code = e.SubjectCode.Value, e.Status, e.EnrollmentNumber })
                .ToListAsync(ct);
            statuses.Should().BeEquivalentTo(new[]
            {
                new { Code = "SUBJ-SYNC-01", Status = EnrollmentStatus.Completed, EnrollmentNumber = (int?)1 },
                new { Code = "SUBJ-SYNC-02", Status = EnrollmentStatus.Withdrawn, EnrollmentNumber = (int?)2 },
                new { Code = "SUBJ-SYNC-03", Status = EnrollmentStatus.ScreenFailed, EnrollmentNumber = (int?)null },
                new { Code = "SUBJ-SYNC-04", Status = EnrollmentStatus.Screening, EnrollmentNumber = (int?)null },
            });

            var eventTypes = await kernelDb.DomainEvents.AsNoTracking()
                .Where(e => e.TenantId == tenant)
                .Select(e => e.Type)
                .ToListAsync(ct);
            eventTypes.Should().Contain(["ParticipantScreened", "ParticipantEnrolled", "EnrollmentStatusChanged", "ParticipantWithdrawn"]);
        }
    }

    private async Task<(TenantId Tenant, string TenantCode, StudyId Study, string StudyCode, StudySiteId StudySite)> SeedSynchronizedTenantAndStudyAsync(
        CancellationToken ct)
    {
        const string tenantCode = TestSecurityContext.TenantCode;
        var tenant = new TenantId(DeterministicTenantCodeResolver.ComputeDeterministicGuid(tenantCode));

        // Provision "tenant-test" in Kernel if not already provisioned in this fixture database.
        await using (var adminDb = kernelFixture.CreateAdminContext())
        {
            bool exists = await adminDb.Tenants.AsNoTracking().AnyAsync(t => t.Code == tenantCode, ct);
            if (!exists)
            {
                await _kernelScenario.Protocol.OkAsync(
                    new ProvisionTenant(tenant, tenantCode, "Synchronized Execution-Kernel test tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
                    ct);
            }
            else
            {
                tenant = (await adminDb.Tenants.AsNoTracking().SingleAsync(t => t.Code == tenantCode, ct)).Id;
            }
        }

        var (_, study) = await _kernelScenario.Protocol.NewStudyAsync(tenant, ct);
        await _kernelScenario.EffectiveVersionAsync(tenant, study, ct);

        string studyCode;
        StudySiteId studySite;
        await using (var db = kernelFixture.CreateContext(tenant))
        {
            var studyRow = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == study, ct);
            studyCode = studyRow.StudyCode;
            var rawSite = new Site(SiteId.New(), tenant, studyRow.SponsorId, SiteCodeValue, "Synchronized Clinical Site", "US", KernelPostgresFixture.ActorId, "Seeded for sync test");
            var link = new StudySite(StudySiteId.New(), tenant, study, rawSite.Id, "Active", KernelPostgresFixture.ActorId, "Seeded for sync test");
            await kernelFixture.SeedAsync(rawSite, link);
            studySite = link.Id;
        }

        // Issue Investigator grants in Kernel covering registration, screening, status transitions, withdrawal, attestation, and form capture.
        await IssueGrantAsync(tenant, Investigator, "participant.register", AuthorityScope.Tenant(), ct);
        await IssueGrantAsync(tenant, Investigator, "attestation.sign", AuthorityScope.Tenant(), ct);
        foreach (string cap in new[]
        {
            "participant.screen",
            "enrollment.change_status",
            "participant.withdraw",
            "collection.open",
            "collection.record",
            "observation.record",
            "observation.correct",
        })
        {
            await IssueGrantAsync(tenant, Investigator, cap, AuthorityScope.StudySite(studySite), ct);
        }

        // Activate SITE-SYNC-01 in Execution so Enrollment Rule 2 passes.
        using (var scope = _executionHost!.Services.CreateSeedingScope())
        {
            var execDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
            execDb.SiteComplianceCaches.Add(SiteComplianceCache.Create(
                new ExecutionStudyCode(studyCode),
                new SiteCode(SiteCodeValue),
                "SITE_ACTIVATION",
                true,
                []));
            await execDb.SaveChangesAsync(ct);
        }

        return (tenant, tenantCode, study, studyCode, studySite);
    }

    private Task<GrantState> IssueGrantAsync(TenantId tenant, string userId, string capability, AuthorityScope scope, CancellationToken ct) =>
        _kernelScenario.Protocol.OkAsync(
            new IssueGrant(
                tenant,
                GrantId.New(),
                Actor.Person(userId),
                capability,
                null,
                scope,
                kernelFixture.Clock.Advance(TimeSpan.FromSeconds(1)),
                null,
                false,
                KernelProtocolScenario.Key(),
                "Sync test grant"),
            ct);

    private static GatewayIdentity ExecutionCaller(string userId, string role, string tenantCode, string reason) =>
        TestSecurityContext.Identity(userId, role) with
        {
            TenantId = tenantCode,
            SiteIds = RoleNormalization.GetNormalizedRoles([role]).Any(SiteAccessEvaluator.SITE_SCOPED_ROLES.Contains) ? [SiteCodeValue] : [],
            ChangeReason = reason,
        };

    private static async Task<string> CreateSubjectAsync(HttpClient client, string studyCode, string subjectCode, CancellationToken ct)
    {
        using var res = await client.PostAsJsonAsync("/api/v1/execution/subjects", new
        {
            subject_id = subjectCode,
            study_id = studyCode,
            site_id = SiteCodeValue,
        }, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.Created, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task PatchStateAsync(HttpClient client, string subjectId, string targetStatus, string reason, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/execution/subjects/{subjectId}/state")
        {
            Content = JsonContent.Create(new { status = targetStatus }),
        };
        req.Headers.Remove(GatewayHeaders.ChangeReason);
        req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        using var res = await client.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
    }

    private async Task ApproveRandomizationSchemeAsync(string studyCode, string tenantCode, CancellationToken ct)
    {
        var authorId = ExecutionCaller(StatisticianA, "unblinded_statistician", tenantCode, "Draft randomization scheme");
        var approverId = ExecutionCaller(StatisticianB, "unblinded_statistician", tenantCode, "Approve randomization scheme");
        using var author = _executionHost!.CreateSignedClient(authorId);
        using var approver = _executionHost!.CreateSignedClient(approverId);

        using var drafted = await author.PostAsJsonAsync("/api/v1/execution/randomization/schemes", new
        {
            study_id = studyCode,
            method = "permuted_block",
            arms = new Dictionary<string, int> { ["ARM-ACTIVE"] = 1, ["ARM-PLACEBO"] = 1 },
            slots_per_stratum = 4,
            requires_kit = false,
        }, ct);
        string draftBody = await drafted.Content.ReadAsStringAsync(ct);
        drafted.StatusCode.Should().Be(HttpStatusCode.Created, draftBody);
        using var doc = JsonDocument.Parse(draftBody);
        Guid schemeId = Guid.Parse(doc.RootElement.GetProperty("id").GetString()!);

        string approvePath = RandomizationEndpoints.ApproveSchemeAction(schemeId);
        using var approveReq = new HttpRequestMessage(HttpMethod.Post, approvePath);
        approveReq.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                StatisticianB,
                approvePath,
                RandomizationEndpoints.ApproveSchemeSemanticAction,
                roles: [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN]));
        using var approved = await approver.SendAsync(approveReq, ct);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(ct));
    }

    private async Task SeedExecutionConsentAsync(string studyCode, string subjectCode, CancellationToken ct)
    {
        using var scope = _executionHost!.Services.CreateSeedingScope();
        var execDb = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        execDb.SubjectConsents.Add(SubjectConsent.Create(
            new ExecutionSubjectCode(subjectCode),
            new ProtocolVersionRef(new ExecutionStudyCode(studyCode), "1.0", 1),
            icfSigned: true,
            SystemClock.Instance.UtcNow.AddMinutes(-10),
            requiresReconsent: false,
            isPaperOverride: false));
        await execDb.SaveChangesAsync(ct);
    }

    private static async Task<KernelEnrollmentStateResponse> LookupKernelEnrollmentAsync(
        HttpClient kernelClient,
        string studyCode,
        string subjectCode,
        CancellationToken ct)
    {
        using var res = await kernelClient.GetAsync(
            new Uri($"/api/v1/kernel/enrollments/by-subject?study_code={Uri.EscapeDataString(studyCode)}&subject_code={Uri.EscapeDataString(subjectCode)}", UriKind.Relative),
            ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<KernelEnrollmentStateResponse>(body, Json)!;
    }

    private static async Task<Guid> OpenVitalSignsFormAsync(HttpClient kernelClient, Guid enrollmentId, CancellationToken ct)
    {
        using var res = await SendKernelFormRequestAsync(kernelClient, HttpMethod.Post, string.Empty, "Visit 1 vitals opened", new
        {
            enrollment_id = enrollmentId,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = SystemClock.Instance.UtcNow.AddMinutes(-5),
        }, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.Created, body);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("form_id").GetGuid();
    }

    private static async Task SaveFormSystolicAsync(HttpClient kernelClient, Guid formId, decimal value, long expectedRevision, CancellationToken ct)
    {
        using var res = await SendKernelFormRequestAsync(kernelClient, HttpMethod.Put, $"/{formId:D}/values", "Vital signs chart entry", new
        {
            values = new[]
            {
                new
                {
                    item_id = Systolic,
                    value = new { type = "quantity", value, unit = "mm[Hg]" },
                    expected_revision = expectedRevision,
                },
            },
        }, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        res.StatusCode.Should().Be(HttpStatusCode.OK, body);
    }

    private static Task<HttpResponseMessage> SendKernelFormRequestAsync(
        HttpClient kernelClient,
        HttpMethod method,
        string suffix,
        string reason,
        object body,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{suffix}", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
        req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        return kernelClient.SendAsync(req, ct);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
