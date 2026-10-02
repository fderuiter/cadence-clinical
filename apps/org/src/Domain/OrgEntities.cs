using System;

namespace Cadence.Apps.Org.Domain;



public class PersonnelAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PersonnelId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class DelegationOfAuthority
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DelegatorId { get; set; } = string.Empty;
    public string DelegateeId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string DutiesJson { get; set; } = "[]";
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Signature { get; set; }
    public string? SignedPayloadJson { get; set; }
    public DateTimeOffset? SignedAt { get; set; }
    public string? SignedBy { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public string? RevocationReason { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}

public class TrainingLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string PersonnelId { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string StudyId { get; set; } = string.Empty;
    public string TrainingTopic { get; set; } = string.Empty;
    public DateTimeOffset CompletionDate { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
    public string? SignatureManifestationJson { get; set; }
    public string? Signer { get; set; }
    public DateTimeOffset? SigningTimestamp { get; set; }
}

public class OrgAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required DateTimeOffset Timestamp { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? RecordId { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
}

public class UserOnboardingPreference
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Persona { get; set; } = string.Empty;
    public int CurrentStep { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public bool Disabled { get; set; } = false;
    public string EventsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public int VersionIndex { get; set; } = 1;
}
