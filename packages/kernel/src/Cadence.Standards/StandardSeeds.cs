using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using Cadence.Kernel.Standards;
using Cadence.Standards.CdiscLibrary;

namespace Cadence.Standards;

/// <summary>
/// The Standards Registry seed content that ships with the platform: public CDISC standards (SDTMIG v3.4 DM, AE, VS
/// and LB; ADaMIG v1.3 ADSL) in the CDISC Library product shape. Every file is listed in <c>Seed/manifest.json</c>
/// with its SHA-256 and is verified before it is read, so a corrupted or edited seed never reaches the registry.
/// Register a loaded seed's package with <see cref="RegisterStandardVersion"/>.
/// </summary>
public static class StandardSeeds
{
    private const string ResourcePrefix = "Cadence.Standards.Seed.";

    private static readonly Lazy<IReadOnlyList<SeedManifestEntry>> Manifest = new(ReadManifest);

    /// <summary>The seeds the manifest lists.</summary>
    public static IReadOnlyList<string> Names => [.. Manifest.Value.Select(e => e.File)];

    /// <summary>Loads one seed by its manifest file name, verifying its checksum first.</summary>
    /// <exception cref="InvalidDataException">The seed's bytes do not match the manifest.</exception>
    public static StandardSeed Load(string file)
    {
        var entry = Manifest.Value.SingleOrDefault(e => string.Equals(e.File, file, StringComparison.Ordinal))
            ?? throw new ArgumentException($"No seed named '{file}'. Known: {string.Join(", ", Names)}.", nameof(file));
        var bytes = ReadResource(entry.File);
        ChecksumVerifier.Verify(bytes, entry.Sha256, entry.File);

        var package = entry.Format switch
        {
            "cdisc-library-product" => CdiscLibraryProductReader.Read(bytes, entry.StandardCode),
            _ => throw new InvalidDataException($"Seed {entry.File} has unknown format '{entry.Format}'."),
        };
        return new StandardSeed(entry.File, package);
    }

    /// <summary>Loads every seed.</summary>
    public static IReadOnlyList<StandardSeed> LoadAll() => [.. Names.Select(Load)];

    private static List<SeedManifestEntry> ReadManifest()
    {
        using var document = JsonDocument.Parse(ReadResource("manifest.json"));
        return [.. document.RootElement.GetProperty("files").EnumerateArray().Select(f => new SeedManifestEntry(
            f.GetProperty("file").GetString()!,
            f.GetProperty("standardCode").GetString()!,
            f.GetProperty("format").GetString()!,
            f.GetProperty("sha256").GetString()!))];
    }

    private static byte[] ReadResource(string file)
    {
        using var stream = typeof(StandardSeeds).Assembly.GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidDataException($"Seed resource {file} is missing from {typeof(StandardSeeds).Assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record SeedManifestEntry(string File, string StandardCode, string Format, string Sha256);
}

/// <summary>A loaded seed: its file name and the package read from it.</summary>
public sealed record StandardSeed(string File, StandardPackage Package);

/// <summary>Checks downloaded or shipped standards content against a published SHA-256 before it is parsed.</summary>
public static class ChecksumVerifier
{
    /// <summary>Throws unless <paramref name="content"/> hashes to <paramref name="expectedSha256"/> (lowercase hex).</summary>
    /// <exception cref="InvalidDataException">The checksum differs.</exception>
    public static void Verify(ReadOnlySpan<byte> content, string expectedSha256, string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        var actual = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{what} has SHA-256 {actual}; expected {expectedSha256}. It was changed or corrupted.");
        }
    }
}
