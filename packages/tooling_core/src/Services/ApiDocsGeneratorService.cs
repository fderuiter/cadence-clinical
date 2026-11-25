using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Generates schema-linked <c>docs/api/*.md</c> reference pages from committed <c>docs/openapi/*_openapi.json</c>
/// contracts using pure Markdown and native HTML <c>&lt;details&gt;</c> disclosure blocks: service maturity badges from
/// <see cref="ServiceTopology"/>, module grouping for <c>kernel.md</c>, HTTP <c>501 Not Implemented</c> stub markers,
/// GxP headers, RBAC/scope/rate-limit metadata, <c>x-kernel-errors</c> domain codes, and one deduplicated schema
/// appendix per page.
/// </summary>
public static partial class ApiDocsGeneratorService
{
    private const string SchemaRefPrefix = "#/components/schemas/";

    private static readonly string[] HttpMethods =
    [
        "get",
        "put",
        "post",
        "delete",
        "options",
        "head",
        "patch",
        "trace",
    ];

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugCharsRegex();

    [GeneratedRegex(@"(`[^`]+`|<!--[\s\S]*?-->|<c>[^<>]+</c>|<em>[^<>]+</em>)", RegexOptions.IgnoreCase)]
    private static partial Regex PreservedMarkdownTokenRegex();

    public static int GenerateApiDocs(
        bool validate = false,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        string specDir = Path.Combine(repoRoot, "docs", "openapi");
        string docsDir = Path.Combine(repoRoot, "docs", "api");

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = validate
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
            if (validate && reader is { HasAuthoritativeIndex: true })
            {
                return ValidateIndexedApiDocs(reader);
            }

            var expected = RenderAll(specDir);
            var drift = FindRenderedDrift(
                expected,
                fileName =>
                {
                    string path = Path.Combine(docsDir, fileName);
                    return File.Exists(path) ? File.ReadAllText(path) : null;
                },
                Directory.Exists(docsDir)
                    ? Directory.GetFiles(docsDir, "*.md").Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!)
                    : []);

            if (drift.Count == 0)
            {
                Console.WriteLine("✔ docs/api matches docs/openapi.");
                return 0;
            }

            if (validate)
            {
                ReportDrift(drift);
                return 1;
            }

            var stale = Directory.Exists(docsDir)
                ? Directory.GetFiles(docsDir, "*.md")
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n) && !expected.ContainsKey(n!))
                    .Select(n => n!)
                    .ToList()
                : [];

            Directory.CreateDirectory(docsDir);
            foreach (var (name, content) in expected)
            {
                File.WriteAllText(Path.Combine(docsDir, name), content);
            }

            foreach (string name in stale)
            {
                File.Delete(Path.Combine(docsDir, name));
            }

            Console.WriteLine($"✔ docs/api regenerated ({drift.Count} file(s) changed).");
            return 0;
        }
    }

    private static int ValidateIndexedApiDocs(GitIndexReader reader)
    {
        var specs = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var indexedSpecs = reader.Paths
            .Where(p => p.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase) &&
                        !p["docs/openapi/".Length..].Contains('/') &&
                        p.EndsWith("_openapi.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);

        foreach (string specRel in indexedSpecs)
        {
            string name = Path.GetFileName(specRel)[..^"_openapi.json".Length];
            if (string.Equals(name, "cdisc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (reader.TryReadText(specRel, out string specJson))
            {
                specs[name] = JsonNode.Parse(specJson) as JsonObject ?? new JsonObject();
            }
        }

        var expected = RenderFromSpecs(specs);
        var existingDocFiles = reader.Paths
            .Where(p => p.StartsWith("docs/api/", StringComparison.OrdinalIgnoreCase) &&
                        !p["docs/api/".Length..].Contains('/') &&
                        p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!);

        var drift = FindRenderedDrift(
            expected,
            fileName => reader.TryReadText($"docs/api/{fileName}", out string docText) ? docText : null,
            existingDocFiles);

        if (drift.Count == 0)
        {
            Console.WriteLine("✔ docs/api matches docs/openapi.");
            return 0;
        }

        ReportDrift(drift);
        return 1;
    }

    private static List<string> FindRenderedDrift(
        SortedDictionary<string, string> expected,
        Func<string, string?> readExisting,
        IEnumerable<string> existingFileNames)
    {
        var drift = new List<string>();
        foreach (var (name, content) in expected)
        {
            string? current = readExisting(name);
            if (current is null)
            {
                drift.Add($"{name} (missing)");
            }
            else if (current.ReplaceLineEndings("\n") != content)
            {
                drift.Add($"{name} (out of date)");
            }
        }

        var stale = existingFileNames
            .Where(n => !expected.ContainsKey(n))
            .OrderBy(n => n, StringComparer.Ordinal);
        drift.AddRange(stale.Select(n => $"{n} (no spec in docs/openapi)"));
        return drift;
    }

    private static void ReportDrift(List<string> drift)
    {
        Console.WriteLine("[ERROR] docs/api differs from docs/openapi:");
        foreach (string line in drift)
        {
            Console.WriteLine($"  - {line}");
        }

        Console.WriteLine("Run 'pnpm cadence generate-api-docs' and commit docs/api.");
    }

    /// <summary>Renders every service and module contract page keyed by file name. The index is always included.</summary>
    public static SortedDictionary<string, string> RenderAll(string specDir)
    {
        var specs = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var specFiles = Directory.Exists(specDir)
            ? Directory.GetFiles(specDir, "*_openapi.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        foreach (string specPath in specFiles)
        {
            string contractName = Path.GetFileName(specPath)[..^"_openapi.json".Length];
            if (string.Equals(contractName, "cdisc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            specs[contractName] = JsonNode.Parse(File.ReadAllText(specPath)) as JsonObject ?? new JsonObject();
        }

        return RenderFromSpecs(specs);
    }

    private static SortedDictionary<string, string> RenderFromSpecs(IReadOnlyDictionary<string, JsonObject> specs)
    {
        var pages = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var summaries = new List<ContractSummary>();

        foreach (var (contractName, spec) in specs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var model = ParseContract(contractName, spec);
            summaries.Add(new ContractSummary(
                contractName,
                model.Maturity,
                model.Operations.Count,
                model.Schemas.Count,
                model.Operations.Count(op => op.IsNotImplementedStub)));
            pages[$"{contractName}.md"] = RenderContractPage(model);
        }

        pages["index.md"] = RenderIndex(summaries);
        return pages;
    }

    private static ContractModel ParseContract(string name, JsonObject spec)
    {
        ServiceMaturity maturity = ServiceTopology.GetContractMaturity(name);
        var schemas = ParseSchemas(spec);
        var schemaSlugs = schemas.ToDictionary(s => s.Name, s => s.AnchorId, StringComparer.Ordinal);
        var operations = ParseOperations(spec, schemaSlugs);
        return new ContractModel(name, maturity, operations, schemas);
    }

    private static List<OperationDoc> ParseOperations(JsonObject spec, IReadOnlyDictionary<string, string> schemaSlugs)
    {
        var result = new List<OperationDoc>();
        if (spec["paths"] is not JsonObject paths)
        {
            return result;
        }

        var usedOperationAnchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, itemNode) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (itemNode is not JsonObject pathItem)
            {
                continue;
            }

            foreach (string method in HttpMethods)
            {
                if (pathItem[method] is not JsonObject op)
                {
                    continue;
                }

                string methodUpper = method.ToUpperInvariant();
                string operationId = op["operationId"]?.GetValue<string>()?.Trim() ?? string.Empty;
                string summary = op["summary"]?.GetValue<string>()?.Trim() ?? string.Empty;
                string description = op["description"]?.GetValue<string>()?.Trim() ?? string.Empty;

                string baseSlug = !string.IsNullOrEmpty(operationId)
                    ? ToSlug($"op-{operationId}")
                    : ToSlug($"op-{methodUpper}-{path}");
                string anchorId = DeduplicateAnchor(baseSlug, usedOperationAnchors);

                var tags = ReadStringArray(op["tags"]);
                var permissions = ReadStringArray(op["x-cadence-permissions"]);
                var scopes = ReadStringArray(op["x-cadence-scopes"]);
                string? rateLimit = op["x-cadence-rate-limit"]?.GetValue<string>();

                bool isAnonymous = false;
                string authMode = HasBearerSecurity(op) ? "BearerAuth" : "None";
                string? signedInReason = null;
                if (op["x-cadence-auth"] is JsonObject authObj)
                {
                    isAnonymous = authObj["anonymous"]?.GetValue<bool>() is true;
                    authMode = authObj["mode"]?.GetValue<string>() ?? authMode;
                    signedInReason = authObj["signed_in_reason"]?.GetValue<string>();
                }

                var parameters = ParseParameters(pathItem, op, schemaSlugs);
                var requestBody = ParseRequestBody(op["requestBody"] as JsonObject, schemaSlugs);
                var responses = ParseResponses(op["responses"] as JsonObject, schemaSlugs);
                var kernelErrors = ParseKernelErrors(op["x-kernel-errors"] as JsonArray);
                bool isStub = responses.Any(r => r.StatusCode == "501");

                result.Add(new OperationDoc(
                    methodUpper,
                    path,
                    operationId,
                    summary,
                    description,
                    anchorId,
                    tags,
                    isAnonymous,
                    authMode,
                    signedInReason,
                    permissions,
                    scopes,
                    rateLimit,
                    parameters,
                    requestBody,
                    responses,
                    kernelErrors,
                    isStub));
            }
        }

        return result;
    }

    private static List<ParameterDoc> ParseParameters(
        JsonObject pathItem,
        JsonObject op,
        IReadOnlyDictionary<string, string> schemaSlugs)
    {
        var merged = new List<ParameterDoc>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonArray? source in new[] { op["parameters"] as JsonArray, pathItem["parameters"] as JsonArray })
        {
            if (source is null)
            {
                continue;
            }

            foreach (JsonNode? node in source)
            {
                if (node is not JsonObject paramObj)
                {
                    continue;
                }

                string name = paramObj["name"]?.GetValue<string>() ?? string.Empty;
                string location = paramObj["in"]?.GetValue<string>() ?? "query";
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string dedupeKey = $"{location}:{name}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                bool required = paramObj["required"]?.GetValue<bool>() is true ||
                                string.Equals(location, "path", StringComparison.OrdinalIgnoreCase);
                string description = paramObj["description"]?.GetValue<string>()?.Trim() ?? string.Empty;
                string typeDisplay = FormatSchemaReferenceOrInline(paramObj["schema"], schemaSlugs);
                merged.Add(new ParameterDoc(name, location, required, typeDisplay, description));
            }
        }

        return merged
            .OrderBy(p => LocationSortOrder(p.Location))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static int LocationSortOrder(string location) => location switch
    {
        "path" => 0,
        "header" => 1,
        "query" => 2,
        _ => 3,
    };

    private static RequestBodyDoc? ParseRequestBody(
        JsonObject? bodyObj,
        IReadOnlyDictionary<string, string> schemaSlugs)
    {
        if (bodyObj?["content"] is not JsonObject contentObj || contentObj.Count == 0)
        {
            return null;
        }

        bool required = bodyObj["required"]?.GetValue<bool>() is true;
        string description = bodyObj["description"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var mediaTypes = new List<(string MediaType, string SchemaDisplay)>();

        foreach (var (mediaType, mediaNode) in contentObj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            JsonNode? schemaNode = (mediaNode as JsonObject)?["schema"];
            mediaTypes.Add((mediaType, FormatSchemaReferenceOrInline(schemaNode, schemaSlugs)));
        }

        return new RequestBodyDoc(required, description, mediaTypes);
    }

    private static List<ResponseDoc> ParseResponses(
        JsonObject? responsesObj,
        IReadOnlyDictionary<string, string> schemaSlugs)
    {
        var list = new List<ResponseDoc>();
        if (responsesObj is null)
        {
            return list;
        }

        foreach (var (status, respNode) in responsesObj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (respNode is not JsonObject respObj)
            {
                continue;
            }

            string description = respObj["description"]?.GetValue<string>()?.Trim() ?? string.Empty;
            var contents = new List<(string MediaType, string SchemaDisplay)>();
            if (respObj["content"] is JsonObject contentObj)
            {
                foreach (var (mediaType, mediaNode) in contentObj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    JsonNode? schemaNode = (mediaNode as JsonObject)?["schema"];
                    contents.Add((mediaType, FormatSchemaReferenceOrInline(schemaNode, schemaSlugs)));
                }
            }

            list.Add(new ResponseDoc(status, description, contents));
        }

        return list;
    }

    private static List<(int Status, string Code)> ParseKernelErrors(JsonArray? errorsArray)
    {
        var result = new List<(int Status, string Code)>();
        if (errorsArray is null)
        {
            return result;
        }

        foreach (JsonNode? item in errorsArray)
        {
            if (item is not JsonObject obj)
            {
                continue;
            }

            string? code = obj["code"]?.GetValue<string>();
            int status = obj["status"]?.GetValue<int>() ?? 0;
            if (!string.IsNullOrWhiteSpace(code) && status > 0)
            {
                result.Add((status, code));
            }
        }

        return result;
    }

    private static List<SchemaDoc> ParseSchemas(JsonObject spec)
    {
        var rawSchemas = new List<(string Name, JsonObject Obj, string AnchorId)>();
        if (spec["components"] is not JsonObject components || components["schemas"] is not JsonObject schemasObj)
        {
            return [];
        }

        var usedAnchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (schemaName, schemaNode) in schemasObj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (schemaNode is not JsonObject schemaJson)
            {
                continue;
            }

            string anchor = DeduplicateAnchor(ToSlug($"schema-{schemaName}"), usedAnchors);
            rawSchemas.Add((schemaName, schemaJson, anchor));
        }

        var slugLookup = rawSchemas.ToDictionary(s => s.Name, s => s.AnchorId, StringComparer.Ordinal);
        var parsed = new List<SchemaDoc>(rawSchemas.Count);
        foreach (var (name, obj, anchorId) in rawSchemas)
        {
            string kind = ReadSchemaKind(obj, slugLookup);
            string description = obj["description"]?.GetValue<string>()?.Trim() ?? string.Empty;
            var enumValues = ReadScalarStringList(obj["enum"] as JsonArray);
            var requiredSet = new HashSet<string>(ReadStringArray(obj["required"]), StringComparer.Ordinal);
            var composition = ParseCompositionRefs(obj, slugLookup);

            var properties = new List<SchemaPropertyDoc>();
            if (obj["properties"] is JsonObject propsObj)
            {
                foreach (var (propName, propNode) in propsObj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    string propType = FormatSchemaReferenceOrInline(propNode, slugLookup);
                    string propDesc = (propNode as JsonObject)?["description"]?.GetValue<string>()?.Trim() ?? string.Empty;
                    properties.Add(new SchemaPropertyDoc(propName, propType, requiredSet.Contains(propName), propDesc));
                }
            }

            string? additionalProps = null;
            if (obj["additionalProperties"] is JsonObject addPropsObj)
            {
                additionalProps = FormatSchemaReferenceOrInline(addPropsObj, slugLookup);
            }
            else if (obj["additionalProperties"] is JsonValue addPropsVal &&
                     addPropsVal.TryGetValue<bool>(out bool allowAddProps) &&
                     allowAddProps)
            {
                additionalProps = "`any`";
            }

            parsed.Add(new SchemaDoc(name, anchorId, kind, description, enumValues, composition, properties, additionalProps));
        }

        return parsed;
    }

    private static List<string> ParseCompositionRefs(JsonObject schemaObj, IReadOnlyDictionary<string, string> slugLookup)
    {
        var items = new List<string>();
        foreach (string keyword in new[] { "allOf", "oneOf", "anyOf" })
        {
            if (schemaObj[keyword] is not JsonArray arr || arr.Count == 0)
            {
                continue;
            }

            var formatted = arr
                .Select(n => FormatSchemaReferenceOrInline(n, slugLookup))
                .Where(s => !string.Equals(s, "`null`", StringComparison.Ordinal))
                .ToList();
            if (formatted.Count > 0)
            {
                items.Add($"`{keyword}`: {string.Join(", ", formatted)}");
            }
        }

        return items;
    }

    private static string ReadSchemaKind(JsonObject schemaObj, IReadOnlyDictionary<string, string> slugLookup)
    {
        string rawType = ExtractJsonTypeString(schemaObj["type"]);
        string? format = schemaObj["format"]?.GetValue<string>();

        if (schemaObj["enum"] is JsonArray)
        {
            return string.IsNullOrEmpty(rawType) ? "enum" : $"{rawType} (enum)";
        }

        if (rawType == "array" && schemaObj["items"] is JsonNode itemsNode)
        {
            return $"array of {FormatSchemaReferenceOrInline(itemsNode, slugLookup)}";
        }

        if (!string.IsNullOrEmpty(rawType))
        {
            return string.IsNullOrEmpty(format) ? rawType : $"{rawType} ({format})";
        }

        if (schemaObj["oneOf"] is JsonArray)
        {
            return "oneOf";
        }

        if (schemaObj["anyOf"] is JsonArray)
        {
            return "anyOf";
        }

        if (schemaObj["allOf"] is JsonArray)
        {
            return "allOf";
        }

        return schemaObj["properties"] is JsonObject ? "object" : "any";
    }

    private static string FormatSchemaReferenceOrInline(JsonNode? node, IReadOnlyDictionary<string, string> slugLookup)
    {
        if (node is not JsonObject obj)
        {
            return "`any`";
        }

        if (obj["$ref"]?.GetValue<string>() is { Length: > 0 } refValue)
        {
            return FormatSchemaRefLink(refValue, slugLookup);
        }

        bool nullable = IsNullableTypeNode(obj["type"]);
        string primaryType = ExtractJsonTypeString(obj["type"]);
        string? format = obj["format"]?.GetValue<string>();

        string core = FormatCoreInlineType(obj, primaryType, format, slugLookup, ref nullable);
        string constraints = FormatInlineConstraints(obj);
        string suffix = nullable ? " \\| `null`" : string.Empty;
        return $"{core}{suffix}{constraints}";
    }

    private static string FormatCoreInlineType(
        JsonObject obj,
        string primaryType,
        string? format,
        IReadOnlyDictionary<string, string> slugLookup,
        ref bool nullable)
    {
        if (primaryType == "array")
        {
            string itemDisplay = FormatSchemaReferenceOrInline(obj["items"], slugLookup);
            return $"array of {itemDisplay}";
        }

        foreach (string keyword in new[] { "oneOf", "anyOf", "allOf" })
        {
            if (obj[keyword] is not JsonArray arr || arr.Count == 0)
            {
                continue;
            }

            var variants = new List<string>();
            foreach (JsonNode? variant in arr)
            {
                if (variant is JsonObject vObj && ExtractJsonTypeString(vObj["type"]) == "null")
                {
                    nullable = true;
                    continue;
                }

                variants.Add(FormatSchemaReferenceOrInline(variant, slugLookup));
            }

            if (variants.Count == 1)
            {
                return variants[0];
            }

            if (variants.Count > 1)
            {
                return string.Join(" \\| ", variants);
            }
        }

        if (string.IsNullOrEmpty(primaryType))
        {
            return obj["properties"] is JsonObject ? "`object`" : "`any`";
        }

        return string.IsNullOrEmpty(format) ? $"`{primaryType}`" : $"`{primaryType}` (`{format}`)";
    }

    private static string FormatInlineConstraints(JsonObject obj)
    {
        var parts = new List<string>();
        if (obj["minLength"]?.GetValue<int>() is int minLen)
        {
            parts.Add($"minLen: {minLen}");
        }

        if (obj["maxLength"]?.GetValue<int>() is int maxLen)
        {
            parts.Add($"maxLen: {maxLen}");
        }

        if (obj["enum"] is JsonArray enumArr && enumArr.Count > 0)
        {
            var vals = ReadScalarStringList(enumArr).Take(8).Select(v => $"`{EscapeInlineCode(v)}`");
            string more = enumArr.Count > 8 ? ", ..." : string.Empty;
            parts.Add($"enum: {string.Join(", ", vals)}{more}");
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join("; ", parts)})";
    }

    private static string FormatSchemaRefLink(string refValue, IReadOnlyDictionary<string, string> slugLookup)
    {
        if (refValue.StartsWith(SchemaRefPrefix, StringComparison.Ordinal))
        {
            string schemaName = refValue[SchemaRefPrefix.Length..];
            if (slugLookup.TryGetValue(schemaName, out string? anchorId))
            {
                return $"[`{EscapeInlineCode(schemaName)}`](#{anchorId})";
            }

            return $"`{EscapeInlineCode(schemaName)}`";
        }

        return $"`{EscapeInlineCode(refValue)}`";
    }

    private static string ExtractJsonTypeString(JsonNode? typeNode)
    {
        if (typeNode is JsonValue val && val.TryGetValue<string>(out string? single))
        {
            return single;
        }

        if (typeNode is JsonArray arr)
        {
            var nonNull = arr
                .Select(n => n?.GetValue<string>())
                .FirstOrDefault(t => !string.IsNullOrEmpty(t) && !string.Equals(t, "null", StringComparison.Ordinal));
            return nonNull ?? "null";
        }

        return string.Empty;
    }

    private static bool IsNullableTypeNode(JsonNode? typeNode)
    {
        if (typeNode is JsonArray arr)
        {
            return arr.Any(n => string.Equals(n?.GetValue<string>(), "null", StringComparison.Ordinal));
        }

        return false;
    }

    private static string RenderContractPage(ContractModel model)
    {
        var sb = new StringBuilder();
        sb.Append($"# {model.Name} API\n\n");
        sb.Append($"Generated from `docs/openapi/{model.Name}_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.\n\n");

        AppendContractMetadataBar(sb, model);
        AppendContractCallouts(sb, model);

        if (model.Operations.Count == 0)
        {
            sb.Append("The service exposes no operations.\n");
            AppendSchemasAppendix(sb, model.Schemas);
            return sb.ToString();
        }

        if (string.Equals(model.Name, "kernel", StringComparison.OrdinalIgnoreCase))
        {
            RenderKernelModuleSections(sb, model.Operations);
        }
        else
        {
            sb.Append("## Operations\n\n");
            AppendOperationsSummaryTable(sb, model.Operations);
            sb.Append('\n');
            foreach (var op in model.Operations)
            {
                AppendOperationDetail(sb, op);
            }
        }

        AppendSchemasAppendix(sb, model.Schemas);
        return sb.ToString();
    }

    private static void AppendContractMetadataBar(StringBuilder sb, ContractModel model)
    {
        string badge = RenderMaturityBadge(model.Maturity);
        string hostDescriptor = DescribeTopologyHost(model.Name);
        sb.Append($"- **Service Maturity:** {badge} ({hostDescriptor})\n");
        sb.Append($"- **OpenAPI 3.1 Contract:** [`{model.Name}_openapi.json`](../openapi/{model.Name}_openapi.json) ({model.Operations.Count} operation(s), {model.Schemas.Count} schema(s))\n\n");
    }

    private static string DescribeTopologyHost(string contractName)
    {
        if (string.Equals(contractName, "gateway", StringComparison.OrdinalIgnoreCase))
        {
            return $"Cadence API Gateway · port `{ServiceTopology.GatewayPort}` · edge OIDC, rate-limiting & step-up `X-Sig-Token` verification";
        }

        if (string.Equals(contractName, "intelligence", StringComparison.OrdinalIgnoreCase))
        {
            return $"Consolidated module hosted in `kernel-api` · port `{ServiceTopology.Get("kernel").Port}` · `/api/v1/ai/*`";
        }

        if (string.Equals(contractName, "execution_forms", StringComparison.OrdinalIgnoreCase))
        {
            return $"Consolidated module hosted in `kernel-api` · port `{ServiceTopology.Get("kernel").Port}` · `/api/v1/execution/forms*`";
        }

        var svc = ServiceTopology.Services.FirstOrDefault(s => string.Equals(s.Name, contractName, StringComparison.OrdinalIgnoreCase));
        if (svc is null)
        {
            return "Standalone .NET 10 contract";
        }

        string prefixes = string.Join(", ", svc.RoutePrefixes.Select(p => $"`/api/v1/{p}`"));
        return $"Port `{svc.Port}` · Gateway prefixes: {prefixes}";
    }

    private static void AppendContractCallouts(StringBuilder sb, ContractModel model)
    {
        if (model.Maturity == ServiceMaturity.Scaffold)
        {
            sb.Append("> **Scaffold Service (`ServiceTopology`):** This host provides production authentication, probes, and OpenAPI contract boundaries, while full domain persistence or downstream workflow coverage remains scheduled on the consolidation roadmap (`ARCHITECTURE.md §3`).\n\n");
        }

        var stubs = model.Operations.Where(op => op.IsNotImplementedStub).ToList();
        if (stubs.Count > 0)
        {
            string stubList = string.Join(", ", stubs.Select(s => $"`{s.Method} {s.Path}` (`{s.OperationId}`)"));
            sb.Append($"> **HTTP 501 Not Implemented Stub(s):** {stubList} intentionally answers `501 Not Implemented` with an RFC 7807 `ProblemDetails` payload until its external integration backend is wired.\n\n");
        }
    }

    private static void RenderKernelModuleSections(StringBuilder sb, List<OperationDoc> operations)
    {
        var kernelOps = new List<OperationDoc>();
        var intelligenceOps = new List<OperationDoc>();
        var executionOps = new List<OperationDoc>();

        foreach (var op in operations)
        {
            switch (ClassifyKernelModule(op))
            {
                case "Intelligence":
                    intelligenceOps.Add(op);
                    break;
                case "Execution":
                    executionOps.Add(op);
                    break;
                default:
                    kernelOps.Add(op);
                    break;
            }
        }

        sb.Append("## Module Overview\n\n");
        sb.Append("`kernel-api` serves the Clinical Kernel alongside consolidated `Intelligence` and `Execution` modules in one transactional composition host:\n\n");
        sb.Append($"- **[Kernel Module](#kernel-module):** {kernelOps.Count} operation(s) covering clinical studies, bitemporal graph nodes, protocol governance, effectivities, locks, proposals, validation rule packs, exports, and commercial entitlements.\n");
        sb.Append($"- **[Intelligence Module](#intelligence-module):** {intelligenceOps.Count} operation(s) in `kernel_openapi.json`; governed `/api/v1/ai/*` operations (including Streamable HTTP MCP `POST /api/v1/ai/mcp`, ledger, task qualification, evaluation runs, liveness, and kill-switch controls) are published in the dedicated [intelligence API](intelligence.md) contract.\n");
        sb.Append($"- **[Execution Module](#execution-module):** {executionOps.Count} kernel subject enrollment, observation, verification, assertion, and casebook operation(s), plus the consolidated [execution_forms API](execution_forms.md) contract (`/api/v1/execution/forms*`) and standalone [execution API](execution.md).\n\n");

        AppendModuleOperationGroup(
            sb,
            "Kernel Module",
            "Core Clinical Kernel read and command endpoints (`Kernel`, `Kernel protocol governance`, `Kernel locks`, `Kernel proposals`, `Kernel validation`, and host health probes).",
            kernelOps);

        AppendModuleOperationGroup(
            sb,
            "Intelligence Module",
            "Governed AI operations hosted on `kernel-api` are published in the dedicated [intelligence API](intelligence.md) contract (`/api/v1/ai/*`, including `POST /api/v1/ai/mcp`).",
            intelligenceOps);

        AppendModuleOperationGroup(
            sb,
            "Execution Module",
            "Kernel-backed subject screening, clinical observation capture/correction/verification, assertion retraction, and participant casebook exports. Companion eCRF form and query endpoints hosted on `kernel-api` are documented in [execution_forms API](execution_forms.md).",
            executionOps);
    }

    private static string ClassifyKernelModule(OperationDoc op)
    {
        if (op.Path.StartsWith("/api/v1/ai/", StringComparison.OrdinalIgnoreCase) ||
            op.Tags.Any(t => t.Contains("Intelligence", StringComparison.OrdinalIgnoreCase) ||
                             t.Contains("AI", StringComparison.OrdinalIgnoreCase)))
        {
            return "Intelligence";
        }

        if (op.Path.StartsWith("/api/v1/execution/", StringComparison.OrdinalIgnoreCase) ||
            op.Path.StartsWith("/api/v1/kernel/enrollments", StringComparison.OrdinalIgnoreCase) ||
            op.Path.StartsWith("/api/v1/kernel/observations", StringComparison.OrdinalIgnoreCase) ||
            op.Path.StartsWith("/api/v1/kernel/assertions", StringComparison.OrdinalIgnoreCase) ||
            op.Tags.Any(t => t.Contains("Execution", StringComparison.OrdinalIgnoreCase) ||
                             t.Contains("observations", StringComparison.OrdinalIgnoreCase) ||
                             t.Contains("enrollments", StringComparison.OrdinalIgnoreCase)))
        {
            return "Execution";
        }

        return "Kernel";
    }

    private static void AppendModuleOperationGroup(StringBuilder sb, string heading, string intro, List<OperationDoc> ops)
    {
        sb.Append($"## {heading}\n\n");
        sb.Append($"{intro}\n\n");
        if (ops.Count == 0)
        {
            sb.Append("_No operations registered in this module group._\n\n");
            return;
        }

        AppendOperationsSummaryTable(sb, ops);
        sb.Append('\n');
        foreach (var op in ops)
        {
            AppendOperationDetail(sb, op);
        }
    }

    private static void AppendOperationsSummaryTable(StringBuilder sb, List<OperationDoc> operations)
    {
        sb.Append("| Method | Path | Operation | Summary | Contract |\n");
        sb.Append("| ------ | ---- | --------- | ------- | -------- |\n");
        foreach (var op in operations)
        {
            string stubFlag = op.IsNotImplementedStub ? " · `501 Stub`" : string.Empty;
            sb.Append($"| `{op.Method}` | `{op.Path}` | {Escape(op.OperationId)} | {Escape(op.Summary)} | [Details](#{op.AnchorId}){stubFlag} |\n");
        }
    }

    private static void AppendOperationDetail(StringBuilder sb, OperationDoc op)
    {
        string stubBadge = op.IsNotImplementedStub
            ? " <span class=\"VPBadge warning\">501 Not Implemented</span>"
            : string.Empty;
        sb.Append($"### `{op.Method}` `{op.Path}`{stubBadge} {{#{op.AnchorId}}}\n\n");

        string opIdText = string.IsNullOrEmpty(op.OperationId) ? "`<unnamed>`" : $"`{EscapeInlineCode(op.OperationId, escapePipe: false)}`";
        sb.Append($"**Operation ID:** {opIdText} — {EscapeParagraph(op.Summary)}\n\n");

        if (!string.IsNullOrEmpty(op.Description) &&
            !string.Equals(op.Description, op.Summary, StringComparison.Ordinal))
        {
            sb.Append($"{EscapeParagraph(op.Description)}\n\n");
        }

        if (op.IsNotImplementedStub)
        {
            sb.Append("> **Stub Endpoint (`501 Not Implemented`):** Returns an RFC 7807 `ProblemDetails` response indicating the external adapter or inference worker is not wired in this build.\n\n");
        }

        sb.Append("<details class=\"api-operation-details\">\n");
        sb.Append("<summary>Authorization, Parameters, Payloads &amp; Responses</summary>\n\n");

        AppendAuthAndPolicyBullets(sb, op);

        if (op.Parameters.Count > 0)
        {
            sb.Append("**Parameters & Headers**\n\n");
            sb.Append("| Name | In | Required | Schema / Constraints | Description |\n");
            sb.Append("| ---- | -- | -------- | -------------------- | ----------- |\n");
            foreach (var p in op.Parameters)
            {
                string reqLabel = p.Required ? "`required`" : "`optional`";
                sb.Append($"| `{EscapeInlineCode(p.Name)}` | `{EscapeInlineCode(p.Location)}` | {reqLabel} | {p.SchemaDisplay} | {Escape(p.Description)} |\n");
            }

            sb.Append('\n');
        }

        if (op.RequestBody is { } body)
        {
            string reqText = body.Required ? "required" : "optional";
            sb.Append($"**Request Body** (`{reqText}`)\n\n");
            if (!string.IsNullOrEmpty(body.Description))
            {
                sb.Append($"{EscapeParagraph(body.Description)}\n\n");
            }

            sb.Append("| Media Type | Schema |\n");
            sb.Append("| ---------- | ------ |\n");
            foreach (var (mediaType, schemaDisplay) in body.MediaTypes)
            {
                sb.Append($"| `{EscapeInlineCode(mediaType)}` | {schemaDisplay} |\n");
            }

            sb.Append('\n');
        }

        if (op.Responses.Count > 0)
        {
            sb.Append("**Responses**\n\n");
            sb.Append("| Status | Description | Media Type | Schema |\n");
            sb.Append("| ------ | ----------- | ---------- | ------ |\n");
            foreach (var resp in op.Responses)
            {
                string statusCell = resp.StatusCode == "501" ? "`501` (`Stub`)" : $"`{EscapeInlineCode(resp.StatusCode)}`";
                if (resp.Contents.Count == 0)
                {
                    sb.Append($"| {statusCell} | {Escape(resp.Description)} | — | — |\n");
                    continue;
                }

                foreach (var (mediaType, schemaDisplay) in resp.Contents)
                {
                    sb.Append($"| {statusCell} | {Escape(resp.Description)} | `{EscapeInlineCode(mediaType)}` | {schemaDisplay} |\n");
                }
            }

            sb.Append('\n');
        }

        if (op.KernelErrors.Count > 0)
        {
            sb.Append("**Registered Kernel Error Codes (`x-kernel-errors`)**\n\n");
            sb.Append("| HTTP Status | Domain Error Code |\n");
            sb.Append("| ----------- | ----------------- |\n");
            foreach (var (status, code) in op.KernelErrors)
            {
                sb.Append($"| `{status.ToString(CultureInfo.InvariantCulture)}` | `{EscapeInlineCode(code)}` |\n");
            }

            sb.Append('\n');
        }

        sb.Append("</details>\n\n");
    }

    private static void AppendAuthAndPolicyBullets(StringBuilder sb, OperationDoc op)
    {
        if (op.IsAnonymous)
        {
            sb.Append("- **Authentication:** `AllowAnonymous` (no bearer token required)\n");
        }
        else
        {
            sb.Append($"- **Authentication:** `BearerAuth` (`{EscapeInlineCode(op.AuthMode, escapePipe: false)}`)\n");
        }

        if (op.Permissions.Count > 0)
        {
            string perms = string.Join(", ", op.Permissions.Select(p => $"`{EscapeInlineCode(p, escapePipe: false)}`"));
            sb.Append($"- **Required Permission(s):** {perms}\n");
        }

        if (!string.IsNullOrWhiteSpace(op.SignedInReason))
        {
            sb.Append($"- **Signed-In Caller Rule:** {EscapeParagraph(op.SignedInReason)}\n");
        }

        if (op.Scopes.Count > 0)
        {
            string scopes = string.Join(", ", op.Scopes.Select(s => $"`{EscapeInlineCode(s, escapePipe: false)}`"));
            sb.Append($"- **Clinical Scope Enforcement:** {scopes}\n");
        }

        if (!string.IsNullOrWhiteSpace(op.RateLimit))
        {
            sb.Append($"- **Rate Limit:** {EscapeParagraph(op.RateLimit)}\n");
        }

        if (op.Tags.Count > 0)
        {
            string tags = string.Join(", ", op.Tags.Select(t => $"`{EscapeInlineCode(t, escapePipe: false)}`"));
            sb.Append($"- **Tags:** {tags}\n");
        }

        sb.Append('\n');
    }

    private static void AppendSchemasAppendix(StringBuilder sb, List<SchemaDoc> schemas)
    {
        if (schemas.Count == 0)
        {
            return;
        }

        sb.Append("## Schemas\n\n");
        sb.Append($"Deduplicated component schemas exported in `components.schemas` ({schemas.Count} total). Operation payloads and responses link here so nested models are defined once per page.\n\n");

        foreach (var schema in schemas)
        {
            sb.Append($"### `{EscapeInlineCode(schema.Name, escapePipe: false)}` {{#{schema.AnchorId}}}\n\n");
            sb.Append($"- **Type:** {schema.Kind}\n");
            if (!string.IsNullOrEmpty(schema.Description))
            {
                sb.Append($"- **Description:** {EscapeParagraph(schema.Description)}\n");
            }

            if (schema.EnumValues.Count > 0)
            {
                string enums = string.Join(", ", schema.EnumValues.Select(v => $"`{EscapeInlineCode(v, escapePipe: false)}`"));
                sb.Append($"- **Allowed Values:** {enums}\n");
            }

            foreach (string comp in schema.Composition)
            {
                sb.Append($"- **Composition:** {comp}\n");
            }

            if (!string.IsNullOrEmpty(schema.AdditionalPropertiesType))
            {
                sb.Append($"- **Additional Properties:** {schema.AdditionalPropertiesType}\n");
            }

            sb.Append('\n');

            if (schema.Properties.Count > 0)
            {
                sb.Append("<details class=\"api-schema-details\">\n");
                sb.Append($"<summary>Properties ({schema.Properties.Count})</summary>\n\n");
                sb.Append("| Property | Type | Required | Description |\n");
                sb.Append("| -------- | ---- | -------- | ----------- |\n");
                foreach (var prop in schema.Properties)
                {
                    string req = prop.Required ? "`required`" : "`optional`";
                    sb.Append($"| `{EscapeInlineCode(prop.Name)}` | {prop.TypeDisplay} | {req} | {Escape(prop.Description)} |\n");
                }

                sb.Append("\n</details>\n\n");
            }
        }
    }

    private static string RenderIndex(List<ContractSummary> contracts)
    {
        int totalOps = contracts.Sum(c => c.Operations);
        int totalSchemas = contracts.Sum(c => c.Schemas);
        int activeCount = contracts.Count(c => c.Maturity == ServiceMaturity.Active);
        int scaffoldCount = contracts.Count(c => c.Maturity == ServiceMaturity.Scaffold);

        var sb = new StringBuilder();
        sb.Append("# API Reference\n\n");
        sb.Append("Generated from `docs/openapi/*_openapi.json` by `pnpm cadence generate-api-docs`. Don't edit this file by hand.\n\n");
        sb.Append("Each page documents the live HTTP operations, authorization policies, GxP headers (`X-Sig-Token`, `Idempotency-Key`, `X-Change-Reason`), RFC 7807 error contracts, and deduplicated `components.schemas` exported directly from the .NET 10 hosts (`pnpm cadence sync-openapi-spec`). The `openapi` quality gate fails whenever any operation omits an `operationId` or `summary` or when `docs/api/*.md` drifts from `docs/openapi/*.json`.\n\n");
        sb.Append($"- **Unified Gateway OpenAPI 3.1 Bundle:** [`cdisc_openapi.json`](../openapi/cdisc_openapi.json)\n");
        sb.Append($"- **Contract Coverage:** `{contracts.Count}` OpenAPI contracts (`{activeCount}` Active, `{scaffoldCount}` Scaffold in `ServiceTopology`), `{totalOps}` documented operations, and `{totalSchemas}` component schemas.\n");
        sb.Append("- **Consolidated Composition Host (`kernel-api`):** [kernel](kernel.md) groups core `Kernel` and kernel-backed `Execution` operations alongside the dedicated module contracts [intelligence](intelligence.md) (including `POST /api/v1/ai/mcp`) and [execution_forms](execution_forms.md).\n");
        sb.Append("- **Legacy Reference Snapshot:** `docs/openapi/legacy/cdisc_openapi.json` retains the frozen Python-era aggregate snapshot solely for unmigrated `apps/web/src/api/types.ts` slices.\n\n");

        sb.Append("| Service | Operations | Schemas | Maturity | Stubs (`501`) | OpenAPI JSON |\n");
        sb.Append("| ------- | ---------- | ------- | -------- | ------------- | ------------ |\n");
        foreach (var svc in contracts)
        {
            string badge = RenderMaturityBadge(svc.Maturity);
            string stubs = svc.StubCount > 0 ? $"`{svc.StubCount}`" : "0";
            sb.Append($"| [{svc.Name}]({svc.Name}.md) | {svc.Operations} | {svc.Schemas} | {badge} | {stubs} | [`{svc.Name}_openapi.json`](../openapi/{svc.Name}_openapi.json) |\n");
        }

        return sb.ToString();
    }

    private static string RenderMaturityBadge(ServiceMaturity maturity) =>
        maturity == ServiceMaturity.Scaffold
            ? "<span class=\"VPBadge warning\">Scaffold</span>"
            : "<span class=\"VPBadge tip\">Active</span>";

    private static bool HasBearerSecurity(JsonObject op)
    {
        if (op["security"] is not JsonArray secArr)
        {
            return false;
        }

        return secArr.OfType<JsonObject>().Any(obj => obj.ContainsKey("BearerAuth"));
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray arr)
        {
            return [];
        }

        return arr
            .Select(n => n?.GetValue<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
    }

    private static List<string> ReadScalarStringList(JsonArray? arr)
    {
        if (arr is null)
        {
            return [];
        }

        var list = new List<string>();
        foreach (JsonNode? item in arr)
        {
            if (item is null)
            {
                continue;
            }

            if (item is JsonValue val && val.TryGetValue<string>(out string? strVal))
            {
                list.Add(strVal);
            }
            else
            {
                list.Add(item.ToJsonString());
            }
        }

        return list;
    }

    private static string ToSlug(string raw)
    {
        string lowered = raw.ToLowerInvariant();
        string slug = NonSlugCharsRegex().Replace(lowered, "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "item" : slug;
    }

    private static string DeduplicateAnchor(string candidate, HashSet<string> used)
    {
        if (used.Add(candidate))
        {
            return candidate;
        }

        int index = 1;
        while (true)
        {
            string suffixed = $"{candidate}-{index.ToString(CultureInfo.InvariantCulture)}";
            if (used.Add(suffixed))
            {
                return suffixed;
            }

            index++;
        }
    }

    private static string Escape(string text) =>
        EscapeVitePressProse(text, escapePipe: true);

    private static string EscapeParagraph(string text) =>
        EscapeVitePressProse(text, escapePipe: false);

    private static string EscapeInlineCode(string text, bool escapePipe = true)
    {
        string normalized = text.Replace('`', '\'').ReplaceLineEndings(" ").Trim();
        return escapePipe ? normalized.Replace("|", "\\|", StringComparison.Ordinal) : normalized;
    }

    private static string EscapeVitePressProse(string text, bool escapePipe)
    {
        string normalized = text.ReplaceLineEndings(" ").Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length + 16);
        int lastIndex = 0;
        foreach (Match match in PreservedMarkdownTokenRegex().Matches(normalized))
        {
            if (match.Index > lastIndex)
            {
                AppendEscapedProseChunk(sb, normalized.AsSpan(lastIndex, match.Index - lastIndex), escapePipe);
            }

            string token = match.Value;
            if (token.StartsWith("<c>", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append('`').Append(EscapeInlineCode(token[3..^4], escapePipe)).Append('`');
            }
            else if (token.StartsWith("<em>", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append('*');
                AppendEscapedProseChunk(sb, token.AsSpan(4, token.Length - 9), escapePipe);
                sb.Append('*');
            }
            else
            {
                sb.Append(escapePipe ? token.Replace("|", "\\|", StringComparison.Ordinal) : token);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < normalized.Length)
        {
            AppendEscapedProseChunk(sb, normalized.AsSpan(lastIndex), escapePipe);
        }

        return sb.ToString();
    }

    private static void AppendEscapedProseChunk(StringBuilder sb, ReadOnlySpan<char> span, bool escapePipe)
    {
        if (span.IsEmpty)
        {
            return;
        }

        string chunk = span.ToString();
        if (escapePipe)
        {
            chunk = chunk.Replace("|", "\\|", StringComparison.Ordinal);
        }

        chunk = chunk
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("{{", "&#123;&#123;", StringComparison.Ordinal)
            .Replace("}}", "&#125;&#125;", StringComparison.Ordinal);
        sb.Append(chunk);
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed record ContractSummary(
        string Name,
        ServiceMaturity Maturity,
        int Operations,
        int Schemas,
        int StubCount);

    private sealed record ContractModel(
        string Name,
        ServiceMaturity Maturity,
        List<OperationDoc> Operations,
        List<SchemaDoc> Schemas);

    private sealed record OperationDoc(
        string Method,
        string Path,
        string OperationId,
        string Summary,
        string Description,
        string AnchorId,
        List<string> Tags,
        bool IsAnonymous,
        string AuthMode,
        string? SignedInReason,
        List<string> Permissions,
        List<string> Scopes,
        string? RateLimit,
        List<ParameterDoc> Parameters,
        RequestBodyDoc? RequestBody,
        List<ResponseDoc> Responses,
        List<(int Status, string Code)> KernelErrors,
        bool IsNotImplementedStub);

    private sealed record ParameterDoc(
        string Name,
        string Location,
        bool Required,
        string SchemaDisplay,
        string Description);

    private sealed record RequestBodyDoc(
        bool Required,
        string Description,
        List<(string MediaType, string SchemaDisplay)> MediaTypes);

    private sealed record ResponseDoc(
        string StatusCode,
        string Description,
        List<(string MediaType, string SchemaDisplay)> Contents);

    private sealed record SchemaDoc(
        string Name,
        string AnchorId,
        string Kind,
        string Description,
        List<string> EnumValues,
        List<string> Composition,
        List<SchemaPropertyDoc> Properties,
        string? AdditionalPropertiesType);

    private sealed record SchemaPropertyDoc(
        string Name,
        string TypeDisplay,
        bool Required,
        string Description);
}
