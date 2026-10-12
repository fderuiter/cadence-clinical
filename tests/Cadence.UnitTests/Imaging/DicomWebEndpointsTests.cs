using System;
using System.IO;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Services;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

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
}
