using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Ddf;

internal static partial class OdmXmlNavigationAndVendorNotes
{
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private static readonly FrozenDictionary<string, FrozenSet<string>> KnownElementAttributes =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["ODM"] = FrozenSet.Create(StringComparer.Ordinal, "FileOID", "FileType", "Description", "AsOfDateTime", "CreationDateTime", "ODMVersion", "Originator", "SourceSystem", "SourceSystemVersion", "ID", "Archival", "PriorFileOID", "Granularity"),
            ["Study"] = FrozenSet.Create(StringComparer.Ordinal, "OID"),
            ["GlobalVariables"] = FrozenSet<string>.Empty,
            ["StudyName"] = FrozenSet<string>.Empty,
            ["StudyDescription"] = FrozenSet<string>.Empty,
            ["ProtocolName"] = FrozenSet<string>.Empty,
            ["BasicDefinitions"] = FrozenSet<string>.Empty,
            ["MeasurementUnit"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name"),
            ["Symbol"] = FrozenSet<string>.Empty,
            ["MetaDataVersion"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "Description", "Include"),
            ["Protocol"] = FrozenSet<string>.Empty,
            ["StudyEventRef"] = FrozenSet.Create(StringComparer.Ordinal, "StudyEventOID", "OrderNumber", "Mandatory", "CollectionExceptionConditionOID"),
            ["StudyEventDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "Repeating", "Type", "Category"),
            ["FormRef"] = FrozenSet.Create(StringComparer.Ordinal, "FormOID", "OrderNumber", "Mandatory", "CollectionExceptionConditionOID"),
            ["FormDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "Repeating"),
            ["ItemGroupRef"] = FrozenSet.Create(StringComparer.Ordinal, "ItemGroupOID", "OrderNumber", "Mandatory", "CollectionExceptionConditionOID"),
            ["ItemGroupDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "Repeating", "IsReferenceData", "SASDatasetName", "Domain", "Origin", "Role", "Purpose", "Comment"),
            ["ItemRef"] = FrozenSet.Create(StringComparer.Ordinal, "ItemOID", "OrderNumber", "Mandatory", "KeySequence", "MethodOID", "ImputationMethodOID", "Role", "RoleCodeListOID", "CollectionExceptionConditionOID"),
            ["ItemDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "DataType", "Length", "SignificantDigits", "SASFieldName", "SDSVarName", "Origin", "Comment"),
            ["Description"] = FrozenSet<string>.Empty,
            ["Question"] = FrozenSet<string>.Empty,
            ["TranslatedText"] = FrozenSet.Create(StringComparer.Ordinal, "lang"),
            ["CodeListRef"] = FrozenSet.Create(StringComparer.Ordinal, "CodeListOID"),
            ["CodeList"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "DataType", "SASFormatName"),
            ["CodeListItem"] = FrozenSet.Create(StringComparer.Ordinal, "CodedValue", "Rank", "OrderNumber"),
            ["Decode"] = FrozenSet<string>.Empty,
            ["EnumeratedItem"] = FrozenSet.Create(StringComparer.Ordinal, "CodedValue", "Rank", "OrderNumber"),
            ["ExternalCodeList"] = FrozenSet.Create(StringComparer.Ordinal, "Dictionary", "Version", "ref", "href"),
            ["MeasurementUnitRef"] = FrozenSet.Create(StringComparer.Ordinal, "MeasurementUnitOID"),
            ["RangeCheck"] = FrozenSet.Create(StringComparer.Ordinal, "Comparator", "SoftHard"),
            ["CheckValue"] = FrozenSet<string>.Empty,
            ["FormalExpression"] = FrozenSet.Create(StringComparer.Ordinal, "Context"),
            ["ErrorMessage"] = FrozenSet<string>.Empty,
            ["Alias"] = FrozenSet.Create(StringComparer.Ordinal, "Context", "Name"),
            ["ConditionDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name"),
            ["MethodDef"] = FrozenSet.Create(StringComparer.Ordinal, "OID", "Name", "Type"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static Dictionary<string, XElement> ParseByOid(XElement parent, string elementName, List<string> problems)
    {
        var map = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var el in Children(parent, elementName))
        {
            var oid = Attr(el, "OID");
            if (string.IsNullOrWhiteSpace(oid))
            {
                problems.Add($"<{elementName}> element is missing its required OID attribute.");
                continue;
            }

            if (!map.TryAdd(oid, el))
            {
                problems.Add($"Duplicate <{elementName}> OID '{oid}' in <MetaDataVersion>.");
            }
        }

        return map;
    }

    public static List<XElement> OrderRefs(IEnumerable<XElement> refs, string ownerOid, string refElementName, List<string> problems)
    {
        var indexed = new List<(XElement Element, int Order, int DocumentIndex)>();
        int index = 0;
        foreach (var el in refs)
        {
            var orderAttr = Attr(el, "OrderNumber");
            int order = int.MaxValue;
            if (!string.IsNullOrWhiteSpace(orderAttr)
                && !int.TryParse(orderAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out order))
            {
                problems.Add($"{ownerOid}: <{refElementName}> has non-integer OrderNumber '{orderAttr}'.");
                order = int.MaxValue;
            }

            indexed.Add((el, order, index++));
        }

        return [.. indexed.OrderBy(x => x.Order).ThenBy(x => x.DocumentIndex).Select(x => x.Element)];
    }

    public static string? ValidateYesNoAttribute(XElement element, string attributeName, string context, List<string> problems)
    {
        var value = Attr(element, attributeName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "No", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{context}: attribute {attributeName} must be 'Yes' or 'No', found '{value}'.");
        }

        return value;
    }

    public static void CollectVendorAndExtensionNotes(XElement root, string odmNamespace, List<ProtocolImportNote> notes)
    {
        foreach (var provenanceAttr in (string[])["Originator", "SourceSystem", "SourceSystemVersion"])
        {
            if (Attr(root, provenanceAttr) is { } val && !string.IsNullOrWhiteSpace(val))
            {
                notes.Add(new ProtocolImportNote("ODM", $"Optional ODM vendor provenance {provenanceAttr}='{val}' recorded in import summary."));
            }
        }

        foreach (var el in root.DescendantsAndSelf())
        {
            var elNs = el.Name.NamespaceName;
            var owner = Attr(el, "OID") ?? Attr(el, "ItemOID") ?? Attr(el, "ItemGroupOID") ?? el.Name.LocalName;

            if (!string.IsNullOrEmpty(elNs) && !string.Equals(elNs, odmNamespace, StringComparison.Ordinal))
            {
                notes.Add(new ProtocolImportNote(
                    owner,
                    $"Optional vendor extension element <{el.Name.LocalName}> ({elNs}) is not part of core protocol design and was noted."));
                continue;
            }

            var knownAttrs = KnownElementAttributes.GetValueOrDefault(el.Name.LocalName);
            foreach (var attr in el.Attributes())
            {
                if (attr.IsNamespaceDeclaration)
                {
                    continue;
                }

                var attrNs = attr.Name.NamespaceName;
                if (!string.IsNullOrEmpty(attrNs) && !string.Equals(attrNs, XmlNamespace, StringComparison.Ordinal) && !string.Equals(attrNs, XmlnsNamespace, StringComparison.Ordinal))
                {
                    notes.Add(new ProtocolImportNote(
                        owner,
                        $"Optional vendor attribute '{attr.Name.LocalName}' ({attrNs})='{attr.Value}' on <{el.Name.LocalName}> was recorded as an import note."));
                }
                else if (string.IsNullOrEmpty(attrNs) && knownAttrs is not null && !knownAttrs.Contains(attr.Name.LocalName))
                {
                    notes.Add(new ProtocolImportNote(
                        owner,
                        $"Optional attribute '{attr.Name.LocalName}'='{attr.Value}' on <{el.Name.LocalName}> is outside core protocol design fields and was recorded as an import note."));
                }
            }
        }
    }

    public static List<ProtocolImportNote> DeduplicateNotes(List<ProtocolImportNote> notes) =>
        [.. notes.Distinct()];

    public static IEnumerable<XElement> Children(XElement? parent, string localName) =>
        parent?.Elements().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal)) ?? [];

    public static XElement? Child(XElement? parent, string localName) =>
        Children(parent, localName).FirstOrDefault();

    public static string? Attr(XElement? element, string localName) =>
        element?.Attributes().FirstOrDefault(a => string.IsNullOrEmpty(a.Name.NamespaceName) && string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal))?.Value?.Trim();

    public static string? TextOf(XElement? parent, string childLocalName)
    {
        var value = Child(parent, childLocalName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeWhitespace(value);
    }

    public static string? TranslatedText(XElement? container)
    {
        if (container is null)
        {
            return null;
        }

        var texts = Children(container, "TranslatedText").ToList();
        if (texts.Count == 0)
        {
            return string.IsNullOrWhiteSpace(container.Value) ? null : NormalizeWhitespace(container.Value);
        }

        var english = texts.FirstOrDefault(t =>
        {
            var lang = t.Attribute(XNamespace.Xml + "lang")?.Value ?? Attr(t, "lang");
            return string.IsNullOrWhiteSpace(lang) || lang.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        });
        var chosen = (english ?? texts[0]).Value;
        return string.IsNullOrWhiteSpace(chosen) ? null : NormalizeWhitespace(chosen);
    }

    public static string NormalizeAliasContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return string.Empty;
        }

        return context.Trim().Trim('(', ')', '[', ']', ' ');
    }

    public static string NormalizeWhitespace(string value) => WhitespaceRegex().Replace(value.Trim(), " ");

    public static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    public static string TruncateText(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();
}
