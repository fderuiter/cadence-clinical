using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Validation;
using Cadence.Rules.Packs;

using static Cadence.Ddf.OdmXmlNavigationAndVendorNotes;

namespace Cadence.Ddf;

/// <summary>Result of importing an ODM v1.3.2 XML document into collection specifications and import notes.</summary>
/// <param name="Title">Study title read from ODM <c>GlobalVariables</c>, when present.</param>
/// <param name="CollectionSpecs">Imported forms in ODM display order with ordered items, prompts, and mandatory flags.</param>
/// <param name="Problems">Structural or XML validation problems that prevented import.</param>
/// <param name="Notes">Readable notes describing unmapped terminology codes, optional vendor attributes, or approximated constructs.</param>
/// <param name="PayloadTooLarge">True when the XML payload exceeded <see cref="IProtocolDesignImporter.MaxPayloadBytes"/>.</param>
/// <param name="Rules">Compiled collection-stage edit-check rules extracted from ODM <c>RangeCheck</c> and <c>ConditionDef</c> elements.</param>
/// <param name="StudyOid">OID attribute on the imported ODM <c>Study</c> element, when present.</param>
/// <param name="Epochs">Ordered study epochs derived from ODM <c>StudyEventDef</c> categories, aliases, and schedule definitions.</param>
/// <param name="PlannedActivities">Ordered planned visit activities derived from ODM <c>Protocol</c>, <c>StudyEventRef</c>, <c>StudyEventDef</c>, and <c>FormRef</c> elements.</param>
public sealed record OdmImportResult(
    string? Title,
    IReadOnlyList<CollectionSpec> CollectionSpecs,
    IReadOnlyList<string> Problems,
    IReadOnlyList<ProtocolImportNote> Notes,
    bool PayloadTooLarge = false,
    IReadOnlyList<Rule>? Rules = null,
    string? StudyOid = null,
    IReadOnlyList<Epoch>? Epochs = null,
    IReadOnlyList<PlannedActivity>? PlannedActivities = null)
{
    /// <summary>True when one or more forms were imported without validation problems.</summary>
    public bool IsSuccess => Problems.Count == 0 && !PayloadTooLarge && CollectionSpecs.Count > 0;
}

/// <summary>
/// Imports CDISC Operational Data Model (ODM) v1.3.2 XML definitions into kernel <see cref="CollectionSpec"/> forms and
/// <see cref="CollectionItem"/> items while enforcing a 10 MB payload ceiling and prohibiting DTD / external XML entity
/// declarations.
/// </summary>
public static partial class OdmDesignImporter
{
    /// <summary>Standard XML namespace for CDISC ODM v1.3.x documents.</summary>
    public const string OdmNamespace = "http://www.cdisc.org/ns/odm/v1.3";

    /// <summary>Imports an ODM v1.3.2 XML string into collection specifications and notes.</summary>
    public static Task<OdmImportResult> ImportAsync(
        string odmXml,
        ProtocolConceptResolver resolveConcept,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(odmXml);
        var bytes = Encoding.UTF8.GetBytes(odmXml);
        return ImportAsync(bytes, resolveConcept, reservedElementIds, cancellationToken, resolveTerminology);
    }

    /// <summary>Imports raw UTF-8/XML bytes in ODM v1.3.2 format into collection specifications and notes.</summary>
    public static async Task<OdmImportResult> ImportAsync(
        byte[] xmlBytes,
        ProtocolConceptResolver resolveConcept,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        ArgumentNullException.ThrowIfNull(resolveConcept);

        if (xmlBytes.Length > IProtocolDesignImporter.MaxPayloadBytes)
        {
            return new OdmImportResult(
                null,
                [],
                [$"Uploaded XML payload ({xmlBytes.Length:N0} bytes) exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit."],
                [],
                PayloadTooLarge: true);
        }

        if (xmlBytes.Length == 0)
        {
            return new OdmImportResult(null, [], ["Uploaded XML file is empty."], []);
        }

        XDocument document;
        try
        {
            using var stream = new MemoryStream(xmlBytes, writable: false);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = IProtocolDesignImporter.MaxPayloadBytes,
                MaxCharactersFromEntities = 1024,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            };
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
        {
            return new OdmImportResult(
                null,
                [],
                ["External XML entity and DTD declarations (<!DOCTYPE> / <!ENTITY>) are blocked for security."],
                []);
        }
        catch (XmlException ex)
        {
            return new OdmImportResult(
                null,
                [],
                [$"Malformed ODM XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}"],
                []);
        }

        return await ParseDocumentAsync(document, resolveConcept, resolveTerminology, reservedElementIds, cancellationToken).ConfigureAwait(false);
    }

