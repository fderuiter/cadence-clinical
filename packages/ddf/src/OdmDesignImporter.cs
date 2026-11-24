using System;
using System.Collections.Frozen;
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

using Cadence.Kernel;
using Cadence.Kernel.Commands;
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

    internal static readonly ConceptRef PlaceholderConceptRef =
        new(ConceptId.FromGuid(Guid.Parse("00000000-0000-7000-8000-000000000001")), 1);

    private static readonly IReadOnlyDictionary<string, ProtocolConceptResolution> EmptyConceptResolutions =
        new Dictionary<string, ProtocolConceptResolution>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ProtocolTerminologyResolution> EmptyTerminologyResolutions =
        new Dictionary<string, ProtocolTerminologyResolution>(StringComparer.Ordinal);

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

    /// <summary>Imports an ODM v1.3.2 XML string into collection specifications and notes using one batch concept lookup.</summary>
    public static Task<OdmImportResult> ImportAsync(
        string odmXml,
        ProtocolConceptBatchResolver resolveConcepts,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(odmXml);
        var bytes = Encoding.UTF8.GetBytes(odmXml);
        return ImportAsync(bytes, resolveConcepts, reservedElementIds, cancellationToken, resolveTerminology);
    }

    /// <summary>Imports raw UTF-8/XML bytes in ODM v1.3.2 format into collection specifications and notes.</summary>
    public static Task<OdmImportResult> ImportAsync(
        byte[] xmlBytes,
        ProtocolConceptResolver resolveConcept,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        ArgumentNullException.ThrowIfNull(resolveConcept);
        return ImportAsync(xmlBytes, AdaptSingleResolver(resolveConcept), reservedElementIds, cancellationToken, resolveTerminology);
    }

    /// <summary>Imports raw UTF-8/XML bytes in ODM v1.3.2 format into collection specifications and notes using one batch concept lookup.</summary>
    public static async Task<OdmImportResult> ImportAsync(
        byte[] xmlBytes,
        ProtocolConceptBatchResolver resolveConcepts,
        ISet<string>? reservedElementIds = null,
        CancellationToken cancellationToken = default,
        ProtocolTerminologyResolver? resolveTerminology = null)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        ArgumentNullException.ThrowIfNull(resolveConcepts);

        var staged = StageBytes(xmlBytes, reservedElementIds);
        if (!staged.CanResolve)
        {
            return staged.ToFailedResult();
        }

        var resolutions = staged.UniqueCandidates.Count == 0
            ? EmptyConceptResolutions
            : await resolveConcepts(staged.UniqueCandidates, cancellationToken).ConfigureAwait(false);
        var allConceptsResolved = staged.UniqueCandidates.All(c =>
            resolutions.TryGetValue(c.StableKey, out var r) && r.Concept is not null);
        var termResolutions = allConceptsResolved
            ? await ResolveStagedTerminologyAsync(staged, resolveTerminology, cancellationToken).ConfigureAwait(false)
            : EmptyTerminologyResolutions;
        return MaterializeStagedDocument(staged, resolutions, termResolutions);
    }

    internal static ProtocolConceptBatchResolver AdaptSingleResolver(ProtocolConceptResolver resolveConcept) =>
        async (candidates, ct) =>
        {
            var resolved = new Dictionary<string, ProtocolConceptResolution>(candidates.Count, StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (!resolved.ContainsKey(candidate.StableKey))
                {
                    resolved[candidate.StableKey] = await resolveConcept(candidate, ct).ConfigureAwait(false);
                }
            }

            return resolved;
        };

    internal static StagedOdmDocument StageBytes(byte[] xmlBytes, ISet<string>? reservedElementIds = null)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);

        if (xmlBytes.Length > IProtocolDesignImporter.MaxPayloadBytes)
        {
            return StagedOdmDocument.Failed(
                null,
                [$"Uploaded XML payload ({xmlBytes.Length:N0} bytes) exceeds the 10 MB ({IProtocolDesignImporter.MaxPayloadBytes:N0} bytes) synchronous import limit."],
                [],
                payloadTooLarge: true);
        }

        if (xmlBytes.Length == 0)
        {
            return StagedOdmDocument.Failed(null, ["Uploaded XML file is empty."], []);
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
            return StagedOdmDocument.Failed(
                null,
                ["External XML entity and DTD declarations (<!DOCTYPE> / <!ENTITY>) are blocked for security."],
                []);
        }
        catch (XmlException ex)
        {
            return StagedOdmDocument.Failed(
                null,
                [$"Malformed ODM XML at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}"],
                []);
        }

        return StageDocument(document, reservedElementIds);
    }

    internal static Task<IReadOnlyDictionary<string, ProtocolTerminologyResolution>> ResolveStagedTerminologyAsync(
        StagedOdmDocument staged,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken) =>
        ResolveTerminologyCandidatesAsync(staged.UniqueTerminologyCandidates, resolveTerminology, cancellationToken);

    internal static async Task<IReadOnlyDictionary<string, ProtocolTerminologyResolution>> ResolveTerminologyCandidatesAsync(
        IReadOnlyList<ProtocolTerminologyCandidate> candidates,
        ProtocolTerminologyResolver? resolveTerminology,
        CancellationToken cancellationToken)
    {
        if (resolveTerminology is null || candidates.Count == 0)
        {
            return EmptyTerminologyResolutions;
        }

        var resolved = new Dictionary<string, ProtocolTerminologyResolution>(candidates.Count, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var key = candidate.CodelistCode ?? candidate.SourceKey;
            if (resolved.ContainsKey(key))
            {
                continue;
            }

            var termResolution = await resolveTerminology(candidate, cancellationToken).ConfigureAwait(false);
            resolved[key] = termResolution;
            if (!string.IsNullOrWhiteSpace(termResolution.ErrorMessage))
            {
                break;
            }
        }

        return resolved;
    }

    internal static OdmImportResult MaterializeStagedDocument(
        StagedOdmDocument staged,
        IReadOnlyDictionary<string, ProtocolConceptResolution> resolutions,
        IReadOnlyDictionary<string, ProtocolTerminologyResolution>? terminologyResolutions = null)
    {
        if (!staged.CanResolve)
        {
            return staged.ToFailedResult();
        }

        var problems = new List<string>();
        var specs = new List<CollectionSpec>(staged.StagedForms.Count);
        var boundCodeListOids = new HashSet<string>(StringComparer.Ordinal);
        var materializedItems = new List<ImportedItemMeta>();
        var conditionTargets = new List<(string ConditionOid, ImportedItemMeta TargetItem)>();

        foreach (var form in staged.StagedForms)
        {
            var items = new List<CollectionItem>(form.Items.Count);
            foreach (var stagedItem in form.Items)
            {
                TerminologyBinding? terminology = null;
                if (stagedItem.CodeListOid is { } codeListOid
                    && terminologyResolutions is not null
                    && terminologyResolutions.TryGetValue(codeListOid, out var termResolution))
                {
                    if (!string.IsNullOrWhiteSpace(termResolution.ErrorMessage))
                    {
                        problems.Add(termResolution.ErrorMessage);
                        continue;
                    }

                    if (termResolution.Binding is { } binding)
                    {
                        terminology = binding;
                        boundCodeListOids.Add(codeListOid);
                    }
                }

                if (!resolutions.TryGetValue(stagedItem.Candidate.StableKey, out var resolution)
                    || resolution.Concept is not { } conceptRef)
                {
                    problems.Add(resolution?.ErrorMessage ?? $"Could not resolve or define a concept for ODM item '{stagedItem.ItemOid}'.");
                    continue;
                }

                var materializedItem = terminology is not null
                    ? stagedItem.PlaceholderItem with
                    {
                        Concept = conceptRef,
                        Terminology = terminology,
                        Unit = null,
                        ValueKind = ObservationValueKind.Coded,
                    }
                    : stagedItem.PlaceholderItem with { Concept = conceptRef };
                items.Add(materializedItem);

                var meta = new ImportedItemMeta(
                    form.FormId,
                    stagedItem.RawFormOid,
                    stagedItem.RawGroupOid,
                    stagedItem.ItemOid,
                    stagedItem.ItemName,
                    materializedItem,
                    stagedItem.ItemDef);
                materializedItems.Add(meta);
                foreach (var condOid in stagedItem.ConditionOids)
                {
                    conditionTargets.Add((condOid, meta));
                }
            }

            specs.Add(new CollectionSpec(form.FormId, form.FormName, items));
        }

        var notes = staged.MaterializeNotes(resolutions, boundCodeListOids);
        if (problems.Count > 0)
        {
            return new OdmImportResult(staged.StudyTitle, [], problems, notes, StudyOid: staged.StudyOid);
        }

        var rules = staged.Rules;
        if (boundCodeListOids.Count > 0 && staged.HasRangeChecks)
        {
            var ruleNotes = new List<ProtocolImportNote>();
            rules = CompileOdmRules(
                FirstNonBlank(staged.StudyOid, staged.StudyTitle, "ODM")!,
                materializedItems,
                staged.FormIdByOid,
                staged.ConditionDefs,
                conditionTargets,
                ruleNotes,
                problems);
            if (problems.Count > 0)
            {
                return new OdmImportResult(staged.StudyTitle, [], problems, notes, StudyOid: staged.StudyOid);
            }
        }

        return new OdmImportResult(staged.StudyTitle, specs, [], notes, Rules: rules, StudyOid: staged.StudyOid);
    }

    private static StagedOdmDocument StageDocument(
        XDocument document,
        ISet<string>? reservedIds)
    {
        var problems = new List<string>();
        var preItemNotes = new List<ProtocolImportNote>();
        var stagedNotes = new List<StagedNoteEntry>();
        var usedIds = reservedIds is not null
            ? new HashSet<string>(reservedIds, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        if (document.Root is not { } root)
        {
            return StagedOdmDocument.Failed(null, ["XML document has no root element."], []);
        }

        if (!string.Equals(root.Name.LocalName, "ODM", StringComparison.Ordinal))
        {
            return StagedOdmDocument.Failed(
                null,
                [$"Expected root <ODM> element for CDISC ODM v1.3.2 XML, found <{root.Name.LocalName}>."],
                []);
        }

        var rootNamespace = root.Name.NamespaceName;
        if (string.IsNullOrEmpty(rootNamespace))
        {
            preItemNotes.Add(new ProtocolImportNote("ODM", $"Root <ODM> element omitted xmlns='{OdmNamespace}'; parsed using ODM v1.3.2 structure rules."));
        }
        else if (!string.Equals(rootNamespace, OdmNamespace, StringComparison.Ordinal))
        {
            return StagedOdmDocument.Failed(
                null,
                [$"Unsupported XML namespace '{rootNamespace}'; expected CDISC ODM v1.3 ('{OdmNamespace}')."],
                []);
        }

        var odmVersion = Attr(root, "ODMVersion");
        if (!string.IsNullOrWhiteSpace(odmVersion) && !odmVersion.StartsWith("1.3", StringComparison.Ordinal))
        {
            return StagedOdmDocument.Failed(
                null,
                [$"Unsupported ODMVersion '{odmVersion}'; synchronous ODM import supports ODM v1.3.x (v1.3.2)."],
                []);
        }

        CollectVendorAndExtensionNotes(root, rootNamespace, preItemNotes);

        var studies = Children(root, "Study").ToList();
        if (studies.Count == 0)
        {
            return StagedOdmDocument.Failed(null, ["ODM document contains no <Study> element."], preItemNotes);
        }

        var study = studies[0];
        if (studies.Count > 1)
        {
            preItemNotes.Add(new ProtocolImportNote("ODM/Study", $"ODM document contains {studies.Count} <Study> elements; imported the first study ('{Attr(study, "OID") ?? "Study_1"}')."));
        }

        var globalVars = Child(study, "GlobalVariables");
        var studyTitle = FirstNonBlank(
            TextOf(globalVars, "StudyName"),
            TextOf(globalVars, "ProtocolName"),
            TextOf(globalVars, "StudyDescription"),
            Attr(study, "OID"));

        var units = ParseMeasurementUnits(study, preItemNotes);

        var metaVersions = Children(study, "MetaDataVersion").ToList();
        if (metaVersions.Count == 0)
        {
            return StagedOdmDocument.Failed(studyTitle, ["ODM <Study> contains no <MetaDataVersion> element."], preItemNotes);
        }

        var mdv = metaVersions[0];
        if (metaVersions.Count > 1)
        {
            preItemNotes.Add(new ProtocolImportNote("ODM/Study/MetaDataVersion", $"ODM <Study> contains {metaVersions.Count} <MetaDataVersion> elements; imported '{Attr(mdv, "OID") ?? "MetaDataVersion_1"}'."));
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
            preItemNotes.Add(new ProtocolImportNote(
                Attr(method, "OID") ?? "MethodDef",
                $"Optional ODM <MethodDef> '{Attr(method, "Name") ?? Attr(method, "OID")}' was noted; derivation rules are authored separately."));
        }

        if (problems.Count > 0)
        {
            return StagedOdmDocument.Failed(studyTitle, problems, preItemNotes);
        }

        foreach (var note in preItemNotes)
        {
            stagedNotes.Add(StagedNoteEntry.Direct(note));
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
        var stagedForms = new List<StagedForm>(formDefs.Count);
        var uniqueCandidates = new List<ProtocolConceptCandidate>();
        var seenCandidateKeys = new HashSet<string>(StringComparer.Ordinal);
        var uniqueTerminologyCandidates = new List<ProtocolTerminologyCandidate>();
        var seenTerminologyKeys = new HashSet<string>(StringComparer.Ordinal);
        var notedCodeLists = new HashSet<string>(StringComparer.Ordinal);
        var formIdByOid = new Dictionary<string, string>(StringComparer.Ordinal);
        var importedItems = new List<ImportedItemMeta>();
        var conditionTargets = new List<(string ConditionOid, ImportedItemMeta TargetItem)>();
        bool hasRangeChecks = false;

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
                stagedNotes.Add(StagedNoteEntry.Direct(
                    new ProtocolImportNote(rawFormOid, $"Form '{formName}' declares Repeating='Yes'; multiple instances may be opened per participant visit.")));
            }

            var formId = AllocateUniqueElementId(rawFormOid, "Form", usedIds);
            formIdByOid[rawFormOid] = formId;
            formConditionOids.TryGetValue(rawFormOid, out var formLevelConditionOid);
            var orderedGroupRefs = OrderRefs(Children(formDef, "ItemGroupRef"), rawFormOid, "ItemGroupRef", problems);
            var stagedItems = new List<StagedItem>();

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
                    stagedNotes.Add(StagedNoteEntry.Direct(new ProtocolImportNote(
                        $"{rawFormOid}/{igOid}",
                        $"ItemGroup '{Attr(igDef, "Name") ?? igOid}' declares Repeating='Yes'; flattened items into form '{formName}' in display order.")));
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

                    var itemConditionOids = new[] { formLevelConditionOid, groupConditionOid, itemConditionOid }
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c!)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    if (Children(itemDef, "RangeCheck").Any())
                    {
                        hasRangeChecks = true;
                    }

                    var stagedItem = BuildStagedItem(
                        formId,
                        rawFormOid,
                        igOid,
                        itemOid,
                        itemDef,
                        required,
                        itemConditionOids,
                        units,
                        codeLists,
                        usedIds,
                        notedCodeLists,
                        seenTerminologyKeys,
                        uniqueTerminologyCandidates,
                        stagedNotes);
                    if (seenCandidateKeys.Add(stagedItem.Candidate.StableKey))
                    {
                        uniqueCandidates.Add(stagedItem.Candidate);
                    }

                    stagedItems.Add(stagedItem);
                    var meta = new ImportedItemMeta(formId, rawFormOid, igOid, itemOid, stagedItem.ItemName, stagedItem.PlaceholderItem, itemDef);
                    importedItems.Add(meta);
                    foreach (var condOid in itemConditionOids)
                    {
                        conditionTargets.Add((condOid, meta));
                    }
                }
            }

            stagedForms.Add(new StagedForm(formId, formName, stagedItems));
        }

        if (problems.Count > 0)
        {
            return StagedOdmDocument.FailedWithStagedNotes(studyTitle, studyOid, problems, stagedNotes);
        }

        var ruleNotes = new List<ProtocolImportNote>();
        var compiledRules = CompileOdmRules(
            FirstNonBlank(studyOid, studyTitle, "ODM")!,
            importedItems,
            formIdByOid,
            conditionDefs,
            conditionTargets,
            ruleNotes,
            problems);
        foreach (var note in ruleNotes)
        {
            stagedNotes.Add(StagedNoteEntry.Direct(note));
        }

        return problems.Count > 0
            ? StagedOdmDocument.FailedWithStagedNotes(studyTitle, studyOid, problems, stagedNotes)
            : new StagedOdmDocument(
                studyTitle,
                studyOid,
                stagedForms,
                uniqueCandidates,
                uniqueTerminologyCandidates,
                [],
                stagedNotes,
                compiledRules,
                PayloadTooLarge: false,
                formIdByOid,
                conditionDefs,
                hasRangeChecks);
    }

    private static StagedItem BuildStagedItem(
        string formId,
        string rawFormOid,
        string rawGroupOid,
        string itemOid,
        XElement itemDef,
        bool required,
        IReadOnlyList<string> conditionOids,
        IReadOnlyDictionary<string, string?> units,
        IReadOnlyDictionary<string, ParsedCodeList> codeLists,
        HashSet<string> usedIds,
        HashSet<string> notedCodeLists,
        HashSet<string> seenTerminologyKeys,
        List<ProtocolTerminologyCandidate> uniqueTerminologyCandidates,
        List<StagedNoteEntry> stagedNotes)
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
                stagedNotes.Add(StagedNoteEntry.Direct(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Item '{itemName}' references MeasurementUnitOID '{unitRefOid}', which is not declared in <BasicDefinitions>; imported without unit constraint.")));
            }
        }

        string? resolvableCodeListOid = null;
        var codeListOid = Attr(Child(itemDef, "CodeListRef"), "CodeListOID");
        if (!string.IsNullOrWhiteSpace(codeListOid))
        {
            if (codeLists.TryGetValue(codeListOid, out var cl))
            {
                resolvableCodeListOid = codeListOid;
                if (seenTerminologyKeys.Add(codeListOid))
                {
                    uniqueTerminologyCandidates.Add(new ProtocolTerminologyCandidate(
                        SourceKey: $"odm:{codeListOid}",
                        CodelistCode: codeListOid,
                        CodelistName: cl.Name,
                        CodeSystemUri: cl.ExternalDictionary,
                        CodeSystemName: cl.ExternalDictionary,
                        CodeSystemVersion: cl.ExternalVersion,
                        Entries: cl.Entries));
                }

                if (notedCodeLists.Add($"{itemOid}:{codeListOid}"))
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

                    stagedNotes.Add(StagedNoteEntry.WhenTerminologyUnbound(
                        codeListOid,
                        new ProtocolImportNote(
                            $"{rawFormOid}/{itemOid}",
                            $"Unmapped terminology CodeList '{codeListOid}' ({cl.Name}: {sample}) on item '{itemName}' has no pinned code-system version in the tenant; imported item without terminology binding.")));
                }
            }
            else if (notedCodeLists.Add($"{itemOid}:{codeListOid}"))
            {
                stagedNotes.Add(StagedNoteEntry.Direct(new ProtocolImportNote(
                    $"{rawFormOid}/{itemOid}",
                    $"Unmapped terminology CodeListRef '{codeListOid}' on item '{itemName}' is not defined in <MetaDataVersion>; imported item without terminology binding.")));
            }
        }

        var dtNotes = new List<ProtocolImportNote>();
        var valueKind = MapDataType(Attr(itemDef, "DataType"), ucumUnit, itemOid, itemName, dtNotes);
        foreach (var dtNote in dtNotes)
        {
            stagedNotes.Add(resolvableCodeListOid is not null
                ? StagedNoteEntry.WhenTerminologyUnbound(resolvableCodeListOid, dtNote)
                : StagedNoteEntry.Direct(dtNote));
        }

        var stableKey = $"odm:{itemOid}:{prompt}";
        var candidate = new ProtocolConceptCandidate(stableKey, extCode, prompt, definition, ConceptKind.DataElement);
        if (!string.IsNullOrWhiteSpace(extCode))
        {
            stagedNotes.Add(StagedNoteEntry.Fallback(stableKey, rawFormOid, itemOid, itemName, extCode));
        }

        var candidateId = SanitizeElementId(itemOid, "Item");
        if (usedIds.Contains(candidateId))
        {
            candidateId = SanitizeElementId($"{formId}.{itemOid}", "Item");
        }

        var itemId = AllocateUniqueElementId(candidateId, "Item", usedIds);
        var placeholderItem = new CollectionItem(
            itemId,
            PlaceholderConceptRef,
            Unit: ucumUnit,
            Terminology: null,
            Required: required,
            Purpose: null,
            ValueKind: valueKind,
            Prompt: prompt);
        return new StagedItem(
            rawFormOid,
            rawGroupOid,
            itemOid,
            itemName,
            resolvableCodeListOid,
            itemDef,
            conditionOids,
            candidate,
            placeholderItem);
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
            var groupCode = BoundTerminologyToken(oid);
            var codes = new List<string>();
            var entries = new List<CodeSystemEntryData>();
            var seenBoundedCodes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var item in Children(cl, "CodeListItem").Concat(Children(cl, "EnumeratedItem")))
            {
                var rawCode = Attr(item, "CodedValue");
                if (string.IsNullOrWhiteSpace(rawCode))
                {
                    continue;
                }

                codes.Add(rawCode);
                var boundedCode = BoundTerminologyToken(rawCode)!;
                if (!seenBoundedCodes.Add(boundedCode))
                {
                    continue;
                }

                var decode = TranslatedText(Child(item, "Decode"));
                var display = FirstNonBlank(decode, rawCode)!;
                var itemExtCode = ExtractExtCode(item);
                var submissionValue = !string.Equals(boundedCode, rawCode, StringComparison.Ordinal)
                    ? rawCode
                    : itemExtCode;
                entries.Add(new CodeSystemEntryData(
                    Code: boundedCode,
                    Display: display,
                    GroupCode: groupCode,
                    SubmissionValue: submissionValue));
            }

            var externalEl = Child(cl, "ExternalCodeList");
            var externalDict = FirstNonBlank(
                Attr(externalEl, "href"),
                Attr(externalEl, "ref"),
                Attr(externalEl, "Dictionary"));
            var externalVersion = Attr(externalEl, "Version");

            result[oid] = new ParsedCodeList(oid, name, codes, entries, externalDict, externalVersion);
        }

        return result;
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
            var effectiveVal = meta.Item.Terminology is not null
                ? BoundTerminologyToken(rawVal) ?? rawVal
                : rawVal;
            literals.Add(new LiteralExpression(ToTypedRuleValue(effectiveVal, meta.Item.ExpectedKind)));
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

    internal sealed record StagedOdmDocument(
        string? StudyTitle,
        string? StudyOid,
        IReadOnlyList<StagedForm> StagedForms,
        IReadOnlyList<ProtocolConceptCandidate> UniqueCandidates,
        IReadOnlyList<ProtocolTerminologyCandidate> UniqueTerminologyCandidates,
        IReadOnlyList<string> Problems,
        IReadOnlyList<StagedNoteEntry> StagedNotes,
        IReadOnlyList<Rule>? Rules,
        bool PayloadTooLarge,
        IReadOnlyDictionary<string, string> FormIdByOid,
        IReadOnlyDictionary<string, XElement> ConditionDefs,
        bool HasRangeChecks)
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyFormMap =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly IReadOnlyDictionary<string, XElement> EmptyConditionMap =
            new Dictionary<string, XElement>(StringComparer.Ordinal);

        public bool CanResolve => !PayloadTooLarge && Problems.Count == 0 && StagedForms.Count > 0;

        public static StagedOdmDocument Failed(
            string? title,
            IReadOnlyList<string> problems,
            IReadOnlyList<ProtocolImportNote> notes,
            bool payloadTooLarge = false,
            string? studyOid = null) =>
            new(
                title,
                studyOid,
                [],
                [],
                [],
                problems,
                [.. notes.Select(StagedNoteEntry.Direct)],
                null,
                payloadTooLarge,
                EmptyFormMap,
                EmptyConditionMap,
                HasRangeChecks: false);

        public static StagedOdmDocument FailedWithStagedNotes(
            string? title,
            string? studyOid,
            IReadOnlyList<string> problems,
            IReadOnlyList<StagedNoteEntry> stagedNotes) =>
            new(
                title,
                studyOid,
                [],
                [],
                [],
                problems,
                stagedNotes,
                null,
                PayloadTooLarge: false,
                EmptyFormMap,
                EmptyConditionMap,
                HasRangeChecks: false);

        public List<ProtocolImportNote> MaterializePreResolutionNotes() =>
            DeduplicateNotes([
                .. StagedNotes
                    .Select(n => n.DirectNote ?? n.UnboundTerminologySpec?.Note)
                    .Where(n => n is not null)
                    .Select(n => n!),
            ]);

        public List<ProtocolImportNote> MaterializeNotes(
            IReadOnlyDictionary<string, ProtocolConceptResolution> resolutions,
            IReadOnlySet<string>? boundCodeListOids = null)
        {
            var list = new List<ProtocolImportNote>(StagedNotes.Count);
            foreach (var entry in StagedNotes)
            {
                if (entry.DirectNote is { } direct)
                {
                    list.Add(direct);
                }
                else if (entry.UnboundTerminologySpec is { } unbound
                         && (boundCodeListOids is null || !boundCodeListOids.Contains(unbound.CodeListOid)))
                {
                    list.Add(unbound.Note);
                }
                else if (entry.FallbackSpec is { } fb
                         && resolutions.TryGetValue(fb.StableKey, out var res)
                         && res.Concept is not null
                         && res.UsedFallbackSponsorConcept)
                {
                    list.Add(new ProtocolImportNote(
                        $"{fb.RawFormOid}/{fb.ItemOid}",
                        $"Unmapped terminology code '{fb.ExtCode}' on item '{fb.ItemName}' is not registered in the tenant Concept Registry; bound to a sponsor-local concept."));
                }
            }

            return DeduplicateNotes(list);
        }

        public List<CollectionSpec> BuildPlaceholderSpecs() =>
            [.. StagedForms.Select(f => new CollectionSpec(f.FormId, f.FormName, [.. f.Items.Select(i => i.PlaceholderItem)]))];

        public OdmImportResult ToFailedResult() =>
            new(StudyTitle, [], Problems, MaterializePreResolutionNotes(), PayloadTooLarge: PayloadTooLarge, StudyOid: StudyOid);
    }

    internal sealed record StagedForm(string FormId, string FormName, IReadOnlyList<StagedItem> Items);

    internal sealed record StagedItem(
        string RawFormOid,
        string RawGroupOid,
        string ItemOid,
        string ItemName,
        string? CodeListOid,
        XElement ItemDef,
        IReadOnlyList<string> ConditionOids,
        ProtocolConceptCandidate Candidate,
        CollectionItem PlaceholderItem);

    internal sealed record StagedNoteEntry(
        ProtocolImportNote? DirectNote,
        FallbackNoteSpec? FallbackSpec,
        UnboundTerminologyNoteSpec? UnboundTerminologySpec = null)
    {
        public static StagedNoteEntry Direct(ProtocolImportNote note) => new(note, null, null);

        public static StagedNoteEntry Fallback(string stableKey, string rawFormOid, string itemOid, string itemName, string extCode) =>
            new(null, new FallbackNoteSpec(stableKey, rawFormOid, itemOid, itemName, extCode), null);

        public static StagedNoteEntry WhenTerminologyUnbound(string codeListOid, ProtocolImportNote note) =>
            new(null, null, new UnboundTerminologyNoteSpec(codeListOid, note));
    }

    internal sealed record FallbackNoteSpec(string StableKey, string RawFormOid, string ItemOid, string ItemName, string ExtCode);

    internal sealed record UnboundTerminologyNoteSpec(string CodeListOid, ProtocolImportNote Note);

    private sealed record ImportedItemMeta(
        string FormId,
        string RawFormOid,
        string RawGroupOid,
        string ItemOid,
        string ItemName,
        CollectionItem Item,
        XElement ItemDef);

    private sealed record ParsedCodeList(
        string Oid,
        string Name,
        IReadOnlyList<string> Codes,
        IReadOnlyList<CodeSystemEntryData> Entries,
        string? ExternalDictionary,
        string? ExternalVersion);
}
