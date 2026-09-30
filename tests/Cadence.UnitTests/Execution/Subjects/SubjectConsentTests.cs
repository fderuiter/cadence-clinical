using System;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Coverage for `MIG-SUBJ-005`'s `SubjectConsent` aggregate: the signed-date invariant (a signed consent
/// always carries the date it was signed and the aggregate never invents one, #4824), `MergeResubmission`'s
/// Rule 9 four-category field-merge precedence (SCN-002's re-submission half, per
/// `02_technical_design.md` §6.2), and `ClearReconsentRequirement`.
/// </summary>
public class SubjectConsentTests
{
    private static readonly DateTimeOffset ExistingDate = new(2025, 3, 15, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IncomingDate = new(2025, 12, 25, 8, 0, 0, TimeSpan.Zero);

    private static ProtocolVersionRef ExistingVersion => new(new StudyCode("STUDY-001"), "v1.0", 1);
    private static ProtocolVersionRef IncomingVersion => new(new StudyCode("STUDY-001"), "v2.0", 2);

    private static SubjectConsent CreateConsent(
        DateTimeOffset? icfSignedDate = null,
        bool icfSigned = true,
        bool requiresReconsent = false,
        bool isPaperOverride = false,
        ProtocolVersionRef? version = null) =>
        SubjectConsent.Create(
            new SubjectCode("SUBJ-001"),
            version ?? ExistingVersion,
            icfSigned,
            icfSignedDate ?? (icfSigned ? ExistingDate : null),
            requiresReconsent,
            isPaperOverride);

    [Fact]
    public void Create_SignedWithoutIcfSignedDate_Throws()
    {
        var act = () => SubjectConsent.Create(
            new SubjectCode("SUBJ-001"), ExistingVersion, icfSigned: true,
            icfSignedDate: null, requiresReconsent: false, isPaperOverride: false);

        act.Should().Throw<ArgumentException>().WithParameterName("icfSignedDate");
    }

    [Fact]
    public void Create_UnsignedWithoutIcfSignedDate_LeavesDateEmpty()
    {
        var consent = SubjectConsent.Create(
            new SubjectCode("SUBJ-001"), ExistingVersion, icfSigned: false,
            icfSignedDate: null, requiresReconsent: true, isPaperOverride: false);

        consent.IcfSigned.Should().BeFalse();
        consent.IcfSignedDate.Should().BeNull();
    }

    [Fact]
    public void Create_IcfSignedDateSupplied_UsesSuppliedValue()
    {
        var consent = SubjectConsent.Create(
            new SubjectCode("SUBJ-001"), ExistingVersion, icfSigned: true,
            icfSignedDate: ExistingDate, requiresReconsent: false, isPaperOverride: false);

        consent.IcfSignedDate.Should().Be(ExistingDate);
    }

    [Fact]
    public void MergeResubmission_IcfSignedDateNull_ExistingDatePreserved()
    {
        var consent = CreateConsent(icfSignedDate: ExistingDate);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: null,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        consent.IcfSignedDate.Should().Be(ExistingDate);
    }

    [Fact]
    public void MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate()
    {
        var consent = CreateConsent(icfSignedDate: ExistingDate);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: IncomingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        consent.IcfSignedDate.Should().Be(IncomingDate);
    }

    [Fact]
    public void MergeResubmission_SignedWithNoDateSuppliedOrStored_ThrowsAndLeavesRowUnchanged()
    {
        var consent = CreateConsent(icfSigned: false, requiresReconsent: true);

        var act = () => consent.MergeResubmission("v1.1", icfSigned: true, icfSignedDate: null,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        act.Should().Throw<InvalidOperationException>();
        consent.IcfSigned.Should().BeFalse();
        consent.IcfSignedDate.Should().BeNull();
        consent.RequiresReconsent.Should().BeTrue();
        consent.ProtocolVersion.VersionTag.Should().Be("v1.0");
    }

    [Fact]
    public void MergeResubmission_UnsignedWithNoDate_IsAllowed()
    {
        var consent = CreateConsent(icfSigned: false);

        consent.MergeResubmission("v1.0", icfSigned: false, icfSignedDate: null,
            requiresReconsent: true, isPaperOverride: false, incomingProtocolVersion: null);

        consent.IcfSigned.Should().BeFalse();
        consent.IcfSignedDate.Should().BeNull();
    }

    [Fact]
    public void MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged()
    {
        var consent = CreateConsent(version: ExistingVersion);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        consent.ProtocolVersion.Should().Be(ExistingVersion with { VersionTag = "v1.0" });
    }

    [Fact]
    public void MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion()
    {
        var consent = CreateConsent(version: ExistingVersion);

        consent.MergeResubmission("v9.9", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: IncomingVersion);

        consent.ProtocolVersion.Should().Be(IncomingVersion);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(icfSigned: existing);

        consent.MergeResubmission("v1.0", icfSigned: incoming, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        consent.IcfSigned.Should().Be(incoming);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(requiresReconsent: existing);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: incoming, isPaperOverride: false, incomingProtocolVersion: null);

        consent.RequiresReconsent.Should().Be(incoming);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(isPaperOverride: existing);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: incoming, incomingProtocolVersion: null);

        consent.IsPaperOverride.Should().Be(incoming);
    }

    [Theory]
    [InlineData("v1.0", "v1.0")]
    [InlineData("v1.0", "v2.0")]
    public void MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(string existingTag, string incomingTag)
    {
        var consent = CreateConsent(version: ExistingVersion with { VersionTag = existingTag });

        consent.MergeResubmission(incomingTag, icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null);

        consent.ProtocolVersion.VersionTag.Should().Be(incomingTag);
    }

    [Fact]
    public void ClearReconsentRequirement_SetsRequiresReconsentFalse()
    {
        var consent = CreateConsent(requiresReconsent: true);

        consent.ClearReconsentRequirement();

        consent.RequiresReconsent.Should().BeFalse();
    }
}
