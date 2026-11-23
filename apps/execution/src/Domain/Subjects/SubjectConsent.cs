using System;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// The consent aggregate (legacy <c>SubjectConsent</c> ORM model), including the field-by-field
/// (not full-overwrite) merge semantics on re-submission (Rule 9) and the re-consent
/// cascade-clearance primitive (Rule 3, invoked per-row by `MIG-SUBJ-018`'s application service).
/// A signed consent always carries the date it was signed: the aggregate never invents one (#4824), so
/// every path that would leave <see cref="IcfSigned"/> true with no <see cref="IcfSignedDate"/> throws.
/// Callers check first and report a validation error (<see cref="SubjectErrors.SignedConsentDateRequired"/>).
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectConsent : AggregateRoot<SubjectConsentId>
{
    public Guid TenantId { get; internal set; }
    public SubjectCode SubjectCode { get; private set; } = default!;
    public ProtocolVersionRef ProtocolVersion { get; private set; } = default!;
    public bool IcfSigned { get; private set; }
    public DateTimeOffset? IcfSignedDate { get; private set; }
    public bool RequiresReconsent { get; private set; }
    public bool IsPaperOverride { get; private set; }

    private const string SignedConsentDateMissing = "A signed consent requires the date it was signed (icf_signed_date).";

    private SubjectConsent() { }

    /// <summary>Creates a consent row. <paramref name="icfSignedDate"/> is required when
    /// <paramref name="icfSigned"/> is true; an unsigned consent may leave it empty.</summary>
    /// <exception cref="ArgumentException">The consent is signed but <paramref name="icfSignedDate"/> is null.</exception>
    public static SubjectConsent Create(SubjectCode subjectCode, ProtocolVersionRef version, bool icfSigned,
        DateTimeOffset? icfSignedDate, bool requiresReconsent, bool isPaperOverride)
    {
        if (icfSigned && icfSignedDate is null)
        {
            throw new ArgumentException(SignedConsentDateMissing, nameof(icfSignedDate));
        }

        return new SubjectConsent
        {
            Id = SubjectConsentId.New(),
            SubjectCode = subjectCode,
            ProtocolVersion = version,
            IcfSigned = icfSigned,
            IcfSignedDate = icfSignedDate,
            RequiresReconsent = requiresReconsent,
            IsPaperOverride = isPaperOverride,
        };
    }

    /// <summary>Rule 9 field-merge-on-resubmission semantics — NOT a full overwrite. `versionTag`/`icfSigned`/
    /// `requiresReconsent`/`isPaperOverride` always replace; `icfSignedDate` only replaces when supplied, so an
    /// omitted date keeps the stored one; `incomingProtocolVersion` only replaces the stored `ProtocolVersion`
    /// when non-null (an omitted resubmission value leaves the stored value untouched, it is not nulled out).
    /// The date is never defaulted to the current time: the legacy system did that, which recorded the moment
    /// of data entry as the moment of signing (#4824).</summary>
    /// <exception cref="InvalidOperationException">The merged consent would be signed with no signed date
    /// (no <paramref name="icfSignedDate"/> supplied and none stored). The row is left unchanged.</exception>
    public void MergeResubmission(string versionTag, bool icfSigned, DateTimeOffset? icfSignedDate,
        bool requiresReconsent, bool isPaperOverride, ProtocolVersionRef? incomingProtocolVersion)
    {
        DateTimeOffset? mergedDate = icfSignedDate ?? IcfSignedDate;
        if (icfSigned && mergedDate is null)
        {
            throw new InvalidOperationException(SignedConsentDateMissing);
        }

        ProtocolVersion = ProtocolVersion with { VersionTag = versionTag };
        IcfSigned = icfSigned;
        RequiresReconsent = requiresReconsent;
        IsPaperOverride = isPaperOverride;
        IcfSignedDate = mergedDate;
        if (incomingProtocolVersion is not null)
        {
            ProtocolVersion = incomingProtocolVersion;
        }
    }

    /// <summary>Rule 3 cascade target — called on every *other* consent row for the same subject when a new
    /// consent is recorded with `requiresReconsent == false`.</summary>
    public void ClearReconsentRequirement() => RequiresReconsent = false;
}
