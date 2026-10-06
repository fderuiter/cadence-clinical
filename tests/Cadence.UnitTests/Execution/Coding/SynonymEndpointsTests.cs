using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Coding;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Coding;

public sealed class SynonymEndpointsTests
{
    private static DefaultHttpContext CreateHttpContext(string role = "DATA_MANAGER")
    {
        return new DefaultHttpContext
        {
            User = TestSecurityContext.CreatePrincipal("test_user_dm", role)
        };
    }

    [Fact]
    public async Task ValidateSynonymFileAsync_WithUnauthorizedRole_Returns403Forbidden()
    {
        var httpContext = CreateHttpContext(role: "INVESTIGATOR");
        var service = Substitute.For<ISynonymImportService>();

        var result = await SynonymEndpoints.ValidateSynonymFileAsync(httpContext, service, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ValidateSynonymFileAsync_WithoutFormContentType_Returns400BadRequest()
    {
        var httpContext = CreateHttpContext(role: "DATA_MANAGER");
        var service = Substitute.For<ISynonymImportService>();

        var result = await SynonymEndpoints.ValidateSynonymFileAsync(httpContext, service, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ImportSynonymFileAsync_WithoutReasonForChange_Returns400BadRequest()
    {
        var httpContext = CreateHttpContext(role: "DATA_MANAGER");
        httpContext.Request.ContentType = "multipart/form-data; boundary=----WebKitFormBoundary123";

        var csvBytes = Encoding.UTF8.GetBytes("verbatim_text,dictionary_type,dictionary_version,target_code,target_term,target_level\nheadache,MEDDRA,26.0,10019211,Headache,PT");
        var formFile = new FormFile(new MemoryStream(csvBytes), 0, csvBytes.Length, "file", "synonyms.csv");
        var formCollection = new FormCollection(
            new Dictionary<string, StringValues>
            {
                { "scope", "STUDY" },
                { "study_id", "STUDY-101" }
            },
            new FormFileCollection { formFile });

        httpContext.Request.Form = formCollection;

        var service = Substitute.For<ISynonymImportService>();

        var result = await SynonymEndpoints.ImportSynonymFileAsync(httpContext, service, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("reason for change");
    }

    [Fact]
    public async Task GetActiveSynonymRulesAsync_CallsServiceAndReturnsOk()
    {
        var service = Substitute.For<ISynonymImportService>();
        var expectedRules = new List<SynonymRuleResponse>
        {
            new SynonymRuleResponse(
                Guid.NewGuid(),
                "STUDY",
                "STUDY-101",
                "HEADACHE",
                "MEDDRA",
                "26.0",
                "10019211",
                "Headache",
                "PT",
                "ACTIVE",
                Guid.NewGuid(),
                "user1",
                FakeClock.DefaultStart.UtcDateTime,
                "Reason")
        };

        service.GetActiveSynonymRulesAsync("STUDY-101", Arg.Any<CancellationToken>())
            .Returns(expectedRules);

        var result = await SynonymEndpoints.GetActiveSynonymRulesAsync(service, "STUDY-101", null, TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<IReadOnlyList<SynonymRuleResponse>>>();
        var ok = (Ok<IReadOnlyList<SynonymRuleResponse>>)result;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeEquivalentTo(expectedRules);
    }

    [Fact]
    public async Task DeactivateSynonymRuleAsync_WithValidReason_ReturnsOk()
    {
        var httpContext = CreateHttpContext(role: "SPONSOR_DESIGNER");
        httpContext.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "reason_for_change", "Obsolete mapping deactivation" }
        });

        var ruleId = Guid.NewGuid();
        var service = Substitute.For<ISynonymImportService>();
        service.DeactivateRuleAsync(ruleId, "Obsolete mapping deactivation", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await SynonymEndpoints.DeactivateSynonymRuleAsync(ruleId, httpContext, service, TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        var statusResult = (IStatusCodeHttpResult)result;
        statusResult.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
