using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Services;
using Cadence.Apps.Etmf.Workers;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Storage;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public sealed class EtmfStorageAndEndpointsTests : IDisposable
{
    private const string StudyId = "CADENCE-101";
    private const string SiteId = "SITE-101";
    private const string Reason = "21 CFR Part 11 regulated eTMF verification reason";

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingCompletenessPublisher _webhookRecorder = new();
    private readonly WebApplicationFactory<Cadence.Apps.Etmf.Program> _factory;

    public EtmfStorageAndEndpointsTests()
    {
        var dbName = $"etmf-storage-{Guid.NewGuid():N}";
        _factory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage()
                .DisableBackgroundHostedServices(typeof(EtmfExportWorker));

            host.ConfigureTestServices(services =>
            {
                var inMemoryServices = new ServiceCollection()
                    .AddEntityFrameworkInMemoryDatabase()
                    .BuildServiceProvider();

                services.RemoveAll<DbContextOptions<EtmfDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<EtmfDbContext>>();
                services.AddDbContext<EtmfDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                    options.UseInternalServiceProvider(inMemoryServices);
                });

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);

                services.RemoveAll<IEtmfCompletenessWebhookPublisher>();
                services.AddSingleton<IEtmfCompletenessWebhookPublisher>(_webhookRecorder);

                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task MultiMegabyteBinaryUpload_PreservesExactBytesAndSha256_SupportsSubmitQcTaxonomyUpdateAndTamperDetection()
    {
        var ct = TestContext.Current.CancellationToken;
        var crcClient = CreateClient(["crc"], [StudyId], [SiteId], "crc.uploader");
        var piClient = CreateClient(["investigator"], [StudyId], [SiteId], "pi.viewer");
        var craClient = CreateClient(["cra"], [StudyId], [SiteId], "cra.reviewer");

        // Build a 2.4 MB binary PDF payload containing NUL (0x00) and high (0xFF) bytes.
        var pdfBytes = BuildMultiMegabytePdf(2_400_000);
        var expectedSha256 = Convert.ToHexString(SHA256.HashData(pdfBytes)).ToLowerInvariant();

        var ingestResp = await crcClient.PostAsJsonAsync(
            "/api/v1/etmf/ingest",
            new IngestDocumentRequest(
                Filename: "Protocol_v2_FullBinary.pdf",
                Content: "Document content index for: Protocol_v2_FullBinary.pdf",
                StudyId: StudyId,
                SiteId: SiteId,
                MimeType: "application/pdf",
                Zone: 1,
                Section: "01.01",
                ArtifactCode: "01.01.01",
                ArtifactType: "Clinical Trial Protocol",
                TaxonomyVersion: "v3.2.0-complete",
                ReasonForChange: Reason,
                ContentBase64: Convert.ToBase64String(pdfBytes)),
            ct);
        ingestResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdDoc = await ingestResp.Content.ReadFromJsonAsync<DocView>(ct);
        createdDoc.Should().NotBeNull();
        createdDoc!.ContentChecksum.Should().Be(expectedSha256);
        createdDoc.ObjectKey.Should().NotBeNullOrWhiteSpace();
        createdDoc.Status.Should().Be("DRAFT");
        createdDoc.SiteId.Should().Be(SiteId);

        // Stream back stored bytes via GET /api/v1/etmf/documents/{id}/content and verify byte-for-byte fidelity.
        var streamResp = await crcClient.GetAsync($"/api/v1/etmf/documents/{createdDoc.Id}/content", ct);
        streamResp.StatusCode.Should().Be(HttpStatusCode.OK);
        streamResp.Headers.TryGetValues("X-Content-SHA256", out var digestHeader).Should().BeTrue();
        digestHeader!.Single().Should().Be(expectedSha256);

        var streamedBytes = await streamResp.Content.ReadAsByteArrayAsync(ct);
        streamedBytes.Length.Should().Be(pdfBytes.Length);
        streamedBytes.Should().Equal(pdfBytes);

        // Submit DRAFT -> TECHNICAL_QC as CRC and verify completeness webhook fires.
        _webhookRecorder.Calls.Clear();
        var submitQcResp = await crcClient.PostAsJsonAsync(
            $"/api/v1/etmf/documents/{createdDoc.Id}/submit-qc",
            new SubmitDocumentForQcRequest(ReasonForChange: Reason),
            ct);
        submitQcResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submittedDoc = await submitQcResp.Content.ReadFromJsonAsync<DocView>(ct);
        submittedDoc!.Status.Should().Be("TECHNICAL_QC");
        _webhookRecorder.Calls.Should().ContainSingle(c => c.StudyId == StudyId && c.SiteId == SiteId);

        // Verify QC queue filters by study_id and site_id.
        var qcQueueResp = await craClient.GetAsync(
            $"/api/v1/etmf/intelligence/qc-queue?study_id={StudyId}&site_id={SiteId}",
            ct);
        qcQueueResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var qcQueue = await qcQueueResp.Content.ReadFromJsonAsync<List<CraqcStagingItem>>(ct);
        qcQueue.Should().ContainSingle(i => i.DocumentId == createdDoc.Id);

        // Investigator lacks etmf_document:tag -> PATCH taxonomy returns 403.
        var piPatchResp = await piClient.PatchAsJsonAsync(
            $"/api/v1/etmf/documents/{createdDoc.Id}/taxonomy",
            new UpdateDocumentTaxonomyRequest(Zone: 5, Section: "05.02", ArtifactCode: "05.02.01", ReasonForChange: Reason),
            ct);
        piPatchResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CRA updates taxonomy via PATCH -> persists taxonomy, audit log, and fires completeness webhook.
        _webhookRecorder.Calls.Clear();
        var craPatchResp = await craClient.PatchAsJsonAsync(
            $"/api/v1/etmf/documents/{createdDoc.Id}/taxonomy",
            new UpdateDocumentTaxonomyRequest(Zone: 5, Section: "05.02", ArtifactCode: "05.02.01", ReasonForChange: Reason),
            ct);
        craPatchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var patchedDoc = await craPatchResp.Content.ReadFromJsonAsync<DocView>(ct);
        patchedDoc!.Zone.Should().Be(5);
        patchedDoc.Section.Should().Be("05.02");
        patchedDoc.ArtifactCode.Should().Be("05.02.01");
        patchedDoc.ArtifactType.Should().Be("FDA Form 1572");
        _webhookRecorder.Calls.Should().ContainSingle(c => c.StudyId == StudyId && c.SiteId == SiteId);

        // Tamper with stored bytes in IStoragePort<byte[]> -> GET /content detects SHA-256 mismatch with 409 Conflict.
        var storage = _factory.Services.GetRequiredService<IStoragePort<byte[]>>();
        var tamperedBytes = (byte[])pdfBytes.Clone();
        tamperedBytes[1024] ^= 0xFF;
        await storage.PutObjectAsync(createdDoc.ObjectKey!, tamperedBytes, "application/pdf", ct: ct);

        var tamperedResp = await craClient.GetAsync($"/api/v1/etmf/documents/{createdDoc.Id}/content", ct);
        tamperedResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SyncAndAsyncBinderExports_PackageRealStoredBytesWithManifests_AndEnforce24hExpiry()
    {
        var ct = TestContext.Current.CancellationToken;
        var craClient = CreateClient(["CRA"], [StudyId], [SiteId], "cra.exporter");

        // Ingest 16 documents (>15 triggers background async export job processed by EtmfExportWorker).
        var firstDocBytes = BuildMultiMegabytePdf(65_536);
        for (var i = 1; i <= 16; i++)
        {
            var payload = i == 1 ? firstDocBytes : Encoding.UTF8.GetBytes($"%PDF-1.7 Artifact #{i} payload");
            var resp = await craClient.PostAsJsonAsync(
                "/api/v1/etmf/ingest",
                new IngestDocumentRequest(
                    Filename: $"Artifact_{i:D2}.pdf",
                    Content: $"Artifact {i} index",
                    StudyId: StudyId,
                    SiteId: SiteId,
                    MimeType: "application/pdf",
                    Zone: 1,
                    Section: "01.01",
                    ArtifactCode: "01.01.01",
                    ArtifactType: "Clinical Trial Protocol",
                    TaxonomyVersion: "v3.2.0-complete",
                    ReasonForChange: Reason,
                    ContentBase64: Convert.ToBase64String(payload)),
                ct);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 1. Synchronous export of <= 50 documents (1 document ID).
        var listResp = await craClient.GetAsync($"/api/v1/etmf/documents?study_id={StudyId}&site_id={SiteId}", ct);
        var docs = await listResp.Content.ReadFromJsonAsync<List<DocView>>(ct);
        docs.Should().HaveCount(16);
        var firstDoc = docs!.First(d => d.Filename == "Artifact_01.pdf");

        var syncExportResp = await craClient.PostAsJsonAsync(
            "/api/v1/etmf/documents/export",
            new ExportDocumentsRequest(StudyId: StudyId, DocumentIds: [firstDoc.Id]),
            ct);
        syncExportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var syncZipBytes = await syncExportResp.Content.ReadAsByteArrayAsync(ct);
        AssertValidBinderZip(syncZipBytes, "Artifact_01.pdf", firstDocBytes);

        // 2. Asynchronous full-binder export (> 15 documents) -> 202 Accepted -> EtmfExportWorker -> COMPLETED -> download ZIP.
        var asyncExportResp = await craClient.PostAsJsonAsync(
            "/api/v1/etmf/documents/export",
            new ExportDocumentsRequest(StudyId: StudyId, IsFullBinder: true),
            ct);
        asyncExportResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var jobJson = JsonDocument.Parse(await asyncExportResp.Content.ReadAsStringAsync(ct));
        var jobId = jobJson.RootElement.GetProperty("job_id").GetString()!;

        string status = "QUEUED";
        for (var attempt = 0; attempt < 40 && status != "COMPLETED"; attempt++)
        {
            await Task.Delay(50, ct);
            var pollResp = await craClient.GetAsync($"/api/v1/etmf/export-jobs/{jobId}", ct);
            pollResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var pollDoc = JsonDocument.Parse(await pollResp.Content.ReadAsStringAsync(ct));
            status = pollDoc.RootElement.GetProperty("status").GetString()!;
        }
        status.Should().Be("COMPLETED");

        var downloadResp = await craClient.GetAsync($"/api/v1/etmf/export-jobs/{jobId}/download", ct);
        downloadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var asyncZipBytes = await downloadResp.Content.ReadAsByteArrayAsync(ct);
        AssertValidBinderZip(asyncZipBytes, "Artifact_01.pdf", firstDocBytes);

        // Advance clock past 24-hour expiration -> 410 Gone.
        _clock.Advance(TimeSpan.FromHours(25));
        var expiredResp = await craClient.GetAsync($"/api/v1/etmf/export-jobs/{jobId}/download", ct);
        expiredResp.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task UnassignedStudyOrSite_Returns403ProblemDetailsWithForbiddenScopeCode()
    {
        var ct = TestContext.Current.CancellationToken;
        var scopedCra = CreateClient(["CRA"], [StudyId], [SiteId], "cra.scoped");

        var unassignedStudyResp = await scopedCra.GetAsync("/api/v1/etmf/documents?study_id=UNASSIGNED-STUDY-999", ct);
        unassignedStudyResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var studyProblem = JsonDocument.Parse(await unassignedStudyResp.Content.ReadAsStringAsync(ct));
        studyProblem.RootElement.GetProperty("code").GetString().Should().Be("FORBIDDEN_SCOPE");

        var unassignedSiteResp = await scopedCra.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyId}&site_id=SITE-999",
            ct);
        unassignedSiteResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var siteProblem = JsonDocument.Parse(await unassignedSiteResp.Content.ReadAsStringAsync(ct));
        siteProblem.RootElement.GetProperty("code").GetString().Should().Be("FORBIDDEN_SCOPE");

        var unassignedQcResp = await scopedCra.GetAsync(
            "/api/v1/etmf/intelligence/qc-queue?study_id=UNASSIGNED-STUDY-999",
            ct);
        unassignedQcResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private HttpClient CreateClient(string[] roles, string[] studyIds, string[] siteIds, string userId)
    {
        var identity = new GatewayIdentity(
            UserId: userId,
            Roles: roles,
            SiteIds: siteIds,
            TenantId: TestSecurityContext.TenantCode,
            ChangeReason: Reason,
            StudyIds: studyIds);
        return _factory.CreateSignedClient(identity);
    }

    private static byte[] BuildMultiMegabytePdf(int byteLength)
    {
        var buffer = new byte[byteLength];
        var header = Encoding.ASCII.GetBytes("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n1 0 obj\n<< /Type /Catalog >>\nstream\n");
        Buffer.BlockCopy(header, 0, buffer, 0, header.Length);
        for (var i = header.Length; i < byteLength - 16; i++)
        {
            buffer[i] = (byte)(i % 256);
        }
        var trailer = Encoding.ASCII.GetBytes("\nendstream\n%%EOF");
        Buffer.BlockCopy(trailer, 0, buffer, byteLength - trailer.Length, trailer.Length);
        return buffer;
    }

    private static void AssertValidBinderZip(byte[] zipBytes, string expectedFileSuffix, byte[] expectedPayload)
    {
        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.GetEntry("manifest.json").Should().NotBeNull();
        archive.GetEntry("manifest.csv").Should().NotBeNull();

        var docEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(expectedFileSuffix, StringComparison.Ordinal));
        docEntry.Should().NotBeNull();
        using var entryStream = docEntry!.Open();
        using var copy = new MemoryStream();
        entryStream.CopyTo(copy);
        copy.ToArray().Should().Equal(expectedPayload);
    }

    private sealed class RecordingCompletenessPublisher : IEtmfCompletenessWebhookPublisher
    {
        public ConcurrentQueue<(string StudyId, string SiteId, string Reason)> Calls { get; } = new();

        public Task PublishSiteActivationCompletenessAsync(
            EtmfDbContext dbContext,
            string studyId,
            string? siteId,
            string? tenantId,
            string reasonForChange,
            CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(siteId))
            {
                Calls.Enqueue((studyId, siteId, reasonForChange));
            }
            return Task.CompletedTask;
        }
    }
}

file sealed record DocView(
    string Id,
    string Filename,
    string Status,
    string? SiteId,
    int Zone,
    string Section,
    string ArtifactCode,
    string ArtifactType,
    string? ContentChecksum,
    string? ObjectKey);
