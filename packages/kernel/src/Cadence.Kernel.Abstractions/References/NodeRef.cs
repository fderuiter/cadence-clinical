using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Kernel.References;

/// <summary>
/// The public reference to any kernel object (ADR-2481, spec §3.4): <c>kind:id</c> or <c>kind:id@version</c>, for
/// example <c>study_site:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@3</c>. The same string is used in URLs, the command
/// palette, trace links and event envelopes.
/// <list type="bullet">
/// <item><c>kind</c> is a registered <see cref="NodeKinds"/> value.</item>
/// <item><c>id</c> is the object's UUID in lower-case hyphenated form (never empty).</item>
/// <item><c>version</c> is the object's positive version index. Without it the reference means "the object as of
/// the read's <c>(valid_at, known_at)</c>".</item>
/// </list>
/// Each reference has exactly one string form: parsing rejects upper case, braces, leading zeros and unregistered
/// kinds, so two different strings never name the same object version.
/// </summary>
[JsonConverter(typeof(NodeRefJsonConverter))]
public sealed record NodeRef : IParsable<NodeRef>
{
    private const int GuidLength = 36;

    public NodeRef(string kind, Guid id, long? version = null)
    {
        if (!NodeKinds.IsRegistered(kind))
        {
            throw new ArgumentException($"'{kind}' is not a registered node kind (ADR-2481).", nameof(kind));
        }

        if (id == Guid.Empty)
        {
            throw new ArgumentException("A node reference needs a non-empty id.", nameof(id));
        }

        if (version is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "A version index starts at 1.");
        }

        Kind = kind;
        Id = id;
        Version = version;
    }

    /// <summary>The object's kind (<see cref="NodeKinds"/>).</summary>
    public string Kind { get; }

    /// <summary>The object's UUID.</summary>
    public Guid Id { get; }

    /// <summary>The object's version index, or null for "as of the read".</summary>
    public long? Version { get; }

    /// <summary>A reference to the object identified by <paramref name="id"/>.</summary>
    public static NodeRef For<TId>(TId id, long? version = null)
        where TId : struct, IEntityId<TId> => new(NodeKinds.Of<TId>(), id.Value, version);

    /// <summary>A reference to the object identified by an untyped kernel identifier.</summary>
    public static NodeRef For(IEntityId id, long? version = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new NodeRef(NodeKinds.Of(id.GetType()), id.Value, version);
    }

    /// <summary>This reference without its version.</summary>
    public NodeRef Unversioned() => Version is null ? this : new NodeRef(Kind, Id);

    /// <summary>This reference pinned to <paramref name="version"/>.</summary>
    public NodeRef AtVersion(long version) => new(Kind, Id, version);

    /// <summary>True when this reference names an object of kind <typeparamref name="TId"/>; returns its typed id.</summary>
    public bool TryGetId<TId>(out TId id)
        where TId : struct, IEntityId<TId>
    {
        if (string.Equals(Kind, NodeKinds.Of<TId>(), StringComparison.Ordinal))
        {
            id = TId.FromGuid(Id);
            return true;
        }

        id = TId.Empty;
        return false;
    }

    /// <summary>The canonical string form.</summary>
    public override string ToString() =>
        Version is { } version
            ? string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Id:D}@{version}")
            : string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Id:D}");

    public static NodeRef Parse(string s) => Parse(s, null);

    public static NodeRef Parse(string s, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, provider, out var result)
            ? result
            : throw new FormatException($"'{s}' is not a canonical node reference (kind:id[@version], ADR-2481).");
    }

    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out NodeRef result) =>
        TryParse(s, null, out result);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out NodeRef result)
    {
        result = null;
        if (s is null)
        {
            return false;
        }

        var text = s.AsSpan();
        var colon = text.IndexOf(':');
        if (colon <= 0 || NodeKinds.Registered(text[..colon]) is not { } kind)
        {
            return false;
        }

        var rest = text[(colon + 1)..];
        var at = rest.IndexOf('@');
        var idText = at < 0 ? rest : rest[..at];
        if (idText.Length != GuidLength || idText.ContainsAnyInRange('A', 'Z') || !Guid.TryParseExact(idText, "D", out var id) || id == Guid.Empty)
        {
            return false;
        }

        long? version = null;
        if (at >= 0)
        {
            var versionText = rest[(at + 1)..];
            if (versionText.IsEmpty || versionText[0] == '0' || versionText.ContainsAnyExceptInRange('0', '9')
                || !long.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            version = parsed;
        }

        result = new NodeRef(kind, id, version);
        return true;
    }
}

/// <summary>Serialises a <see cref="NodeRef"/> as its canonical string.</summary>
public sealed class NodeRefJsonConverter : JsonConverter<NodeRef>
{
    public override NodeRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a node reference string, found {reader.TokenType}.");
        }

        var text = reader.GetString();
        return NodeRef.TryParse(text, out var node)
            ? node
            : throw new JsonException($"'{text}' is not a canonical node reference (ADR-2481).");
    }

    public override void Write(Utf8JsonWriter writer, NodeRef value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.ToString());
    }
}
