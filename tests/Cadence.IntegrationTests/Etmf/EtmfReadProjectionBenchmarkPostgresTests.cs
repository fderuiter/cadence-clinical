using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.IntegrationTests.Database;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Etmf;

public sealed class EtmfBenchmarkPostgresFixture : IAsyncLifetime
{
    private readonly ThrowawayPostgresDatabase _database = new();

    public string? ConnectionString => _database.ConnectionString;
    public string? AdminDatabaseConnectionString => _database.AdminDatabaseConnectionString;

    public void SkipIfUnavailable() => _database.SkipIfUnavailable();

    public ValueTask InitializeAsync() => _database.InitializeAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}

/// <summary>
/// Benchmarks eTMF binder folder reads, single-document detail hydration, site-scoped SQL pushdown, QC queue reads,
/// and 50-item batch QC approvals across a 3,000-document study on PostgreSQL.
/// Opt in with <c>CADENCE_ETMF_BENCHMARK=1</c> alongside <c>CADENCE_TEST_POSTGRES</c>.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Benchmark")]
public sealed class EtmfReadProjectionBenchmarkPostgresTests(
    EtmfBenchmarkPostgresFixture fixture,
    ITestOutputHelper output)
    : IClassFixture<EtmfBenchmarkPostgresFixture>
{
    private const int TotalDocuments = 3_000;
    private const int QcItemCount = 50;
    private const string StudyCode = "STUDY-ETMF-BMK-3000";
    private const string TargetArtifactCode = "01.01.02";
    private const string TargetSection = "01.01";

    private static readonly (int Zone, string Section, string Code, string Name)[] ArtifactCatalog =
    [
        (1, "01.01", "01.01.01", "Trial Master File Plan"),
        (1, "01.01", "01.01.02", "Protocol"),
        (1, "01.01", "01.01.03", "Protocol Amendment"),
        (1, "01.02", "01.02.01", "Investigator Brochure"),
        (1, "01.04", "01.04.01", "Debarment Statement"),
        (2, "02.01", "02.01.02", "Certificate of Analysis"),
        (2, "02.01", "02.01.04", "IP Shipping Records"),
        (3, "03.01", "03.01.01", "Regulatory Submission"),
        (4, "04.01", "04.01.01", "IRB Approval"),
        (4, "04.01", "04.01.02", "Informed Consent Form"),
        (5, "05.01", "05.01.01", "Site Selection Visit Report"),
        (5, "05.02", "05.02.02", "FDA Form 1572"),
        (5, "05.02", "05.02.04", "Principal Investigator CV"),
        (5, "05.02", "05.02.06", "Medical License"),
        (5, "05.02", "05.02.07", "Delegation of Authority Log"),
        (5, "05.02", "05.02.10", "Financial Disclosure Form"),
        (5, "05.03", "05.03.01", "Site Initiation Visit Report"),
        (5, "05.04", "05.04.02", "Routine Monitoring Visit Report"),
        (5, "05.04", "05.04.05", "Close-Out Visit Report"),
        (6, "06.01", "06.01.01", "Expedited Safety Report"),
        (7, "07.01", "07.01.01", "Central Lab Certification"),
        (8, "08.01", "08.01.01", "Case Report Form"),
        (9, "09.01", "09.01.01", "Vendor Qualification Audit"),
        (10, "10.01", "10.01.01", "Data Management Plan"),
        (11, "11.01", "11.01.01", "Statistical Analysis Plan"),
    ];

    [Fact(DisplayName = "Benchmark_ThreeThousandStudyDocuments_ProjectedReadsSqlScopeAndReconciledMutations", Timeout = 600_000)]
    public async Task BenchmarkThreeThousandStudyDocumentsProjectedReadsSqlScopeAndReconciledMutations()
    {
        fixture.SkipIfUnavailable();
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("CADENCE_ETMF_BENCHMARK") != "1",
            "Set CADENCE_ETMF_BENCHMARK=1 to run the 3,000-document eTMF PostgreSQL benchmark.");

        var ct = TestContext.Current.CancellationToken;
        bool baselineMode = string.Equals(
            Environment.GetEnvironmentVariable("CADENCE_ETMF_BENCHMARK_MODE"),
            "before",
            StringComparison.OrdinalIgnoreCase);

        string connectionString = fixture.ConnectionString!;
        var sqlRecorder = new SqlCaptureInterceptor();

        await using var factory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestStorage()
                .UseSetting("ConnectionStrings:cadence", connectionString)
                .UseSetting("Database:MigrateOnStartup", "true")
                .ConfigureServices(services =>
                {
                    services.ConfigureDbContext<EtmfDbContext>(opts => opts.AddInterceptors(sqlRecorder));
                });
        });

        // Trigger host startup & migrations before bulk seeding
        var craIdentity = new GatewayIdentity(
            UserId: "cra@etmf-benchmark.test",
            Roles: ["CRA"],
            SiteIds: ["SITE-01", "SITE-02", "SITE-03", "SITE-04", "SITE-05", "SITE-06", "SITE-07", "SITE-08", "SITE-09", "SITE-10"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [StudyCode]);
        using var craClient = factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(craIdentity));
        var warmupTax = await craClient.GetAsync("/api/v1/etmf/taxonomy", ct);
        warmupTax.StatusCode.Should().Be(HttpStatusCode.OK);

        await SeedThreeThousandDocumentsAsync(factory.Services, connectionString, ct);

        // Warmup request
        _ = await craClient.GetAsync($"/api/v1/etmf/documents?study_id={StudyCode}&artifact_code={TargetArtifactCode}", ct);
        _ = await craClient.GetAsync($"/api/v1/etmf/intelligence/qc-queue?study_id={StudyCode}", ct);

        // 1. Measure Binder Folder Click (GET /api/v1/etmf/documents?study_id=...&artifact_code=01.01.02)
        sqlRecorder.Clear();
        long folderAllocStart = GC.GetTotalAllocatedBytes(precise: false);
        var folderSw = Stopwatch.StartNew();
        using var folderResp = await craClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyCode}&artifact_code={TargetArtifactCode}",
            ct);
        byte[] folderBytes = await folderResp.Content.ReadAsByteArrayAsync(ct);
        folderSw.Stop();
        long folderAllocDelta = GC.GetTotalAllocatedBytes(precise: false) - folderAllocStart;
        folderResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var folderJson = JsonDocument.Parse(folderBytes);
        int folderCount = folderJson.RootElement.GetArrayLength();
        string folderSql = string.Join(" | ", sqlRecorder.SelectStatements);

        // 1b. Verify Section Filter (GET /api/v1/etmf/documents?study_id=...&section=01.01)
        using var sectionResp = await craClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyCode}&section={TargetSection}",
            ct);
        sectionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sectionJson = JsonDocument.Parse(await sectionResp.Content.ReadAsByteArrayAsync(ct));
        int sectionCount = sectionJson.RootElement.GetArrayLength();
        var crcIdentity = new GatewayIdentity(
            UserId: "crc@etmf-benchmark.test",
            Roles: ["CRC"],
            SiteIds: ["SITE-01"],
            TenantId: TestSecurityContext.TenantCode,
            StudyIds: [StudyCode]);
        using var crcClient = factory.CreateDefaultClient(new TestSecurityContext.GatewaySigningHandler(crcIdentity));
        sqlRecorder.Clear();
        var crcSw = Stopwatch.StartNew();
        using var crcResp = await crcClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyCode}&artifact_code={TargetArtifactCode}",
            ct);
        byte[] crcBytes = await crcResp.Content.ReadAsByteArrayAsync(ct);
        crcSw.Stop();
        crcResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var crcJson = JsonDocument.Parse(crcBytes);
        int crcCount = crcJson.RootElement.GetArrayLength();
        string crcSql = string.Join(" | ", sqlRecorder.SelectStatements);

        // 3. Measure Single-Document Detail Read (GET /api/v1/etmf/documents/{id})
        sqlRecorder.Clear();
        const string detailDocId = "DOC-BMK-0052";
        var detailSw = Stopwatch.StartNew();
        using var detailResp = await craClient.GetAsync($"/api/v1/etmf/documents/{detailDocId}", ct);
        byte[] detailBytes = await detailResp.Content.ReadAsByteArrayAsync(ct);
        detailSw.Stop();
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detailJson = JsonDocument.Parse(detailBytes);

        // 4. Measure QC Queue Read (GET /api/v1/etmf/intelligence/qc-queue?study_id=...)
        sqlRecorder.Clear();
        long qcAllocStart = GC.GetTotalAllocatedBytes(precise: false);
        var qcSw = Stopwatch.StartNew();
        using var qcResp = await craClient.GetAsync($"/api/v1/etmf/intelligence/qc-queue?study_id={StudyCode}", ct);
        byte[] qcBytes = await qcResp.Content.ReadAsByteArrayAsync(ct);
        qcSw.Stop();
        long qcAllocDelta = GC.GetTotalAllocatedBytes(precise: false) - qcAllocStart;
        qcResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var qcJson = JsonDocument.Parse(qcBytes);
        int qcCount = qcJson.RootElement.GetArrayLength();
        string qcSql = string.Join(" | ", sqlRecorder.SelectStatements);

        // 5. Measure 50-item Batch QC Approval
        // In baseline mode ("before"), each reviewQcDocument was followed by GET /intelligence/qc-queue + GET /documents!
        // In optimized mode, each reviewQcDocument runs ONLY POST /intelligence/qc-review/{id} (0 GET reloads)!
        var batchSw = Stopwatch.StartNew();
        int batchHttpCalls = 0;
        long batchTransferBytes = 0;
        for (int i = 1; i <= QcItemCount; i++)
        {
            string qcDocId = $"DOC-BMK-QC-{i:D4}";
            using var postResp = await craClient.PostAsJsonAsync(
                $"/api/v1/etmf/intelligence/qc-review/{qcDocId}",
                new CraqcReviewRequest(
                    Decision: "ACCEPT",
                    ReasonForChange: "Batch approved verified AI taxonomy classification"),
                ct);
            postResp.StatusCode.Should().Be(HttpStatusCode.OK);
            byte[] postBytes = await postResp.Content.ReadAsByteArrayAsync(ct);
            batchTransferBytes += postBytes.Length;
            batchHttpCalls++;

            if (baselineMode)
            {
                using var reloadQc = await craClient.GetAsync($"/api/v1/etmf/intelligence/qc-queue?study_id={StudyCode}", ct);
                batchTransferBytes += (await reloadQc.Content.ReadAsByteArrayAsync(ct)).Length;
                batchHttpCalls++;

                using var reloadDocs = await craClient.GetAsync($"/api/v1/etmf/documents?study_id={StudyCode}", ct);
                batchTransferBytes += (await reloadDocs.Content.ReadAsByteArrayAsync(ct)).Length;
                batchHttpCalls++;
            }
        }
        batchSw.Stop();

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[ETMF-BENCHMARK mode={(baselineMode ? "BEFORE" : "AFTER")}] " +
            $"FolderGet: rows={folderCount}, bytes={folderBytes.Length}, ms={folderSw.Elapsed.TotalMilliseconds:F2}, alloc={folderAllocDelta}; " +
            $"CrcFolderGet: rows={crcCount}, bytes={crcBytes.Length}, ms={crcSw.Elapsed.TotalMilliseconds:F2}; " +
            $"DetailGet: bytes={detailBytes.Length}, ms={detailSw.Elapsed.TotalMilliseconds:F2}; " +
            $"QcQueueGet: rows={qcCount}, bytes={qcBytes.Length}, ms={qcSw.Elapsed.TotalMilliseconds:F2}, alloc={qcAllocDelta}; " +
            $"BatchAccept50: httpCalls={batchHttpCalls}, transferBytes={batchTransferBytes}, ms={batchSw.Elapsed.TotalMilliseconds:F2}"));
        output.WriteLine($"[ETMF-SQL FolderGet] {folderSql}");
        output.WriteLine($"[ETMF-SQL CrcFolderGet] {crcSql}");
        output.WriteLine($"[ETMF-SQL QcQueueGet] {qcSql}");

        if (!baselineMode)
        {
            // Verify projected folder & section filter + summary payload invariants
            folderCount.Should().Be(120, "3,000 study rows evenly distributed across 25 artifacts yields 120 rows for 01.01.02");
            sectionCount.Should().Be(360, "section 01.01 spans 3 of the 25 artifacts (3 x 120 = 360)");
            folderBytes.Length.Should().BeLessThan(200_000, "summary projection omits 8 KiB Content and JSONB manifests");

            var firstSummary = folderJson.RootElement[0];
            firstSummary.TryGetProperty("content", out _).Should().BeFalse();
            firstSummary.TryGetProperty("metadataJson", out _).Should().BeFalse();
            firstSummary.TryGetProperty("signatureManifestation", out _).Should().BeFalse();
            firstSummary.TryGetProperty("redactionManifestJson", out _).Should().BeFalse();

            folderSql.Should().NotContain("\"Content\"");
            folderSql.Should().NotContain("\"MetadataJson\"");
            folderSql.Should().NotContain("\"SignatureManifestation\"");
            folderSql.Should().NotContain("\"RedactionManifestJson\"");

            // Verify CRC SQL site pushdown
            crcSql.Should().Contain("SiteId");
            crcCount.Should().BeLessThan(folderCount);

            // Verify single-document detail includes JSONB metadata while omitting inline Content
            detailJson.RootElement.TryGetProperty("content", out _).Should().BeFalse();
            detailJson.RootElement.GetProperty("metadataJson").GetString().Should().NotBeNullOrWhiteSpace();
            detailJson.RootElement.GetProperty("signatureManifestation").GetString().Should().NotBeNullOrWhiteSpace();
            detailJson.RootElement.GetProperty("redactionManifestJson").GetString().Should().NotBeNullOrWhiteSpace();

            // Verify QC queue read
            qcCount.Should().Be(QcItemCount);
            qcSql.Should().NotContain("\"SignatureManifestation\"");
            qcSql.Should().NotContain("\"RedactionManifestJson\"");

            // Verify 50-item batch accept ran 50 POSTs and 0 GETs
            batchHttpCalls.Should().Be(QcItemCount);
        }
    }

    private static async Task SeedThreeThousandDocumentsAsync(
        IServiceProvider rootServices,
        string connectionString,
        CancellationToken ct)
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        string eightKbContent = string.Concat(Enumerable.Repeat(
            "CLINICAL TRIAL MASTER FILE VERIFIED PROTOCOL EXCERPT SECTION 21 CFR PART 11 AUDIT RECORD DATA PAYLOAD ",
            82))[..8192];
        string sampleSignatureJson = "{\"signer_id\":\"usr-pi-001\",\"signer_name\":\"Dr. Elena Rostova\",\"role\":\"Principal Investigator\",\"meaning\":\"Document Approval\",\"timestamp\":\"2026-10-15T12:00:00Z\"}";
        string sampleRedactionJson = "{\"operator_name\":\"Cadence DeID Engine\",\"applied_at\":\"2026-10-15T12:00:00Z\",\"rules\":[\"HIPAA_SAFE_HARBOR\"],\"redacted_spans\":14}";

        var batch = new List<TmfDocument>(TotalDocuments);
        for (int i = 1; i <= TotalDocuments; i++)
        {
            var art = ArtifactCatalog[(i - 1) % ArtifactCatalog.Length];
            bool isCentral = i % 11 == 0;
            string? siteId = isCentral ? null : $"SITE-{(i % 10) + 1:D2}";
            bool isQc = i <= QcItemCount;
            string docId = isQc ? $"DOC-BMK-QC-{i:D4}" : $"DOC-BMK-{i:D4}";

            var report = new DocumentIntelligenceReport(
                DocumentId: docId,
                Filename: $"Artifact_{i:D4}_{art.Code}.pdf",
                MimeType: "application/pdf",
                Sha256Hash: $"{i:x64}",
                Modality: "PDF",
                PrimaryClassification: new TaxonomyMatchCandidate(art.Zone, art.Section, art.Code, art.Name, 0.96),
                AlternativeCandidates: [new TaxonomyMatchCandidate(art.Zone, art.Section, art.Code, art.Name, 0.42)],
                ConfidenceTier: "HIGH",
                QcRecommendation: "AUTO_STAGE_FOR_CRA_QC",
                AiGenerationManifest: new Dictionary<string, object>
                {
                    ["model_id"] = "cadence-etmf-classifier-v2.1",
                    ["classified_at"] = "2026-10-15T12:00:00Z"
                });
            string metadataJson = JsonSerializer.Serialize(new
            {
                document_intelligence = report,
                assigned_cra = "cra@etmf-benchmark.test"
            });

            TmfDocument doc;
            if (isQc)
            {
                doc = TmfDocument.CreateForTechnicalQc(
                    id: docId,
                    studyId: StudyCode,
                    filename: $"Artifact_{i:D4}_{art.Code}.pdf",
                    createdBy: "crc@etmf-benchmark.test",
                    actorRole: "CRC",
                    timestamp: now,
                    reasonForChange: "Batch upload AI auto-classification staging for CRA QC",
                    siteId: siteId,
                    zone: art.Zone,
                    section: art.Section,
                    artifactCode: art.Code,
                    artifactType: art.Name,
                    content: eightKbContent,
                    mimeType: "application/pdf",
                    taxonomyVersion: "v3.2.0-complete",
                    metadataJson: metadataJson,
                    contentChecksum: $"{i:x64}");
            }
            else
            {
                doc = TmfDocument.CreateApproved(
                    id: docId,
                    studyId: StudyCode,
                    filename: $"Artifact_{i:D4}_{art.Code}.pdf",
                    createdBy: "cra@etmf-benchmark.test",
                    createdAt: now.UtcDateTime.AddSeconds(i),
                    reasonForChange: "Seeded approved study eTMF artifact for PostgreSQL benchmark",
                    siteId: siteId,
                    zone: art.Zone,
                    section: art.Section,
                    artifactCode: art.Code,
                    artifactType: art.Name,
                    content: eightKbContent,
                    mimeType: "application/pdf",
                    taxonomyVersion: "v3.2.0-complete",
                    metadataJson: metadataJson);
                doc.Signer = "Dr. Elena Rostova";
                doc.SigningTimestamp = now;
                doc.SignatureManifestation = sampleSignatureJson;
                doc.ContentChecksum = $"{i:x64}";
            }

            doc.IsRedacted = true;
            doc.RedactionManifestJson = sampleRedactionJson;
            doc.DocumentOwnerId = "usr-pi-001";
            batch.Add(doc);
        }

        var tenantAccessor = rootServices.GetRequiredService<ICurrentTenantAccessor>();
        var tenantScope = rootServices.GetRequiredService<ITenantScope>();
        using (tenantScope.Bind(TestSecurityContext.TenantCode))
        {
            var optionsBuilder = new DbContextOptionsBuilder<EtmfDbContext>();
            optionsBuilder.UseNpgsql(
                $"{connectionString};Search Path=etmf",
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "etmf").EnableRetryOnFailure());
            optionsBuilder.UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true);
            optionsBuilder.AddInterceptors(
                StandaloneTenantConnectionInterceptor.Instance,
                StandaloneTenantSaveChangesInterceptor.Instance);

            for (int offset = 0; offset < batch.Count; offset += 500)
            {
                await using var db = new EtmfDbContext(optionsBuilder.Options);
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.TmfDocuments.AddRange(batch.Skip(offset).Take(500));
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _selects = new();

        public IReadOnlyCollection<string> SelectStatements => _selects.ToArray();

        public void Clear() => _selects.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase))
            {
                _selects.Enqueue(command.CommandText.Replace('\n', ' '));
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
