using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel;

namespace Cadence.Modules.Execution.Domain;

/// <summary>
/// An eCRF form instance as Execution keeps it (ADR-2622 §4.1): one per kernel <c>CollectionInstance</c>, with the same
/// id. It holds the form's workflow and layout, never a value: the values, their revisions, locks, verifications and
/// signatures are kernel facts read from the <c>CollectionInstance</c>, so the form screen is a view over them.
/// </summary>
public sealed class FormInstance : AggregateRoot<Guid>
{
    /// <summary>The longest CollectionSpec, planned activity or item id the kernel accepts.</summary>
    public const int ElementIdMaxLength = 128;

    private readonly string[] _itemOrder = [];

    private FormInstance()
    {
    }

    private FormInstance(
        CollectionInstanceId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        IReadOnlyList<string> itemOrder)
        : base(id.Value)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        ParticipantId = participantId;
        StudyId = studyId;
        ProtocolVersionId = protocolVersionId;
        CollectionSpecId = collectionSpecId;
        PlannedActivityId = plannedActivityId;
        CollectedAt = collectedAt;
        _itemOrder = [.. itemOrder];
    }

    /// <summary>The kernel form instance this form is the Execution side of; the same value as <see cref="AggregateRoot{TId}.Id"/>.</summary>
    public CollectionInstanceId CollectionInstanceId => CollectionInstanceId.FromGuid(Id);

    /// <summary>The tenant the form belongs to.</summary>
    public TenantId TenantId { get; private set; }

    /// <summary>The enrollment of the participant the form is for.</summary>
    public EnrollmentId EnrollmentId { get; private set; }

    /// <summary>The participant the form is for.</summary>
    public ParticipantId ParticipantId { get; private set; }

    /// <summary>The study the form belongs to.</summary>
    public StudyId StudyId { get; private set; }

    /// <summary>The protocol version that governs the form (K-TIME-8), as the kernel resolved it.</summary>
    public ProtocolVersionId ProtocolVersionId { get; private set; }

    /// <summary>The CollectionSpec the form collects.</summary>
    public string CollectionSpecId { get; private set; } = string.Empty;

    /// <summary>The planned activity (visit) the form belongs to; null for an unscheduled or log form.</summary>
    public string? PlannedActivityId { get; private set; }

    /// <summary>When the form's values were collected in the trial world.</summary>
    public DateTimeOffset CollectedAt { get; private set; }

    /// <summary>
    /// The form's layout: the CollectionSpec's item ids in the order the screen shows them, fixed when the form opens so
    /// a later reading of the specification never reorders a form someone is entering.
    /// </summary>
    public IReadOnlyList<string> ItemOrder => _itemOrder;

    /// <summary>
    /// Opens the Execution side of a kernel form instance. The layout is the specification's item order; an id the
    /// kernel would not accept, or one listed twice, means the specification is not a form layout.
    /// </summary>
    /// <exception cref="ArgumentException">An argument is empty, too long or repeated.</exception>
    public static FormInstance Open(
        CollectionInstanceId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        ProtocolVersionId protocolVersionId,
        string collectionSpecId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        IReadOnlyList<string> itemOrder)
    {
        ArgumentNullException.ThrowIfNull(itemOrder);
        if (id.IsEmpty || tenantId.IsEmpty || enrollmentId.IsEmpty || participantId.IsEmpty || studyId.IsEmpty || protocolVersionId.IsEmpty)
        {
            throw new ArgumentException("A form instance names its kernel form, tenant, enrollment, participant, study and protocol version.");
        }

        if (!IsElementId(collectionSpecId) || (plannedActivityId is not null && !IsElementId(plannedActivityId)))
        {
            throw new ArgumentException("The CollectionSpec and planned activity ids are non-empty and at most 128 characters.");
        }

        if (!itemOrder.All(IsElementId) || itemOrder.Distinct(StringComparer.Ordinal).Count() != itemOrder.Count)
        {
            throw new ArgumentException("The layout lists each item id once, each non-empty and at most 128 characters.", nameof(itemOrder));
        }

        return new FormInstance(
            id, tenantId, enrollmentId, participantId, studyId, protocolVersionId, collectionSpecId, plannedActivityId, collectedAt, itemOrder);
    }

    private static bool IsElementId(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= ElementIdMaxLength;
}
