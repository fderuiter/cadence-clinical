using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.Kernel.Standards;

namespace Cadence.Standards.Blocks;

/// <summary>
/// The content of a Metadata Registry library item of kind <see cref="LibraryItemKind.CollectionSpecification"/>: a
/// reusable set of items to collect, such as a vital signs panel (ADR-2503). Items name CDISC Biomedical Concepts by
/// their public C-code, so the same content can be drafted in any tenant; instantiating it resolves each code to the
/// tenant's concept version (<see cref="CollectionSpecificationInstantiator"/>).
/// </summary>
/// <param name="Name">What the specification collects, for example "Vital Signs".</param>
/// <param name="Description">An optional longer description.</param>
/// <param name="Items">The items, in collection order.</param>
public sealed record CollectionSpecificationDefinition(string Name, string? Description, IReadOnlyList<CollectionItemDefinition> Items);

/// <summary>One item of a <see cref="CollectionSpecificationDefinition"/>.</summary>
/// <param name="Id">The item's id within the specification.</param>
/// <param name="BiomedicalConcept">The CDISC Biomedical Concept the item records, by C-code (for example <c>C25298</c>).</param>
/// <param name="Unit">The UCUM unit when the item is a quantity.</param>
/// <param name="Terminology">The pinned terminology its values come from, when it is coded.</param>
/// <param name="Required">Whether the item must be collected.</param>
public sealed record CollectionItemDefinition(
    string Id,
    string BiomedicalConcept,
    string? Unit,
    TerminologyReference? Terminology,
    bool Required);

/// <summary>A code-system version by its URI and version, optionally narrowed to one codelist (by C-code).</summary>
/// <param name="CodeSystem">The code-system URI, for example <c>https://library.cdisc.org/api/mdr/ct/packages/sdtmct</c>.</param>
/// <param name="Version">The pinned version, for example <c>2024-09-27</c>. There is no "latest".</param>
/// <param name="Codelist">The codelist within that version, when the values come from one.</param>
public sealed record TerminologyReference(string CodeSystem, string Version, string? Codelist);

/// <summary>
/// Reads and checks collection specification content. The format is strict: unknown or repeated properties, a blank
/// name, an item id that is not a valid protocol element id or appears twice, a concept that is not a C-code, a blank
/// or spaced unit and a terminology reference without a version are all problems, and every problem is reported.
/// </summary>
/// <example>
/// <code>
/// {
///   "name": "Vital Signs",
///   "items": [
///     { "id": "SYSBP", "concept": "C25298", "unit": "mm[Hg]", "required": true }
///   ]
/// }
/// </code>
/// </example>
public static partial class CollectionSpecificationJson
{
    /// <summary>The most items one specification may hold.</summary>
    public const int MaxItems = 500;

    /// <summary>The longest item id; instantiation prefixes it with the specification id, and the result stays a valid protocol element id.</summary>
    public const int MaxItemIdLength = 63;

    private static readonly string[] RootProperties = ["name", "description", "items"];
    private static readonly string[] ItemProperties = ["id", "concept", "unit", "terminology", "required"];
    private static readonly string[] TerminologyProperties = ["codeSystem", "version", "codelist"];

