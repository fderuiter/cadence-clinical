using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Etmf.Domain;

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
    public string Status { get; set; } = string.Empty;
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
    public string ApprovalStatus { get; set; } = "DRAFT";
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

    public TmfDocument() : base(Guid.NewGuid().ToString()) { }
    public TmfDocument(string id) : base(id) { }
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

