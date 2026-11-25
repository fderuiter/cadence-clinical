using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Protocol.Import;

namespace Cadence.Ddf;

/// <summary>
/// Discovers and serves the bundled CDISC Library ODM v1.3.2 eCRF templates shipped under
/// <c>docs/CDISC/Library/Data_Collection/eCRFs</c>.
/// </summary>
public sealed partial class CdiscLibraryTemplateCatalog
{
    private const string RelativeLibraryPath = "docs/CDISC/Library/Data_Collection/eCRFs";

    private readonly Lazy<LoadedCatalog> _catalog;

    /// <summary>Initializes a catalog that discovers bundled CDISC Library templates from <paramref name="libraryDirectory"/> or the repository root.</summary>
    public CdiscLibraryTemplateCatalog(string? libraryDirectory = null)
    {
        _catalog = new Lazy<LoadedCatalog>(() => Load(libraryDirectory));
    }

    /// <summary>Lists all bundled CDISC Library eCRF templates in deterministic category and form order.</summary>
    public IReadOnlyList<BundledProtocolTemplate> ListTemplates() => _catalog.Value.Summaries;

    /// <summary>Reads the raw ODM v1.3.2 XML bytes of a bundled template by its <paramref name="templateId"/>, or null when unknown.</summary>
    public byte[]? ReadTemplateBytes(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return null;
        }

        return _catalog.Value.FilesById.TryGetValue(templateId.Trim(), out var filePath) && File.Exists(filePath)
            ? File.ReadAllBytes(filePath)
            : null;
    }

    private static LoadedCatalog Load(string? explicitDirectory)
    {
        var root = ResolveDirectory(explicitDirectory);
        if (root is null || !Directory.Exists(root))
        {
            return new LoadedCatalog([], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var summaries = new List<BundledProtocolTemplate>();
        var filesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var candidateFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(IsOdmCandidateFile)
            .OrderBy(p => Path.GetRelativePath(root, p).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var filePath in candidateFiles)
        {
            if (TryInspectTemplate(root, filePath, filesById) is { } template)
            {
                summaries.Add(template);
                filesById[template.Id] = filePath;
            }
        }

        return new LoadedCatalog(summaries, filesById);
    }

    private static bool IsOdmCandidateFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            && !filePath.EndsWith("_XML.txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[256];
            using var stream = File.OpenRead(filePath);
            int read = stream.Read(buffer);
            var prefix = Encoding.UTF8.GetString(buffer[..read]);
            return prefix.Contains("<ODM", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static BundledProtocolTemplate? TryInspectTemplate(
        string root,
        string filePath,
        Dictionary<string, string> existingIds)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var stream = File.OpenRead(filePath);
            using var reader = XmlReader.Create(stream, settings);
            var doc = XDocument.Load(reader);
            var mdv = doc.Root?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "Study")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "MetaDataVersion");
            if (mdv is null)
            {
                return null;
            }

            var forms = mdv.Elements().Where(e => e.Name.LocalName == "FormDef").ToList();
            if (forms.Count == 0)
            {
                return null;
            }

            var igCounts = mdv.Elements()
                .Where(e => e.Name.LocalName == "ItemGroupDef")
                .ToDictionary(
                    ig => ig.Attribute("OID")?.Value ?? string.Empty,
                    ig => ig.Elements().Count(c => c.Name.LocalName == "ItemRef"),
                    StringComparer.Ordinal);

            int itemCount = forms
                .SelectMany(f => f.Elements().Where(e => e.Name.LocalName == "ItemGroupRef"))
                .Sum(igr => igCounts.GetValueOrDefault(igr.Attribute("ItemGroupOID")?.Value ?? string.Empty, 0));

            var primaryForm = forms[0];
            var formOid = primaryForm.Attribute("OID")?.Value?.Trim() ?? Path.GetFileNameWithoutExtension(filePath);
            var formName = primaryForm.Attribute("Name")?.Value?.Trim() ?? formOid;
            var repeating = forms.Any(f => string.Equals(f.Attribute("Repeating")?.Value?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase));
            var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');
            var parentFolder = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "General";
            var category = parentFolder.Replace('_', ' ');
            var baseSlug = Slugify(Path.ChangeExtension(relative, null));
            var slug = baseSlug;
            int suffix = 2;
            while (existingIds.ContainsKey(slug))
            {
                slug = $"{baseSlug}-{suffix}";
                suffix++;
            }

            return new BundledProtocolTemplate(slug, category, formOid, formName, Path.GetFileName(filePath), itemCount, repeating);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return null;
        }
    }

    private static string Slugify(string value) =>
        NonSlugRegex().Replace(value.ToLowerInvariant(), "-").Trim('-');

    private static string? ResolveDirectory(string? explicitDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return explicitDirectory;
        }

        string?[] starts = [Directory.GetCurrentDirectory(), AppContext.BaseDirectory];
        foreach (var start in starts.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var current = new DirectoryInfo(start!);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, RelativeLibraryPath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NonSlugRegex();

    private sealed record LoadedCatalog(
        IReadOnlyList<BundledProtocolTemplate> Summaries,
        Dictionary<string, string> FilesById);
}
