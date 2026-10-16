using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Imaging;

public sealed class DicomWebEndpointsTests
{
    [Fact(DisplayName = "StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreInstancesAsync(
            httpContext.Request,
            mockIngestionService,
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var value = ((IValueHttpResult)result).Value;
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        json.Should().Contain("\"EMPTY_PAYLOAD\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestInstanceAsync(
            default!, default!, default!, default!, default!, Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact(DisplayName = "StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(
            httpContext.Request,
            mockIngestionService,
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var value = ((IValueHttpResult)result).Value;
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        json.Should().Contain("\"EMPTY_PAYLOAD\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!, Arg.Any<System.Threading.CancellationToken>());
    }

    /// <summary>
    /// <c>GET /certificates/{id}</c> verifies a stored certificate with the configured key (#5399): an untouched
    /// certificate is valid, and one whose stored fields were altered, or that another key signed, is not.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public async Task GetCertificateByIdAsync_VerifiesWithTheConfiguredKey_AndRejectsAlteredCertificates()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingCertificateTest_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var deidentifier = DicomIngestionServiceTests.TestDeidentifier();
        var ingested = await new DicomIngestionService(dbContext, deidentifier, SystemClock.Instance).IngestInstanceAsync(
            DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("60001"), "SUBJ-6001", "SITE-601", "STUDY-6001", "tester", ct);
        var caller = new DefaultHttpContext();
        caller.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal { UserId = "reader", AssignedStudies = ["STUDY-6001"] };

        async Task<bool> IsValidAsync(DicomDeidentifier key)
        {
            var result = await DicomWebEndpoints.GetCertificateByIdAsync(ingested.CertificateId, dbContext, key, caller, ct);
            return result.Should().BeOfType<Ok<CertificateResponse>>().Which.Value!.IsSignatureValid;
        }

        (await IsValidAsync(deidentifier)).Should().BeTrue();
        (await IsValidAsync(new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)))).Should().BeFalse();

        var stored = await dbContext.DeidentificationCertificates.SingleAsync(c => c.Id == ingested.CertificateId, ct);
        stored.SubjectId = "SUBJ-OTHER";
        await dbContext.SaveChangesAsync(ct);

        (await IsValidAsync(deidentifier)).Should().BeFalse();
    }
}
