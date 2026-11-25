using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Trust;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Application.Validation;

/// <summary>What <see cref="FindingLedger"/> needs to derive a study's findings: its runs, detections and decisions.</summary>
public sealed record ValidationLedgerData(
    StudyId Study, IReadOnlyList<LedgerRun> Runs, IReadOnlyList<LedgerDetection> Detections, IReadOnlyList<LedgerDecision> Decisions)
{
    /// <summary>The derived state of every finding.</summary>
    public IReadOnlyList<FindingState> Derive() => FindingLedger.Derive(Runs, Detections, Decisions);
}

/// <summary>Loads ledger data for the validation handlers. Implemented by kernel persistence; reads only.</summary>
public interface IValidationLedgerSource
{
    /// <summary>The runs, detections and decisions of <paramref name="study"/> at <paramref name="stage"/>.</summary>
    Task<ValidationLedgerData> LoadStudyAsync(TenantId tenant, StudyId study, ValidationStage stage, CancellationToken cancellationToken);

    /// <summary>
    /// The runs of the finding's study and stage, the finding's own detections and its decisions, or null when no run
    /// has detected it.
    /// </summary>
    Task<ValidationLedgerData?> LoadFindingAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken);
}

/// <summary>
/// Transactional read-and-stage port for <c>kernel.validation_finding_projections</c> on the kernel command unit of
/// work (K-TX-1, K-TX-2). Implemented by kernel persistence on <c>KernelCommandTransaction</c>.
/// </summary>
public interface IValidationProjectionStaging
{
    /// <summary>
    /// Applies <paramref name="run"/> to the study's covered finding projections on this command's transaction and
    /// returns the run's <see cref="ValidationRunOutcome"/>.
    /// </summary>
    Task<ValidationRunOutcome> StageValidationRunAsync(
        ValidationRun run, DateTimeOffset recordedAt, CancellationToken cancellationToken);

    /// <summary>Loads one finding's current projected state and study on this command's transaction, or null when unknown.</summary>
    Task<(StudyId Study, FindingProjectionState State)?> FindProjectedFindingAsync(
        ValidationFindingId finding, CancellationToken cancellationToken);

    /// <summary>Stages an updated <see cref="LedgerDecision"/> onto <paramref name="finding"/>'s projection row in this command's transaction.</summary>
    Task StageFindingDecisionAsync(
        ValidationFindingId finding, LedgerDecision decision, CancellationToken cancellationToken);

    /// <summary>Rebuilds <paramref name="study"/>'s projected findings from its immutable ledger rows on this command's transaction.</summary>
    Task<int> StageRebuildStudyAsync(StudyId study, CancellationToken cancellationToken);
}

