using System;

using Cadence.Apps.Execution.Domain.Devices;
using Cadence.Testing;

using Xunit;

namespace Cadence.UnitTests.Execution.Devices;

public sealed class UdiParserTests
{
    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_Gs1128WithParentheses_ReturnsParsedAttributes()
    {
        // GTIN: 00850006789016, Exp: 261231, Lot: LOT123, Serial: SN987
        string barcode = "(01)00850006789016(17)261231(10)LOT123(21)SN987";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("GS1_128", result.UdiCarrierFormat);
        Assert.Equal("00850006789016", result.UdiDi);
        Assert.Equal("LOT123", result.LotNumber);
        Assert.Equal("SN987", result.SerialNumber);
        Assert.NotNull(result.ExpirationDate);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_Gs1DataMatrixPrefix_ReturnsDataMatrixCarrierFormat()
    {
        string barcode = "]d2(01)00850006789016(10)LOT999";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("GS1_DATAMATRIX", result.UdiCarrierFormat);
        Assert.Equal("00850006789016", result.UdiDi);
        Assert.Equal("LOT999", result.LotNumber);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_Gs1128SequentialWithoutParentheses_ParsesFields()
    {
        // 01008500067890161726123110LOT123
        string barcode = "01008500067890161726123110LOT123";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("00850006789016", result.UdiDi);
        Assert.Equal("LOT123", result.LotNumber);
        Assert.NotNull(result.ExpirationDate);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_Hibcc128PrimaryAndSecondary_ParsesComponents()
    {
        // Primary: +H1234567890H, Secondary: /$3260101LOT123
        string barcode = "+H1234567890H/$3260101LOT123";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("HIBCC_128", result.UdiCarrierFormat);
        Assert.Equal("H1234567890H", result.UdiDi);
        Assert.NotNull(result.ExpirationDate);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void ValidateGtinMod10_ValidAndInvalidGtins_EvaluatesCorrectly()
    {
        Assert.True(UdiParser.ValidateGtinMod10("00850006789016"));
        Assert.False(UdiParser.ValidateGtinMod10("00850006789014")); // Invalid check digit
        Assert.False(UdiParser.ValidateGtinMod10("123")); // Invalid length
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void IsHibccMod43Valid_ValidAndInvalidCodes_EvaluatesCorrectly()
    {
        Assert.True(UdiParser.IsHibccMod43Valid("+H1234567890H"));
        Assert.False(UdiParser.IsHibccMod43Valid("+H1234567890Z")); // Invalid check char
        Assert.False(UdiParser.IsHibccMod43Valid(""));
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_HibccDataMatrixWithPrefix_ParsesComponentsAndCarrierFormat()
    {
        string barcode = "]d2+H1234567890H/$$3261231LOT123";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("HIBCC_DATAMATRIX", result.UdiCarrierFormat);
        Assert.Equal("H1234567890H", result.UdiDi);
        Assert.Equal("LOT123", result.LotNumber);
        Assert.NotNull(result.ExpirationDate);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_HibccSecondarySerialNumber_ParsesSerialNumber()
    {
        string barcode = "+H1234567890H/$$7SN98765";

        var result = UdiParser.Parse(barcode);

        Assert.True(result.IsValid);
        Assert.Equal("SN98765", result.SerialNumber);
    }

    [Fact]
    [GxPRequirement("PRD-DEV-001")]
    public void Parse_NullOrEmptyBarcode_ReturnsInvalidResult()
    {
        var result = UdiParser.Parse("");

        Assert.False(result.IsValid);
        Assert.Contains(result.ValidationErrors, e => e.Contains("cannot be null or empty"));
    }
}
