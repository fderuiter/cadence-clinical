using System;
using System.Collections.Generic;
using System.Text;

using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Simulation;

/// <summary>
/// Canonical metadata binding a saved <see cref="Evidence.ArtifactKind.SimulationReport"/> artifact version to the
/// candidate and base specifications it evaluated (K-SIM-5, K-PROT-2).
/// </summary>
/// <param name="CandidateKind">The kind of candidate specification.</param>
/// <param name="CandidateVersion">The stored candidate reference, when saved.</param>
/// <param name="CandidateContentHash">The exact content hash of the candidate specification.</param>
/// <param name="BaseVersion">The base specification reference, when present.</param>
/// <param name="BaseContentHash">The exact content hash of the base specification, when present.</param>
/// <param name="InputHash">The simulation's <see cref="SimulationResult.InputHash"/>.</param>
/// <param name="ResultHash">The simulation's <see cref="SimulationResult.ResultHash"/>.</param>
public sealed record SimulationArtifactBinding(
    CandidateKind CandidateKind,
    NodeRef? CandidateVersion,
    string CandidateContentHash,
    NodeRef? BaseVersion,
    string? BaseContentHash,
    string InputHash,
    string ResultHash)
{
    /// <summary>Canonical media type of a saved simulation report artifact.</summary>
    public const string MediaType = "application/vnd.cadence.simulation-result+json";

    /// <summary>URI prefix used in <c>ArtifactContent.StorageRef</c> to record the verified simulation binding.</summary>
    public const string StoragePrefix = "urn:cadence:simulation-result:v1?";

    /// <summary>Extracts the artifact binding from an unmasked <paramref name="result"/>, or null when masked.</summary>
    public static SimulationArtifactBinding? FromResult(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Candidate.ContentHash is not { } candidateHash || !ContentHash.IsWellFormed(candidateHash))
        {
            return null;
        }

        if (!ContentHash.IsWellFormed(result.InputHash) || !ContentHash.IsWellFormed(result.ResultHash))
        {
            return null;
        }

        return new SimulationArtifactBinding(
            result.Candidate.Kind,
            result.Candidate.Version is { } v ? new NodeRef(v.Kind, v.Id) : null,
            candidateHash,
            result.Candidate.BaseVersion is { } bv ? new NodeRef(bv.Kind, bv.Id) : null,
            result.Candidate.BaseContentHash,
            result.InputHash,
            result.ResultHash);
    }

    /// <summary>Builds a deterministic <see cref="Evidence.ArtifactContent"/> for <paramref name="result"/>.</summary>
    public static Evidence.ArtifactContent? ToArtifactContent(SimulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (FromResult(result) is not { } binding)
        {
            return null;
        }

        var canonicalJson = SimulationResultHashing.ToCanonicalJson(result);
        var computedHash = ContentHash.Compute(canonicalJson);
        if (!string.Equals(computedHash, result.ResultHash, StringComparison.Ordinal))
        {
            return null;
        }

        var sizeBytes = Encoding.UTF8.GetByteCount(canonicalJson);
        return new Evidence.ArtifactContent(result.ResultHash, sizeBytes, MediaType, binding.ToStorageRef());
    }

    /// <summary>Formats this binding as a deterministic <c>StorageRef</c> URI.</summary>
    public string ToStorageRef()
    {
        var parts = new List<string>(7)
        {
            $"kind={Uri.EscapeDataString(CandidateKind.ToString())}",
            $"candidateHash={Uri.EscapeDataString(CandidateContentHash)}",
            $"inputHash={Uri.EscapeDataString(InputHash)}",
            $"resultHash={Uri.EscapeDataString(ResultHash)}",
        };
        if (CandidateVersion is not null)
        {
            parts.Add($"candidate={Uri.EscapeDataString(new NodeRef(CandidateVersion.Kind, CandidateVersion.Id).ToString())}");
        }

        if (BaseVersion is not null)
        {
            parts.Add($"base={Uri.EscapeDataString(new NodeRef(BaseVersion.Kind, BaseVersion.Id).ToString())}");
        }

        if (!string.IsNullOrEmpty(BaseContentHash))
        {
            parts.Add($"baseHash={Uri.EscapeDataString(BaseContentHash)}");
        }

        return StoragePrefix + string.Join("&", parts);
    }

    /// <summary>Parses a <see cref="SimulationArtifactBinding"/> from <paramref name="storageRef"/>.</summary>
    public static bool TryParseStorageRef(string? storageRef, out SimulationArtifactBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(storageRef) || !storageRef.StartsWith(StoragePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in storageRef[StoragePrefix.Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == segment.Length - 1)
            {
                return false;
            }

            values[segment[..eq]] = Uri.UnescapeDataString(segment[(eq + 1)..]);
        }

        if (!values.TryGetValue("kind", out var kindText)
            || !Enum.TryParse<CandidateKind>(kindText, ignoreCase: false, out var kind)
            || !values.TryGetValue("candidateHash", out var candidateHash)
            || !ContentHash.IsWellFormed(candidateHash)
            || !values.TryGetValue("inputHash", out var inputHash)
            || !ContentHash.IsWellFormed(inputHash)
            || !values.TryGetValue("resultHash", out var resultHash)
            || !ContentHash.IsWellFormed(resultHash))
        {
            return false;
        }

        NodeRef? candidateRef = null;
        if (values.TryGetValue("candidate", out var candidateText) && !NodeRef.TryParse(candidateText, out candidateRef))
        {
            return false;
        }

        NodeRef? baseRef = null;
        if (values.TryGetValue("base", out var baseText) && !NodeRef.TryParse(baseText, out baseRef))
        {
            return false;
        }

        string? baseHash = null;
        if (values.TryGetValue("baseHash", out var parsedBaseHash))
        {
            if (!ContentHash.IsWellFormed(parsedBaseHash))
            {
                return false;
            }

            baseHash = parsedBaseHash;
        }

        binding = new SimulationArtifactBinding(kind, candidateRef, candidateHash, baseRef, baseHash, inputHash, resultHash);
        return true;
    }

    /// <summary>
    /// True when this simulation binding proves an evaluation of <paramref name="amendedId"/> (at
    /// <paramref name="amendedContentHash"/>) forked from <paramref name="parentId"/> (at <paramref name="parentContentHash"/>).
    /// </summary>
    public bool MatchesProtocolAmendment(
        ProtocolVersionId parentId,
        string parentContentHash,
        ProtocolVersionId amendedId,
        string amendedContentHash) =>
        CandidateKind == CandidateKind.ProtocolVersion
        && CandidateVersion is not null
        && string.Equals(CandidateVersion.Kind, NodeKinds.ProtocolVersion, StringComparison.Ordinal)
        && CandidateVersion.Id == amendedId.Value
        && string.Equals(CandidateContentHash, amendedContentHash, StringComparison.Ordinal)
        && BaseVersion is not null
        && string.Equals(BaseVersion.Kind, NodeKinds.ProtocolVersion, StringComparison.Ordinal)
        && BaseVersion.Id == parentId.Value
        && string.Equals(BaseContentHash, parentContentHash, StringComparison.Ordinal);
}

