using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Conformance;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.Rules.Design;

/// <summary>What to check: one protocol version's design, as it is stored now.</summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="ProtocolVersion">The protocol version.</param>
/// <param name="IdempotencyKey">The idempotency key of the <see cref="RecordValidationRun"/> command.</param>
/// <param name="ReasonForChange">Why the run is recorded.</param>
public sealed record DesignConformanceRequest(TenantId Tenant, ProtocolVersionId ProtocolVersion, string IdempotencyKey, string ReasonForChange)
{
    /// <summary>The date the rules' effective periods are evaluated at; null is today (UTC).</summary>
    public DateOnly? AsOf { get; init; }
}

/// <summary>
/// Design-time conformance (K-STD-2, ADR-2464). Reads a protocol version's design, derives its design facts
/// (<see cref="DesignConformance"/>) against the study's mapping specifications, standard packages and controlled
/// terminology, and evaluates every rule pack the study pins over them at <see cref="ValidationStage.Design"/>
/// (<see cref="ValidationRunner"/>). The run names the protocol version's lineage node, at the content read, and each
/// mapping specification version as its inputs; submitting the version for review needs such a run of the draft as it
/// is and no blocking design-stage finding.
/// </summary>
public sealed class DesignConformanceRunner(
    IProtocolDesignReader designs, IStandardsRegistry registry, IConceptRegistry concepts, ValidationRunner runner, IClock clock)
{
    /// <summary>Checks the design and records the run.</summary>
    public async Task<CommandResult<ValidationRunOutcome>> RunAsync(DesignConformanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await designs.GetDesignAsync(request.Tenant, request.ProtocolVersion, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            return KernelErrors.NotFound;
        }

        // Findings follow elements across versions, so a run of an approved or withdrawn version could resolve a finding
        // a draft still has. Conformance gates submission, so it runs on drafts only.
        if (snapshot.Version.Status != ProtocolVersionStatus.Draft)
        {
            return ProtocolErrors.InvalidTransition.WithMessage(
                $"Design conformance runs on a draft; protocol version {snapshot.Version.Id} is {snapshot.Version.Status}.");
        }

        var study = snapshot.Version.StudyId;
        if (await registry.FindStudyStandardsAsync(request.Tenant, study, cancellationToken).ConfigureAwait(false) is not { } profile)
        {
            return RuleErrors.NoRulePacks;
        }

        var pins = new List<LibraryItemView>();
        foreach (var id in profile.LibraryPins.Distinct())
        {
            if (await registry.FindLibraryItemAsync(request.Tenant, id, cancellationToken).ConfigureAwait(false) is { } pin)
            {
                pins.Add(pin);
            }
        }

        // A declaration refuses pins that do not resolve, so null here means the study pins no usable mapping.
        var (mappings, _) = StudyMappings.ResolvePins(pins, profile.StandardVersions);
        var packages = new Dictionary<StandardVersionId, StandardPackage>();
        foreach (var standard in profile.StandardVersions.Distinct())
        {
            if (await registry.GetStandardPackageAsync(request.Tenant, standard, cancellationToken).ConfigureAwait(false) is { } package)
            {
                packages[standard] = package;
            }
        }

        var records = await DesignConformance.BuildAsync(
            new DesignConformanceInput(study, snapshot.Design, mappings, packages),
            new RegistryTerminology(concepts, request.Tenant),
            cancellationToken).ConfigureAwait(false);

        var validation = new ValidationRequest(
            request.Tenant,
            study,
            ValidationStage.Design,
            request.AsOf ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            [.. records.Select(r => new RuleRecord(r.Dataset, r.Key.ToString(), r.Values.ToDictionary(v => v.Key, v => RuleValue.FromText(v.Value), StringComparer.Ordinal)))],
            request.IdempotencyKey,
            request.ReasonForChange)
        {
            Inputs = [snapshot.Node, .. pins.Where(p => p.Kind == LibraryItemKind.MappingSpecification).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => NodeRef.For(p.Id, p.VersionIndex))],
            Datasets = DesignConformance.Datasets,
            TargetKind = FindingTargetKind.DesignElement,
        };
        return await runner.RunAsync(validation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads controlled terminology from the tenant's Concept Registry.</summary>
    private sealed class RegistryTerminology(IConceptRegistry concepts, TenantId tenant) : IDesignTerminology
    {
        public async Task<CodeSystemVersionId?> ResolveAsync(CodeSystemVersionRef version, CancellationToken cancellationToken)
        {
            if (version.ToCodeSystemUri() is not { } uri)
            {
                return null;
            }

            var found = await concepts.FindCodeSystemVersionAsync(tenant, uri, version.Version, cancellationToken).ConfigureAwait(false);
            return found?.Id;
        }

        public async Task<IReadOnlySet<string>?> SubmissionValuesAsync(CodeSystemVersionId version, string codelist, CancellationToken cancellationToken)
        {
            var terms = await concepts.GetGroupAsync(tenant, version, codelist, cancellationToken).ConfigureAwait(false);
            return terms.Count == 0
                ? null
                : terms.Select(t => t.SubmissionValue).OfType<string>().ToHashSet(StringComparer.Ordinal);
        }
    }
}
