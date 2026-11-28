using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;

using static Cadence.Kernel.Api.KernelExportFormatting;

namespace Cadence.Kernel.Api;

internal static class KernelStudyBundleZipWriter
{
    public static async Task<KernelHiddenContentResponse> WriteStudyBundleZipAsync(
        Stream destination,
        ProjectedStudyExport export,
        StudyExportFormat format,
        string requestedBy,
        IReadOnlyList<KernelCasebookResponse> archiveCasebooks,
        CancellationToken cancellationToken)
    {
        var entryTimestamp = NormalizeZipTimestamp(export.GeneratedAt);
        var manifestFiles = new List<BundleManifestFileEntry>();
        var manifestCasebooks = new List<BundleManifestCasebookEntry>();
        var combinedHiddenReasons = new HashSet<string>(export.Hidden.Reasons, StringComparer.Ordinal);

        var orderedHiddenReasons = new List<string>();
        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            await AddZipEntryAsync(
                archive,
                "data/observations.csv",
                "text/csv",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => KernelCsvExportWriter.WriteStudyCsvAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            await AddZipEntryAsync(
                archive,
                "data/dataset.json",
                "application/json",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => KernelDatasetJsonExportWriter.WriteStudyDatasetJsonAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            await AddZipEntryAsync(
                archive,
                "metadata/study.odm.xml",
                "application/xml",
                entryTimestamp,
                manifestFiles,
                (stream, ct) => KernelStudyOdmXmlExportWriter.WriteStudyOdmXmlAsync(stream, export, ct),
                cancellationToken).ConfigureAwait(false);

            var collectionGroups = KernelStatisticalSyntaxWriter.BuildCollectionTables(export);
            foreach (var table in collectionGroups)
            {
                var tablePath = $"data/collection_{table.TableName}.csv";
                var tableExport = export with { Rows = table.Rows };
                await AddZipEntryAsync(
                    archive,
                    tablePath,
                    "text/csv",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => KernelCsvExportWriter.WriteStudyCsvAsync(stream, tableExport, ct),
                    cancellationToken).ConfigureAwait(false);
            }

            if (format is StudyExportFormat.SasBundle or StudyExportFormat.StudyArchive)
            {
                var sasScript = KernelStatisticalSyntaxWriter.BuildSasSyntaxScript(export, collectionGroups);
                await AddZipUtf8EntryAsync(
                    archive,
                    "syntax/load_study.sas",
                    "text/x-sas",
                    sasScript,
                    entryTimestamp,
                    manifestFiles,
                    cancellationToken).ConfigureAwait(false);
            }

            if (format is StudyExportFormat.RBundle or StudyExportFormat.StudyArchive)
            {
                var rScript = KernelStatisticalSyntaxWriter.BuildRSyntaxScript(export, collectionGroups);
                await AddZipUtf8EntryAsync(
                    archive,
                    "syntax/load_study.R",
                    "text/x-r",
                    rScript,
                    entryTimestamp,
                    manifestFiles,
                    cancellationToken).ConfigureAwait(false);
            }

            if (format == StudyExportFormat.StudyArchive)
            {
                var maskedLocks = KernelStudyBundleMaskingBuilder.BuildMaskedStudyLocks(export);
                combinedHiddenReasons.UnionWith(maskedLocks.Hidden.Reasons);
                await AddZipEntryAsync(
                    archive,
                    "locks/locks.json",
                    "application/json",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => JsonSerializer.SerializeAsync(stream, maskedLocks, CasebookJsonOptions, ct),
                    cancellationToken).ConfigureAwait(false);

                var (auditEntries, hiddenAuditReasons) = KernelStudyBundleMaskingBuilder.BuildMaskedAuditTrail(export);
                foreach (var reason in hiddenAuditReasons)
                {
                    combinedHiddenReasons.Add(reason.ToString());
                }

                var auditPayload = new BundleAuditTrailPayload(
                    Hidden: KernelHiddenContentResponse.FromReasons(hiddenAuditReasons),
                    Entries: auditEntries);

                await AddZipEntryAsync(
                    archive,
                    "audit/audit_trail.json",
                    "application/json",
                    entryTimestamp,
                    manifestFiles,
                    (stream, ct) => JsonSerializer.SerializeAsync(stream, auditPayload, CasebookJsonOptions, ct),
                    cancellationToken).ConfigureAwait(false);

                var usedSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var maskedCounter = 0;
                foreach (var casebook in archiveCasebooks)
                {
                    string slug;
                    if (casebook.Masked)
                    {
                        maskedCounter++;
                        slug = $"masked-{maskedCounter.ToString("D3", CultureInfo.InvariantCulture)}";
                    }
                    else
                    {
                        var baseSlug = $"{SanitizeBundleToken(casebook.SiteCode)}-{SanitizeBundleToken(casebook.SubjectCode)}";
                        slug = baseSlug;
                        var suffix = 2;
                        while (!usedSlugs.Add(slug))
                        {
                            slug = $"{baseSlug}-{suffix.ToString(CultureInfo.InvariantCulture)}";
                            suffix++;
                        }
                    }

                    var pdfPath = $"casebooks/{slug}.casebook.pdf";
                    var odmPath = $"casebooks/{slug}.casebook.odm.xml";

                    var pdfDigest = await AddZipEntryAsync(
                        archive,
                        pdfPath,
                        "application/pdf",
                        entryTimestamp,
                        manifestFiles,
                        (stream, ct) => KernelCasebookPdfExportWriter.WriteCasebookPdfAsync(stream, casebook, ct),
                        cancellationToken).ConfigureAwait(false);

                    var odmDigest = await AddZipEntryAsync(
                        archive,
                        odmPath,
                        "application/xml",
                        entryTimestamp,
                        manifestFiles,
                        (stream, ct) => KernelCasebookOdmXmlExportWriter.WriteCasebookOdmXmlAsync(stream, casebook, ct),
                        cancellationToken).ConfigureAwait(false);

                    manifestCasebooks.Add(new BundleManifestCasebookEntry(
                        EnrollmentRef: casebook.EnrollmentRef,
                        SiteCode: casebook.SiteCode,
                        SubjectCode: casebook.SubjectCode,
                        Masked: casebook.Masked,
                        Mask: casebook.Mask,
                        CasebookDigestSha256: casebook.CasebookDigestSha256,
                        PdfPath: pdfPath,
                        PdfSha256: pdfDigest,
                        OdmXmlPath: odmPath,
                        OdmXmlSha256: odmDigest));
                }
            }

            var formatCode = format switch
            {
                StudyExportFormat.SasBundle => "sas_bundle",
                StudyExportFormat.RBundle => "r_bundle",
                _ => "study_archive",
            };

            orderedHiddenReasons.AddRange(combinedHiddenReasons.OrderBy(r => r, StringComparer.Ordinal));
            var manifestHidden = orderedHiddenReasons.Count == 0
                ? KernelHiddenContentResponse.None
                : new KernelHiddenContentResponse(true, orderedHiddenReasons);

            var manifest = new StudyExportBundleManifest(
                Producer: ProducerName,
                Format: formatCode,
                StudyRef: NodeRef.For(export.Snapshot.StudyId).ToString(),
                StudyCode: export.Snapshot.StudyCode,
                StudyTitle: export.Snapshot.StudyTitle,
                LockFilter: export.LockFilter,
                ValidAt: FormatInstant(export.Snapshot.ValidAt),
                KnownAt: FormatInstant(export.Snapshot.KnownAt),
                GeneratedAt: FormatInstant(export.GeneratedAt),
                RequestedBy: requestedBy,
                Hidden: manifestHidden,
                HiddenScopes: new BundleHiddenScopesView(manifestHidden.MoreExist, manifestHidden.Reasons),
                Files: manifestFiles,
                Casebooks: manifestCasebooks);

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = entryTimestamp;
            await using var manifestStream = await manifestEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await JsonSerializer.SerializeAsync(manifestStream, manifest, CasebookJsonOptions, cancellationToken).ConfigureAwait(false);
            await manifestStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        return orderedHiddenReasons.Count == 0
            ? KernelHiddenContentResponse.None
            : new KernelHiddenContentResponse(true, orderedHiddenReasons);
    }

    private static DateTimeOffset NormalizeZipTimestamp(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        var minZip = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var maxZip = new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero);
        if (utc < minZip)
        {
            return minZip;
        }

        return utc > maxZip ? maxZip : utc;
    }

    private static async Task<string> AddZipEntryAsync(
        ZipArchive archive,
        string entryPath,
        string contentType,
        DateTimeOffset entryTimestamp,
        List<BundleManifestFileEntry> manifestFiles,
        Func<Stream, CancellationToken, Task> writer,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await writer(buffer, cancellationToken).ConfigureAwait(false);
        var payload = buffer.ToArray();
        var digest = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        entry.LastWriteTime = entryTimestamp;
        await using (var entryStream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            await entryStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await entryStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        manifestFiles.Add(new BundleManifestFileEntry(
            Path: entryPath,
            ContentType: contentType,
            SizeBytes: payload.LongLength,
            Sha256: digest));
        return digest;
    }

    private static Task<string> AddZipUtf8EntryAsync(
        ZipArchive archive,
        string entryPath,
        string contentType,
        string content,
        DateTimeOffset entryTimestamp,
        List<BundleManifestFileEntry> manifestFiles,
        CancellationToken cancellationToken) =>
        AddZipEntryAsync(
            archive,
            entryPath,
            contentType,
            entryTimestamp,
            manifestFiles,
            async (stream, ct) =>
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            },
            cancellationToken);
}
