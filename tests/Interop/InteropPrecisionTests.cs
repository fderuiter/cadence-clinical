using System;
using System.Linq;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Interop;

public class InteropPrecisionTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SensorSafetyAlertRecord_ObservationAndThresholdProperties_UseDecimalType()
    {
        typeof(SensorSafetyAlertRecord).GetProperty(nameof(SensorSafetyAlertRecord.ObservedValue))!.PropertyType.Should().Be(typeof(decimal));
        typeof(SensorSafetyAlertRecord).GetProperty(nameof(SensorSafetyAlertRecord.ThresholdValue))!.PropertyType.Should().Be(typeof(decimal));
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SensorDailySummaryRecord_VitalMeasurementProperties_UseNullableDecimalType()
    {
        var vitalProperties = new[]
        {
            nameof(SensorDailySummaryRecord.TirPercentage),
            nameof(SensorDailySummaryRecord.TbrLevel1Percentage),
            nameof(SensorDailySummaryRecord.TbrLevel2Percentage),
            nameof(SensorDailySummaryRecord.TarLevel1Percentage),
            nameof(SensorDailySummaryRecord.TarLevel2Percentage),
            nameof(SensorDailySummaryRecord.MeanGlucoseMgDl),
            nameof(SensorDailySummaryRecord.GlucoseSd),
            nameof(SensorDailySummaryRecord.GlucoseCvPercent),
            nameof(SensorDailySummaryRecord.GlucoseManagementIndicator),
            nameof(SensorDailySummaryRecord.MeanCadenceSpm),
            nameof(SensorDailySummaryRecord.PeakCadenceSpm),
            nameof(SensorDailySummaryRecord.ActiveMinutes),
            nameof(SensorDailySummaryRecord.SedentaryMinutes),
            nameof(SensorDailySummaryRecord.SleepDurationMinutes),
            nameof(SensorDailySummaryRecord.SleepEfficiencyPercent),
            nameof(SensorDailySummaryRecord.RestingHeartRateBpm),
            nameof(SensorDailySummaryRecord.BestFev1Liters),
            nameof(SensorDailySummaryRecord.BestFvcLiters),
            nameof(SensorDailySummaryRecord.Fev1FvcRatio),
            nameof(SensorDailySummaryRecord.Fev1PercentPredicted),
            nameof(SensorDailySummaryRecord.PeakExpiratoryFlowLMin),
            nameof(SensorDailySummaryRecord.MeanSystolicMmhg),
            nameof(SensorDailySummaryRecord.MeanDiastolicMmhg),
            nameof(SensorDailySummaryRecord.MeanArterialPressureMmhg),
        };

        foreach (var propertyName in vitalProperties)
        {
            var propInfo = typeof(SensorDailySummaryRecord).GetProperty(propertyName);
            propInfo.Should().NotBeNull($"Property {propertyName} must exist on SensorDailySummaryRecord");
            propInfo!.PropertyType.Should().Be(typeof(decimal?), $"Property SensorDailySummaryRecord.{propertyName} must be decimal?");
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void InteropConfigurations_ExplicitlyConfiguresNumeric18_4ForPrecisionProperties()
    {
        var options = new DbContextOptionsBuilder<InteropDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new InteropDbContext(options);
        var model = dbContext.Model;

        var expectedProperties = new[]
        {
            (typeof(SensorSafetyAlertRecord), nameof(SensorSafetyAlertRecord.ObservedValue)),
            (typeof(SensorSafetyAlertRecord), nameof(SensorSafetyAlertRecord.ThresholdValue)),

            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.TirPercentage)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.TbrLevel1Percentage)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.TbrLevel2Percentage)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.TarLevel1Percentage)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.TarLevel2Percentage)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.MeanGlucoseMgDl)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.GlucoseSd)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.GlucoseCvPercent)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.GlucoseManagementIndicator)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.MeanCadenceSpm)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.PeakCadenceSpm)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.ActiveMinutes)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.SedentaryMinutes)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.SleepDurationMinutes)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.SleepEfficiencyPercent)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.RestingHeartRateBpm)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.BestFev1Liters)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.BestFvcLiters)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.Fev1FvcRatio)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.Fev1PercentPredicted)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.PeakExpiratoryFlowLMin)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.MeanSystolicMmhg)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.MeanDiastolicMmhg)),
            (typeof(SensorDailySummaryRecord), nameof(SensorDailySummaryRecord.MeanArterialPressureMmhg)),
        };

        foreach (var (entityType, propertyName) in expectedProperties)
        {
            var entityTypeMeta = model.FindEntityType(entityType);
            entityTypeMeta.Should().NotBeNull($"Entity type {entityType.Name} should exist in Interop EF Core model");

            var propertyMeta = entityTypeMeta!.FindProperty(propertyName);
            propertyMeta.Should().NotBeNull($"Property {propertyName} should exist on {entityType.Name}");

            var precision = propertyMeta!.GetPrecision();
            precision.Should().Be(18, $"Property {entityType.Name}.{propertyName} must have precision 18");

            var scale = propertyMeta.GetScale();
            scale.Should().Be(4, $"Property {entityType.Name}.{propertyName} must have scale 4");

            var columnType = propertyMeta.FindAnnotation("Relational:ColumnType")?.Value as string;
            columnType.Should().Be("numeric(18,4)", $"Property {entityType.Name}.{propertyName} must map to numeric(18,4)");
        }
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SensorSafetyAlert_ExactBoundaryEvaluations_PreventFloatingPointInaccuracies()
    {
        // SpO2 88.0% threshold boundary condition
        decimal thresholdSpO2 = 88.0000m;
        decimal observedSpO2Exact = 88.0000m;
        decimal observedSpO2SlightlyBelow = 87.9999m;

        var alertExact = new SensorSafetyAlertRecord
        {
            ParameterName = "SpO2",
            ObservedValue = observedSpO2Exact,
            ThresholdValue = thresholdSpO2,
            Unit = "%",
        };

        var alertBelow = new SensorSafetyAlertRecord
        {
            ParameterName = "SpO2",
            ObservedValue = observedSpO2SlightlyBelow,
            ThresholdValue = thresholdSpO2,
            Unit = "%",
        };

        // Exact decimal equality: 88.0000m == 88.0m without double precision epsilon drift
        alertExact.ObservedValue.Should().Be(alertExact.ThresholdValue);
        (alertExact.ObservedValue < alertExact.ThresholdValue).Should().BeFalse();

        // Exact decimal inequality
        alertBelow.ObservedValue.Should().BeLessThan(alertBelow.ThresholdValue);
        (alertBelow.ThresholdValue - alertBelow.ObservedValue).Should().Be(0.0001m);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SensorDailySummary_ExactDecimalPrecision_PreservesSubDecimalMeasurementValues()
    {
        var summary = new SensorDailySummaryRecord
        {
            SubjectId = "SUBJ-1001",
            SummaryDate = DateTime.UtcNow.Date,
            MeanGlucoseMgDl = 112.4567m,
            GlucoseSd = 18.1234m,
            GlucoseCvPercent = 16.1189m,
            RestingHeartRateBpm = 64.2500m,
            BestFev1Liters = 2.8525m,
            BestFvcLiters = 3.6100m,
            Fev1FvcRatio = 0.7902m,
            MeanSystolicMmhg = 118.5000m,
            MeanDiastolicMmhg = 78.2500m,
            MeanArterialPressureMmhg = 91.6667m,
        };

        summary.MeanGlucoseMgDl.Should().Be(112.4567m);
        summary.GlucoseSd.Should().Be(18.1234m);
        summary.GlucoseCvPercent.Should().Be(16.1189m);
        summary.RestingHeartRateBpm.Should().Be(64.2500m);
        summary.BestFev1Liters.Should().Be(2.8525m);
        summary.BestFvcLiters.Should().Be(3.6100m);
        summary.Fev1FvcRatio.Should().Be(0.7902m);
        summary.MeanSystolicMmhg.Should().Be(118.5000m);
        summary.MeanDiastolicMmhg.Should().Be(78.2500m);
        summary.MeanArterialPressureMmhg.Should().Be(91.6667m);
    }
}
