using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Kernel;
using Cadence.Kernel.Events;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;

using FluentAssertions;

using MassTransit;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Consumers;

public class FormVersionPublishedConsumerTests
{
    private readonly InMemoryPublishedFormVersionRegistryRepository _repo = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Consume_WhenValidFormVersionPublishedEvent_UpsertsRegistry()
    {
        // Arrange
        var consumer = new FormVersionPublishedConsumer(_repo, _clock);
        var tenantId = TenantId.New();
        var studyId = StudyId.New();
        var protocolVersionId = ProtocolVersionId.New();

        var evt = new FormVersionPublished(
            tenantId,
            studyId,
            protocolVersionId,
            "1.0.0",
            "FORM-VITALS",
            "HASH_ABC_123",
            "<xml>vitals</xml>",
            "{\"items\": []}");

        var context = Substitute.For<ConsumeContext<FormVersionPublished>>();
        context.Message.Returns(evt);
        context.CancellationToken.Returns(CancellationToken.None);

        // Act
        await consumer.Consume(context);

        // Assert
        var registered = await _repo.GetByVersionAndFormAsync(studyId.Value.ToString(), "1.0.0", "FORM-VITALS", CancellationToken.None);
        registered.Should().NotBeNull();
        registered!.FormHash.Should().Be("HASH_ABC_123");
        registered.OpenRosaXml.Should().Be("<xml>vitals</xml>");
        registered.DdfIr.Should().Be("{\"items\": []}");
        registered.PublishedAt.Should().Be(_clock.UtcNow);
    }
}
