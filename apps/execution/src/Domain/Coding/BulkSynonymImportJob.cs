using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Coding;

/// <summary>
/// Domain record tracking bulk synonym dictionary import execution and GxP audit lineage.
/// @req:PRD-SYS-001
/// </summary>
public sealed class BulkSynonymImportJob : IGxPAuditable
{
    public Guid Id { get; private set; }
    public string Scope { get; private set; } = "STUDY"; // "STUDY" | "ENTERPRISE"
    public string? StudyId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string FileHash { get; private set; } = string.Empty; // SHA256 hex
    public int TotalRows { get; private set; }
    public int ValidRows { get; private set; }
    public int DuplicateRows { get; private set; }
    public int InvalidRows { get; private set; }
    public string Status { get; private set; } = "COMPLETED"; // "COMPLETED" | "FAILED" | "PREFLIGHT"

    // IGxPAuditable properties
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string ReasonForChange { get; set; } = string.Empty;
    public long VersionIndex { get; set; } = 1;

    private BulkSynonymImportJob() { } // EF Core constructor

    public BulkSynonymImportJob(
        Guid id,
        string scope,
        string? studyId,
        string fileName,
        string fileHash,
        int totalRows,
        int validRows,
        int duplicateRows,
        int invalidRows,
        string status,
        string createdBy,
        string reasonForChange,
        DateTime createdAt)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Scope = (scope ?? "STUDY").Trim().ToUpperInvariant();
        StudyId = string.IsNullOrWhiteSpace(studyId) ? null : studyId.Trim();
        FileName = fileName?.Trim() ?? string.Empty;
        FileHash = fileHash?.Trim() ?? string.Empty;
        TotalRows = totalRows;
        ValidRows = validRows;
        DuplicateRows = duplicateRows;
        InvalidRows = invalidRows;
        Status = string.IsNullOrWhiteSpace(status) ? "COMPLETED" : status.Trim().ToUpperInvariant();
        CreatedBy = createdBy?.Trim() ?? string.Empty;
        ReasonForChange = reasonForChange?.Trim() ?? string.Empty;
        CreatedAt = createdAt;
        VersionIndex = 1;
    }
}
