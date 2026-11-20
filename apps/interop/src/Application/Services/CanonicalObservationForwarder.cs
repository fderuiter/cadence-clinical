using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cadence.Apps.Interop.Application.Services;

/// <summary>
/// Represents one canonical clinical observation derived from a resolved participant diary item (<c>K-TRUTH-1</c>).
/// </summary>
public sealed record CanonicalDiaryObservationEnvelope(
    Guid ObservationId,
    string EnrollmentRef,
    string ConceptRef,
    string ConceptCode,
    ObservationValue Value,
    DateTimeOffset ObservedAt,
    string IdempotencyKey);

/// <summary>
/// Metadata describing a resolved ePRO diary submission to forward into canonical EDC/Kernel observations.
/// </summary>
public sealed record CanonicalObservationForwardingContext(
    string SubmissionId,
    string SubjectCode,
    string StudyCode,
    string DiaryId,
    int VersionIndex,
    DateTimeOffset ObservedAt,
    JsonElement ResolvedAnswers,
    string? InstrumentItemsJson,
    string? EnrollmentRef,
    string TenantCode,
    string ActorUserId,
    IReadOnlyList<string> ActorRoles,
    string ChangeReason);

/// <summary>
/// Outcome of translating and forwarding resolved ePRO diary items into canonical observations (<c>K-TRUTH-1</c>).
/// </summary>
public sealed record CanonicalObservationForwardingReceipt(
    IReadOnlyList<CanonicalDiaryObservationEnvelope> Envelopes,
    int DispatchedCount,
    bool ForwardedOverHttp);

/// <summary>
/// Translates resolved participant ePRO diary answers into typed <see cref="ObservationValue"/> records and
/// forwards them to the canonical Kernel observation endpoint (<c>POST /api/v1/kernel/observations</c>) per <c>K-TRUTH-1</c>.
/// </summary>
public interface ICanonicalObservationForwarder
{
    Task<CanonicalObservationForwardingReceipt> ForwardAsync(
        CanonicalObservationForwardingContext context,
        CancellationToken ct = default);
}

