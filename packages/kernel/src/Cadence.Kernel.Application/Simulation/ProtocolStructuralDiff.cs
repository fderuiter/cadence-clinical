using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Simulation;

namespace Cadence.Kernel.Application.Simulation;

/// <summary>
/// The structural diff between two protocol versions by stable element id (K-PROT-1, ADR-2480 §4.1). It is a pure
/// function of the two documents.
/// <list type="bullet">
/// <item>An element is a JSON object with a non-empty string <c>id</c>, the CDISC USDM convention. Ids are unique
/// within a version. The document root is an element too, with id <see cref="RootId"/> when it has none.</item>
/// <item>Each element is compared on its own values. A nested element is replaced in its parent by a reference to its
/// id, so a change inside a child is reported once, on the child, and the parent changes only when its children are
/// added, removed or reordered.</item>
/// <item>Arrays are compared whole: a changed array is reported at its own path.</item>
/// </list>
/// </summary>
public static class ProtocolStructuralDiff
{
    /// <summary>The name and version recorded in a result's engine versions; bump the version when the output changes.</summary>
    public static readonly EngineVersion Engine = new("protocol.structural_diff", "1");

    /// <summary>The element id of a document root that carries no <c>id</c>.</summary>
    public const string RootId = "$root";

    private static readonly KernelError NotAnObject = SimulationErrors.ContentInvalid.WithMessage("Protocol content must be a JSON object.");

    /// <summary>The property a nested element is replaced with in its parent.</summary>
    private const string ElementReference = "$element";

    /// <summary>
    /// The elements of <paramref name="baseContent"/> (null for none) that <paramref name="candidateContent"/> adds,
    /// removes or changes, sorted by element id. Fails when either document is not a JSON object or repeats an id.
    /// </summary>
    public static CommandResult<IReadOnlyList<ElementChange>> Compare(string? baseContent, string candidateContent)
    {
        ArgumentNullException.ThrowIfNull(candidateContent);
        var baseElements = new Dictionary<string, Element>(StringComparer.Ordinal);
        if (baseContent is not null && Read(baseContent, baseElements) is { } baseError)
        {
            return baseError;
        }

        var candidateElements = new Dictionary<string, Element>(StringComparer.Ordinal);
        if (Read(candidateContent, candidateElements) is { } candidateError)
        {
            return candidateError;
        }

        var changes = new List<ElementChange>();
        foreach (var id in baseElements.Keys.Union(candidateElements.Keys).Order(StringComparer.Ordinal))
        {
            var inBase = baseElements.TryGetValue(id, out var before);
            var inCandidate = candidateElements.TryGetValue(id, out var after);
            if (!inBase)
            {
                changes.Add(new ElementChange(id, after!.InstanceType, ElementChangeKind.Added, []));
            }
            else if (!inCandidate)
            {
                changes.Add(new ElementChange(id, before!.InstanceType, ElementChangeKind.Removed, []));
            }
            else
            {
                var paths = new List<string>();
                Differences(before!.Values, after!.Values, string.Empty, paths);
                if (paths.Count > 0)
                {
                    paths.Sort(StringComparer.Ordinal);
                    changes.Add(new ElementChange(id, after.InstanceType, ElementChangeKind.Changed, paths));
                }
            }
        }

        return CommandResult<IReadOnlyList<ElementChange>>.Success(changes);
    }

    private static KernelError? Read(string content, Dictionary<string, Element> elements)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            return NotAnObject;
        }

        if (root is not JsonObject document)
        {
            return NotAnObject;
        }

        var duplicate = (string?)null;
        Register(document, IdOf(document) ?? RootId, elements, ref duplicate);
        return duplicate is null
            ? null
            : SimulationErrors.ElementIdDuplicated.WithMessage($"The protocol element id '{duplicate}' appears more than once; element ids must be unique within a version (K-PROT-1).");
    }

    private static void Register(JsonObject source, string id, Dictionary<string, Element> elements, ref string? duplicate)
    {
        // Reserve the id before visiting children, so a child that repeats its parent's id is caught too.
        if (!elements.TryAdd(id, null!))
        {
            duplicate ??= id;
            return;
        }

        var values = new JsonObject();
        foreach (var (name, value) in source)
        {
            values[name] = Detach(value, elements, ref duplicate);
        }

        var instanceType = source["instanceType"] is JsonValue type && type.TryGetValue<string>(out var text) ? text : null;
        elements[id] = new Element(instanceType, values);
    }

    private static JsonNode? Detach(JsonNode? node, Dictionary<string, Element> elements, ref string? duplicate)
    {
        switch (node)
        {
            case JsonObject child when IdOf(child) is { } childId:
                Register(child, childId, elements, ref duplicate);
                return new JsonObject { [ElementReference] = childId };
            case JsonObject nested:
                var copy = new JsonObject();
                foreach (var (name, value) in nested)
                {
                    copy[name] = Detach(value, elements, ref duplicate);
                }

                return copy;
            case JsonArray array:
                var items = new JsonArray();
                foreach (var item in array)
                {
                    items.Add(Detach(item, elements, ref duplicate));
                }

                return items;
            default:
                return node?.DeepClone();
        }
    }

    private static string? IdOf(JsonObject node) =>
        node["id"] is JsonValue value && value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id) ? id : null;

    /// <summary>Adds the JSON Pointer of every value that differs between two detached objects.</summary>
    private static void Differences(JsonObject before, JsonObject after, string path, List<string> paths)
    {
        foreach (var name in before.Select(p => p.Key).Union(after.Select(p => p.Key), StringComparer.Ordinal))
        {
            var pointer = path + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
            var inBefore = before.TryGetPropertyValue(name, out var left);
            var inAfter = after.TryGetPropertyValue(name, out var right);
            if (inBefore && inAfter && left is JsonObject leftObject && right is JsonObject rightObject
                && !leftObject.ContainsKey(ElementReference) && !rightObject.ContainsKey(ElementReference))
            {
                Differences(leftObject, rightObject, pointer, paths);
            }
            else if (inBefore != inAfter || !JsonNode.DeepEquals(left, right))
            {
                paths.Add(pointer);
            }
        }
    }

    private sealed record Element(string? InstanceType, JsonObject Values);
}
