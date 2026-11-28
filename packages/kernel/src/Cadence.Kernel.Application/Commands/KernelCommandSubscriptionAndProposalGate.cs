using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Intelligence;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Trust;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Commands;

internal static class KernelCommandSubscriptionAndProposalGate
{
    private static readonly ProductFamily[] BlueprintCommandFamilies = [ProductFamily.Blueprint];
    private static readonly ProductFamily[] CaptureCommandFamilies = [ProductFamily.Capture];
    private static readonly ProductFamily[] StudySetupCommandFamilies = [ProductFamily.Blueprint, ProductFamily.Capture, ProductFamily.Orbit];
    private static readonly ProductFamily[] AnyEntitledCommandFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

    internal static async Task<KernelError?> CheckSubscriptionActiveAsync(
        IServiceProvider services,
        IKernelCommandTransaction transaction,
        CommandExecution execution,
        CancellationToken cancellationToken)
    {
        if (services.GetService<ITenantEntitlementReader>() is not { } reader)
        {
            return null;
        }

        var snapshot = await reader
            .GetEntitlementsAsync(execution.Command.TenantId, execution.RecordedAt, cancellationToken)
            .ConfigureAwait(false);
        var tenantRef = NodeRef.For(execution.Command.TenantId);
        if (snapshot.IsReadOnly)
        {
            var condition = EvaluatedCondition.Unsatisfied(
                "subscription.active",
                "Activate or renew the workspace subscription to record new study mutations; regulated reads, exports, closeout signatures, and audit trails remain available.",
                tenantRef);
            var refusal = KernelErrors.SubscriptionExpired with
            {
                Explanation = new Explanation(
                    tenantRef,
                    $"{execution.CommandType} is refused because the workspace trial subscription has expired.",
                    "kernel.subscription",
                    [new ExplanationStep(KernelErrors.SubscriptionExpired.Message, [tenantRef])],
                    [condition],
                    [new NextAction(condition.Description, "subscription.activate", tenantRef)]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        if (ResolveRequiredCommandFamilies(execution.Command) is { } requiredFamilies
            && !requiredFamilies.Any(required => snapshot.ProductFamilies.Contains(required)))
        {
            string familyList = string.Join(", ", requiredFamilies.Select(static f => f.ToString()));
            var condition = EvaluatedCondition.Unsatisfied(
                "subscription.product_family",
                $"Enable one of the required product families ({familyList}) on the workspace subscription.",
                tenantRef);
            var refusal = CommercialErrors.ProductFamilyNotEntitled
                .WithMessage($"{CommercialErrors.ProductFamilyNotEntitled.Message} Required product family: {familyList}.") with
            {
                Explanation = new Explanation(
                    tenantRef,
                    $"{execution.CommandType} is refused because the workspace is not entitled to {familyList}.",
                    "kernel.subscription",
                    [new ExplanationStep(CommercialErrors.ProductFamilyNotEntitled.Message, [tenantRef])],
                    [condition],
                    [new NextAction(condition.Description, "entitlement.grant", tenantRef)]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        if (snapshot.Scoping is { } scoping
            && await CheckSubscriptionScopingCapsAsync(services, execution.Command, execution.RecordedAt, scoping, cancellationToken).ConfigureAwait(false) is { } capDetail)
        {
            var condition = EvaluatedCondition.Unsatisfied("subscription.scoping", capDetail, tenantRef);
            var refusal = CommercialErrors.ScopingCapExceeded.WithMessage(capDetail) with
            {
                Explanation = new Explanation(
                    tenantRef,
                    $"{execution.CommandType} is refused because it exceeds the workspace subscription's contracted scoping parameters.",
                    "kernel.subscription",
                    [new ExplanationStep(capDetail, [tenantRef])],
                    [condition],
                    [new NextAction("Update the workspace subscription against a new commercial quote covering the expanded study scope.", "subscription.activate", tenantRef)]),
            };
            await transaction.RecordRefusalAsync(refusal, cancellationToken).ConfigureAwait(false);
            return refusal;
        }

        return null;
    }

    internal static async Task<KernelError?> ExecuteUnderProposalAsync(
        IServiceProvider services,
        IKernelCommandTransaction transaction,
        CommandExecution execution,
        ProposalId proposalId,
        CancellationToken cancellationToken)
    {
        var catalog = services.GetService<KernelCommandCatalog>() ?? KernelCommandCatalog.Kernel;
        if (catalog.Find(execution.Command.GetType()) is not { } entry || entry.ResourceOf(execution.Command) is not { } resource)
        {
            return ProposalErrors.CapabilityNotRequested;
        }

        if (await transaction.Work.ReadAsync<Proposal>(proposalId, cancellationToken).ConfigureAwait(false) is not { } proposal)
        {
            return ProposalErrors.ExecutionUnknown;
        }

        if (proposal.CheckExecution(execution.Actor, entry.Capability.Token, execution.RecordedAt) is { } refused)
        {
            return refused;
        }

        if (services.GetService<IAuthorityStore>() is not { } store)
        {
            return ProposalErrors.OutsideStudy.WithMessage("The command's study cannot be checked against the proposal's.");
        }

        var path = await store.PathOfAsync(execution.Command.TenantId, resource, cancellationToken).ConfigureAwait(false);
        if (path.Study is { } study && study != proposal.StudyId)
        {
            return ProposalErrors.OutsideStudy;
        }

        transaction.RecordProposalExecution(NodeRef.For(proposal.Id, proposal.VersionIndex));
        transaction.Work.Raise(new ProposalExecuted(
            proposal.TenantId,
            proposal.Id,
            proposal.StudyId,
            execution.CommandType,
            entry.Capability.Token,
            resource.ToString(),
            execution.Actor.Id,
            proposal.ApprovalAttestationId!.Value,
            proposal.ProposedById,
            proposal.ProposedByKind,
            proposal.ModelId,
            proposal.RequestedById));
        return null;
    }

    private static IReadOnlyList<ProductFamily>? ResolveRequiredCommandFamilies(ICommand command)
    {
        if (command is SwitchTrialTier or GrantProductEntitlement or RevokeProductEntitlement)
        {
            return null;
        }

        if (command is CreateProtocolDraft
            or ReviseProtocolDraft
            or SubmitProtocolVersionForReview
            or ReturnProtocolVersionToDraft
            or ApproveProtocolVersion
            or WithdrawProtocolVersion
            or ForkProtocolVersion
            or ImportProtocolDraft
            or SaveSimulationReport
            or RegisterCodeSystem
            or ImportCodeSystemVersion
            or DefineConcept
            or ReviseConcept
            or DeprecateConcept
            or MapConcept
            or ReviewConceptMapping)
        {
            return BlueprintCommandFamilies;
        }

        if (command is RegisterParticipant
            or ScreenParticipant
            or ChangeEnrollmentStatus
            or WithdrawParticipant
            or OpenCollectionInstance
            or RecordCollectedValues
            or RecordUnaskedCollectionItem
            or RecordObservation
            or CorrectObservation
            or VerifyObservation
            or RaiseQuery
            or AnswerQuery
            or RetractAssertion
            or RecordPersonIdentity
            or RecordSiteLocalIdentity
            or LinkParticipantToPerson)
        {
            return CaptureCommandFamilies;
        }

        if (command is RegisterStudy
            or RegisterSite
            or LinkSiteToStudy
            or DeclareEffectivity
            or ChangeEffectivityStatus
            or ActivateStudySite)
        {
            return StudySetupCommandFamilies;
        }

        string ns = command.GetType().Namespace ?? string.Empty;
        if (ns.StartsWith("Cadence.Kernel.Standards", StringComparison.Ordinal))
        {
            return BlueprintCommandFamilies;
        }

        if (ns.StartsWith("Cadence.Apps.Execution", StringComparison.Ordinal)
            || ns.StartsWith("Cadence.Modules.Execution", StringComparison.Ordinal))
        {
            return CaptureCommandFamilies;
        }

        return AnyEntitledCommandFamilies;
    }

    private static async Task<string?> CheckSubscriptionScopingCapsAsync(
        IServiceProvider services,
        ICommand command,
        DateTimeOffset recordedAt,
        SubscriptionScoping scoping,
        CancellationToken cancellationToken)
    {
        var usageReader = services.GetService<ISubscriptionScopingUsageReader>();

        switch (command)
        {
            case RegisterStudy registerStudy:
                {
                    string trimmedPhase = registerStudy.Phase?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(trimmedPhase)
                        && !string.Equals(trimmedPhase, scoping.StudyPhase, StringComparison.OrdinalIgnoreCase))
                    {
                        return $"Study phase '{trimmedPhase}' does not match the workspace subscription's contracted phase ('{scoping.StudyPhase}').";
                    }

                    if (usageReader is not null)
                    {
                        int currentStudies = await usageReader
                            .CountStudiesAsync(registerStudy.TenantId, registerStudy.StudyId, cancellationToken)
                            .ConfigureAwait(false);
                        if (currentStudies >= scoping.ActiveStudyCount)
                        {
                            return $"The workspace subscription allows at most {scoping.ActiveStudyCount} active study(ies), and {currentStudies} already exist.";
                        }
                    }

                    return null;
                }

            case RegisterSite registerSite:
                {
                    string trimmedCountry = registerSite.Country?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(trimmedCountry)
                        && !scoping.Countries.Any(c => string.Equals(c, trimmedCountry, StringComparison.OrdinalIgnoreCase)))
                    {
                        return $"Country '{trimmedCountry}' is outside the workspace subscription's contracted countries ({string.Join(", ", scoping.Countries)}).";
                    }

                    if (usageReader is not null)
                    {
                        int currentSites = await usageReader
                            .CountSitesAsync(registerSite.TenantId, registerSite.SiteId, cancellationToken)
                            .ConfigureAwait(false);
                        if (currentSites >= scoping.SiteCount)
                        {
                            return $"The workspace subscription allows at most {scoping.SiteCount} site(s), and {currentSites} already exist.";
                        }
                    }

                    return null;
                }

            case DeclareEffectivity declareEffectivity
                when (declareEffectivity.DeclaredAt - recordedAt).TotalDays > scoping.StudyDurationMonths * 31.0:
                {
                    return $"Effectivity horizon exceeds the workspace subscription's contracted study duration ({scoping.StudyDurationMonths} months).";
                }

            case ChangeEffectivityStatus changeEffectivity
                when (changeEffectivity.EffectiveAt - recordedAt).TotalDays > scoping.StudyDurationMonths * 31.0:
                {
                    return $"Effectivity horizon exceeds the workspace subscription's contracted study duration ({scoping.StudyDurationMonths} months).";
                }

            case RegisterParticipant registerParticipant when usageReader is not null:
                {
                    int currentParticipants = await usageReader
                        .CountParticipantsAsync(registerParticipant.TenantId, registerParticipant.ParticipantId, cancellationToken)
                        .ConfigureAwait(false);
                    if (currentParticipants >= scoping.ParticipantCount)
                    {
                        return $"The workspace subscription allows at most {scoping.ParticipantCount} participant(s), and {currentParticipants} already exist.";
                    }

                    return null;
                }

            default:
                return null;
        }
    }
}
