using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using Cadence.Rules.Packs;

namespace Cadence.Rules.Design;

/// <summary>
/// The design-time conformance rule pack that ships with the platform (ADR-2464): design-stage rules over the facts
/// <see cref="Kernel.Standards.Conformance.DesignConformance"/> derives from a protocol design. Draft it in a tenant with
/// <c>DraftLibraryItem</c> (kind <c>RulePack</c>, key <see cref="Key"/>), release it with an approval attestation, and pin
/// it in the study's standards profile. A sponsor may release its own pack over the same facts, with different
/// severities, instead. The embedded file is verified against <see cref="Sha256"/> before it is read.
/// </summary>
public static class DesignConformancePack
{
    /// <summary>The library item key the pack is drafted under.</summary>
    public const string Key = "cadence-design-conformance";

    /// <summary>The SHA-256 of the embedded pack, in lower-case hex.</summary>
    public const string Sha256 = "ffbc188fe0069dac482dd6d79b84fcb0e4b4edbdadaebbfdf863da634433ec4a";

    private const string Resource = "Cadence.Rules.Design.design-conformance.json";

    private static readonly Lazy<JsonElement> Loaded = new(Load);

    /// <summary>The pack's content, to draft as a library item.</summary>
    public static JsonElement Content => Loaded.Value;

    /// <summary>The pack, read.</summary>
    public static RulePack Pack => RulePackJson.Parse(Loaded.Value);

    private static JsonElement Load()
    {
        using var stream = typeof(DesignConformancePack).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException($"The embedded resource {Resource} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The design conformance pack's SHA-256 is {actual}, not {Sha256}.");
        }

        using var document = JsonDocument.Parse(bytes);
        var content = document.RootElement.Clone();
        var (pack, problems) = RulePackJson.Read(content);
        return pack is null
            ? throw new InvalidDataException($"The design conformance pack is not a valid rule pack: {string.Join(" ", problems)}")
            : content;
    }
}
