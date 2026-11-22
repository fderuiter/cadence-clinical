using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>Request body for <c>POST /api/v1/kernel/protocol-versions/{versionRef}/approve</c>.</summary>
/// <param name="Meaning">Attestation meaning (defaults to <c>Approved</c>).</param>
/// <param name="ExpectedContentHash">Optional frozen protocol content hash expected by the signer (21 CFR 11.70).</param>
/// <param name="AttestationId">Optional caller-assigned attestation UUID; derived deterministically when omitted.</param>
public sealed record KernelApproveProtocolVersionRequest(
    string? Meaning = null,
    string? ExpectedContentHash = null,
    Guid? AttestationId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/artifacts</c>.</summary>
/// <param name="Kind">Artifact kind, such as <c>EthicsApproval</c>, <c>RegulatoryApproval</c>, or <c>CtaAcknowledgement</c>.</param>
/// <param name="Title">Human-readable document title (at most 500 characters).</param>
/// <param name="ContentHash">Canonical <c>sha256:&lt;64-hex&gt;</c> or 64-character hex digest of the artifact bytes.</param>
/// <param name="SizeBytes">Byte length of the artifact content.</param>
/// <param name="MediaType">MIME media type (for example <c>application/pdf</c>).</param>
/// <param name="StorageRef">Storage location URI or object key of the artifact bytes.</param>
/// <param name="ArtifactId">Optional artifact UUID; derived deterministically when omitted.</param>
/// <param name="ArtifactVersionId">Optional artifact-version UUID; derived deterministically when omitted.</param>
public sealed record KernelRegisterArtifactRequest(
    string? Kind,
    string? Title,
    string? ContentHash,
    long SizeBytes,
    string? MediaType,
    string? StorageRef,
    Guid? ArtifactId = null,
    Guid? ArtifactVersionId = null);

/// <summary>A study artifact and its latest immutable version.</summary>
/// <param name="ArtifactRef">Canonical NodeRef of the artifact (<c>artifact:&lt;uuid&gt;</c>).</param>
/// <param name="ArtifactId">Artifact UUID.</param>
/// <param name="ArtifactVersionRef">Canonical NodeRef of the latest artifact version (<c>artifact_version:&lt;uuid&gt;</c>).</param>
/// <param name="ArtifactVersionId">Latest artifact-version UUID.</param>
/// <param name="VersionNumber">1-based version number of the latest artifact version.</param>
/// <param name="Kind">Artifact kind (<c>EthicsApproval</c>, <c>RegulatoryApproval</c>, <c>CtaAcknowledgement</c>, etc.).</param>
/// <param name="StudyRef">Canonical NodeRef of the owning study, when study-scoped.</param>
/// <param name="Title">Human-readable artifact title.</param>
/// <param name="ContentHash">Canonical <c>sha256:</c> digest of the latest version.</param>
/// <param name="SizeBytes">Byte length of the latest version.</param>
/// <param name="MediaType">MIME type of the latest version.</param>
/// <param name="StorageRef">Object-storage reference of the latest version.</param>
/// <param name="RecordedAt">When the latest version was recorded, when read from persistence.</param>
public sealed record KernelArtifactResponse(
    string ArtifactRef,
    Guid ArtifactId,
    string ArtifactVersionRef,
    Guid ArtifactVersionId,
    int VersionNumber,
    string Kind,
    string? StudyRef,
    string Title,
    string ContentHash,
    long SizeBytes,
    string MediaType,
    string StorageRef,
    DateTimeOffset? RecordedAt)
{
    internal static KernelArtifactResponse From(StudyArtifactSummary summary) =>
        new(
            NodeRef.For(summary.ArtifactId).ToString(),
            summary.ArtifactId.Value,
            NodeRef.For(summary.LatestVersionId).ToString(),
            summary.LatestVersionId.Value,
            summary.LatestVersionNumber,
            summary.Kind.ToString(),
            NodeRef.For(summary.StudyId).ToString(),
            summary.Title,
            summary.ContentHash,
            summary.SizeBytes,
            summary.MediaType,
            summary.StorageRef,
            summary.RecordedAt);

    internal static KernelArtifactResponse From(ArtifactState state, long sizeBytes, string mediaType, string storageRef, DateTimeOffset? recordedAt) =>
        new(
            NodeRef.For(state.ArtifactId).ToString(),
            state.ArtifactId.Value,
            NodeRef.For(state.LatestVersionId).ToString(),
            state.LatestVersionId.Value,
            state.LatestVersionNumber,
            state.Kind.ToString(),
            state.StudyId is { } studyId ? NodeRef.For(studyId).ToString() : null,
            state.Title,
            state.LatestContentHash,
            sizeBytes,
            mediaType,
            storageRef,
            recordedAt);
}

/// <summary>Request body for <c>POST /api/v1/kernel/protocol-versions/{versionRef}/effectivities</c>.</summary>
/// <param name="ScopeLevel">Effectivity scope level: <c>Study</c>, <c>Country</c>, or <c>StudySite</c> (defaults to <c>Study</c>).</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code when <paramref name="ScopeLevel"/> is <c>Country</c>.</param>
/// <param name="StudySiteRef">Study-site NodeRef (<c>study_site:&lt;uuid&gt;</c>) or UUID when <paramref name="ScopeLevel"/> is <c>StudySite</c>.</param>
/// <param name="DeclaredAt">Optional valid-time timestamp when the effectivity was declared.</param>
/// <param name="EffectivityId">Optional effectivity UUID; derived deterministically when omitted.</param>
public sealed record KernelDeclareEffectivityRequest(
    string? ScopeLevel = null,
    string? Country = null,
    string? StudySiteRef = null,
    DateTimeOffset? DeclaredAt = null,
    Guid? EffectivityId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/effectivities/{effectivityRef}/status</c>.</summary>
/// <param name="Status">Target effectivity status (<c>RegulatoryApproved</c>, <c>EthicsApproved</c>, <c>Effective</c>, or <c>Withdrawn</c>).</param>
/// <param name="EvidenceArtifactVersionRefs">Artifact-version NodeRefs (<c>artifact_version:&lt;uuid&gt;</c>) or UUIDs evidencing the transition.</param>
/// <param name="EffectiveAt">Optional valid-time timestamp when the status took effect.</param>
public sealed record KernelChangeEffectivityStatusRequest(
    string? Status,
    IReadOnlyList<string>? EvidenceArtifactVersionRefs = null,
    DateTimeOffset? EffectiveAt = null);

/// <summary>A protocol-version effectivity record.</summary>
/// <param name="EffectivityRef">Canonical NodeRef of the effectivity (<c>effectivity:&lt;uuid&gt;</c>).</param>
/// <param name="EffectivityId">Effectivity UUID.</param>
/// <param name="StudyRef">Canonical NodeRef of the study, when known.</param>
/// <param name="ProtocolVersionRef">Canonical NodeRef of the governed protocol version (<c>protocol_version:&lt;uuid&gt;</c>).</param>
/// <param name="ProtocolVersionNumber">1-based protocol version number within the study, when known.</param>
/// <param name="ScopeLevel">Scope level (<c>Study</c>, <c>Country</c>, or <c>StudySite</c>).</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code for a country scope, otherwise null.</param>
/// <param name="StudySiteRef">Canonical NodeRef of the study site for a study-site scope, otherwise null.</param>
/// <param name="Status">Current lifecycle status (<c>Pending</c>, <c>RegulatoryApproved</c>, <c>EthicsApproved</c>, <c>Effective</c>, <c>Withdrawn</c>).</param>
/// <param name="StatusSince">When the current status took effect in valid time.</param>
/// <param name="EvidenceArtifactVersionRefs">Canonical NodeRefs of the artifact versions evidencing the current status.</param>
public sealed record KernelEffectivityResponse(
    string EffectivityRef,
    Guid EffectivityId,
    string? StudyRef,
    string ProtocolVersionRef,
    int? ProtocolVersionNumber,
    string ScopeLevel,
    string? Country,
    string? StudySiteRef,
    string Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<string> EvidenceArtifactVersionRefs)
{
    internal static KernelEffectivityResponse From(StudyEffectivitySummary summary) =>
        new(
            NodeRef.For(summary.EffectivityId).ToString(),
            summary.EffectivityId.Value,
            NodeRef.For(summary.StudyId).ToString(),
            NodeRef.For(summary.ProtocolVersionId).ToString(),
            summary.ProtocolVersionNumber > 0 ? summary.ProtocolVersionNumber : null,
            summary.Scope.Level.ToString(),
            summary.Scope.Country,
            summary.Scope.StudySiteId is { } siteId ? NodeRef.For(siteId).ToString() : null,
            summary.Status.ToString(),
            summary.StatusSince,
            [.. summary.Evidence.Select(id => NodeRef.For(id).ToString())]);

    internal static KernelEffectivityResponse From(EffectivityState state, StudyId? studyId = null, int? versionNumber = null) =>
        new(
            NodeRef.For(state.EffectivityId).ToString(),
            state.EffectivityId.Value,
            studyId is { } sid ? NodeRef.For(sid).ToString() : null,
            NodeRef.For(state.ProtocolVersionId).ToString(),
            versionNumber,
            state.Scope.Level.ToString(),
            state.Scope.Country,
            state.Scope.StudySiteId is { } siteId ? NodeRef.For(siteId).ToString() : null,
            state.Status.ToString(),
            state.StatusSince,
            [.. state.Evidence.Select(id => NodeRef.For(id).ToString())]);
}

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/sites</c>.</summary>
/// <param name="SiteCode">Site code to register (when new) and link to the study.</param>
/// <param name="SiteName">Display name when registering a new site (defaults to <paramref name="SiteCode"/>).</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code when registering a new site (defaults to <c>US</c>).</param>
/// <param name="InstitutionRef">Optional institution organization NodeRef (<c>organization:&lt;uuid&gt;</c>).</param>
/// <param name="SiteId">Optional site UUID when registering a new site.</param>
/// <param name="StudySiteId">Optional study-site UUID when linking to the study.</param>
public sealed record KernelLinkStudySiteRequest(
    string? SiteCode,
    string? SiteName = null,
    string? Country = null,
    string? InstitutionRef = null,
    Guid? SiteId = null,
    Guid? StudySiteId = null);

/// <summary>One effective protocol version governing a study site.</summary>
/// <param name="ProtocolVersionRef">Canonical NodeRef of the effective protocol version.</param>
/// <param name="Number">1-based protocol version number within the study.</param>
/// <param name="EffectivityRef">Canonical NodeRef of the deciding effectivity.</param>
/// <param name="ScopeLevel">Level of the deciding effectivity (<c>Study</c>, <c>Country</c>, or <c>StudySite</c>).</param>
public sealed record KernelEffectiveVersionResponse(
    string ProtocolVersionRef,
    int Number,
    string EffectivityRef,
    string ScopeLevel)
{
    internal static KernelEffectiveVersionResponse From(EffectiveVersion version) =>
        new(
            NodeRef.For(version.ProtocolVersionId).ToString(),
            version.Number,
            NodeRef.For(version.EffectivityId).ToString(),
            version.Level.ToString());
}

/// <summary>Summary of a study site in the Clinical Kernel and its effective protocol versions.</summary>
/// <param name="StudySiteRef">Canonical NodeRef of the study site (<c>study_site:&lt;uuid&gt;</c>).</param>
/// <param name="StudySiteId">Study-site UUID.</param>
/// <param name="StudyRef">Canonical NodeRef of the study, when known.</param>
/// <param name="SiteRef">Canonical NodeRef of the underlying site, when known.</param>
/// <param name="SiteCode">Site code, when known.</param>
/// <param name="SiteName">Site display name, when known.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code, when known.</param>
/// <param name="Status">Current study-site status (<c>Selected</c> or <c>Active</c>).</param>
/// <param name="EffectiveVersions">Protocol versions currently effective at this study site.</param>
public sealed record KernelStudySiteResponse(
    string StudySiteRef,
    Guid StudySiteId,
    string? StudyRef,
    string? SiteRef,
    string? SiteCode,
    string? SiteName,
    string? Country,
    string Status,
    IReadOnlyList<KernelEffectiveVersionResponse> EffectiveVersions)
{
    internal static KernelStudySiteResponse From(StudySiteGovernanceSummary summary) =>
        new(
            NodeRef.For(summary.StudySiteId).ToString(),
            summary.StudySiteId.Value,
            NodeRef.For(summary.StudyId).ToString(),
            NodeRef.For(summary.SiteId).ToString(),
            summary.SiteCode,
            summary.SiteName,
            summary.Country,
            summary.Status,
            [.. summary.EffectiveVersions.Select(KernelEffectiveVersionResponse.From)]);

    internal static KernelStudySiteResponse From(StudySiteState state, StudySiteGovernanceSummary? details = null) =>
        new(
            NodeRef.For(state.StudySiteId).ToString(),
            state.StudySiteId.Value,
            details is not null ? NodeRef.For(details.StudyId).ToString() : null,
            details is not null ? NodeRef.For(details.SiteId).ToString() : null,
            details?.SiteCode,
            details?.SiteName,
            details?.Country,
            state.Status,
            [.. state.EffectiveVersions.Select(KernelEffectiveVersionResponse.From)]);
}
