using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api.StudyImports;

internal sealed class KernelStudyImportConceptStager(TenantId tenant, IConceptRegistry concepts)
{
    private readonly Dictionary<string, ProtocolConceptResolution> _resolvedConceptsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConceptRef> _stagedBiomedicalByCode = new(StringComparer.Ordinal);
    private readonly Dictionary<ConceptId, ConceptRef> _stagedByConceptId = [];
    private readonly Dictionary<string, ProtocolTerminologyResolution> _resolvedTerminologyByKey = new(StringComparer.Ordinal);

    internal List<StagedProtocolConcept> StagedConcepts { get; } = [];
    internal Dictionary<ConceptRef, string> StagedLabelsByRef { get; } = [];
    internal Dictionary<CodeSystemId, StagedCodeSystem> StagedCodeSystemsById { get; } = [];
    internal Dictionary<CodeSystemVersionId, StagedCodeSystemVersion> StagedCodeSystemVersionsById { get; } = [];

    internal async ValueTask<ProtocolConceptResolution> ResolveOrDefineConceptAsync(
        ProtocolConceptCandidate candidate,
        CancellationToken ct)
    {
        if (_resolvedConceptsByKey.TryGetValue(candidate.StableKey, out var cached))
        {
            return cached;
        }

        var label = string.IsNullOrWhiteSpace(candidate.PreferredLabel) ? "Protocol Item" : candidate.PreferredLabel.Trim();
        var definition = string.IsNullOrWhiteSpace(candidate.Definition) ? label : candidate.Definition.Trim();

        if (!string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode))
        {
            var bcCode = candidate.BiomedicalConceptCode.Trim();
            if (_stagedBiomedicalByCode.TryGetValue(bcCode, out var stagedBcRef))
            {
                var stagedHit = new ProtocolConceptResolution(stagedBcRef, UsedFallbackSponsorConcept: false);
                _resolvedConceptsByKey[candidate.StableKey] = stagedHit;
                return stagedHit;
            }

            var foundBc = await concepts.FindBiomedicalConceptAsync(tenant, bcCode, ct).ConfigureAwait(false);
            if (foundBc is not null && foundBc.Status != ConceptStatus.Deprecated)
            {
                var hit = new ProtocolConceptResolution(foundBc.Concept, UsedFallbackSponsorConcept: false);
                _resolvedConceptsByKey[candidate.StableKey] = hit;
                return hit;
            }

            if (foundBc is null)
            {
                var bcConceptId = ConceptId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "biomedical-concept", bcCode));
                var existingByBcId = await concepts.GetCurrentAsync(tenant, bcConceptId, ct).ConfigureAwait(false);
                if (existingByBcId is not null
                    && existingByBcId.Status != ConceptStatus.Deprecated
                    && existingByBcId.Origin == ConceptOrigin.CdiscBiomedicalConcept
                    && string.Equals(existingByBcId.ExternalCode, bcCode, StringComparison.Ordinal))
                {
                    var reusedBc = new ProtocolConceptResolution(existingByBcId.Concept, UsedFallbackSponsorConcept: false);
                    _resolvedConceptsByKey[candidate.StableKey] = reusedBc;
                    return reusedBc;
                }

