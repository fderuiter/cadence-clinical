using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Explanations;

namespace Cadence.Kernel.Host;

/// <summary>
/// Fails closed until the authority slice (#4781) evaluates grants, delegations and qualifications: every decision,
/// real or dry run, is a denial that names the missing evaluator as its unmet condition (ADR-2540).
/// </summary>
internal sealed class UnavailableAuthorizationEvaluator : IAuthorizationEvaluator
{
    /// <summary>Condition code of the one condition this evaluator reports.</summary>
    public const string ConditionCode = "authority.evaluator";

    private static readonly AuthorizationDecision Denied = AuthorizationDecision.Deny(
        "Permissions can't be evaluated yet, so no kernel action is permitted.",
        EvaluatedCondition.Unsatisfied(ConditionCode, "Permissions are evaluated from grants, delegations and qualifications"));

    public ValueTask<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Denied);
}
