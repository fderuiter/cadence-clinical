using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Rbac;

/// <summary>
/// Builds ADR-2482 <see cref="Explanation"/> payloads for standalone service HTTP 403 <c>ProblemDetails</c>
/// responses across RBAC role/permission policies, tenant/study/site scope guards, auditor read-only guards,
/// and workspace subscription posture guards while preserving K-XS-1/K-XS-2 service boundaries.
/// </summary>
public static class RbacExplanationBuilder
{
    public const string RbacProducer = "security.rbac";
    public const string SubscriptionProducer = "security.subscription";

    public const string TenantScopeCondition = "rbac.tenant_scope";
    public const string StudyScopeCondition = "rbac.study_scope";
    public const string SiteScopeCondition = "rbac.site_scope";
    public const string RolePermissionCondition = "rbac.role_permission";
    public const string RoleCondition = "rbac.role";
    public const string ChangeJustificationCondition = "gxp.change_justification";
    public const string AuditorReadOnlyCondition = "rbac.auditor_read_only";
    public const string EndpointPolicyCondition = "rbac.endpoint_policy";
    public const string SubscriptionCondition = "commercial.subscription";

    /// <summary>
    /// Builds an <see cref="Explanation"/> for a failed ASP.NET Core authorization policy evaluation, naming met
    /// and unmet conditions in plain clinical English without exposing raw <c>resource:action</c> tokens.
    /// </summary>
    public static (Explanation Explanation, string ErrorCode, string Detail) ForPolicyDenial(
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        Principal? principal = ClinicalScopeGuard.ResolvePrincipal(context);
        string callerRoleSummary = DescribeCallerRoles(principal);
        var failed = ResolveFailedRequirements(policy, authorizeResult);

        var conditions = new List<EvaluatedCondition>();
        var nextActions = new List<NextAction>();
        NodeRef? subject = ResolveRouteSubject(context);
        string? primaryClaim = null;
        string errorCode = "FORBIDDEN";

        AppendSatisfiedPolicyConditions(context, policy, failed, principal, conditions);

        foreach (IAuthorizationRequirement requirement in failed)
        {
            switch (requirement)
            {
                case PermissionRequirement permReq:
                    {
                        string actionPhrase = PermissionMatrix.DescribePermission(permReq.Permission);
                        IReadOnlyList<string> holders = PermissionMatrix.RolesHolding(permReq.Permission);
                        if (!ClinicalScopeGuard.IsVerifiedTrialTenant(principal?.TenantId)
                            && !PermissionAuthorizationHandler.IsReadPermission(permReq.Permission))
                        {
                            var nonTrialHolders = holders
                                .Where(r => !string.Equals(r, RoleNormalization.ROLE_TRIAL_COORDINATOR, StringComparison.OrdinalIgnoreCase))
                                .ToArray();
                            if (nonTrialHolders.Length > 0)
                            {
                                holders = nonTrialHolders;
                            }
                        }

                        string rolePhrase = PermissionMatrix.DescribeRoles(holders);
                        string conditionText = $"Assign a {rolePhrase} role to {actionPhrase}.";
                        conditions.Add(EvaluatedCondition.Unsatisfied(RolePermissionCondition, conditionText));
                        nextActions.Add(new NextAction(
                            $"Request a {rolePhrase} role assignment from your study or workspace administrator.",
                            Capability: permReq.Permission,
                            Target: subject));
                        primaryClaim ??= $"{callerRoleSummary} cannot {actionPhrase}.";
                        if (string.Equals(errorCode, "FORBIDDEN", StringComparison.Ordinal))
                        {
                            errorCode = "FORBIDDEN_PERMISSION";
                        }

                        break;
                    }

                case RoleRequirement roleReq:
                    {
                        string rolePhrase = PermissionMatrix.DescribeRoles(roleReq.RequiredRoles);
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            RoleCondition,
                            $"Assign a {rolePhrase} role before performing this clinical action."));
                        nextActions.Add(new NextAction(
                            $"Request a {rolePhrase} role assignment from your workspace administrator.",
                            Target: subject));
                        primaryClaim ??= $"{callerRoleSummary} does not hold the required {rolePhrase} role.";
                        errorCode = "FORBIDDEN_PERMISSION";
                        break;
                    }

                case TenantScopeRequirement:
                    {
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            TenantScopeCondition,
                            "Select an active verified clinical workspace before accessing tenant records."));
                        nextActions.Add(new NextAction(
                            "Switch to an active verified workspace or ask your workspace administrator to activate tenant access."));
                        primaryClaim ??= ClinicalScopeGuard.TenantAccessDeniedDetail;
                        errorCode = SetScopeCodeIfUnclassified(errorCode);
                        break;
                    }

                case CallerStudyScopeRequirement:
                    {
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            StudyScopeCondition,
                            "Assign your account to at least one clinical study before accessing study records."));
                        nextActions.Add(new NextAction(
                            "Request study assignment from your Sponsor Data Manager or workspace administrator."));
                        primaryClaim ??= ClinicalScopeGuard.StudyAccessDeniedDetail;
                        errorCode = SetScopeCodeIfUnclassified(errorCode);
                        break;
                    }

                case CallerStudyAndSiteScopeRequirement:
                    {
                        if (!ClinicalScopeGuard.HasAnyStudyAssignment(principal))
                        {
                            conditions.Add(EvaluatedCondition.Unsatisfied(
                                StudyScopeCondition,
                                "Assign your account to at least one clinical study before accessing study records."));
                            nextActions.Add(new NextAction(
                                "Request study assignment from your Sponsor Data Manager or workspace administrator."));
                            primaryClaim ??= ClinicalScopeGuard.StudyAccessDeniedDetail;
                        }
                        else
                        {
                            conditions.Add(EvaluatedCondition.Unsatisfied(
                                SiteScopeCondition,
                                "Assign your site-scoped account to at least one clinical site before accessing site records."));
                            nextActions.Add(new NextAction(
                                "Request clinical site assignment from your Sponsor Data Manager or study coordinator lead."));
                            primaryClaim ??= ClinicalScopeGuard.SiteAccessDeniedDetail;
                        }

                        errorCode = SetScopeCodeIfUnclassified(errorCode);
                        break;
                    }

                case StudyScopeRequirement:
                    {
                        string? studyId = StudyScopeAuthorizationHandler.RouteValue<StudyScopeRouteParameter>(context, p => p.Name);
                        NodeRef? studyTarget = TryParseNodeRef(NodeKinds.Study, studyId);
                        subject ??= studyTarget;
                        string label = string.IsNullOrWhiteSpace(studyId) ? "the requested study" : $"study {studyId}";
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            StudyScopeCondition,
                            $"Assign your account to {label} before accessing its clinical records.",
                            EvidenceFor(studyTarget)));
                        nextActions.Add(new NextAction(
                            $"Request assignment to {label} from your Sponsor Data Manager.",
                            Target: studyTarget));
                        primaryClaim ??= $"Your account is not assigned to {label}.";
                        errorCode = SetScopeCodeIfUnclassified(errorCode);
                        break;
                    }

                case SiteScopeRequirement:
                    {
                        if (!ClinicalScopeGuard.HasAnyStudyAssignment(principal))
                        {
                            conditions.Add(EvaluatedCondition.Unsatisfied(
                                StudyScopeCondition,
                                "Assign your account to at least one clinical study before accessing clinical site records."));
                            nextActions.Add(new NextAction(
                                "Request study assignment from your Sponsor Data Manager or workspace administrator."));
                            primaryClaim ??= ClinicalScopeGuard.StudyAccessDeniedDetail;
                        }
                        else
                        {
                            string? siteId = StudyScopeAuthorizationHandler.RouteValue<SiteScopeRouteParameter>(context, p => p.Name);
                            NodeRef? siteTarget = TryParseNodeRef(NodeKinds.StudySite, siteId);
                            subject ??= siteTarget;
                            string label = string.IsNullOrWhiteSpace(siteId) ? "the requested clinical site" : $"clinical site {siteId}";
                            conditions.Add(EvaluatedCondition.Unsatisfied(
                                SiteScopeCondition,
                                $"Assign your account to {label} before accessing its records.",
                                EvidenceFor(siteTarget)));
                            nextActions.Add(new NextAction(
                                $"Request assignment to {label} from your Sponsor Data Manager or Principal Investigator.",
                                Target: siteTarget));
                            primaryClaim ??= $"Your account is not assigned to {label}.";
                        }

                        errorCode = SetScopeCodeIfUnclassified(errorCode);
                        break;
                    }

                case ChangeJustificationRequirement:
                    {
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            ChangeJustificationCondition,
                            "Provide a 21 CFR Part 11 reason for change before modifying regulated clinical records."));
                        nextActions.Add(new NextAction(
                            "Enter a clinical reason for change and submit the request again.",
                            Target: subject));
                        primaryClaim ??= "Modifying this regulated record requires a 21 CFR Part 11 reason for change.";
                        break;
                    }

                case AuditorReadOnlyRequirement:
                    {
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            AuditorReadOnlyCondition,
                            "Regulatory Auditor accounts are restricted to read-only inspection access."));
                        nextActions.Add(new NextAction(
                            "Sign in with an operational study role to create or modify clinical records."));
                        primaryClaim ??= "Regulatory Auditor accounts are restricted to read-only inspection access.";
                        break;
                    }

                case UndeclaredEndpointRequirement:
                    {
                        conditions.Add(EvaluatedCondition.Unsatisfied(
                            EndpointPolicyCondition,
                            "This route has no declared clinical authorization policy and is denied by default."));
                        nextActions.Add(new NextAction(
                            "Contact platform support if this clinical operation should be enabled."));
                        primaryClaim ??= "This endpoint is denied because it declares no clinical authorization policy.";
                        break;
                    }
            }
        }

        if (conditions.All(c => c.Met))
        {
            conditions.Add(EvaluatedCondition.Unsatisfied(
                RolePermissionCondition,
                "Verify your clinical role and study or site assignments for this operation."));
            nextActions.Add(new NextAction(
                "Request access to this clinical operation from your study or workspace administrator.",
                Target: subject));
        }

        string claim = primaryClaim ?? $"{callerRoleSummary} is not authorized to perform this clinical action.";
        var stepEvidence = conditions.SelectMany(c => c.Evidence).Distinct().ToArray();
        var explanation = new Explanation(
            Claim: claim,
            Producer: RbacProducer,
            Subject: subject,
            Steps: [new ExplanationStep(claim, stepEvidence)],
            Conditions: conditions,
            NextActions: nextActions);

        return (explanation, errorCode, claim);
    }

    private static string SetScopeCodeIfUnclassified(string currentCode) =>
        string.Equals(currentCode, "FORBIDDEN", StringComparison.Ordinal) ? "FORBIDDEN_SCOPE" : currentCode;

    /// <summary>
    /// Builds an <see cref="Explanation"/> for handler-level tenant, study, or site scope denials in
    /// <see cref="ClinicalScopeGuard"/> and <c>CtmsScopeGuard</c>.
    /// </summary>
    public static Explanation ForScopeDenial(
        string detail,
        string conditionCode,
        string? targetKind = null,
        string? targetIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionCode);

        NodeRef? target = targetKind is not null ? TryParseNodeRef(targetKind, targetIdentifier) : null;
        string trimmedTarget = targetIdentifier?.Trim() ?? string.Empty;

        (string conditionText, string nextActionText) = conditionCode switch
        {
            TenantScopeCondition => (
                "Select an active verified clinical workspace before accessing tenant records.",
                "Switch to an active verified workspace or contact your workspace administrator."),
            SiteScopeCondition => (
                trimmedTarget.Length > 0
                    ? $"Assign your account to clinical site {trimmedTarget} before accessing its records."
                    : "Assign your site-scoped account to at least one clinical site before accessing site records.",
                trimmedTarget.Length > 0
                    ? $"Request assignment to clinical site {trimmedTarget} from your Sponsor Data Manager or Principal Investigator."
                    : "Request clinical site assignment from your Sponsor Data Manager or Principal Investigator."),
            _ => (
                trimmedTarget.Length > 0
                    ? $"Assign your account to study {trimmedTarget} before accessing its records."
                    : "Assign your account to the governing clinical study before accessing study records.",
                trimmedTarget.Length > 0
                    ? $"Request assignment to study {trimmedTarget} from your Sponsor Data Manager."
                    : "Request study assignment from your Sponsor Data Manager or workspace administrator."),
        };

        var evidence = EvidenceFor(target);
        return new Explanation(
            Claim: detail,
            Producer: RbacProducer,
            Subject: target,
            Steps: [new ExplanationStep(detail, evidence)],
            Conditions: [EvaluatedCondition.Unsatisfied(conditionCode, conditionText, evidence)],
            NextActions: [new NextAction(nextActionText, Target: target)]);
    }

    /// <summary>Builds an <see cref="Explanation"/> for auditor read-only write guard rejections.</summary>
    public static Explanation ForAuditorReadOnly(string detail) =>
        new(
            Subject: null,
            Claim: detail,
            Producer: RbacProducer,
            Steps: [new ExplanationStep(detail, [])],
            Conditions:
            [
                EvaluatedCondition.Unsatisfied(
                    AuditorReadOnlyCondition,
                    "Regulatory Auditor accounts are restricted to read-only inspection access and cannot modify clinical records."),
            ],
            NextActions:
            [
                new NextAction("Sign in with an operational study role to create or update clinical records."),
            ]);

    /// <summary>Builds an <see cref="Explanation"/> for workspace subscription or entitlement 403 guard rejections.</summary>
    public static Explanation ForSubscriptionDenial(
        string claim,
        string conditionDescription,
        string nextActionDescription,
        string? capability = null) =>
        new(
            Subject: null,
            Claim: claim,
            Producer: SubscriptionProducer,
            Steps: [new ExplanationStep(claim, [])],
            Conditions: [EvaluatedCondition.Unsatisfied(SubscriptionCondition, conditionDescription)],
            NextActions: [new NextAction(nextActionDescription, Capability: capability)]);

    private static IReadOnlyList<IAuthorizationRequirement> ResolveFailedRequirements(
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.AuthorizationFailure?.FailedRequirements.ToArray() is { Length: > 0 } explicitFailed)
        {
            return explicitFailed;
        }

        return [.. policy.Requirements];
    }

    private static void AppendSatisfiedPolicyConditions(
        HttpContext context,
        AuthorizationPolicy policy,
        IReadOnlyList<IAuthorizationRequirement> failed,
        Principal? principal,
        List<EvaluatedCondition> conditions)
    {
        if (policy.Requirements.OfType<TenantScopeRequirement>().Any()
            && !failed.OfType<TenantScopeRequirement>().Any()
            && ClinicalScopeGuard.HasTenant(principal))
        {
            conditions.Add(EvaluatedCondition.Satisfied(
                TenantScopeCondition,
                "Signed in to an active verified clinical workspace."));
        }

        if (policy.Requirements.OfType<StudyScopeRequirement>().Any()
            && !failed.OfType<StudyScopeRequirement>().Any())
        {
            string? studyId = StudyScopeAuthorizationHandler.RouteValue<StudyScopeRouteParameter>(context, p => p.Name);
            NodeRef? studyRef = TryParseNodeRef(NodeKinds.Study, studyId);
            string studyLabel = string.IsNullOrWhiteSpace(studyId) ? "the study" : $"study {studyId}";
            conditions.Add(EvaluatedCondition.Satisfied(
                StudyScopeCondition,
                $"Assigned to {studyLabel}.",
                EvidenceFor(studyRef)));
        }
        else if (policy.Requirements.OfType<CallerStudyScopeRequirement>().Any()
            && !failed.OfType<CallerStudyScopeRequirement>().Any()
            && ClinicalScopeGuard.HasAnyStudyAssignment(principal))
        {
            conditions.Add(EvaluatedCondition.Satisfied(
                StudyScopeCondition,
                "Assigned to at least one clinical study."));
        }
    }

    private static string DescribeCallerRoles(Principal? principal)
    {
        if (principal is null || (principal.Roles.Count == 0 && principal.RawRoles.Count == 0))
        {
            return "Your account (no assigned clinical role)";
        }

        var labels = principal.Roles
            .Concat(principal.RawRoles)
            .Select(PermissionMatrix.DescribeRole)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return labels.Count == 1
            ? $"Your {labels[0]} role"
            : $"Your current roles ({string.Join(", ", labels)})";
    }

    private static NodeRef? ResolveRouteSubject(HttpContext context)
    {
        string? siteId = StudyScopeAuthorizationHandler.RouteValue<SiteScopeRouteParameter>(context, p => p.Name);
        if (TryParseNodeRef(NodeKinds.StudySite, siteId) is { } siteRef)
        {
            return siteRef;
        }

        string? studyId = StudyScopeAuthorizationHandler.RouteValue<StudyScopeRouteParameter>(context, p => p.Name);
        return TryParseNodeRef(NodeKinds.Study, studyId);
    }

    private static NodeRef? TryParseNodeRef(string kind, string? rawId) =>
        !string.IsNullOrWhiteSpace(rawId) && Guid.TryParse(rawId.Trim(), out Guid parsed) && parsed != Guid.Empty
            ? new NodeRef(kind, parsed)
            : null;

    private static NodeRef[] EvidenceFor(NodeRef? node) =>
        node is null ? [] : [node];
}
