using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.Topology;

namespace Cadence.Testing.GoldenMaster;

/// <summary>
/// Joins a golden-master <c>input_*.json</c> fixture file against its paired <c>output_*.json</c> file by
/// <c>scenario_id</c>, producing one <see cref="GoldenMasterScenario"/> per matched pair. Per
/// <c>02_technical_design.md</c> §6.1: fails fast -- throwing <see cref="InvalidOperationException"/>
/// synchronously and eagerly (this method never returns a lazily-evaluated sequence), not silently on some
/// later enumeration -- when a <c>scenario_id</c> the input file declares has no corresponding entry in the
/// output file, so a real, requested scenario can never silently vanish from the replayed set by having its
/// missing expectation simply excluded.
/// <para>
/// This check is deliberately one-directional (input -&gt; output only), not a symmetric
/// "every id must appear in both files" check: verified directly against this slice's own real fixtures,
/// <c>output_expected_nominal.json</c> is one shared output file (30 total results) legitimately paired
/// against <b>two</b> different input files -- <c>input_valid_nominal.json</c> (19 <c>SCN-*</c> ids) and
/// <c>input_valid_edge_cases.json</c> (11 <c>EDGE-*</c> ids) -- per the design's own file-pairing. A symmetric
/// check would spuriously fail both real pairings (each input file "lacks" the other's ids in the shared
/// output file) even though neither pairing has an actual gap. An output-only id -- one no current input file
/// requests -- has nothing to replay and is simply not returned; it is the opposite direction (a request with
/// no verified expectation) that this loader treats as the fixture-integrity failure the ticket's fail-fast
/// requirement exists to catch.
/// </para>
/// </summary>
public static class GoldenMasterFixtureLoader
{
    /// <summary>
    /// Loads and joins the two fixture files, returning one <see cref="GoldenMasterScenario"/> per
    /// <c>scenario_id</c> the input file declares, in the order the input file itself declares them
    /// (multi-step scenarios, e.g. <c>SCN-001-A</c> then <c>SCN-001-B</c>, rely on that order for later
    /// stateful replay). An output-file entry whose <c>scenario_id</c> no input entry declares is ignored --
    /// see this type's own remarks for why that is the correct, asymmetric behavior rather than a fixture
    /// error. Relative paths are resolved against the repository root -- the nearest ancestor of the running
    /// test assembly that contains <c>Cadence.slnx</c> -- independent of the test host's current working
    /// directory, so callers can pass the same repo-relative path this repo's tickets and design docs already
    /// use (e.g. <c>"docs/migration/slices/execution-subjects/fixtures/input_valid_nominal.json"</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A <c>scenario_id</c> the input file declares has no corresponding entry in the output file, a file has
    /// no top-level scenarios/results array, an entry has no (or a blank) <c>scenario_id</c>, or a file
    /// declares the same <c>scenario_id</c> twice.
    /// </exception>
    public static IReadOnlyList<GoldenMasterScenario> LoadScenarios(string inputPath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string resolvedInputPath = ResolveFixturePath(inputPath);
        string resolvedOutputPath = ResolveFixturePath(outputPath);

        using JsonDocument inputDocument = JsonDocument.Parse(File.ReadAllText(resolvedInputPath));
        using JsonDocument outputDocument = JsonDocument.Parse(File.ReadAllText(resolvedOutputPath));

        (List<string> inputOrder, Dictionary<string, JsonElement> inputById) =
            IndexByScenarioId(inputDocument.RootElement, "scenarios", inputPath);
        (_, Dictionary<string, JsonElement> outputById) =
            IndexByScenarioId(outputDocument.RootElement, "results", outputPath);

        List<string> onlyInInput = inputById.Keys.Except(outputById.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

        if (onlyInInput.Count > 0)
        {
            throw new InvalidOperationException(
                $"Fixture pair mismatch between '{inputPath}' and '{outputPath}': " +
                $"scenario_id(s) requested by the input file have no corresponding output entry: " +
                $"[{string.Join(", ", onlyInInput)}]. An incomplete fixture pair must never silently pass by " +
                "omitting the orphaned scenario.");
        }

        var scenarios = new List<GoldenMasterScenario>(inputOrder.Count);
        foreach (string scenarioId in inputOrder)
        {
            scenarios.Add(BuildScenario(scenarioId, inputById[scenarioId], outputById[scenarioId]));
        }

        return scenarios;
    }

    private static (List<string> Order, Dictionary<string, JsonElement> ByScenarioId) IndexByScenarioId(
        JsonElement root, string arrayPropertyName, string sourcePath)
    {
        if (!root.TryGetProperty(arrayPropertyName, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Fixture file '{sourcePath}' has no top-level '{arrayPropertyName}' array.");
        }

        var order = new List<string>();
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (!element.TryGetProperty("scenario_id", out JsonElement idProperty) ||
                idProperty.GetString() is not { Length: > 0 } scenarioId)
            {
                throw new InvalidOperationException(
                    $"Fixture file '{sourcePath}' contains a '{arrayPropertyName}' entry with no 'scenario_id'.");
            }

            if (!byId.TryAdd(scenarioId, element))
            {
                throw new InvalidOperationException(
                    $"Fixture file '{sourcePath}' contains a duplicate scenario_id '{scenarioId}'.");
            }

            order.Add(scenarioId);
        }

        return (order, byId);
    }

    private static GoldenMasterScenario BuildScenario(string scenarioId, JsonElement input, JsonElement output)
    {
        string kind = input.TryGetProperty("kind", out JsonElement kindProperty) ? kindProperty.GetString() ?? "unknown" : "unknown";

        string? method = null;
        string? route = null;
        IReadOnlyDictionary<string, string>? headers = null;
        JsonElement? requestBody = null;

        if (string.Equals(kind, "http", StringComparison.Ordinal))
        {
            method = input.TryGetProperty("method", out JsonElement methodProperty) ? methodProperty.GetString() : null;
            route = input.TryGetProperty("route", out JsonElement routeProperty) ? routeProperty.GetString() : null;

            if (input.TryGetProperty("headers", out JsonElement headersProperty) && headersProperty.ValueKind == JsonValueKind.Object)
            {
                var headerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty header in headersProperty.EnumerateObject())
                {
                    headerMap[header.Name] = header.Value.GetString() ?? string.Empty;
                }

                headers = headerMap;
            }

            if (input.TryGetProperty("body", out JsonElement bodyProperty))
            {
                requestBody = bodyProperty.Clone();
            }
        }

        int? expectedStatus = output.TryGetProperty("status", out JsonElement statusProperty) && statusProperty.ValueKind == JsonValueKind.Number
            ? statusProperty.GetInt32()
            : null;

        JsonElement? expectedBody = output.TryGetProperty("body", out JsonElement expectedBodyProperty) ? expectedBodyProperty.Clone() : null;

        string? expectedDetail = expectedBody is { ValueKind: JsonValueKind.Object } bodyObject &&
            bodyObject.TryGetProperty("detail", out JsonElement detailProperty) && detailProperty.ValueKind == JsonValueKind.String
            ? detailProperty.GetString()
            : null;

        JsonElement? dbAssertions = output.TryGetProperty("db_assertions", out JsonElement dbAssertionsProperty) ? dbAssertionsProperty.Clone() : null;
        JsonElement? auditAssertions = output.TryGetProperty("audit_assertions", out JsonElement auditAssertionsProperty) ? auditAssertionsProperty.Clone() : null;

        return new GoldenMasterScenario
        {
            ScenarioId = scenarioId,
            Kind = kind,
            Method = method,
            Route = route,
            Headers = headers,
            RequestBody = requestBody,
            ExpectedStatus = expectedStatus,
            ExpectedBody = expectedBody,
            ExpectedDetail = expectedDetail,
            DbAssertions = dbAssertions,
            AuditAssertions = auditAssertions,
            Input = input.Clone(),
            Expected = output.Clone(),
        };
    }

    /// <summary>
    /// Resolves a repo-relative fixture path against the nearest ancestor of the running assembly's own
    /// directory that contains <c>Cadence.slnx</c> (or legacy <c>Cadence.sln</c> / <c>global.json</c>), so this
    /// loader works identically regardless of the test host's current working directory.
    /// Absolute paths pass through unchanged.
    /// </summary>
    private static string ResolveFixturePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        string repoRoot = RepositoryRoot.Find(AppContext.BaseDirectory);
        return Path.Combine(repoRoot, path);
    }
}
