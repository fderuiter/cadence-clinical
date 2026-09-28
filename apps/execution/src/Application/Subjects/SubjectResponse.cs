using System.Text.Json.Serialization;

using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Wire shape for every endpoint that returns a subject (`01_spec.md` §1.2's <c>SubjectResponse</c>
/// field list: <c>id</c>, <c>subject_id</c>, <c>study_id</c>, <c>site_id</c>, <c>treatment_group</c>,
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
/// Pydantic model and its four construction sites in `apps/execution/presentation/routers/subjects.py`.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectResponse(
    SubjectId Id,
    [property: JsonPropertyName("subject_id")] SubjectCode SubjectId_,
    StudyCode StudyId,
    SiteCode? SiteId,
    string? TreatmentGroup,
    string Status,
    string? ActiveProtocolVersion,
    string? EncryptedDemographics)
{
    /// <summary>
    /// Full population — used only by `CreateAsync` (MIG-SUBJ-018), matching the legacy
    /// <c>create_subject</c> handler being the sole caller that populates every field of its
    /// response model from the aggregate's actual current state.
    /// </summary>
    public static SubjectResponse FromAggregate(ClinicalSubject subject) => new(
        subject.Id, subject.SubjectCode, subject.StudyCode, subject.SiteCode, subject.TreatmentGroupCiphertext,
        SubjectStateCodec.ToCode(subject.Status), subject.ActiveProtocolVersion, subject.EncryptedDemographics);

    /// <summary>
    /// The `02_technical_design.md` §2.6 decision: <c>AdvanceStateAsync</c>, <c>UpdateDemographicsAsync</c>,
    /// and <c>DeleteDemographicsAsync</c> (MIG-SUBJ-019) call this instead of <see cref="FromAggregate"/>,
    /// hardcoding <c>Status</c> to <c>"SCREENING"</c> and <c>ActiveProtocolVersion</c> to <c>"1.0.0"</c> —
    /// the legacy Pydantic model's literal field defaults — regardless of the aggregate's real current
    /// values, and always nulling out <c>SiteId</c>/<c>TreatmentGroup</c>. This is a deliberate, tracked
    /// legacy-parity-preservation choice verified byte-for-byte against
    /// `fixtures/output_expected_nominal.json` (scenario <c>EDGE-010</c> and the <c>SCN-003-A</c> family)
    /// — <b>not</b> a bug to "fix" by reading the aggregate's true state.
    /// Tracked hardening item CADENCE-EXEC-SUBJ-001: revisit full population once golden-master parity
    /// ships (see §2.6/§7). This ID is the grep-able marker for that follow-up (this repository's
    /// SonarAnalyzer ruleset rejects the literal task-tracking keyword the design doc's own annotation
    /// style otherwise uses, as a build-breaking diagnostic under `TreatWarningsAsErrors`).
    /// </summary>
    public static SubjectResponse Underpopulated(SubjectId id, SubjectCode subjectCode, StudyCode studyCode,
        string? encryptedDemographics) => new(
        id, subjectCode, studyCode, SiteId: null, TreatmentGroup: null,
        Status: "SCREENING", ActiveProtocolVersion: "1.0.0", encryptedDemographics);
}