/// <summary>Errors raised by simulation (ADR-2480).</summary>
public static class SimulationErrors
{
    public static readonly KernelError VersionUnknown =
        new("K.Simulation.VersionUnknown", 404, "The protocol version does not exist.");

    public static readonly KernelError VersionStale =
        new("K.Simulation.VersionStale", 409, "The protocol version is not at the pinned version; reload and simulate the current version.");

    public static readonly KernelError StudyUnknown =
        new("K.Simulation.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError LibraryItemUnknown =
        new("K.Simulation.LibraryItemUnknown", 404, "The library item does not exist or has a different kind.");

    public static readonly KernelError BaseNotFrozen =
        new("K.Simulation.BaseNotFrozen", 422, "The base version is still a draft; compare with a version whose content is frozen.");

    public static readonly KernelError ContentInvalid =
        new("K.Simulation.ContentInvalid", 422, "The candidate content must be a protocol design (ADR-2472).");

    public static readonly KernelError MappingSpecificationInvalid =
        new("K.Simulation.MappingSpecificationInvalid", 422, "The candidate content must be a valid mapping specification (ADR-2463).");

    public static readonly KernelError RulePackInvalid =
        new("K.Simulation.RulePackInvalid", 422, "The candidate content must be a valid rule pack (spec §10.1).");

    public static readonly KernelError ElementIdDuplicated =
        new("K.Simulation.ElementIdDuplicated", 422, "Two protocol elements share an id; element ids must be unique within a version (K-PROT-1).");
}
