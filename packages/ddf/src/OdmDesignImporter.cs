using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Validation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Ddf;

/// <summary>Result of importing an ODM v1.3.2 XML document into collection specifications and import notes.</summary>
/// <param name="Title">Study title read from ODM <c>GlobalVariables</c>, when present.</param>
/// <param name="CollectionSpecs">Imported forms in ODM display order with ordered items, prompts, and mandatory flags.</param>
/// <param name="Problems">Structural or XML validation problems that prevented import.</param>
/// <param name="Notes">Readable notes describing unmapped terminology codes, optional vendor attributes, or approximated constructs.</param>
/// <param name="PayloadTooLarge">True when the XML payload exceeded <see cref="IProtocolDesignImporter.MaxPayloadBytes"/>.</param>
/// <param name="Rules">Compiled collection-stage edit-check rules extracted from ODM <c>RangeCheck</c> and <c>ConditionDef</c> elements.</param>
/// <param name="StudyOid">OID attribute on the imported ODM <c>Study</c> element, when present.</param>
public sealed record OdmImportResult(
    string? Title,
    IReadOnlyList<CollectionSpec> CollectionSpecs,
    IReadOnlyList<string> Problems,
    IReadOnlyList<ProtocolImportNote> Notes,
    bool PayloadTooLarge = false,
    IReadOnlyList<Rule>? Rules = null,
    string? StudyOid = null)
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

    /// <summary>Imports an ODM v1.3.2 XML string into collection specifications and notes.</summary>
    public static Task<OdmImportResult> ImportAsync(
        string odmXml,
        ProtocolConceptResolver resolveConcept,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(odmXml);
        var bytes = Encoding.UTF8.GetBytes(odmXml);
        return ImportAsync(bytes, resolveConcept, reservedElementIds, cancellationToken);
    }

    /// <summary>Imports raw UTF-8/XML bytes in ODM v1.3.2 format into collection specifications and notes.</summary>
    public static async Task<OdmImportResult> ImportAsync(
        byte[] xmlBytes,
        ProtocolConceptResolver resolveConcept,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default)
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

        return await ParseDocumentAsync(document, resolveConcept, reservedElementIds, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<OdmImportResult> ParseDocumentAsync(
        XDocument document,
        ProtocolConceptResolver resolveConcept,
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

        var units = ParseMeasurementUnits(study, notes);

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

        var codeLists = ParseCodeLists(mdv);
        var itemDefs = ParseByOid(mdv, "ItemDef", problems);
        var itemGroupDefs = ParseByOid(mdv, "ItemGroupDef", problems);
        var conditionDefs = ParseByOid(mdv, "ConditionDef", problems);
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
        var formConditionOids = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Child(mdv, "Protocol") is { } protocolEl)
        {
            foreach (var eventRef in Children(protocolEl, "StudyEventRef"))
            {
                var eventOid = Attr(eventRef, "StudyEventOID");
                if (string.IsNullOrWhiteSpace(eventOid))
                {
                    continue;
                }

                var eventDef = Children(mdv, "StudyEventDef").FirstOrDefault(e => string.Equals(Attr(e, "OID"), eventOid, StringComparison.Ordinal));
                if (eventDef is null)
                {
                    continue;
                }

                foreach (var formRef in Children(eventDef, "FormRef"))
                {
                    var fOid = Attr(formRef, "FormOID");
                    var cOid = Attr(formRef, "CollectionExceptionConditionOID");
                    if (!string.IsNullOrWhiteSpace(fOid) && !string.IsNullOrWhiteSpace(cOid))
                    {
                        formConditionOids[fOid] = cOid;
                    }
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

        var seenFormOids = new HashSet<string>(StringComparer.Ordinal);
        var specs = new List<CollectionSpec>(formDefs.Count);
        var notedCodeLists = new HashSet<string>(StringComparer.Ordinal);
        var formIdByOid = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedItems = new List<ImportedItemMeta>();
        var conditionTargets = new List<(string ConditionOid, ImportedItemMeta TargetItem)>();

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

            if (string.Equals(Attr(formDef, "Repeating"), "Yes", StringComparison.OrdinalIgnoreCase))
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

                    var item = await BuildItemAsync(
                        formId,
                        rawFormOid,
                        itemOid,
                        itemDef,
                        required,
                        units,
                        codeLists,
                        usedIds,
                        notedCodeLists,
                        notes,
                        problems,
                        resolveConcept,
                        cancellationToken).ConfigureAwait(false);
                    if (item is not null)
                    {
                        items.Add(item);
                        var itemName = FirstNonBlank(Attr(itemDef, "Name"), itemOid)!;
                        var meta = new ImportedItemMeta(formId, rawFormOid, igOid, itemOid, itemName, item, itemDef);
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

            specs.Add(new CollectionSpec(formId, formName, items));
        }

        if (problems.Count > 0)
        {
            return new OdmImportResult(studyTitle, [], problems, DeduplicateNotes(notes), StudyOid: studyOid);
        }

        var compiledRules = CompileOdmRules(
            FirstNonBlank(studyOid, studyTitle, "ODM")!,
            importedItems,
            formIdByOid,
            conditionDefs,
            conditionTargets,
            notes,
            problems);

        return problems.Count > 0
            ? new OdmImportResult(studyTitle, [], problems, DeduplicateNotes(notes), StudyOid: studyOid)
            : new OdmImportResult(studyTitle, specs, [], DeduplicateNotes(notes), Rules: compiledRules, StudyOid: studyOid);
    }

    private static async Task<CollectionItem?> BuildItemAsync(
        string formId,
        string rawFormOid,
        string itemOid,
        XElement itemDef,
        bool required,
        IReadOnlyDictionary<string, string?> units,
        IReadOnlyDictionary<string, ParsedCodeList> codeLists,
        HashSet<string> usedIds,
        HashSet<string> notedCodeLists,
        List<ProtocolImportNote> notes,
        List<string> problems,
        ProtocolConceptResolver resolveConcept,
        CancellationToken cancellationToken)
    {
        var itemName = FirstNonBlank(Attr(itemDef, "Name"), itemOid)!;
        var prompt = ExtractItemPrompt(itemDef, itemName);
        var question = TranslatedText(Child(itemDef, "Question"));
        var description = TranslatedText(Child(itemDef, "Description"));
        var definition = FirstNonBlank(question, description, prompt)!;
        var extCode = ExtractExtCode(itemDef);

        var unitRefOid = Attr(Child(itemDef, "MeasurementUnitRef"), "MeasurementUnitOID");
        string? ucumUnit = null;
        if (!string.IsNullOrWhiteSpace(unitRefOid))
        {
            if (units.TryGetValue(unitRefOid, out var resolvedUnit))
            {
                ucumUnit = resolvedUnit;
            }
            else
            {
                notes.Add(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Item '{itemName}' references MeasurementUnitOID '{unitRefOid}', which is not declared in <BasicDefinitions>; imported without unit constraint."));
            }
        }

        var codeListOid = Attr(Child(itemDef, "CodeListRef"), "CodeListOID");
        if (!string.IsNullOrWhiteSpace(codeListOid) && notedCodeLists.Add($"{itemOid}:{codeListOid}"))
        {
            if (codeLists.TryGetValue(codeListOid, out var cl))
            {
                string sample;
                if (cl.Codes.Count == 0)
                {
                    sample = "no coded values";
                }
                else
                {
                    var moreSuffix = cl.Codes.Count > 5 ? $", +{cl.Codes.Count - 5} more" : string.Empty;
                    sample = string.Join(", ", cl.Codes.Take(5)) + moreSuffix;
                }

                notes.Add(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Unmapped terminology CodeList '{codeListOid}' ({cl.Name}: {sample}) on item '{itemName}' has no pinned code-system version in the tenant; imported item without terminology binding."));
            }
            else
            {
                notes.Add(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Unmapped terminology CodeListRef '{codeListOid}' on item '{itemName}' is not defined in <MetaDataVersion>; imported item without terminology binding."));
            }
        }

        var valueKind = MapDataType(Attr(itemDef, "DataType"), ucumUnit, itemOid, itemName, notes);
        var stableKey = $"odm:{itemOid}:{prompt}";
        var resolution = await resolveConcept(
            new ProtocolConceptCandidate(stableKey, extCode, prompt, definition, ConceptKind.DataElement),
            cancellationToken).ConfigureAwait(false);
        if (resolution.Concept is not { } conceptRef)
        {
            problems.Add(resolution.ErrorMessage ?? $"Could not resolve or define a concept for ODM item '{itemOid}'.");
            return null;
        }

        if (resolution.UsedFallbackSponsorConcept && !string.IsNullOrWhiteSpace(extCode))
        {
            notes.Add(new ProtocolImportNote(
                $"{rawFormOid}/{itemOid}",
                $"Unmapped terminology code '{extCode}' on item '{itemName}' is not registered in the tenant Concept Registry; bound to a sponsor-local concept."));
        }

        var candidateId = SanitizeElementId(itemOid, "Item");
        if (usedIds.Contains(candidateId))
        {
            candidateId = SanitizeElementId($"{formId}.{itemOid}", "Item");
        }

        var itemId = AllocateUniqueElementId(candidateId, "Item", usedIds);
        return new CollectionItem(
            itemId,
            conceptRef,
            Unit: ucumUnit,
            Terminology: null,
            Required: required,
            Purpose: null,
            ValueKind: valueKind,
            Prompt: prompt);
    }

    private static ObservationValueKind? MapDataType(
        string? dataType,
        string? ucumUnit,
        string itemOid,
        string itemName,
        List<ProtocolImportNote> notes)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return ucumUnit is not null ? ObservationValueKind.Quantity : ObservationValueKind.Text;
        }

        switch (dataType.Trim().ToLowerInvariant())
        {
            case "text":
            case "string":
            case "uri":
                return ObservationValueKind.Text;
            case "integer":
            case "float":
            case "double":
            case "decimal":
            case "hexfloat":
            case "base64float":
                return ObservationValueKind.Quantity;
            case "boolean":
                return ObservationValueKind.Boolean;
            case "date":
            case "time":
            case "datetime":
            case "partialdate":
            case "partialtime":
            case "partialdatetime":
            case "incompletedatetime":
            case "durationdatetime":
            case "intervaldatetime":
                return ObservationValueKind.DateTime;
            default:
                notes.Add(new ProtocolImportNote(
                    itemOid,
                    $"ODM DataType '{dataType}' on item '{itemName}' was approximated as text."));
                return ObservationValueKind.Text;
        }
    }

    private static string ExtractItemPrompt(XElement itemDef, string fallbackName)
    {
        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = NormalizeAliasContext(Attr(alias, "Context"));
            var name = Attr(alias, "Name");
            if (string.Equals(ctx, "prompt", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        if (TranslatedText(Child(itemDef, "Question")) is { } question)
        {
            return question;
        }

        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = NormalizeAliasContext(Attr(alias, "Context"));
            var name = Attr(alias, "Name");
            if (string.Equals(ctx, "questiontext", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }

        return FirstNonBlank(TranslatedText(Child(itemDef, "Description")), fallbackName) ?? fallbackName;
    }

    private static string? ExtractExtCode(XElement itemDef)
    {
        foreach (var alias in Children(itemDef, "Alias"))
        {
            var ctx = Attr(alias, "Context");
            var name = Attr(alias, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var trimmedName = name.Trim();
            if (!string.IsNullOrWhiteSpace(ctx) && ctx.Contains("ExtCodeID", StringComparison.OrdinalIgnoreCase))
            {
                return trimmedName;
            }

            const string prefix = "extCodeID:";
            if (trimmedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var code = trimmedName[prefix.Length..].Trim();
                if (code.Length > 0)
                {
                    return code;
                }
            }

            if (!string.IsNullOrWhiteSpace(ctx)
                && (ctx.Contains("NCI", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("CDISC", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("CDASH", StringComparison.OrdinalIgnoreCase)
                    || ctx.Contains("SDTM", StringComparison.OrdinalIgnoreCase))
                && trimmedName.Length >= 2
                && (trimmedName[0] == 'C' || trimmedName[0] == 'c')
                && trimmedName[1..].All(char.IsAsciiDigit))
            {
                return trimmedName.ToUpperInvariant();
            }
        }

        return null;
    }

    private static string NormalizeAliasContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return string.Empty;
        }

        return context.Trim().Trim('(', ')', '[', ']', ' ');
    }

    private static Dictionary<string, string?> ParseMeasurementUnits(XElement study, List<ProtocolImportNote> notes)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var basic = Child(study, "BasicDefinitions");
        if (basic is null)
        {
            return result;
        }

        foreach (var unit in Children(basic, "MeasurementUnit"))
        {
            var oid = Attr(unit, "OID");
            if (string.IsNullOrWhiteSpace(oid))
            {
                continue;
            }

            var symbol = FirstNonBlank(TranslatedText(Child(unit, "Symbol")), Attr(unit, "Name"));
            if (string.IsNullOrWhiteSpace(symbol))
            {
                result[oid] = null;
                continue;
            }

            if (Ucum.Problem(symbol) is null)
            {
                result[oid] = symbol;
            }
            else
            {
                notes.Add(new ProtocolImportNote(
                    oid,
                    $"MeasurementUnit '{oid}' symbol '{symbol}' is not a canonical UCUM expression; imported numeric items without fixed unit constraint."));
                result[oid] = null;
            }
        }

        return result;
    }

    private static Dictionary<string, ParsedCodeList> ParseCodeLists(XElement mdv)
    {
        var result = new Dictionary<string, ParsedCodeList>(StringComparer.Ordinal);
        foreach (var cl in Children(mdv, "CodeList"))
        {
            var oid = Attr(cl, "OID");
            if (string.IsNullOrWhiteSpace(oid))
            {
                continue;
            }

            var name = FirstNonBlank(Attr(cl, "Name"), oid)!;
            var codes = Children(cl, "CodeListItem")
                .Concat(Children(cl, "EnumeratedItem"))
                .Select(i => Attr(i, "CodedValue"))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!)
                .ToList();
            result[oid] = new ParsedCodeList(oid, name, codes);
        }

        return result;
    }

    private static Dictionary<string, XElement> ParseByOid(XElement parent, string elementName, List<string> problems)
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

    private static List<XElement> OrderRefs(IEnumerable<XElement> refs, string ownerOid, string refElementName, List<string> problems)
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

    private static string? ValidateYesNoAttribute(XElement element, string attributeName, string context, List<string> problems)
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

    private static void CollectVendorAndExtensionNotes(XElement root, string odmNamespace, List<ProtocolImportNote> notes)
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

    private static List<ProtocolImportNote> DeduplicateNotes(List<ProtocolImportNote> notes) =>
        [.. notes.Distinct()];

    private static IEnumerable<XElement> Children(XElement? parent, string localName) =>
        parent?.Elements().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal)) ?? [];

    private static XElement? Child(XElement? parent, string localName) =>
        Children(parent, localName).FirstOrDefault();

    private static string? Attr(XElement? element, string localName) =>
        element?.Attributes().FirstOrDefault(a => string.IsNullOrEmpty(a.Name.NamespaceName) && string.Equals(a.Name.LocalName, localName, StringComparison.Ordinal))?.Value?.Trim();

    private static string? TextOf(XElement? parent, string childLocalName)
    {
        var value = Child(parent, childLocalName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeWhitespace(value);
    }

    private static string? TranslatedText(XElement? container)
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

    private static string NormalizeWhitespace(string value) => WhitespaceRegex().Replace(value.Trim(), " ");

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    [GeneratedRegex("[^A-Za-z0-9_.:-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidIdCharsRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidRuleIdCharsRegex();

    private static List<Rule> CompileOdmRules(
        string citationDocumentLabel,
        IReadOnlyList<ImportedItemMeta> importedItems,
        IReadOnlyDictionary<string, string> formIdByOid,
        IReadOnlyDictionary<string, XElement> conditionDefs,
        IReadOnlyList<(string ConditionOid, ImportedItemMeta TargetItem)> conditionTargets,
        List<ProtocolImportNote> notes,
        List<string> problems)
    {
        var rules = new List<Rule>();
        var usedRuleIds = new HashSet<string>(StringComparer.Ordinal);
        var itemsByLookup = BuildItemLookup(importedItems, formIdByOid);
        var docTitle = TruncateText($"CDISC ODM Import ({citationDocumentLabel})", RulePackJson.MaxTextLength);

        foreach (var meta in importedItems)
        {
            int rcIndex = 0;
            foreach (var rangeCheck in Children(meta.ItemDef, "RangeCheck"))
            {
                rcIndex++;
                var comparator = Attr(rangeCheck, "Comparator")?.ToUpperInvariant();
                var softHard = Attr(rangeCheck, "SoftHard");
                var severity = string.Equals(softHard, "Hard", StringComparison.OrdinalIgnoreCase)
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning;
                var errorMessage = TranslatedText(Child(rangeCheck, "ErrorMessage"));
                var checkValues = Children(rangeCheck, "CheckValue")
                    .Select(v => v.Value?.Trim())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!)
                    .ToList();
                var formalEl = Children(rangeCheck, "FormalExpression").FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Value));

                RuleExpression? condition = null;
                if (!string.IsNullOrWhiteSpace(comparator) && checkValues.Count > 0)
                {
                    condition = BuildRangeCheckExpression(meta, comparator, checkValues, problems);
                }
                else if (formalEl is not null)
                {
                    var rawExpr = formalEl.Value.Trim();
                    if (OdmFormalExpressionParser.TryParse(
                        rawExpr,
                        token => ResolveVariableForForm(token, meta.FormId, itemsByLookup),
                        meta.Item.Id,
                        out var parsedFormal,
                        out var parseError) && parsedFormal is not null)
                    {
                        condition = rawExpr.StartsWith('{')
                            ? parsedFormal
                            : new LogicalExpression(
                                LogicalOperator.And,
                                [
                                    new FunctionExpression(RuleFunction.IsNotEmpty, [new VariableExpression(meta.Item.Id)]),
                                    new NotExpression(parsedFormal),
                                ]);
                    }
                    else
                    {
                        notes.Add(new ProtocolImportNote(
                            $"{meta.RawFormOid}/{meta.ItemOid}",
                            $"RangeCheck #{rcIndex} FormalExpression on item '{meta.ItemName}' could not be compiled ({parseError}); skipped."));
                    }
                }
                else
                {
                    problems.Add($"Item '{meta.ItemOid}' RangeCheck #{rcIndex} must define either Comparator with <CheckValue> or a <FormalExpression>.");
                }

                if (condition is null)
                {
                    continue;
                }

                var ruleId = AllocateRuleId($"RC.{meta.Item.Id}.{rcIndex}", usedRuleIds);
                var explanation = TruncateText(
                    FirstNonBlank(
                        errorMessage,
                        !string.IsNullOrWhiteSpace(comparator)
                            ? $"Item '{meta.Item.Prompt ?? meta.ItemName}' must satisfy {comparator} {string.Join(", ", checkValues)}."
                            : $"Item '{meta.Item.Prompt ?? meta.ItemName}' failed range check #{rcIndex}.")!,
                    RulePackJson.MaxTextLength);

                rules.Add(new Rule(
                    ruleId,
                    1,
                    RuleAuthority.Study,
                    new SourceCitation(docTitle, $"{meta.RawFormOid}/{meta.ItemOid}", null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, meta.FormId),
                    severity,
                    condition,
                    explanation,
                    RemediationGuidance: "Verify the source value and correct or explain the discrepancy.",
                    EffectivePeriod.Always));
            }
        }

        var targetsByCondition = conditionTargets
            .GroupBy(t => t.ConditionOid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TargetItem).ToList(), StringComparer.Ordinal);

        foreach (var (condOid, condDef) in conditionDefs)
        {
            var condName = FirstNonBlank(Attr(condDef, "Name"), condOid)!;
            var condDesc = TranslatedText(Child(condDef, "Description"));
            var formalElements = Children(condDef, "FormalExpression")
                .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                .ToList();

            if (formalElements.Count == 0)
            {
                notes.Add(new ProtocolImportNote(
                    condOid,
                    $"ODM <ConditionDef> '{condName}' contains no <FormalExpression>; recorded description only."));
                continue;
            }

            RuleExpression? rawParsed = null;
            string? lastParseError = null;
            foreach (var fe in formalElements)
            {
                if (OdmFormalExpressionParser.TryParse(fe.Value, null, null, out var candidateExpr, out lastParseError)
                    && candidateExpr is not null)
                {
                    rawParsed = candidateExpr;
                    break;
                }
            }

            if (rawParsed is null)
            {
                notes.Add(new ProtocolImportNote(
                    condOid,
                    $"ODM <ConditionDef> '{condName}' FormalExpression could not be compiled ({lastParseError}); skipped."));
                continue;
            }

            if (targetsByCondition.TryGetValue(condOid, out var targets) && targets.Count > 0)
            {
                foreach (var target in targets)
                {
                    var resolvedCond = OdmFormalExpressionParser.RewriteVariables(
                        rawParsed,
                        token => ResolveVariableForForm(token, target.FormId, itemsByLookup),
                        target.Item.Id);
                    var exceptionViolation = new LogicalExpression(
                        LogicalOperator.And,
                        [
                            resolvedCond,
                            new FunctionExpression(RuleFunction.IsNotEmpty, [new VariableExpression(target.Item.Id)]),
                        ]);
                    var ruleId = AllocateRuleId($"CD.{condOid}.{target.Item.Id}", usedRuleIds);
                    var explanation = TruncateText(
                        FirstNonBlank(
                            condDesc,
                            $"Item '{target.Item.Prompt ?? target.ItemName}' must not be collected when condition '{condName}' holds.")!,
                        RulePackJson.MaxTextLength);

                    rules.Add(new Rule(
                        ruleId,
                        1,
                        RuleAuthority.Study,
                        new SourceCitation(docTitle, condOid, null),
                        new RuleApplicability(ValidationStage.Collection, null, null, null, target.FormId),
                        ValidationSeverity.Warning,
                        exceptionViolation,
                        explanation,
                        RemediationGuidance: "Confirm whether the conditional collection exception applies or clear the unneeded item.",
                        EffectivePeriod.Always));
                }
            }
            else
            {
                var referencedVars = RuleExpressionJson.Variables(rawParsed);
                var primaryFormId = ResolvePrimaryFormForStandaloneCondition(referencedVars, importedItems, itemsByLookup);
                if (primaryFormId is null)
                {
                    continue;
                }

                var resolvedCond = OdmFormalExpressionParser.RewriteVariables(
                    rawParsed,
                    token => ResolveVariableForForm(token, primaryFormId, itemsByLookup));
                var ruleId = AllocateRuleId($"CD.{condOid}", usedRuleIds);
                var explanation = TruncateText(
                    FirstNonBlank(condDesc, condName, $"Condition '{condOid}' reported a discrepancy.")!,
                    RulePackJson.MaxTextLength);

                rules.Add(new Rule(
                    ruleId,
                    1,
                    RuleAuthority.Study,
                    new SourceCitation(docTitle, condOid, null),
                    new RuleApplicability(ValidationStage.Collection, null, null, null, primaryFormId),
                    ValidationSeverity.Warning,
                    resolvedCond,
                    explanation,
                    RemediationGuidance: "Verify the related clinical items and resolve the conditional discrepancy.",
                    EffectivePeriod.Always));
            }
        }

        return rules;
    }

    private static RuleExpression? BuildRangeCheckExpression(
        ImportedItemMeta meta,
        string comparator,
        IReadOnlyList<string> checkValues,
        List<string> problems)
    {
        var variable = new VariableExpression(meta.Item.Id);
        var literals = new List<RuleExpression>(checkValues.Count);
        foreach (var rawVal in checkValues)
        {
            literals.Add(new LiteralExpression(ToTypedRuleValue(rawVal, meta.Item.ExpectedKind)));
        }

        switch (comparator)
        {
            case "LT":
                return new ComparisonExpression(ComparisonOperator.GreaterOrEqual, variable, literals[0]);
            case "LE":
                return new ComparisonExpression(ComparisonOperator.Greater, variable, literals[0]);
            case "GT":
                return new ComparisonExpression(ComparisonOperator.LessOrEqual, variable, literals[0]);
            case "GE":
                return new ComparisonExpression(ComparisonOperator.Less, variable, literals[0]);
            case "EQ":
                return new LogicalExpression(
                    LogicalOperator.And,
                    [
                        new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                        new ComparisonExpression(ComparisonOperator.NotEqual, variable, literals[0]),
                    ]);
            case "NE":
                return new ComparisonExpression(ComparisonOperator.Equal, variable, literals[0]);
            case "IN":
                return new LogicalExpression(
                    LogicalOperator.And,
                    [
                        new FunctionExpression(RuleFunction.IsNotEmpty, [variable]),
                        new FunctionExpression(RuleFunction.NotIn, [variable, .. literals]),
                    ]);
            case "NOTIN":
                return new FunctionExpression(RuleFunction.In, [variable, .. literals]);
            default:
                problems.Add($"Item '{meta.ItemOid}' RangeCheck uses unsupported ODM Comparator '{comparator}'.");
                return null;
        }
    }

    private static RuleValue ToTypedRuleValue(string rawValue, ObservationValueKind? expectedKind)
    {
        if (expectedKind == ObservationValueKind.Quantity
            && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return RuleValue.FromNumber(numeric);
        }

        if (expectedKind == ObservationValueKind.Boolean)
        {
            if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase) || rawValue == "1")
            {
                return RuleValue.True;
            }

            if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase) || rawValue == "0")
            {
                return RuleValue.False;
            }
        }

        if (expectedKind is null
            && decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var inferredNumeric))
        {
            return RuleValue.FromNumber(inferredNumeric);
        }

        return RuleValue.FromText(rawValue);
    }

    private static Dictionary<string, List<(string FormId, string ItemId)>> BuildItemLookup(
        IReadOnlyList<ImportedItemMeta> items,
        IReadOnlyDictionary<string, string> formIdByOid)
    {
        var map = new Dictionary<string, List<(string FormId, string ItemId)>>(StringComparer.Ordinal);
        void AddKey(string key, string formId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Contains((formId, itemId)))
            {
                list.Add((formId, itemId));
            }
        }

        foreach (var meta in items)
        {
            AddKey(meta.Item.Id, meta.FormId, meta.Item.Id);
            AddKey(meta.ItemOid, meta.FormId, meta.Item.Id);
            AddKey(meta.ItemName, meta.FormId, meta.Item.Id);
            AddKey($"{meta.FormId}.{meta.Item.Id}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}.{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}.{meta.ItemName}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawGroupOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            AddKey($"{meta.RawFormOid}/{meta.RawGroupOid}/{meta.ItemOid}", meta.FormId, meta.Item.Id);
            if (formIdByOid.TryGetValue(meta.RawFormOid, out var mappedFormId))
            {
                AddKey($"{mappedFormId}.{meta.ItemOid}", meta.FormId, meta.Item.Id);
            }
        }

        return map;
    }

    private static string ResolveVariableForForm(
        string rawToken,
        string targetFormId,
        IReadOnlyDictionary<string, List<(string FormId, string ItemId)>> lookup)
    {
        var cleaned = rawToken.Trim().TrimStart('/');
        if (lookup.TryGetValue(cleaned, out var candidates) && candidates.Count > 0)
        {
            var sameForm = candidates.FirstOrDefault(c => string.Equals(c.FormId, targetFormId, StringComparison.Ordinal));
            if (sameForm.ItemId is not null)
            {
                return sameForm.ItemId;
            }

            var sibling = candidates[0];
            var qualified = $"{sibling.FormId}.{sibling.ItemId}";
            return qualified.Length <= FindingTarget.MaxNameLength ? qualified : sibling.ItemId;
        }

        // Try trailing path segment for XPath-like paths such as StudyEventData/FormData/ItemGroupData/IT.SYSBP.
        var slashIdx = cleaned.LastIndexOf('/');
        if (slashIdx >= 0 && slashIdx + 1 < cleaned.Length)
        {
            return ResolveVariableForForm(cleaned[(slashIdx + 1)..], targetFormId, lookup);
        }

        return cleaned.Length <= FindingTarget.MaxNameLength ? cleaned : cleaned[..FindingTarget.MaxNameLength];
    }

    private static string? ResolvePrimaryFormForStandaloneCondition(
        IReadOnlyList<string> referencedVariables,
        IReadOnlyList<ImportedItemMeta> importedItems,
        IReadOnlyDictionary<string, List<(string FormId, string ItemId)>> lookup)
    {
        foreach (var rawVar in referencedVariables)
        {
            var cleaned = rawVar.Trim().TrimStart('/');
            if (!cleaned.Contains('/') && lookup.TryGetValue(cleaned, out var unqualified) && unqualified.Count > 0)
            {
                return unqualified[0].FormId;
            }
        }

        foreach (var rawVar in referencedVariables)
        {
            var cleaned = rawVar.Trim().TrimStart('/');
            if (lookup.TryGetValue(cleaned, out var candidates) && candidates.Count > 0)
            {
                return candidates[0].FormId;
            }
        }

        return importedItems.FirstOrDefault()?.FormId;
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

    private static string TruncateText(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 1), "…");

    private sealed record ImportedItemMeta(
        string FormId,
        string RawFormOid,
        string RawGroupOid,
        string ItemOid,
        string ItemName,
        CollectionItem Item,
        XElement ItemDef);

    private sealed record ParsedCodeList(string Oid, string Name, IReadOnlyList<string> Codes);
}