public sealed partial class CanonicalObservationForwarder(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IClock clock,
    ILogger<CanonicalObservationForwarder> logger) : ICanonicalObservationForwarder
{
    public const string HttpClientName = "KernelCanonicalObservations";
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly JsonSerializerOptions KernelWireJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Kernel observation forwarding for submission '{SubmissionId}' concept '{ConceptCode}' returned HTTP {StatusCode}.")]
    public static partial void LogKernelForwardNonSuccess(
        ILogger logger, string submissionId, string conceptCode, int statusCode);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Warning,
        Message = "Kernel observation forwarding for submission '{SubmissionId}' failed transiently: {Reason}")]
    public static partial void LogKernelForwardWarning(
        ILogger logger, string submissionId, string reason);

    public async Task<CanonicalObservationForwardingReceipt> ForwardAsync(
        CanonicalObservationForwardingContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var envelopes = BuildEnvelopes(context);
        if (envelopes.Count == 0)
        {
            return new CanonicalObservationForwardingReceipt(envelopes, 0, ForwardedOverHttp: false);
        }

        string? kernelBaseUrl = configuration["KERNEL_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(kernelBaseUrl))
        {
            return new CanonicalObservationForwardingReceipt(envelopes, 0, ForwardedOverHttp: false);
        }

        string baseUri = kernelBaseUrl.TrimEnd('/');
        string? gatewaySecret = configuration["GATEWAY_SECRET"];
        var client = httpClientFactory.CreateClient(HttpClientName);
        int dispatched = 0;

        foreach (var envelope in envelopes)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}/api/v1/kernel/observations");
                request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, envelope.IdempotencyKey);
                request.Headers.TryAddWithoutValidation(GatewayHeaders.ChangeReason, context.ChangeReason);

                if (!string.IsNullOrWhiteSpace(gatewaySecret)
                    && gatewaySecret.Length >= GatewayAuthenticationOptions.MinimumSecretLength)
                {
                    var identity = new GatewayIdentity(
                        UserId: string.IsNullOrWhiteSpace(context.ActorUserId) ? "svc:interop-epro-forwarder" : context.ActorUserId,
                        Roles: context.ActorRoles.Count > 0 ? [.. context.ActorRoles] : ["subject"],
                        SiteIds: [],
                        TenantId: string.IsNullOrWhiteSpace(context.TenantCode) ? "cadence-local" : context.TenantCode,
                        StudyIds: string.IsNullOrWhiteSpace(context.StudyCode) ? [] : [context.StudyCode],
                        ChangeReason: context.ChangeReason,
                        SubjectCode: context.SubjectCode);

                    foreach (var header in identity.ToSignedHeaders(clock.UtcNow, GatewayIdentity.EncodeSecret(gatewaySecret)))
                    {
                        request.Headers.Remove(header.Key);
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var wireBody = new KernelObservationWriteWirePayload(
                    envelope.ObservationId,
                    envelope.EnrollmentRef,
                    envelope.ConceptRef,
                    envelope.Value,
                    envelope.ObservedAt);
                string json = JsonSerializer.Serialize(wireBody, KernelWireJsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    dispatched++;
                }
                else
                {
                    LogKernelForwardNonSuccess(logger, context.SubmissionId, envelope.ConceptCode, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                LogKernelForwardWarning(logger, context.SubmissionId, ex.Message);
            }
        }

        return new CanonicalObservationForwardingReceipt(envelopes, dispatched, ForwardedOverHttp: dispatched > 0);
    }

    public static IReadOnlyList<CanonicalDiaryObservationEnvelope> BuildEnvelopes(CanonicalObservationForwardingContext context)
    {
        if (context.ResolvedAnswers.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var itemSpecs = ParseInstrumentItems(context.InstrumentItemsJson);
        string enrollmentRef = ResolveEnrollmentRef(context.EnrollmentRef, context.StudyCode, context.SubjectCode);
        int version = Math.Max(1, context.VersionIndex);
        var list = new List<CanonicalDiaryObservationEnvelope>();

        foreach (var prop in context.ResolvedAnswers.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            itemSpecs.TryGetValue(prop.Name, out var spec);
            ObservationValue? observationValue = TranslateAnswer(prop.Name, prop.Value, spec);
            if (observationValue is null || ObservationValue.ProblemOf(observationValue) is not null)
            {
                continue;
            }

            Guid observationGuid = DeterministicGuid($"epro.observation:{context.StudyCode}:{context.SubjectCode}:{context.DiaryId}:{prop.Name}:{context.SubmissionId}");
            Guid conceptGuid = DeterministicGuid($"epro.concept:{context.DiaryId}:{prop.Name}");
            string conceptRef = $"{conceptGuid:D}@{version.ToString(CultureInfo.InvariantCulture)}";
            string conceptCode = $"{context.DiaryId}.{prop.Name}@{version.ToString(CultureInfo.InvariantCulture)}";
            string valueDigest = ComputeShortHex($"{conceptCode}:{observationValue.ToDisplayString()}:{context.ObservedAt:O}");
            string idempotencyKey = $"epro-{context.SubmissionId}-{prop.Name}-v{version.ToString(CultureInfo.InvariantCulture)}-{valueDigest}";

            list.Add(new CanonicalDiaryObservationEnvelope(
                observationGuid,
                enrollmentRef,
                conceptRef,
                conceptCode,
                observationValue,
                context.ObservedAt,
                idempotencyKey));
        }

        return list;
    }

    private static ObservationValue? TranslateAnswer(string itemKey, JsonElement rawValue, InstrumentItemSpec? spec)
    {
        switch (rawValue.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.True:
                return new BooleanValue(true);

            case JsonValueKind.False:
                return new BooleanValue(false);

            case JsonValueKind.Number:
                if (rawValue.TryGetDecimal(out decimal numeric))
                {
                    return new QuantityValue(numeric, ResolveUcumUnit(itemKey, spec));
                }

                return null;

            case JsonValueKind.String:
                string text = (rawValue.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (IsNumericItem(itemKey, spec)
                    && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedDecimal))
                {
                    return new QuantityValue(parsedDecimal, ResolveUcumUnit(itemKey, spec));
                }

                if (IsYesNoBooleanItem(spec) && TryParseYesNo(text, out bool yesNo))
                {
                    return new BooleanValue(yesNo);
                }

                var maybeDate = new DateTimeValue(text);
                if (maybeDate.Problem() is null && text.Length >= 10 && char.IsDigit(text[0]))
                {
                    return maybeDate;
                }

                return new TextValue(text.Length > ObservationValue.MaxTextLength ? text[..ObservationValue.MaxTextLength] : text);

            default:
                string serialized = rawValue.GetRawText();
                if (string.IsNullOrWhiteSpace(serialized))
                {
                    return null;
                }

                string bounded = serialized.Length > ObservationValue.MaxTextLength
                    ? serialized[..ObservationValue.MaxTextLength]
                    : serialized;
                return new TextValue(bounded);
        }
    }

    private static bool IsNumericItem(string itemKey, InstrumentItemSpec? spec) =>
        spec?.Type is "numeric" or "number" or "visual_analog" or "vas" or "slider" or "range"
        || itemKey is "vssbp" or "vsdpb" or "vshr" or "pain_scale";

    private static bool IsYesNoBooleanItem(InstrumentItemSpec? spec) =>
        spec?.Options is { Count: 2 } opts
        && opts.Contains("Yes", StringComparer.OrdinalIgnoreCase)
        && opts.Contains("No", StringComparer.OrdinalIgnoreCase);

    private static bool TryParseYesNo(string text, out bool value)
    {
        if (string.Equals(text, "Yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "No", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static string ResolveUcumUnit(string itemKey, InstrumentItemSpec? spec)
    {
        if (!string.IsNullOrWhiteSpace(spec?.Unit))
        {
            return spec.Unit!;
        }

        return itemKey.ToLowerInvariant() switch
        {
            "vssbp" or "vsdpb" => "mm[Hg]",
            "vshr" => "/min",
            _ => "1",
        };
    }

    private static string ResolveEnrollmentRef(string? explicitRef, string studyCode, string subjectCode)
    {
        if (!string.IsNullOrWhiteSpace(explicitRef)
            && NodeRef.TryParse(explicitRef, out var parsed)
            && parsed.Kind == NodeKinds.Enrollment)
        {
            return parsed.ToString();
        }

        Guid enrollmentGuid = DeterministicGuid($"epro.enrollment:{studyCode}:{subjectCode}");
        return NodeRef.For(EnrollmentId.FromGuid(enrollmentGuid)).ToString();
    }

    private static Dictionary<string, InstrumentItemSpec> ParseInstrumentItems(string? itemsJson)
    {
        var result = new Dictionary<string, InstrumentItemSpec>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(itemsJson))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(itemsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? type = prop.Value.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    ? typeEl.GetString()
                    : null;
                string? unit = prop.Value.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String
                    ? unitEl.GetString()
                    : null;
                List<string>? options = null;
                if (prop.Value.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
                {
                    options = optsEl.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToList();
                }

                result[prop.Name] = new InstrumentItemSpec(type, unit, options);
            }
        }
        catch (JsonException)
        {
            // Ignore malformed metadata and fall back to value-kind inference.
        }

        return result;
    }

    private static Guid DeterministicGuid(string material)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        // Stamp RFC 9562 version 5 / variant 1 bits so Guid is never Guid.Empty.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string ComputeShortHex(string input)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..12];
    }

    private sealed record InstrumentItemSpec(string? Type, string? Unit, IReadOnlyList<string>? Options);

    private sealed record KernelObservationWriteWirePayload(
        Guid ObservationId,
        string EnrollmentRef,
        string Concept,
        ObservationValue Value,
        DateTimeOffset ObservedAt);
}