    internal static string? BoundTerminologyToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length <= 64)
        {
            return trimmed;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)));
        return $"{trimmed[..55]}-{hash[..8]}";
    }

    internal static string AllocateUniqueElementId(string rawCandidate, string prefix, HashSet<string> usedIds)
    {
        var baseId = SanitizeElementId(rawCandidate, prefix);
        if (usedIds.Add(baseId))
        {
            return baseId;
        }

        int suffix = 2;
        while (true)
        {
            var suffixText = $"_{suffix.ToString(CultureInfo.InvariantCulture)}";
            var maxStem = ProtocolDesignDocument.MaxIdLength - suffixText.Length;
            var stem = baseId.Length > maxStem ? baseId[..maxStem] : baseId;
            var candidate = $"{stem}{suffixText}";
            if (usedIds.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    internal static string SanitizeElementId(string? raw, string fallbackPrefix)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallbackPrefix;
        }

        var cleaned = InvalidIdCharsRegex().Replace(raw.Trim(), "_").Trim('_');
        if (cleaned.Length == 0)
        {
            return fallbackPrefix;
        }

        if (!char.IsAsciiLetter(cleaned[0]))
        {
            cleaned = $"{fallbackPrefix}_{cleaned}";
        }

        return cleaned.Length > ProtocolDesignDocument.MaxIdLength
            ? cleaned[..ProtocolDesignDocument.MaxIdLength]
            : cleaned;
    }

    internal static string AllocateRuleId(string rawCandidate, HashSet<string> usedRuleIds)
    {
        var cleaned = InvalidRuleIdCharsRegex().Replace(rawCandidate.Trim(), "_").Trim('_', '.', '-');
        if (cleaned.Length == 0 || !char.IsAsciiLetterOrDigit(cleaned[0]))
        {
            cleaned = $"R_{cleaned}";
        }

        if (cleaned.Length > ValidationDetection.MaxRuleIdLength)
        {
            cleaned = cleaned[..ValidationDetection.MaxRuleIdLength];
        }

        if (usedRuleIds.Add(cleaned))
        {
            return cleaned;
        }

        int suffix = 2;
        while (true)
        {
            var suffixText = $"_{suffix.ToString(CultureInfo.InvariantCulture)}";
            var maxStem = ValidationDetection.MaxRuleIdLength - suffixText.Length;
            var stem = cleaned.Length > maxStem ? cleaned[..maxStem] : cleaned;
            var candidate = $"{stem}{suffixText}";
            if (usedRuleIds.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static async Task<OdmImportResult> ParseDocumentAsync(
        XDocument document,
        ProtocolConceptResolver resolveConcept,
        ProtocolTerminologyResolver? resolveTerminology,
        ISet<string>? reservedIds,
        CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var notes = new List<ProtocolImportNote>();
        var usedIds = reservedIds is not null
            ? new HashSet<string>(reservedIds, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        if (document.Root is not { } root)
        {
            return new OdmImportResult(null, [], ["XML document has no root element."], []);
        }

        if (!string.Equals(root.Name.LocalName, "ODM", StringComparison.Ordinal))
        {
            return new OdmImportResult(
                null,
                [],
                [$"Expected root <ODM> element for CDISC ODM v1.3.2 XML, found <{root.Name.LocalName}>."],
                []);
        }

        var rootNamespace = root.Name.NamespaceName;
        if (string.IsNullOrEmpty(rootNamespace))
        {
            notes.Add(new ProtocolImportNote("ODM", $"Root <ODM> element omitted xmlns='{OdmNamespace}'; parsed using ODM v1.3.2 structure rules."));
        }
        else if (!string.Equals(rootNamespace, OdmNamespace, StringComparison.Ordinal))
        {
            return new OdmImportResult(
                null,
                [],
                [$"Unsupported XML namespace '{rootNamespace}'; expected CDISC ODM v1.3 ('{OdmNamespace}')."],
                []);
        }

        var odmVersion = Attr(root, "ODMVersion");
        if (!string.IsNullOrWhiteSpace(odmVersion) && !odmVersion.StartsWith("1.3", StringComparison.Ordinal))
        {
            return new OdmImportResult(
                null,
                [],
                [$"Unsupported ODMVersion '{odmVersion}'; synchronous ODM import supports ODM v1.3.x (v1.3.2)."],
                []);
        }

        CollectVendorAndExtensionNotes(root, rootNamespace, notes);

        var studies = Children(root, "Study").ToList();
        if (studies.Count == 0)
        {
            return new OdmImportResult(null, [], ["ODM document contains no <Study> element."], notes);
        }

        var study = studies[0];
        if (studies.Count > 1)
        {
            notes.Add(new ProtocolImportNote("ODM/Study", $"ODM document contains {studies.Count} <Study> elements; imported the first study ('{Attr(study, "OID") ?? "Study_1"}')."));
        }

        var globalVars = Child(study, "GlobalVariables");
        var studyTitle = FirstNonBlank(
            TextOf(globalVars, "StudyName"),
            TextOf(globalVars, "ProtocolName"),
            TextOf(globalVars, "StudyDescription"),
            Attr(study, "OID"));

        var units = OdmItemAndMetadataBuilder.ParseMeasurementUnits(study, notes);

        var metaVersions = Children(study, "MetaDataVersion").ToList();
        if (metaVersions.Count == 0)
        {
            return new OdmImportResult(studyTitle, [], ["ODM <Study> contains no <MetaDataVersion> element."], notes);
        }

        var mdv = metaVersions[0];
        if (metaVersions.Count > 1)
        {
            notes.Add(new ProtocolImportNote("ODM/Study/MetaDataVersion", $"ODM <Study> contains {metaVersions.Count} <MetaDataVersion> elements; imported '{Attr(mdv, "OID") ?? "MetaDataVersion_1"}'."));
        }

        var codeLists = OdmItemAndMetadataBuilder.ParseCodeLists(mdv);
        var itemDefs = ParseByOid(mdv, "ItemDef", problems);
        var itemGroupDefs = ParseByOid(mdv, "ItemGroupDef", problems);
        var conditionDefs = ParseByOid(mdv, "ConditionDef", problems);
        var studyEventDefs = ParseByOid(mdv, "StudyEventDef", problems);
        foreach (var (eventOid, eventDef) in studyEventDefs)
        {
            ValidateYesNoAttribute(eventDef, "Repeating", $"StudyEventDef '{eventOid}'", problems);
        }

        var protocolEl = Child(mdv, "Protocol");
        var orderedEventEntries = OdmScheduleCompiler.OrderStudyEvents(mdv, protocolEl, studyEventDefs, notes, problems);

        var formDefs = Children(mdv, "FormDef").ToList();
        if (formDefs.Count == 0)
        {
            problems.Add("ODM <MetaDataVersion> contains no <FormDef> elements.");
        }

        foreach (var method in Children(mdv, "MethodDef"))
        {
            notes.Add(new ProtocolImportNote(
                Attr(method, "OID") ?? "MethodDef",
                $"Optional ODM <MethodDef> '{Attr(method, "Name") ?? Attr(method, "OID")}' was noted; derivation rules are authored separately."));
        }

        if (problems.Count > 0)
        {
            return new OdmImportResult(studyTitle, [], problems, notes);
        }

        var studyOid = Attr(study, "OID");
        var formConditionOids = CollectFormConditionOids(mdv, orderedEventEntries);

        var seenFormOids = new HashSet<string>(StringComparer.Ordinal);
        var specs = new List<CollectionSpec>(formDefs.Count);
        var notedCodeLists = new HashSet<string>(StringComparer.Ordinal);
        var resolvedCodeLists = new Dictionary<string, ProtocolTerminologyResolution>(StringComparer.Ordinal);
        var formIdByOid = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedItems = new List<OdmImportedItemMeta>();
        var conditionTargets = new List<(string ConditionOid, OdmImportedItemMeta TargetItem)>();

        foreach (var formDef in formDefs)
        {
            var rawFormOid = Attr(formDef, "OID");
            if (string.IsNullOrWhiteSpace(rawFormOid))
            {
                problems.Add("A <FormDef> element is missing its required OID attribute.");
                continue;
            }

            if (!seenFormOids.Add(rawFormOid))
            {
                problems.Add($"Duplicate <FormDef> OID '{rawFormOid}' in <MetaDataVersion>.");
                continue;
            }

            var formName = FirstNonBlank(
                Attr(formDef, "Name"),
                TranslatedText(Child(formDef, "Description")),
                rawFormOid)!;

            var formRepeatingAttr = ValidateYesNoAttribute(formDef, "Repeating", $"Form '{rawFormOid}'", problems);
            var formRepeating = string.Equals(formRepeatingAttr, "Yes", StringComparison.OrdinalIgnoreCase);
            if (formRepeating)
            {
                notes.Add(new ProtocolImportNote(rawFormOid, $"Form '{formName}' declares Repeating='Yes'; multiple instances may be opened per participant visit."));
            }

            var formId = AllocateUniqueElementId(rawFormOid, "Form", usedIds);
            formIdByOid[rawFormOid] = formId;
            formConditionOids.TryGetValue(rawFormOid, out var formLevelConditionOid);
            var orderedGroupRefs = OrderRefs(Children(formDef, "ItemGroupRef"), rawFormOid, "ItemGroupRef", problems);
            var items = new List<CollectionItem>();

            foreach (var groupRef in orderedGroupRefs)
            {
                var igOid = Attr(groupRef, "ItemGroupOID");
                if (string.IsNullOrWhiteSpace(igOid))
                {
                    problems.Add($"Form '{rawFormOid}' has an <ItemGroupRef> without an ItemGroupOID attribute.");
                    continue;
                }

                ValidateYesNoAttribute(groupRef, "Mandatory", $"Form '{rawFormOid}' ItemGroupRef '{igOid}'", problems);
                var groupConditionOid = Attr(groupRef, "CollectionExceptionConditionOID");
                if (!itemGroupDefs.TryGetValue(igOid, out var igDef))
                {
                    problems.Add($"Form '{rawFormOid}' references ItemGroupOID '{igOid}', which does not exist in <MetaDataVersion>.");
                    continue;
                }

                if (string.Equals(Attr(igDef, "Repeating"), "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add(new ProtocolImportNote(
                        $"{rawFormOid}/{igOid}",
                        $"ItemGroup '{Attr(igDef, "Name") ?? igOid}' declares Repeating='Yes'; flattened items into form '{formName}' in display order."));
                }

                var orderedItemRefs = OrderRefs(Children(igDef, "ItemRef"), igOid, "ItemRef", problems);
                foreach (var itemRef in orderedItemRefs)
                {
                    var itemOid = Attr(itemRef, "ItemOID");
                    if (string.IsNullOrWhiteSpace(itemOid))
                    {
                        problems.Add($"ItemGroup '{igOid}' has an <ItemRef> without an ItemOID attribute.");
                        continue;
                    }

                    var mandatoryAttr = ValidateYesNoAttribute(itemRef, "Mandatory", $"ItemGroup '{igOid}' ItemRef '{itemOid}'", problems);
                    var required = string.Equals(mandatoryAttr, "Yes", StringComparison.OrdinalIgnoreCase);
                    var itemConditionOid = Attr(itemRef, "CollectionExceptionConditionOID");

                    if (!itemDefs.TryGetValue(itemOid, out var itemDef))
                    {
                        problems.Add($"ItemGroup '{igOid}' references ItemOID '{itemOid}', which does not exist in <MetaDataVersion>.");
                        continue;
                    }

                    var item = await OdmItemAndMetadataBuilder.BuildItemAsync(
                        formId,
                        rawFormOid,
                        itemOid,
                        itemDef,
                        required,
                        units,
                        codeLists,
                        usedIds,
                        notedCodeLists,
                        resolvedCodeLists,
                        notes,
                        problems,
                        resolveConcept,
                        resolveTerminology,
                        cancellationToken).ConfigureAwait(false);
                    if (item is not null)
                    {
                        items.Add(item);
                        var itemName = FirstNonBlank(Attr(itemDef, "Name"), itemOid)!;
                        var meta = new OdmImportedItemMeta(formId, rawFormOid, igOid, itemOid, itemName, item, itemDef);
                        importedItems.Add(meta);
                        foreach (var condOid in new[] { formLevelConditionOid, groupConditionOid, itemConditionOid }
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct(StringComparer.Ordinal))
                        {
                            conditionTargets.Add((condOid!, meta));
                        }
                    }
                }
            }

            specs.Add(new CollectionSpec(formId, formName, items, Repeating: formRepeating));
        }

        var (epochs, plannedActivities) = OdmScheduleCompiler.CompileStudySchedule(
            orderedEventEntries,
            formIdByOid,
            usedIds,
            notes,
            problems);

        if (problems.Count > 0)
        {
            return new OdmImportResult(studyTitle, [], problems, DeduplicateNotes(notes), StudyOid: studyOid);
        }

        var compiledRules = OdmRuleCompiler.CompileOdmRules(
            FirstNonBlank(studyOid, studyTitle, "ODM")!,
            importedItems,
            formIdByOid,
            conditionDefs,
            conditionTargets,
            notes,
            problems);

        return problems.Count > 0
            ? new OdmImportResult(studyTitle, [], problems, DeduplicateNotes(notes), StudyOid: studyOid)
            : new OdmImportResult(
                studyTitle,
                specs,
                [],
                DeduplicateNotes(notes),
                Rules: compiledRules,
                StudyOid: studyOid,
                Epochs: epochs,
                PlannedActivities: plannedActivities);
    }

    private static Dictionary<string, string> CollectFormConditionOids(
        XElement mdv,
        IReadOnlyList<(XElement? EventRef, XElement EventDef)> orderedEventEntries)
    {
        var formConditionOids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, eventDef) in orderedEventEntries)
        {
            foreach (var formRef in Children(eventDef, "FormRef"))
            {
                var fOid = Attr(formRef, "FormOID");
                var cOid = Attr(formRef, "CollectionExceptionConditionOID");
                if (!string.IsNullOrWhiteSpace(fOid) && !string.IsNullOrWhiteSpace(cOid))
                {
                    formConditionOids.TryAdd(fOid, cOid);
                }
            }
        }

        foreach (var eventDef in Children(mdv, "StudyEventDef"))
        {
            foreach (var formRef in Children(eventDef, "FormRef"))
            {
                var fOid = Attr(formRef, "FormOID");
                var cOid = Attr(formRef, "CollectionExceptionConditionOID");
                if (!string.IsNullOrWhiteSpace(fOid) && !string.IsNullOrWhiteSpace(cOid))
                {
                    formConditionOids.TryAdd(fOid, cOid);
                }
            }
        }

        return formConditionOids;
    }

    [GeneratedRegex("[^A-Za-z0-9_.:-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidIdCharsRegex();

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidRuleIdCharsRegex();
}
