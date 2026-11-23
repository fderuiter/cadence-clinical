using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Storage;

namespace Cadence.Apps.Etmf.Services;

public static class EtmfExportPackager
{
    public record ExportManifestItem(
        string document_id,
        string filename,
        int zone,
        string section,
        string artifact_code,
        string artifact_type,
        long version_number,
        string sha256,
        string path
    );

    public record ExportManifestRoot(
        string export_id,
        string study_id,
        string exported_at,
        int total_documents,
        List<ExportManifestItem> documents
    );

    private static readonly string BinderVerificationRoot = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "cadence_etmf_binder_root"));

    public static string SanitizeKeySegment(string? raw, string fallback) =>
        TmfDocument.SanitizePathSegment(raw, fallback);

    public static string BuildDocumentObjectKey(string studyId, string documentId, string filename) =>
        $"documents/{SanitizeKeySegment(studyId, "study")}/{SanitizeKeySegment(documentId, "doc")}/{SanitizeKeySegment(filename, "document.bin")}";

    public static string BuildExportObjectKey(string studyId, string jobId) =>
        $"exports/{SanitizeKeySegment(studyId, "study")}/{SanitizeKeySegment(jobId, "job")}.zip";

    public static byte[] CreateZipArchive(
        string exportId,
        string studyId,
        IReadOnlyList<TmfDocument> documents,
        DateTimeOffset exportedAt,
        Action<int, int>? progressCallback = null)
    {
        return CreateZipArchiveCore(
            exportId,
            studyId,
            documents.Select(d => (d, Encoding.UTF8.GetBytes(d.Content ?? string.Empty))).ToList(),
            exportedAt,
            progressCallback);
    }

    public static async Task<byte[]> CreateZipArchiveAsync(
        string exportId,
        string studyId,
        IReadOnlyList<TmfDocument> documents,
        IStoragePort<byte[]>? storage,
        DateTimeOffset exportedAt,
        Action<int, int>? progressCallback = null,
        CancellationToken ct = default)
    {
        var resolved = new List<(TmfDocument Doc, byte[] Bytes)>(documents.Count);
        foreach (var doc in documents)
        {
            byte[]? bytes = null;
            if (storage is not null && !string.IsNullOrWhiteSpace(doc.ObjectKey))
            {
                try
                {
                    (bytes, _) = await storage.GetObjectAsync(doc.ObjectKey, ct);
                }
                catch (StorageObjectNotFoundException)
                {
                    bytes = null;
                }
            }

            bytes ??= Encoding.UTF8.GetBytes(doc.Content ?? string.Empty);
            resolved.Add((doc, bytes));
        }

        return CreateZipArchiveCore(exportId, studyId, resolved, exportedAt, progressCallback);
    }

    private static byte[] CreateZipArchiveCore(
        string exportId,
        string studyId,
        IReadOnlyList<(TmfDocument Doc, byte[] Bytes)> documents,
        DateTimeOffset exportedAt,
        Action<int, int>? progressCallback)
    {
        using var memoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestItems = new List<ExportManifestItem>();
            var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int processed = 0;

            for (int i = 0; i < documents.Count; i++)
            {
                var (doc, contentBytes) = documents[i];
                var item = AddDocumentToZip(zipArchive, doc, contentBytes, usedEntryPaths);
                manifestItems.Add(item);

                processed++;
                progressCallback?.Invoke(processed, documents.Count);
            }

            var manifestRoot = new ExportManifestRoot(
                export_id: exportId,
                study_id: studyId,
                exported_at: exportedAt.ToString("o"),
                total_documents: manifestItems.Count,
                documents: manifestItems
            );

            AddManifestJsonToZip(zipArchive, manifestRoot);
            AddManifestCsvToZip(zipArchive, manifestItems);
        }

        return memoryStream.ToArray();
    }

    private static ExportManifestItem AddDocumentToZip(
        ZipArchive zipArchive,
        TmfDocument doc,
        byte[] contentBytes,
        HashSet<string> usedEntryPaths)
    {
        int safeZone = doc.Zone > 0 ? doc.Zone : 1;
        string safeSection = TmfDocument.SanitizePathSegment(doc.Section, "01.01");
        string sectionRelativeDir = $"Zone_{safeZone:D2}/Section_{safeSection}";

        string baseFilename = TmfDocument.SanitizePathSegment(doc.Filename, "document.bin");
        string safeFilename = ResolveUniqueFilename(sectionRelativeDir, baseFilename, doc.Id, usedEntryPaths);
        string relativePath = $"{sectionRelativeDir}/{safeFilename}";

        EnsureConfinedEntryPath(sectionRelativeDir, relativePath);

        var entry = zipArchive.CreateEntry(relativePath, CompressionLevel.Optimal);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(contentBytes));

        using var entryStream = entry.Open();
        entryStream.Write(contentBytes, 0, contentBytes.Length);

        return new ExportManifestItem(
            document_id: doc.Id,
            filename: safeFilename,
            zone: safeZone,
            section: safeSection,
            artifact_code: doc.ArtifactCode,
            artifact_type: doc.ArtifactType,
            version_number: doc.VersionIndex,
            sha256: sha256,
            path: relativePath
        );
    }

    private static string ResolveUniqueFilename(
        string sectionRelativeDir,
        string baseFilename,
        string? documentId,
        HashSet<string> usedEntryPaths)
    {
        string initialPath = $"{sectionRelativeDir}/{baseFilename}";
        if (usedEntryPaths.Add(initialPath))
        {
            return baseFilename;
        }

        string safeDocId = TmfDocument.SanitizePathSegment(documentId, "doc");
        string ext = Path.GetExtension(baseFilename);
        string stem = Path.GetFileNameWithoutExtension(baseFilename);

        string candidate = BuildDisambiguatedFilename(stem, ext, baseFilename, safeDocId, suffix: null);
        int suffix = 2;
        while (!usedEntryPaths.Add($"{sectionRelativeDir}/{candidate}"))
        {
            candidate = BuildDisambiguatedFilename(stem, ext, baseFilename, safeDocId, suffix);
            suffix++;
        }

        return candidate;
    }

    private static string BuildDisambiguatedFilename(
        string stem,
        string ext,
        string baseFilename,
        string safeDocId,
        int? suffix)
    {
        string tag = suffix.HasValue ? $"_{safeDocId}_{suffix.Value}" : $"_{safeDocId}";
        bool hasBoundedExt = ext.Length is > 0 and <= 16 && stem.Length > 0;
        string effectiveExt = hasBoundedExt ? ext : string.Empty;
        string effectiveStem = hasBoundedExt ? stem : baseFilename;

        int maxStemLength = Math.Max(1, TmfDocument.MaxPathSegmentLength - tag.Length - effectiveExt.Length);
        if (effectiveStem.Length > maxStemLength)
        {
            effectiveStem = effectiveStem[..maxStemLength].TrimEnd('.');
            if (effectiveStem.Length == 0)
            {
                effectiveStem = "doc";
            }
        }

        string combined = $"{effectiveStem}{tag}{effectiveExt}";
        return TmfDocument.SanitizePathSegment(combined, "document.bin");
    }

    private static void EnsureConfinedEntryPath(string sectionRelativeDir, string relativePath)
    {
        if (string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, "manifest.csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Export entry '{relativePath}' collides with a reserved root manifest.");
        }

        string rootWithSep = BinderVerificationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string sectionFullDir = Path.GetFullPath(Path.Combine(BinderVerificationRoot, sectionRelativeDir));
        if (!sectionFullDir.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Export section directory '{sectionRelativeDir}' escapes binder root.");
        }

        string sectionWithSep = sectionFullDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string entryFullPath = Path.GetFullPath(Path.Combine(BinderVerificationRoot, relativePath));
        string manifestJsonFullPath = Path.GetFullPath(Path.Combine(BinderVerificationRoot, "manifest.json"));
        string manifestCsvFullPath = Path.GetFullPath(Path.Combine(BinderVerificationRoot, "manifest.csv"));

        if (string.Equals(entryFullPath, manifestJsonFullPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entryFullPath, manifestCsvFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Export entry '{relativePath}' resolves to a reserved root manifest.");
        }

        if (!entryFullPath.StartsWith(sectionWithSep, StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(entryFullPath), sectionFullDir, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Export entry '{relativePath}' escapes section directory '{sectionRelativeDir}'.");
        }
    }

    private static void AddManifestJsonToZip(ZipArchive zipArchive, ExportManifestRoot manifestRoot)
    {
        var jsonEntry = zipArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var jsonWriter = new StreamWriter(jsonEntry.Open(), Encoding.UTF8);
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonWriter.Write(JsonSerializer.Serialize(manifestRoot, jsonOptions));
    }

    private static void AddManifestCsvToZip(ZipArchive zipArchive, List<ExportManifestItem> manifestItems)
    {
        var csvEntry = zipArchive.CreateEntry("manifest.csv", CompressionLevel.Optimal);
        using var csvWriter = new StreamWriter(csvEntry.Open(), Encoding.UTF8);
        csvWriter.WriteLine("document_id,filename,zone,section,artifact_code,artifact_type,version_number,sha256,path");
        foreach (var item in manifestItems)
        {
            csvWriter.WriteLine(
                $"\"{EscapeCsv(item.document_id)}\",\"" +
                $"{EscapeCsv(item.filename)}\"," +
                $"{item.zone},\"" +
                $"{EscapeCsv(item.section)}\",\"" +
                $"{EscapeCsv(item.artifact_code)}\",\"" +
                $"{EscapeCsv(item.artifact_type)}\"," +
                $"{item.version_number},\"" +
                $"{item.sha256}\",\"" +
                $"{EscapeCsv(item.path)}\""
            );
        }
    }

    private static string EscapeCsv(string val) => (val ?? string.Empty).Replace("\"", "\"\"");
}
