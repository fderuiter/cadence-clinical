using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// One subject in a subject list. Carries no treatment group and no demographics, so the list is safe to show a
/// blinded caller; <c>GET /api/v1/execution/subjects/{subjectId}</c> returns the detail.
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "The trailing underscore keeps the property apart from the SubjectId type of Id; the JSON name is subject_id.")]
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
