using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Evidence;

internal static class LockReadinessExplanationBuilder
{
    internal static Explanation BuildExplanation(
        NodeRef scope,
        int openQueryCount,
        int missingRequiredCount,
        int openErrorFindingCount,
        IReadOnlyList<LockReadinessBlocker> blockers,
        int unverifiedValueCount = 0,
        int unsignedFormCount = 0,
        int unopenedVisitCount = 0)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(blockers);

        var queryEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.OpenQuery)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var missingEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.MissingRequiredItem)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unverifiedEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnverifiedValue)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unsignedFormEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnsignedForm)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var unopenedVisitEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.UnopenedVisit)
            .Select(b => b.Target)
            .Distinct()
            .ToArray();
        var errorFindingEvidence = blockers
            .Where(b => b.Kind == LockBlockerKind.ErrorFinding)
            .Select(b => b.FindingId is { } fid ? NodeRef.For(fid) : b.Target)
            .Distinct()
            .ToArray();

        var queryNoun = openQueryCount == 1 ? "query" : "queries";
        var itemNoun = missingRequiredCount == 1 ? "item" : "items";
        var unverifiedNoun = unverifiedValueCount == 1 ? "revision" : "revisions";
        var formNoun = unsignedFormCount == 1 ? "form" : "forms";
        var visitNoun = unopenedVisitCount == 1 ? "visit form" : "visit forms";
        var findingNoun = openErrorFindingCount == 1 ? "finding" : "findings";

        var queriesCondition = openQueryCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.QueriesClosedCondition, "All queries in the lock scope are closed.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.QueriesClosedCondition,
                $"Close {openQueryCount} open {queryNoun} in {scope} before applying a Locked assertion.",
                queryEvidence);

        var itemsCondition = missingRequiredCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.RequiredItemsCompleteCondition, "All required items on opened forms in the lock scope are recorded.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.RequiredItemsCompleteCondition,
                $"Complete {missingRequiredCount} missing required {itemNoun} in {scope} before applying a Locked assertion.",
                missingEvidence);

        var verifiedCondition = unverifiedValueCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.ObservationsVerifiedCondition, "All recorded observation revisions in the lock scope are source-verified.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.ObservationsVerifiedCondition,
                $"Verify {unverifiedValueCount} unverified observation {unverifiedNoun} in {scope} before applying a Locked assertion.",
                unverifiedEvidence);

        var formsSignedCondition = unsignedFormCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.FormsSignedCondition, "All populated forms in the lock scope hold a current investigator signature.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.FormsSignedCondition,
                $"Sign {unsignedFormCount} unsigned or superseded {formNoun} in {scope} before applying a Locked assertion.",
                unsignedFormEvidence);

        var visitsOpenedCondition = unopenedVisitCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.ScheduledVisitsOpenedCondition, "All due or closed scheduled visit forms on active enrollments in the lock scope are opened.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.ScheduledVisitsOpenedCondition,
                $"Open {unopenedVisitCount} scheduled {visitNoun} in {scope} before applying a Locked assertion.",
                unopenedVisitEvidence);

        var findingsCondition = openErrorFindingCount == 0
            ? EvaluatedCondition.Satisfied(LockReadinessEvaluator.ErrorFindingsResolvedCondition, "All Error validation findings in the lock scope are resolved, accepted, or explained.")
            : EvaluatedCondition.Unsatisfied(
                LockReadinessEvaluator.ErrorFindingsResolvedCondition,
                $"Resolve, accept, or explain {openErrorFindingCount} Error validation {findingNoun} in {scope} before applying a Locked assertion.",
                errorFindingEvidence);

        var conditions = new List<EvaluatedCondition>
        {
            queriesCondition,
            itemsCondition,
            verifiedCondition,
            formsSignedCondition,
            visitsOpenedCondition,
            findingsCondition,
        };

        var steps = new List<ExplanationStep>();
        var nextActions = new List<NextAction>();

        foreach (var blocker in blockers)
        {
            NodeRef[] evidence;
            if (blocker.QueryAssertionId is { } qid)
            {
                evidence = [NodeRef.For(qid), blocker.Target];
            }
            else if (blocker.FindingId is { } fid)
            {
                evidence = [NodeRef.For(fid), blocker.Target];
            }
            else
            {
                evidence = [blocker.Target];
            }

            var conditionCode = blocker.Kind switch
            {
                LockBlockerKind.OpenQuery => LockReadinessEvaluator.OpenQueryBlockerCondition,
                LockBlockerKind.UnverifiedValue => LockReadinessEvaluator.UnverifiedValueBlockerCondition,
                LockBlockerKind.UnsignedForm => LockReadinessEvaluator.UnsignedFormBlockerCondition,
                LockBlockerKind.UnopenedVisit => LockReadinessEvaluator.UnopenedVisitBlockerCondition,
                LockBlockerKind.ErrorFinding => LockReadinessEvaluator.ErrorFindingBlockerCondition,
                _ => LockReadinessEvaluator.MissingRequiredItemBlockerCondition,
            };
            var queryLabel = blocker.QueryAssertionId is { } q ? NodeRef.For(q).ToString() : "query";
            var findingLabel = blocker.FindingId is { } f ? NodeRef.For(f).ToString() : "finding";
            var claim = blocker.Kind switch
            {
                LockBlockerKind.OpenQuery =>
                    $"{blocker.Owner} owns open query {queryLabel} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.UnverifiedValue =>
                    $"{blocker.Owner} owns unverified revision {blocker.CurrentRevision ?? 1} on {blocker.Target} ({blocker.ItemLabel ?? "observation"}).",
                LockBlockerKind.UnsignedForm =>
                    $"{blocker.Owner} owns {(blocker.SignatureSuperseded ? "superseded signature" : "unsigned form")} on {blocker.Target} ({blocker.CollectionTitle ?? blocker.CollectionSpecId}).",
                LockBlockerKind.UnopenedVisit =>
                    $"{blocker.Owner} owns unopened scheduled visit '{blocker.PlannedActivityName ?? blocker.PlannedActivityId}' ({blocker.CollectionTitle ?? blocker.CollectionSpecId}) on {blocker.Target}.",
                LockBlockerKind.ErrorFinding =>
                    $"{blocker.Owner} owns Error validation finding {findingLabel} ({blocker.RuleId ?? "rule"}) on {blocker.Target}.",
                _ =>
                    $"{blocker.Owner} owns missing required item '{blocker.ItemLabel ?? blocker.ItemId}' on {blocker.Target} ({blocker.CollectionTitle ?? blocker.CollectionSpecId}).",
            };

            conditions.Add(EvaluatedCondition.Unsatisfied(conditionCode, blocker.NextAction, evidence));
            steps.Add(new ExplanationStep(claim, evidence));
            nextActions.Add(new NextAction(blocker.NextAction, blocker.NextCapability, blocker.NextTarget));
        }

        if (steps.Count == 0)
        {
            steps.Add(new ExplanationStep(
                $"Checked queries, required form items, source verification, form signatures, scheduled visits, and validation findings under {scope}; no blockers remain.",
                [scope]));
        }

        string headline;
        if (blockers.Count == 0)
        {
            headline = $"Scope {scope} is ready for a Locked assertion: 0 open queries, 0 missing required items, 0 unverified revisions, 0 unsigned forms, 0 unopened visits, and 0 open Error validation findings.";
        }
        else
        {
            var summaryParts = new List<string>();
            if (openQueryCount > 0)
            {
                summaryParts.Add($"{openQueryCount} open {queryNoun}");
            }

            if (missingRequiredCount > 0)
            {
                summaryParts.Add($"{missingRequiredCount} missing required {itemNoun}");
            }

            if (unverifiedValueCount > 0)
            {
                summaryParts.Add($"{unverifiedValueCount} unverified {unverifiedNoun}");
            }

            if (unsignedFormCount > 0)
            {
                summaryParts.Add($"{unsignedFormCount} unsigned {formNoun}");
            }

            if (unopenedVisitCount > 0)
            {
                summaryParts.Add($"{unopenedVisitCount} unopened {visitNoun}");
            }

            if (openErrorFindingCount > 0)
            {
                summaryParts.Add($"{openErrorFindingCount} open Error validation {findingNoun}");
            }

            headline = $"Locking {scope} is blocked by {FormatList(summaryParts)}; Frozen remains available while blockers are resolved.";
        }

        return new Explanation(scope, headline, LockReadinessEvaluator.Producer, steps, conditions, nextActions);
    }

    private static string FormatList(IReadOnlyList<string> items) =>
        items.Count switch
        {
            0 => "unresolved readiness blockers",
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };
}
