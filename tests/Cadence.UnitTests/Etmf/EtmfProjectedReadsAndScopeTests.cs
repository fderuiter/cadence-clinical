using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.UnitTests.TestSupport;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public sealed class EtmfProjectedReadsAndScopeTests : IDisposable
{
    private const string StudyId = "CADENCE-PROJ-101";
    private const string Reason = "21 CFR Part 11 regulated projection and scope verification";

    private readonly SqliteConnection _sqliteConnection;
    private readonly RecordingSqlInterceptor _sqlRecorder = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Cadence.Apps.Etmf.Program> _factory;

    public EtmfProjectedReadsAndScopeTests()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _factory = new WebApplicationFactory<Cadence.Apps.Etmf.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseTestStorage();

            host.ConfigureTestServices(services =>
            {
                services.RemoveBackgroundHostedServices();

                services.RemoveAll<DbContextOptions<EtmfDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<EtmfDbContext>>();
                services.AddDbContext<EtmfDbContext>((sp, options) =>
                {
                    options.UseSqlite(_sqliteConnection);
                    options.UseInternalServiceProvider(TestEfServiceProviders.Sqlite);
                    options.UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                        .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance, _sqlRecorder);
                });

                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(_clock);

                services.AddLogging(logging =>
                {
                    logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                });
            });
        });

        using var scope = _factory.Services.CreateScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        using (tenantScope.Bind(TestSecurityContext.TenantCode))
        {
            var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            db.Database.EnsureCreated();
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
        _sqliteConnection.Dispose();
    }

    [Fact]
    public async Task GetDocuments_FiltersByArtifactCodeAndSection_AndProjectsLightweightSummariesWithoutHeavyColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStudyDocumentsAsync(ct);

        using var craClient = CreateClient(["CRA"], [StudyId], ["SITE-101", "SITE-102"], "cra.lead");

        // 1. Filter by artifact_code=01.01.02
        _sqlRecorder.Clear();
        using var artifactResp = await craClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyId}&artifact_code=01.01.02",
            ct);
        artifactResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var artifactJson = JsonDocument.Parse(await artifactResp.Content.ReadAsStringAsync(ct));
        var artifactArray = artifactJson.RootElement.EnumerateArray().ToList();
        artifactArray.Should().ContainSingle();
        artifactArray[0].GetProperty("id").GetString().Should().Be("DOC-CENTRAL-010102");
        artifactArray[0].GetProperty("artifactCode").GetString().Should().Be("01.01.02");
        artifactArray[0].GetProperty("isRedacted").GetBoolean().Should().BeTrue();
        artifactArray[0].GetProperty("signer").GetString().Should().Be("Dr. Elena Rostova");

        // Summary DTO omits heavy text and JSONB columns from both payload and SQL SELECT
        artifactArray[0].TryGetProperty("content", out _).Should().BeFalse();
        artifactArray[0].TryGetProperty("metadataJson", out _).Should().BeFalse();
        artifactArray[0].TryGetProperty("signatureManifestation", out _).Should().BeFalse();
        artifactArray[0].TryGetProperty("redactionManifestJson", out _).Should().BeFalse();

        string artifactSql = string.Join(" | ", _sqlRecorder.DocumentSelects);
        artifactSql.Should().Contain("ArtifactCode");
        artifactSql.Should().NotContain("\"Content\"");
        artifactSql.Should().NotContain("\"MetadataJson\"");
        artifactSql.Should().NotContain("\"SignatureManifestation\"");
        artifactSql.Should().NotContain("\"RedactionManifestJson\"");

        // 2. Filter by section=01.01 returns both 01.01.01 and 01.01.02
        using var sectionResp = await craClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyId}&section=01.01",
            ct);
        sectionResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var sectionJson = JsonDocument.Parse(await sectionResp.Content.ReadAsStringAsync(ct));
        sectionJson.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .Should().BeEquivalentTo("DOC-CENTRAL-010101", "DOC-CENTRAL-010102");

        // 3. Omitting artifact_code and section returns all authorized study documents as summaries
        using var allResp = await craClient.GetAsync($"/api/v1/etmf/documents?study_id={StudyId}", ct);
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var allJson = JsonDocument.Parse(await allResp.Content.ReadAsStringAsync(ct));
        allJson.RootElement.GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task GetDocumentById_ReturnsFullMetadataSignatureAndRedactionManifests_WhileOmittingInlineContent()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStudyDocumentsAsync(ct);

        using var craClient = CreateClient(["CRA"], [StudyId], ["SITE-101", "SITE-102"], "cra.lead");

        _sqlRecorder.Clear();
        const string detailDocId = "DOC-CENTRAL-010102";
        using var detailResp = await craClient.GetAsync($"/api/v1/etmf/documents/{detailDocId}", ct);
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var detailJson = JsonDocument.Parse(await detailResp.Content.ReadAsStringAsync(ct));
        var root = detailJson.RootElement;
        root.GetProperty("id").GetString().Should().Be("DOC-CENTRAL-010102");
        root.TryGetProperty("content", out _).Should().BeFalse("inline Content must never be serialized on single-document metadata read");
        root.GetProperty("metadataJson").GetString().Should().Contain("document_intelligence");
        root.GetProperty("signatureManifestation").GetString().Should().Contain("Protocol Approval");
        root.GetProperty("redactionManifestJson").GetString().Should().Contain("HIPAA_SAFE_HARBOR");

        string detailSql = string.Join(" | ", _sqlRecorder.DocumentSelects);
        detailSql.Should().NotContain("\"Content\"");
        detailSql.Should().Contain("\"MetadataJson\"");
        detailSql.Should().Contain("\"SignatureManifestation\"");
        detailSql.Should().Contain("\"RedactionManifestJson\"");
    }

    [Theory]
    [InlineData("CRC")]
    [InlineData("Investigator")]
    public async Task SiteBoundCallers_PushCentralAndAssignedSitePredicatesIntoSql_OnDocumentsAndQcQueue(string siteBoundRole)
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStudyDocumentsAsync(ct);

        using var scopedClient = CreateClient([siteBoundRole], [StudyId], ["SITE-101"], $"{siteBoundRole.ToLowerInvariant()}.site101");

        // 1. GET /api/v1/etmf/documents pushes central (SiteId IS NULL / '') + assigned site predicate into SQL
        _sqlRecorder.Clear();
        using var docsResp = await scopedClient.GetAsync($"/api/v1/etmf/documents?study_id={StudyId}", ct);
        docsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var docsJson = JsonDocument.Parse(await docsResp.Content.ReadAsStringAsync(ct));
        var returnedDocIds = docsJson.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToList();

        string[] expectedScopedDocs = ["DOC-CENTRAL-010101", "DOC-CENTRAL-010102", "DOC-SITE101-050201"];
        returnedDocIds.Should().BeEquivalentTo(
            expectedScopedDocs,
            "central documents and assigned SITE-101 documents are returned while unassigned SITE-102 is filtered out in SQL");
        string docsSql = string.Join(" | ", _sqlRecorder.DocumentSelects);
        docsSql.Should().Contain("SiteId");

        // 2. Requesting unassigned site_id=SITE-102 returns 403 FORBIDDEN_SCOPE
        using var forbiddenResp = await scopedClient.GetAsync(
            $"/api/v1/etmf/documents?study_id={StudyId}&site_id=SITE-102",
            ct);
        forbiddenResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problemJson = JsonDocument.Parse(await forbiddenResp.Content.ReadAsStringAsync(ct));
        problemJson.RootElement.GetProperty("code").GetString().Should().Be("FORBIDDEN_SCOPE");

        // 3. GET /api/v1/etmf/intelligence/qc-queue also pushes central + assigned site predicate into SQL
        _sqlRecorder.Clear();
        using var qcResp = await scopedClient.GetAsync($"/api/v1/etmf/intelligence/qc-queue?study_id={StudyId}", ct);
        qcResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var qcJson = JsonDocument.Parse(await qcResp.Content.ReadAsStringAsync(ct));
        var qcDocIds = qcJson.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("documentId").GetString())
            .ToList();

        string[] expectedScopedQc = ["DOC-CENTRAL-010101", "DOC-SITE101-050201"];
        qcDocIds.Should().BeEquivalentTo(
            expectedScopedQc,
            "central and SITE-101 QC items are returned while unassigned SITE-102 QC item is filtered out in SQL");
        string qcSql = string.Join(" | ", _sqlRecorder.DocumentSelects);
        qcSql.Should().Contain("SiteId");
    }

    [Fact]
    public async Task GetQcQueue_OmitsSignatureAndRedactionColumns_ConditionallyLoadsContentOnlyWhenMetadataMissing_AndParsesSinglePassEnvelope()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedStudyDocumentsAsync(ct);

        using var craClient = CreateClient(["CRA"], [StudyId], ["SITE-101", "SITE-102"], "cra.reviewer");

        _sqlRecorder.Clear();
        using var qcResp = await craClient.GetAsync($"/api/v1/etmf/intelligence/qc-queue?study_id={StudyId}", ct);
        qcResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = JsonSerializer.Deserialize<List<CraqcStagingItem>>(
            await qcResp.Content.ReadAsStringAsync(ct),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        items.Should().NotBeNull();
        items.Should().HaveCount(3);

        // Item with persisted MetadataJson envelope hydrates report and assigned_cra in one JSON pass
        var persistedItem = items!.Single(i => i.DocumentId == "DOC-SITE101-050201");
        persistedItem.QcAssignedTo.Should().Be("cra.site101");
        persistedItem.DiscrepancyNotes.Should().Contain("Low classification confidence score");
        persistedItem.IntelligenceReport.PrimaryClassification.ArtifactCode.Should().Be("05.02.01");

        // Legacy item without MetadataJson falls back to Content heuristic classification
        var fallbackItem = items!.Single(i => i.DocumentId == "DOC-CENTRAL-010101");
        fallbackItem.IntelligenceReport.PrimaryClassification.ArtifactCode.Should().Be("01.01.01");

        string qcSql = string.Join(" | ", _sqlRecorder.DocumentSelects);
        qcSql.Should().Contain("CASE");
        qcSql.Should().Contain("MetadataJson");
        qcSql.Should().NotContain("\"SignatureManifestation\"");
        qcSql.Should().NotContain("\"RedactionManifestJson\"");
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

    private async Task SeedStudyDocumentsAsync(CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScope>();
        using (tenantScope.Bind(TestSecurityContext.TenantCode))
        {
            var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();
            if (await db.TmfDocuments.AnyAsync(ct))
            {
                return;
            }

            string largeContent = new('X', 8_192);
            var now = _clock.UtcNow;

            // 1. Central QC document WITHOUT MetadataJson (exercises Content fallback in GetQcQueue)
            var centralLegacyQc = TmfDocument.CreateForTechnicalQc(
                id: "DOC-CENTRAL-010101",
                studyId: StudyId,
                filename: "Clinical_Trial_Protocol_Draft.pdf",
                createdBy: "sponsor.lead",
                actorRole: "Sponsor Admin",
                timestamp: now,
                reasonForChange: Reason,
                siteId: null,
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.01",
                artifactType: "Clinical Trial Protocol",
                content: "Clinical Trial Protocol v1.0 full text " + largeContent,
                mimeType: "application/pdf",
                taxonomyVersion: "v3.2.0-complete",
                metadataJson: null,
                contentChecksum: "1111111111111111111111111111111111111111111111111111111111111111");

            // 2. Central Approved document with MetadataJson, SignatureManifestation, and RedactionManifestJson
            var centralApprovedReport = new DocumentIntelligenceReport(
                DocumentId: "DOC-CENTRAL-010102",
                Filename: "Protocol_Amendment_01.pdf",
                MimeType: "application/pdf",
                Sha256Hash: "2222222222222222222222222222222222222222222222222222222222222222",
                Modality: "PDF_BINARY",
                PrimaryClassification: new TaxonomyMatchCandidate(1, "01.01", "01.01.02", "Protocol Amendment", 0.97),
                AlternativeCandidates: [],
                ConfidenceTier: "HIGH",
                QcRecommendation: "AUTO_CLASSIFY",
                AiGenerationManifest: []);

            var centralApproved = TmfDocument.CreateApproved(
                id: "DOC-CENTRAL-010102",
                studyId: StudyId,
                filename: "Protocol_Amendment_01.pdf",
                createdBy: "sponsor.lead",
                createdAt: now.UtcDateTime.AddMinutes(1),
                reasonForChange: Reason,
                siteId: null,
                zone: 1,
                section: "01.01",
                artifactCode: "01.01.02",
                artifactType: "Protocol Amendment",
                content: largeContent,
                mimeType: "application/pdf",
                taxonomyVersion: "v3.2.0-complete",
                metadataJson: JsonSerializer.Serialize(new { document_intelligence = centralApprovedReport }));
            centralApproved.Signer = "Dr. Elena Rostova";
            centralApproved.SigningTimestamp = now;
            centralApproved.SignatureManifestation = "{\"signing_reason\":\"Protocol Approval\",\"signer\":\"Dr. Elena Rostova\"}";
            centralApproved.IsRedacted = true;
            centralApproved.RedactionManifestJson = "{\"operator_name\":\"Cadence DeID\",\"rules\":[\"HIPAA_SAFE_HARBOR\"]}";

            // 3. Site-101 QC document WITH MetadataJson envelope (assigned_cra + discrepancy_comment)
            var site101Report = new DocumentIntelligenceReport(
                DocumentId: "DOC-SITE101-050201",
                Filename: "Site101_FDA_1572.pdf",
                MimeType: "application/pdf",
                Sha256Hash: "3333333333333333333333333333333333333333333333333333333333333333",
                Modality: "PDF_BINARY",
                PrimaryClassification: new TaxonomyMatchCandidate(5, "05.02", "05.02.01", "FDA Form 1572", 0.62),
                AlternativeCandidates: [],
                ConfidenceTier: "LOW",
                QcRecommendation: "CRA_QC_REQUIRED",
                AiGenerationManifest: []);

            var site101Qc = TmfDocument.CreateForTechnicalQc(
                id: "DOC-SITE101-050201",
                studyId: StudyId,
                filename: "Site101_FDA_1572.pdf",
                createdBy: "crc.site101",
                actorRole: "CRC",
                timestamp: now.AddMinutes(2),
                reasonForChange: Reason,
                siteId: "SITE-101",
                zone: 5,
                section: "05.02",
                artifactCode: "05.02.01",
                artifactType: "FDA Form 1572",
                content: largeContent,
                mimeType: "application/pdf",
                taxonomyVersion: "v3.2.0-complete",
                metadataJson: JsonSerializer.Serialize(new
                {
                    document_intelligence = site101Report,
                    assigned_cra = "cra.site101",
                    discrepancy_comment = "Verify wet-ink PI signature date",
                }),
                contentChecksum: "3333333333333333333333333333333333333333333333333333333333333333");
            site101Qc.IsRedacted = true;
            site101Qc.RedactionManifestJson = "{\"operator_name\":\"Cadence DeID\"}";

            // 4. Site-102 QC document (unassigned for SITE-101 callers)
            var site102Report = new DocumentIntelligenceReport(
                DocumentId: "DOC-SITE102-050203",
                Filename: "Site102_Investigator_CV.pdf",
                MimeType: "application/pdf",
                Sha256Hash: "4444444444444444444444444444444444444444444444444444444444444444",
                Modality: "PDF_BINARY",
                PrimaryClassification: new TaxonomyMatchCandidate(5, "05.02", "05.02.03", "Investigator CV", 0.94),
                AlternativeCandidates: [],
                ConfidenceTier: "HIGH",
                QcRecommendation: "CRA_QC_REQUIRED",
                AiGenerationManifest: []);

            var site102Qc = TmfDocument.CreateForTechnicalQc(
                id: "DOC-SITE102-050203",
                studyId: StudyId,
                filename: "Site102_Investigator_CV.pdf",
                createdBy: "crc.site102",
                actorRole: "CRC",
                timestamp: now.AddMinutes(3),
                reasonForChange: Reason,
                siteId: "SITE-102",
                zone: 5,
                section: "05.02",
                artifactCode: "05.02.03",
                artifactType: "Investigator CV",
                content: largeContent,
                mimeType: "application/pdf",
                taxonomyVersion: "v3.2.0-complete",
                metadataJson: JsonSerializer.Serialize(new { document_intelligence = site102Report }),
                contentChecksum: "4444444444444444444444444444444444444444444444444444444444444444");

            db.TmfDocuments.AddRange(centralLegacyQc, centralApproved, site101Qc, site102Qc);
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed class RecordingSqlInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _selects = new();

        public IReadOnlyCollection<string> DocumentSelects => _selects.ToArray();

        public void Clear() => _selects.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("tmf_documents", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                _selects.Enqueue(command.CommandText.Replace('\n', ' '));
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
