using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;

namespace Cadence.Apps.Etmf.Domain;

public static class TmfDocumentLifecycleState
{
    public const string Draft = "DRAFT";
    public const string TechnicalQc = "TECHNICAL_QC";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Filed = "FILED";
    public const string Archived = "ARCHIVED";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Draft,
        TechnicalQc,
        Approved,
        Rejected,
        Filed,
        Archived
    };
}

public sealed class InvalidStateTransitionException : InvalidOperationException
{
    public InvalidStateTransitionException(string message) : base(message) { }
    public InvalidStateTransitionException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class TmfDocument : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? SiteId { get; set; }
    public int Zone { get; set; }
    public string Section { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Status { get; private set; }
    public string TaxonomyVersion { get; set; } = string.Empty;
    public string ArtifactCode { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public DateTimeOffset? IssueDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? DocumentOwnerId { get; set; }
    public string? ProtocolVersionTag { get; set; }
    public int? ProtocolVersionIndex { get; set; }
    public string? ProtocolVersionStatus { get; set; }
    public string? DocumentType { get; set; }
    public string ApprovalStatus { get; private set; }
    public string? SignatureManifestation { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }
    public bool IsRedacted { get; set; }
    public string? RedactionSourceId { get; set; }
    public string? RedactionManifestJson { get; set; }
    public string? CorrelationKey { get; set; }
    public string? ContentChecksum { get; set; }
    public string? SourceSystem { get; set; }
    public string? SyncStatus { get; set; }
    public string? ObjectKey { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int TransitionSequence { get; private set; }

    private readonly List<TmfDocumentQcTransition> _transitions = new();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IReadOnlyCollection<TmfDocumentQcTransition> Transitions => _transitions.AsReadOnly();

    public TmfDocument() : base(Guid.NewGuid().ToString())
    {
        Status = TmfDocumentLifecycleState.Draft;
        ApprovalStatus = TmfDocumentLifecycleState.Draft;
    }

    public TmfDocument(string id) : base(id)
    {
        Status = TmfDocumentLifecycleState.Draft;
        ApprovalStatus = TmfDocumentLifecycleState.Draft;
    }

    public static bool IsValidTransition(string fromStatus, string toStatus)
    {
        if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fromStatus.ToUpperInvariant() switch
        {
            TmfDocumentLifecycleState.Draft => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.TechnicalQc => true,
                _ => false
            },
            TmfDocumentLifecycleState.TechnicalQc => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.Approved or TmfDocumentLifecycleState.Rejected => true,
                _ => false
            },
            TmfDocumentLifecycleState.Approved => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.Filed or TmfDocumentLifecycleState.Archived => true,
                _ => false
            },
            TmfDocumentLifecycleState.Filed => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.Archived => true,
                _ => false
            },
            TmfDocumentLifecycleState.Rejected => false,
            TmfDocumentLifecycleState.Archived => false,
            _ => false
        };
    }

    public static IReadOnlyList<string> GetAllowedTransitions(string currentStatus)
    {
        return currentStatus.ToUpperInvariant() switch
        {
            TmfDocumentLifecycleState.Draft => [TmfDocumentLifecycleState.TechnicalQc],
            TmfDocumentLifecycleState.TechnicalQc => [TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.Rejected],
            TmfDocumentLifecycleState.Approved => [TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.Archived],
            TmfDocumentLifecycleState.Filed => [TmfDocumentLifecycleState.Archived],
            TmfDocumentLifecycleState.Rejected => [],
            TmfDocumentLifecycleState.Archived => [],
            _ => []
        };
    }

    public static bool CanReactivate(string fromStatus, string toStatus)
    {
        return fromStatus.ToUpperInvariant() switch
        {
            TmfDocumentLifecycleState.Rejected => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.Draft or TmfDocumentLifecycleState.TechnicalQc => true,
                _ => false
            },
            TmfDocumentLifecycleState.Archived => toStatus.ToUpperInvariant() switch
            {
                TmfDocumentLifecycleState.Filed or TmfDocumentLifecycleState.Approved or TmfDocumentLifecycleState.Draft => true,
                _ => false
            },
            _ => false
        };
    }

    public bool CanTransitionTo(string targetStatus)
    {
        return IsValidTransition(Status, targetStatus);
    }

    public TmfDocumentQcTransition TransitionTo(
        string targetStatus,
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor ID is required for document state transitions.", nameof(actorId));
        }
        if (string.IsNullOrWhiteSpace(actorRole))
        {
            throw new ArgumentException("Actor role is required for document state transitions.", nameof(actorRole));
        }
        if (string.IsNullOrWhiteSpace(reasonForChange) || reasonForChange.Trim().Length < 10)
        {
            throw new ArgumentException("21 CFR Part 11 requires a reason_for_change of at least 10 characters.", nameof(reasonForChange));
        }

        var upperTarget = targetStatus.Trim().ToUpperInvariant();
        var upperCurrent = (string.IsNullOrWhiteSpace(Status) ? TmfDocumentLifecycleState.Draft : Status).ToUpperInvariant();

        if (upperCurrent == TmfDocumentLifecycleState.Rejected || upperCurrent == TmfDocumentLifecycleState.Archived)
        {
            throw new InvalidStateTransitionException(
                $"Document '{Id}' is in terminal state '{upperCurrent}'. Archived or rejected documents cannot transition without an explicit reactivation workflow.");
        }

        if (!IsValidTransition(upperCurrent, upperTarget))
        {
            var allowed = GetAllowedTransitions(upperCurrent);
            var allowedStr = allowed.Count > 0 ? string.Join(", ", allowed) : "None";
            throw new InvalidStateTransitionException(
                $"Invalid document state transition from '{upperCurrent}' to '{upperTarget}'. Allowed transitions from '{upperCurrent}': {allowedStr}.");
        }

        int nextSeq = sequenceNumber > 0 ? sequenceNumber : (TransitionSequence + 1);
        if (nextSeq <= TransitionSequence)
        {
            nextSeq = TransitionSequence + 1;
        }

        var oldStatus = upperCurrent;
        Status = upperTarget;
        ApprovalStatus = MapApprovalStatus(upperTarget);
        ReasonForChange = reasonForChange;
        TransitionSequence = nextSeq;

        var transition = new TmfDocumentQcTransition
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Id,
            TransitionSequence = nextSeq,
            FromStatus = oldStatus,
            ToStatus = Status,
            ActorId = actorId,
            ActorRole = actorRole,
            ReasonForChange = reasonForChange,
            Timestamp = timestamp
        };

        _transitions.Add(transition);
        return transition;
    }

    public TmfDocumentQcTransition StageForQc(
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        return TransitionTo(TmfDocumentLifecycleState.TechnicalQc, actorId, actorRole, reasonForChange, timestamp, sequenceNumber);
    }

    public TmfDocumentQcTransition Approve(
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        return TransitionTo(TmfDocumentLifecycleState.Approved, actorId, actorRole, reasonForChange, timestamp, sequenceNumber);
    }

    public TmfDocumentQcTransition Reject(
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        return TransitionTo(TmfDocumentLifecycleState.Rejected, actorId, actorRole, reasonForChange, timestamp, sequenceNumber);
    }

    public TmfDocumentQcTransition File(
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        return TransitionTo(TmfDocumentLifecycleState.Filed, actorId, actorRole, reasonForChange, timestamp, sequenceNumber);
    }

    public TmfDocumentQcTransition Archive(
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        return TransitionTo(TmfDocumentLifecycleState.Archived, actorId, actorRole, reasonForChange, timestamp, sequenceNumber);
    }

    public TmfDocumentQcTransition Reactivate(
        string targetStatus,
        string actorId,
        string actorRole,
        string reasonForChange,
        DateTimeOffset timestamp,
        int sequenceNumber = 0)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor ID is required for document reactivation.", nameof(actorId));
        }
        if (string.IsNullOrWhiteSpace(actorRole))
        {
            throw new ArgumentException("Actor role is required for document reactivation.", nameof(actorRole));
        }
        if (string.IsNullOrWhiteSpace(reasonForChange) || reasonForChange.Trim().Length < 10)
        {
            throw new ArgumentException("21 CFR Part 11 requires a reason_for_change of at least 10 characters.", nameof(reasonForChange));
        }

        var upperTarget = targetStatus.Trim().ToUpperInvariant();
        var upperCurrent = (string.IsNullOrWhiteSpace(Status) ? TmfDocumentLifecycleState.Draft : Status).ToUpperInvariant();

        if (upperCurrent != TmfDocumentLifecycleState.Rejected && upperCurrent != TmfDocumentLifecycleState.Archived)
        {
            throw new InvalidStateTransitionException(
                $"Cannot reactivate document in state '{upperCurrent}'. Only documents in 'REJECTED' or 'ARCHIVED' states can be reactivated.");
        }

        if (!CanReactivate(upperCurrent, upperTarget))
        {
            throw new InvalidStateTransitionException(
                $"Invalid reactivation target '{upperTarget}' from state '{upperCurrent}'.");
        }

        int nextSeq = sequenceNumber > 0 ? sequenceNumber : (TransitionSequence + 1);
        if (nextSeq <= TransitionSequence)
        {
            nextSeq = TransitionSequence + 1;
        }

        var oldStatus = upperCurrent;
        Status = upperTarget;
        ApprovalStatus = MapApprovalStatus(upperTarget);
        ReasonForChange = reasonForChange;
        TransitionSequence = nextSeq;

        var transition = new TmfDocumentQcTransition
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = Id,
            TransitionSequence = nextSeq,
            FromStatus = oldStatus,
            ToStatus = Status,
            ActorId = actorId,
            ActorRole = actorRole,
            ReasonForChange = reasonForChange,
            Timestamp = timestamp
        };

        _transitions.Add(transition);
        return transition;
    }

    public void OverrideTaxonomy(int zone, string section, string artifactType, string artifactCode)
    {
        Zone = zone;
        Section = section;
        ArtifactType = artifactType;
        ArtifactCode = artifactCode;
    }

    private static string MapApprovalStatus(string status) => status.ToUpperInvariant() switch
    {
        TmfDocumentLifecycleState.Draft => TmfDocumentLifecycleState.Draft,
        TmfDocumentLifecycleState.TechnicalQc => TmfDocumentLifecycleState.TechnicalQc,
        TmfDocumentLifecycleState.Approved => TmfDocumentLifecycleState.Approved,
        TmfDocumentLifecycleState.Rejected => TmfDocumentLifecycleState.Rejected,
        TmfDocumentLifecycleState.Filed => TmfDocumentLifecycleState.Approved,
        TmfDocumentLifecycleState.Archived => TmfDocumentLifecycleState.Approved,
        _ => status
    };

    public static TmfDocument CreateDraft(
        string id,
        string studyId,
        string filename,
        string createdBy,
        DateTime createdAt,
        string reasonForChange,
        string? siteId = null,
        int zone = 1,
        string section = "01.01",
        string artifactCode = "01.01.01",
        string artifactType = "Clinical Trial Protocol",
        string content = "",
        string mimeType = "application/pdf",
        string taxonomyVersion = "v3.2.0-complete",
        string? metadataJson = null,
        long versionIndex = 1)
    {
        return new TmfDocument(id)
        {
            StudyId = studyId,
            SiteId = siteId,
            Zone = zone,
            Section = section,
            ArtifactCode = artifactCode,
            ArtifactType = artifactType,
            Filename = filename,
            Content = content,
            MimeType = mimeType,
            TaxonomyVersion = taxonomyVersion,
            MetadataJson = metadataJson,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            ReasonForChange = reasonForChange,
            VersionIndex = versionIndex
        };
    }

    public static TmfDocument CreateForTechnicalQc(
        string id,
        string studyId,
        string filename,
        string createdBy,
        string actorRole,
        DateTimeOffset timestamp,
        string reasonForChange,
        string? siteId = null,
        int zone = 1,
        string section = "01.01",
        string artifactCode = "01.01.01",
        string artifactType = "Clinical Trial Protocol",
        string content = "",
        string mimeType = "application/pdf",
        string taxonomyVersion = "v3.2.0-complete",
        string? metadataJson = null,
        long versionIndex = 1)
    {
        var doc = new TmfDocument(id)
        {
            StudyId = studyId,
            SiteId = siteId,
            Zone = zone,
            Section = section,
            ArtifactCode = artifactCode,
            ArtifactType = artifactType,
            Filename = filename,
            Content = content,
            MimeType = mimeType,
            TaxonomyVersion = taxonomyVersion,
            MetadataJson = metadataJson,
            CreatedAt = timestamp.UtcDateTime,
            CreatedBy = createdBy,
            ReasonForChange = reasonForChange,
            VersionIndex = versionIndex,
            Status = TmfDocumentLifecycleState.TechnicalQc,
            ApprovalStatus = TmfDocumentLifecycleState.TechnicalQc,
            TransitionSequence = 1
        };

        var transition = new TmfDocumentQcTransition
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = id,
            TransitionSequence = 1,
            FromStatus = TmfDocumentLifecycleState.Draft,
            ToStatus = TmfDocumentLifecycleState.TechnicalQc,
            ActorId = createdBy,
            ActorRole = actorRole,
            ReasonForChange = reasonForChange,
            Timestamp = timestamp
        };
        doc._transitions.Add(transition);

        return doc;
    }

    public static TmfDocument CreateApproved(
        string id,
        string studyId,
        string filename,
        string createdBy,
        DateTime createdAt,
        string reasonForChange,
        string? siteId = null,
        int zone = 1,
        string section = "01.01",
        string artifactCode = "01.01.01",
        string artifactType = "Clinical Trial Protocol",
        string content = "",
        string mimeType = "application/pdf",
        string taxonomyVersion = "v3.2.0-complete",
        string? metadataJson = null,
        int? protocolVersionIndex = null,
        long versionIndex = 1)
    {
        return new TmfDocument(id)
        {
            StudyId = studyId,
            SiteId = siteId,
            Zone = zone,
            Section = section,
            ArtifactCode = artifactCode,
            ArtifactType = artifactType,
            Filename = filename,
            Content = content,
            MimeType = mimeType,
            TaxonomyVersion = taxonomyVersion,
            MetadataJson = metadataJson,
            ProtocolVersionIndex = protocolVersionIndex,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            ReasonForChange = reasonForChange,
            VersionIndex = versionIndex,
            Status = TmfDocumentLifecycleState.Approved,
            ApprovalStatus = TmfDocumentLifecycleState.Approved,
            TransitionSequence = 1
        };
    }

    public static TmfDocument CreateFiled(
        string id,
        string studyId,
        string filename,
        string createdBy,
        DateTime createdAt,
        string reasonForChange,
        string? siteId = null,
        int zone = 1,
        string section = "01.01",
        string artifactCode = "01.01.01",
        string artifactType = "Clinical Trial Protocol",
        string content = "",
        string mimeType = "application/pdf",
        string taxonomyVersion = "v3.2.0-complete",
        string? objectKey = null,
        string? idempotencyKey = null,
        string? correlationKey = null,
        string? sourceSystem = null,
        string? syncStatus = null,
        DateTimeOffset? signingTimestamp = null,
        string? protocolVersionTag = null,
        int? protocolVersionIndex = null,
        long versionIndex = 1)
    {
        return new TmfDocument(id)
        {
            StudyId = studyId,
            SiteId = siteId,
            Zone = zone,
            Section = section,
            ArtifactCode = artifactCode,
            ArtifactType = artifactType,
            Filename = filename,
            Content = content,
            MimeType = mimeType,
            TaxonomyVersion = taxonomyVersion,
            ObjectKey = objectKey,
            IdempotencyKey = idempotencyKey,
            CorrelationKey = correlationKey,
            SourceSystem = sourceSystem,
            SyncStatus = syncStatus,
            SigningTimestamp = signingTimestamp,
            ProtocolVersionTag = protocolVersionTag,
            ProtocolVersionIndex = protocolVersionIndex,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            ReasonForChange = reasonForChange,
            VersionIndex = versionIndex,
            Status = TmfDocumentLifecycleState.Filed,
            ApprovalStatus = TmfDocumentLifecycleState.Approved,
            TransitionSequence = 1
        };
    }
}

