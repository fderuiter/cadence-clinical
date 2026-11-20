using System;
using System.Collections.Generic;

namespace Cadence.Modules.Org.Contracts;

/// <summary>
/// Workspace team directory response for the caller's tenant and scope visibility (ADR-2611).
/// Directory reads are strictly pseudonymous so <c>GET /api/v1/org/team</c> never triggers unrequested IdentityVault
/// disclosure audit entries (K-IDN-1); authorized operators reveal individual identities on demand via
/// <c>POST /api/v1/org/members/{personId}/reveal</c> with a signed <c>X-Change-Reason</c>.
/// </summary>
/// <param name="TenantCode">The signed tenant code.</param>
/// <param name="TenantName">Display name of the tenant workspace.</param>
/// <param name="ActorId">Pseudonymous actor identifier of the signed-in caller.</param>
/// <param name="CanInvite">True when the caller holds <c>grant.issue</c> authority to invite new seats.</param>
/// <param name="CanDelegate">True when the caller holds at least one active grant with <c>WithGrantOption</c>.</param>
/// <param name="CanRevealIdentity">True when the caller holds <c>identity.reveal.person</c> authority.</param>
/// <param name="Roles">Tenant role definitions available for assignment.</param>
/// <param name="Scopes">Scope targets visible or assignable by the caller.</param>
/// <param name="DelegableGrants">Active delegable grants held by the caller.</param>
/// <param name="DutyCatalog">Clinical duty bundles available for bounded site delegation.</param>
/// <param name="Members">Current tenant members visible to the caller, with active and historical grants.</param>
public sealed record OrgTeamViewResponse(
    string TenantCode,
    string TenantName,
    string ActorId,
    bool CanInvite,
    bool CanDelegate,
    bool CanRevealIdentity,
    IReadOnlyList<OrgRoleDto> Roles,
    IReadOnlyList<OrgScopeOptionDto> Scopes,
    IReadOnlyList<OrgDelegableGrantDto> DelegableGrants,
    IReadOnlyList<OrgDutyOptionDto> DutyCatalog,
    IReadOnlyList<OrgTeamMemberDto> Members);

/// <summary>A role bundle defined in the tenant.</summary>
/// <param name="RoleId">Role identifier.</param>
/// <param name="Code">Stable uppercase role code.</param>
/// <param name="Name">Human-readable role title.</param>
/// <param name="Capabilities">Bundled kernel capability tokens.</param>
public sealed record OrgRoleDto(
    Guid RoleId,
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities);

/// <summary>An organizational, study, or site scope option.</summary>
/// <param name="Key">Composite scope key (<c>Level:TargetId:Country</c>).</param>
/// <param name="Level">Scope level (<c>Tenant</c>, <c>Organization</c>, <c>Study</c>, <c>StudyCountry</c>, <c>StudySite</c>).</param>
/// <param name="TargetId">Target node UUID, or null for tenant-wide scope.</param>
/// <param name="Country">ISO country code when <paramref name="Level"/> is <c>StudyCountry</c>.</param>
/// <param name="Code">Short code of the target node.</param>
/// <param name="Label">Human-readable label for selectors and badges.</param>
/// <param name="ParentLabel">Parent study code for study-site scopes, if applicable.</param>
/// <param name="CanIssueGrants">True when the caller's <c>grant.issue</c> authority covers this scope.</param>
public sealed record OrgScopeOptionDto(
    string Key,
    string Level,
    Guid? TargetId,
    string? Country,
    string Code,
    string Label,
    string? ParentLabel,
    bool CanIssueGrants);

