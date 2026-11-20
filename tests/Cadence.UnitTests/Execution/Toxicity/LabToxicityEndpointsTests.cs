using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Toxicity;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Toxicity;

public sealed class LabToxicityEndpointsTests : IDisposable
{
    private static readonly GatewayIdentity Investigator = TestSecurityContext.Identity("pi_user_01", "Site Investigator") with { SiteIds = ["SITE-01"] };

    private readonly LabToxicityTestFactory _factory = new();
    private readonly HttpClient _client;

    public LabToxicityEndpointsTests()
    {
        _client = _factory.CreateSignedClient(Investigator);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact(DisplayName = "ReviewActionPath_FormatsCanonicalEndpointPath")]
    public void ReviewActionPath_FormatsCanonicalEndpointPath()
    {
        string path = LabToxicityEndpoints.ReviewActionPath("REV-4001");
        path.Should().Be("/api/v1/execution/labs/toxicity/review/REV-4001/action");
    }

    [Fact(DisplayName = "ReviewActionSemanticAction_IsExecutionLabToxicityReviewAction")]
    public void ReviewActionSemanticAction_IsExecutionLabToxicityReviewAction()
    {
        LabToxicityEndpoints.ReviewActionSemanticAction.Should().Be("execution.lab_toxicity_review.action");
    }

    [Fact(DisplayName = "ExecuteReviewAction_WithoutSigToken_Returns401ReauthenticationRequired")]
    public async Task ExecuteReviewAction_WithoutSigToken_Returns401ReauthenticationRequired()
    {
        // Arrange
        const string reviewId = "REV-1001";
        var request = new StagedAdverseEventActionRequest(
            Action: "CONFIRM",
            ReasonForChange: "PI confirmation of Grade 3 AE");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, LabToxicityEndpoints.ReviewActionPath(reviewId))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Change-Reason", "PI confirmation of Grade 3 AE");

        // Act
        var response = await _client.SendAsync(httpRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        content.Should().Contain(SigTokenVerifier.ReauthenticationRequired);

        await _factory.GovernanceService.DidNotReceive().ExecuteActionAsync(
            Arg.Any<string>(), Arg.Any<StagedAdverseEventActionRequest>(), Arg.Any<AuthenticatedCaller>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteReviewAction_WithTokenForDifferentReviewId_Returns401Unauthorized")]
    public async Task ExecuteReviewAction_WithTokenForDifferentReviewId_Returns401Unauthorized()
    {
        // Arrange
        const string reviewId = "REV-1001";
        const string differentReviewId = "REV-9999";
        var request = new StagedAdverseEventActionRequest(
            Action: "CONFIRM",
            ReasonForChange: "PI confirmation of Grade 3 AE");

        var wrongToken = TestSecurityContext.CreateSigToken(
            Investigator.UserId,
            LabToxicityEndpoints.ReviewActionPath(differentReviewId),
            LabToxicityEndpoints.ReviewActionSemanticAction);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, LabToxicityEndpoints.ReviewActionPath(reviewId))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Change-Reason", "PI confirmation of Grade 3 AE");
        httpRequest.Headers.Add(GatewayHeaders.SigToken, wrongToken);

        // Act
        var response = await _client.SendAsync(httpRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.GovernanceService.DidNotReceive().ExecuteActionAsync(
            Arg.Any<string>(), Arg.Any<StagedAdverseEventActionRequest>(), Arg.Any<AuthenticatedCaller>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteReviewAction_WithTokenForDifferentSemanticAction_Returns401Unauthorized")]
    public async Task ExecuteReviewAction_WithTokenForDifferentSemanticAction_Returns401Unauthorized()
    {
        // Arrange
        const string reviewId = "REV-1001";
        var request = new StagedAdverseEventActionRequest(
            Action: "CONFIRM",
            ReasonForChange: "PI confirmation of Grade 3 AE");

        var wrongToken = TestSecurityContext.CreateSigToken(
            Investigator.UserId,
            LabToxicityEndpoints.ReviewActionPath(reviewId),
            "execution.form.approve");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, LabToxicityEndpoints.ReviewActionPath(reviewId))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Change-Reason", "PI confirmation of Grade 3 AE");
        httpRequest.Headers.Add(GatewayHeaders.SigToken, wrongToken);

        // Act
        var response = await _client.SendAsync(httpRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _factory.GovernanceService.DidNotReceive().ExecuteActionAsync(
            Arg.Any<string>(), Arg.Any<StagedAdverseEventActionRequest>(), Arg.Any<AuthenticatedCaller>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ExecuteReviewAction_WithValidSigToken_SucceedsAndInvokesGovernanceService")]
    public async Task ExecuteReviewAction_WithValidSigToken_SucceedsAndInvokesGovernanceService()
    {
        // Arrange
        const string reviewId = "REV-1001";
        var request = new StagedAdverseEventActionRequest(
            Action: "CONFIRM",
            ReasonForChange: "PI confirmation of Grade 3 AE",
            InvestigatorNotes: "Patient monitored closely");

        _factory.GovernanceService.ExecuteActionAsync(
            reviewId, Arg.Any<StagedAdverseEventActionRequest>(), Arg.Any<AuthenticatedCaller>(), Arg.Any<CancellationToken>())
            .Returns(Result<StagedAdverseEventActionResponse>.Success(new StagedAdverseEventActionResponse(
                Id: reviewId,
                ReviewStatus: "CONFIRMED",
                ActionTaken: "CONFIRM",
                Message: "Staged adverse event review action executed successfully.",
                Timestamp: SystemClock.Instance.UtcNow)));

        var validToken = TestSecurityContext.CreateSigToken(
            Investigator.UserId,
            LabToxicityEndpoints.ReviewActionPath(reviewId),
            LabToxicityEndpoints.ReviewActionSemanticAction);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, LabToxicityEndpoints.ReviewActionPath(reviewId))
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("X-Change-Reason", "PI confirmation of Grade 3 AE");
        httpRequest.Headers.Add(GatewayHeaders.SigToken, validToken);

        // Act
        var response = await _client.SendAsync(httpRequest, TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _factory.GovernanceService.Received(1).ExecuteActionAsync(
            reviewId, Arg.Is<StagedAdverseEventActionRequest>(r => r.Action == "CONFIRM"), Arg.Any<AuthenticatedCaller>(), Arg.Any<CancellationToken>());
    }

    private sealed class LabToxicityTestFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Cadence.Apps.Execution.Program>
    {
        public ILabToxicityGovernanceService GovernanceService { get; } = Substitute.For<ILabToxicityGovernanceService>();

        protected override IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureWebHost(webHost => webHost
                    .UseTestServer()
                    .UseTestGatewaySecret()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddCadenceSecurity();
                        services.AddSingleton(GovernanceService);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCadenceSecurity();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapLabToxicityEndpoints();
                        });
                    }));
    }
}
