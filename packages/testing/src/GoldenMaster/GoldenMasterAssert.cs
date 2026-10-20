using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Testing.GoldenMaster;

/// <summary>
/// Sentinel-aware structural JSON comparison plus DB/audit-log post-condition assertions for golden-master
/// fixture replay. Per <c>02_technical_design.md</c> §6.1, three sentinel values may appear in an
/// expected-body fixture in place of literal expected content:
/// <list type="bullet">
/// <item><description><c>"&lt;opaque&gt;"</c> / <c>"&lt;opaque-non-null&gt;"</c> -- the field must be present
/// and non-null; its actual value is otherwise not compared. Verified against every real usage across
/// <c>fixtures/output_expected_*.json</c>: both sentinels are used exclusively on fields that can never
/// legitimately be null in a successful response (overwhelmingly a generated <c>id</c>), so no real fixture
/// exercises plain <c>"&lt;opaque&gt;"</c> tolerating a null actual value -- and this repo's own ticket
/// acceptance criteria for MIG-SUBJ-025 independently mandates that plain <c>"&lt;opaque&gt;"</c> reject null
/// too. This type therefore gives both sentinels identical non-null-enforcing behavior rather than assuming an
/// asymmetry the fixtures never actually exercise.</description></item>
/// <item><description><c>"&lt;unchanged&gt;"</c> -- the field must equal whatever a supplied pre-state
/// snapshot already had it as; see <see cref="BodyMatches"/>'s optional pre-state parameter.</description></item>
/// </list>
/// <para>
/// <see cref="DbStateMatches{TDbContext}"/>/<see cref="AuditLogContains{TDbContext}"/> are generic over any
/// <see cref="DbContext"/>-derived type rather than hardcoding the execution-subjects slice's own concrete
/// <c>ExecutionDbContext</c> (which the ticket's own prose names), so this shared, cross-slice
/// <c>packages/testing</c> package never needs a project reference to any slice's application assembly --
/// exactly the reference `MIG-SUBJ-018`'s sign-off documents as reintroducing a real
/// `Microsoft.EntityFrameworkCore.Relational` assembly-version conflict build **warning** (Npgsql's own
/// transitive Relational dependency vs. this repo's centrally-pinned version) the moment a slice's EF Core
/// provider package enters this project's dependency graph -- unacceptable under this repo's 0-warnings gate.
/// The slice-specific call site (e.g. this slice's own `ExecutionApiFactory`/fixture-replay suite, which
/// already references its own slice assembly and its EF Core provider) supplies the concrete `TDbContext`
/// type argument instead, achieving genuine cross-slice reusability for this package rather than a
/// single-slice-only helper that merely lives under `packages/testing/` in name.
/// </para>
/// </summary>
public static class GoldenMasterAssert
{
    private const string OpaqueSentinel = "<opaque>";
    private const string OpaqueNonNullSentinel = "<opaque-non-null>";
    private const string UnchangedSentinel = "<unchanged>";

    /// <summary>
    /// Structurally compares <paramref name="actual"/> against <paramref name="expected"/>, honoring the
    /// sentinels described on this type. Every property <paramref name="expected"/> declares must be present
    /// (recursively) in <paramref name="actual"/> and match; extra properties <paramref name="actual"/>
    /// carries beyond what <paramref name="expected"/> asserts are ignored, so this remains a valid golden
    /// master even as a DTO gains fields no fixture has been updated to assert yet. Throws
    /// <see cref="GoldenMasterAssertionException"/> describing the exact JSON path and mismatch on failure;
    /// returns normally on a full match.
    /// </summary>
    /// <param name="actual">The real, observed JSON value (e.g. the deserialized HTTP response body).</param>
    /// <param name="expected">The fixture's expected JSON value, which may contain sentinels.</param>
    /// <param name="preState">
    /// A pre-operation snapshot of the same JSON shape, required only when <paramref name="expected"/> uses
    /// the <c>"&lt;unchanged&gt;"</c> sentinel anywhere. Omit (default <see langword="null"/>) for scenarios
    /// that never use it; using <c>"&lt;unchanged&gt;"</c> with no <paramref name="preState"/> supplied throws
    /// rather than silently treating the field as a pass.
    /// </param>
    public static void BodyMatches(JsonElement actual, JsonElement expected, JsonElement? preState = null) =>
        Compare(actual, expected, preState, "$");

