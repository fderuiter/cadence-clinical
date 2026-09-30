using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cadence.Standards.Blocks;

/// <summary>
/// The reusable collection specifications (form blocks) that ship with the platform (ADR-2503). Each is library item
/// content of kind <c>CollectionSpecification</c>: draft it in a tenant with <c>DraftLibraryItem</c> under its
/// <see cref="FormBlock.Key"/>, approve it with an attestation, and instantiate it into a protocol design with
/// <see cref="CollectionSpecificationInstantiator"/>. Every file is listed in <c>Blocks/manifest.json</c> with its
/// SHA-256 and is verified before it is read.
/// </summary>
public static class FormBlocks
{
    private const string ResourcePrefix = "Cadence.Standards.Blocks.";

    private static readonly Lazy<IReadOnlyList<BlockManifestEntry>> Manifest = new(ReadManifest);

    /// <summary>The blocks the manifest lists, by file name.</summary>
    public static IReadOnlyList<string> Names => [.. Manifest.Value.Select(e => e.File)];

    /// <summary>Loads one block by its manifest file name, verifying its checksum and content first.</summary>
    /// <exception cref="ArgumentException">No block has that name.</exception>
    /// <exception cref="InvalidDataException">The block's bytes do not match the manifest, or its content is not valid.</exception>
    public static FormBlock Load(string file)
    {
        var entry = Manifest.Value.SingleOrDefault(e => string.Equals(e.File, file, StringComparison.Ordinal))
            ?? throw new ArgumentException($"No form block named '{file}'. Known: {string.Join(", ", Names)}.", nameof(file));
        var bytes = ReadResource(entry.File);
        ChecksumVerifier.Verify(bytes, entry.Sha256, entry.File);

        using var document = JsonDocument.Parse(bytes);
        var content = document.RootElement.Clone();
        var (definition, problems) = CollectionSpecificationJson.Read(content);
        if (definition is null)
        {
            throw new InvalidDataException($"Form block {entry.File} is not a valid collection specification: {string.Join(" ", problems)}");
        }

        return new FormBlock(entry.File, entry.Key, entry.Title, content, definition);
    }

    /// <summary>Loads every block.</summary>
    public static IReadOnlyList<FormBlock> LoadAll() => [.. Names.Select(Load)];

    private static List<BlockManifestEntry> ReadManifest()
    {
        using var document = JsonDocument.Parse(ReadResource("manifest.json"));
        return [.. document.RootElement.GetProperty("files").EnumerateArray().Select(f => new BlockManifestEntry(
            f.GetProperty("file").GetString()!,
            f.GetProperty("key").GetString()!,
            f.GetProperty("title").GetString()!,
            f.GetProperty("sha256").GetString()!))];
    }

    private static byte[] ReadResource(string file)
    {
        using var stream = typeof(FormBlocks).Assembly.GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidDataException($"Form block resource {file} is missing from {typeof(FormBlocks).Assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed record BlockManifestEntry(string File, string Key, string Title, string Sha256);
}

/// <summary>A shipped form block.</summary>
/// <param name="File">Its file name in the manifest.</param>
/// <param name="Key">The library item key to draft it under.</param>
/// <param name="Title">The library item title.</param>
/// <param name="Content">The library item content, as drafted.</param>
/// <param name="Definition">The content, read.</param>
public sealed record FormBlock(string File, string Key, string Title, JsonElement Content, CollectionSpecificationDefinition Definition);
