using System;
using System.Reflection;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel.Time;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Subjects;

/// <summary>
/// Coverage for `MIG-SUBJ-005`'s `SubjectConsent` aggregate: `Create`'s `IcfSignedDate` defaulting,
/// `MergeResubmission`'s Rule 9 four-category field-merge precedence (SCN-002's re-submission half,
/// per `02_technical_design.md` §6.2), and `ClearReconsentRequirement`.
/// @req:PRD-SUB-001
/// </summary>
public class SubjectConsentTests
{
    private static readonly DateTimeOffset CreateClockNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MergeClockNow = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExistingDate = new(2025, 3, 15, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IncomingDate = new(2025, 12, 25, 8, 0, 0, TimeSpan.Zero);

    private static ProtocolVersionRef ExistingVersion => new(new StudyCode("STUDY-001"), "v1.0", 1);
    private static ProtocolVersionRef IncomingVersion => new(new StudyCode("STUDY-001"), "v2.0", 2);

    private static SubjectConsent CreateConsent(
        DateTimeOffset? icfSignedDate = null,
        bool icfSigned = true,
        bool requiresReconsent = false,
        bool isPaperOverride = false,
        IClock? clock = null,
        ProtocolVersionRef? version = null) =>
        SubjectConsent.Create(
            new SubjectCode("SUBJ-001"),
            version ?? ExistingVersion,
            icfSigned,
            icfSignedDate,
            requiresReconsent,
            isPaperOverride,
            clock ?? new FakeClock(CreateClockNow));

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_IcfSignedDateOmitted_DefaultsToClockUtcNow()
    {
        var clock = new FakeClock(CreateClockNow);

        var consent = SubjectConsent.Create(
            new SubjectCode("SUBJ-001"), ExistingVersion, icfSigned: true,
            icfSignedDate: null, requiresReconsent: false, isPaperOverride: false, clock);

        consent.IcfSignedDate.Should().Be(CreateClockNow);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Create_IcfSignedDateSupplied_UsesSuppliedValueNotClock()
    {
        var clock = new FakeClock(CreateClockNow);

        var consent = SubjectConsent.Create(
            new SubjectCode("SUBJ-001"), ExistingVersion, icfSigned: true,
            icfSignedDate: ExistingDate, requiresReconsent: false, isPaperOverride: false, clock);

        consent.IcfSignedDate.Should().Be(ExistingDate);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MergeResubmission_IcfSignedDateNull_ExistingDatePreserved()
    {
        var consent = CreateConsent(icfSignedDate: ExistingDate);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: null,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.IcfSignedDate.Should().Be(ExistingDate);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MergeResubmission_IcfSignedDateSupplied_ReplacesExistingDate()
    {
        var consent = CreateConsent(icfSignedDate: ExistingDate);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: IncomingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.IcfSignedDate.Should().Be(IncomingDate);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MergeResubmission_NoPriorDateAndIcfSignedDateNull_DefaultsToClockUtcNow()
    {
        // `Create` always defaults IcfSignedDate to clock.UtcNow when omitted, so a null
        // IcfSignedDate can only exist as a persistence-round-trip edge case (e.g. a legacy row
        // migrated with no date recorded) — not reachable through the public API. Set it via the
        // private setter directly to exercise the acceptance criterion's genuine edge case.
        var consent = CreateConsent(icfSignedDate: ExistingDate);
        SetIcfSignedDate(consent, null);
        var mergeClock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: null,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, mergeClock);

        consent.IcfSignedDate.Should().Be(MergeClockNow);
    }

    private static void SetIcfSignedDate(SubjectConsent consent, DateTimeOffset? value)
    {
        var property = typeof(SubjectConsent).GetProperty(nameof(SubjectConsent.IcfSignedDate))
            ?? throw new InvalidOperationException($"{nameof(SubjectConsent.IcfSignedDate)} property not found via reflection.");
        property.SetValue(consent, value);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MergeResubmission_IncomingProtocolVersionNull_StoredProtocolVersionUnchanged()
    {
        var consent = CreateConsent(version: ExistingVersion);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.ProtocolVersion.Should().Be(ExistingVersion with { VersionTag = "v1.0" });
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void MergeResubmission_IncomingProtocolVersionSupplied_ReplacesStoredProtocolVersion()
    {
        var consent = CreateConsent(version: ExistingVersion);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v9.9", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: IncomingVersion, clock);

        consent.ProtocolVersion.Should().Be(IncomingVersion);
    }

    [Theory]
    [GxPRequirement("PRD-SUB-001")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_IcfSigned_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(icfSigned: existing);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: incoming, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.IcfSigned.Should().Be(incoming);
    }

    [Theory]
    [GxPRequirement("PRD-SUB-001")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_RequiresReconsent_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(requiresReconsent: existing);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: incoming, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.RequiresReconsent.Should().Be(incoming);
    }

    [Theory]
    [GxPRequirement("PRD-SUB-001")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void MergeResubmission_IsPaperOverride_AlwaysReplacedUnconditionally(bool existing, bool incoming)
    {
        var consent = CreateConsent(isPaperOverride: existing);
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission("v1.0", icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: incoming, incomingProtocolVersion: null, clock);

        consent.IsPaperOverride.Should().Be(incoming);
    }

    [Theory]
    [GxPRequirement("PRD-SUB-001")]
    [InlineData("v1.0", "v1.0")]
    [InlineData("v1.0", "v2.0")]
    public void MergeResubmission_VersionTag_AlwaysReplacedUnconditionally(string existingTag, string incomingTag)
    {
        var consent = CreateConsent(version: ExistingVersion with { VersionTag = existingTag });
        var clock = new FakeClock(MergeClockNow);

        consent.MergeResubmission(incomingTag, icfSigned: true, icfSignedDate: ExistingDate,
            requiresReconsent: false, isPaperOverride: false, incomingProtocolVersion: null, clock);

        consent.ProtocolVersion.VersionTag.Should().Be(incomingTag);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void ClearReconsentRequirement_SetsRequiresReconsentFalse()
    {
        var consent = CreateConsent(requiresReconsent: true);

        consent.ClearReconsentRequirement();

        consent.RequiresReconsent.Should().BeFalse();
    }

    /// <summary>Deterministic <see cref="IClock"/> test double — see the ticket's guidance to use a
    /// fake/test-double implementation rather than modifying <c>IClock</c> itself.</summary>
    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
