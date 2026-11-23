using System;
using System.Security.Cryptography;

using Cadence.Apps.Imaging.Infrastructure.Security;
using Cadence.Deid;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Imaging;

/// <summary>
/// The imaging host refuses to start without its de-identification keys (#5399): there is no default key, so a
/// missing, malformed, short or shared key stops the service at boot instead of signing with a key from the source.
/// </summary>
public sealed class ImagingDeidentificationKeyTests
{
    private static readonly string ValidKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [Trait("Category", "GxP_IQ")]
    [InlineData(null, "pseudonym")]
    [InlineData("", "pseudonym")]
    [InlineData("not base64!", "pseudonym")]
    [InlineData("short", "pseudonym")]
    [InlineData("valid", null)]
    [InlineData("valid", "short")]
    [InlineData("valid", "same")]
    public void HostStartup_WithoutTwoValidDistinctKeys_FailsFast(string? signing, string? pseudonym)
    {
        static string? Resolve(string? value) => value switch
        {
            "valid" => ValidKey,
            "same" => ValidKey,
            "pseudonym" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "short" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(31)),
            _ => value,
        };

        using var host = new StartupValidationFactory<Cadence.Apps.Imaging.Program>(builder =>
        {
            builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
            if (Resolve(signing) is { } s) builder.UseSetting(ImagingDeidentificationOptions.SigningKeyConfigurationKey, s);
            if (Resolve(pseudonym) is { } p) builder.UseSetting(ImagingDeidentificationOptions.PseudonymizationKeyConfigurationKey, p);
        });

        host.ValidateStartup().Should().BeOfType<OptionsValidationException>()
            .Which.Message.Should().Contain(ImagingDeidentificationOptions.SigningKeyConfigurationKey);
    }

    /// <summary>With two valid keys the host passes validation and resolves one de-identifier, so the refusals above are the keys'.</summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void HostStartup_WithTwoValidKeys_PassesValidationAndResolvesTheDeidentifier()
    {
        using var host = new StartupValidationFactory<Cadence.Apps.Imaging.Program>(builder => builder
            .UseTestGatewaySecret()
            .UseTestPostgresConnectionString()
            .UseTestImagingDeidentificationKeys());

        host.ValidateStartup().Should().BeNull();
        host.Services.GetRequiredService<DicomDeidentifier>()
            .Should().BeSameAs(host.Services.GetRequiredService<DicomDeidentifier>());
    }
}
