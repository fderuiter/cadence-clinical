using System;
using System.Collections.Generic;
using System.Globalization;

using Cadence.Core.Domain.Supply;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class CgtEngineTests
{
    [Theory]
    [InlineData(-196.0, TemperatureZone.CRYOGENIC)]
    [InlineData(-80.0, TemperatureZone.CRYOGENIC)]
    [InlineData(-70.0, TemperatureZone.CRYOGENIC)]
    [InlineData(-65.0, TemperatureZone.ULTRA_COLD)]
    [InlineData(-20.0, TemperatureZone.ULTRA_COLD)]
    [InlineData(-15.0, TemperatureZone.FROZEN)]
    [InlineData(0.0, TemperatureZone.FROZEN)]
    [InlineData(2.0, TemperatureZone.REFRIGERATED)]
    [InlineData(8.0, TemperatureZone.REFRIGERATED)]
    [InlineData(20.0, TemperatureZone.CONTROLLED_AMBIENT)]
    public void TemperatureZoneClassifier_ClassifiesCorrectZones(double tempCelsius, TemperatureZone expectedZone)
    {
        var zone = TemperatureZoneClassifier.ClassifyZone(tempCelsius);
        Assert.Equal(expectedZone, zone);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Roslyn", "RS0030:Banned API", Justification = "Test creates sample timestamp range")]
    public void TemperatureZoneClassifier_CalculatesExcursionMetrics()
    {
        var t1 = DateTimeOffset.Parse("2026-10-01T10:00:00Z", CultureInfo.InvariantCulture).ToString("o");
        var t2 = DateTimeOffset.Parse("2026-10-01T12:00:00Z", CultureInfo.InvariantCulture).ToString("o");

        var readings = new List<TemperatureReading>
        {
            new(-80.0, t1),
            new(-50.0, t2)
        };

        var result = TemperatureZoneClassifier.CalculateExcursionMetrics(readings, -196.0, -70.0);
        Assert.Equal(1, result.ExcursionCount);
        Assert.Equal(-80.0, result.LowestTempCelsius);
        Assert.Equal(-50.0, result.HighestTempCelsius);
    }

    [Fact]
    public void Isbt128Engine_GeneratesAndValidatesDin()
    {
        string din = Isbt128Engine.GenerateDin("W1234", 26, 987654);
        Assert.NotNull(din);
        Assert.StartsWith("W123426987654", din);

        bool isValid = Isbt128Engine.ValidateDin(din);
        Assert.True(isValid);

        char checkChar = Isbt128Engine.CalculateIso7064Mod37_2("W123426987654");
        Assert.NotEqual('\0', checkChar);
    }

    [Fact]
    public void Isbt128Engine_RejectsInvalidDin()
    {
        Assert.False(Isbt128Engine.ValidateDin("INVALID_DIN"));
        Assert.False(Isbt128Engine.ValidateDin("=W123426987654XX"));
    }

    [Fact]
    public void CgtBarcodeEngine_GeneratesAndVerifiesCryptographicBarcode()
    {
        var barcode = CgtBarcodeEngine.GenerateCryptoBarcode(
            studyId: "STU-001",
            siteId: "SITE-101",
            subjectId: "SUB-8899",
            din: "=W12342698765400",
            batchId: "BATCH-9911");

        Assert.NotNull(barcode);
        Assert.NotEmpty(barcode.BarcodePayload);

        var (isValid, failureReason, decoded) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: barcode.BarcodePayload,
            expectedSubjectId: "SUB-8899",
            expectedDin: "=W12342698765400",
            expectedBatchId: "BATCH-9911");

        Assert.True(isValid);
        Assert.Null(failureReason);
        Assert.Equal("STU-001", decoded["study_id"]);
        Assert.Equal("SUB-8899", decoded["subject_id"]);
    }

    [Fact]
    public void CgtBarcodeEngine_RejectsTamperedBarcodePayload()
    {
        var barcode = CgtBarcodeEngine.GenerateCryptoBarcode(
            studyId: "STU-001",
            siteId: "SITE-101",
            subjectId: "SUB-8899",
            din: "=W12342698765400",
            batchId: "BATCH-9911");

        string tamperedPayload = barcode.BarcodePayload.Replace("SUB-8899", "SUB-9999");

        var (isValid, failureReason, _) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: tamperedPayload,
            expectedSubjectId: "SUB-9999");

        Assert.False(isValid);
        Assert.NotNull(failureReason);
    }
}
