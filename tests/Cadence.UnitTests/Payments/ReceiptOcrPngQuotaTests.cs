using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Payments;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentsPngMemoryQuotaCollection
{
    public const string Name = "PaymentsPngMemoryQuota";

    private PaymentsPngMemoryQuotaCollection()
    {
    }
}

/// <summary>
/// Regression and memory-allocation quota suite for <see cref="ReceiptOcrEngine"/>,
/// <c>PngReceiptDecoder</c>, and the participant/staff receipt OCR and expense endpoints.
/// Isolated from <see cref="ParticipantSelfServiceAndOcrTests"/> to keep memory-quota
/// fixtures non-parallelized and avoid merge collisions on general wallet/expense tests.
/// </summary>
[Collection(PaymentsPngMemoryQuotaCollection.Name)]
public sealed class ReceiptOcrPngQuotaTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly WebApplicationFactory<Cadence.Apps.Payments.Program> _factory;

    public ReceiptOcrPngQuotaTests()
    {
        _connection.Open();
        _factory = new WebApplicationFactory<Cadence.Apps.Payments.Program>().WithWebHostBuilder(host =>
        {
            host.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys();
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<PaymentsDbContext>>();
                TestSupport.TestEfServiceProviders.RemoveBackgroundHostedServices(services);
                services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(_connection));
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task PngHighRatioZlibBombs_AcrossAllFourOcrAndExpenseRoutes_FailFastWithBoundedMemoryAndHoldInPendingReview()
    {
        await EnsureDbCreatedAsync();
        using var subjectClient = SubjectClient("SUBJ-PNG-QUOTA-01", "SITE-01");
        using var crcClient = _factory.CreateSignedClient(
            TestSecurityContext.Identity("crc-png-quota-1", "crc") with { StudyIds = ["STUDY-01"], SiteIds = ["SITE-01"] });

        // Warm up EF Core model, JSON serializers, and route pipelines before measuring per-request GC deltas.
        byte[] warmupPng = ReceiptOcrEngine.RenderReceiptPng(["METRO TRANSIT", "DATE 2026-08-23", "TOTAL $18.00"]);
        string warmupB64 = Convert.ToBase64String(warmupPng);
        _ = await PostOcrAsync(subjectClient, warmupB64, "warmup.png");
        using (var warmSelfClaim = await subjectClient.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses",
            new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "METRO TRANSIT",
                GrossAmount: 18.00m,
                ExpenseDate: "2026-08-23",
                ReceiptImageBase64: warmupB64,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01"),
            Ct))
        {
            warmSelfClaim.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var warmStaffOcr = await crcClient.PostAsJsonAsync(
            "/api/v1/payments/participant/expenses/ocr-extract",
            new OcrExtractRequestDto(ImageBase64: warmupB64, Filename: "warmup_staff.png"),
            Ct))
        {
            warmStaffOcr.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var warmStaffClaim = await crcClient.PostAsJsonAsync(
            "/api/v1/payments/participant/expenses",
            new SubmitExpenseClaimRequestDto(
                SubjectId: "SUBJ-PNG-WARMUP",
                StudyId: "STUDY-01",
                SiteId: "SITE-01",
                Category: "TRANSIT",
                MerchantName: "METRO TRANSIT",
                GrossAmount: 18.00m,
                ExpenseDate: "2026-08-23",
                ReceiptImageBase64: warmupB64),
            Ct))
        {
            warmStaffClaim.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        const int inflatedBombBytes = 36 * 1024 * 1024; // 36 MiB inflated (~36 KiB compressed, >1000:1 ratio)
        const long maxAllowedAllocationDelta = 32L * 1024 * 1024; // 32 MiB
        const int maxRawTextChars = 262_144;

        byte[] compressedTextBomb = BuildHighRatioZlibPayload(
            inflatedBombBytes,
            prefixAscii: "METRO TRANSIT\nDATE 2026-08-23\nTOTAL $28.50\n",
            fillByte: (byte)' ');
        byte[] compressedIdatBomb = BuildHighRatioZlibPayload(
            inflatedBombBytes,
            prefixAscii: null,
            fillByte: 0);

        // Verify the compressed payloads truly exceed 1000:1 expansion ratio (~36 KiB compressed -> 36 MiB inflated).
        (inflatedBombBytes / Math.Max(1, compressedTextBomb.Length)).Should().BeGreaterThanOrEqualTo(900);
        (inflatedBombBytes / Math.Max(1, compressedIdatBomb.Length)).Should().BeGreaterThanOrEqualTo(900);

        byte[] valid1x1Idat = CompressBytes([0, 250]); // 1x1 grayscale (expectedRawBytes = 2)

        byte[] ztxtBombPng = BuildCustom1x1GrayscalePng(
            idatCompressed: valid1x1Idat,
            extraChunks: [("zTXt", BuildZtxtPayload("Comment", compressedTextBomb))]);

        byte[] itxtBombPng = BuildCustom1x1GrayscalePng(
            idatCompressed: valid1x1Idat,
            extraChunks: [("iTXt", BuildItxtPayload("Description", compressionFlag: 1, compressedTextBomb))]);

        byte[] idatBombPng = BuildCustom1x1GrayscalePng(
            idatCompressed: compressedIdatBomb,
            extraChunks: []);

        var cases = new (string Name, string Base64)[]
        {
            ("ztxt-comment-36mib", Convert.ToBase64String(ztxtBombPng)),
            ("itxt-description-36mib", Convert.ToBase64String(itxtBombPng)),
            ("idat-1x1-36mib", Convert.ToBase64String(idatBombPng)),
        };

        int caseIndex = 0;
        foreach (var (caseName, bombBase64) in cases)
        {
            caseIndex++;

            // Route 1: signed site-scoped "subject" -> POST /api/v1/payments/participant/me/expenses/ocr-extract
            var selfOcrReq = new OcrExtractRequestDto(ImageBase64: bombBase64, Filename: $"{caseName}-self.png");
            long allocBeforeSelfOcr = GC.GetTotalAllocatedBytes(precise: true);
            using var selfOcrResp = await subjectClient.PostAsJsonAsync(
                "/api/v1/payments/participant/me/expenses/ocr-extract",
                selfOcrReq,
                Ct);
            var selfOcrBody = await selfOcrResp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(Ct);
            long selfOcrDelta = GC.GetTotalAllocatedBytes(precise: true) - allocBeforeSelfOcr;

            selfOcrResp.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} self ocr-extract");
            selfOcrBody.Should().NotBeNull();
            selfOcrBody!.OcrReadable.Should().BeFalse($"{caseName} self ocr-extract must reject high-ratio PNG");
            selfOcrBody.OcrToken.Should().BeNull();
            selfOcrBody.RawText.Length.Should().BeLessThanOrEqualTo(maxRawTextChars);
            selfOcrDelta.Should().BeLessThan(maxAllowedAllocationDelta, $"{caseName} self ocr-extract memory delta");

            // Route 2: signed site-scoped "subject" -> POST /api/v1/payments/participant/me/expenses
            var selfExpenseReq = new SelfServiceSubmitExpenseRequestDto(
                Category: "TRANSIT",
                MerchantName: "METRO TRANSIT",
                GrossAmount: 28.50m,
                ExpenseDate: "2026-08-23",
                ReceiptImageBase64: bombBase64,
                StudyCode: "STUDY-01",
                SiteCode: "SITE-01");
            long allocBeforeSelfExp = GC.GetTotalAllocatedBytes(precise: true);
            using var selfExpResp = await subjectClient.PostAsJsonAsync(
                "/api/v1/payments/participant/me/expenses",
                selfExpenseReq,
                Ct);
            var selfExpBody = await selfExpResp.Content.ReadFromJsonAsync<SelfServiceExpenseClaimResponseDto>(Ct);
            long selfExpDelta = GC.GetTotalAllocatedBytes(precise: true) - allocBeforeSelfExp;

            selfExpResp.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} self expenses");
            selfExpBody.Should().NotBeNull();
            selfExpBody!.Status.Should().Be("PENDING_REVIEW", $"{caseName} self expense claim must not auto-approve");
            selfExpBody.AutoApproved.Should().BeFalse();
            selfExpBody.DisbursementReference.Should().BeNull();
            selfExpDelta.Should().BeLessThan(maxAllowedAllocationDelta, $"{caseName} self expenses memory delta");

            // Route 3: signed "crc" -> POST /api/v1/payments/participant/expenses/ocr-extract
            var staffOcrReq = new OcrExtractRequestDto(ImageBase64: bombBase64, Filename: $"{caseName}-staff.png");
            long allocBeforeStaffOcr = GC.GetTotalAllocatedBytes(precise: true);
            using var staffOcrResp = await crcClient.PostAsJsonAsync(
                "/api/v1/payments/participant/expenses/ocr-extract",
                staffOcrReq,
                Ct);
            var staffOcrBody = await staffOcrResp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(Ct);
            long staffOcrDelta = GC.GetTotalAllocatedBytes(precise: true) - allocBeforeStaffOcr;

            staffOcrResp.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} staff ocr-extract");
            staffOcrBody.Should().NotBeNull();
            staffOcrBody!.OcrReadable.Should().BeFalse($"{caseName} staff ocr-extract must reject high-ratio PNG");
            staffOcrBody.OcrToken.Should().BeNull();
            staffOcrBody.RawText.Length.Should().BeLessThanOrEqualTo(maxRawTextChars);
            staffOcrDelta.Should().BeLessThan(maxAllowedAllocationDelta, $"{caseName} staff ocr-extract memory delta");

            // Route 4: signed "crc" -> POST /api/v1/payments/participant/expenses
            var staffExpenseReq = new SubmitExpenseClaimRequestDto(
                SubjectId: $"SUBJ-STAFF-QUOTA-{caseIndex:D2}",
                StudyId: "STUDY-01",
                SiteId: "SITE-01",
                Category: "TRANSIT",
                MerchantName: "METRO TRANSIT",
                GrossAmount: 28.50m,
                ExpenseDate: "2026-08-23",
                ReceiptImageBase64: bombBase64);
            long allocBeforeStaffExp = GC.GetTotalAllocatedBytes(precise: true);
            using var staffExpResp = await crcClient.PostAsJsonAsync(
                "/api/v1/payments/participant/expenses",
                staffExpenseReq,
                Ct);
            var staffExpBody = await staffExpResp.Content.ReadFromJsonAsync<ParticipantExpenseClaim>(Ct);
            long staffExpDelta = GC.GetTotalAllocatedBytes(precise: true) - allocBeforeStaffExp;

            staffExpResp.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} staff expenses");
            staffExpBody.Should().NotBeNull();
            staffExpBody!.Status.Should().Be("PENDING_REVIEW", $"{caseName} staff expense claim must not auto-approve");
            staffExpBody.DisbursementReference.Should().BeNull();
            staffExpDelta.Should().BeLessThan(maxAllowedAllocationDelta, $"{caseName} staff expenses memory delta");
        }
    }

    [Fact]
    [GxPRequirement("PRD-PAT-002")]
    public async Task PngQuotasAndPayloadBounds_AllowSmallHighRatioMetadataUnder16KiB_AndRejectMalformedOrderChunkFloodAndOver10MiBPayloads()
    {
        await EnsureDbCreatedAsync();
        using var client = SubjectClient("SUBJ-PNG-EDGE-01", "SITE-01");

        byte[] valid1x1Idat = CompressBytes([0, 250]);

        // 1. Small zTXt chunk (< 16 KiB uncompressed) with repeated spaces (> 100:1 ratio) is allowed by the 16 KiB floor,
        // including MIME line-wrapped base64 payloads.
        string paddedSmallReceipt = "METRO TRANSIT" + new string(' ', 10_000) + "\nDATE 2026-08-23\nTOTAL $28.50";
        byte[] compressedSmallReceipt = CompressBytes(Encoding.UTF8.GetBytes(paddedSmallReceipt));
        (Encoding.UTF8.GetByteCount(paddedSmallReceipt) / Math.Max(1, compressedSmallReceipt.Length)).Should().BeGreaterThan(100);

        byte[] smallHighRatioPng = BuildCustom1x1GrayscalePng(
            idatCompressed: valid1x1Idat,
            extraChunks: [("zTXt", BuildZtxtPayload("Comment", compressedSmallReceipt))]);
        string wrappedSmallPngBase64 = Convert.ToBase64String(smallHighRatioPng, Base64FormattingOptions.InsertLineBreaks);
        var smallScan = await PostOcrAsync(client, wrappedSmallPngBase64, "small_padded.png");
        smallScan.OcrReadable.Should().BeTrue();
        smallScan.MerchantName.Should().Be("METRO TRANSIT");
        smallScan.ExpenseDate.Should().Be("2026-08-23");
        smallScan.GrossAmount.Should().Be(28.50m);
        smallScan.OcrToken.Should().NotBeNullOrWhiteSpace();

        // 2. PNG with zTXt appearing BEFORE IHDR or with duplicate IHDR or invalid scanline filter (> 4) is rejected without extracting metadata.
        byte[] textBeforeIhdrPng = BuildPngWithRawChunkList(
        [
            ("zTXt", BuildZtxtPayload("Comment", compressedSmallReceipt)),
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
            ("IDAT", valid1x1Idat),
            ("IEND", []),
        ]);
        var outOfOrderScan = await PostOcrAsync(client, Convert.ToBase64String(textBeforeIhdrPng), "bad_order.png");
        outOfOrderScan.OcrReadable.Should().BeFalse();
        outOfOrderScan.OcrToken.Should().BeNull();

        byte[] duplicateIhdrPng = BuildPngWithRawChunkList(
        [
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
            ("zTXt", BuildZtxtPayload("Comment", compressedSmallReceipt)),
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
            ("IDAT", valid1x1Idat),
            ("IEND", []),
        ]);
        var dupIhdrScan = await PostOcrAsync(client, Convert.ToBase64String(duplicateIhdrPng), "dup_ihdr.png");
        dupIhdrScan.OcrReadable.Should().BeFalse();
        dupIhdrScan.OcrToken.Should().BeNull();

        byte[] invalidFilterPng = BuildCustom1x1GrayscalePng(
            idatCompressed: CompressBytes([9, 250]), // filterType = 9 is invalid per PNG spec
            extraChunks: [("zTXt", BuildZtxtPayload("Comment", compressedSmallReceipt))]);
        var invalidFilterScan = await PostOcrAsync(client, Convert.ToBase64String(invalidFilterPng), "bad_filter.png");
        invalidFilterScan.OcrReadable.Should().BeFalse();
        invalidFilterScan.OcrToken.Should().BeNull();

        // 3. PNG exceeding MaxPngChunkCount (2,048 chunks) is rejected.
        var manyChunks = new List<(string Type, byte[] Data)>(ReceiptOcrEngine.MaxPngChunkCount + 4)
        {
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
            ("zTXt", BuildZtxtPayload("Comment", compressedSmallReceipt)),
        };
        for (int i = 0; i < ReceiptOcrEngine.MaxPngChunkCount; i++)
        {
            manyChunks.Add(("tIME", [0x07, 0xEA, 0x08, 0x17, 0x0C, 0x00, 0x00]));
        }

        manyChunks.Add(("IDAT", valid1x1Idat));
        manyChunks.Add(("IEND", []));
        byte[] chunkFloodPng = BuildPngWithRawChunkList(manyChunks);
        var chunkFloodScan = await PostOcrAsync(client, Convert.ToBase64String(chunkFloodPng), "chunk_flood.png");
        chunkFloodScan.OcrReadable.Should().BeFalse();
        chunkFloodScan.OcrToken.Should().BeNull();

        // 4. Cumulative metadata exceeding 256 KiB across chunks (five 56 KiB tEXt chunks = 280 KiB) is rejected.
        var cumulativeMetaChunks = new List<(string Type, byte[] Data)>
        {
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
        };
        byte[] fiftySixKibChunk = Encoding.Latin1.GetBytes("Comment\0METRO TRANSIT\nDATE 2026-08-23\nTOTAL $28.50\n" + new string('X', 56 * 1024));
        for (int i = 0; i < 5; i++)
        {
            cumulativeMetaChunks.Add(("tEXt", fiftySixKibChunk));
        }

        cumulativeMetaChunks.Add(("IDAT", valid1x1Idat));
        cumulativeMetaChunks.Add(("IEND", []));
        byte[] cumulativeOverflowPng = BuildPngWithRawChunkList(cumulativeMetaChunks);
        var cumulativeScan = await PostOcrAsync(client, Convert.ToBase64String(cumulativeOverflowPng), "cumulative_meta.png");
        cumulativeScan.OcrReadable.Should().BeFalse();
        cumulativeScan.OcrToken.Should().BeNull();
        cumulativeScan.RawText.Length.Should().BeLessThanOrEqualTo(ReceiptOcrEngine.MaxExtractedRawTextChars);

        // 5. Base64 and data: inputs above 10 MiB (10 MiB + 1 byte = 13,981,016 chars ending with single '=') are rejected before allocating decoded byte[].
        string tenMibPlusOneBase64 = string.Create(
            ReceiptOcrEngine.MaxBase64NonWhitespaceChars,
            0,
            static (span, _) =>
            {
                span.Fill('A');
                span[^1] = '=';
            });
        string oversizedDataUri = "data:image/png;base64," + tenMibPlusOneBase64;

        long beforeDecodeCheck = GC.GetAllocatedBytesForCurrentThread();
        bool decodedRaw = ReceiptOcrEngine.TryDecodeReceiptPayload(tenMibPlusOneBase64, out byte[] rawOut);
        bool decodedUri = ReceiptOcrEngine.TryDecodeReceiptPayload(oversizedDataUri, out byte[] uriOut);
        long decodeAllocDelta = GC.GetAllocatedBytesForCurrentThread() - beforeDecodeCheck;

        decodedRaw.Should().BeFalse();
        rawOut.Should().BeEmpty();
        decodedUri.Should().BeFalse();
        uriOut.Should().BeEmpty();
        decodeAllocDelta.Should().BeLessThan(64 * 1024, "oversized base64/data: inputs must fail length check before allocating 10 MiB buffers");

        // Massive encoded string exceeding MaxEncodedReceiptPayloadChars also short-circuits in O(1) with near-zero allocation.
        string aboveMaxCharsPayload = new('A', ReceiptOcrEngine.MaxEncodedReceiptPayloadChars + 4);
        long beforeFastReject = GC.GetAllocatedBytesForCurrentThread();
        var fastRejected = ReceiptOcrEngine.Extract(rawText: null, imageBase64: aboveMaxCharsPayload);
        long fastRejectDelta = GC.GetAllocatedBytesForCurrentThread() - beforeFastReject;

        fastRejected.OcrReadable.Should().BeFalse();
        fastRejectDelta.Should().BeLessThan(16 * 1024);

        // HTTP routes also reject > 10 MiB base64/data: inputs cleanly without falling back to RawText or minting OcrToken.
        using (var mixedOversizedResp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses/ocr-extract",
            new OcrExtractRequestDto(
                RawText: "METRO TRANSIT\nDATE 2026-08-23\nTOTAL $28.50",
                ImageBase64: oversizedDataUri,
                Filename: "oversized_10mb.png"),
            Ct))
        {
            mixedOversizedResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var oversizedOcrScan = (await mixedOversizedResp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(Ct))!;
            oversizedOcrScan.OcrReadable.Should().BeFalse("oversized ImageBase64 must never fall back to RawText");
            oversizedOcrScan.OcrToken.Should().BeNull();
            oversizedOcrScan.ReceiptSha256.Should().BeNull();
        }

        // ADR-2667 alignment: unassigned subject (SiteIds = []) fails closed with 403 Forbidden before decoding payload.
        using (var unassignedSubject = _factory.CreateSignedClient(
            TestSecurityContext.Identity("SUBJ-PNG-UNASSIGNED", "subject") with { SiteIds = [] }))
        {
            using var unassignedResp = await unassignedSubject.PostAsJsonAsync(
                "/api/v1/payments/participant/me/expenses/ocr-extract",
                new OcrExtractRequestDto(ImageBase64: Convert.ToBase64String(smallHighRatioPng), Filename: "unassigned.png"),
                Ct);
            unassignedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 6. Valid PNG, JPEG, BMP, PNM, PDF, and SVG receipts still extract identical merchant, date, category, amount, and non-null ocrToken.
        byte[] canonicalPng = ReceiptOcrEngine.RenderReceiptPng(["METRO TRANSIT", "DATE 2026-08-23", "TOTAL $28.50"]);
        (byte[] rasterGray, int rasterW, int rasterH) = UnpackFilterZeroGrayscalePng(canonicalPng);
        byte[] canonicalJpeg = BuildCommentJpeg("METRO TRANSIT\nDATE 2026-08-23\nTOTAL $28.50");
        byte[] canonicalBmp = EncodeGrayscaleAs24BitBmp(rasterGray, rasterW, rasterH);
        byte[] canonicalPnm = EncodeGrayscaleAsPgm(rasterGray, rasterW, rasterH);
        byte[] canonicalPdf = Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\n<< /Length 68 >>\nstream\nBT (METRO TRANSIT) Tj (DATE 2026-08-23) Tj (TOTAL $28.50) Tj ET\nendstream\nendobj\n%%EOF");
        byte[] canonicalSvg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text>METRO TRANSIT</text><text>DATE 2026-08-23</text><text>TOTAL $28.50</text></svg>");

        foreach (var (ext, payloadBytes) in new (string Ext, byte[] Bytes)[]
        {
            ("png", canonicalPng),
            ("jpg", canonicalJpeg),
            ("bmp", canonicalBmp),
            ("pgm", canonicalPnm),
            ("pdf", canonicalPdf),
            ("svg", canonicalSvg),
        })
        {
            var formatScan = await PostOcrAsync(client, Convert.ToBase64String(payloadBytes), $"receipt.{ext}");
            formatScan.OcrReadable.Should().BeTrue($"{ext} receipt must remain readable");
            formatScan.MerchantName.Should().Be("METRO TRANSIT", $"{ext} merchant");
            formatScan.ExpenseDate.Should().Be("2026-08-23", $"{ext} date");
            formatScan.Category.Should().Be("TRANSIT", $"{ext} category");
            formatScan.GrossAmount.Should().Be(28.50m, $"{ext} amount");
            formatScan.OcrToken.Should().NotBeNullOrWhiteSpace($"{ext} ocrToken");
        }
    }

    private HttpClient SubjectClient(string subjectId, params string[] siteIds) =>
        _factory.CreateSignedClient(TestSecurityContext.Identity(subjectId, "subject") with
        {
            SiteIds = siteIds.Length > 0 ? siteIds : ["SITE-01"],
        });

    private PaymentsDbContext OpenDb() =>
        _factory.Services.CreateScope().ServiceProvider.GetRequiredService<PaymentsDbContext>();

    private async Task EnsureDbCreatedAsync()
    {
        await using var db = OpenDb();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private static async Task<ReceiptOcrExtractResponseDto> PostOcrAsync(HttpClient client, string imageBase64, string filename)
    {
        using var resp = await client.PostAsJsonAsync(
            "/api/v1/payments/participant/me/expenses/ocr-extract",
            new OcrExtractRequestDto(ImageBase64: imageBase64, Filename: filename),
            Ct);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<ReceiptOcrExtractResponseDto>(Ct);
        body.Should().NotBeNull();
        return body!;
    }

    private static (byte[] Gray, int Width, int Height) UnpackFilterZeroGrayscalePng(byte[] png)
    {
        int width = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        int height = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        int idatLen = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33, 4));
        using var idatMs = new MemoryStream(png, 41, idatLen, writable: false);
        using var zlib = new ZLibStream(idatMs, CompressionMode.Decompress);
        byte[] scanlines = new byte[(width + 1) * height];
        zlib.ReadExactly(scanlines);
        byte[] gray = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(scanlines, y * (width + 1) + 1, gray, y * width, width);
        }

        return (gray, width, height);
    }

    private static byte[] BuildCommentJpeg(string commentText)
    {
        byte[] commentBytes = Encoding.UTF8.GetBytes(commentText);
        ushort segLen = (ushort)(commentBytes.Length + 2);
        byte[] jpeg = new byte[4 + 2 + commentBytes.Length + 2];
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        jpeg[2] = 0xFF;
        jpeg[3] = 0xFE;
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(4, 2), segLen);
        Buffer.BlockCopy(commentBytes, 0, jpeg, 6, commentBytes.Length);
        jpeg[^2] = 0xFF;
        jpeg[^1] = 0xD9;
        return jpeg;
    }

    private static byte[] EncodeGrayscaleAs24BitBmp(byte[] gray, int width, int height)
    {
        int rowStride = ((width * 3 + 3) / 4) * 4;
        int imageBytes = rowStride * height;
        byte[] bmp = new byte[54 + imageBytes];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2, 4), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22, 4), -height); // top-down
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bmp.AsSpan(28, 2), 24);
        for (int y = 0; y < height; y++)
        {
            int dstRow = 54 + y * rowStride;
            for (int x = 0; x < width; x++)
            {
                byte lum = gray[y * width + x];
                int px = dstRow + x * 3;
                bmp[px] = lum;
                bmp[px + 1] = lum;
                bmp[px + 2] = lum;
            }
        }

        return bmp;
    }

    private static byte[] EncodeGrayscaleAsPgm(byte[] gray, int width, int height)
    {
        byte[] header = Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n");
        byte[] pgm = new byte[header.Length + gray.Length];
        Buffer.BlockCopy(header, 0, pgm, 0, header.Length);
        Buffer.BlockCopy(gray, 0, pgm, header.Length, gray.Length);
        return pgm;
    }

    private static byte[] CompressBytes(ReadOnlySpan<byte> raw)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return ms.ToArray();
    }

    private static byte[] BuildHighRatioZlibPayload(int totalUncompressedBytes, string? prefixAscii, byte fillByte)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            int remaining = totalUncompressedBytes;
            if (!string.IsNullOrEmpty(prefixAscii))
            {
                byte[] prefix = Encoding.ASCII.GetBytes(prefixAscii);
                zlib.Write(prefix);
                remaining -= prefix.Length;
            }

            byte[] block = new byte[64 * 1024];
            Array.Fill(block, fillByte);
            while (remaining > 0)
            {
                int step = Math.Min(remaining, block.Length);
                zlib.Write(block, 0, step);
                remaining -= step;
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildZtxtPayload(string keyword, byte[] compressedText)
    {
        byte[] keyBytes = Encoding.Latin1.GetBytes(keyword);
        byte[] payload = new byte[keyBytes.Length + 2 + compressedText.Length];
        Buffer.BlockCopy(keyBytes, 0, payload, 0, keyBytes.Length);
        payload[keyBytes.Length] = 0; // null separator
        payload[keyBytes.Length + 1] = 0; // compression method 0 (deflate)
        Buffer.BlockCopy(compressedText, 0, payload, keyBytes.Length + 2, compressedText.Length);
        return payload;
    }

    private static byte[] BuildItxtPayload(string keyword, byte compressionFlag, byte[] textPayload)
    {
        byte[] keyBytes = Encoding.Latin1.GetBytes(keyword);
        // keyword + \0 + compressionFlag + compressionMethod(0) + languageTag("")\0 + translatedKeyword("")\0 + payload
        byte[] payload = new byte[keyBytes.Length + 5 + textPayload.Length];
        Buffer.BlockCopy(keyBytes, 0, payload, 0, keyBytes.Length);
        payload[keyBytes.Length] = 0;
        payload[keyBytes.Length + 1] = compressionFlag;
        payload[keyBytes.Length + 2] = 0;
        payload[keyBytes.Length + 3] = 0;
        payload[keyBytes.Length + 4] = 0;
        Buffer.BlockCopy(textPayload, 0, payload, keyBytes.Length + 5, textPayload.Length);
        return payload;
    }

    private static byte[] BuildGrayscaleIhdr(uint width, uint height)
    {
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8; // 8-bit grayscale, non-interlaced
        return ihdr;
    }

    private static byte[] BuildCustom1x1GrayscalePng(
        byte[] idatCompressed,
        IReadOnlyList<(string Type, byte[] Data)> extraChunks)
    {
        var chunks = new List<(string Type, byte[] Data)>(extraChunks.Count + 3)
        {
            ("IHDR", BuildGrayscaleIhdr(1, 1)),
        };
        chunks.AddRange(extraChunks);
        chunks.Add(("IDAT", idatCompressed));
        chunks.Add(("IEND", []));
        return BuildPngWithRawChunkList(chunks);
    }

    private static byte[] BuildPngWithRawChunkList(IReadOnlyList<(string Type, byte[] Data)> chunks)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[8];
        Span<byte> trailer = stackalloc byte[4];

        foreach (var (type, data) in chunks)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)data.Length);
            Encoding.ASCII.GetBytes(type, header.Slice(4, 4));
            ms.Write(header);
            if (data.Length > 0)
            {
                ms.Write(data);
            }

            uint crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8; i++)
            {
                crc = StepPngCrc(crc, header[i]);
            }

            foreach (byte b in data)
            {
                crc = StepPngCrc(crc, b);
            }

            BinaryPrimitives.WriteUInt32BigEndian(trailer, ~crc);
            ms.Write(trailer);
        }

        return ms.ToArray();
    }

    private static uint StepPngCrc(uint state, byte next)
    {
        uint v = state ^ next;
        for (int bit = 0; bit < 8; bit++)
        {
            v = (v & 1u) != 0 ? (v >> 1) ^ 0xEDB88320u : v >> 1;
        }

        return v;
    }
}
