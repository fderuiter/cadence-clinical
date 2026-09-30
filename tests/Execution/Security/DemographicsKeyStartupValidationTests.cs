using System;
using System.Security.Cryptography;

using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
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
        using var unconfigured = new WebApplicationFactory<Cadence.Apps.Execution.Program>()
            .WithWebHostBuilder(builder => builder
                .UseTestGatewaySecret()
                .UseTestPostgresConnectionString()
                .UseSetting("DemographicsEncryption:KeyBase64", keyBase64));

        Action start = () => unconfigured.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*DemographicsEncryption__KeyBase64*");
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
