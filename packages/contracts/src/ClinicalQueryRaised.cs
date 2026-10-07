using System;

namespace Cadence.Contracts;

/// <summary>
/// Domain event published when a clinical query is raised in EDC Execution.
/// Written to the transactional outbox table for at-least-once downstream delivery.
/// @req:PRD-SYS-001
/// </summary>
/// <param name="QueryId">Unique identifier of the raised query.</param>
/// <param name="StudyId">Study identifier.</param>
/// <param name="SubjectId">Subject identifier.</param>
/// <param name="SiteId">Optional site identifier.</param>
/// <param name="TestCode">Test code associated with the query.</param>
/// <param name="Explanation">Detailed explanation/reason for raising the query.</param>
/// <param name="RaisedBy">User or process identifier that raised the query.</param>
/// <param name="RaisedAt">Timestamp when the query was raised.</param>
/// <param name="QueryType">Optional classification or type of query.</param>
/// <param name="Priority">Optional priority level of the query.</param>
public sealed record ClinicalQueryRaised(
    string QueryId,
    string StudyId,
    string SubjectId,
    string? SiteId,
    string TestCode,
    string Explanation,
    string RaisedBy,
    DateTimeOffset RaisedAt,
    string? QueryType = null,
    string? Priority = null
);
