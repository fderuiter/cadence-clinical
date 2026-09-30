using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Cadence.Kernel.Standards;

namespace Cadence.Standards.CdiscLibrary;

/// <summary>
/// Reads a CDISC Library product response (the contract in <c>docs/CDISC/cdisc-library-api.json</c>) into a
/// <see cref="StandardPackage"/>. Supported shapes:
/// <list type="bullet">
/// <item>tabulation and collection implementation guides (<c>/mdr/sdtmig/{version}</c>, <c>/mdr/sendig/{version}</c>,
/// <c>/mdr/cdashig/{version}</c>): <c>classes → datasets|domains → datasetVariables|fields</c>;</item>
/// <item>ADaM products (<c>/mdr/adam/{product}</c>): <c>dataStructures → analysisVariableSets → analysisVariables</c>.</item>
/// </list>
/// Codelist links (<c>/mdr/ct/packages/{type}-{date}/codelists/{C-code}</c>) become codelist codes on the variable and
/// <see cref="CodeSystemVersionRef"/>s (<c>{type}</c>, <c>{date}</c>) on the package. The reader only reshapes public
/// content; it never adds rules or messages of its own (K-RULE-2).
/// </summary>
public static class CdiscLibraryProductReader
{
    private const string Publisher = "CDISC";

    /// <summary>Reads a product response.</summary>
    /// <param name="product">The product JSON.</param>
    /// <param name="standardCode">The registry code for the standard, for example <c>SDTMIG</c>.</param>
    /// <exception cref="FormatException">The JSON is not a supported CDISC Library product.</exception>
    public static StandardPackage Read(JsonElement product, string standardCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(standardCode);
        if (product.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A CDISC Library product is a JSON object.");
        }

        var terminology = new SortedSet<CodeSystemVersionRef>(Comparer<CodeSystemVersionRef>.Create(
            (a, b) => string.CompareOrdinal(a.ToString(), b.ToString())));
        List<DatasetDefinitionData> datasets;
        if (product.TryGetProperty("classes", out var classes))
        {
            datasets = ReadClasses(classes, terminology);
        }
        else if (product.TryGetProperty("dataStructures", out var structures))
        {
            datasets = ReadDataStructures(structures, terminology);
        }
        else
        {
            throw new FormatException("The product has neither 'classes' nor 'dataStructures'.");
        }

        return new StandardPackage(
            standardCode,
            RequiredString(product, "version"),
            OptionalString(product, "label") ?? RequiredString(product, "name"),
            Publisher,
            Href(product, "self"),
            OptionalString(product, "effectiveDate") is { } date
                ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null,
            datasets,
            [],
            [.. terminology]);
    }

