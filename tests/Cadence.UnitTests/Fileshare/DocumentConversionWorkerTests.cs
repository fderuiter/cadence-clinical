using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Workers;

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
            UploadedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
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
        var updated = await verifyDb.FileRecords.FindAsync(new object[] { "doc-1" }, TestContext.Current.CancellationToken);
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
            UploadedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
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
        var updated = await verifyDb.FileRecords.FindAsync(new object[] { "doc-fail" }, TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.ConversionStatus.Should().Be("FAILED");
        updated.ConversionError.Should().Contain("LibreOffice process exit code 1");
        await mockProcessor.Received(2).ConvertToPdfAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingConversionsAsync_IgnoresNonPendingOrDeletedRecords()
    {
        var dbName = $"ConversionTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var completedRecord = new FileRecord("doc-done")
        {
            UploadedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ConversionStatus = "COMPLETED"
        };
        var deletedRecord = new FileRecord("doc-del")
        {
            UploadedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
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
}
