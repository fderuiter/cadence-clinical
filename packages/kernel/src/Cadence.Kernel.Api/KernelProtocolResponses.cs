using System;
using System.Collections.Generic;

using Cadence.Kernel.Protocol;

namespace Cadence.Kernel.Api;

/// <summary>Amendment metadata describing why and how a protocol version diverges from its parent.</summary>
/// <param name="Intent">Short statement of what the amendment sets out to change.</param>
/// <param name="Rationale">Clinical or operational justification for the amendment.</param>
/// <param name="SummaryOfChanges">Structured summary of protocol modifications.</param>
/// <param name="ImpactedElements">Identifiers or paths of impacted protocol elements (at least one required).</param>
/// <param name="Classification">Amendment classification (<c>Substantial</c>, <c>NonSubstantial</c>, or <c>UrgentSafetyMeasure</c>).</param>
/// <param name="TransitionPolicy">Participant transition policy (<c>NewParticipantsOnly</c>, <c>RequiresReconsent</c>, <c>Automatic</c>, or <c>ByCohort</c>).</param>
public sealed record KernelProtocolAmendmentInput(
    string Intent,
    string Rationale,
    string SummaryOfChanges,
    IReadOnlyList<string> ImpactedElements,
    string? Classification = null,
    string? TransitionPolicy = null)
{
    internal ProtocolAmendment ToDomain()
    {
        var classification = Enum.TryParse<AmendmentClassification>(Classification?.Trim(), ignoreCase: true, out var parsedClass) && Enum.IsDefined(parsedClass)
            ? parsedClass
            : AmendmentClassification.Substantial;
        var transition = Enum.TryParse<TransitionPolicy>(TransitionPolicy?.Trim(), ignoreCase: true, out var parsedTransition) && Enum.IsDefined(parsedTransition)
            ? parsedTransition
            : Kernel.Protocol.TransitionPolicy.Automatic;
        return new ProtocolAmendment(
            Intent ?? string.Empty,
            Rationale ?? string.Empty,
            classification,
            SummaryOfChanges ?? string.Empty,
            ImpactedElements ?? [],
            transition);
    }
}

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/protocol-versions</c>.</summary>
/// <param name="Content">Protocol design JSON payload.</param>
/// <param name="ProtocolVersionId">Optional caller-supplied protocol version identifier.</param>
public sealed record KernelCreateProtocolDraftRequest(
    string Content,
    Guid? ProtocolVersionId = null);

/// <summary>Request body for <c>PUT /api/v1/kernel/protocol-versions/{versionRef}/draft</c>.</summary>
/// <param name="Content">Replacement protocol design JSON payload.</param>
/// <param name="Amendment">Optional replacement amendment metadata when revising a forked draft.</param>
public sealed record KernelReviseProtocolDraftRequest(
    string Content,
    KernelProtocolAmendmentInput? Amendment = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/protocol-versions/{versionRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Approved</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelApproveProtocolVersionRequest(
    string? Meaning = null,
    Guid? AttestationId = null);

/// <summary>Optional request body for <c>POST /api/v1/kernel/protocol-versions/{versionRef}/withdraw</c>.</summary>
/// <param name="Meaning">Attestation meaning (<c>Withdrawn</c>).</param>
/// <param name="AttestationId">Optional caller-supplied attestation identifier.</param>
public sealed record KernelWithdrawProtocolVersionRequest(
    string? Meaning = null,
    Guid? AttestationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/protocol-versions/{versionRef}/amendments</c>.</summary>
/// <param name="Intent">Short statement of what the amendment sets out to change.</param>
/// <param name="Rationale">Clinical or operational justification for the amendment.</param>
/// <param name="SummaryOfChanges">Summary of protocol changes relative to the approved parent version.</param>
/// <param name="ImpactedElements">Identifiers or paths of impacted protocol elements (at least one required).</param>
/// <param name="Classification">Optional amendment classification (<c>Substantial</c>, <c>NonSubstantial</c>, or <c>UrgentSafetyMeasure</c>).</param>
/// <param name="TransitionPolicy">Optional participant transition policy (<c>NewParticipantsOnly</c>, <c>RequiresReconsent</c>, <c>Automatic</c>, or <c>ByCohort</c>).</param>
/// <param name="ProtocolVersionId">Optional caller-supplied protocol version identifier for the new draft.</param>
public sealed record KernelForkProtocolVersionRequest(
    string Intent,
    string Rationale,
    string SummaryOfChanges,
    IReadOnlyList<string> ImpactedElements,
    string? Classification = null,
    string? TransitionPolicy = null,
    Guid? ProtocolVersionId = null);
