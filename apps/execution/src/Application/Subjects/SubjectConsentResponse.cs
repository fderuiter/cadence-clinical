using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Application.Subjects;

/// <summary>
/// Wire shape for consent read/record endpoints (`01_spec.md` §1.2's <c>SubjectConsentResponse</c>
/// field list). Full population, no under-population quirk — mirrors the <see cref="SubjectConsent"/>
/// aggregate's fields one-for-one.
/// Legacy Python source being replaced: `apps/execution/presentation/dtos.py`'s
/// <c>SubjectConsentResponse</c> Pydantic model.
/// @req:PRD-SUB-001
/// </summary>
public sealed record SubjectConsentResponse(SubjectConsentId Id, SubjectCode SubjectId, StudyCode StudyId,
    string VersionTag, int VersionIndex, bool IcfSigned, DateTimeOffset? IcfSignedDate,
    bool RequiresReconsent, bool IsPaperOverride, long Version);
