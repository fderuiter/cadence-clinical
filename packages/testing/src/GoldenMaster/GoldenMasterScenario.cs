using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Testing.GoldenMaster;

/// <summary>
/// One row of a golden-master fixture pair -- an <c>input_*.json</c> scenario joined against its matching
/// <c>output_*.json</c> result by <c>scenario_id</c>, as produced by
/// <see cref="GoldenMasterFixtureLoader.LoadScenarios"/>. Carries the HTTP method/route/body/headers needed to
/// replay an <c>"http"</c>-kind scenario via <see cref="SendAsync"/>, the expected status/body/detail/DB/audit
/// assertions to verify the response against, and the raw parsed <see cref="Input"/>/<see cref="Expected"/>
/// fixture JSON as an escape hatch for the <c>"service"</c>/<c>"model"</c>/<c>"background-handler"</c> fixture
/// "kind"s this shared loader deliberately does not interpret further -- dispatching those belongs to
/// whichever slice-specific fixture-replay suite actually consumes them (e.g. a future
/// golden-master-fixture-replay ticket for <c>execution-subjects</c>), not this cross-slice harness.
/// @req:PRD-SYS-053
/// </summary>
public sealed class GoldenMasterScenario
{
    /// <summary>The fixture's own <c>scenario_id</c> (e.g. <c>"SCN-001-A"</c>, <c>"EDGE-004"</c>,
    /// <c>"INV-012"</c>).</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The fixture's own <c>kind</c> discriminator (<c>"http"</c>, <c>"service"</c>, <c>"model"</c>,
    /// or <c>"background-handler"</c>). Only <c>"http"</c> scenarios can be dispatched via
    /// <see cref="SendAsync"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The HTTP verb to send (e.g. <c>"POST"</c>), populated only when <see cref="Kind"/> is
    /// <c>"http"</c>.</summary>
    public string? Method { get; init; }

    /// <summary>The request route, including any query string verbatim from the fixture, populated only when
    /// <see cref="Kind"/> is <c>"http"</c>.</summary>
    public string? Route { get; init; }

    /// <summary>Request headers to send, populated only when <see cref="Kind"/> is <c>"http"</c> and the
    /// fixture entry declares a <c>headers</c> object.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>The request body to send as JSON, populated only when <see cref="Kind"/> is <c>"http"</c> and
    /// the fixture entry declares a <c>body</c>.</summary>
    public JsonElement? RequestBody { get; init; }

    /// <summary>The expected HTTP status code, when the paired <c>output_*.json</c> entry's <c>status</c> is a
    /// JSON number. For non-<c>"http"</c> scenario kinds the fixture's own <c>status</c> is instead a
    /// descriptive string (e.g. <c>"n/a (service call, not HTTP)"</c>), in which case this is
    /// <see langword="null"/>.</summary>
    public int? ExpectedStatus { get; init; }

    /// <summary>The expected response body, verbatim from the paired output fixture's <c>body</c> field --
    /// may contain the <c>"&lt;opaque&gt;"</c>/<c>"&lt;opaque-non-null&gt;"</c>/<c>"&lt;unchanged&gt;"</c>
    /// sentinels <see cref="GoldenMasterAssert.BodyMatches"/> understands.</summary>
    public JsonElement? ExpectedBody { get; init; }

    /// <summary>The expected <c>{"detail": ...}</c> error message, extracted from <see cref="ExpectedBody"/>
    /// when it is an object with a string <c>detail</c> property (the shape every error-fixture entry
    /// uses).</summary>
    public string? ExpectedDetail { get; init; }

    /// <summary>Expected post-condition database state (e.g. <c>{"enrollment_index": 1}</c>), verbatim from
    /// the paired output fixture's <c>db_assertions</c> field, for use with
    /// <see cref="GoldenMasterAssert.DbStateMatches{TDbContext}"/>.</summary>
    public JsonElement? DbAssertions { get; init; }

    /// <summary>Expected audit-log entries (an array of <c>{"table_name": ..., "action": ...}</c>-shaped
    /// objects), verbatim from the paired output fixture's <c>audit_assertions</c> field, for use with
    /// <see cref="GoldenMasterAssert.AuditLogContains{TDbContext}"/>.</summary>
    public JsonElement? AuditAssertions { get; init; }

    /// <summary>The complete, raw parsed <c>input_*.json</c> entry this scenario was built from -- an escape
    /// hatch for fixture fields this type does not otherwise surface (e.g. <c>preconditions</c>, <c>args</c>,
    /// <c>context</c>).</summary>
    public JsonElement? Input { get; init; }

    /// <summary>The complete, raw parsed <c>output_*.json</c> entry this scenario was built from -- an escape
    /// hatch for fixture fields this type does not otherwise surface (e.g. <c>exception</c>, <c>note</c>).</summary>
    public JsonElement? Expected { get; init; }

    /// <summary>
    /// Builds and sends the HTTP request this <c>"http"</c>-kind scenario describes. Throws
    /// <see cref="InvalidOperationException"/> for any other <see cref="Kind"/> -- those are dispatched by
    /// whatever slice-specific mechanism understands them, not this shared primitive.
    /// </summary>
    public async Task<HttpResponseMessage> SendAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (!string.Equals(Kind, "http", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' has kind '{Kind}', not 'http' -- SendAsync only dispatches http-kind scenarios.");
        }

        if (Method is null || Route is null)
        {
            throw new InvalidOperationException(
                $"Scenario '{ScenarioId}' is missing 'method'/'route' despite declaring kind == \"http\".");
        }

        using var request = new HttpRequestMessage(new HttpMethod(Method), Route);
        if (RequestBody is { } body)
        {
            request.Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
        }

        if (Headers is not null)
        {
            foreach ((string name, string value) in Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public override string ToString() => ScenarioId;
}
