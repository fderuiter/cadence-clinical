using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Apps.Etmf.Domain;

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

    public static byte[] CreateZipArchive(
        string exportId,
        string studyId,
        IReadOnlyList<TmfDocument> documents,
        DateTimeOffset exportedAt,
        Action<int, int>? progressCallback = null)
    {
        using var memoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestItems = new List<ExportManifestItem>();
            int processed = 0;

            for (int i = 0; i < documents.Count; i++)
            {
                var doc = documents[i];
                var item = AddDocumentToZip(zipArchive, doc);
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

    private static ExportManifestItem AddDocumentToZip(ZipArchive zipArchive, TmfDocument doc)
    {
        var relativePath = $"Zone_{doc.Zone:D2}/Section_{doc.Section}/{doc.Filename}";
        var entry = zipArchive.CreateEntry(relativePath, CompressionLevel.Optimal);
        var contentBytes = Encoding.UTF8.GetBytes(doc.Content ?? string.Empty);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(contentBytes));

        using var entryStream = entry.Open();
        entryStream.Write(contentBytes, 0, contentBytes.Length);

        return new ExportManifestItem(
            document_id: doc.Id,
            filename: doc.Filename,
            zone: doc.Zone,
            section: doc.Section,
            artifact_code: doc.ArtifactCode,
            artifact_type: doc.ArtifactType,
            version_number: doc.VersionIndex,
            sha256: sha256,
            path: relativePath
        );
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