    /// <summary>Reads <paramref name="content"/>; the definition is null when there are problems.</summary>
    public static (CollectionSpecificationDefinition? Definition, IReadOnlyList<string> Problems) Read(JsonElement content)
    {
        var problems = new List<string>();
        if (content.ValueKind != JsonValueKind.Object)
        {
            return (null, ["A collection specification is a JSON object."]);
        }

        CheckProperties(content, RootProperties, "The collection specification", problems);
        string? name = Text(content, "name", "The collection specification", required: true, problems);
        string? description = Text(content, "description", "The collection specification", required: false, problems);

        var items = new List<CollectionItemDefinition>();
        if (!content.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            problems.Add("'items' must be a non-empty array.");
        }
        else if (array.GetArrayLength() > MaxItems)
        {
            problems.Add($"'items' holds {array.GetArrayLength()} items; at most {MaxItems} are allowed.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var element in array.EnumerateArray())
            {
                if (ReadItem(element, index++, problems) is { } item)
                {
                    if (!seen.Add(item.Id))
                    {
                        problems.Add($"Item id '{item.Id}' is used more than once.");
                    }

                    items.Add(item);
                }
            }
        }

        return problems.Count > 0 || name is null
            ? (null, problems)
            : (new CollectionSpecificationDefinition(name, description, items), problems);
    }

    /// <summary>Whether <paramref name="id"/> is a valid item id.</summary>
    public static bool IsItemId(string id) => ItemIdPattern().IsMatch(id);

    private static CollectionItemDefinition? ReadItem(JsonElement element, int index, List<string> problems)
    {
        string where = $"Item {index}";
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{where}: an item is a JSON object.");
            return null;
        }

        int before = problems.Count;
        string? id = Text(element, "id", where, required: true, problems);
        if (id is not null)
        {
            where = $"Item '{id}'";
            if (!IsItemId(id))
            {
                problems.Add($"{where}: the id must start with a letter and hold up to {MaxItemIdLength} characters: letters, digits, '_', '-', ':' or '.'.");
            }
        }

        CheckProperties(element, ItemProperties, where, problems);

        string? concept = Text(element, "concept", where, required: true, problems);
        if (concept is not null && !ConceptCodePattern().IsMatch(concept))
        {
            problems.Add($"{where}: 'concept' must be a CDISC Biomedical Concept C-code such as 'C25298'.");
        }

        string? unit = Text(element, "unit", where, required: false, problems);
        if (unit is not null && unit.Any(char.IsWhiteSpace))
        {
            problems.Add($"{where}: 'unit' must be a UCUM code, which has no spaces.");
        }

        bool required = false;
        if (element.TryGetProperty("required", out var flag))
        {
            if (flag.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                required = flag.GetBoolean();
            }
            else
            {
                problems.Add($"{where}: 'required' must be true or false.");
            }
        }

        TerminologyReference? terminology = null;
        if (element.TryGetProperty("terminology", out var binding))
        {
            terminology = ReadTerminology(binding, where, problems);
        }

        return problems.Count > before ? null : new CollectionItemDefinition(id!, concept!, unit, terminology, required);
    }

    private static TerminologyReference? ReadTerminology(JsonElement binding, string where, List<string> problems)
    {
        if (binding.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{where}: 'terminology' is an object with 'codeSystem', 'version' and an optional 'codelist'.");
            return null;
        }

        string context = $"{where} terminology";
        CheckProperties(binding, TerminologyProperties, context, problems);
        string? codeSystem = Text(binding, "codeSystem", context, required: true, problems);
        if (codeSystem is not null && !Uri.TryCreate(codeSystem, UriKind.Absolute, out _))
        {
            problems.Add($"{context}: 'codeSystem' must be an absolute URI.");
        }

        string? version = Text(binding, "version", context, required: true, problems);
        string? codelist = Text(binding, "codelist", context, required: false, problems);
        if (codelist is not null && !ConceptCodePattern().IsMatch(codelist))
        {
            problems.Add($"{context}: 'codelist' must be a codelist C-code such as 'C66742'.");
        }

        return codeSystem is null || version is null ? null : new TerminologyReference(codeSystem, version, codelist);
    }

    private static void CheckProperties(JsonElement element, string[] allowed, string where, List<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in element.EnumerateObject().Select(property => property.Name))
        {
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"{where}: unknown property '{name}'.");
            }
            else if (!seen.Add(name))
            {
                problems.Add($"{where}: property '{name}' appears more than once.");
            }
        }
    }

    private static string? Text(JsonElement element, string property, string where, bool required, List<string> problems)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            if (required)
            {
                problems.Add($"{where}: '{property}' is required.");
            }

            return null;
        }

        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            problems.Add($"{where}: '{property}' must be non-blank text.");
            return null;
        }

        string text = value.GetString()!;
        if (text.Trim().Length != text.Length)
        {
            problems.Add($"{where}: '{property}' has leading or trailing spaces.");
            return null;
        }

        return text;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.:-]{0,62}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ItemIdPattern();

    [GeneratedRegex("^C[0-9]{1,10}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ConceptCodePattern();
}

/// <summary>
/// Checks collection specification content when a <c>DraftLibraryItem</c> command drafts one, so content that cannot
/// be instantiated is refused before it can be approved.
/// </summary>
public sealed class CollectionSpecificationContentValidator : ILibraryContentValidator
{
    /// <inheritdoc/>
    public LibraryItemKind Kind => LibraryItemKind.CollectionSpecification;

    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(JsonElement content) => CollectionSpecificationJson.Read(content).Problems;
}
