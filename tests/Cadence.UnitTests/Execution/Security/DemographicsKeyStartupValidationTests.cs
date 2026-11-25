using System;
using System.Security.Cryptography;

using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Checks that the execution host refuses to start without a usable demographics encryption key, so a
/// missing or malformed key stops the service at boot instead of failing the first subject registration.
/// </summary>
public sealed class DemographicsKeyStartupValidationTests
{
    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==AAAA")]
    [GxPRequirement("PRD-SYS-190")]
    public void HostStartup_WithoutAValidDemographicsKey_FailsFast(string keyBase64)
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestRandomizationKey()
            .UseTestCgtBarcodeKey()
            .UseSetting("DemographicsEncryption:KeyBase64", keyBase64));

        unconfigured.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain("DemographicsEncryption__KeyBase64");
    }

    /// <summary>
    /// The randomisation allocation key (#5607) is checked at boot the same way, so a host that would store arms
    /// under no key, or a short one, never starts.
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    public void HostStartup_WithoutAValidRandomizationKey_FailsFast(string keyBase64)
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey()
            .UseTestCgtBarcodeKey()
            .UseSetting(AllocationProtectionOptions.ConfigurationKey, keyBase64));

        unconfigured.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(AllocationProtectionOptions.ConfigurationKey);
    }

    /// <summary>
    /// The CGT barcode HMAC key (ADR-2666) is validated at boot so the host refuses to start without a 32-byte key.
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [GxPRequirement("SRS-SYS-005")]
    public void HostStartup_WithoutAValidCgtBarcodeKey_FailsFast(string keyBase64)
    {
        using var unconfigured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey()
            .UseTestRandomizationKey()
            .UseSetting(CgtBarcodeProtectionOptions.ConfigurationKey, keyBase64));

        unconfigured.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(CgtBarcodeProtectionOptions.ConfigurationKey);
    }

    /// <summary>The same host with a valid key passes startup validation, so the refusals above are the key's.</summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void HostStartup_WithAValidDemographicsKey_PassesValidation()
    {
        using var configured = new StartupValidationFactory<Cadence.Apps.Execution.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestDemographicsKey().UseTestRandomizationKey().UseTestCgtBarcodeKey());

        configured.ValidateStartup().Should().BeNull();
    }

    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData(16, true)]
    [InlineData(24, true)]
    [InlineData(32, true)]
    [InlineData(10, false)]
    [InlineData(33, false)]
    [InlineData(48, false)]
    public void IsValidKey_AcceptsOnlyAesKeyLengths(int length, bool expected)
    {
        DemographicsEncryptionOptions.IsValidKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(length)))
            .Should().Be(expected);
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void IsValidKey_RejectsMissingKey()
    {
        DemographicsEncryptionOptions.IsValidKey(null).Should().BeFalse();
        DemographicsEncryptionOptions.IsValidKey("   ").Should().BeFalse();
    }
}
