using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public class EtmfExportTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));

    private IEtmfExportService CreateExportService(EtmfDbContext dbContext, EtmfExportQueue? queue = null) =>
        new EtmfExportService(dbContext, new EtmfUnitOfWork(dbContext), queue ?? new EtmfExportQueue(), _clock);

    private IEtmfDocumentService CreateDocumentService(EtmfDbContext dbContext) =>
        new EtmfDocumentService(dbContext, new EtmfUnitOfWork(dbContext), _clock);

    [Fact]
    public void CreateZipArchive_CreatesValidZipWithManifestsAndTaxonomyStructure()
    {
        var doc1 = TmfDocument.CreateApproved(
            id: "DOC-101",
            studyId: "STUDY-99",
            filename: "Protocol_v1.pdf",
            createdBy: "test.author",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Uploading approved trial protocol",
            siteId: "SITE-1",
            zone: 1,
            section: "01.01",
            artifactCode: "01.01.01",
            artifactType: "Protocol",
            content: "Sample protocol content",
            mimeType: "application/pdf",
            protocolVersionIndex: 1
        );

        var doc2 = TmfDocument.CreateApproved(
            id: "DOC-102",
            studyId: "STUDY-99",
            filename: "FDA_1572.pdf",
            createdBy: "test.author",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Uploading approved 1572 form",
            siteId: "SITE-1",
            zone: 5,
            section: "05.02",
            artifactCode: "05.02.01",
            artifactType: "1572",
            content: "Sample 1572 content",
            mimeType: "application/pdf",
            protocolVersionIndex: 1
        );

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

        var doc = TmfDocument.CreateApproved(
            id: "DOC-201",
            studyId: "STUDY-01",
            filename: "Protocol.pdf",
            createdBy: "test.author",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Uploading approved trial protocol",
            siteId: "SITE-1",
            zone: 1,
            section: "01.01",
            artifactCode: "01.01.01",
            artifactType: "Protocol",
            content: "Protocol Content",
            mimeType: "application/pdf",
            protocolVersionIndex: 1
        );
        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-01", ["DOC-201"], IsFullBinder: false);

        var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-01"), ct);

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

        var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-01"), ct);

        result.GetType().IsGenericType.Should().BeTrue();
        result.GetType().GetGenericTypeDefinition().Should().Be(typeof(Accepted<>));

        var job = await dbContext.TmfExportJobs.FirstOrDefaultAsync(j => j.StudyId == "STUDY-01", ct);
        job.Should().NotBeNull();
        job!.Status.Should().Be("QUEUED");

        var dequeuedJobId = await queue.DequeueAsync(ct);
        dequeuedJobId.Should().Be(job.Id);
    }

    [Fact]
    public async Task ExportDocuments_EmptyBinderSynchronous_ReturnsValidEmptyManifestZipWithoutLoadingRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var sqlRecorder = new RecordingSqlCommandInterceptor();
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(sqlRecorder)
            .Options;

        await using (var setupContext = new EtmfDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync(ct);
        }

        sqlRecorder.Clear();
        await using var dbContext = new EtmfDbContext(options);
        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-EMPTY", Zone: 1, IsFullBinder: false);

        var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-EMPTY"), ct);

        var fileResult = result.Should().BeOfType<FileContentHttpResult>().Subject;
        fileResult.ContentType.Should().Be("application/zip");

        using var memoryStream = new MemoryStream(fileResult.FileContents.ToArray());
        await using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo("manifest.json", "manifest.csv");

        await using var jsonStream = await archive.GetEntry("manifest.json")!.OpenAsync(ct);
        using var jsonReader = new StreamReader(jsonStream);
        using var manifestDoc = JsonDocument.Parse(await jsonReader.ReadToEndAsync(ct));
        manifestDoc.RootElement.GetProperty("total_documents").GetInt32().Should().Be(0);
        manifestDoc.RootElement.GetProperty("documents").GetArrayLength().Should().Be(0);

        var documentQueries = sqlRecorder.Commands
            .Where(sql => sql.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase))
            .ToList();
        documentQueries.Should().ContainSingle("empty synchronous exports should not issue a second SELECT after CountAsync returns 0");
        documentQueries[0].Should().ContainEquivalentOf("COUNT");
        documentQueries[0].Should().NotContainEquivalentOf("Content");
        queue.TryDequeue(out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 12)]
    [InlineData(false, 55)]
    public async Task ExportDocuments_AsynchronousOnSqlite_CountsRowsAndSkipsSelectingDocumentContent(
        bool isFullBinder,
        int matchingSiteOneDocs)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var sqlRecorder = new RecordingSqlCommandInterceptor();
        var auditInterceptor = new GxPAuditInterceptor(() => "cra-1", _clock);
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(sqlRecorder, auditInterceptor)
            .Options;

        await using (var seedContext = new EtmfDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync(ct);

            for (int i = 1; i <= matchingSiteOneDocs; i++)
            {
                seedContext.TmfDocuments.Add(TmfDocument.CreateApproved(
                    id: $"DOC-S1-{i:D3}",
                    studyId: "STUDY-SQLITE",
                    filename: $"Artifact_{i:D3}.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded approved eTMF document",
                    siteId: i % 2 == 0 ? " site-1 " : null,
                    zone: 1,
                    section: "01.01",
                    artifactCode: "01.01.01",
                    artifactType: "Protocol",
                    content: new string('X', 4096),
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1));
            }

            // Documents from an unassigned site and another study must be excluded by the SQL count predicate.
            seedContext.TmfDocuments.Add(TmfDocument.CreateApproved(
                id: "DOC-UNASSIGNED-SITE",
                studyId: "STUDY-SQLITE",
                filename: "OtherSite.pdf",
                createdBy: "seed.author",
                createdAt: _clock.UtcNow.UtcDateTime,
                reasonForChange: "Seeded unassigned site document",
                siteId: "SITE-999",
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Protocol",
                content: "Excluded site payload",
                mimeType: "application/pdf",
                protocolVersionIndex: 1));

            seedContext.TmfDocuments.Add(TmfDocument.CreateApproved(
                id: "DOC-OTHER-STUDY",
                studyId: "STUDY-OTHER",
                filename: "OtherStudy.pdf",
                createdBy: "seed.author",
                createdAt: _clock.UtcNow.UtcDateTime,
                reasonForChange: "Seeded other study document",
                siteId: "SITE-1",
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Protocol",
                content: "Excluded study payload",
                mimeType: "application/pdf",
                protocolVersionIndex: 1));

            await seedContext.SaveChangesAsync(ct);
        }

        sqlRecorder.Clear();
        await using var dbContext = new EtmfDbContext(options);
        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-SQLITE", Zone: 1, IsFullBinder: isFullBinder);

        var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-SQLITE"), ct);

        result.GetType().IsGenericType.Should().BeTrue();
        result.GetType().GetGenericTypeDefinition().Should().Be(typeof(Accepted<>));

        var documentQueries = sqlRecorder.Commands
            .Where(sql => sql.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase))
            .ToList();
        documentQueries.Should().ContainSingle("202 Accepted should only run SELECT COUNT(*) against tmf_documents");
        documentQueries[0].Should().ContainEquivalentOf("COUNT");
        documentQueries[0].Should().NotContainEquivalentOf("Content");
        documentQueries[0].Should().NotContainEquivalentOf("MetadataJson");
        documentQueries[0].Should().NotContainEquivalentOf("SignatureManifestation");
        documentQueries[0].Should().NotContainEquivalentOf("RedactionManifestJson");

        var job = await dbContext.TmfExportJobs.AsNoTracking().SingleAsync(j => j.StudyId == "STUDY-SQLITE", ct);
        job.Status.Should().Be("QUEUED");
        job.TotalDocuments.Should().Be(matchingSiteOneDocs);

        var auditEntry = await dbContext.AuditLogEntries.AsNoTracking()
            .SingleAsync(a => a.EntityName == nameof(TmfExportJob) && a.EntityId == job.Id, ct);
        auditEntry.UserId.Should().Be("cra-1");

        var dequeuedJobId = await queue.DequeueAsync(ct);
        dequeuedJobId.Should().Be(job.Id);
    }

    [Fact]
    public async Task ExportDocuments_SmallExportOnSqlite_OrdersRowsAndScopesBySite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var seedContext = new EtmfDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync(ct);
            seedContext.TmfDocuments.AddRange(
                TmfDocument.CreateApproved(
                    id: "DOC-Z5",
                    studyId: "STUDY-SYNC",
                    filename: "B_1572.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded zone 5 document",
                    siteId: " site-1 ",
                    zone: 5,
                    section: "05.02",
                    artifactCode: "05.02.01",
                    artifactType: "1572",
                    content: "1572 payload",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1),
                TmfDocument.CreateApproved(
                    id: "DOC-Z1-B",
                    studyId: "STUDY-SYNC",
                    filename: "B_ProtocolAmendment.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded protocol amendment",
                    siteId: null,
                    zone: 1,
                    section: "01.01",
                    artifactCode: "01.01.02",
                    artifactType: "Protocol Amendment",
                    content: "Amendment payload",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1),
                TmfDocument.CreateApproved(
                    id: "DOC-Z1-A",
                    studyId: "STUDY-SYNC",
                    filename: "A_Protocol.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded protocol",
                    siteId: "   ",
                    zone: 1,
                    section: "01.01",
                    artifactCode: "01.01.01",
                    artifactType: "Protocol",
                    content: "Protocol payload",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1),
                TmfDocument.CreateApproved(
                    id: "DOC-Z4-2",
                    studyId: "STUDY-SYNC",
                    filename: "IRB_Approval.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded colliding filename second id",
                    siteId: "SITE-1",
                    zone: 4,
                    section: "04.02",
                    artifactCode: "04.02.01",
                    artifactType: "IRB/IEC Approval Notification",
                    content: "IRB payload 2",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1),
                TmfDocument.CreateApproved(
                    id: "DOC-Z4-1",
                    studyId: "STUDY-SYNC",
                    filename: "IRB_Approval.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded colliding filename first id",
                    siteId: "SITE-1",
                    zone: 4,
                    section: "04.02",
                    artifactCode: "04.02.01",
                    artifactType: "IRB/IEC Approval Notification",
                    content: "IRB payload 1",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1),
                TmfDocument.CreateApproved(
                    id: "DOC-EXCLUDED-SITE",
                    studyId: "STUDY-SYNC",
                    filename: "OtherSite_1572.pdf",
                    createdBy: "seed.author",
                    createdAt: _clock.UtcNow.UtcDateTime,
                    reasonForChange: "Seeded other site document",
                    siteId: "SITE-2",
                    zone: 5,
                    section: "05.02",
                    artifactCode: "05.02.01",
                    artifactType: "1572",
                    content: "Excluded payload",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1));
            await seedContext.SaveChangesAsync(ct);
        }

        await using var dbContext = new EtmfDbContext(options);
        var queue = new EtmfExportQueue();
        var request = new ExportDocumentsRequest("STUDY-SYNC", IsFullBinder: false);

        var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-SYNC"), ct);

        var fileResult = result.Should().BeOfType<FileContentHttpResult>().Subject;
        using var memoryStream = new MemoryStream(fileResult.FileContents.ToArray());
        await using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        await using var jsonStream = await archive.GetEntry("manifest.json")!.OpenAsync(ct);
        using var jsonReader = new StreamReader(jsonStream);
        using var manifestDoc = JsonDocument.Parse(await jsonReader.ReadToEndAsync(ct));
        var docIds = manifestDoc.RootElement
            .GetProperty("documents")
            .EnumerateArray()
            .Select(item => item.GetProperty("document_id").GetString())
            .ToList();

        docIds.Should().Equal("DOC-Z1-A", "DOC-Z1-B", "DOC-Z4-1", "DOC-Z4-2", "DOC-Z5");
    }

    [Fact]
    public async Task ExportDocuments_ConcurrentInsertPastThresholdAfterCount_FallsThroughToAsyncJob()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbPath = Path.Combine(Path.GetTempPath(), $"etmf_export_race_{Guid.NewGuid():N}.db");
        try
        {
            var seedOptions = new DbContextOptionsBuilder<EtmfDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var seedContext = new EtmfDbContext(seedOptions))
            {
                await seedContext.Database.EnsureCreatedAsync(ct);
                for (int i = 1; i <= 50; i++)
                {
                    seedContext.TmfDocuments.Add(TmfDocument.CreateApproved(
                        id: $"DOC-RACE-{i:D3}",
                        studyId: "STUDY-RACE",
                        filename: $"Race_{i:D3}.pdf",
                        createdBy: "seed.author",
                        createdAt: _clock.UtcNow.UtcDateTime,
                        reasonForChange: "Seeded boundary document",
                        siteId: "SITE-1",
                        zone: 1,
                        section: "01.01",
                        artifactCode: "01.01.01",
                        artifactType: "Protocol",
                        content: "Payload",
                        mimeType: "application/pdf",
                        protocolVersionIndex: 1));
                }

                await seedContext.SaveChangesAsync(ct);
            }

            var raceInterceptor = new ConcurrentInsertAfterCountInterceptor(dbPath, _clock.UtcNow.UtcDateTime);
            var testOptions = new DbContextOptionsBuilder<EtmfDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .AddInterceptors(raceInterceptor)
                .Options;

            await using var dbContext = new EtmfDbContext(testOptions);
            var queue = new EtmfExportQueue();
            var request = new ExportDocumentsRequest("STUDY-RACE", Zone: 1, IsFullBinder: false);

            var result = await EtmfExportEndpoints.ExportDocuments(request, CreateExportService(dbContext, queue), Caller("STUDY-RACE"), ct);

            result.GetType().IsGenericType.Should().BeTrue();
            result.GetType().GetGenericTypeDefinition().Should().Be(typeof(Accepted<>));

            var job = await dbContext.TmfExportJobs.AsNoTracking().SingleAsync(j => j.StudyId == "STUDY-RACE", ct);
            job.TotalDocuments.Should().Be(51);
            (await queue.DequeueAsync(ct)).Should().Be(job.Id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task EtmfExportWorker_ProcessesEnqueuedJobToCompletion()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var doc = TmfDocument.CreateApproved(
            id: "DOC-301",
            studyId: "STUDY-02",
            filename: "IB_v1.pdf",
            createdBy: "test.author",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Uploading approved IB document",
            siteId: "SITE-1",
            zone: 2,
            section: "02.01",
            artifactCode: "02.01.01",
            artifactType: "IB",
            content: "IB Content",
            mimeType: "application/pdf",
            protocolVersionIndex: 1
        );
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

    [Fact]
    public async Task EtmfExportWorker_AwaitsChannelWakeupWithTenant_WithoutPollingActiveTenantsWhileIdle()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = $"etmf-channel-wakeup-{Guid.NewGuid():N}";
        Guid alphaTenant = Guid.NewGuid();
        var mappings = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["tenant-alpha"] = alphaTenant,
        };

        int listActiveCalls = 0;
        var resolver = new StubTenantCodeResolver(mappings, () => Interlocked.Increment(ref listActiveCalls));

        var services = new ServiceCollection();
        services.AddSingleton<Cadence.Database.Tenancy.ITenantCodeResolver>(resolver);
        services.AddScoped<Cadence.Database.Tenancy.CurrentTenantAccessor>(sp =>
            new Cadence.Database.Tenancy.CurrentTenantAccessor(
                sp.GetRequiredService<Cadence.Database.Tenancy.ITenantCodeResolver>()));
        services.AddScoped<Cadence.Database.Tenancy.ICurrentTenantAccessor>(sp =>
            sp.GetRequiredService<Cadence.Database.Tenancy.CurrentTenantAccessor>());
        services.AddScoped<Cadence.Database.Tenancy.ITenantScope>(sp =>
            sp.GetRequiredService<Cadence.Database.Tenancy.CurrentTenantAccessor>());
        services.AddScoped<Cadence.Database.Audit.IAuditActorScope, Cadence.Database.Audit.AmbientAuditActorScope>();
        services.AddScoped(sp =>
        {
            var accessor = sp.GetRequiredService<Cadence.Database.Tenancy.ICurrentTenantAccessor>();
            var optionsBuilder = new DbContextOptionsBuilder<EtmfDbContext>();
            optionsBuilder.UseInMemoryDatabase(dbName);
            optionsBuilder.UseCadenceStandaloneTenancy(accessor, isRelational: false);
            optionsBuilder.AddInterceptors(Cadence.Database.Tenancy.StandaloneTenantSaveChangesInterceptor.Instance);
            return new EtmfDbContext(optionsBuilder.Options);
        });
        services.AddSingleton<IClock>(_clock);

        await using var provider = services.BuildServiceProvider();
        var queue = new EtmfExportQueue();
        var worker = new EtmfExportWorker(provider, queue, NullLogger<EtmfExportWorker>.Instance);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await worker.StartAsync(cts.Token);

        // Startup drain runs once on non-relational fallback; reset counter to observe steady-state idle + channel wakeup.
        await Task.Delay(50, ct);
        Interlocked.Exchange(ref listActiveCalls, 0);

        // Seed a queued job for tenant-alpha and enqueue it on EtmfExportQueue.
        using (var seedScope = provider.CreateScope())
        {
            using var _ = seedScope.ServiceProvider.GetRequiredService<Cadence.Database.Tenancy.ITenantScope>().Bind(alphaTenant);
            var seedDb = seedScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            seedDb.TmfDocuments.Add(TmfDocument.CreateApproved(
                id: "DOC-WAKE-1",
                studyId: "STUDY-WAKE",
                filename: "Protocol.pdf",
                createdBy: "cra.user",
                createdAt: _clock.UtcNow.UtcDateTime,
                reasonForChange: "Approved protocol",
                siteId: "SITE-1",
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Protocol",
                content: "Protocol Content",
                mimeType: "application/pdf",
                protocolVersionIndex: 1));
            seedDb.TmfExportJobs.Add(new TmfExportJob("JOB-WAKE-1")
            {
                StudyId = "STUDY-WAKE",
                RequestedBy = "cra.user",
                Status = "QUEUED",
                TotalDocuments = 1,
                ProcessedDocuments = 0,
                CreatedAt = _clock.UtcNow.UtcDateTime,
            });
            await seedDb.SaveChangesAsync(ct);
        }

        await queue.EnqueueAsync("JOB-WAKE-1", "tenant-alpha", ct);

        string? observedStatus = null;
        for (int attempt = 0; attempt < 40 && observedStatus != "COMPLETED"; attempt++)
        {
            await Task.Delay(25, ct);
            using var verifyScope = provider.CreateScope();
            using var _ = verifyScope.ServiceProvider.GetRequiredService<Cadence.Database.Tenancy.ITenantScope>().Bind(alphaTenant);
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            observedStatus = (await verifyDb.TmfExportJobs.AsNoTracking().FirstAsync(j => j.Id == "JOB-WAKE-1", ct)).Status;
        }

        await cts.CancelAsync();
        await worker.StopAsync(ct);

        observedStatus.Should().Be("COMPLETED");
        Volatile.Read(ref listActiveCalls).Should().Be(0, "channel wakeups with tenant code must not sweep ListActiveTenantIdsAsync");
    }

    private sealed class StubTenantCodeResolver(
        IReadOnlyDictionary<string, Guid> mappings,
        Action onListActive) : Cadence.Database.Tenancy.ITenantCodeResolver
    {
        public Guid? ResolveTenantId(string tenantCode) =>
            mappings.TryGetValue(tenantCode, out Guid id) ? id : null;

        public ValueTask<Guid?> ResolveTenantIdAsync(string tenantCode, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResolveTenantId(tenantCode));

        public Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken cancellationToken = default)
        {
            onListActive();
            return Task.FromResult<IReadOnlyList<Guid>>(mappings.Values.ToList());
        }
    }

    /// <summary>A request signed by a CRA assigned to <paramref name="studies"/>.</summary>
    private static DefaultHttpContext Caller(params string[] studies)
    {
        var context = new DefaultHttpContext();
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "cra-1",
            Roles = ["cra"],
            AssignedStudies = studies,
            AssignedSites = ["SITE-1"],
        };
        return context;
    }

    /// <summary>An export of a study the caller is not assigned to is refused, and queues nothing (#5361).</summary>
    [Fact]
    public async Task ExportDocuments_ForAStudyOutsideTheCallersStudies_IsRefusedAndQueuesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);
        var queue = new EtmfExportQueue();

        var result = await EtmfExportEndpoints.ExportDocuments(
            new ExportDocumentsRequest("STUDY-01", null, IsFullBinder: true), CreateExportService(dbContext, queue), Caller("STUDY-99"), ct);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await dbContext.TmfExportJobs.CountAsync(ct)).Should().Be(0);
    }

    /// <summary>A caller sees only documents and export jobs in the studies it is assigned to (#5361).</summary>
    [Fact]
    public async Task DocumentAndExportJobLists_AreNarrowedToTheCallersStudies()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);
        dbContext.TmfDocuments.Add(TmfDocument.CreateApproved("DOC-A", "STUDY-A", "a.pdf", "u", _clock.UtcNow.UtcDateTime, "Approved document for study A", mimeType: "application/pdf"));
        dbContext.TmfDocuments.Add(TmfDocument.CreateApproved("DOC-B", "STUDY-B", "b.pdf", "u", _clock.UtcNow.UtcDateTime, "Approved document for study B", mimeType: "application/pdf"));
        dbContext.TmfExportJobs.Add(new TmfExportJob("JOB-A") { StudyId = "STUDY-A", RequestedBy = "u", Status = "COMPLETED", CreatedAt = _clock.UtcNow.UtcDateTime });
        dbContext.TmfExportJobs.Add(new TmfExportJob("JOB-B") { StudyId = "STUDY-B", RequestedBy = "u", Status = "COMPLETED", CreatedAt = _clock.UtcNow.UtcDateTime });
        await dbContext.SaveChangesAsync(ct);

        var docService = CreateDocumentService(dbContext);
        var exportService = CreateExportService(dbContext);
        var documents = await EtmfDocumentEndpoints.GetDocuments(null, null, docService, Caller("STUDY-A"), ct);
        var other = await EtmfDocumentEndpoints.GetDocumentById("DOC-B", docService, Caller("STUDY-A"), ct);
        var download = await EtmfExportEndpoints.DownloadExportJob("JOB-B", exportService, Caller("STUDY-A"), ct);
        var unassigned = await EtmfDocumentEndpoints.GetDocuments(null, null, docService, Caller(), ct);

        documents.Should().BeOfType<Ok<List<TmfDocumentSummaryDto>>>().Which.Value!.Select(d => d.Id).Should().Equal("DOC-A");
        other.GetType().Name.Should().StartWith("NotFound");
        download.GetType().Name.Should().StartWith("NotFound");
        unassigned.Should().BeOfType<Ok<List<TmfDocumentSummaryDto>>>().Which.Value.Should().BeEmpty();
    }

    [Fact]
    public void CreateZipArchive_SanitizesLegacyTraversalAndSlashRows_ConfinesEntriesAndDisambiguatesCollisions()
    {
        var legacyDocs = new List<TmfDocument>
        {
            new("DOC-TRAV-1")
            {
                StudyId = "STUDY-01",
                Zone = 1,
                Section = "01.01",
                ArtifactCode = "01.01.01",
                ArtifactType = "Protocol",
                Filename = "../../../canary-outside-binder.txt",
                Content = "harmless-canary-marker",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            },
            new("DOC-COLLIDE-1")
            {
                StudyId = "STUDY-01",
                Zone = 1,
                Section = "01.01",
                ArtifactCode = "01.01.01",
                ArtifactType = "Protocol",
                Filename = "..\\..\\manifest.json",
                Content = "{\"canary_manifest_collision\":1}",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            },
            new("DOC-COLLIDE-2")
            {
                StudyId = "STUDY-01",
                Zone = 1,
                Section = "01.01",
                ArtifactCode = "01.01.01",
                ArtifactType = "Protocol",
                Filename = "../../manifest.json",
                Content = "{\"canary_manifest_collision\":2}",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            },
            new("DOC-IRB-1")
            {
                StudyId = "STUDY-01",
                Zone = 4,
                Section = "04.02",
                ArtifactCode = "04.02.01",
                ArtifactType = "IRB/IEC Approval Notification",
                Filename = "IRB/IEC Approval.pdf",
                Content = "IRB approval letter 1",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            },
            new("DOC-IRB-2")
            {
                StudyId = "STUDY-01",
                Zone = 4,
                Section = "04.02",
                ArtifactCode = "04.02.01",
                ArtifactType = "IRB/IEC Approval Notification",
                Filename = "IRB\\IEC Approval.pdf",
                Content = "IRB approval letter 2",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            },
            new("DOC-SEC-1")
            {
                StudyId = "STUDY-01",
                Zone = 2,
                Section = "../../02.01",
                ArtifactCode = "02.01.01",
                ArtifactType = "Investigator's Brochure",
                Filename = "..",
                Content = "Legacy section traversal row",
                CreatedAt = _clock.UtcNow.UtcDateTime,
                CreatedBy = "legacy.import",
                ReasonForChange = "Legacy migrated row"
            }
        };

        var firstZip = EtmfExportPackager.CreateZipArchive("EXP-SEC-1", "STUDY-01", legacyDocs, _clock.UtcNow);
        var secondZip = EtmfExportPackager.CreateZipArchive("EXP-SEC-1", "STUDY-01", legacyDocs, _clock.UtcNow);

        using var ms1 = new MemoryStream(firstZip);
        using var archive1 = new ZipArchive(ms1, ZipArchiveMode.Read);
        using var ms2 = new MemoryStream(secondZip);
        using var archive2 = new ZipArchive(ms2, ZipArchiveMode.Read);

        // Deterministic entry names across repeated exports
        archive1.Entries.Select(e => e.FullName)
            .Should().Equal(archive2.Entries.Select(e => e.FullName));

        // 6 documents + 1 root manifest.json + 1 root manifest.csv = 8 unique entries
        archive1.Entries.Should().HaveCount(8);
        archive1.Entries.Select(e => e.FullName.ToUpperInvariant()).Should().OnlyHaveUniqueItems();

        AssertAllBinderEntriesConfined(archive1, expectedDocumentCount: 6);

        // Colliding sanitized names append doc.Id deterministically
        archive1.Entries.Select(e => e.FullName).Should().Contain(n => n.Contains("DOC-COLLIDE-2", StringComparison.Ordinal));
        archive1.Entries.Select(e => e.FullName).Should().Contain("Zone_04/Section_04.02/IRB_IEC Approval.pdf");
        archive1.Entries.Select(e => e.FullName).Should().Contain("Zone_04/Section_04.02/IRB_IEC Approval_DOC-IRB-2.pdf");
    }

    [Fact]
    public async Task HttpIntakeRejectsTraversalPayloadsWith400_AndExportConfinesPreExistingRowsUnderZoneSection()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbName = $"etmf-zip-traversal-{Guid.NewGuid():N}";
        using var factory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage();

            host.ConfigureTestServices(services =>
            {
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);

                services.RemoveAll<DbContextOptions<EtmfDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<EtmfDbContext>>();
                services.AddDbContext<EtmfDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                    options.UseInternalServiceProvider(TestSupport.TestEfServiceProviders.InMemory);
                });

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);
                services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            });
        });

        var client = factory.CreateSignedClient(new GatewayIdentity(
            UserId: "cra-exporter",
            Roles: ["cra"],
            SiteIds: ["SITE-1"],
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: "Staging QC document for binder manifest check",
            StudyIds: ["STUDY-01"]));

        // Seed pre-existing legacy TmfDocument rows directly in persistence
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            db.TmfDocuments.AddRange(
                new TmfDocument("DOC-PRE-1")
                {
                    StudyId = "STUDY-01",
                    Zone = 1,
                    Section = "01.01",
                    ArtifactCode = "01.01.01",
                    ArtifactType = "Protocol",
                    Filename = "../../../canary-outside-binder.txt",
                    Content = "harmless-canary-marker",
                    CreatedAt = _clock.UtcNow.UtcDateTime,
                    CreatedBy = "legacy.migrator",
                    ReasonForChange = "Legacy TMF import"
                },
                new TmfDocument("DOC-PRE-2")
                {
                    StudyId = "STUDY-01",
                    Zone = 1,
                    Section = "01.01",
                    ArtifactCode = "01.01.01",
                    ArtifactType = "Protocol",
                    Filename = "..\\..\\manifest.json",
                    Content = "{\"canary_manifest_collision\":true}",
                    CreatedAt = _clock.UtcNow.UtcDateTime,
                    CreatedBy = "legacy.migrator",
                    ReasonForChange = "Legacy TMF import"
                },
                new TmfDocument("DOC-PRE-3")
                {
                    StudyId = "STUDY-01",
                    Zone = 1,
                    Section = "01.01",
                    ArtifactCode = "01.01.01",
                    ArtifactType = "Protocol",
                    Filename = "../../manifest.json",
                    Content = "{\"canary_manifest_collision\":true}",
                    CreatedAt = _clock.UtcNow.UtcDateTime,
                    CreatedBy = "legacy.migrator",
                    ReasonForChange = "Legacy TMF import"
                });
            await db.SaveChangesAsync(ct);
        }

        // 1. POST /api/v1/etmf/ingest with traversal filename -> 400 Bad Request
        using var ingestContent = new StringContent(
            "{\"studyId\":\"STUDY-01\",\"filename\":\"../../../canary-outside-binder.txt\",\"section\":\"01.01\",\"zone\":1,\"content\":\"harmless-canary-marker\"}",
            Encoding.UTF8,
            "application/json");
        var ingestResponse = await client.PostAsync("/api/v1/etmf/ingest", ingestContent, ct);
        ingestResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. POST /api/v1/etmf/intelligence/stage-qc with manifest collision traversal -> 400 Bad Request
        using var stageQcContent = new StringContent(
            "{\"studyId\":\"STUDY-01\",\"filename\":\"../../manifest.json\",\"content\":\"{\\\"canary_manifest_collision\\\":true}\",\"reasonForChange\":\"Staging QC document for binder manifest check\"}",
            Encoding.UTF8,
            "application/json");
        var stageQcResponse = await client.PostAsync("/api/v1/etmf/intelligence/stage-qc", stageQcContent, ct);
        stageQcResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. POST /api/v1/etmf/documents/export -> 200 OK with strictly confined ZIP entries
        using var exportContent = new StringContent(
            "{\"study_id\":\"STUDY-01\",\"is_full_binder\":false}",
            Encoding.UTF8,
            "application/json");
        var exportResponse = await client.PostAsync("/api/v1/etmf/documents/export", exportContent, ct);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var zipBytes = await exportResponse.Content.ReadAsByteArrayAsync(ct);
        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        AssertAllBinderEntriesConfined(archive, expectedDocumentCount: 3);
    }

    private static void AssertAllBinderEntriesConfined(ZipArchive archive, int expectedDocumentCount)
    {
        var manifestJsonEntries = archive.Entries
            .Where(e => string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var manifestCsvEntries = archive.Entries
            .Where(e => string.Equals(e.FullName, "manifest.csv", StringComparison.OrdinalIgnoreCase))
            .ToList();

        manifestJsonEntries.Should().ContainSingle("binder must contain exactly one root manifest.json");
        manifestCsvEntries.Should().ContainSingle("binder must contain exactly one root manifest.csv");

        using (var manifestReader = new StreamReader(manifestJsonEntries[0].Open(), Encoding.UTF8))
        {
            var manifestText = manifestReader.ReadToEnd();
            manifestText.Should().NotContain("canary_manifest_collision");
            var parsed = JsonSerializer.Deserialize<EtmfExportPackager.ExportManifestRoot>(manifestText);
            parsed.Should().NotBeNull();
            parsed!.total_documents.Should().Be(expectedDocumentCount);
        }

        string binderRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "binder_confinement_assert_root"));
        string binderRootWithSep = binderRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var docEntries = archive.Entries
            .Where(e => !string.Equals(e.FullName, "manifest.json", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(e.FullName, "manifest.csv", StringComparison.OrdinalIgnoreCase))
            .ToList();

        docEntries.Should().HaveCount(expectedDocumentCount);

        var zoneSectionPattern = new Regex(@"^Zone_\d{2,}/Section_[^/\\]+/[^/\\]+$", RegexOptions.CultureInvariant);
        foreach (var entry in docEntries)
        {
            entry.FullName.Should().MatchRegex(zoneSectionPattern);
            entry.FullName.Should().NotContain("..");

            var segments = entry.FullName.Split('/');
            segments.Should().HaveCount(3);
            string sectionDir = Path.GetFullPath(Path.Combine(binderRoot, segments[0], segments[1]));
            sectionDir.StartsWith(binderRootWithSep, StringComparison.Ordinal).Should().BeTrue();

            string sectionDirWithSep = sectionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string resolvedEntryPath = Path.GetFullPath(Path.Combine(binderRoot, entry.FullName));
            resolvedEntryPath.StartsWith(sectionDirWithSep, StringComparison.Ordinal).Should().BeTrue(
                $"entry '{entry.FullName}' must resolve strictly under '{sectionDirWithSep}'");
            Path.GetDirectoryName(resolvedEntryPath).Should().Be(sectionDir);
        }
    }

    private sealed class RecordingSqlCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class ConcurrentInsertAfterCountInterceptor(string dbPath, DateTime createdAt) : DbCommandInterceptor
    {
        private int _inserted;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("COUNT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _inserted, 1) == 0)
            {
                var writerOptions = new DbContextOptionsBuilder<EtmfDbContext>()
                    .UseSqlite($"Data Source={dbPath}")
                    .Options;
                await using var writerDb = new EtmfDbContext(writerOptions);
                writerDb.TmfDocuments.Add(TmfDocument.CreateApproved(
                    id: "DOC-RACE-051",
                    studyId: "STUDY-RACE",
                    filename: "Race_051.pdf",
                    createdBy: "concurrent.author",
                    createdAt: createdAt,
                    reasonForChange: "Concurrent upload during export",
                    siteId: "SITE-1",
                    zone: 1,
                    section: "01.01",
                    artifactCode: "01.01.01",
                    artifactType: "Protocol",
                    content: "Concurrent payload",
                    mimeType: "application/pdf",
                    protocolVersionIndex: 1));
                await writerDb.SaveChangesAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
