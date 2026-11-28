using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

/// <summary>
/// Verifies through the real Knowledge host that duplicate delegation endpoints are retired (404) and that
/// <c>POST /api/v1/training/assignments/{assignment_id}/complete</c> enforces 21 CFR Part 11 step-up electronic
/// signatures (<c>X-Sig-Token</c>), personal trainee identity binding, <c>05.02.04</c> eTMF filing, and Kernel
/// <c>TrainingRecord</c> + <c>Qualification</c> registration.
/// </summary>
public sealed class DelegationSignOffAuthorityTests(DelegationSignOffAuthorityTests.KnowledgeFactory factory)
    : IClassFixture<DelegationSignOffAuthorityTests.KnowledgeFactory>
{
    private const string SemanticAction = "training.assignment.complete";

    private static readonly GatewayIdentity Trainee = TestSecurityContext.Identity("crc-trainee-1", "Clinical Research Coordinator") with
    {
        StudyIds = ["CARDIO-2026"],
        SiteIds = ["SITE-101"],
    };

    [Fact]
    [GxPRequirement("PRD-TRN-001")]
    public async Task RetiredDelegationRoutes_Return404OnKnowledgeHost()
    {
        using var client = factory.CreateSignedClient(Trainee);

        var getResponse = await client.GetAsync("/api/v1/training/delegations?study_id=CARDIO-2026", Ct);
        var postResponse = await client.PostAsJsonAsync("/api/v1/training/delegations/bulk-signoff", new { }, Ct);

        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        postResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CompleteAssignment_WithoutSigToken_Returns401AndSealsNothing()
    {
        string id = await SeedPendingAssignmentAsync();
        using var client = factory.CreateSignedClient(Trainee);

        var response = await client.PostAsJsonAsync(CompletePath(id), Body(), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(Ct));
        (await AssignmentAsync(id))!.Status.Should().Be("PENDING");
    }

    [Theory]
    [GxPRequirement("PRD-TRN-003")]
    [InlineData("Auditor")]
    public async Task CompleteAssignment_WithoutTrainingCompletePermission_Returns403(string role)
    {
        string id = await SeedPendingAssignmentAsync("auditor-1");
        var caller = TestSecurityContext.Identity("auditor-1", role) with
        {
            StudyIds = ["CARDIO-2026"],
            SiteIds = ["SITE-101"],
        };
        using var client = factory.CreateSignedClient(caller);

        var response = await SendAsync(client, CompletePath(id), TokenFor(caller.UserId, id), Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await AssignmentAsync(id))!.Status.Should().Be("PENDING");
    }

    [Fact]
    [GxPRequirement("PRD-TRN-003")]
    public async Task CompleteAssignment_SignedByDifferentUser_Returns403()
    {
        string id = await SeedPendingAssignmentAsync("crc-trainee-1");
        var otherStaff = TestSecurityContext.Identity("crc-other-2", "Clinical Research Coordinator") with
        {
            StudyIds = ["CARDIO-2026"],
            SiteIds = ["SITE-101"],
        };
        using var client = factory.CreateSignedClient(otherStaff);

        var response = await SendAsync(client, CompletePath(id), TokenFor(otherStaff.UserId, id), Body());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
        (await AssignmentAsync(id))!.Status.Should().Be("PENDING");
    }

    [Fact]
    [GxPRequirement("PRD-TRN-003")]
    public async Task CompleteAssignment_WithUnknownAssignmentId_Returns404AndCreatesNothing()
    {
        string unknown = $"asn-unknown-{Guid.NewGuid():N}";
        using var client = factory.CreateSignedClient(Trainee);

        var response = await SendAsync(client, CompletePath(unknown), TokenFor(Trainee.UserId, unknown), Body());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(Ct));
        (await AssignmentAsync(unknown)).Should().BeNull();
    }

    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task CompleteAssignment_WithTokenForAnotherAssignment_Returns401()
    {
        string id = await SeedPendingAssignmentAsync();
        string other = await SeedPendingAssignmentAsync();
        using var client = factory.CreateSignedClient(Trainee);

        var response = await SendAsync(client, CompletePath(id), TokenFor(Trainee.UserId, other), Body());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AssignmentAsync(id))!.Status.Should().Be("PENDING");
    }

    [Fact]
    [GxPRequirement("PRD-TRN-003")]
    [GxPRequirement("PRD-TRN-004")]
    public async Task CompleteAssignment_WithValidSigToken_SealsAsVerifiedCallerAndRegisters050204AndQualification()
    {
        string id = await SeedPendingAssignmentAsync();
        using var client = factory.CreateSignedClient(Trainee);

        var response = await SendAsync(client, CompletePath(id), TokenFor(Trainee.UserId, id), Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var payload = await response.Content.ReadFromJsonAsync<CompleteTrainingAssignmentResponse>(Ct);
        payload.Should().NotBeNull();
        payload!.EtmfArtifactId.Should().Be("05.02.04");
        payload.KernelArtifactVersionRef.Should().StartWith("artifact_version:");
        payload.KernelQualificationRef.Should().StartWith("qualification:");

        var assignment = (await AssignmentAsync(id))!;
        assignment.Status.Should().Be("COMPLETED");
        assignment.EtmfDocumentId.Should().Be(payload.EtmfDocumentId);
        assignment.KernelQualificationRef.Should().Be(payload.KernelQualificationRef);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        var audit = await db.OrgAuditLogs.AsNoTracking().SingleAsync(a => a.RecordId == id && a.Action == "training.assignment.complete", Ct);
        audit.ActorId.Should().Be(Trainee.UserId);
        var certificate = await db.TrainingCertificates.AsNoTracking().SingleAsync(c => c.AssignmentId == id, Ct);
        certificate.SignerName.Should().Be(Trainee.UserId);
        certificate.EtmfArtifactId.Should().Be("05.02.04");
        certificate.Sha256Seal.Should().NotBeNullOrEmpty();
    }

    private static string CompletePath(string assignmentId) => $"/api/v1/training/assignments/{assignmentId}/complete";

    private static object Body() => new
    {
        quiz_answers = new[]
        {
            new { question_id = "q-1", selected_option_id = "opt-b" },
        },
        time_spent_seconds = 300,
        signature_payload = new
        {
            meaning = "I have read, understood, and agree to follow this protocol version",
            reason_for_change = "Completed protocol V3.0 qualification module and quiz",
        },
    };

    private static string TokenFor(string userId, string assignmentId) =>
        TestSecurityContext.CreateSigToken(userId, CompletePath(assignmentId), SemanticAction);

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayHeaders.SigToken, token);
        return await client.SendAsync(request, Ct);
    }

    private async Task<string> SeedPendingAssignmentAsync(string userId = "crc-trainee-1")
    {
        string curriculumId = $"cur-{Guid.NewGuid():N}";
        string id = $"asn-{Guid.NewGuid():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        db.TrainingCurricula.Add(new TrainingCurriculum
        {
            Id = curriculumId,
            StudyId = new KernelStudyCode("CARDIO-2026"),
            ProtocolVersion = "3.0",
            ProtocolVersionNumber = 3,
            QualificationCode = "protocol_training",
            PassingScore = 0.8,
            Title = "Protocol V3.0 Qualification Curriculum",
            QuizQuestionsJson = "[{\"id\":\"q-1\",\"prompt\":\"Updated SBP threshold?\",\"options\":[{\"id\":\"opt-a\",\"label\":\"120\"},{\"id\":\"opt-b\",\"label\":\"140\"}],\"correct_option_id\":\"opt-b\"}]",
            IsActive = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "sponsor-dm",
        });
        db.TrainingAssignments.Add(new TrainingAssignment
        {
            Id = id,
            CurriculumId = curriculumId,
            StudyId = new KernelStudyCode("CARDIO-2026"),
            SiteId = new KernelSiteCode("SITE-101"),
            UserId = userId,
            UserName = "Clinical Research Coordinator",
            UserRole = "Clinical Research Coordinator",
            Status = "PENDING",
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "sponsor-dm",
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<TrainingAssignment?> AssignmentAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
        return await db.TrainingAssignments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, Ct);
    }

    private static System.Threading.CancellationToken Ct => TestContext.Current.CancellationToken;

    public sealed class KnowledgeFactory : WebApplicationFactory<Cadence.Apps.Knowledge.Program>
    {
        private readonly string _databaseName = $"knowledge-signoff-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            builder.ConfigureServices(services =>
            {
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);
                services.RemoveAll<DbContextOptions<KnowledgeDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<KnowledgeDbContext>>();
                services.AddDbContext<KnowledgeDbContext>((sp, options) => options
                    .UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(TestSupport.TestEfServiceProviders.InMemory)
                    .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                    .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance));

                services.RemoveAll<IKnowledgeKernelTrustClient>();
                services.AddSingleton<InMemoryKnowledgeKernelTrustClient>();
                services.AddSingleton<IKnowledgeKernelTrustClient>(sp => sp.GetRequiredService<InMemoryKnowledgeKernelTrustClient>());

                services.RemoveAll<IEtmfTrainingIngestClient>();
                services.AddSingleton<InMemoryEtmfTrainingIngestClient>();
                services.AddSingleton<IEtmfTrainingIngestClient>(sp => sp.GetRequiredService<InMemoryEtmfTrainingIngestClient>());

                services.RemoveAll<ISigTokenUseStore>();
                services.AddSingleton<ISigTokenUseStore>(new ReplayCache());
            });
        }
    }
}
