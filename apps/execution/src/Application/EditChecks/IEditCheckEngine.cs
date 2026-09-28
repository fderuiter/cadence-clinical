using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Observations;

namespace Cadence.Apps.Execution.Application.EditChecks;

public sealed record EditCheckViolation(
    string RuleId,
    string Message,
    string Severity,
    string? TargetField = null,
    string? FormId = null,
    string? VisitId = null
);

/// <summary>
/// Service abstraction for evaluating synchronous inline constraints and asynchronous longitudinal AST rules.
/// @req:PRD-SYS-004
/// @req:SRS-EDC-002
/// @req:SRS-EDC-005
/// </summary>
public interface IEditCheckEngine
{
    object? EvaluateAst(object? astNode, IReadOnlyDictionary<string, object?> context);

    IReadOnlyList<EditCheckViolation> EvaluateInlineConstraints(ClinicalObservation observation);

    IReadOnlyList<EditCheckViolation> EvaluateBatchRules(BatchEvaluationContext context);
}
