using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel.Execution;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Api;

/// <summary>Summary of a registered site returned by <c>/api/v1/kernel/sites</c>.</summary>
public sealed record KernelSiteSummaryResponse(
    string Ref,
    string SiteCode,
    string Name,
    string Country,
    string InstitutionRef,
    string InstitutionCode,
    string InstitutionName)
{
    internal static KernelSiteSummaryResponse From(SiteDirectoryEntry entry) => new(
        NodeRef.For(entry.SiteId, entry.Version).ToString(),
        entry.SiteCode,
        entry.Name,
        entry.Country,
        NodeRef.For(entry.InstitutionId).ToString(),
        entry.InstitutionCode,
        entry.InstitutionName);
}

/// <summary>Request body for <c>POST /api/v1/kernel/sites</c>.</summary>
/// <param name="SiteCode">Site code unique within the institution.</param>
/// <param name="Name">Human-readable site name.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code (e.g. <c>US</c>, <c>NL</c>).</param>
/// <param name="InstitutionRef">Optional existing institution <c>organization:&lt;uuid&gt;</c> NodeRef.</param>
/// <param name="InstitutionCode">Optional institution code when registering or resolving an institution.</param>
/// <param name="InstitutionName">Optional institution name when auto-registering an institution.</param>
/// <param name="SiteId">Optional caller-supplied site identifier.</param>
public sealed record KernelRegisterSiteRequest(
    string SiteCode,
    string Name,
    string Country,
    string? InstitutionRef = null,
    string? InstitutionCode = null,
    string? InstitutionName = null,
    Guid? SiteId = null);

/// <summary>Effective protocol version summary on a study site.</summary>
public sealed record KernelEffectiveProtocolVersionResponse(
    string VersionRef,
    int Number,
    string ScopeLevel);

/// <summary>Summary of a site linked to a study (<c>StudySite</c>).</summary>
public sealed record KernelStudySiteSummaryResponse(
    string Ref,
    string StudyRef,
    string StudyCode,
    string SiteRef,
    string SiteCode,
    string SiteName,
    string Country,
    string InstitutionRef,
    string InstitutionName,
    string Status,
    IReadOnlyList<KernelEffectiveProtocolVersionResponse> EffectiveVersions)
{
    internal static KernelStudySiteSummaryResponse From(
        StudySiteDirectoryEntry entry,
        IReadOnlyList<EffectiveVersion> effective) => new(
        NodeRef.For(entry.StudySiteId, entry.Version).ToString(),
        NodeRef.For(entry.StudyId).ToString(),
        entry.StudyCode,
        NodeRef.For(entry.SiteId).ToString(),
        entry.SiteCode,
        entry.SiteName,
        entry.Country,
        NodeRef.For(entry.InstitutionId).ToString(),
        entry.InstitutionName,
        entry.Status,
        [.. effective.Select(e => new KernelEffectiveProtocolVersionResponse(
            NodeRef.For(e.ProtocolVersionId).ToString(),
            e.Number,
            e.Level.ToString()))]);
}

/// <summary>Request body for <c>POST /api/v1/kernel/studies/{studyRef}/sites</c>.</summary>
/// <param name="SiteRef">Registered site <c>site:&lt;uuid&gt;</c> NodeRef or site code.</param>
/// <param name="StudySiteId">Optional caller-supplied study-site identifier.</param>
public sealed record KernelLinkSiteToStudyRequest(
    string SiteRef,
    Guid? StudySiteId = null);

/// <summary>Summary of one immutable version of an evidence artifact.</summary>
public sealed record KernelArtifactVersionSummaryResponse(
    string Ref,
    int Number,
    string ContentHash,
    long SizeBytes,
    string MediaType,
    string StorageRef,
    DateTimeOffset RecordedAt)
{
    internal static KernelArtifactVersionSummaryResponse From(ArtifactVersionDirectoryEntry entry) => new(
        NodeRef.For(entry.VersionId).ToString(),
        entry.Number,
        entry.ContentHash,
        entry.SizeBytes,
        entry.MediaType,
        entry.StorageRef,
        entry.RecordedAt);
}

/// <summary>Summary of an evidence artifact and its immutable versions.</summary>
public sealed record KernelArtifactSummaryResponse(
    string Ref,
    string Kind,
    string? StudyRef,
    string Title,
    KernelArtifactVersionSummaryResponse LatestVersion,
    IReadOnlyList<KernelArtifactVersionSummaryResponse> Versions)
{
    internal static KernelArtifactSummaryResponse From(ArtifactDirectoryEntry entry) => new(
        NodeRef.For(entry.ArtifactId, entry.Version).ToString(),
        entry.Kind.ToString(),
        entry.StudyId is { } studyId ? NodeRef.For(studyId).ToString() : null,
        entry.Title,
        KernelArtifactVersionSummaryResponse.From(entry.LatestVersion),
        [.. entry.Versions.Select(KernelArtifactVersionSummaryResponse.From)]);
}