/// <summary>Handles <see cref="RecordValidationRun"/>.</summary>
internal sealed class RecordValidationRunHandler(IStandardsRegistry registry, IValidationLedgerSource ledger)
    : ICommandHandler<RecordValidationRun, ValidationRunOutcome>
{
    public async Task<CommandResult<ValidationRunOutcome>> HandleAsync(RecordValidationRun command, ICommandContext context, CancellationToken cancellationToken)
    {
        var packs = new List<RunRulePack>();
        foreach (var id in (command.RulePacks ?? []).Distinct())
        {
            var item = await registry.FindLibraryItemAsync(command.TenantId, id, cancellationToken).ConfigureAwait(false);
            if (item is null || item.Kind != LibraryItemKind.RulePack || item.Status == GovernanceStatus.Draft)
            {
                return ValidationErrors.RulePackNotUsable.WithMessage(item switch
                {
                    null => $"Library item {id} does not exist.",
                    { Kind: not LibraryItemKind.RulePack } => $"Library item {id} is a {item.Kind}, not a rule pack.",
                    _ => $"Rule pack {item.Key} v{item.Version} is a draft; release it before validating with it.",
                });
            }

            packs.Add(new RunRulePack(item.Id, item.Key, item.Version, item.ContentHash, item.VersionIndex));
        }

        if (await context.Work.ReadAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ValidationErrors.StudyUnknown;
        }

        var (run, error) = ValidationRun.Record(command.TenantId, command, packs, context.RecordedAt);
        if (error is not null)
        {
            return error;
        }

        if (await context.Work.ExistsAsync<ValidationRun>(command.RunId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        ValidationRunOutcome outcome;
        if (context.Work is IValidationProjectionStaging projection)
        {
            outcome = await projection.StageValidationRunAsync(run!, context.RecordedAt, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var before = await ledger.LoadStudyAsync(command.TenantId, command.StudyId, command.Stage, cancellationToken).ConfigureAwait(false);
            outcome = Outcome(before, run!, context.RecordedAt);
        }

        context.Work.Add(run!);
        foreach (var pack in packs)
        {
            context.Work.LinkInput(NodeRef.For(pack.LibraryItemId, pack.VersionIndex), LineageEdgeKind.DefinedBy);
        }

        foreach (var input in run!.Inputs)
        {
            context.Work.LinkInput(NodeRef.Parse(input.Input), LineageEdgeKind.ValidatedBy);
        }

        context.Work.Raise(new ValidationRunCompleted(
            command.TenantId, run.Id, run.StudyId, run.Stage, run.Packs.Count, run.Datasets.Count, outcome.Detected, outcome.New, outcome.Resolved));
        return CommandResult<ValidationRunOutcome>.Success(outcome);
    }

    /// <summary>Compares the study's findings before and after <paramref name="run"/> (K-FIND-1).</summary>
    private static ValidationRunOutcome Outcome(ValidationLedgerData before, ValidationRun run, DateTimeOffset recordedAt)
    {
        var previous = before.Derive();
        var known = previous.Select(f => f.Finding).ToHashSet();
        var thisRun = new LedgerRun(
            run.Id, run.Stage, recordedAt, run.Packs.Select(p => p.PackKey).ToHashSet(StringComparer.Ordinal), run.Datasets.Select(d => d.Dataset).ToHashSet(StringComparer.Ordinal));
        var detections = run.Detections.Select(d => new LedgerDetection(run.Id, d.FindingId, d.Stage, d.PackKey, d.Dataset, d.Severity)).ToList();
        var after = FindingLedger.Derive([.. before.Runs, thisRun], [.. before.Detections, .. detections], before.Decisions)
            .ToDictionary(f => f.Finding);
        var resolved = previous.Count(f => f.Status != FindingStatus.Resolved && after[f.Finding].Status == FindingStatus.Resolved);
        return new ValidationRunOutcome(run.Id, detections.Count, detections.Count(d => !known.Contains(d.Finding)), resolved);
    }
}

/// <summary>
/// Handles the decisions people make on a finding: investigate, classify, accept, explain and reopen. Each reads the
/// finding's derived status first, so a decision is made on the finding as it stands now.
/// </summary>
internal sealed class FindingDecisionHandler(IValidationLedgerSource ledger, IAttestationVerifier attestations) :
    ICommandHandler<InvestigateFinding, ValidationFindingId>,
    ICommandHandler<ClassifyFinding, ValidationFindingId>,
    ICommandHandler<AcceptFinding, ValidationFindingId>,
    ICommandHandler<ExplainFinding, ValidationFindingId>,
    ICommandHandler<ReopenFinding, ValidationFindingId>
{
    public Task<CommandResult<ValidationFindingId>> HandleAsync(InvestigateFinding command, ICommandContext context, CancellationToken cancellationToken) =>
        DecideAsync(command.FindingId, context, cancellationToken, (finding, state) => Task.FromResult(
            finding.Investigate(state.Status, context.Actor.Id, context.RecordedAt)
            ?? Raise(context, new FindingInvestigationStarted(command.TenantId, command.FindingId))));

    public Task<CommandResult<ValidationFindingId>> HandleAsync(ClassifyFinding command, ICommandContext context, CancellationToken cancellationToken) =>
        DecideAsync(command.FindingId, context, cancellationToken, (finding, state) => Task.FromResult(
            finding.Classify(state.Status, command.Classification)
            ?? Raise(context, new FindingClassified(command.TenantId, command.FindingId, command.Classification))));

    public Task<CommandResult<ValidationFindingId>> HandleAsync(AcceptFinding command, ICommandContext context, CancellationToken cancellationToken) =>
        DecideAsync(command.FindingId, context, cancellationToken, (finding, state) => ConcludeAsync(
            finding, state, FindingStatus.Accepted, AttestationMeaning.Responsibility, command.Rationale, command.AttestationId, context, cancellationToken,
            new FindingAccepted(command.TenantId, command.FindingId, state.LastDetectedRun, command.AttestationId)));

    public Task<CommandResult<ValidationFindingId>> HandleAsync(ExplainFinding command, ICommandContext context, CancellationToken cancellationToken) =>
        DecideAsync(command.FindingId, context, cancellationToken, (finding, state) => ConcludeAsync(
            finding, state, FindingStatus.Explained, AttestationMeaning.Approved, command.Explanation, command.AttestationId, context, cancellationToken,
            new FindingExplained(command.TenantId, command.FindingId, state.LastDetectedRun, command.AttestationId)));

    public Task<CommandResult<ValidationFindingId>> HandleAsync(ReopenFinding command, ICommandContext context, CancellationToken cancellationToken) =>
        DecideAsync(command.FindingId, context, cancellationToken, (finding, state) => Task.FromResult(
            finding.Reopen(state.Status, context.Actor.Id, context.RecordedAt)
            ?? Raise(context, new FindingReopened(command.TenantId, command.FindingId, state.Status))));

    /// <summary>Accepts or explains a finding once the attestation over the decision's content is verified (K-FIND-2).</summary>
    private async Task<KernelError?> ConcludeAsync(
        ValidationFinding finding,
        FindingState state,
        FindingStatus decision,
        AttestationMeaning meaning,
        string text,
        AttestationId attestation,
        ICommandContext context,
        CancellationToken cancellationToken,
        object decided)
    {
        if (!FindingText.IsValid(text))
        {
            return ValidationErrors.DecisionTextInvalid;
        }

        var target = new AttestationTarget(NodeKinds.ValidationFinding, finding.Id.Value, FindingDecisionContent.Hash(finding.Id, state.LastDetectedRun, decision, text));
        var check = await attestations.VerifyAsync(new AttestationRequirement(context.TenantId, attestation, meaning, target, context.Actor), cancellationToken)
            .ConfigureAwait(false);
        if (!check.Satisfied)
        {
            return ValidationErrors.AttestationRejected.WithMessage(check.Reason ?? ValidationErrors.AttestationRejected.Message);
        }

        return finding.Conclude(state.Status, decision, text, state.LastDetectedRun, attestation, context.Actor.Id, context.RecordedAt)
            ?? Raise(context, decided);
    }

    /// <summary>
    /// Derives the finding's status, loads or starts its decision record, and applies <paramref name="decide"/>. The
    /// record is added only when the decision succeeds.
    /// </summary>
    private async Task<CommandResult<ValidationFindingId>> DecideAsync(
        ValidationFindingId id, ICommandContext context, CancellationToken cancellationToken, Func<ValidationFinding, FindingState, Task<KernelError?>> decide)
    {
        StudyId study;
        FindingState state;
        var projection = context.Work as IValidationProjectionStaging;
        if (projection is not null)
        {
            if (await projection.FindProjectedFindingAsync(id, cancellationToken).ConfigureAwait(false) is not var (projectedStudy, projectedState))
            {
                return ValidationErrors.FindingUnknown;
            }

            study = projectedStudy;
            state = projectedState.ToFindingState();
        }
        else
        {
            var data = await ledger.LoadFindingAsync(context.TenantId, id, cancellationToken).ConfigureAwait(false);
            if (data?.Derive().SingleOrDefault(f => f.Finding == id) is not { } derivedState)
            {
                return ValidationErrors.FindingUnknown;
            }

            study = data.Study;
            state = derivedState;
        }

        var finding = await context.Work.FindAsync<ValidationFinding>(id, cancellationToken).ConfigureAwait(false);
        var isNew = finding is null;
        finding ??= ValidationFinding.Start(id, context.TenantId, study);
        if (await decide(finding, state).ConfigureAwait(false) is { } error)
        {
            return error;
        }

        if (isNew)
        {
            context.Work.Add(finding);
        }

        if (projection is not null)
        {
            await projection.StageFindingDecisionAsync(
                id,
                new LedgerDecision(id, finding.Decision, finding.DecidedAt, finding.Classification),
                cancellationToken).ConfigureAwait(false);
        }

        // K-FIND-3: each version of the decisions record derives from the run it was made on, and through that run's
        // lineage from the rule pack and the specifications the run validated.
        context.Work.LinkInput(NodeRef.For(state.LastDetectedRun, 1), LineageEdgeKind.DerivedFrom);

        return CommandResult<ValidationFindingId>.Success(id);
    }

    private static KernelError? Raise(ICommandContext context, object domainEvent)
    {
        context.Work.Raise(domainEvent);
        return null;
    }
}

/// <summary>
/// Lets a person sign a decision on a finding (K-FIND-2). The decision is signed before it is recorded, so the signed
/// hash covers the decision itself (<see cref="FindingDecisionContent"/>), which the accept or explain handler
/// recomputes from the command and the run that last detected the finding. Here the finding must exist and be open
/// or under investigation.
/// </summary>
internal sealed class FindingDecisionAttestationCheck(IValidationLedgerSource ledger) : IAttestationTargetCheck
{
    public string Kind => NodeKinds.ValidationFinding;

    public async Task<KernelError?> CheckAsync(AttestationTarget target, ICommandContext context, CancellationToken cancellationToken)
    {
        var id = ValidationFindingId.FromGuid(target.Id);
        FindingState? state;
        if (context.Work is IValidationProjectionStaging projection)
        {
            state = (await projection.FindProjectedFindingAsync(id, cancellationToken).ConfigureAwait(false))?.State.ToFindingState();
        }
        else
        {
            var data = await ledger.LoadFindingAsync(context.TenantId, id, cancellationToken).ConfigureAwait(false);
            state = data?.Derive().SingleOrDefault(f => f.Finding == id);
        }

        if (state is null)
        {
            return TrustErrors.TargetUnknown;
        }

        return state.Status is FindingStatus.Open or FindingStatus.Investigating
            ? null
            : TrustErrors.TargetNotSignable.WithMessage($"Only an open or investigating finding can be accepted or explained; this one is {state.Status}.");
    }
}

/// <summary>Handles <see cref="RebuildValidationFindingProjections"/>.</summary>
internal sealed class RebuildValidationFindingProjectionsHandler(IValidationLedgerSource ledger)
    : ICommandHandler<RebuildValidationFindingProjections, int>
{
    private static readonly ValidationStage[] AllStages = Enum.GetValues<ValidationStage>();

    public async Task<CommandResult<int>> HandleAsync(
        RebuildValidationFindingProjections command,
        ICommandContext context,
        CancellationToken cancellationToken)
    {
        if (await context.Work.ReadAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ValidationErrors.StudyUnknown;
        }

        if (context.Work is IValidationProjectionStaging projection)
        {
            var rebuilt = await projection.StageRebuildStudyAsync(command.StudyId, cancellationToken).ConfigureAwait(false);
            return CommandResult<int>.Success(rebuilt);
        }

        var runs = new List<LedgerRun>();
        var detections = new List<LedgerDetection>();
        var decisionsById = new Dictionary<ValidationFindingId, LedgerDecision>();
        foreach (var stage in AllStages)
        {
            var stageData = await ledger.LoadStudyAsync(command.TenantId, command.StudyId, stage, cancellationToken).ConfigureAwait(false);
            runs.AddRange(stageData.Runs);
            detections.AddRange(stageData.Detections);
            foreach (var decision in stageData.Decisions)
            {
                decisionsById[decision.Finding] = decision;
            }
        }

        var derived = FindingLedger.Derive(runs, detections, [.. decisionsById.Values]);
        return CommandResult<int>.Success(derived.Count);
    }
}

/// <summary>Registers the validation run and finding command handlers (spec §10.3).</summary>
public static class KernelValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the validation command handlers. The host must also register kernel persistence's
    /// <see cref="IValidationLedgerSource"/> and <see cref="IValidationFindings"/> (<c>AddKernelValidationPersistence</c>),
    /// the <see cref="IStandardsRegistry"/> that resolves rule packs, and the <see cref="IAttestationVerifier"/>.
    /// </summary>
    public static IServiceCollection AddKernelValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<ICommandHandler<RecordValidationRun, ValidationRunOutcome>, RecordValidationRunHandler>();
        services.AddTransient<ICommandHandler<InvestigateFinding, ValidationFindingId>, FindingDecisionHandler>();
        services.AddTransient<ICommandHandler<ClassifyFinding, ValidationFindingId>, FindingDecisionHandler>();
        services.AddTransient<ICommandHandler<AcceptFinding, ValidationFindingId>, FindingDecisionHandler>();
        services.AddTransient<ICommandHandler<ExplainFinding, ValidationFindingId>, FindingDecisionHandler>();
        services.AddTransient<ICommandHandler<ReopenFinding, ValidationFindingId>, FindingDecisionHandler>();
        services.AddTransient<ICommandHandler<RebuildValidationFindingProjections, int>, RebuildValidationFindingProjectionsHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAttestationTargetCheck, FindingDecisionAttestationCheck>());
        return services;
    }
}
