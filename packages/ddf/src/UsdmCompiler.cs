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
        var (studyIr, warnings) = ParseUsdmToIr(usdmJson, fallbackStudyId, versionTag);

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
    public static (DDFStudyIR StudyIr, List<IReadOnlyDictionary<string, object?>> Warnings) ParseUsdmToIr(
        string usdmJson,
        string? fallbackStudyId = null,
        string? versionTag = null)
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

        if (root.TryGetProperty("study", out var studyElem))
        {
            if (studyElem.TryGetProperty("id", out var idProp)) studyId = idProp.GetString() ?? studyId;
            if (studyElem.TryGetProperty("name", out var nameProp)) studyName = nameProp.GetString() ?? studyName;
            if (studyElem.TryGetProperty("version", out var verProp)) version = verProp.GetString() ?? version;
        }
        else
        {
            if (root.TryGetProperty("id", out var idProp)) studyId = idProp.GetString() ?? studyId;
            if (root.TryGetProperty("name", out var nameProp)) studyName = nameProp.GetString() ?? studyName;
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

        var items = BuildItems(sources, warnings);

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
    private sealed record ItemSource(string? Id, string Name, string Type, JsonElement? Rules, bool HasRawConstraint, string? Appearance)
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
                Width(item) ?? Width(item, "layout") ?? Width(item, "grid"));
        }

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
    private static List<DDFItemIR> BuildItems(List<ItemSource> sources, List<IReadOnlyDictionary<string, object?>> warnings)
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
                warnings.Add(new Dictionary<string, object?>
                {
                    ["field"] = $"items[{index}].id",
                    ["generated_value"] = id,
                    ["warning_reason"] = "The item has no id; its element name is used.",
                });
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

            items.Add(new DDFItemIR(
                Id: id,
                Name: element,
                Type: source.Type,
                Appearance: source.Appearance,
                Relevant: itemRules.Relevant,
                Constraint: itemRules.Constraint,
                ConstraintMessage: itemRules.ConstraintMessage));
        }

        problems.AddRange(DdfDependencyGraph.FindCycles(dependsOn).Select(cycle => $"Skip logic makes a cycle: {cycle}."));
        if (problems.Count > 0)
        {
            throw new DdfCompilationException(problems);
        }

        return items;
    }

    private static string ComputeSha256Hash(string rawData)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
