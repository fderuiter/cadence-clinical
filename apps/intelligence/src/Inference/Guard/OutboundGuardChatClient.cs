using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Deid;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Inference.Routing;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Inference.Guard;

/// <summary>Which detections the outbound guard scrubs (ADR-2636 §4.3).</summary>
public enum ScrubProfile
{
    /// <summary>
    /// The default: direct identifiers only. Dates, ages and postcodes stay, because pseudonymous clinical data needs
    /// them and birth dates live only in the IdentityVault.
    /// </summary>
    DirectIdentifiers,

    /// <summary>Every category the detector knows (HIPAA Safe Harbor's list).</summary>
    Hipaa,

    /// <summary>The EU Clinical Trials Regulation profile.</summary>
    EuCtr,
}

/// <summary>
/// The mark the tool layer (#5547) puts in a message's or content's <see cref="AdditionalPropertiesDictionary"/> when
/// the content derives from a node the kernel masked as identity-layer data. The guard refuses any marked request.
/// </summary>
public static class IdentityLayerMark
{
    /// <summary>The property key; its value is the identity layer's name.</summary>
    public const string Key = "cadence.identity_layer";

    /// <summary>Marks <paramref name="content"/> as identity-layer data and returns it.</summary>
    public static T Mark<T>(T content, string layer)
        where T : AIContent
    {
        ArgumentNullException.ThrowIfNull(content);
        content.AdditionalProperties = PropertyBag.With(content.AdditionalProperties, Key, layer);
        return content;
    }

    /// <summary>True when <paramref name="properties"/> carries the mark.</summary>
    public static bool IsMarked(AdditionalPropertiesDictionary? properties) => properties is not null && properties.ContainsKey(Key);
}

/// <summary>Builds the guard for a routed client.</summary>
internal sealed class OutboundGuardFactory(DeidDetector detector)
{
    public IChatClient Wrap(IChatClient inner, ScrubProfile profile = ScrubProfile.DirectIdentifiers) =>
        new OutboundGuardChatClient(inner, detector, profile);
}

/// <summary>
/// Checks every request before it leaves the process (ADR-2636 §4.3): identity-layer content and binary content it
/// cannot inspect refuse the call; direct identifiers in free text become one-way placeholders such as <c>[EMAIL]</c>.
/// Model output (reasoning, tool calls) passes unchanged: the model only ever saw scrubbed input, and thinking must
/// be replayed byte for byte. A protective control, not de-identification. Each response carries the counts by
/// category under <see cref="ScrubCountsKey"/>, never the values.
/// </summary>
public sealed class OutboundGuardChatClient : DelegatingChatClient
{
    /// <summary>The response property key carrying the scrub counts by category.</summary>
    public const string ScrubCountsKey = AgentRunInference.ScrubCountsKey;

    private static readonly FrozenSet<string> DirectIdentifierCategories = new[]
    {
        DetectorCategory.Email,
        DetectorCategory.TelephoneFax,
        DetectorCategory.SsnNationalId,
        DetectorCategory.MedicalRecordAccount,
        DetectorCategory.HealthPlanBeneficiary,
        DetectorCategory.CertificateLicense,
        DetectorCategory.VehicleIdentifiers,
        DetectorCategory.Urls,
        DetectorCategory.IpMacAddresses,
    }.ToFrozenSet(StringComparer.Ordinal);

