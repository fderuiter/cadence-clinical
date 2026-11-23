using System.Collections.Generic;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Which subjects of one study to list, and which page. <see cref="SiteCode"/>, <see cref="Status"/>, and
/// <see cref="Query"/> narrow the list when set. Subjects come back in enrollment order.
/// </summary>
public sealed record SubjectListCriteria(
    StudyCode StudyCode,
    SiteCode? SiteCode,
    SubjectState? Status,
    int Limit,
    int Offset,
    IReadOnlyCollection<SiteCode>? AllowedSites = null,
    string? Query = null);

/// <summary>One page of subjects and the number of subjects that match the criteria across all pages.</summary>
public sealed record SubjectPage(IReadOnlyList<ClinicalSubject> Items, int Total);

/// <summary>
/// Which subjects of one study to count by <see cref="SubjectState"/>. <see cref="SiteCode"/> or
/// <see cref="AllowedSites"/> narrows the count to permitted sites when set.
/// </summary>
public sealed record SubjectCountCriteria(
    StudyCode StudyCode,
    SiteCode? SiteCode = null,
    IReadOnlyCollection<SiteCode>? AllowedSites = null);
