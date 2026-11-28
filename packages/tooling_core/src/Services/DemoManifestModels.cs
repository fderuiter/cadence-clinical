using System.Collections.Generic;

namespace Cadence.ToolingCore.Services;

/// <summary>Canonical snapshot of the synthetic demo study validated from <c>demo-seed.json</c> and <c>cadence-realm.json</c>.</summary>
public sealed record DemoManifestSnapshot(
    string TenantCode,
    string TenantUuid,
    string EpochIso,
    long EpochMs,
    DemoSponsorEntry Sponsor,
    DemoStudyEntry Study,
    IReadOnlyList<DemoSiteEntry> Sites,
    IReadOnlyList<DemoParticipantEntry> Participants,
    IReadOnlyList<DemoRoleEntry> Roles,
    IReadOnlyList<DemoPersonaEntry> Personas);

/// <summary>Synthetic sponsor entry in the demo manifest.</summary>
public sealed record DemoSponsorEntry(
    string Code,
    string Name,
    string OrganizationUuid);

/// <summary>Synthetic study entry in the demo manifest.</summary>
public sealed record DemoStudyEntry(
    string Code,
    string Title,
    string Phase,
    string SponsorCode,
    string Uuid,
    string Ref);

/// <summary>Synthetic site entry in the demo manifest.</summary>
public sealed record DemoSiteEntry(
    string Code,
    string Name,
    string Country,
    string InstitutionCode,
    string ComplianceStage,
    string InstitutionUuid,
    string SiteUuid,
    string StudySiteUuid,
    string StudySiteRef);

/// <summary>Synthetic participant entry in the demo manifest.</summary>
public sealed record DemoParticipantEntry(
    string SubjectCode,
    string SiteCode,
    int EnrollmentIndex,
    string Status,
    string ScreenedAtIso,
    string ParticipantUuid,
    string EnrollmentUuid,
    string EnrollmentRef);

/// <summary>Synthetic role entry in the demo manifest.</summary>
public sealed record DemoRoleEntry(
    string Code,
    string Name,
    string RoleUuid,
    IReadOnlyList<string> Capabilities);

/// <summary>Synthetic realm persona entry in the demo manifest.</summary>
public sealed record DemoPersonaEntry(
    string Username,
    string KeycloakSub,
    string Email,
    string FirstName,
    string LastName,
    string TenantCode,
    string KernelRoleCode,
    string Workflow,
    string WorkspacePersona,
    string? AssignedSiteCode,
    string GrantUuid,
    IReadOnlyList<string> RealmRoles);
