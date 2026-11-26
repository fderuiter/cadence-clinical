using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Coding;
using Cadence.Apps.Execution.Application.Observations;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TabularImportMemoryQuotaCollection
{
    public const string Name = "TabularImportMemoryQuota";

    private TabularImportMemoryQuotaCollection()
    {
    }
}

/// <summary>
/// End-to-end verification of OpenXML archive, XML, row, and column quotas across all four bulk tabular import
/// endpoints in <see cref="Cadence.Apps.Execution.Program"/> via <see cref="ExecutionApiFactory"/>.
/// </summary>
[Collection(TabularImportMemoryQuotaCollection.Name)]
public sealed class TabularImportQuotaEndpointsTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string StudyId = "STUDY-VERIFY-101";
    private const string TenantId = "tenant-verify-101";
    private const string ChangeReason = "Bulk tabular quota verification for study import endpoints.";
    private const long MaxSparseRejectionAllocatedBytes = 4L * 1024 * 1024; // 4 MiB

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient CreateScopedDataManagerClient() =>
        factory.CreateSignedClient(new GatewayIdentity(
            UserId: "dm-verify-101",
            Roles: ["DATA_MANAGER"],
            SiteIds: [],
            TenantId: TenantId,
            StudyIds: [StudyId],
            ChangeReason: ChangeReason));

    [Fact]
    public async Task AllFourImportEndpoints_AcceptValidXlsx_WithExpectedParsedRows()
    {
        using var client = CreateScopedDataManagerClient();

        byte[] synonymXlsx = CreateValidSynonymXlsxBytes("headache", "MEDDRA", "26.0", "10019211", "Headache", "PT");
        using (var validateResp = await PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/validate", synonymXlsx, includeReason: false))
        {
            validateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var report = await validateResp.Content.ReadFromJsonAsync<PreflightValidationReportResponse>(ExecutionApiFactory.JsonOptions, Ct);
            report.Should().NotBeNull();
            report!.TotalRows.Should().Be(1);
            report.ValidCount.Should().Be(1);
            report.InvalidCount.Should().Be(0);
            report.CanImport.Should().BeTrue();
        }

        byte[] importSynonymXlsx = CreateValidSynonymXlsxBytes("pyrexia", "MEDDRA", "26.0", "10037660", "Pyrexia", "PT");
        using (var importResp = await PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/import", importSynonymXlsx, includeReason: true))
        {
            importResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var imported = await importResp.Content.ReadFromJsonAsync<BulkImportExecutionResponse>(ExecutionApiFactory.JsonOptions, Ct);
            imported.Should().NotBeNull();
            imported!.Job.TotalRows.Should().Be(1);
            imported.Job.ValidRows.Should().Be(1);
            imported.ImportedRules.Should().ContainSingle(r => r.VerbatimText == "pyrexia" && r.TargetCode == "10037660");
        }

        byte[] labXlsx = CreateValidLabRangeXlsxBytes("ALT", "Alanine Aminotransferase", "U/L", "10", "40");
        using (var dryRunResp = await PostLabMultipartAsync(client, "/api/v1/execution/labs/import/dry-run", labXlsx, includeReason: false))
        {
            dryRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dryRun = await dryRunResp.Content.ReadFromJsonAsync<BulkLabRangeDryRunResponse>(ExecutionApiFactory.JsonOptions, Ct);
            dryRun.Should().NotBeNull();
            dryRun!.IsSuccess.Should().BeTrue();
            dryRun.TotalRows.Should().Be(1);
            dryRun.ValidRows.Should().Be(1);
            dryRun.Rows.Should().ContainSingle().Which.TestCode.Should().Be("ALT");
            dryRun.Rows[0].LowBound.Should().BeApproximately(10.0, 0.001);
            dryRun.Rows[0].HighBound.Should().BeApproximately(40.0, 0.001);
        }

        byte[] commitLabXlsx = CreateValidLabRangeXlsxBytes("AST", "Aspartate Aminotransferase", "U/L", "8", "48");
        using var commitResp = await PostLabMultipartAsync(client, "/api/v1/execution/labs/import/commit", commitLabXlsx, includeReason: true);
        commitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var committed = await commitResp.Content.ReadFromJsonAsync<BulkLabRangeImportJobResponse>(ExecutionApiFactory.JsonOptions, Ct);
        committed.Should().NotBeNull();
        committed!.Status.Should().Be("COMPLETED");
        committed.TotalRows.Should().Be(1);
        committed.SuccessRows.Should().Be(1);
    }

    [Fact]
    public async Task AllFourImportEndpoints_RejectExtremeSparseColumnXlsx_With400Under4MbAllocated()
    {
        using var client = CreateScopedDataManagerClient();
        byte[] sparseXlsx = CreateSparseMarkerColumnXlsxBytes();
        sparseXlsx.Length.Should().BeLessThanOrEqualTo(352);

        // Warm up host pipeline and serializer caches so per-request allocation measurement reflects request execution.
        byte[] warmupXlsx = CreateValidSynonymXlsxBytes("nausea", "MEDDRA", "26.0", "10028813", "Nausea", "PT");
        using (var warmup = await PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/validate", warmupXlsx, includeReason: false))
        {
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        byte[] warmupLab = CreateValidLabRangeXlsxBytes("GLUC", "Glucose", "mg/dL", "70", "99");
        using (var warmup = await PostLabMultipartAsync(client, "/api/v1/execution/labs/import/dry-run", warmupLab, includeReason: false))
        {
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var endpoints = new (string Name, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("POST /api/v1/execution/coding/synonyms/validate",
                () => PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/validate", sparseXlsx, includeReason: false)),
            ("POST /api/v1/execution/coding/synonyms/import",
                () => PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/import", sparseXlsx, includeReason: true)),
            ("POST /api/v1/execution/labs/import/dry-run",
                () => PostLabMultipartAsync(client, "/api/v1/execution/labs/import/dry-run", sparseXlsx, includeReason: false)),
            ("POST /api/v1/execution/labs/import/commit",
                () => PostLabMultipartAsync(client, "/api/v1/execution/labs/import/commit", sparseXlsx, includeReason: true)),
        };

        foreach (var (name, send) in endpoints)
        {
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            using var response = await send();
            long allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            await AssertBadRequestProblemDetailsWithoutStackTraceAsync(response, name);
            allocatedDelta.Should().BeLessThan(
                MaxSparseRejectionAllocatedBytes,
                $"endpoint '{name}' must reject sparse column coordinates before allocating multi-megabyte row buffers");
        }
    }

    [Fact]
    public async Task ImportEndpoints_RejectHighRatioCorruptAndMalformedXlsx_With400ProblemDetails()
    {
        using var client = CreateScopedDataManagerClient();
        byte[] highRatioXlsx = CreateHighCompressionRatioXlsxBytes();
        byte[] corruptZipBytes = [0x50, 0x4B, 0x03, 0x04, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01];
        byte[] malformedXmlXlsx = CreateMalformedXmlXlsxBytes();

        foreach (byte[] payload in new[] { highRatioXlsx, corruptZipBytes, malformedXmlXlsx })
        {
            using var synValidate = await PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/validate", payload, includeReason: false);
            await AssertBadRequestProblemDetailsWithoutStackTraceAsync(synValidate, "/coding/synonyms/validate");

            using var synImport = await PostSynonymMultipartAsync(client, "/api/v1/execution/coding/synonyms/import", payload, includeReason: true);
            await AssertBadRequestProblemDetailsWithoutStackTraceAsync(synImport, "/coding/synonyms/import");

            using var labDryRun = await PostLabMultipartAsync(client, "/api/v1/execution/labs/import/dry-run", payload, includeReason: false);
            await AssertBadRequestProblemDetailsWithoutStackTraceAsync(labDryRun, "/labs/import/dry-run");

            using var labCommit = await PostLabMultipartAsync(client, "/api/v1/execution/labs/import/commit", payload, includeReason: true);
            await AssertBadRequestProblemDetailsWithoutStackTraceAsync(labCommit, "/labs/import/commit");
        }
    }

    private static async Task AssertBadRequestProblemDetailsWithoutStackTraceAsync(HttpResponseMessage response, string endpointName)
    {
        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"'{endpointName}' returned {(int)response.StatusCode}: {body}");
        body.Should().NotContain("   at ", $"'{endpointName}' must not leak stack traces in ProblemDetails");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<HttpResponseMessage> PostSynonymMultipartAsync(
        HttpClient client,
        string path,
        byte[] fileBytes,
        bool includeReason)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "synonyms.xlsx");
        form.Add(new StringContent("STUDY"), "scope");
        form.Add(new StringContent(StudyId), "study_id");
        if (includeReason)
        {
            form.Add(new StringContent(ChangeReason), "reason_for_change");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        if (includeReason)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReason);
        }

        return await client.SendAsync(req, Ct);
    }

    private static async Task<HttpResponseMessage> PostLabMultipartAsync(
        HttpClient client,
        string path,
        byte[] fileBytes,
        bool includeReason)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "lab_ranges.xlsx");
        form.Add(new StringContent(StudyId), "study_id");
        form.Add(new StringContent("CENTRAL"), "lab_source");
        if (includeReason)
        {
            form.Add(new StringContent(ChangeReason), "reason_for_change");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        if (includeReason)
        {
            req.Headers.Add(GatewayHeaders.ChangeReason, ChangeReason);
        }

        return await client.SendAsync(req, Ct);
    }

    private static byte[] CreateSparseMarkerColumnXlsxBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"BZZZZ1\" t=\"inlineStr\"><is><t>MARKER_COL</t></is></c></row></sheetData></worksheet>");
        }

        return ms.ToArray();
    }

    private static byte[] CreateHighCompressionRatioXlsxBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            writer.Write(new string(' ', 300_000));
            writer.Write("</sheetData></worksheet>");
        }

        return ms.ToArray();
    }

    private static byte[] CreateMalformedXmlXlsxBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">");
        }

        return ms.ToArray();
    }

    private static byte[] CreateValidSynonymXlsxBytes(
        string verbatim,
        string dictType,
        string version,
        string code,
        string term,
        string level)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ssEntry = zip.CreateEntry("xl/sharedStrings.xml");
            using (var writer = new StreamWriter(ssEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<si><t>verbatim_text</t></si><si><t>dictionary_type</t></si><si><t>dictionary_version</t></si>" +
                    "<si><t>target_code</t></si><si><t>target_term</t></si><si><t>target_level</t></si>" +
                    $"<si><t>{verbatim}</t></si><si><t>{dictType}</t></si><si><t>{version}</t></si>" +
                    $"<si><t>{code}</t></si><si><t>{term}</t></si><si><t>{level}</t></si>" +
                    "</sst>");
            }

            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(sheetEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"s\"><v>2</v></c><c r=\"D1\" t=\"s\"><v>3</v></c><c r=\"E1\" t=\"s\"><v>4</v></c><c r=\"F1\" t=\"s\"><v>5</v></c></row>" +
                    "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>6</v></c><c r=\"B2\" t=\"s\"><v>7</v></c><c r=\"C2\" t=\"s\"><v>8</v></c><c r=\"D2\" t=\"s\"><v>9</v></c><c r=\"E2\" t=\"s\"><v>10</v></c><c r=\"F2\" t=\"s\"><v>11</v></c></row>" +
                    "</sheetData></worksheet>");
            }
        }

        return ms.ToArray();
    }

    private static byte[] CreateValidLabRangeXlsxBytes(
        string testCode,
        string testName,
        string unit,
        string lowBound,
        string highBound)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheetEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" +
                "<row r=\"1\">" +
                "<c r=\"A1\" t=\"inlineStr\"><is><t>Test Code</t></is></c>" +
                "<c r=\"B1\" t=\"inlineStr\"><is><t>Test Name</t></is></c>" +
                "<c r=\"C1\" t=\"inlineStr\"><is><t>Unit</t></is></c>" +
                "<c r=\"D1\" t=\"inlineStr\"><is><t>Low Bound</t></is></c>" +
                "<c r=\"E1\" t=\"inlineStr\"><is><t>High Bound</t></is></c>" +
                "</row>" +
                "<row r=\"2\">" +
                $"<c r=\"A2\" t=\"inlineStr\"><is><t>{testCode}</t></is></c>" +
                $"<c r=\"B2\" t=\"inlineStr\"><is><t>{testName}</t></is></c>" +
                $"<c r=\"C2\" t=\"inlineStr\"><is><t>{unit}</t></is></c>" +
                $"<c r=\"D2\"><v>{lowBound}</v></c>" +
                $"<c r=\"E2\"><v>{highBound}</v></c>" +
                "</row>" +
                "</sheetData></worksheet>");
        }

        return ms.ToArray();
    }
}
