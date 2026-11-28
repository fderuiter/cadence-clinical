using System;
using System.Collections.Generic;

using Cadence.Apps.Interop.Domain;

namespace Cadence.Apps.Interop.Endpoints;

public record EhrEndpointCreateRequest(
    string Name,
    string? SystemType,
    string FhirBaseUrl,
    string? TokenUrl,
    string? ClientId,
    string? PublicKeyJwksUrl,
    List<string>? Scopes,
    int PollingIntervalSec,
    string? ReasonForChange,
    string? SigningKeyRef = null,
    string? SigningKeyId = null);

public record EhrEndpointListResponse(
    List<EhrEndpointRecord> Endpoints);

/// <summary>Asks for a client assertion for a registered endpoint; the key is the endpoint's, never the caller's.</summary>
public record SmartAssertionRequest(
    string EndpointId,
    int ExpirationSeconds = 300);

public record SmartAssertionResponse(
    string ClientAssertionToken,
    string ClientAssertionType,
    DateTimeOffset ExpiresAt);

public record SmartVerifyRequest(
    string ClientAssertionToken,
    string EndpointId);

public record SmartVerifyResponse(
    bool IsValid,
    string Subject,
    string Issuer,
    string Audience,
    DateTimeOffset ExpiresAt,
    string? ErrorMessage);

public record EhrPollRequest(
    string? EndpointId,
    string StudyId,
    string? SiteId,
    string SubjectId);

public record FhirBundleIngestRequest(
    string StudyId,
    string? SiteId,
    string SubjectId,
    string? SubjectPseudonym,
    string? EhrSystemId,
    string RawFhirBundleJson);

public record FhirBundleIngestResponse(
    bool Success,
    int CandidateCount,
    List<EhrCandidateItemDto> Candidates,
    string Message);

public record EhrCandidateListResponse(
    List<EhrCandidateItemDto> Candidates,
    int TotalCount);

public record EhrCandidateItemDto(
    string Id, string StudyId, string SiteId, string SubjectId, string SubjectPseudonym,
    string EhrSystemId, string FhirResourceType, string FhirResourceId, DateTimeOffset SourceTimestamp,
    string TargetDomain, string TargetVariable, string? CdashTestcd, string? CdashTest,
    object CandidateValue, string? CandidateUnit, object? CurrentEcrfValue, DateTimeOffset? CurrentEcrfUpdatedAt,
    string DiscrepancyType, double ConfidenceScore, string MappingProvenance, object RawFhirResource,
    string Status, string? ResolutionAction, string? ResolutionReason, string? ResolvedBy, DateTimeOffset? ResolvedAt,
    object? ElectronicSignature, string? ClinicalQueryId, DateTimeOffset CreatedAt, int VersionIndex);

public record EhrReconcileAcceptRequest(
    string CandidateId,
    string? CommitReason,
    string? ChangeReason);

public record EhrReconcileBatchAcceptRequest(
    List<string> CandidateIds,
    string? CommitReason,
    string? ChangeReason);

public record EhrReconcileRejectRequest(
    string CandidateId,
    string RejectionReason,
    string? ChangeReason);

public record EhrReconcileBatchRejectRequest(
    List<string> CandidateIds,
    string RejectionReason,
    string? ChangeReason);

public record EhrReconcileQueryRequest(
    string CandidateId,
    string QueryExplanation,
    string? DiscrepancyType,
    string? ChangeReason);

public record EhrReconcileActionResponse(
    bool Success,
    EhrCandidateItemDto Candidate,
    string? ClinicalQueryId,
    string Message);

public record EhrReconcileBatchActionResponse(
    bool Success,
    int ActionCount,
    List<EhrCandidateItemDto> Candidates,
    string Message);

public record EhrProvenanceResponse(
    string CandidateId, string StudyId, string SiteId, string SubjectId, string SubjectPseudonym,
    string EhrSystemId, string FhirResourceType, string FhirResourceId, DateTimeOffset? SourceTimestamp,
    string TargetDomain, string TargetVariable, string? CdashTestcd, string? CdashTest,
    object CandidateValue, string? CandidateUnit, object? CurrentEcrfValue, DateTimeOffset? CurrentEcrfUpdatedAt,
    string DiscrepancyType, double ConfidenceScore, string MappingProvenance, object RawFhirResource,
    string Status, string? ResolutionAction, string? ResolutionReason, string? ResolvedBy, DateTimeOffset? ResolvedAt,
    object? ElectronicSignature, string? ClinicalQueryId, DateTimeOffset CreatedAt, int VersionIndex,
    List<InteropAuditLog> AuditTrail);
