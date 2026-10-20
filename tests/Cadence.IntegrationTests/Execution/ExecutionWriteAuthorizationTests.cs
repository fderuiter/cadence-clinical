using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Signed callers acting on stored records through the real composition root on PostgreSQL: callers without the
/// needed role get 403 (#4990 moved these from the unit tests, whose host ran on the EF Core in-memory provider). The
/// pre-persistence checks (anonymous and forged callers get 401) stay in
/// <c>Cadence.UnitTests.Execution.Endpoints.ExecutionAuthenticationTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionWriteAuthorizationTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string ReviewPath = "/api/v1/execution/labs/toxicity/review/";

    private static readonly GatewayIdentity Cra = TestSecurityContext.Identity("cra-01", "CRA") with { ChangeReason = "Authorization check" };
    private static readonly GatewayIdentity Investigator = TestSecurityContext.Identity("pi-01", "Site Investigator") with { ChangeReason = "Authorization check" };

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ReviewToxicity_AsSignedCallerWithoutInvestigatorRole_Returns403()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string reviewId = await StageReviewAsync();
        using var cra = factory.CreateSignedClient(Cra);

        var response = await cra.PostAsJsonAsync(
            $"{ReviewPath}{reviewId}/action",
            new { action = "DISMISS", reason_for_change = "Lab artifact, repeat normal" },
            ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReviewStatusAsync(reviewId)).Should().Be("STAGED_FOR_INVESTIGATOR_REVIEW");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ReviewToxicity_AsSignedInvestigator_Succeeds()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string reviewId = await StageReviewAsync();
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await investigator.PostAsJsonAsync(
            $"{ReviewPath}{reviewId}/action",
            new { action = "DISMISS", reason_for_change = "Lab artifact, repeat normal" },
            ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        (await ReviewStatusAsync(reviewId)).Should().Be("DISMISSED");
    }

    private async Task<string> StageReviewAsync()
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditActorScope>()
            .RunAs(new ServiceAccount("svc:integration-test", "Stage a toxicity review for an authorization check"));
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var review = StagedAdverseEventReview.Create(
            studyId: new StudyCode("STUDY-AUTH"),
            subjectId: new SubjectCode($"SUBJ-{Guid.NewGuid():N}"),
            testCode: new TestCode("ALT"),
            testName: "Alanine Aminotransferase",
            labValue: 250.0,
            unit: "U/L",
            ctcaeGrade: 3,
            toxicityTerm: "ALT increased",
            clinicalDescription: "Grade 3 ALT elevation",
            isHysLawWarning: false,
            isDeltaCheckAlert: false,
            deltaPercentage: null);
        db.StagedAdverseEventReviews.Add(review);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return review.Id;
    }

    private async Task<string> ReviewStatusAsync(string reviewId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExecutionDbContext>();
        var review = await db.StagedAdverseEventReviews.AsNoTracking()
            .SingleAsync(r => r.Id == reviewId, TestContext.Current.CancellationToken);
        return review.ReviewStatus;
    }
}
