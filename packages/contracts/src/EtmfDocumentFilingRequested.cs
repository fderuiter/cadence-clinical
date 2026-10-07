using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when an eTMF document filing is requested across services.
/// Written to the transactional outbox table for at-least-once downstream delivery.
/// @req:PRD-SYS-001
/// </summary>
/// <param name="DocumentId">Unique identifier for the document filing request.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="SiteId">Optional site identifier.</param>
/// <param name="ZoneCode">TMF Reference Model Zone code.</param>
/// <param name="SectionCode">TMF Reference Model Section code.</param>
/// <param name="ArtifactCode">TMF Reference Model Artifact code.</param>
/// <param name="DocumentTitle">Human-readable document title.</param>
/// <param name="StoragePath">Storage location path for the artifact.</param>
/// <param name="RequestedBy">User or system entity requesting filing.</param>
/// <param name="RequestedAt">Timestamp when filing was requested.</param>
public sealed record EtmfDocumentFilingRequested(
    string DocumentId,
    string StudyId,
    string? SiteId,
    string ZoneCode,
    string SectionCode,
    string ArtifactCode,
    string DocumentTitle,
    string StoragePath,
    string RequestedBy,
    DateTimeOffset RequestedAt
);
