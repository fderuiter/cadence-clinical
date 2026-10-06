using System;
using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Status of a subject batch import job.
/// </summary>
public enum SubjectBatchStatus
{
    Pending = 0,
    Validating = 1,
    Validated = 2,
    Committed = 3,
    Failed = 4
}

/// <summary>
/// Status of an individual row in a subject batch import job.
/// </summary>
public enum SubjectBatchRowStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    Committed = 3,
    Failed = 4
}

/// <summary>
/// Aggregate root for asynchronous subject batch CSV ingestion.
/// Manages staging and pre-enrollment validation queue state across study sites.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectBatchImport : AggregateRoot<Guid>
{
    public string BatchId { get; private set; } = default!;
    public StudyCode StudyCode { get; private set; } = default!;
    public string FileName { get; private set; } = default!;
    public int TotalRows { get; private set; }
    public int ValidRows { get; private set; }
    public int InvalidRows { get; private set; }
    public SubjectBatchStatus Status { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CommittedAt { get; private set; }

    private readonly List<SubjectBatchRow> _rows = new();
    public IReadOnlyCollection<SubjectBatchRow> Rows => _rows.AsReadOnly();

    private SubjectBatchImport() { } // EF Core

    public static SubjectBatchImport Create(
        string batchId,
        StudyCode studyCode,
        string fileName,
        string createdBy,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(batchId);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(createdBy);

        var batch = new SubjectBatchImport
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            StudyCode = studyCode,
            FileName = fileName,
            Status = SubjectBatchStatus.Pending
        };

        batch.CreatedBy = createdBy;
        batch.CreatedAt = createdAt.UtcDateTime;

        return batch;
    }

    public SubjectBatchRow AddRow(
        int rowNumber,
        string subjectId,
        SiteCode? siteCode,
        string? armId,
        string? name,
        string? birthdate,
        string? gender,
        string? race)
    {
        var row = SubjectBatchRow.Create(
            Id,
            rowNumber,
            subjectId,
            siteCode,
            armId,
            name,
            birthdate,
            gender,
            race);

        _rows.Add(row);
        TotalRows = _rows.Count;
        return row;
    }

    public void MarkValidating()
    {
        Status = SubjectBatchStatus.Validating;
    }

    public void MarkValidationCompleted(int validCount, int invalidCount, DateTimeOffset completedAt)
    {
        ValidRows = validCount;
        InvalidRows = invalidCount;
        CompletedAt = completedAt;
        Status = SubjectBatchStatus.Validated;
    }

    public void MarkFailed(string reason, DateTimeOffset completedAt)
    {
        CompletedAt = completedAt;
        Status = SubjectBatchStatus.Failed;
    }

    public void MarkCommitted(DateTimeOffset committedAt)
    {
        CommittedAt = committedAt;
        Status = SubjectBatchStatus.Committed;
    }
}

/// <summary>
/// Individual row entity staged within a subject batch import job.
/// Holds pre-enrollment validation status and error details.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectBatchRow
{
    public Guid Id { get; private set; }
    public Guid BatchImportId { get; private set; }
    public int RowNumber { get; private set; }
    public string SubjectId { get; private set; } = default!;
    public SiteCode? SiteCode { get; private set; }
    public string? ArmId { get; private set; }
    public string? Name { get; private set; }
    public string? Birthdate { get; private set; }
    public string? Gender { get; private set; }
    public string? Race { get; private set; }
    public SubjectBatchRowStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }

    private SubjectBatchRow() { } // EF Core

    public static SubjectBatchRow Create(
        Guid batchImportId,
        int rowNumber,
        string subjectId,
        SiteCode? siteCode,
        string? armId,
        string? name,
        string? birthdate,
        string? gender,
        string? race)
    {
        return new SubjectBatchRow
        {
            Id = Guid.NewGuid(),
            BatchImportId = batchImportId,
            RowNumber = rowNumber,
            SubjectId = subjectId,
            SiteCode = siteCode,
            ArmId = armId,
            Name = name,
            Birthdate = birthdate,
            Gender = gender,
            Race = race,
            Status = SubjectBatchRowStatus.Pending
        };
    }

    public void MarkValid()
    {
        Status = SubjectBatchRowStatus.Valid;
        ErrorMessage = null;
    }

    public void MarkInvalid(string errorMessage)
    {
        Status = SubjectBatchRowStatus.Invalid;
        ErrorMessage = errorMessage;
    }

    public void MarkCommitted()
    {
        Status = SubjectBatchRowStatus.Committed;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = SubjectBatchRowStatus.Failed;
        ErrorMessage = errorMessage;
    }
}
