using System;
using System.Linq;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// #4770: the payments, econsent and interop composition roots wire field protection from configuration and refuse to
/// start without their keys.
/// </summary>
public class FieldProtectionHostWiringTests
{
    /// <summary>
    /// Payments and eConsent resolve their DbContexts with the configured AES-GCM encryptor behind the encrypted
    /// columns.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void PaymentsAndEconsentHosts_WithKey_UseConfiguredEncryptorForEncryptedColumns()
    {
        string expectedKeyId = TestFieldProtection.CreateEncryptor().KeyId;

        using var payments = new WebApplicationFactory<Cadence.Apps.Payments.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys());
        using (var scope = payments.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            context.FieldEncryptor.Should().BeOfType<AesGcmFieldEncryptor>().Which.KeyId.Should().Be(expectedKeyId);
            context.Model.FindEntityType(typeof(ParticipantWallet))!
                .FindProperty(nameof(ParticipantWallet.AchProcessorReference))!.GetValueConverter()
                .Should().BeOfType<EncryptedStringConverter>();
        }

        using var econsent = new WebApplicationFactory<Cadence.Apps.Econsent.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys());
        using (var scope = econsent.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<EconsentDbContext>();
            context.FieldEncryptor.Should().BeOfType<AesGcmFieldEncryptor>().Which.KeyId.Should().Be(expectedKeyId);
            context.Model.FindEntityType(typeof(ConsentSignature))!
                .FindProperty(nameof(ConsentSignature.SignerEmail))!.GetValueConverter()
                .Should().BeOfType<EncryptedStringConverter>();
        }
    }

    /// <summary>
    /// Interop and Execution resolve their lookup hashers with the configured lookup key.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void InteropAndExecutionHosts_WithKey_ResolveLookupHasher()
    {
        using var interop = new WebApplicationFactory<Cadence.Apps.Interop.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys().UseTestSceSigningKey());
        using var execution = new WebApplicationFactory<Cadence.Apps.Execution.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestDemographicsKey().UseTestRandomizationKey().UseTestFieldProtectionKeys());

        var expectedHasher = TestFieldProtection.CreateLookupHasher();
        var interopHasher = interop.Services.GetRequiredService<DeviceTokenHasher>();
        var executionHasher = execution.Services.GetRequiredService<ILookupHasher>();

        interopHasher.Hash("token-1").Should().Be(new DeviceTokenHasher(expectedHasher).Hash("token-1"));
        executionHasher.ComputeHash("CAD-2345-6789-ABCD", "subject_activation_token")
            .Should().Be(expectedHasher.ComputeHash("CAD-2345-6789-ABCD", "subject_activation_token"));
    }

    /// <summary>
    /// Each composition root registers the fail-closed key validation for the key it needs, so a missing or blank
    /// key is rejected. (That registered validation stops a host at startup is proven for the shared registration in
    /// <see cref="FieldProtectionTests.Host_WithoutFieldProtectionKeys_FailsAtStartup"/>; a failing
    /// WebApplicationFactory start is not asserted here because it surfaces nondeterministically as an
    /// ObjectDisposedException.)
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void Hosts_RegisterFailClosedKeyValidation()
    {
        using var payments = new WebApplicationFactory<Cadence.Apps.Payments.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys());
        using var econsent = new WebApplicationFactory<Cadence.Apps.Econsent.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys());
        using var interop = new WebApplicationFactory<Cadence.Apps.Interop.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestFieldProtectionKeys().UseTestSceSigningKey());
        using var execution = new WebApplicationFactory<Cadence.Apps.Execution.Program>()
            .WithWebHostBuilder(builder => builder.UseTestGatewaySecret().UseTestPostgresConnectionString().UseTestDemographicsKey().UseTestRandomizationKey().UseTestFieldProtectionKeys());

        AssertRejectsMissingKey<FieldEncryptionOptions>(payments.Services, new FieldEncryptionOptions(), new FieldEncryptionOptions { KeyBase64 = " " });
        AssertRejectsMissingKey<FieldEncryptionOptions>(econsent.Services, new FieldEncryptionOptions(), new FieldEncryptionOptions { KeyBase64 = " " });
        AssertRejectsMissingKey<LookupHashOptions>(interop.Services, new LookupHashOptions(), new LookupHashOptions { KeyBase64 = " " });
        AssertRejectsMissingKey<LookupHashOptions>(execution.Services, new LookupHashOptions(), new LookupHashOptions { KeyBase64 = " " });
    }

    private static void AssertRejectsMissingKey<TOptions>(IServiceProvider services, params TOptions[] invalid)
        where TOptions : class
    {
        var validators = services.GetServices<IValidateOptions<TOptions>>().ToList();
        validators.Should().NotBeEmpty($"the host must validate {typeof(TOptions).Name}");

        foreach (var options in invalid)
        {
            validators.Select(v => v.Validate(Options.DefaultName, options)).Should().Contain(result => result.Failed);
        }
    }
}
