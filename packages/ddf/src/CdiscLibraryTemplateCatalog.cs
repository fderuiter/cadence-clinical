using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

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

    /// <summary>
    /// Synthesizes starter collection-stage edit-check rules from a bundled CDISC Library eCRF template's bounded items
    /// (mandatory flags, enumerated codelists, character length limits, and numeric digit bounds) citing public CDISC
    /// template metadata (K-RULE-2).
    /// </summary>
    public IReadOnlyList<Rule> SynthesizeStarterRules(string templateId, IReadOnlyList<CollectionSpec> importedSpecs)
    {
        ArgumentNullException.ThrowIfNull(importedSpecs);
        if (string.IsNullOrWhiteSpace(templateId)
            || importedSpecs.Count == 0
            || !_catalog.Value.FilesById.TryGetValue(templateId.Trim(), out var filePath)
            || !File.Exists(filePath))
        {
            return [];
        }

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
                return [];
            }

            return BuildTemplateStarterRules(mdv, importedSpecs);
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return [];
        }
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

            return new BundledProtocolTemplate(slug, category, formOid, formName, Path.GetFileName(filePath), itemCount);
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

    private static List<Rule> BuildTemplateStarterRules(XElement mdv, IReadOnlyList<CollectionSpec> importedSpecs)
    {
        var forms = mdv.Elements().Where(e => e.Name.LocalName == "FormDef").ToList();
        var itemGroups = mdv.Elements()
            .Where(e => e.Name.LocalName == "ItemGroupDef" && !string.IsNullOrWhiteSpace(e.Attribute("OID")?.Value))
            .ToDictionary(e => e.Attribute("OID")!.Value.Trim(), StringComparer.Ordinal);
        var itemDefs = mdv.Elements()
            .Where(e => e.Name.LocalName == "ItemDef" && !string.IsNullOrWhiteSpace(e.Attribute("OID")?.Value))
            .ToDictionary(e => e.Attribute("OID")!.Value.Trim(), StringComparer.Ordinal);
        var codeLists = mdv.Elements()
            .Where(e => e.Name.LocalName == "CodeList" && !string.IsNullOrWhiteSpace(e.Attribute("OID")?.Value))
            .ToDictionary(
                cl => cl.Attribute("OID")!.Value.Trim(),
                cl => cl.Elements()
                    .Where(i => i.Name.LocalName is "CodeListItem" or "EnumeratedItem")
                    .Select(i => i.Attribute("CodedValue")?.Value?.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        var rules = new List<Rule>();
        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);

        int formCount = Math.Min(forms.Count, importedSpecs.Count);
        for (int f = 0; f < formCount; f++)
        {
            var formDef = forms[f];
            var spec = importedSpecs[f];
            var formOid = formDef.Attribute("OID")?.Value?.Trim() ?? spec.Id;
            var citationDoc = Truncate($"CDISC Library eCRF Template ({spec.Name})", RulePackJson.MaxTextLength);

            var orderedItemDefs = ResolveOrderedFormItems(formDef, itemGroups, itemDefs);
            int itemCount = Math.Min(orderedItemDefs.Count, spec.Items.Count);
            for (int i = 0; i < itemCount; i++)
            {
                var (itemOid, itemDef) = orderedItemDefs[i];
                var item = spec.Items[i];
                var label = string.IsNullOrWhiteSpace(item.Prompt) ? item.Id : item.Prompt;
                var variable = new VariableExpression(item.Id);

                if (item.Required)
                {
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"REQ.{spec.Id}.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, $"{formOid}/{itemOid}", null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        new FunctionExpression(RuleFunction.IsEmpty, [variable]),
                        Truncate($"Required item '{label}' is missing on '{spec.Name}'.", RulePackJson.MaxTextLength),
                        "Enter the required clinical value or record why it was not collected.",
                        EffectivePeriod.Always));
                }

                var codeListOid = itemDef.Elements().FirstOrDefault(e => e.Name.LocalName == "CodeListRef")?.Attribute("CodeListOID")?.Value?.Trim();
                bool hasCodeListRule = false;
                if (!string.IsNullOrWhiteSpace(codeListOid)
                    && codeLists.TryGetValue(codeListOid, out var allowedCodes)
                    && allowedCodes.Count is > 0 and <= 100)
                {
                    hasCodeListRule = true;
                    var sample = allowedCodes.Count <= 5
                        ? string.Join(", ", allowedCodes)
                        : $"{string.Join(", ", allowedCodes.Take(5))}, +{allowedCodes.Count - 5} more";
                    var condition = new LogicalExpression(
                        LogicalOperator.And,
                        [
                            new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                            new FunctionExpression(
                                RuleFunction.NotIn,
                                [variable, .. allowedCodes.Select(c => (RuleExpression)new LiteralExpression(RuleValue.FromText(c)))]),
                        ]);
                    rules.Add(new Rule(
                        OdmDesignImporter.AllocateRuleId($"CL.{spec.Id}.{item.Id}", usedRuleIds),
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(citationDoc, $"{formOid}/{itemOid}/{codeListOid}", null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                        ValidationSeverity.Warning,
                        condition,
                        Truncate($"Item '{label}' must use a permitted CDISC codelist value ({sample}).", RulePackJson.MaxTextLength),
                        "Select a coded value from the form's controlled terminology codelist.",
                        EffectivePeriod.Always));
                }

                if (int.TryParse(itemDef.Attribute("Length")?.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxLen)
                    && maxLen > 0)
                {
                    if (item.ValueKind == ObservationValueKind.Quantity && maxLen <= 12)
                    {
                        decimal upperBound = 1m;
                        for (int d = 0; d < maxLen; d++)
                        {
                            upperBound *= 10m;
                        }

                        upperBound -= 1m;
                        rules.Add(new Rule(
                            OdmDesignImporter.AllocateRuleId($"MAX.{spec.Id}.{item.Id}", usedRuleIds),
                            1,
                            RuleAuthority.Study,
                            new SourceCitation(citationDoc, $"{formOid}/{itemOid}", null),
                            new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                            ValidationSeverity.Warning,
                            new ComparisonExpression(ComparisonOperator.Greater, variable, new LiteralExpression(RuleValue.FromNumber(upperBound))),
                            Truncate($"Numeric item '{label}' exceeds the {maxLen}-digit template bound ({upperBound.ToString(CultureInfo.InvariantCulture)}).", RulePackJson.MaxTextLength),
                            "Verify the source numeric measurement and unit.",
                            EffectivePeriod.Always));
                    }
                    else if (!hasCodeListRule && item.ValueKind == ObservationValueKind.Text && maxLen <= 2000)
                    {
                        rules.Add(new Rule(
                            OdmDesignImporter.AllocateRuleId($"LEN.{spec.Id}.{item.Id}", usedRuleIds),
                            1,
                            RuleAuthority.Study,
                            new SourceCitation(citationDoc, $"{formOid}/{itemOid}", null),
                            new RuleApplicability(ValidationStage.Collection, null, null, null, spec.Id),
                            ValidationSeverity.Notice,
                            new ComparisonExpression(
                                ComparisonOperator.Greater,
                                new FunctionExpression(RuleFunction.Length, [variable]),
                                new LiteralExpression(RuleValue.FromNumber(maxLen))),
                            Truncate($"Item '{label}' exceeds the {maxLen}-character template field length.", RulePackJson.MaxTextLength),
                            "Shorten the text entry to fit the template's maximum character length.",
                            EffectivePeriod.Always));
                    }
                }
            }
        }

        return rules;
    }

    private static List<(string ItemOid, XElement ItemDef)> ResolveOrderedFormItems(
        XElement formDef,
        Dictionary<string, XElement> itemGroups,
        Dictionary<string, XElement> itemDefs)
    {
        var list = new List<(string ItemOid, XElement ItemDef)>();
        var orderedGroupRefs = OrderElements(formDef.Elements().Where(e => e.Name.LocalName == "ItemGroupRef"));
        foreach (var igRef in orderedGroupRefs)
        {
            var igOid = igRef.Attribute("ItemGroupOID")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(igOid) || !itemGroups.TryGetValue(igOid, out var igDef))
            {
                continue;
            }

            foreach (var itemRef in OrderElements(igDef.Elements().Where(e => e.Name.LocalName == "ItemRef")))
            {
                var itemOid = itemRef.Attribute("ItemOID")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(itemOid) && itemDefs.TryGetValue(itemOid, out var itemDef))
                {
                    list.Add((itemOid, itemDef));
                }
            }
        }

        return list;
    }

    private static IEnumerable<XElement> OrderElements(IEnumerable<XElement> elements) =>
        elements
            .Select((el, idx) => (
                Element: el,
                Order: int.TryParse(el.Attribute("OrderNumber")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue,
                Index: idx))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Index)
            .Select(x => x.Element);

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private sealed record LoadedCatalog(
        IReadOnlyList<BundledProtocolTemplate> Summaries,
        Dictionary<string, string> FilesById);
}
