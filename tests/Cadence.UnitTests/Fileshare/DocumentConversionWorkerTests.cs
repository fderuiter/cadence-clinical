using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Workers;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Xunit;

namespace Cadence.UnitTests.Fileshare;

public sealed class DocumentConversionWorkerTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static FileshareDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<FileshareDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new FileshareDbContext(options);
    }

    [Fact]
    public async Task ProcessPendingConversionsAsync_WithPendingFile_ConvertsAndSetsCompletedStatus()
    {
        var dbName = $"ConversionTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var fileRecord = new FileRecord("doc-1")
        {
            Filename = "protocol.docx",
            ObjectKey = "docs/doc-1",
            UploadedAt = FixedTimestamp,
            ConversionStatus = "PENDING"
        };
        dbContext.FileRecords.Add(fileRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IDocumentConverterProcessor>();
        mockProcessor.ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns("docs/doc-1.pdf");
        services.AddScoped(_ => mockProcessor);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var workerOptions = Options.Create(new FileshareWorkerOptions
        {
            BatchSize = 10,
            MaxConcurrentJobs = 2,
            MaxRetryCount = 3,
            InitialBackoff = TimeSpan.FromMilliseconds(10)
        });

        var worker = new DocumentConversionWorker(scopeFactory, workerOptions, NullLogger<DocumentConversionWorker>.Instance);

        await worker.ProcessPendingConversionsAsync(TestContext.Current.CancellationToken);

        using var verifyDb = CreateDbContext(dbName);
        var updated = await verifyDb.FileRecords.FindAsync(["doc-1"], TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.ConversionStatus.Should().Be("COMPLETED");
        updated.PdfRenditionKey.Should().Be("docs/doc-1.pdf");
        updated.ConversionError.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingConversionsAsync_WhenConversionFails_RetriesAndSetsFailedStatus()
    {
        var dbName = $"ConversionTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var fileRecord = new FileRecord("doc-fail")
        {
            Filename = "corrupt.docx",
            ObjectKey = "docs/doc-fail",
            UploadedAt = FixedTimestamp,
            ConversionStatus = "PENDING"
        };
        dbContext.FileRecords.Add(fileRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IDocumentConverterProcessor>();
        mockProcessor.ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("LibreOffice process exit code 1"));
        services.AddScoped(_ => mockProcessor);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var workerOptions = Options.Create(new FileshareWorkerOptions
        {
            BatchSize = 10,
            MaxConcurrentJobs = 2,
            MaxRetryCount = 2,
            InitialBackoff = TimeSpan.FromMilliseconds(1)
        });

        var worker = new DocumentConversionWorker(scopeFactory, workerOptions, NullLogger<DocumentConversionWorker>.Instance);

        await worker.ProcessPendingConversionsAsync(TestContext.Current.CancellationToken);

        using var verifyDb = CreateDbContext(dbName);
        var updated = await verifyDb.FileRecords.FindAsync(["doc-fail"], TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.ConversionStatus.Should().Be("FAILED");
        updated.ConversionError.Should().Contain("LibreOffice process");
        await mockProcessor.Received(2).ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingConversionsAsync_IgnoresNonPendingOrDeletedRecords()
    {
        var dbName = $"ConversionTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var completedRecord = new FileRecord("doc-done")
        {
            UploadedAt = FixedTimestamp,
            ConversionStatus = "COMPLETED"
        };
        var deletedRecord = new FileRecord("doc-del")
        {
            UploadedAt = FixedTimestamp,
            ConversionStatus = "PENDING",
            IsDeleted = true
        };
        dbContext.FileRecords.AddRange(completedRecord, deletedRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IDocumentConverterProcessor>();
        services.AddScoped(_ => mockProcessor);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var workerOptions = Options.Create(new FileshareWorkerOptions());
        var worker = new DocumentConversionWorker(scopeFactory, workerOptions, NullLogger<DocumentConversionWorker>.Instance);

        await worker.ProcessPendingConversionsAsync(TestContext.Current.CancellationToken);

        await mockProcessor.DidNotReceive().ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingConversionsAsync_WithMultiTenantResolverAndOnlyOnePendingTenant_ProcessesPendingTenantOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = $"ConversionMultiTenant_{Guid.NewGuid():N}";
        var activeTenantIds = new Guid[200];
        for (int i = 0; i < activeTenantIds.Length; i++)
        {
            activeTenantIds[i] = Guid.NewGuid();
        }

        Guid targetTenant = activeTenantIds[17];

        var resolver = Substitute.For<Cadence.Database.Tenancy.ITenantCodeResolver>();
        resolver.ListActiveTenantIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<System.Collections.Generic.IReadOnlyList<Guid>>(activeTenantIds));

        var services = new ServiceCollection();
        services.AddSingleton(resolver);
        services.AddScoped<Cadence.Database.Tenancy.CurrentTenantAccessor>(sp =>
            new Cadence.Database.Tenancy.CurrentTenantAccessor(
                sp.GetRequiredService<Cadence.Database.Tenancy.ITenantCodeResolver>()));
        services.AddScoped<Cadence.Database.Tenancy.ICurrentTenantAccessor>(sp =>
            sp.GetRequiredService<Cadence.Database.Tenancy.CurrentTenantAccessor>());
        services.AddScoped<Cadence.Database.Tenancy.ITenantScope>(sp =>
            sp.GetRequiredService<Cadence.Database.Tenancy.CurrentTenantAccessor>());
        services.AddScoped(sp =>
        {
            var accessor = sp.GetRequiredService<Cadence.Database.Tenancy.ICurrentTenantAccessor>();
            var optionsBuilder = new DbContextOptionsBuilder<FileshareDbContext>();
            optionsBuilder.UseInMemoryDatabase(dbName);
            optionsBuilder.UseCadenceStandaloneTenancy(accessor, isRelational: false);
            optionsBuilder.AddInterceptors(Cadence.Database.Tenancy.StandaloneTenantSaveChangesInterceptor.Instance);
            return new FileshareDbContext(optionsBuilder.Options);
        });

        var mockProcessor = Substitute.For<IDocumentConverterProcessor>();
        mockProcessor.ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns("docs/tenant-17.pdf");
        services.AddScoped(_ => mockProcessor);

        using var provider = services.BuildServiceProvider();

        using (var seedScope = provider.CreateScope())
        {
            using var _ = seedScope.ServiceProvider.GetRequiredService<Cadence.Database.Tenancy.ITenantScope>().Bind(targetTenant);
            var seedDb = seedScope.ServiceProvider.GetRequiredService<FileshareDbContext>();
            seedDb.FileRecords.Add(new FileRecord("doc-t17")
            {
                Filename = "tenant17.docx",
                ObjectKey = "docs/tenant-17",
                UploadedAt = FixedTimestamp,
                ConversionStatus = "PENDING",
            });
            await seedDb.SaveChangesAsync(ct);
        }

        var worker = new DocumentConversionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new FileshareWorkerOptions { BatchSize = 10, MaxConcurrentJobs = 2 }),
            NullLogger<DocumentConversionWorker>.Instance);

        await worker.ProcessPendingConversionsAsync(ct);

        await mockProcessor.Received(1).ConvertToPdfAsync(
            Arg.Is<FileRecord>(f => f.Id == "doc-t17"),
            Arg.Any<CancellationToken>());
    }
}
