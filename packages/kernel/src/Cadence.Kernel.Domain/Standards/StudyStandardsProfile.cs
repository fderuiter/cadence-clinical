using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Standards;

namespace Cadence.Kernel.Domain.Standards;

/// <summary>
/// What one study declared it uses (spec §6.3, K-STD-1): standard versions (at most one per standard), controlled
/// terminology versions (at most one per code system, each resolved to a version the tenant imported into the Concept
/// Registry, ADR-2460 §4.2) and pinned library item versions (at most one per kind and key).
/// The declaration is revised as a whole; each revision is a new <see cref="AggregateRootBase.VersionIndex"/>, with the
/// previous one in the audit trail. Whether each reference was approved is checked when it is declared, so a later
/// deprecation never changes it.
/// </summary>
public sealed class StudyStandardsProfile : AggregateRoot<StudyStandardsProfileId>
{
    private StudyStandardsProfile() { }

    private StudyStandardsProfile(StudyStandardsProfileId id, TenantId tenantId, StudyId studyId)
        : base(id)
    {
        TenantId = tenantId;
        StudyId = studyId;
    }

    public TenantId TenantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public StandardVersionId[] StandardVersionIds { get; private set; } = [];

    /// <summary>Declared terminology versions, each as <c>codeSystem|version</c>, sorted.</summary>
    public string[] ControlledTerminology { get; private set; } = [];

    /// <summary>
    /// The Concept Registry version of each <see cref="ControlledTerminology"/> entry, in the same order. Empty only for
    /// a declaration made before terminology was resolved whose packages were never imported.
    /// </summary>
    public CodeSystemVersionId[] ControlledTerminologyVersions { get; private set; } = [];

    public LibraryItemId[] LibraryPins { get; private set; } = [];

    public IReadOnlyList<CodeSystemVersionRef> ControlledTerminologyRefs =>
        [.. ControlledTerminology.Select(Parse)];

    /// <summary>Each declared terminology version with its Concept Registry version.</summary>
    public IReadOnlyList<ResolvedCodeSystemVersion> ResolvedTerminology =>
        ControlledTerminologyVersions.Length == ControlledTerminology.Length
            ? [.. ControlledTerminology.Select(Parse).Zip(ControlledTerminologyVersions, (r, v) => new ResolvedCodeSystemVersion(r, v))]
            : [];

    /// <summary>Starts a study's profile; <see cref="Declare"/> fills it.</summary>
    public static StudyStandardsProfile Create(StudyStandardsProfileId id, TenantId tenantId, StudyId studyId) =>
        new(id, tenantId, studyId);

    /// <summary>
    /// Replaces the declaration. <paramref name="standards"/> and <paramref name="pins"/> are the approved records the
    /// caller resolved for the requested ids; each id must appear in them. <paramref name="terminology"/> holds the
    /// Concept Registry versions the caller found for the declared terminology; each declared version must appear in it.
    /// </summary>
    public KernelError? Declare(
        IReadOnlyList<StandardVersionId> standardVersionIds,
        IReadOnlyList<CodeSystemVersionRef> controlledTerminology,
        IReadOnlyList<LibraryItemId> libraryPins,
        IReadOnlyDictionary<StandardVersionId, StandardVersionSummary> standards,
        IReadOnlyDictionary<LibraryItemId, LibraryItemView> pins,
        IReadOnlyDictionary<CodeSystemVersionRef, CodeSystemVersionId> terminology)
    {
        ArgumentNullException.ThrowIfNull(standardVersionIds);
        ArgumentNullException.ThrowIfNull(controlledTerminology);
        ArgumentNullException.ThrowIfNull(libraryPins);
        ArgumentNullException.ThrowIfNull(standards);
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(terminology);

        if (standardVersionIds.Count == 0)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage("A study declares at least one standard version.");
        }

        foreach (var id in standardVersionIds)
        {
            if (!standards.TryGetValue(id, out var standard))
            {
                return StandardsErrors.StandardVersionUnknown.WithMessage($"Standard version {id} does not exist.");
            }

            if (standard.Status != GovernanceStatus.Approved)
            {
                return StandardsErrors.NotApproved.WithMessage($"{standard.StandardCode} {standard.Version} is {standard.Status}.");
            }
        }

        var duplicateStandard = standardVersionIds.Distinct().Select(id => standards[id])
            .GroupBy(s => s.StandardCode, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateStandard is not null)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage($"A study declares one version of {duplicateStandard.Key}, not {duplicateStandard.Count()}.");
        }

        if (controlledTerminology.Any(t => string.IsNullOrWhiteSpace(t.CodeSystem) || string.IsNullOrWhiteSpace(t.Version)
            || t.CodeSystem.Contains('|', StringComparison.Ordinal)))
        {
            return StandardsErrors.DeclarationInvalid.WithMessage("Each terminology version needs a code system (without '|') and a version.");
        }

        var duplicateTerminology = controlledTerminology.Distinct().GroupBy(t => t.CodeSystem, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicateTerminology is not null)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage($"A study declares one version of {duplicateTerminology.Key} terminology.");
        }

        foreach (var reference in controlledTerminology)
        {
            if (reference.ToCodeSystemUri() is null)
            {
                return StandardsErrors.DeclarationInvalid.WithMessage(
                    $"'{reference.CodeSystem}' names no code system: use a CDISC CT package type such as 'sdtmct' or a code-system URI.");
            }

            if (!terminology.ContainsKey(reference))
            {
                return StandardsErrors.DeclarationInvalid.WithMessage(
                    $"{reference} has not been imported into this tenant's Concept Registry; import the package before declaring it.");
            }
        }

        foreach (var id in libraryPins)
        {
            if (!pins.TryGetValue(id, out var pin))
            {
                return StandardsErrors.LibraryItemUnknown.WithMessage($"Library item {id} does not exist.");
            }

            if (pin.Status != GovernanceStatus.Approved)
            {
                return StandardsErrors.NotApproved.WithMessage($"{pin.Kind} {pin.Key} v{pin.Version} is {pin.Status}.");
            }
        }

        var duplicatePin = libraryPins.Distinct().Select(id => pins[id])
            .GroupBy(p => (p.Kind, p.Key)).FirstOrDefault(g => g.Count() > 1);
        if (duplicatePin is not null)
        {
            return StandardsErrors.DeclarationInvalid.WithMessage($"A study pins one version of {duplicatePin.Key.Kind} {duplicatePin.Key.Key}.");
        }

        StandardVersionIds = [.. standardVersionIds.Distinct().Order()];
        var declared = controlledTerminology.Distinct().OrderBy(t => $"{t.CodeSystem}|{t.Version}", StringComparer.Ordinal).ToList();
        ControlledTerminology = [.. declared.Select(t => $"{t.CodeSystem}|{t.Version}")];
        ControlledTerminologyVersions = [.. declared.Select(t => terminology[t])];
        LibraryPins = [.. libraryPins.Distinct().Order()];
        return null;
    }

    private static CodeSystemVersionRef Parse(string value)
    {
        var separator = value.IndexOf('|', StringComparison.Ordinal);
        return new CodeSystemVersionRef(value[..separator], value[(separator + 1)..]);
    }
}