    /// <summary>Reads a product response from UTF-8 JSON text.</summary>
    public static StandardPackage Read(ReadOnlySpan<byte> utf8Json, string standardCode)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
        using var document = JsonDocument.ParseValue(ref reader);
        return Read(document.RootElement, standardCode);
    }

    private static List<DatasetDefinitionData> ReadClasses(JsonElement classes, SortedSet<CodeSystemVersionRef> terminology)
    {
        var datasets = new List<DatasetDefinitionData>();
        foreach (var @class in Array(classes, "classes"))
        {
            var className = OptionalString(@class, "name");
            if (FirstProperty(@class, "datasets", "domains") is not { } classDatasets)
            {
                continue;
            }

            foreach (var dataset in Array(classDatasets, "datasets"))
            {
                var variablesElement = FirstProperty(dataset, "datasetVariables", "fields")
                    ?? throw new FormatException($"Dataset {OptionalString(dataset, "name")} has no 'datasetVariables' or 'fields'.");
                datasets.Add(new DatasetDefinitionData(
                    RequiredString(dataset, "name"),
                    RequiredString(dataset, "label"),
                    className,
                    OptionalString(dataset, "datasetStructure"),
                    OptionalInt(dataset, "ordinal") ?? datasets.Count + 1,
                    [.. Array(variablesElement, "datasetVariables").Select((v, i) => ReadVariable(v, i + 1, terminology))]));
            }
        }

        return datasets;
    }

    private static List<DatasetDefinitionData> ReadDataStructures(JsonElement structures, SortedSet<CodeSystemVersionRef> terminology)
    {
        var datasets = new List<DatasetDefinitionData>();
        foreach (var structure in Array(structures, "dataStructures"))
        {
            var variables = new List<VariableDefinitionData>();
            foreach (var set in Array(structure.GetProperty("analysisVariableSets"), "analysisVariableSets"))
            {
                var setName = OptionalString(set, "name");
                foreach (var variable in Array(set.GetProperty("analysisVariables"), "analysisVariables"))
                {
                    // ADaM groups variables into sets; the set is the variable's role within the structure.
                    variables.Add(ReadVariable(variable, variables.Count + 1, terminology) with { Role = setName });
                }
            }

            datasets.Add(new DatasetDefinitionData(
                RequiredString(structure, "name"),
                RequiredString(structure, "label"),
                OptionalString(structure, "className"),
                OptionalString(structure, "description"),
                OptionalInt(structure, "ordinal") ?? datasets.Count + 1,
                variables));
        }

        return datasets;
    }

    private static VariableDefinitionData ReadVariable(JsonElement variable, int position, SortedSet<CodeSystemVersionRef> terminology)
    {
        var name = RequiredString(variable, "name");
        var codelists = new List<string>();
        foreach (var link in Links(variable, "codelist"))
        {
            var (codelist, codeSystem) = ParseCodelistHref(link, name);
            codelists.Add(codelist);
            if (codeSystem is not null)
            {
                terminology.Add(codeSystem);
            }
        }

        return new VariableDefinitionData(
            name,
            RequiredString(variable, "label"),
            OptionalInt(variable, "ordinal") ?? position,
            ParseDataType(OptionalString(variable, "simpleDatatype"), name),
            OptionalString(variable, "role"),
            ParseCore(OptionalString(variable, "core"), name),
            [.. codelists.Distinct(StringComparer.Ordinal)],
            variable.TryGetProperty("valueList", out var values) && values.ValueKind == JsonValueKind.Array
                ? [.. values.EnumerateArray().Select(v => v.GetString() ?? string.Empty)]
                : [],
            OptionalString(variable, "describedValueDomain"),
            OptionalString(variable, "description"));
    }

    /// <summary>Splits <c>/mdr/ct/packages/sdtmct-2024-09-27/codelists/C66741</c> into the codelist and its package.</summary>
    internal static (string Codelist, CodeSystemVersionRef? Package) ParseCodelistHref(string href, string variable)
    {
        var segments = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var codelistsAt = System.Array.IndexOf(segments, "codelists");
        if (codelistsAt < 0 || codelistsAt + 1 >= segments.Length)
        {
            throw new FormatException($"Variable {variable} links to codelist '{href}', which is not a CDISC Library codelist path.");
        }

        var codelist = segments[codelistsAt + 1];
        var packagesAt = System.Array.IndexOf(segments, "packages");
        if (packagesAt < 0 || packagesAt + 1 >= codelistsAt)
        {
            // /mdr/root/ct/... links name a codelist without a package version.
            return (codelist, null);
        }

        var package = segments[packagesAt + 1];
        var dash = package.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0 || dash == package.Length - 1)
        {
            throw new FormatException($"Variable {variable} links to CT package '{package}', which is not '<type>-<date>'.");
        }

        return (codelist, new CodeSystemVersionRef(package[..dash], package[(dash + 1)..]));
    }

    private static StandardDataType ParseDataType(string? value, string variable) => value switch
    {
        "Char" => StandardDataType.Char,
        "Num" => StandardDataType.Num,
        _ => throw new FormatException($"Variable {variable} has simpleDatatype '{value}'; expected Char or Num."),
    };

    private static VariableCore ParseCore(string? value, string variable) => value switch
    {
        "Req" => VariableCore.Required,
        "Exp" => VariableCore.Expected,
        "Perm" => VariableCore.Permissible,
        "Cond" => VariableCore.Conditional,
        _ => throw new FormatException($"Variable {variable} has core '{value}'; expected Req, Exp, Perm or Cond."),
    };

    private static IEnumerable<string> Links(JsonElement element, string relation)
    {
        if (!element.TryGetProperty("_links", out var links) || !links.TryGetProperty(relation, out var link))
        {
            yield break;
        }

        // The published contract types a link as one object; live responses may carry an array of them.
        if (link.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in link.EnumerateArray())
            {
                yield return RequiredString(item, "href");
            }
        }
        else if (link.ValueKind == JsonValueKind.Object)
        {
            yield return RequiredString(link, "href");
        }
    }

    private static JsonElement? FirstProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? Href(JsonElement element, string relation) => Links(element, relation).FirstOrDefault();

    private static JsonElement.ArrayEnumerator Array(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
            : throw new FormatException($"'{name}' must be an array.");

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) ?? throw new FormatException($"'{name}' is required.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i) ? i : null;
}