                if (existingByBcId is null)
                {
                    var newBcRef = new ConceptRef(bcConceptId, 1);
                    StagedConcepts.Add(new StagedProtocolConcept(
                        bcConceptId,
                        ConceptOrigin.CdiscBiomedicalConcept,
                        bcCode,
                        ConceptKind.BiomedicalConcept,
                        label,
                        definition));
                    _stagedBiomedicalByCode[bcCode] = newBcRef;
                    _stagedByConceptId[bcConceptId] = newBcRef;
                    StagedLabelsByRef[newBcRef] = label;

                    var stagedResolution = new ProtocolConceptResolution(newBcRef, UsedFallbackSponsorConcept: false);
                    _resolvedConceptsByKey[candidate.StableKey] = stagedResolution;
                    return stagedResolution;
                }
            }
        }

        var usedFallback = !string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode);
        var conceptId = ConceptId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "import-concept", candidate.StableKey));
        if (_stagedByConceptId.TryGetValue(conceptId, out var alreadyStagedSponsor))
        {
            var cachedSponsor = new ProtocolConceptResolution(alreadyStagedSponsor, UsedFallbackSponsorConcept: usedFallback);
            _resolvedConceptsByKey[candidate.StableKey] = cachedSponsor;
            return cachedSponsor;
        }

        var existingConcept = await concepts.GetCurrentAsync(tenant, conceptId, ct).ConfigureAwait(false);
        if (existingConcept is not null && existingConcept.Status != ConceptStatus.Deprecated)
        {
            var reused = new ProtocolConceptResolution(existingConcept.Concept, UsedFallbackSponsorConcept: usedFallback);
            _resolvedConceptsByKey[candidate.StableKey] = reused;
            return reused;
        }

        var targetSponsorId = existingConcept is null
            ? conceptId
            : ConceptId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "import-concept-active", candidate.StableKey));
        var sponsorRef = new ConceptRef(targetSponsorId, 1);
        if (_stagedByConceptId.TryAdd(targetSponsorId, sponsorRef))
        {
            StagedConcepts.Add(new StagedProtocolConcept(
                targetSponsorId,
                ConceptOrigin.Sponsor,
                ExternalCode: null,
                candidate.Kind,
                label,
                definition));
            StagedLabelsByRef[sponsorRef] = label;
        }

        var created = new ProtocolConceptResolution(sponsorRef, UsedFallbackSponsorConcept: usedFallback);
        _resolvedConceptsByKey[candidate.StableKey] = created;
        return created;
    }

    internal async ValueTask<ProtocolTerminologyResolution> ResolveOrRegisterTerminologyAsync(
        ProtocolTerminologyCandidate candidate,
        CancellationToken ct)
    {
        var boundedGroup = BoundTerminologyToken(candidate.CodelistCode);
        var normalizedEntries = NormalizeTerminologyEntries(candidate.Entries, boundedGroup);

        if (!string.IsNullOrWhiteSpace(candidate.CodeSystemUri) && !string.IsNullOrWhiteSpace(candidate.CodeSystemVersion))
        {
            foreach (var candidateUri in CandidateCodeSystemUris(candidate.CodeSystemUri))
            {
                var preExisting = await concepts.FindCodeSystemVersionAsync(tenant, candidateUri, candidate.CodeSystemVersion.Trim(), ct).ConfigureAwait(false);
                if (preExisting is null)
                {
                    continue;
                }

                if (normalizedEntries.Count == 0)
                {
                    if (boundedGroup is not null)
                    {
                        var emptyCheckGroup = await concepts.GetGroupAsync(tenant, preExisting.Id, boundedGroup, ct).ConfigureAwait(false);
                        if (emptyCheckGroup.Count > 0)
                        {
                            return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id, boundedGroup));
                        }
                    }

                    return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id));
                }

                if (boundedGroup is not null)
                {
                    var existingGroup = await concepts.GetGroupAsync(tenant, preExisting.Id, boundedGroup, ct).ConfigureAwait(false);
                    var groupCodes = existingGroup.Select(e => e.Code).ToHashSet(StringComparer.Ordinal);
                    if (existingGroup.Count == normalizedEntries.Count
                        && normalizedEntries.All(e => groupCodes.Contains(e.Code)))
                    {
                        return new ProtocolTerminologyResolution(new TerminologyBinding(preExisting.Id, boundedGroup));
                    }
                }
            }
        }

        if (normalizedEntries.Count == 0)
        {
            return new ProtocolTerminologyResolution(null);
        }

        var effectiveGroup = boundedGroup ?? BoundTerminologyToken(candidate.SourceKey) ?? "codelist";
        var groupedEntries = normalizedEntries
            .Select(e => e.GroupCode == effectiveGroup ? e : e with { GroupCode = effectiveGroup })
            .ToList();

        var licence = InferLicenceClass(candidate.CodeSystemUri);
        var systemUri = licence switch
        {
            LicenseClass.Licensed => "urn:cadence:terminology:protocol-codelists:licensed",
            LicenseClass.AcceptedTerms => "urn:cadence:terminology:protocol-codelists:accepted-terms",
            _ => "urn:cadence:terminology:protocol-codelists",
        };

        var packageBytes = SerializeTerminologyEntries(groupedEntries);
        var digest = Convert.ToHexStringLower(SHA256.HashData(packageBytes));
        var versionPrefix = SanitizeVersionPrefix(effectiveGroup);
        var versionLabel = $"{versionPrefix}-{digest[..16]}";
        var cacheKey = $"{systemUri}|{versionLabel}|{effectiveGroup}";
        if (_resolvedTerminologyByKey.TryGetValue(cacheKey, out var cachedTerminology))
        {
            return cachedTerminology;
        }

        var existingPinned = await concepts.FindCodeSystemVersionAsync(tenant, systemUri, versionLabel, ct).ConfigureAwait(false);
        if (existingPinned is not null)
        {
            var reusedBinding = new ProtocolTerminologyResolution(new TerminologyBinding(existingPinned.Id, effectiveGroup));
            _resolvedTerminologyByKey[cacheKey] = reusedBinding;
            return reusedBinding;
        }

        var codeSystemId = CodeSystemId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "import-codesystem", systemUri));
        StagedCodeSystemsById.TryAdd(codeSystemId, new StagedCodeSystem(codeSystemId, systemUri, licence));

        var versionId = CodeSystemVersionId.FromGuid(KernelStudyDirectoryResolver.DeriveGuid(tenant, "import-codesystem-version", $"{systemUri}:{versionLabel}"));
        var releasedOn = !string.IsNullOrWhiteSpace(candidate.CodeSystemVersion)
            && DateOnly.TryParseExact(candidate.CodeSystemVersion.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedRelease)
                ? parsedRelease
                : new DateOnly(2026, 1, 1);
        StagedCodeSystemVersionsById.TryAdd(
            versionId,
            new StagedCodeSystemVersion(
                versionId,
                codeSystemId,
                systemUri,
                versionLabel,
                releasedOn,
                new TerminologyPackage(
                    digest,
                    $"{versionPrefix}.json",
                    systemUri,
                    packageBytes.LongLength,
                    "application/vnd.cadence.codelist+json"),
                groupedEntries));

        var createdBinding = new ProtocolTerminologyResolution(new TerminologyBinding(versionId, effectiveGroup));
        _resolvedTerminologyByKey[cacheKey] = createdBinding;
        return createdBinding;
    }

    private static IReadOnlyList<string> CandidateCodeSystemUris(string rawCodeSystem)
    {
        var trimmed = rawCodeSystem.Trim();
        var candidates = new List<string>();
        if (KnownCodeSystems.UriFor(trimmed) is { } canonical)
        {
            candidates.Add(canonical);
        }

        if (!candidates.Contains(trimmed, StringComparer.Ordinal))
        {
            candidates.Add(trimmed);
        }

        if (string.Equals(trimmed, "http://www.cdisc.org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "https://www.cdisc.org", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "NCI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "CDISC", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var fallback in (string[])[KnownCodeSystems.NciThesaurus, KnownCodeSystems.SdtmCt, KnownCodeSystems.CdashCt, KnownCodeSystems.ProtocolCt])
            {
                if (!candidates.Contains(fallback, StringComparer.Ordinal))
                {
                    candidates.Add(fallback);
                }
            }
        }

        return candidates;
    }

    private static LicenseClass InferLicenceClass(string? rawCodeSystem)
    {
        if (string.IsNullOrWhiteSpace(rawCodeSystem))
        {
            return LicenseClass.Open;
        }

        var trimmed = rawCodeSystem.Trim();
        if (KnownCodeSystems.UriFor(trimmed) is { } canonical && KnownCodeSystems.RequiredLicence(canonical) is { } knownLicence)
        {
            return knownLicence;
        }

        if (trimmed.Contains("snomed", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("meddra", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("whodrug", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseClass.Licensed;
        }

        if (trimmed.Contains("loinc", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("rxnorm", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseClass.AcceptedTerms;
        }

        return LicenseClass.Open;
    }

    private static string? BoundTerminologyToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var trimmed = rawToken.Trim();
        if (trimmed.Length <= 64)
        {
            return trimmed;
        }

        var suffix = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)))[..8];
        return $"{trimmed[..55]}-{suffix}";
    }

    private static List<CodeSystemEntryData> NormalizeTerminologyEntries(
        IReadOnlyList<CodeSystemEntryData>? rawEntries,
        string? defaultGroup)
    {
        if (rawEntries is null || rawEntries.Count == 0)
        {
            return [];
        }

        var normalized = new List<CodeSystemEntryData>(rawEntries.Count);
        var seen = new HashSet<(string? Group, string Code)>();
        foreach (var entry in rawEntries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Code))
            {
                continue;
            }

            var rawCode = entry.Code.Trim();
            var boundedCode = BoundTerminologyToken(rawCode)!;
            var group = BoundTerminologyToken(entry.GroupCode) ?? defaultGroup;
            if (!seen.Add((group, boundedCode)))
            {
                continue;
            }

            var display = string.IsNullOrWhiteSpace(entry.Display) ? rawCode : entry.Display.Trim();
            if (display.Length > 1000)
            {
                display = display[..1000];
            }

            string? submission = null;
            if (!string.IsNullOrWhiteSpace(entry.SubmissionValue))
            {
                submission = entry.SubmissionValue.Trim();
            }
            else if (!string.Equals(boundedCode, rawCode, StringComparison.Ordinal))
            {
                submission = rawCode;
            }

            if (submission is { Length: > 255 })
            {
                submission = submission[..255];
            }

            normalized.Add(new CodeSystemEntryData(
                boundedCode,
                display,
                GroupCode: group,
                SubmissionValue: submission,
                Definition: string.IsNullOrWhiteSpace(entry.Definition) ? null : entry.Definition.Trim(),
                Synonyms: entry.Synonyms));
        }

        return normalized;
    }

    private static byte[] SerializeTerminologyEntries(IReadOnlyList<CodeSystemEntryData> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("code", entry.Code);
                writer.WriteString("display", entry.Display);
                if (entry.GroupCode is not null)
                {
                    writer.WriteString("group", entry.GroupCode);
                }

                if (entry.SubmissionValue is not null)
                {
                    writer.WriteString("submissionValue", entry.SubmissionValue);
                }

                if (entry.Definition is not null)
                {
                    writer.WriteString("definition", entry.Definition);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static string SanitizeVersionPrefix(string raw)
    {
        var sb = new StringBuilder();
        foreach (var ch in raw)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }

            if (sb.Length >= 32)
            {
                break;
            }
        }

        var prefix = sb.ToString().Trim('-', '.', '_');
        return string.IsNullOrEmpty(prefix) ? "codelist" : prefix;
    }
}
