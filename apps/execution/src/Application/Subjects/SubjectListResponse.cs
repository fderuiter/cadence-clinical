using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// One subject in a subject list. Carries no treatment group and no demographics, so the list is safe to show a
/// blinded caller; <c>GET /api/v1/execution/subjects/{subjectId}</c> returns the detail.
/// </summary>
public sealed record SubjectSummaryResponse(
    SubjectId Id,
    [property: JsonPropertyName("subject_id")] SubjectCode SubjectId_,
    StudyCode StudyId,
    SiteCode? SiteId,
    string Status,
    string ActiveProtocolVersion,
    int EnrollmentIndex)
{
    public static SubjectSummaryResponse FromAggregate(ClinicalSubject subject) => new(
        subject.Id, subject.SubjectCode, subject.StudyCode, subject.SiteCode, SubjectStateCodec.ToCode(subject.Status),
        subject.ActiveProtocolVersion, subject.EnrollmentIndex);
}

/// <summary>One page of a study's subjects. <see cref="Total"/> counts every subject that matches the filters.</summary>
public sealed record SubjectListResponse(IReadOnlyList<SubjectSummaryResponse> Items, int Total, int Limit, int Offset);

/// <summary>
/// Participant counts for one study grouped by lifecycle status code (<see cref="SubjectStateCodec.ToCode"/>), plus
/// the total across all statuses.
/// </summary>
public sealed record SubjectCountsResponse(int Total, IReadOnlyDictionary<string, int> Counts);
