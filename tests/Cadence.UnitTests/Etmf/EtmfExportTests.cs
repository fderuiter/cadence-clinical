using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Kernel.Time;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public class EtmfExportTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void CreateZipArchive_CreatesValidZipWithManifestsAndTaxonomyStructure()
    {
        var doc1 = new TmfDocument("DOC-101")
        {
            StudyId = "STUDY-99",
            SiteId = "SITE-1",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Filename = "Protocol_v1.pdf",
            MimeType = "application/pdf",
            ProtocolVersionIndex = 1,
            Status = "APPROVED",
            Content = "Sample protocol content"
        };

        var doc2 = new TmfDocument("DOC-102")
        {
            StudyId = "STUDY-99",
            SiteId = "SITE-1",
            Zone = 5,
            Section = "05.02",
            ArtifactCode = "05.02.01",
            ArtifactType = "1572",
            Filename = "FDA_1572.pdf",
            MimeType = "application/pdf",
            ProtocolVersionIndex = 1,
            Status = "APPROVED",
            Content = "Sample 1572 content"
        };

        var docs = new List<TmfDocument> { doc1, doc2 };
        var zipBytes = EtmfExportPackager.CreateZipArchive("EXP-001", "STUDY-99", docs, _clock.UtcNow);

        zipBytes.Should().NotBeNullOrEmpty();

        using var memoryStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        archive.Entries.Should().HaveCount(4); // doc1, doc2, manifest.json, manifest.csv

        var entry1 = archive.GetEntry("Zone_01/Section_01.01/Protocol_v1.pdf");
        entry1.Should().NotBeNull();

        var entry2 = archive.GetEntry("Zone_05/Section_05.02/FDA_1572.pdf");
        entry2.Should().NotBeNull();

        var jsonEntry = archive.GetEntry("manifest.json");
        jsonEntry.Should().NotBeNull();

        var csvEntry = archive.GetEntry("manifest.csv");
        csvEntry.Should().NotBeNull();

        using var jsonReader = new StreamReader(jsonEntry!.Open());
        var jsonText = jsonReader.ReadToEnd();
        jsonText.Should().Contain("EXP-001").And.Contain("STUDY-99").And.Contain("DOC-101").And.Contain("DOC-102");

        using var csvReader = new StreamReader(csvEntry!.Open());
        var csvText = csvReader.ReadToEnd();
        csvText.Should().Contain("document_id,filename,zone,section,artifact_code,artifact_type,version_number,sha256,path")
               .And.Contain("DOC-101")
               .And.Contain("DOC-102");
    }

    [Fact]
    public async Task ExportDocuments_Synchronous_ReturnsZipFileStream()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var doc = new TmfDocument("DOC-201")
        {
            StudyId = "STUDY-01",
            SiteId = "SITE-1",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Filename = "Protocol.pdf",
            MimeType = "application/pdf",
            ProtocolVersionIndex = 1,
            Status = "APPROVED",
            Content = "Protocol Content"
        };
        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-01", ["DOC-201"], IsFullBinder: false);

        var result = await EtmfIntelligenceEndpoints.ExportDocuments(request, dbContext, queue, _clock, new Microsoft.AspNetCore.Http.DefaultHttpContext(), ct);

        result.Should().BeOfType<FileContentHttpResult>();
        var fileResult = (FileContentHttpResult)result;
        fileResult.ContentType.Should().Be("application/zip");
        fileResult.FileDownloadName.Should().StartWith("etmf_export_STUDY-01");
        fileResult.FileContents.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportDocuments_Asynchronous_ReturnsAcceptedAndEnqueuesJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-01", null, IsFullBinder: true);

        var result = await EtmfIntelligenceEndpoints.ExportDocuments(request, dbContext, queue, _clock, new Microsoft.AspNetCore.Http.DefaultHttpContext(), ct);

        result.GetType().IsGenericType.Should().BeTrue();
        result.GetType().GetGenericTypeDefinition().Should().Be(typeof(Accepted<>));

        var job = await dbContext.TmfExportJobs.FirstOrDefaultAsync(j => j.StudyId == "STUDY-01", ct);
        job.Should().NotBeNull();
        job!.Status.Should().Be("QUEUED");

        var dequeuedJobId = await queue.DequeueAsync(ct);
        dequeuedJobId.Should().Be(job.Id);
    }

    [Fact]
    public async Task EtmfExportWorker_ProcessesEnqueuedJobToCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var doc = new TmfDocument("DOC-301")
        {
            StudyId = "STUDY-02",
            SiteId = "SITE-1",
            Zone = 2,
            Section = "02.01",
            ArtifactCode = "02.01.01",
            ArtifactType = "IB",
            Filename = "IB_v1.pdf",
            MimeType = "application/pdf",
            ProtocolVersionIndex = 1,
            Status = "APPROVED",
            Content = "IB Content"
        };
        dbContext.TmfDocuments.Add(doc);

        var job = new TmfExportJob("JOB-100")
        {
            StudyId = "STUDY-02",
            RequestedBy = "test.user",
            Status = "QUEUED",
            TotalDocuments = 1,
            ProcessedDocuments = 0,
            CreatedAt = _clock.UtcNow.UtcDateTime
        };
        dbContext.TmfExportJobs.Add(job);
        await dbContext.SaveChangesAsync(ct);

        var queue = new EtmfExportQueue();

        var services = new ServiceCollection();
        services.AddSingleton(dbContext);
        services.AddSingleton<IClock>(_clock);
        var provider = services.BuildServiceProvider();

        var worker = new EtmfExportWorker(provider, queue, NullLogger<EtmfExportWorker>.Instance);

        await worker.ProcessJobAsync("JOB-100", ct);

        var updatedJob = await dbContext.TmfExportJobs.FirstAsync(j => j.Id == "JOB-100", ct);
        updatedJob.Status.Should().Be("COMPLETED");
        updatedJob.ProcessedDocuments.Should().Be(1);
        updatedJob.DownloadUrl.Should().Be("/api/v1/etmf/export-jobs/JOB-100/download");
        updatedJob.ExpiresAt.Should().Be(_clock.UtcNow.AddHours(24));
    }
}
