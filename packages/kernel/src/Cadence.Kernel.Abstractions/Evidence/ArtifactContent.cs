using System;
using System.Text.RegularExpressions;

using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Evidence;

/// <summary>
/// The description of one artifact version's content (spec §8.3). The bytes live in object storage at
/// <paramref name="StorageRef"/>; the kernel keeps their hash, size and media type. The storage adapter computes
/// <paramref name="ContentHash"/> from the bytes it stored (<see cref="Trust.ContentHash.OfBytes"/>), so a version can
/// be verified against its storage at any time.
/// </summary>
/// <param name="ContentHash"><c>sha256:</c> and the lower-case hex SHA-256 of the exact bytes.</param>
/// <param name="SizeBytes">The number of bytes, at least one.</param>
/// <param name="MediaType">The media type, for example <c>application/pdf</c>.</param>
/// <param name="StorageRef">Where the bytes are stored: an opaque reference the storage adapter understands.</param>
public sealed partial record ArtifactContent(string ContentHash, long SizeBytes, string MediaType, string StorageRef)
{
    /// <summary>The longest media type the kernel stores.</summary>
    public const int MaxMediaTypeLength = 255;

    /// <summary>The longest storage reference the kernel stores.</summary>
    public const int MaxStorageRefLength = 1024;

    /// <summary>
    /// Why this content description is not acceptable, or null when it is: a malformed hash, an empty file, a media type
    /// that is not <c>type/subtype</c>, or a missing storage reference.
    /// </summary>
    public string? Problem()
    {
        if (!Trust.ContentHash.IsWellFormed(ContentHash))
        {
            return "The content hash must be 'sha256:' and 64 lower-case hex characters.";
        }

        if (SizeBytes <= 0)
        {
            return "An artifact version has at least one byte.";
        }

        if (MediaType is null || MediaType.Length > MaxMediaTypeLength || !MediaTypeSyntax().IsMatch(MediaType))
        {
            return "The media type must be 'type/subtype' in lower case, for example 'application/pdf'.";
        }

        if (string.IsNullOrWhiteSpace(StorageRef) || StorageRef.Length > MaxStorageRefLength)
        {
            return $"The storage reference is required and at most {MaxStorageRefLength} characters.";
        }

        return null;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]{0,126}/[a-z0-9][a-z0-9!#$&^_.+-]{0,126}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MediaTypeSyntax();
}
