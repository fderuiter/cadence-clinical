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

public sealed class MediaTranscoderWorkerTests
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
    public async Task ProcessPendingTranscodesAsync_WithPendingFile_TranscodesAndSetsCompletedStatus()
    {
        var dbName = $"TranscodeTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var fileRecord = new FileRecord("video-1")
        {
            Filename = "video.mp4",
            ObjectKey = "videos/video-1",
            UploadedAt = FixedTimestamp,
            TranscodeStatus = "PENDING"
        };
        dbContext.FileRecords.Add(fileRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IMediaTranscoderProcessor>();
        mockProcessor.TranscodeToHlsAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns("videos/video-1/master.m3u8");
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

        var worker = new MediaTranscoderWorker(scopeFactory, workerOptions, NullLogger<MediaTranscoderWorker>.Instance);

        await worker.ProcessPendingTranscodesAsync(TestContext.Current.CancellationToken);

        using var verifyDb = CreateDbContext(dbName);
        var updated = await verifyDb.FileRecords.FindAsync(["video-1"], TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.TranscodeStatus.Should().Be("COMPLETED");
        updated.HlsManifestKey.Should().Be("videos/video-1/master.m3u8");
        updated.TranscodeError.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingTranscodesAsync_WhenTranscodeFails_RetriesAndSetsFailedStatus()
    {
        var dbName = $"TranscodeTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var fileRecord = new FileRecord("video-fail")
        {
            Filename = "corrupt.mp4",
            ObjectKey = "videos/video-fail",
            UploadedAt = FixedTimestamp,
            TranscodeStatus = "PENDING"
        };
        dbContext.FileRecords.Add(fileRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IMediaTranscoderProcessor>();
        mockProcessor.TranscodeToHlsAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("FFmpeg error: Invalid data found"));
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

        var worker = new MediaTranscoderWorker(scopeFactory, workerOptions, NullLogger<MediaTranscoderWorker>.Instance);

        await worker.ProcessPendingTranscodesAsync(TestContext.Current.CancellationToken);

        using var verifyDb = CreateDbContext(dbName);
        var updated = await verifyDb.FileRecords.FindAsync(["video-fail"], TestContext.Current.CancellationToken);
        updated.Should().NotBeNull();
        updated!.TranscodeStatus.Should().Be("FAILED");
        updated.TranscodeError.Should().Contain("FFmpeg error");
        await mockProcessor.Received(2).TranscodeToHlsAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPendingTranscodesAsync_IgnoresNonPendingOrDeletedRecords()
    {
        var dbName = $"TranscodeTest_{Guid.NewGuid():N}";
        using var dbContext = CreateDbContext(dbName);

        var completedRecord = new FileRecord("video-done")
        {
            UploadedAt = FixedTimestamp,
            TranscodeStatus = "COMPLETED"
        };
        var deletedRecord = new FileRecord("video-del")
        {
            UploadedAt = FixedTimestamp,
            TranscodeStatus = "PENDING",
            IsDeleted = true
        };
        dbContext.FileRecords.AddRange(completedRecord, deletedRecord);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection();
        services.AddScoped(_ => CreateDbContext(dbName));

        var mockProcessor = Substitute.For<IMediaTranscoderProcessor>();
        services.AddScoped(_ => mockProcessor);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var workerOptions = Options.Create(new FileshareWorkerOptions());
        var worker = new MediaTranscoderWorker(scopeFactory, workerOptions, NullLogger<MediaTranscoderWorker>.Instance);

        await worker.ProcessPendingTranscodesAsync(TestContext.Current.CancellationToken);

        await mockProcessor.DidNotReceive().TranscodeToHlsAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }
}
