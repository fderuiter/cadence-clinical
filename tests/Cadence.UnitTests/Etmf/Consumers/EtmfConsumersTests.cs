using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Infrastructure.Consumers;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Etmf.Consumers;

public class EtmfConsumersTests
{
    private static ServiceProvider CreateEtmfServiceProvider(EtmfDbContext dbContext, IClock? clock = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IClock>(clock ?? new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<EtmfDocumentFilingRequestedConsumer>();
            x.AddConsumer<EConsentCompletedConsumer>();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_EtmfDocumentFilingRequested_FilesDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        await using var provider = CreateEtmfServiceProvider(dbContext, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EtmfDocumentFilingRequested(
            DocumentId: "DOC-2001",
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            ZoneCode: "1",
            SectionCode: "01.01",
            ArtifactCode: "PROTOCOL",
            DocumentTitle: "Protocol_v1.pdf",
            StoragePath: "s3://etmf/DOC-2001.pdf",
            RequestedBy: "cra-user-1",
            RequestedAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<EtmfDocumentFilingRequested>(ct)).Should().BeTrue();

        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.Id == "DOC-2001" || d.IdempotencyKey == "DOC-2001", ct);
        doc.Should().NotBeNull();
        doc!.StudyId.Should().Be("STUDY-01");
        doc.SiteId.Should().Be("SITE-01");
        doc.Filename.Should().Be("Protocol_v1.pdf");
        doc.Status.Should().Be("FILED");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_EtmfDocumentFilingRequested_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        await using var provider = CreateEtmfServiceProvider(dbContext, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EtmfDocumentFilingRequested(
            DocumentId: "DOC-2002",
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            ZoneCode: "1",
            SectionCode: "01.01",
            ArtifactCode: "PROTOCOL",
            DocumentTitle: "Protocol_v1.pdf",
            StoragePath: "s3://etmf/DOC-2002.pdf",
            RequestedBy: "cra-user-1",
            RequestedAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<EtmfDocumentFilingRequested>(ct)).Should().BeTrue();

        // Publish duplicate message
        await harness.Bus.Publish(message, ct);

        var count = await dbContext.TmfDocuments.CountAsync(d => d.Id == "DOC-2002" || d.IdempotencyKey == "DOC-2002", ct);
        count.Should().Be(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_EConsentCompleted_FilesICFDocumentInEtmf()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        await using var provider = CreateEtmfServiceProvider(dbContext, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var signedAt = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);
        var message = new EConsentCompleted(
            SubjectId: "SUBJ-99",
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            VersionTag: "v1.0",
            VersionIndex: 1,
            SignedAt: signedAt);

        await harness.Bus.Publish(message, ct);

        (await harness.Consumed.Any<EConsentCompleted>(ct)).Should().BeTrue();

        var expectedKey = "SUBJ-99:STUDY-01:1";
        var doc = await dbContext.TmfDocuments.FirstOrDefaultAsync(d => d.IdempotencyKey == expectedKey, ct);
        doc.Should().NotBeNull();
        doc!.Zone.Should().Be(5);
        doc.ArtifactCode.Should().Be("ICF");
        doc.ProtocolVersionTag.Should().Be("v1.0");
        doc.ProtocolVersionIndex.Should().Be(1);
        doc.SigningTimestamp.Should().Be(signedAt);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Consume_EConsentCompleted_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        await using var provider = CreateEtmfServiceProvider(dbContext, clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new EConsentCompleted(
            SubjectId: "SUBJ-100",
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            VersionTag: "v1.0",
            VersionIndex: 1,
            SignedAt: clock.UtcNow);

        await harness.Bus.Publish(message, ct);
        (await harness.Consumed.Any<EConsentCompleted>(ct)).Should().BeTrue();

        // Duplicate publish
        await harness.Bus.Publish(message, ct);

        var expectedKey = "SUBJ-100:STUDY-01:1";
        var count = await dbContext.TmfDocuments.CountAsync(d => d.IdempotencyKey == expectedKey, ct);
        count.Should().Be(1);
    }
}