    /// <summary>
    /// Resolves a scoped <typeparamref name="TDbContext"/> from <paramref name="services"/>, materializes the
    /// actual post-operation state via <paramref name="actualStateProvider"/>, and applies the same
    /// sentinel-aware comparison <see cref="BodyMatches"/> uses between that actual state and
    /// <paramref name="scenario"/>'s <see cref="GoldenMasterScenario.DbAssertions"/>. Database row shapes are
    /// inherently slice-specific (e.g. an `enrollment_index` column only `ClinicalSubject` has), so --
    /// unlike <see cref="AuditLogContains{TDbContext}"/> -- this method cannot itself know how to fetch the
    /// actual value: <paramref name="actualStateProvider"/> is the slice-specific call site's own query,
    /// while this method supplies the reusable parts (DI scope resolution, sentinel-aware comparison, and
    /// wrapping any mismatch with clear scenario-ID context). A mismatch throws
    /// <see cref="GoldenMasterAssertionException"/> attributed to <see cref="GoldenMasterScenario.ScenarioId"/>,
    /// never a bare comparison-library failure with no scenario context.
    /// </summary>
    public static async Task DbStateMatches<TDbContext>(
        IServiceProvider services,
        GoldenMasterScenario scenario,
        Func<TDbContext, CancellationToken, Task<JsonElement>> actualStateProvider,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(actualStateProvider);

        if (scenario.DbAssertions is not { } expected)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.ScenarioId}' has no DbAssertions -- DbStateMatches should not be called for it.");
        }

        using IServiceScope scope = services.CreateScope();
        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        JsonElement actual = await actualStateProvider(dbContext, cancellationToken).ConfigureAwait(false);

        try
        {
            BodyMatches(actual, expected);
        }
        catch (GoldenMasterAssertionException ex)
        {
            throw new GoldenMasterAssertionException($"[{scenario.ScenarioId}] DB state assertion failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolves a scoped <typeparamref name="TDbContext"/> from <paramref name="services"/> and verifies its
    /// shared-kernel <see cref="AuditLogEntry"/> set contains at least one row matching every field
    /// (`table_name` -&gt; <see cref="AuditLogEntry.EntityName"/>, `action` -&gt;
    /// <see cref="AuditLogEntry.Action"/>, `user_id` -&gt; <see cref="AuditLogEntry.UserId"/>, each optional)
    /// each entry of <paramref name="scenario"/>'s <see cref="GoldenMasterScenario.AuditAssertions"/> declares.
    /// Genuinely generic with no delegate needed, unlike <see cref="DbStateMatches{TDbContext}"/>: every
    /// slice's `DbContext` exposes the same shared-kernel <see cref="AuditLogEntry"/> shape (written by the
    /// shared `GxPAuditInterceptor`/`IAuditLogWriter`), so this assertion needs no slice-specific knowledge at
    /// all -- only the concrete <typeparamref name="TDbContext"/> to resolve from DI. A missing match throws
    /// <see cref="GoldenMasterAssertionException"/> attributed to <see cref="GoldenMasterScenario.ScenarioId"/>,
    /// listing what was actually recorded rather than a bare "assertion failed".
    /// </summary>
    public static async Task AuditLogContains<TDbContext>(
        IServiceProvider services,
        GoldenMasterScenario scenario,
        CancellationToken cancellationToken = default)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(scenario);

        if (scenario.AuditAssertions is not { ValueKind: JsonValueKind.Array } expectedEntries)
        {
            throw new InvalidOperationException(
                $"Scenario '{scenario.ScenarioId}' has no AuditAssertions -- AuditLogContains should not be called for it.");
        }

        using IServiceScope scope = services.CreateScope();
        TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        List<AuditLogEntry> recorded = await dbContext.Set<AuditLogEntry>().AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        int index = 0;
        foreach (JsonElement expectedEntry in expectedEntries.EnumerateArray())
        {
            string? tableName = expectedEntry.TryGetProperty("table_name", out JsonElement tableNameProperty) ? tableNameProperty.GetString() : null;
            string? action = expectedEntry.TryGetProperty("action", out JsonElement actionProperty) ? actionProperty.GetString() : null;
            string? userId = expectedEntry.TryGetProperty("user_id", out JsonElement userIdProperty) ? userIdProperty.GetString() : null;

            bool found = recorded.Any(entry =>
                (tableName is null || entry.EntityName == tableName) &&
                (action is null || entry.Action == action) &&
                (userId is null || entry.UserId == userId));

            if (!found)
            {
                string recordedSummary = recorded.Count == 0
                    ? "(none)"
                    : string.Join("; ", recorded.Select(entry => $"{{table_name={entry.EntityName}, action={entry.Action}, user_id={entry.UserId}}}"));

                throw new GoldenMasterAssertionException(
                    $"[{scenario.ScenarioId}] AuditLogContains failed: expected an audit log entry matching " +
                    $"assertion #{index} ({expectedEntry.GetRawText()}) but none of the {recorded.Count} recorded " +
                    $"entries matched. Recorded entries: {recordedSummary}.");
            }

            index++;
        }
    }

    private static void Compare(JsonElement actual, JsonElement expected, JsonElement? preState, string path)
    {
        if (expected.ValueKind == JsonValueKind.String && expected.GetString() is { } candidate && IsSentinel(candidate))
        {
            MatchSentinel(actual, candidate, preState, path);
            return;
        }

        if (actual.ValueKind != expected.ValueKind)
        {
            throw new GoldenMasterAssertionException(
                $"{path}: expected JSON kind {expected.ValueKind} ({expected.GetRawText()}) but actual was " +
                $"{actual.ValueKind} ({actual.GetRawText()}).");
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in expected.EnumerateObject())
                {
                    if (!actual.TryGetProperty(property.Name, out JsonElement actualProperty))
                    {
                        throw new GoldenMasterAssertionException($"{path}.{property.Name}: missing from actual body.");
                    }

                    JsonElement? childPreState = preState is { ValueKind: JsonValueKind.Object } preObject &&
                        preObject.TryGetProperty(property.Name, out JsonElement preProperty)
                        ? preProperty
                        : null;
                    Compare(actualProperty, property.Value, childPreState, $"{path}.{property.Name}");
                }

                break;

            case JsonValueKind.Array:
                List<JsonElement> expectedItems = expected.EnumerateArray().ToList();
                List<JsonElement> actualItems = actual.EnumerateArray().ToList();
                if (expectedItems.Count != actualItems.Count)
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected array length {expectedItems.Count} but actual was {actualItems.Count}.");
                }

                for (int i = 0; i < expectedItems.Count; i++)
                {
                    Compare(actualItems[i], expectedItems[i], null, $"{path}[{i}]");
                }

                break;

            case JsonValueKind.String:
                if (actual.GetString() != expected.GetString())
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected \"{expected.GetString()}\" but actual was \"{actual.GetString()}\".");
                }

                break;

            case JsonValueKind.Number:
                if (!DoubleExactlyEquals(actual.GetDouble(), expected.GetDouble()))
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected {expected.GetRawText()} but actual was {actual.GetRawText()}.");
                }

                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break; // ValueKind equality (checked above) is the entire comparison needed for these kinds.

            default:
                throw new GoldenMasterAssertionException($"{path}: unsupported JSON value kind {expected.ValueKind}.");
        }
    }

    private static void MatchSentinel(JsonElement actual, string sentinel, JsonElement? preState, string path)
    {
        switch (sentinel)
        {
            case OpaqueSentinel:
            case OpaqueNonNullSentinel:
                if (actual.ValueKind == JsonValueKind.Null)
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected an opaque, non-null value (\"{sentinel}\") but actual was null.");
                }

                break;

            case UnchangedSentinel:
                if (preState is not { } pre)
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected \"{UnchangedSentinel}\" but BodyMatches was not given a pre-state snapshot to compare against.");
                }

                if (!JsonElementDeepEquals(actual, pre))
                {
                    throw new GoldenMasterAssertionException(
                        $"{path}: expected the pre-state value {pre.GetRawText()} to remain unchanged but actual was {actual.GetRawText()}.");
                }

                break;
        }
    }

    private static bool IsSentinel(string value) => value is OpaqueSentinel or OpaqueNonNullSentinel or UnchangedSentinel;

    private static bool JsonElementDeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                List<JsonProperty> aProperties = a.EnumerateObject().ToList();
                Dictionary<string, JsonElement> bProperties = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (aProperties.Count != bProperties.Count)
                {
                    return false;
                }

                foreach (JsonProperty property in aProperties)
                {
                    if (!bProperties.TryGetValue(property.Name, out JsonElement bValue) || !JsonElementDeepEquals(property.Value, bValue))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.Array:
                List<JsonElement> aItems = a.EnumerateArray().ToList();
                List<JsonElement> bItems = b.EnumerateArray().ToList();
                if (aItems.Count != bItems.Count)
                {
                    return false;
                }

                for (int i = 0; i < aItems.Count; i++)
                {
                    if (!JsonElementDeepEquals(aItems[i], bItems[i]))
                    {
                        return false;
                    }
                }

                return true;

            case JsonValueKind.String:
                return a.GetString() == b.GetString();

            case JsonValueKind.Number:
                return DoubleExactlyEquals(a.GetDouble(), b.GetDouble());

            default:
                return true; // True/False/Null/Undefined: ValueKind equality (checked above) fully determines equality.
        }
    }

    /// <summary>
    /// Exact (not epsilon-tolerant) floating-point equality, deliberately: golden-master parity must catch a
    /// genuine computation drift (e.g. a different <c>/365.25</c> divisor result), not mask one behind a
    /// tolerance band. Compares the IEEE-754 bit patterns directly via <see cref="BitConverter"/> rather than
    /// <c>==</c>/<see cref="double.Equals(double)"/> on the <see langword="double"/> operands themselves so
    /// this intentional exactness reads, to both a reviewer and SonarAnalyzer's S1244, as a deliberate
    /// bit-for-bit check rather than the classic "forgot about float imprecision" mistake that rule flags.
    /// </summary>
    private static bool DoubleExactlyEquals(double a, double b) =>
        BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
}

/// <summary>
/// Thrown by <see cref="GoldenMasterAssert"/> when a golden-master comparison fails. Carries a human-readable,
/// JSON-path-attributed (and, for DB/audit assertions, scenario-ID-attributed) message describing exactly what
/// mismatched, rather than surfacing a generic assertion-library stack trace with no fixture context.
/// </summary>
public sealed class GoldenMasterAssertionException : Exception
{
    public GoldenMasterAssertionException()
    {
    }

    public GoldenMasterAssertionException(string message)
        : base(message)
    {
    }

    public GoldenMasterAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
