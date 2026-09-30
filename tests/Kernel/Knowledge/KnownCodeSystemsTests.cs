using System;

using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Knowledge;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel.Knowledge;

/// <summary>CDISC CT packages as package-scoped code systems (ADR-2470).</summary>
[Trait("Category", "Unit")]
public class KnownCodeSystemsTests
{
    /// <summary>The package type in a CDISC Library CT path maps to one code-system URI, and back.</summary>
    [Theory(DisplayName = "CdiscCt_PackageType_RoundTripsThroughTheUri")]
    [InlineData("sdtmct", KnownCodeSystems.SdtmCt)]
    [InlineData("cdashct", KnownCodeSystems.CdashCt)]
    [InlineData("adamct", KnownCodeSystems.AdamCt)]
    [InlineData("sendct", KnownCodeSystems.SendCt)]
    [InlineData("protocolct", KnownCodeSystems.ProtocolCt)]
    [InlineData("define-xmlct", KnownCodeSystems.DefineXmlCt)]
    [InlineData("qs-ftct", "https://library.cdisc.org/api/mdr/ct/packages/qs-ftct")]
    public void CdiscCtPackageTypeRoundTripsThroughTheUri(string package, string uri)
    {
        KnownCodeSystems.CdiscCt(package).Should().Be(uri);

        KnownCodeSystems.TryGetCdiscCtPackage(uri, out var parsed).Should().BeTrue();
        parsed.Should().Be(package);
    }

    /// <summary>Names that are not CT package types (dated, upper case, empty, not ending in "ct") are refused.</summary>
    [Theory(DisplayName = "CdiscCt_NotAPackageType_Throws")]
    [InlineData("sdtmct-2024-09-27")]
    [InlineData("SDTMCT")]
    [InlineData("")]
    [InlineData("sdtm")]
    [InlineData("../sdtmct")]
    public void CdiscCtNotAPackageTypeThrows(string package)
    {
        var act = () => KnownCodeSystems.CdiscCt(package);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Other URIs, including the NCI Thesaurus and a dated package path, are not CT package code systems.</summary>
    [Theory(DisplayName = "TryGetCdiscCtPackage_OtherUri_IsFalse")]
    [InlineData(KnownCodeSystems.NciThesaurus)]
    [InlineData(KnownCodeSystems.Loinc)]
    [InlineData("https://library.cdisc.org/api/mdr/ct/packages/sdtmct-2024-09-27")]
    [InlineData("https://library.cdisc.org/api/mdr/ct/packages/")]
    public void TryGetCdiscCtPackageOtherUriIsFalse(string uri)
    {
        KnownCodeSystems.TryGetCdiscCtPackage(uri, out var package).Should().BeFalse();
        package.Should().BeNull();
    }

    /// <summary>Every CDISC CT package is open content, including package types this list has no constant for.</summary>
    [Theory(DisplayName = "RequiredLicence_CdiscCtPackage_IsOpen")]
    [InlineData(KnownCodeSystems.SdtmCt)]
    [InlineData("https://library.cdisc.org/api/mdr/ct/packages/glossaryct")]
    public void RequiredLicenceCdiscCtPackageIsOpen(string uri)
    {
        KnownCodeSystems.RequiredLicence(uri).Should().Be(LicenseClass.Open);
    }

    /// <summary>The CDISC CT parser reports the code system of the package it was built for.</summary>
    [Fact(DisplayName = "CdiscCtParser_CodeSystemUri_IsThePackage")]
    public void CdiscCtParserCodeSystemUriIsThePackage()
    {
        new CdiscControlledTerminologyParser("adamct").CodeSystemUri.Should().Be(KnownCodeSystems.AdamCt);
    }
}