public sealed class TmfExpectedDocument : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string Milestone { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public int? Zone { get; set; }
    public string? Section { get; set; }
    public string? MetadataJson { get; set; }

    public TmfExpectedDocument() : base(Guid.NewGuid().ToString()) { }
    public TmfExpectedDocument(string id) : base(id) { }
}

public sealed class TmfAuditLedgerSeal
{
    public int BlockIndex { get; set; }
    public string PreviousBlockHash { get; set; } = string.Empty;
    public string CurrentBlockHash { get; set; } = string.Empty;
    public required DateTimeOffset Timestamp { get; set; }
    public int SealedRecordCount { get; set; }
    public string MerkleRootHash { get; set; } = string.Empty;
}

public sealed class TmfAuditLog : AggregateRoot<string>
{
    public required DateTimeOffset Timestamp { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? CryptographicSeal { get; set; }

    public TmfAuditLog() : base(Guid.NewGuid().ToString()) { }
    public TmfAuditLog(string id) : base(id) { }
}

public sealed class TmfDocumentExpirationAlertState : AggregateRoot<string>
{
    public string DocumentId { get; set; } = string.Empty;
    public string WarningWindow { get; set; } = string.Empty;
    public required DateTimeOffset AlertedAt { get; set; }
    public bool Dispatched { get; set; }
    public string? NotificationId { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public TmfDocumentExpirationAlertState() : base(Guid.NewGuid().ToString()) { }
    public TmfDocumentExpirationAlertState(string id) : base(id) { }
}

public sealed class TmfDocumentQcTransition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public int TransitionSequence { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public required DateTimeOffset Timestamp { get; set; }
}

public sealed class TmfExportJob : AggregateRoot<string>
{
    public string JobId { get => Id; set => Id = value; }
    public string StudyId { get; set; } = string.Empty;
    public string RequestedBy { get => CreatedBy; set => CreatedBy = value; }
    public string Status { get; set; } = "QUEUED";
    public double ProgressPercentage { get; set; }
    public int TotalDocuments { get; set; }
    public int ProcessedDocuments { get; set; }
    public string? DownloadUrl { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public TmfExportJob() : base("JOB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()) { }
    public TmfExportJob(string id) : base(id) { }
}

public sealed class TmfDocumentRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DocumentId { get; set; } = string.Empty;
    public long RevisionNumber { get; set; }
    public string StudyCode { get; set; } = string.Empty;
    public string? SiteCode { get; set; }
    public int Zone { get; set; }
    public string Section { get; set; } = string.Empty;
    public string ArtifactCode { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = string.Empty;
    public string TaxonomyVersion { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string? ObjectKey { get; set; }
    public string? ContentChecksum { get; set; }
    public required DateTimeOffset RecordedAt { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;

    public static TmfDocumentRevision FromDocument(TmfDocument doc, DateTimeOffset recordedAt, string recordedBy) =>
        new()
        {
            DocumentId = doc.Id,
            RevisionNumber = doc.VersionIndex,
            StudyCode = doc.StudyId,
            SiteCode = doc.SiteId,
            Zone = doc.Zone,
            Section = doc.Section,
            ArtifactCode = doc.ArtifactCode,
            ArtifactType = doc.ArtifactType,
            Filename = doc.Filename,
            Content = doc.Content,
            MimeType = doc.MimeType,
            Status = doc.Status,
            ApprovalStatus = doc.ApprovalStatus,
            TaxonomyVersion = doc.TaxonomyVersion,
            MetadataJson = doc.MetadataJson,
            ObjectKey = doc.ObjectKey,
            ContentChecksum = doc.ContentChecksum,
            RecordedAt = recordedAt,
            RecordedBy = recordedBy,
            ReasonForChange = doc.ReasonForChange ?? "Document revision snapshot",
        };
}

