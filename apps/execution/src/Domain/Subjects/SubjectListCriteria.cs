using System.Collections.Generic;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Which subjects of one study to list, and which page. <see cref="SiteCode"/> and <see cref="Status"/> narrow
/// the list when set. Subjects come back in enrollment order.
/// </summary>
public sealed record SubjectListCriteria(StudyCode StudyCode, SiteCode? SiteCode, SubjectState? Status, int Limit, int Offset);

/// <summary>One page of subjects and the number of subjects that match the criteria across all pages.</summary>
public sealed record SubjectPage(IReadOnlyList<ClinicalSubject> Items, int Total);
