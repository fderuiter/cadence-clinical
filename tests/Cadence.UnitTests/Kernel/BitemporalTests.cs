using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Domain.Entities;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public class BitemporalTests
{
    /// <summary>
    /// Validates that TemporalPeriod.CreateCurrent correctly initializes open-ended valid and recorded intervals.
    /// </summary>
    [Fact(DisplayName = "TemporalPeriod_CreateCurrent_SetsOpenEndedUpperBounds")]
    [Trait("Category", "GxP_OQ")]
    public void TemporalPeriodCreateCurrentSetsOpenEndedUpperBounds()
    {
        // Arrange
        var validFrom = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var recordedAt = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);

        // Act
        var period = TemporalPeriod.CreateCurrent(validFrom, recordedAt);

        // Assert
        period.ValidFrom.Should().Be(validFrom);
        period.ValidTo.Should().BeNull();
        period.RecordedFrom.Should().Be(recordedAt);
        period.RecordedTo.Should().BeNull();
        period.IsCurrent(recordedAt).Should().BeTrue();
        period.IsEffectiveAsOf(validFrom, recordedAt).Should().BeTrue();
    }

    /// <summary>
    /// Validates that TemporalPeriod.CreateBounded sets explicit valid intervals and validates boundary constraints.
    /// </summary>
    [Fact(DisplayName = "TemporalPeriod_CreateBounded_EnforcesBoundaryConstraints")]
    [Trait("Category", "GxP_OQ")]
    public void TemporalPeriodCreateBoundedEnforcesBoundaryConstraints()
    {
        // Arrange
        var validFrom = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var validTo = new DateTimeOffset(2026, 9, 1, 16, 0, 0, TimeSpan.Zero);
        var recordedAt = new DateTimeOffset(2026, 9, 1, 17, 0, 0, TimeSpan.Zero);

        // Act
        var period = TemporalPeriod.CreateBounded(validFrom, validTo, recordedAt);

        // Assert
        period.ValidFrom.Should().Be(validFrom);
        period.ValidTo.Should().Be(validTo);
        period.RecordedFrom.Should().Be(recordedAt);
        period.RecordedTo.Should().BeNull();

        // Effective within bounds
        period.IsEffectiveAsOf(validFrom.AddHours(2), recordedAt.AddHours(1)).Should().BeTrue();
        // Not effective after validTo
        period.IsEffectiveAsOf(validTo.AddMinutes(1), recordedAt.AddHours(1)).Should().BeFalse();
        // Not effective before validFrom
        period.IsEffectiveAsOf(validFrom.AddMinutes(-1), recordedAt.AddHours(1)).Should().BeFalse();

        // Boundary constraint checks
        var actInvalidValidTo = () => new TemporalPeriod(validTo, validFrom, recordedAt, null);
        actInvalidValidTo.Should().Throw<ArgumentException>()
            .WithMessage("*ValidTo cannot precede ValidFrom*");

        var actInvalidRecordedTo = () => new TemporalPeriod(validFrom, validTo, recordedAt, recordedAt.AddHours(-1));
        actInvalidRecordedTo.Should().Throw<ArgumentException>()
            .WithMessage("*RecordedTo cannot precede RecordedFrom*");
    }

    /// <summary>
    /// Validates that TemporalPeriod.Supersede closes the recorded interval as of the supersede instant.
    /// </summary>
    [Fact(DisplayName = "TemporalPeriod_Supersede_ClosesRecordedInterval")]
    [Trait("Category", "GxP_OQ")]
    public void TemporalPeriodSupersedeClosesRecordedInterval()
    {
        // Arrange
        var validFrom = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var recordedAt = new DateTimeOffset(2026, 9, 1, 11, 0, 0, TimeSpan.Zero);
        var supersededAt = new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

        var originalPeriod = TemporalPeriod.CreateCurrent(validFrom, recordedAt);

        // Act
        var closedPeriod = originalPeriod.Supersede(supersededAt);

        // Assert
        closedPeriod.RecordedTo.Should().Be(supersededAt);
        // A superseded record is no longer current regardless of evaluation timestamp because RecordedTo is closed
        closedPeriod.IsCurrent(supersededAt.AddHours(1)).Should().BeFalse();
        closedPeriod.IsCurrent(supersededAt.AddHours(-1)).Should().BeFalse();

        // Historical query between recordedAt and supersededAt still sees original fact
        var queryTimeDuringFactLife = new DateTimeOffset(2026, 9, 1, 15, 0, 0, TimeSpan.Zero);
        closedPeriod.IsEffectiveAsOf(validFrom, queryTimeDuringFactLife).Should().BeTrue();

        // Query after supersede does not see superseded fact
        var queryTimeAfterSupersede = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        closedPeriod.IsEffectiveAsOf(validFrom, queryTimeAfterSupersede).Should().BeFalse();
    }

    /// <summary>
    /// Validates interval overlap detection across both valid time and recorded time dimensions.
    /// </summary>
    [Fact(DisplayName = "TemporalPeriod_Overlaps_DetectsIntervalCollisions")]
    [Trait("Category", "GxP_OQ")]
    public void TemporalPeriodOverlapsDetectsIntervalCollisions()
    {
        // Arrange
        var t0 = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 9, 1, 16, 0, 0, TimeSpan.Zero);

        var periodA = new TemporalPeriod(t0, t2, t0, t2);
        var periodB = new TemporalPeriod(t1, t3, t1, t3); // Overlaps [t1, t2)
        var periodC = new TemporalPeriod(t2, t3, t2, t3); // Disjoint [t2, t3) with periodA [t0, t2)

        // Act & Assert
        periodA.OverlapsValid(periodB).Should().BeTrue();
        periodA.OverlapsRecorded(periodB).Should().BeTrue();

        periodA.OverlapsValid(periodC).Should().BeFalse();
        periodA.OverlapsRecorded(periodC).Should().BeFalse();
    }

    /// <summary>
    /// Validates 21 CFR Part 11 point-in-time state reconstruction using LINQ AsOf and AsOfCurrent query extensions.
    /// </summary>
    [Fact(DisplayName = "BitemporalQueryExtensions_AsOf_PointInTime_StateReconstruction")]
    [Trait("Category", "GxP_OQ")]
    public void BitemporalQueryExtensionsAsOfPointInTimeStateReconstruction()
    {
        // Arrange

        // Clinical timeline:
        // T_event: 2026-09-01 09:00:00 (Observation valid time)
        // T_entry: 2026-09-01 10:00:00 (First recorded in database: Systolic BP = 140)
        // T_audit_1: 2026-09-01 12:00:00 (Auditor checks database state)
        // T_correction: 2026-09-02 08:00:00 (Investigator corrects typo to Systolic BP = 120)
        // T_audit_2: 2026-09-02 10:00:00 (Second audit inspection)

        var tEvent = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var tEntry = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var tAudit1 = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var tCorrection = new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero);
        var tAudit2 = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        var obsId1 = ObservationId.New();
        var obsId2 = ObservationId.New();

        // Record 1: Original reading recorded at tEntry, superseded at tCorrection
        var record1 = new RecordedFact(obsId1, "140", new TemporalPeriod(tEvent, null, tEntry, tCorrection));

        // Record 2: Corrected reading recorded at tCorrection, currently open-ended
        var record2 = new RecordedFact(obsId2, "120", TemporalPeriod.CreateCurrent(tEvent, tCorrection));

        var repository = new List<RecordedFact> { record1, record2 };

        // Act & Assert 1: Query before entry (T_event, T_entry - 1s) => zero records known to system
        var knownBeforeEntry = repository.AsOf(tEvent, tEntry.AddSeconds(-1)).ToList();
        knownBeforeEntry.Should().BeEmpty();

        var queryableBeforeEntry = repository.AsQueryable().AsOf(tEvent, tEntry.AddSeconds(-1)).ToList();
        queryableBeforeEntry.Should().BeEmpty();

        // Act & Assert 2: Query at T_audit_1 (before correction) => returns Record 1 ("140")
        var audit1Results = repository.AsOf(tEvent, tAudit1).ToList();
        audit1Results.Should().ContainSingle();
        audit1Results[0].Value.Should().Be("140");

        var queryableAudit1 = repository.AsQueryable().AsOf(tEvent, tAudit1).ToList();
        queryableAudit1.Should().ContainSingle();
        queryableAudit1[0].Value.Should().Be("140");

        // Act & Assert 3: Query at T_audit_2 (after correction) => returns Record 2 ("120")
        var audit2Results = repository.AsOf(tEvent, tAudit2).ToList();
        audit2Results.Should().ContainSingle();
        audit2Results[0].Value.Should().Be("120");

        var queryableAudit2 = repository.AsQueryable().AsOf(tEvent, tAudit2).ToList();
        queryableAudit2.Should().ContainSingle();
        queryableAudit2[0].Value.Should().Be("120");

        // Act & Assert 4: Query AsOfCurrent at T_audit_2 => returns only Record 2 ("120")
        var currentResults = repository.AsOfCurrent(tAudit2).ToList();
        currentResults.Should().ContainSingle();
        currentResults[0].Value.Should().Be("120");

        var queryableCurrent = repository.AsQueryable().AsOfCurrent(tAudit2).ToList();
        queryableCurrent.Should().ContainSingle();
        queryableCurrent[0].Value.Should().Be("120");
    }

    /// <summary>A bitemporal row as a table of superseding facts stores it: one value per recorded period.</summary>
    private sealed record RecordedFact(ObservationId Id, string Value, TemporalPeriod Period) : IBitemporalEntity;
}
