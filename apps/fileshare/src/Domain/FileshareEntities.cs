using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Fileshare.Domain;

public sealed class FileRecord : AggregateRoot<string>
{
    public string StudyId { get; set; } = string.Empty;
    public string? SiteId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string? ChecksumSha256 { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public required DateTimeOffset UploadedAt { get; set; }
    public bool IsOnHold { get; set; }
    public string? HoldReason { get; set; }
    public bool IsDeleted { get; set; }
    public string Status { get; set; } = "PENDING_SCAN";
    public string ScanStatus { get; set; } = "PENDING";
    public string? ScanEngine { get; set; }
    public DateTimeOffset? ScanCompletedAt { get; set; }
    public string? ScanThreatName { get; set; }
    public string TranscodeStatus { get; set; } = "NOT_REQUIRED";
    public string? HlsManifestKey { get; set; }
    public string? TranscodeError { get; set; }
    public string? PdfRenditionKey { get; set; }
    public string ConversionStatus { get; set; } = "NOT_REQUIRED";
    public string? ConversionError { get; set; }
    public string ApprovalStatus { get; set; } = "PENDING_APPROVAL";
    public string? SignatureManifestation { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovalReason { get; set; }

    public FileRecord() : base(Guid.NewGuid().ToString()) { }
    public FileRecord(string id) : base(id) { }
}

public sealed class ShareGrant : AggregateRoot<string>
{
    public string FileRecordId { get; set; } = string.Empty;
    public string? GrantedToUserId { get; set; }
    public string GrantedByUserId { get; set; } = string.Empty;
    public string Scope { get; set; } = "individual";
    public string PermissionLevel { get; set; } = "view";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public bool IsDeleted { get; set; }

    public ShareGrant() : base(Guid.NewGuid().ToString()) { }
    public ShareGrant(string id) : base(id) { }
}

public sealed class GuestLink : AggregateRoot<string>
{
    public string FileRecordId { get; set; } = string.Empty;
    public string TokenHmac { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastAccessedAt { get; set; }
    public int AccessCount { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public GuestLink() : base(Guid.NewGuid().ToString()) { }
    public GuestLink(string id) : base(id) { }
}

public sealed class PlaybackAuditLog : AggregateRoot<string>
{
    public string FileRecordId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Source { get; set; } = "internal";
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public required DateTimeOffset Timestamp { get; set; }

    public PlaybackAuditLog() : base(Guid.NewGuid().ToString()) { }
    public PlaybackAuditLog(string id) : base(id) { }
}

public sealed class FileAuditLog : AggregateRoot<string>
{
    public string FileRecordId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public required DateTimeOffset Timestamp { get; set; }
    public string? Detail { get; set; }

    public FileAuditLog() : base(Guid.NewGuid().ToString()) { }
    public FileAuditLog(string id) : base(id) { }
}