/// <summary>An active grant held by the caller with <c>WithGrantOption</c> enabled.</summary>
/// <param name="GrantId">Grant identifier.</param>
/// <param name="Label">Human-readable summary of the grant.</param>
/// <param name="ScopeKey">Composite scope key.</param>
/// <param name="ScopeLevel">Scope level.</param>
/// <param name="ScopeTargetId">Target node UUID, if scoped below tenant.</param>
/// <param name="ScopeCountry">Country code when country-scoped.</param>
/// <param name="ScopeLabel">Display label of the grant's scope.</param>
/// <param name="Capabilities">Capabilities carried by this grant.</param>
/// <param name="ValidFrom">Start of the grant's effective validity.</param>
/// <param name="ValidTo">End of the grant's effective validity, or null when open-ended.</param>
public sealed record OrgDelegableGrantDto(
    Guid GrantId,
    string Label,
    string ScopeKey,
    string ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    string ScopeLabel,
    IReadOnlyList<string> Capabilities,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>A named clinical duty bundle that maps to bounded kernel capabilities.</summary>
/// <param name="Duty">Canonical duty key.</param>
/// <param name="Label">Display title.</param>
/// <param name="Capabilities">Mapped kernel capability tokens.</param>
public sealed record OrgDutyOptionDto(
    string Duty,
    string Label,
    IReadOnlyList<string> Capabilities);

/// <summary>A tenant member and their active and historical kernel grants and delegations.</summary>
/// <param name="PersonId">Pseudonymous person identifier (matches OIDC <c>sub</c>).</param>
/// <param name="DisplayName">Pseudonymous handle (<c>Member &lt;shortId&gt;</c>) until disclosed via audited reveal.</param>
/// <param name="Email">Null on pseudonymous directory reads; populated client-side after audited reveal.</param>
/// <param name="IdentityRevealed">True when direct identifiers have been disclosed.</param>
/// <param name="HasSealedIdentity">True when a non-erased person record exists in <c>IdentityVault</c>.</param>
/// <param name="CanRevealIdentity">True when the caller holds <c>identity.reveal.person</c> and a sealed record exists.</param>
/// <param name="JoinedAt">UTC timestamp when the member joined the tenant.</param>
/// <param name="ActiveGrants">Currently effective role grants and delegations.</param>
/// <param name="HistoricalGrants">Expired, superseded, or revoked grants and delegations.</param>
public sealed record OrgTeamMemberDto(
    Guid PersonId,
    string DisplayName,
    string? Email,
    bool IdentityRevealed,
    bool HasSealedIdentity,
    bool CanRevealIdentity,
    DateTimeOffset JoinedAt,
    IReadOnlyList<OrgMemberGrantDto> ActiveGrants,
    IReadOnlyList<OrgMemberGrantDto> HistoricalGrants);

/// <summary>One role grant, capability grant, or bounded delegation held by a member.</summary>
/// <param name="GrantId">Grant identifier.</param>
/// <param name="Kind">Assignment kind (<c>role</c>, <c>capability</c>, or <c>delegation</c>).</param>
/// <param name="Summary">Display title of the role or capability.</param>
/// <param name="RoleId">Role UUID when a role was granted.</param>
/// <param name="RoleCode">Role code when a role was granted.</param>
/// <param name="Capability">Capability token when a single capability was granted.</param>
/// <param name="Capabilities">Effective capabilities conferred by this grant.</param>
/// <param name="ScopeLevel">Scope level.</param>
/// <param name="ScopeTargetId">Scope target UUID.</param>
/// <param name="ScopeCountry">Scope country code.</param>
/// <param name="ScopeCode">Short code of the scope target.</param>
/// <param name="ScopeLabel">Human-readable scope label.</param>
/// <param name="ParentGrantId">Parent grant UUID when this record is a delegation.</param>
/// <param name="WithGrantOption">True when the grantee may sub-delegate within this grant.</param>
/// <param name="IssuedBy">Actor string of the administrator or delegator who signed the grant.</param>
/// <param name="IssuedAt">UTC timestamp when the grant was recorded.</param>
/// <param name="ReasonForChange">Signed reason for change recorded on issuance.</param>
/// <param name="ValidFrom">Effective start timestamp.</param>
/// <param name="ValidTo">Effective end timestamp, if bounded or revoked.</param>
/// <param name="IsActive">True when the grant and its entire ancestor chain are currently in effect.</param>
/// <param name="IsRevoked">True when the grant has been revoked.</param>
/// <param name="CanRevoke">True when the caller is permitted to revoke this grant.</param>
public sealed record OrgMemberGrantDto(
    Guid GrantId,
    string Kind,
    string Summary,
    Guid? RoleId,
    string? RoleCode,
    string? Capability,
    IReadOnlyList<string> Capabilities,
    string ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    string ScopeCode,
    string ScopeLabel,
    Guid? ParentGrantId,
    bool WithGrantOption,
    string IssuedBy,
    DateTimeOffset IssuedAt,
    string ReasonForChange,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    bool IsRevoked,
    bool CanRevoke);

/// <summary>
/// Request payload for inviting a colleague to a tenant seat and issuing a kernel grant (<c>POST /api/v1/org/invitations</c>).
/// </summary>
/// <param name="Email">Colleague email address.</param>
/// <param name="FullName">Colleague full name (split into given/family name when separate fields are omitted).</param>
/// <param name="GivenName">Optional explicit given name.</param>
/// <param name="FamilyName">Optional explicit family name.</param>
/// <param name="RoleCode">Role code to grant (mutually exclusive with <paramref name="Capability"/>).</param>
/// <param name="RoleId">Optional role UUID to grant.</param>
/// <param name="Capability">Single capability token to grant instead of a role.</param>
/// <param name="ScopeLevel">Target scope level (<c>Tenant</c>, <c>Organization</c>, <c>Study</c>, <c>StudyCountry</c>, <c>StudySite</c>).</param>
/// <param name="ScopeTargetId">Target node UUID when scoped below tenant.</param>
/// <param name="ScopeCountry">ISO country code when <paramref name="ScopeLevel"/> is <c>StudyCountry</c>.</param>
/// <param name="ValidFrom">Optional effective start timestamp (defaults to start of current UTC day).</param>
/// <param name="ValidTo">Optional effective end timestamp.</param>
/// <param name="WithGrantOption">Whether the invitee may sub-delegate within this grant.</param>
/// <param name="ReasonForChange">Optional body fallback when <c>X-Change-Reason</c> header is omitted.</param>
public sealed record InviteMemberRequest(
    string Email,
    string? FullName = null,
    string? GivenName = null,
    string? FamilyName = null,
    string? RoleCode = null,
    Guid? RoleId = null,
    string? Capability = null,
    string ScopeLevel = "Study",
    Guid? ScopeTargetId = null,
    string? ScopeCountry = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    bool WithGrantOption = false,
    string? ReasonForChange = null);

/// <summary>Result of inviting a colleague and issuing or reusing their kernel grant.</summary>
/// <param name="PersonId">Pseudonymous person UUID (matches OIDC <c>sub</c>).</param>
/// <param name="GrantId">Issued or reused kernel grant UUID.</param>
/// <param name="ReusedExistingAccount">True when an existing tenant identity was reused without duplicating the user.</param>
/// <param name="ReusedExistingGrant">True when an identical active grant already covered the assignment.</param>
/// <param name="WelcomeEmailSent">True when the OIDC welcome/password-setup email was dispatched.</param>
/// <param name="ScopeLabel">Human-readable label of the assigned scope.</param>
/// <param name="ValidFrom">Effective start timestamp of the grant.</param>
/// <param name="ValidTo">Effective end timestamp of the grant, if bounded.</param>
public sealed record InviteMemberResponse(
    Guid PersonId,
    Guid GrantId,
    bool ReusedExistingAccount,
    bool ReusedExistingGrant,
    bool WelcomeEmailSent,
    string ScopeLabel,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>
/// Request payload for delegating bounded duties by colleague email under a parent grant (<c>POST /api/v1/org/delegations</c>).
/// </summary>
/// <param name="Email">Colleague email address.</param>
/// <param name="FullName">Colleague full name (optional when reusing an existing colleague).</param>
/// <param name="GivenName">Optional explicit given name.</param>
/// <param name="FamilyName">Optional explicit family name.</param>
/// <param name="ParentGrantId">Optional parent grant UUID; when omitted, selects the caller's matching delegable grant.</param>
/// <param name="Duties">Named clinical duties (for example <c>Informed Consent</c>, <c>CRF Data Entry</c>).</param>
/// <param name="Capabilities">Explicit capability tokens to delegate.</param>
/// <param name="RoleCode">Optional role code when delegating an entire role subset.</param>
/// <param name="RoleId">Optional role UUID when delegating a role.</param>
/// <param name="ScopeLevel">Explicit target scope level, when not resolving by <paramref name="StudyCode"/>/<paramref name="SiteCode"/>.</param>
/// <param name="ScopeTargetId">Explicit target scope UUID.</param>
/// <param name="ScopeCountry">Explicit target scope country code.</param>
/// <param name="StudyCode">Study code or UUID string (used by CTMS site delegation lookup).</param>
/// <param name="SiteCode">Site code (used by CTMS site delegation lookup).</param>
/// <param name="ValidFrom">Optional effective start timestamp.</param>
/// <param name="ValidTo">Optional effective end timestamp.</param>
/// <param name="WithGrantOption">Whether the delegatee may further sub-delegate.</param>
/// <param name="ReasonForChange">Optional body fallback when <c>X-Change-Reason</c> header is omitted.</param>
public sealed record DelegateDutiesRequest(
    string Email,
    string? FullName = null,
    string? GivenName = null,
    string? FamilyName = null,
    Guid? ParentGrantId = null,
    IReadOnlyList<string>? Duties = null,
    IReadOnlyList<string>? Capabilities = null,
    string? RoleCode = null,
    Guid? RoleId = null,
    string? ScopeLevel = null,
    Guid? ScopeTargetId = null,
    string? ScopeCountry = null,
    string? StudyCode = null,
    string? SiteCode = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    bool WithGrantOption = false,
    string? ReasonForChange = null);

/// <summary>Result of delegating bounded duties by colleague email.</summary>
/// <param name="PersonId">Pseudonymous delegatee person UUID (matches OIDC <c>sub</c>).</param>
/// <param name="ParentGrantId">Parent grant UUID under which the delegation was signed.</param>
/// <param name="PrimaryGrantId">First issued or reused child delegation grant UUID.</param>
/// <param name="GrantIds">All child delegation grant UUIDs issued or reused for the requested duties.</param>
/// <param name="Capabilities">Effective capabilities delegated.</param>
/// <param name="ReusedExistingAccount">True when an existing tenant identity was reused.</param>
/// <param name="WelcomeEmailSent">True when a welcome password-setup email was dispatched.</param>
/// <param name="ScopeLabel">Human-readable label of the target scope.</param>
/// <param name="ValidFrom">Effective start timestamp.</param>
/// <param name="ValidTo">Effective end timestamp, if bounded.</param>
public sealed record DelegateDutiesResponse(
    Guid PersonId,
    Guid ParentGrantId,
    Guid PrimaryGrantId,
    IReadOnlyList<Guid> GrantIds,
    IReadOnlyList<string> Capabilities,
    bool ReusedExistingAccount,
    bool WelcomeEmailSent,
    string ScopeLabel,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>Request payload for revoking an active role grant or delegation (<c>POST /api/v1/org/grants/{grantId}/revoke</c>).</summary>
/// <param name="EffectiveAt">Optional effective revocation timestamp (defaults to start of current UTC day or grant start).</param>
/// <param name="ReasonForChange">Optional body fallback when <c>X-Change-Reason</c> header is omitted.</param>
public sealed record RevokeOrgGrantRequest(
    DateTimeOffset? EffectiveAt = null,
    string? ReasonForChange = null);

/// <summary>Result of revoking an active role grant or delegation.</summary>
/// <param name="GrantId">Revoked grant UUID.</param>
/// <param name="Revoked">True when the grant has been revoked entirely or ended.</param>
/// <param name="ValidFrom">Historical start timestamp, if any valid slice remains before revocation.</param>
/// <param name="ValidTo">Effective end timestamp after revocation, if any valid slice remains.</param>
public sealed record RevokeOrgGrantResponse(
    Guid GrantId,
    bool Revoked,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>
/// Audited disclosure response for <c>POST /api/v1/org/members/{personId}/reveal</c> (K-IDN-1, ADR-2458).
/// </summary>
/// <param name="PersonId">Pseudonymous person UUID.</param>
/// <param name="DisplayName">Disclosed full name or email fallback.</param>
/// <param name="Email">Disclosed email address, when present.</param>
/// <param name="GivenName">Disclosed given name, when present.</param>
/// <param name="FamilyName">Disclosed family name, when present.</param>
public sealed record RevealMemberIdentityResponse(
    Guid PersonId,
    string DisplayName,
    string? Email,
    string? GivenName,
    string? FamilyName);
