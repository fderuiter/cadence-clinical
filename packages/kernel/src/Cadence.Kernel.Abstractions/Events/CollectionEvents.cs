using System;
using System.Collections.Generic;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A form instance was opened under its governing protocol version (K-FACT-6, K-TIME-8; Evidence family).</summary>
[KernelEvent("CollectionInstanceOpened", 1)]
public sealed record CollectionInstanceOpened(
    TenantId TenantId,
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    ParticipantId ParticipantId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string? PlannedActivityId,
    DateTimeOffset CollectedAt);

/// <summary>
/// A form page was saved (ADR-2622, Evidence family): the values it recorded or corrected, in one commit. Edit checks run
/// from this event against the form's CollectionSpec version; each value also raises its own
/// <see cref="ObservationRecorded"/> or <see cref="ObservationCorrected"/>.
/// </summary>
/// <remarks>
/// <para><c>Version</c>: The form instance's version after the save.</para>
/// </remarks>
[KernelEvent("CollectedValuesSaved", 1)]
public sealed record CollectedValuesSaved(
    TenantId TenantId,
    CollectionInstanceId CollectionInstanceId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    long Version,
    IReadOnlyList<CollectedValueChange> Changes);

/// <summary>One value a form save recorded (<c>Supersedes</c> null) or corrected.</summary>
public sealed record CollectedValueChange(string ItemId, ObservationId ObservationId, int Revision, int? Supersedes);
