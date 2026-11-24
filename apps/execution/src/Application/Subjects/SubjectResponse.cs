using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Wire shape for every endpoint that returns a subject (`01_spec.md` §1.2's <c>SubjectResponse</c>
/// field list: <c>id</c>, <c>subject_id</c>, <c>study_id</c>, <c>site_id</c>,
/// <c>status</c>, <c>active_protocol_version</c>, <c>encrypted_demographics</c>). Property names carry
/// the wire's PascalCase-of-the-snake_case-name form so the shared <see cref="System.Text.Json.JsonNamingPolicy"/>
/// registered once in `Program.cs` (MIG-SUBJ-008, `02_technical_design.md` §2.6) maps them automatically
/// (e.g. <c>StudyId</c> → <c>study_id</c>). <c>SubjectId_</c> is the sole exception: it carries the
/// domain subject code (legacy <c>subject_id</c> field) but is spelled with a trailing underscore so it
/// cannot be confused with — or collide in review/IntelliSense with — this same record's <c>Id</c>
/// property, which is of the unrelated surrogate-key type <see cref="SubjectId"/>; it therefore needs
/// (and carries) an explicit <see cref="JsonPropertyNameAttribute"/> override rather than relying on the
/// naming policy. No other response record in this slice has both an <see cref="SubjectId"/>-typed
/// property and a domain-code property in the same type, so none of them need this treatment
/// (see <see cref="SubjectDetailResponse"/>, which names its domain-code property plain <c>SubjectId</c>).
/// Legacy Python source being replaced: `apps/execution/presentation/dtos.py`'s <c>SubjectResponse</c>
/// Pydantic model and its four construction sites in `apps/execution/presentation/routers/subjects.py`. It carries no
/// treatment arm: the arm lives only encrypted on the randomisation allocation, and only subject detail reveals it, to an
/// unblinded caller (#5607).
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectResponse(
    SubjectId Id,
    [property: JsonPropertyName("subject_id")] SubjectCode SubjectId_,
    StudyCode StudyId,
    SiteCode? SiteId,
    string Status,
    string? ActiveProtocolVersion,
    string? EncryptedDemographics,
    string? EnrollmentRef = null)
{
    /// <summary>
    /// Full population — used only by `CreateAsync` (MIG-SUBJ-018), matching the legacy
    /// <c>create_subject</c> handler being the sole caller that populates every field of its
    /// response model from the aggregate's actual current state.
    /// </summary>
    public static SubjectResponse FromAggregate(ClinicalSubject subject) => new(
        subject.Id, subject.SubjectCode, subject.StudyCode, subject.SiteCode,
        SubjectStateCodec.ToCode(subject.Status), subject.ActiveProtocolVersion, subject.EncryptedDemographics,
        subject.EnrollmentRef);
}
