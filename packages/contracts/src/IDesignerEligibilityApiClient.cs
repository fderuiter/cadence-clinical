using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;

namespace Cadence.Contracts;

/// <summary>
/// Structured field reference inside a Designer eligibility expression tree
/// (e.g. "eCRF.DM.AGE" resolving to Domain="DM", Variable="AGE"). Wire field
/// names are snake_case (raw_reference, domain, variable).
/// </summary>
public record FieldReferenceDto(string RawReference, string Domain = "", string Variable = "");

/// <summary>
/// AST node of a Designer eligibility expression tree. Type is one of
/// "logical", "comparison", "field_ref", "constant" at the wire level;
/// Operator is "and"/"or"/"not" for logical nodes or a comparison operator
/// ("==", "!=", "&lt;", "&lt;=", "&gt;", "&gt;=") for comparison nodes.
/// </summary>
public record DesignerExpressionNodeDto(
    string Type,
    string? Operator = null,
    IReadOnlyList<DesignerExpressionNodeDto>? Operands = null,
    object? Value = null,
    FieldReferenceDto? FieldRef = null);

/// <summary>
/// Execution's frozen view of a single Designer-authored eligibility criterion.
/// Mirrors the existing Python anti-corruption-layer DTO at
/// apps/execution/domain/acl/designer_eligibility_dto.py
/// (DesignerEligibilityCriterionDTO) so both sides of the Execution&lt;-&gt;Designer
/// same-tier cycle (MIG-03, see docs/migration/phase0/03_migration_plan.md)
/// can build against an identical shape. CriterionType is "inclusion" or
/// "exclusion" at the wire level.
/// </summary>
public record DesignerEligibilityCriterionDto(
    string Id,
    string CriterionType,
    string Identifier,
    string HumanReadableText,
    string DslExpressionString,
    DesignerExpressionNodeDto? StructuredExpressionTree,
    bool ExpectedOutcome,
    string CreatedBy,
    string ReasonForChange,
    int VersionIndex = 1,
    DateTimeOffset? CreatedAt = null);

/// <summary>
/// Frozen HTTP contract for the Execution -&gt; Designer edge of the MIG-03
/// same-tier cycle: GET /api/v1/studies/{studyId}/eligibility-criteria on the
/// Designer service. It was defined for the Python Execution service's
/// eligibility checks; no C# service implements or consumes it yet. This is a
/// contract/DTO surface only: request signing, retry and serving are not built.
/// </summary>
public interface IDesignerEligibilityApiClient
{
    /// <summary>Reads the eligibility criteria of a study's current design.</summary>
    /// <param name="studyId">The study's identifier in the Designer service.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The criteria, or a failure result.</returns>
    Task<Result<IReadOnlyList<DesignerEligibilityCriterionDto>>> GetEligibilityCriteriaAsync(
        string studyId, CancellationToken ct = default);
}
