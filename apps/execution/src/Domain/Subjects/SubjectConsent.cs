using System;

using Cadence.Core.Domain;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// The consent aggregate (legacy <c>SubjectConsent</c> ORM model), including the field-by-field
/// (not full-overwrite) merge semantics on re-submission (Rule 9) and the re-consent
/// cascade-clearance primitive (Rule 3, invoked per-row by `MIG-SUBJ-018`'s application service).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectConsent : AggregateRoot<SubjectConsentId>
{
    public SubjectCode SubjectCode { get; private set; } = default!;
    public ProtocolVersionRef ProtocolVersion { get; private set; } = default!;
    public bool IcfSigned { get; private set; }
    public DateTimeOffset? IcfSignedDate { get; private set; }
    public bool RequiresReconsent { get; private set; }
    public bool IsPaperOverride { get; private set; }

    private SubjectConsent() { }

    public static SubjectConsent Create(SubjectCode subjectCode, ProtocolVersionRef version, bool icfSigned,
        DateTimeOffset? icfSignedDate, bool requiresReconsent, bool isPaperOverride, IClock clock) => new()
        {
            Id = SubjectConsentId.New(),
            SubjectCode = subjectCode,
            ProtocolVersion = version,
            IcfSigned = icfSigned,
            IcfSignedDate = icfSignedDate ?? clock.UtcNow,
            RequiresReconsent = requiresReconsent,
            IsPaperOverride = isPaperOverride,
        };

    /// <summary>Rule 9 field-merge-on-resubmission semantics — NOT a full overwrite. `versionTag`/`icfSigned`/
    /// `requiresReconsent`/`isPaperOverride` always replace; `icfSignedDate` only replaces when supplied
    /// (defaults to now only if there was no prior date at all); `incomingProtocolVersion` only replaces the
    /// stored `ProtocolVersion` when non-null (an omitted resubmission value leaves the stored value untouched,
    /// it is not nulled out).</summary>
    public void MergeResubmission(string versionTag, bool icfSigned, DateTimeOffset? icfSignedDate,
        bool requiresReconsent, bool isPaperOverride, ProtocolVersionRef? incomingProtocolVersion, IClock clock)
    {
        ProtocolVersion = ProtocolVersion with { VersionTag = versionTag };
        IcfSigned = icfSigned;
        RequiresReconsent = requiresReconsent;
        IsPaperOverride = isPaperOverride;
        IcfSignedDate = icfSignedDate ?? IcfSignedDate ?? clock.UtcNow;
        if (incomingProtocolVersion is not null)
        {
            ProtocolVersion = incomingProtocolVersion;
        }
    }

    /// <summary>Rule 3 cascade target — called on every *other* consent row for the same subject when a new
    /// consent is recorded with `requiresReconsent == false`.</summary>
    public void ClearReconsentRequirement() => RequiresReconsent = false;
}