/// <summary>Request body for <c>POST /api/v1/kernel/artifacts</c>.</summary>
/// <param name="Kind">Artifact kind (<c>EthicsApproval</c>, <c>RegulatoryApproval</c>, <c>CtaAcknowledgement</c>, etc.).</param>
/// <param name="Title">Human-readable artifact title (1..500 chars).</param>
/// <param name="StudyRef">Optional study <c>NodeRef</c> or study code; null for tenant-wide evidence.</param>
/// <param name="ContentHash">Canonical <c>sha256:&lt;64-lower-hex&gt;</c> digest; if omitted and <paramref name="DocumentText"/> is provided, computed automatically.</param>
/// <param name="SizeBytes">Payload byte count; computed from <paramref name="DocumentText"/> when omitted.</param>
/// <param name="MediaType">IANA media type (defaults to <c>application/pdf</c> or <c>text/plain</c>).</param>
/// <param name="StorageRef">Object storage reference URI/key (auto-derived when omitted).</param>
/// <param name="DocumentText">Optional plain-text summary/reference whose SHA-256 is hashed when <paramref name="ContentHash"/> is omitted.</param>
/// <param name="ArtifactId">Optional caller-supplied artifact identifier.</param>
/// <param name="ArtifactVersionId">Optional caller-supplied artifact version identifier.</param>
public sealed record KernelRegisterArtifactRequest(
    string Kind,
    string Title,
    string? StudyRef = null,
    string? ContentHash = null,
    long? SizeBytes = null,
    string? MediaType = null,
    string? StorageRef = null,
    string? DocumentText = null,
    Guid? ArtifactId = null,
    Guid? ArtifactVersionId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/artifacts/{artifactRef}/versions</c>.</summary>
/// <param name="ContentHash">Canonical <c>sha256:&lt;64-lower-hex&gt;</c> digest; computed from <paramref name="DocumentText"/> when omitted.</param>
/// <param name="SizeBytes">Payload byte count.</param>
/// <param name="MediaType">IANA media type.</param>
/// <param name="StorageRef">Object storage reference URI/key.</param>
/// <param name="DocumentText">Optional plain-text summary/reference whose SHA-256 is hashed when <paramref name="ContentHash"/> is omitted.</param>
/// <param name="ArtifactVersionId">Optional caller-supplied artifact version identifier.</param>
public sealed record KernelAddArtifactVersionRequest(
    string? ContentHash = null,
    long? SizeBytes = null,
    string? MediaType = null,
    string? StorageRef = null,
    string? DocumentText = null,
    Guid? ArtifactVersionId = null);

/// <summary>Summary of a protocol effectivity declaration.</summary>
public sealed record KernelEffectivitySummaryResponse(
    string Ref,
    string StudyRef,
    string ProtocolVersionRef,
    int ProtocolVersionNumber,
    string ScopeLevel,
    string? ScopeCountry,
    string? ScopeStudySiteRef,
    string Status,
    DateTimeOffset StatusSince,
    IReadOnlyList<string> EvidenceVersionRefs)
{
    internal static KernelEffectivitySummaryResponse From(EffectivityDirectoryEntry entry) => new(
        NodeRef.For(entry.EffectivityId, entry.Version).ToString(),
        NodeRef.For(entry.StudyId).ToString(),
        NodeRef.For(entry.ProtocolVersionId).ToString(),
        entry.ProtocolVersionNumber,
        entry.Scope.Level.ToString(),
        entry.Scope.Country,
        entry.Scope.StudySiteId is { } siteId ? NodeRef.For(siteId).ToString() : null,
        entry.Status.ToString(),
        entry.StatusSince,
        [.. entry.Evidence.Select(id => NodeRef.For(id).ToString())]);
}

/// <summary>Request body for <c>POST /api/v1/kernel/effectivities</c>.</summary>
/// <param name="ProtocolVersionRef">Target <c>protocol_version:&lt;uuid&gt;</c> NodeRef.</param>
/// <param name="ScopeLevel">Scope level: <c>Study</c>, <c>Country</c>, or <c>StudySite</c>.</param>
/// <param name="Country">ISO 3166-1 alpha-2 country code when <paramref name="ScopeLevel"/> is <c>Country</c>.</param>
/// <param name="StudySiteRef">Target <c>study_site:&lt;uuid&gt;</c> NodeRef when <paramref name="ScopeLevel"/> is <c>StudySite</c>.</param>
/// <param name="DeclaredAt">Optional valid-time declaration timestamp; defaults to shortly before the kernel clock instant.</param>
/// <param name="EffectivityId">Optional caller-supplied effectivity identifier.</param>
public sealed record KernelDeclareEffectivityRequest(
    string ProtocolVersionRef,
    string ScopeLevel,
    string? Country = null,
    string? StudySiteRef = null,
    DateTimeOffset? DeclaredAt = null,
    Guid? EffectivityId = null);

/// <summary>Request body for <c>POST /api/v1/kernel/effectivities/{effectivityRef}/status</c>.</summary>
/// <param name="Status">Target status (<c>Effective</c>, <c>Superseded</c>, <c>Ended</c>).</param>
/// <param name="EvidenceVersionRefs">Artifact version <c>artifact_version:&lt;uuid&gt;</c> NodeRefs evidencing the status change.</param>
/// <param name="EffectiveAt">Optional valid-time transition timestamp; defaults to the kernel clock instant.</param>
public sealed record KernelChangeEffectivityStatusRequest(
    string Status,
    IReadOnlyList<string>? EvidenceVersionRefs = null,
    DateTimeOffset? EffectiveAt = null);

/// <summary>Request body for <c>POST /api/v1/kernel/study-sites/activate-by-code</c>.</summary>
/// <param name="StudyCode">Canonical study code.</param>
/// <param name="SiteCode">Canonical site code.</param>
public sealed record KernelActivateStudySiteByCodeRequest(
    string StudyCode,
    string SiteCode);
