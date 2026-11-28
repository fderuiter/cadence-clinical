using System;
using System.Collections.Generic;

using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Persistence.Evidence;

internal sealed record MemberObservationRow(string ItemId, ObservationId ObservationId);

internal sealed record ObservationSummaryRow(ObservationId Id, ObservationValue Value, int Revision);

internal sealed record ObservationProjectionRow(Guid Id, int Revision, bool IsBlinded);

internal sealed record QueryTargetObservationRow(ObservationId Id, ConceptId ConceptId, ObservationValue Value, int Revision);

internal sealed record QueryTargetFormRow(
    CollectionInstanceId Id,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    IReadOnlyList<MemberObservationRow> Members);

internal sealed record QueryTargetRow(
    CollectionInstanceId Id,
    StudyId StudyId,
    StudySiteId StudySiteId,
    EnrollmentId EnrollmentId,
    string CollectionSpecId,
    string? PlannedActivityId,
    long VersionIndex,
    long EnrollmentVersion,
    string? ItemId,
    ObservationId? ObservationId,
    int? Revision,
    ObservationValue? Value);