    // Structured references the citation verifier needs; their values are kernel ids, never free text.
    private static readonly FrozenSet<string> ReferenceProperties =
        new[] { "ref", "nodeRef", "node_ref", "id", "handle" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly DeidDetector _detector;
    private readonly ComplianceProfile _detectorProfile;
    private readonly IReadOnlySet<string> _categories;

    /// <summary>Wraps <paramref name="innerClient"/>.</summary>
    public OutboundGuardChatClient(IChatClient innerClient, DeidDetector detector, ScrubProfile profile = ScrubProfile.DirectIdentifiers)
        : base(innerClient)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        (_detectorProfile, _categories) = profile switch
        {
            ScrubProfile.DirectIdentifiers => (ComplianceProfile.HIPAA, (IReadOnlySet<string>)DirectIdentifierCategories),
            ScrubProfile.Hipaa => (ComplianceProfile.HIPAA, ProfileCategories.AllCategories),
            ScrubProfile.EuCtr => (ComplianceProfile.EU_CTR, ProfileCategories.EuCtrCategories),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown scrub profile."),
        };
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var (safe, safeOptions) = Prepare(messages, options, counts);
        var response = await base.GetResponseAsync(safe, safeOptions, cancellationToken).ConfigureAwait(false);
        response.AdditionalProperties = PropertyBag.With(response.AdditionalProperties, ScrubCountsKey, counts);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var (safe, safeOptions) = Prepare(messages, options, counts);
        var first = true;
        await foreach (var update in base.GetStreamingResponseAsync(safe, safeOptions, cancellationToken).ConfigureAwait(false))
        {
            if (first)
            {
                update.AdditionalProperties = PropertyBag.With(update.AdditionalProperties, ScrubCountsKey, counts);
                first = false;
            }

            yield return update;
        }
    }

    private (List<ChatMessage> Messages, ChatOptions? Options) Prepare(
        IEnumerable<ChatMessage> messages, ChatOptions? options, Dictionary<string, int> counts)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var safe = new List<ChatMessage>();
        foreach (var message in messages)
        {
            if (IdentityLayerMark.IsMarked(message.AdditionalProperties))
            {
                throw IdentityRefusal();
            }

            var copy = message.Clone();
            copy.Contents = [.. message.Contents.Select(c => Guard(c, counts))];
            safe.Add(copy);
        }

        if (options?.Instructions is { } instructions)
        {
            options = options.Clone();
            options.Instructions = Scrub(instructions, counts);
        }

        return (safe, options);
    }

    private AIContent Guard(AIContent content, Dictionary<string, int> counts)
    {
        if (IdentityLayerMark.IsMarked(content.AdditionalProperties))
        {
            throw IdentityRefusal();
        }

        return content switch
        {
            TextContent text => new TextContent(Scrub(text.Text, counts))
            {
                AdditionalProperties = text.AdditionalProperties,
                Annotations = text.Annotations,
            },
            FunctionResultContent result => new FunctionResultContent(result.CallId, ScrubValue(result.Result, counts))
            {
                AdditionalProperties = result.AdditionalProperties,
                Exception = result.Exception,
            },
            DataContent or UriContent or HostedFileContent => throw new InferenceRefusedException(
                InferenceRefusalReason.UninspectableContent,
                "A message carries binary or linked content the outbound guard cannot inspect; nothing was sent."),
            _ => content,
        };
    }

    private object? ScrubValue(object? value, Dictionary<string, int> counts) => value switch
    {
        null => null,
        string s => Scrub(s, counts),
        JsonElement element => ScrubJson(JsonSerializer.SerializeToNode(element), counts),
        JsonNode node => ScrubJson(node.DeepClone(), counts),
        _ => ScrubJson(JsonSerializer.SerializeToNode(value, value.GetType(), AIJsonUtilities.DefaultOptions), counts),
    };

    private JsonNode? ScrubJson(JsonNode? node, Dictionary<string, int> counts)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (!ReferenceProperties.Contains(key))
                    {
                        obj[key] = ScrubJson(child, counts);
                    }
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = ScrubJson(array[i]?.DeepClone(), counts);
                }

                return array;
            case JsonValue leaf when leaf.TryGetValue<string>(out var text):
                return JsonValue.Create(Scrub(text, counts));
            default:
                return node;
        }
    }

    private string Scrub(string text, Dictionary<string, int> counts)
    {
        var found = _detector.Detect(text, _detectorProfile).Where(d => _categories.Contains(d.Category)).ToList();
        if (found.Count == 0)
        {
            return text;
        }

        foreach (var category in DeidDetector.ResolveOverlaps(found, text).Select(d => d.Category))
        {
            counts[category] = counts.GetValueOrDefault(category) + 1;
        }

        return DeidDetector.RedactText(text, found);
    }

    private static InferenceRefusedException IdentityRefusal() => new(
        InferenceRefusalReason.IdentityLayerContent,
        "A message carries identity-layer (IdentityVault) content; it never reaches a model, and nothing was sent.");
}
