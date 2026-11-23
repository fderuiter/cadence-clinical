using Cadence.Core.Domain.Supply;
using Cadence.UnitTests.Fixtures;

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
        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STU-001",
            siteId: "SITE-101",
            subjectId: "SUB-8899",
            din: "=W12342698765400",
            batchId: "BATCH-9911");

        Assert.NotEmpty(barcodePayload);

        var (isValid, failureReason, decoded) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: barcodePayload,
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
        string barcodePayload = CgtBarcodeTestSigner.SignPayload(
            studyId: "STU-001",
            siteId: "SITE-101",
            subjectId: "SUB-8899",
            din: "=W12342698765400",
            batchId: "BATCH-9911");

        string tamperedPayload = barcodePayload.Replace("SUB-8899", "SUB-9999");

        var (isValid, failureReason, _) = CgtBarcodeEngine.VerifyCryptoBarcode(
            barcodePayload: tamperedPayload,
            expectedSubjectId: "SUB-9999");

        Assert.False(isValid);
        Assert.NotNull(failureReason);
    }
}
