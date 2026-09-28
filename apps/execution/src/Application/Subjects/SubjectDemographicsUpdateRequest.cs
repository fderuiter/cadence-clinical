using System.Collections.Generic;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// <c>PUT /api/v1/execution/subjects/{id}/demographics</c> request body. No dedicated FluentValidation
/// validator per <c>02_technical_design.md</c> §2.7 — both fields are optional at this DTO layer. The
/// post-randomization lock check (SCN-004) and any demographics-shape rules are application/domain-layer
/// concerns owned by the demographics-update handler (a later ticket), not shape validation here.
/// Legacy Python source being replaced: <c>apps/execution/presentation/dtos.py</c> (Pydantic request models).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectDemographicsUpdateRequest(
    DemographicsPayload? Demographics,
    IReadOnlyDictionary<string, object?>? StratFactors);
