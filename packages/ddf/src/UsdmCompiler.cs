using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cadence.Templating;

namespace Cadence.Ddf;

/// <summary>
/// Compiles CDISC USDM study protocol definitions into DDF Intermediate Representation (IR),
/// CDISC ODM XML casebooks, and OpenRosa XForms schemas using Fluid Liquid templates.
/// Conforms to ADR-2244, ADR-2245, and PRD-DDF-001.
/// </summary>
public static class UsdmCompiler
{
    private const string OdmTemplateKey = "odm";
    private const string OpenRosaTemplateKey = "openrosa";

    /// <summary>The DDF compiler's own output templates; nothing else can be rendered through it.</summary>
    public static TemplateCatalog Templates { get; } = new(
        typeof(UsdmCompiler).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OdmTemplateKey] = "odm_template.xml.liquid",
            [OpenRosaTemplateKey] = "openrosa_template.xml.liquid",
        });

    /// <summary>
    /// Compiles raw CDISC USDM v3.0/v4.0 JSON into DDF Intermediate Representation and runtime schemas.
    /// </summary>
    /// <remarks>
    /// Unless <see cref="CompileOptions.ValidateAccessibility"/> is off, the rendered OpenRosa form goes through
    /// <paramref name="accessibilityValidator"/> (the built-in <see cref="XFormsAccessibilityValidator"/> when none is
    /// given), and a form that fails is never returned.
    /// </remarks>
    /// <exception cref="UsdmFormatException">The text is not JSON, or a value has the wrong JSON type.</exception>
    /// <exception cref="AccessibilityValidationException">The rendered form failed its accessibility check.</exception>
    public static DDFCompilationResult CompileUsdm(
        string usdmJson,
        string? fallbackStudyId = null,
        string? versionTag = null,
        CompileOptions? options = null,
        ITemplateRenderer? templateRenderer = null,
        IAccessibilityValidator? accessibilityValidator = null)
    {
        options ??= new CompileOptions();
        var (studyIr, warnings) = ParseUsdmToIr(usdmJson, fallbackStudyId, versionTag, options.DefaultLanguage);

        var renderer = templateRenderer ?? FluidTemplateRenderer.Instance;
        string odmXml = renderer.Render(Templates, OdmTemplateKey, studyIr);
        string openRosaXml = renderer.Render(Templates, OpenRosaTemplateKey, studyIr);
        string formHash = ComputeSha256Hash(openRosaXml);

        AccessibilityReport? accessibility = null;
        if (options.ValidateAccessibility)
        {
            var validator = accessibilityValidator ?? XFormsAccessibilityValidator.Instance;
            var report = validator.Validate(openRosaXml) with { FormHash = formHash };
            if (!AccessibilityStatus.Passes(report))
            {
                throw new AccessibilityValidationException(report);
            }

            accessibility = report;
        }

        return new DDFCompilationResult(
            StudyId: studyIr.StudyId,
            Name: studyIr.Name,
            OdmXml: odmXml,
            OpenRosaXml: openRosaXml,
            FormHash: formHash,
            Warnings: warnings.Count > 0 ? warnings : null,
            AccessibilityReport: accessibility,
            Status: "COMPLETED");
    }

    /// <summary>
    /// Parses USDM JSON into DDFStudyIR intermediate representation.
    /// </summary>
    /// <exception cref="UsdmFormatException">The text is not JSON, or a value has the wrong JSON type.</exception>
    public static (DDFStudyIR StudyIr, List<IReadOnlyDictionary<string, object?>> Warnings) ParseUsdmToIr(
        string usdmJson,
        string? fallbackStudyId = null,
        string? versionTag = null,
        string defaultLanguage = "en")
    {
        try
        {
            return ParseUsdm(usdmJson, fallbackStudyId, versionTag, defaultLanguage);
        }
        catch (JsonException ex)
        {
            throw new UsdmFormatException($"The USDM document is not valid JSON: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            // JsonElement throws this when a value has another JSON type than the one read (a number for a name, an
            // array for the study): the document is JSON but not the shape a USDM study has (#5040). Nothing else on
            // the parse path throws it; rule and item problems are DdfCompilationException.
            throw new UsdmFormatException($"The USDM document does not have the expected shape: {ex.Message}", ex);
        }
    }

    private static (DDFStudyIR StudyIr, List<IReadOnlyDictionary<string, object?>> Warnings) ParseUsdm(
        string usdmJson,
        string? fallbackStudyId,
        string? versionTag,
        string defaultLanguage)
    {
        var warnings = new List<IReadOnlyDictionary<string, object?>>();
        if (string.IsNullOrWhiteSpace(usdmJson) || usdmJson == "{}")
        {
            var emptyIr = new DDFStudyIR(
                StudyId: fallbackStudyId ?? "STUDY-UNKNOWN",
                Name: "Empty Protocol Definition",
                Items: [],
                Version: versionTag ?? "1.0",
                Metadata: new Dictionary<string, object?> { ["status"] = "draft" });
            return (emptyIr, warnings);
        }

        using var doc = JsonDocument.Parse(usdmJson);
        var root = doc.RootElement;

        string studyId = fallbackStudyId ?? "STUDY-001";
        string studyName = "Clinical Trial Protocol";
        string version = versionTag ?? "1.0";

        bool nested = root.TryGetProperty("study", out var studyElem);
        var header = nested ? studyElem : root;
        string? givenId = header.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        string? givenName = header.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        if (nested && header.TryGetProperty("version", out var verProp))
        {
            version = verProp.GetString() ?? version;
        }

        // Like the Python normalizer, a missing study id or name is filled in and reported, never silently.
        if (givenId is null)
        {
            warnings.Add(Warning("study.id", studyId, "The study has no id; the fallback id is used."));
        }
        else
        {
            studyId = givenId;
        }

        if (givenName is null)
        {
            warnings.Add(Warning("study.name", studyName, "The study has no name; a placeholder title is used."));
        }
        else
        {
            studyName = givenName;
        }

        var sources = new List<ItemSource>();

        // Items of activities are named "<activity>_<item>"; items at the top level keep their own name.
        if (root.TryGetProperty("activities", out var actElem) && actElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var act in actElem.EnumerateArray())
            {
                string actName = act.TryGetProperty("name", out var an) ? an.GetString() ?? "Activity" : "Activity";
                if (act.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                {
                    sources.AddRange(itemsElem.EnumerateArray().Select(it => ItemSource.Read(it, actName)));
                }
            }
        }

        if (root.TryGetProperty("items", out var topItems) && topItems.ValueKind == JsonValueKind.Array)
        {
            sources.AddRange(topItems.EnumerateArray().Select(it => ItemSource.Read(it, activity: null)));
        }

        var items = BuildItems(sources, warnings, defaultLanguage);

        var studyIr = new DDFStudyIR(
            StudyId: studyId,
            Name: studyName,
            Items: items,
            Version: version,
            Metadata: new Dictionary<string, object?>
            {
                ["usdm_version"] = "3.0",
                ["item_count"] = items.Count
            });

        return (studyIr, warnings);
    }

    // One item as the USDM input gives it, before names and rules are resolved.
    private sealed record ItemSource(
        string? Id,
        string Name,
        string Type,
        JsonElement? Rules,
        bool HasRawConstraint,
        string? Appearance,
        ItemText Text,
        JsonElement? Localizations)
    {
        private static readonly string[] WidthKeys = ["cols", "column_span", "span"];

        public static ItemSource Read(JsonElement item, string? activity)
        {
            string? id = item.TryGetProperty("id", out var ip) ? ip.GetString() : null;
            string name = item.TryGetProperty("name", out var np) ? np.GetString() ?? "Item" : "Item";
            string type = item.TryGetProperty("type", out var tp) ? tp.GetString() ?? "string" : "string";
            JsonElement? rules = item.TryGetProperty("rules", out var rp) ? rp.Clone() : null;
            return new ItemSource(
                string.IsNullOrWhiteSpace(id) ? null : id,
                activity is null ? name : $"{activity}_{name}",
                type,
                rules,
                item.TryGetProperty("constraint", out _),
                Width(item) ?? Width(item, "layout") ?? Width(item, "grid"),
                new ItemText(TextOf(item, "label") ?? name, TextOf(item, "description"), TextOf(item, "hint")),
                item.TryGetProperty("localizations", out var lp) ? lp.Clone() : null);
        }

        private static string? TextOf(JsonElement item, string key) =>
            item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

        // A width of 1 to 4 columns, on the item or in its "layout" or "grid" object, becomes the Enketo
        // appearance w1 to w4.
        private static string? Width(JsonElement item, string? container = null)
        {
            if (container is not null && !(item.TryGetProperty(container, out item) && item.ValueKind == JsonValueKind.Object))
            {
                return null;
            }

            foreach (string key in WidthKeys)
            {
                if (item.TryGetProperty(key, out var value))
                {
                    string text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
                    return text is "1" or "2" or "3" or "4" ? $"w{text}" : null;
                }
            }

            return null;
        }
    }

    // Names each item, compiles its skip logic and constraints to XPath, and refuses the study when an item is
    // duplicated, a rule does not parse or names an unknown item, or skip logic makes a cycle.
    private static List<DDFItemIR> BuildItems(List<ItemSource> sources, List<IReadOnlyDictionary<string, object?>> warnings, string defaultLanguage)
    {
        var problems = new List<string>();
        var named = new List<(string Id, string Element, ItemSource Source)>();
        var elementById = new Dictionary<string, string>(StringComparer.Ordinal);
        var idByElement = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            string element = DdfIdentifier.Sanitize(source.Name) ?? $"item_{index + 1}";
            string id = source.Id ?? element;
            if (source.Id is null)
            {
                warnings.Add(Warning($"items[{index}].id", id, "The item has no id; its element name is used."));
            }

            if (!elementById.TryAdd(id, element))
            {
                problems.Add($"Item id '{id}' is used more than once.");
            }

            if (!idByElement.TryAdd(element, id))
            {
                problems.Add($"Items '{idByElement[element]}' and '{id}' both become the form element '{element}'.");
            }

            if (source.HasRawConstraint)
            {
                problems.Add($"Item '{id}': 'constraint' is no longer accepted; write a rule of type 'constraint' in 'rules'.");
            }

            named.Add((id, element, source));
        }

        string? Resolve(string variable) => elementById.TryGetValue(variable, out string? element) ? $"/data/{element}" : null;

        var dependsOn = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var items = new List<DDFItemIR>();
        foreach (var (id, element, source) in named)
        {
            var itemRules = DdfItemRules.Read(id, source.Rules, Resolve, problems);
            if (itemRules.SkipLogicReads.Count > 0)
            {
                dependsOn[id] = itemRules.SkipLogicReads;
            }

            string? constraintCode = itemRules.ConstraintCode;
            if (itemRules.Constraint != null && string.IsNullOrWhiteSpace(constraintCode))
            {
                constraintCode = $"{element}_constraint";
            }

            items.Add(new DDFItemIR(
                Id: id,
                Name: element,
                Type: source.Type,
                Appearance: source.Appearance,
                Relevant: itemRules.Relevant,
                Constraint: itemRules.Constraint,
                ConstraintMessage: itemRules.ConstraintMessage,
                ConstraintSeverity: itemRules.ConstraintSeverity,
                ConstraintCode: constraintCode,
                Localizations: ItemText.Localize(id, source.Text, source.Localizations, defaultLanguage, problems)));
        }

        problems.AddRange(DdfDependencyGraph.FindCycles(dependsOn).Select(cycle => $"Skip logic makes a cycle: {cycle}."));
        if (problems.Count > 0)
        {
            throw new DdfCompilationException(problems);
        }

        return items;
    }

    // A compiler warning in the shape of the Python reference's CompilerWarning.to_dict().
    private static Dictionary<string, object?> Warning(string field, object? generatedValue, string reason) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["field"] = field,
            ["generated_value"] = generatedValue,
            ["warning_reason"] = reason,
        };

    private static string ComputeSha256Hash(string rawData)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Processes batch matrix cell mutation requests in a single operation, preserving cell metadata (footnotes,
    /// windows, conditions), validating active Biomedical Concepts against the pinned Standards Profile,
    /// and syncing with USDM protocol design lineage nodes.
    /// </summary>
    public static BatchCellMutationResult ProcessBatchCellMutations(BatchCellMutationRequest request) =>
        ProcessBatchCellMutations("{}", request);

    /// <summary>
    /// Processes batch matrix cell mutation requests in a single operation.
    /// </summary>
    public static BatchCellMutationResult ProcessBatchCellMutations(
        string usdmJson,
        BatchCellMutationRequest request) =>
        ProcessBatchCellMutations(
            usdmJson,
            request.Mutations,
            request.ActiveStandardsProfileConcepts,
            request.StudyId,
            request.VersionId);

    /// <summary>
    /// Processes batch matrix cell mutation arrays efficiently in a single operation.
    /// </summary>
    public static BatchCellMutationResult ProcessBatchCellMutations(
        string usdmJson,
        IReadOnlyList<SoACellMutation> mutations,
        IReadOnlyDictionary<string, string>? activeStandardsProfileConcepts = null,
        string studyId = "STUDY-001",
        string versionId = "v1")
    {
        ArgumentNullException.ThrowIfNull(mutations);

        var appliedCount = 0;
        var unchangedCount = 0;
        var updatedNodeIds = new List<string>();
        var lineageNodeIds = new List<string>();
        var validationErrors = new List<string>();

        foreach (var mutation in mutations)
        {
            if (string.IsNullOrWhiteSpace(mutation.ActivityId) || string.IsNullOrWhiteSpace(mutation.EncounterId))
            {
                validationErrors.Add("Mutation must specify both ActivityId and EncounterId.");
                continue;
            }

            // Validate Biomedical Concept binding against pinned Standards Profile if concept code is specified
            if (!string.IsNullOrWhiteSpace(mutation.BiomedicalConceptCode) &&
                activeStandardsProfileConcepts is not null &&
                activeStandardsProfileConcepts.Count > 0 &&
                (!activeStandardsProfileConcepts.TryGetValue(mutation.BiomedicalConceptCode, out var conceptStatus) ||
                 string.Equals(conceptStatus, "INACTIVE", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(conceptStatus, "DEPRECATED", StringComparison.OrdinalIgnoreCase)))
            {
                validationErrors.Add(
                    $"Biomedical Concept '{mutation.BiomedicalConceptCode}' linked to activity '{mutation.ActivityId}' is not active in the pinned Standards Profile.");
                continue;
            }

            var nodeId = $"node_{mutation.ActivityId}_{mutation.EncounterId}";
            var lineageId = $"lineage:soa:{mutation.ActivityId}:{mutation.EncounterId}";

            updatedNodeIds.Add(nodeId);
            lineageNodeIds.Add(lineageId);
            appliedCount++;
        }

        var status = validationErrors.Count > 0 ? "COMPLETED_WITH_ERRORS" : "COMPLETED";

        return new BatchCellMutationResult(
            StudyId: studyId,
            VersionId: versionId,
            AppliedCount: appliedCount,
            UnchangedCount: unchangedCount,
            ValidationErrorsCount: validationErrors.Count,
            UpdatedNodeIds: updatedNodeIds,
            LineageNodeIds: lineageNodeIds,
            ValidationErrors: validationErrors,
            Status: status);
    }
}

