using System;
using System.Collections.Generic;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

public sealed record SubjectBatchUploadResponse(
    string BatchId,
    StudyCode StudyId,
    string FileName,
    int TotalRows,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record SubjectBatchRowResponse(
    int RowNumber,
    string SubjectId,
    SiteCode? SiteId,
    string? ArmId,
    string? Name,
    string? Birthdate,
    string? Gender,
    string? Race,
    string Status,
    string? ErrorMessage);

public sealed record SubjectBatchDetailResponse(
    string BatchId,
    StudyCode StudyId,
    string FileName,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CommittedAt,
    IReadOnlyList<SubjectBatchRowResponse> Rows);

public sealed record SubjectBatchCommitResponse(
    string BatchId,
    int CommittedCount,
    IReadOnlyList<string> SubjectCodes,
    DateTimeOffset CommittedAt);
