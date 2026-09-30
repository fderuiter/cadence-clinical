using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;

namespace Cadence.Kernel.Application.Knowledge.Terminology;

/// <summary>A terminology release to load into one tenant, and the checksum its publisher gave for it.</summary>
/// <param name="TenantId">The tenant that adopts this release (terminology is per tenant, ADR-2470).</param>
/// <param name="CodeSystemId">The registered code system the release belongs to.</param>
/// <param name="CodeSystemVersionId">Id for the new version.</param>
/// <param name="Version">The publisher's version label, for example <c>2026-06-27</c> or <c>2.78</c>.</param>
/// <param name="ReleasedOn">The publisher's release date.</param>
/// <param name="FileName">The file as published.</param>
/// <param name="SourceUri">Where the file was obtained.</param>
/// <param name="ExpectedSha256">The SHA-256 the publisher, or the person loading it, recorded for the file.</param>
/// <param name="ReasonForChange">Why the tenant adopts this release.</param>
public sealed record TerminologyRelease(
    TenantId TenantId,
    CodeSystemId CodeSystemId,
    CodeSystemVersionId CodeSystemVersionId,
    string Version,
    DateOnly ReleasedOn,
    string FileName,
    string SourceUri,
    string ExpectedSha256,
    string ReasonForChange);

/// <summary>
/// Loads a terminology package into a tenant's Concept Registry: checks the file against its expected SHA-256, parses
/// it, and imports it as one <see cref="ImportCodeSystemVersion"/> command. Loading the same file again replays the
/// original import (the idempotency key is derived from the code system, version and digest).
/// </summary>
public sealed class TerminologyPackageLoader(IKernelCommandSender sender)
{
    public async Task<CommandResult<CodeSystemVersionId>> LoadAsync(
        TerminologyRelease release, byte[] content, ITerminologyParser parser, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(parser);

        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(digest, release.ExpectedSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeErrors.PackageChecksumMismatch.WithMessage(
                $"{release.FileName} has SHA-256 {digest}, not the expected {release.ExpectedSha256}.");
        }

        IReadOnlyList<CodeSystemEntryData> entries;
        using (var stream = new MemoryStream(content, writable: false))
        {
            try
            {
                entries = parser.Parse(stream);
            }
            catch (FormatException ex)
            {
                return KnowledgeErrors.EntriesInvalid.WithMessage($"{release.FileName} is not valid {parser.Format}: {ex.Message}");
            }
        }

        var command = new ImportCodeSystemVersion(
            release.TenantId,
            release.CodeSystemVersionId,
            release.CodeSystemId,
            release.Version,
            release.ReleasedOn,
            new TerminologyPackage(digest, release.FileName, release.SourceUri, content.LongLength, parser.Format),
            entries,
            $"terminology:{release.CodeSystemId}:{release.Version}:{digest}",
            release.ReasonForChange);
        return await sender.SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
