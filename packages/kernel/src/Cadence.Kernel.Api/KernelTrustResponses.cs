using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

namespace Cadence.Kernel.Api;

/// <summary>Request body for <c>POST /api/v1/kernel/roles</c> (<see cref="DefineRole"/>).</summary>
public sealed record KernelDefineRoleRequest(
    string? Code,
    string? Name,
    IReadOnlyList<string>? Capabilities,
    RoleId? RoleId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/grants</c> (<see cref="IssueGrant"/>).</summary>
public sealed record KernelIssueGrantRequest(
    string? GranteeId,
    string? GranteeKind = null,
    string? Capability = null,
    RoleId? RoleId = null,
    string? RoleCode = null,
    string? ScopeLevel = null,
    string? ScopeTargetRef = null,
    Guid? ScopeTargetId = null,
    string? ScopeCountry = null,
    string? StudyCode = null,
    string? SiteCode = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    bool WithGrantOption = false,
    GrantId? GrantId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/grants/{grantRef}/delegate</c> (<see cref="DelegateGrant"/>).</summary>
public sealed record KernelDelegateGrantRequest(
    string? GranteeId,
    string? GranteeKind = null,
    string? Capability = null,
    RoleId? RoleId = null,
    string? RoleCode = null,
    string? ScopeLevel = null,
    string? ScopeTargetRef = null,
    Guid? ScopeTargetId = null,
    string? ScopeCountry = null,
    string? StudyCode = null,
    string? SiteCode = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    bool WithGrantOption = false,
    GrantId? GrantId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/grants/{grantRef}/revoke</c> (<see cref="RevokeGrant"/>).</summary>
public sealed record KernelRevokeGrantRequest(
    DateTimeOffset? EffectiveAt = null);

/// <summary>Request body for <c>POST /api/v1/kernel/qualifications</c> (<see cref="RecordQualification"/>).</summary>
public sealed record KernelRecordQualificationRequest(
    string? HolderId,
    string? HolderKind = null,
    string? Kind = null,
    string? Code = null,
    string? EvidenceRef = null,
    ArtifactVersionId? EvidenceArtifactVersionId = null,
    string? ProtocolVersionRef = null,
    ProtocolVersionId? ProtocolVersionId = null,
    string? StudyCode = null,
    int? ProtocolVersionNumber = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    QualificationId? QualificationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/qualifications/{qualificationRef}/revoke</c> (<see cref="RevokeQualification"/>).</summary>
public sealed record KernelRevokeQualificationRequest(
    DateTimeOffset? EffectiveAt = null);

/// <summary>Request body for <c>POST /api/v1/kernel/qualification-requirements</c> (<see cref="DefineQualificationRequirement"/>).</summary>
public sealed record KernelDefineQualificationRequirementRequest(
    string? Capability,
    string? QualificationCode,
    string? ProtocolVersionRef = null,
    ProtocolVersionId? ProtocolVersionId = null,
    string? ScopeLevel = null,
    string? ScopeTargetRef = null,
    Guid? ScopeTargetId = null,
    string? ScopeCountry = null,
    string? StudyCode = null,
    string? SiteCode = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    QualificationRequirementId? RequirementId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/qualification-requirements/{requirementRef}/retire</c> (<see cref="RetireQualificationRequirement"/>).</summary>
public sealed record KernelRetireQualificationRequirementRequest(
    DateTimeOffset? EffectiveAt = null);

/// <summary>Wire representation of a bitemporal validity/recorded period (<c>K-TIME-1</c>).</summary>
public sealed record KernelBitemporalPeriodResponse(
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    DateTimeOffset RecordedFrom,
    DateTimeOffset? RecordedTo)
{
    internal static KernelBitemporalPeriodResponse From(KernelBitemporalPeriodReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new(model.ValidFrom, model.ValidTo, model.RecordedFrom, model.RecordedTo);
    }
}

/// <summary>Wire representation of a defined Kernel role.</summary>
public sealed record KernelRoleResponse(
    string Ref,
    RoleId RoleId,
    string Code,
    string Name,
    IReadOnlyList<string> Capabilities)
{
    internal static KernelRoleResponse From(RoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(
            NodeRef.For(state.RoleId).ToString(),
            state.RoleId,
            state.Code,
            state.Name,
            state.Capabilities);
    }

    internal static KernelRoleResponse From(KernelRoleReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new(
            NodeRef.For(model.RoleId, model.VersionIndex).ToString(),
            model.RoleId,
            model.Code,
            model.Name,
            model.Capabilities);
    }
}

/// <summary>Wire representation of a Kernel grant or delegation.</summary>
public sealed record KernelGrantResponse(
    string Ref,
    GrantId GrantId,
    string? ParentGrantRef,
    GrantId? ParentGrantId,
    string GranteeKind,
    string GranteeId,
    string? Capability,
    RoleId? RoleId,
    string? RoleCode,
    string? RoleName,
    IReadOnlyList<string> Capabilities,
    string ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    StudyId? StudyId,
    string? StudyCode,
    SiteId? SiteId,
    string? SiteCode,
    StudySiteId? StudySiteId,
    bool WithGrantOption,
    string? IssuedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    IReadOnlyList<KernelBitemporalPeriodResponse> Periods)
{
    internal static KernelGrantResponse From(GrantState state, string? roleCode = null, string? roleName = null, IReadOnlyList<string>? capabilities = null, StudyId? studyId = null, string? studyCode = null, SiteId? siteId = null, string? siteCode = null, StudySiteId? studySiteId = null, string? issuedBy = null, DateTimeOffset? recordedAt = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<string> resolvedCapabilities = state.Capability is { } single
            ? [single]
            : capabilities ?? [];
        IReadOnlyList<KernelBitemporalPeriodResponse> periods = state.ValidFrom is { } vf && recordedAt is { } rec
            ? [new KernelBitemporalPeriodResponse(vf, state.ValidTo, rec, null)]
            : [];
        return new(
            NodeRef.For(state.GrantId).ToString(),
            state.GrantId,
            state.ParentGrantId is { } pid ? NodeRef.For(pid).ToString() : null,
            state.ParentGrantId,
            state.Grantee.Kind.ToString(),
            state.Grantee.Id,
            state.Capability,
            state.RoleId,
            roleCode,
            roleName,
            resolvedCapabilities,
            state.Scope.Level.ToString(),
            state.Scope.TargetId,
            state.Scope.Country,
            studyId,
            studyCode,
            siteId,
            siteCode,
            studySiteId,
            state.WithGrantOption,
            issuedBy,
            !state.Revoked,
            state.ValidFrom,
            state.ValidTo,
            state.Revoked,
            periods);
    }

    internal static KernelGrantResponse From(KernelGrantReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new(
            NodeRef.For(model.GrantId, model.VersionIndex).ToString(),
            model.GrantId,
            model.ParentGrantId is { } pid ? NodeRef.For(pid).ToString() : null,
            model.ParentGrantId,
            model.GranteeKind.ToString(),
            model.GranteeId,
            model.Capability,
            model.RoleId,
            model.RoleCode,
            model.RoleName,
            model.Capabilities,
            model.ScopeLevel.ToString(),
            model.ScopeTargetId,
            model.ScopeCountry,
            model.StudyId,
            model.StudyCode,
            model.SiteId,
            model.SiteCode,
            model.StudySiteId,
            model.WithGrantOption,
            model.IssuedBy,
            model.InEffect,
            model.ValidFrom,
            model.ValidTo,
            model.Revoked,
            [.. model.Periods.Select(KernelBitemporalPeriodResponse.From)]);
    }
}

/// <summary>Wire representation of a Kernel qualification.</summary>
public sealed record KernelQualificationResponse(
    string Ref,
    QualificationId QualificationId,
    string HolderKind,
    string HolderId,
    string Kind,
    string Code,
    string? ProtocolVersionRef,
    ProtocolVersionId? ProtocolVersionId,
    int? ProtocolVersionNumber,
    StudyId? StudyId,
    string? StudyCode,
    string? EvidenceArtifactRef,
    string EvidenceArtifactVersionRef,
    ArtifactVersionId EvidenceArtifactVersionId,
    string? RecordedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Revoked,
    IReadOnlyList<KernelBitemporalPeriodResponse> Periods)
{
    internal static KernelQualificationResponse From(QualificationState state, int? versionNumber = null, StudyId? studyId = null, string? studyCode = null, string? recordedBy = null, DateTimeOffset? recordedAt = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<KernelBitemporalPeriodResponse> periods = state.ValidFrom is { } vf && recordedAt is { } rec
            ? [new KernelBitemporalPeriodResponse(vf, state.ValidTo, rec, null)]
            : [];
        return new(
            NodeRef.For(state.QualificationId).ToString(),
            state.QualificationId,
            state.Holder.Kind.ToString(),
            state.Holder.Id,
            state.Kind.ToString(),
            state.Code,
            state.ProtocolVersionId is { } pvId ? NodeRef.For(pvId).ToString() : null,
            state.ProtocolVersionId,
            versionNumber,
            studyId,
            studyCode,
            null,
            NodeRef.For(state.Evidence).ToString(),
            state.Evidence,
            recordedBy,
            !state.Revoked,
            state.ValidFrom,
            state.ValidTo,
            state.Revoked,
            periods);
    }

    internal static KernelQualificationResponse From(KernelQualificationReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new(
            NodeRef.For(model.QualificationId, model.VersionIndex).ToString(),
            model.QualificationId,
            model.HolderKind.ToString(),
            model.HolderId,
            model.Kind.ToString(),
            model.Code,
            model.ProtocolVersionId is { } pvId ? NodeRef.For(pvId).ToString() : null,
            model.ProtocolVersionId,
            model.ProtocolVersionNumber,
            model.StudyId,
            model.StudyCode,
            model.EvidenceArtifactId is { } aid ? NodeRef.For(aid).ToString() : null,
            NodeRef.For(model.EvidenceArtifactVersionId).ToString(),
            model.EvidenceArtifactVersionId,
            model.RecordedBy,
            model.InEffect,
            model.ValidFrom,
            model.ValidTo,
            model.Revoked,
            [.. model.Periods.Select(KernelBitemporalPeriodResponse.From)]);
    }
}

/// <summary>Wire representation of a Kernel qualification requirement.</summary>
public sealed record KernelQualificationRequirementResponse(
    string Ref,
    QualificationRequirementId RequirementId,
    string Capability,
    string QualificationCode,
    string? ProtocolVersionRef,
    ProtocolVersionId? ProtocolVersionId,
    int? ProtocolVersionNumber,
    string ScopeLevel,
    Guid? ScopeTargetId,
    string? ScopeCountry,
    StudyId? StudyId,
    string? StudyCode,
    string? DefinedBy,
    bool InEffect,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool Retired,
    IReadOnlyList<KernelBitemporalPeriodResponse> Periods)
{
    internal static KernelQualificationRequirementResponse From(QualificationRequirementState state, int? versionNumber = null, StudyId? studyId = null, string? studyCode = null, string? definedBy = null, DateTimeOffset? recordedAt = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<KernelBitemporalPeriodResponse> periods = state.ValidFrom is { } vf && recordedAt is { } rec
            ? [new KernelBitemporalPeriodResponse(vf, state.ValidTo, rec, null)]
            : [];
        return new(
            NodeRef.For(state.RequirementId).ToString(),
            state.RequirementId,
            state.Capability,
            state.QualificationCode,
            state.ProtocolVersionId is { } pvId ? NodeRef.For(pvId).ToString() : null,
            state.ProtocolVersionId,
            versionNumber,
            state.Scope.Level.ToString(),
            state.Scope.TargetId,
            state.Scope.Country,
            studyId,
            studyCode,
            definedBy,
            !state.Retired,
            state.ValidFrom,
            state.ValidTo,
            state.Retired,
            periods);
    }

    internal static KernelQualificationRequirementResponse From(KernelQualificationRequirementReadModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new(
            NodeRef.For(model.RequirementId, model.VersionIndex).ToString(),
            model.RequirementId,
            model.Capability,
            model.QualificationCode,
            model.ProtocolVersionId is { } pvId ? NodeRef.For(pvId).ToString() : null,
            model.ProtocolVersionId,
            model.ProtocolVersionNumber,
            model.ScopeLevel.ToString(),
            model.ScopeTargetId,
            model.ScopeCountry,
            model.StudyId,
            model.StudyCode,
            model.DefinedBy,
            model.InEffect,
            model.ValidFrom,
            model.ValidTo,
            model.Retired,
            [.. model.Periods.Select(KernelBitemporalPeriodResponse.From)]);
    }
}
