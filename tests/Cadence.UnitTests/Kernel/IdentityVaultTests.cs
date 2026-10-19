using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Database;
using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Identity;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Testing.Hosting;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The IdentityVault without a database (spec §9.2, ADR-2458): envelope sealing, blind indexes, the key hierarchy, the
/// layer rules for details, and that plaintext identifiers never leave memory through a command.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityVaultTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid Subject = Guid.CreateVersion7();

    private static readonly IdentityDetails Jane = new()
    {
        GivenName = "Jane",
        FamilyName = "Doe",
        BirthDate = new DateOnly(1970, 3, 14),
        Email = "Jane.Doe@Hospital.test",
        Phone = "+1 555 0100",
    };

    /// <summary>Validates that sealed details open to the same details and hold none of them in the clear.</summary>
    [Fact(DisplayName = "Seal_RoundTrips_AndHidesThePlaintext")]
    public void SealRoundTripsAndHidesThePlaintext()
    {
        var sealer = Sealer();

        var sealedJane = sealer.Seal(Tenant, IdentityLayer.Person, Subject, Jane);

        sealer.Unseal(Tenant, IdentityLayer.Person, Subject, sealedJane).Should().Be(Jane);
        foreach (var value in new[] { "Jane", "Doe", "1970", "Hospital", "555" })
        {
            Contains(sealedJane.Ciphertext, value).Should().BeFalse(value);
            Contains(sealedJane.WrappedDataKey, value).Should().BeFalse(value);
        }
    }

    /// <summary>
    /// Validates that each record gets a fresh data key and nonce, and that sealed bytes bind their tenant, layer and
    /// subject: moved to another record they do not open.
    /// </summary>
    [Fact(DisplayName = "Seal_UsesAFreshDataKey_AndBindsTenantLayerAndSubject")]
    public void SealUsesAFreshDataKeyAndBindsTenantLayerAndSubject()
    {
        var sealer = Sealer();
        var first = sealer.Seal(Tenant, IdentityLayer.Person, Subject, Jane);
        var second = sealer.Seal(Tenant, IdentityLayer.Person, Subject, Jane);

        second.WrappedDataKey.Should().NotEqual(first.WrappedDataKey);
        second.Ciphertext.Should().NotEqual(first.Ciphertext);

        var moved = new Action[]
        {
            () => sealer.Unseal(TenantId.New(), IdentityLayer.Person, Subject, first),
            () => sealer.Unseal(Tenant, IdentityLayer.Person, Guid.CreateVersion7(), first),
            () => sealer.Unseal(Tenant, IdentityLayer.SiteLocal, Subject, first),
            () => sealer.Unseal(Tenant, IdentityLayer.Person, Subject, first with { Ciphertext = second.Ciphertext }),
        };
        foreach (var open in moved)
        {
            open.Should().Throw<CryptographicException>();
        }
    }

    /// <summary>
    /// Validates the blind index: equal after normalization within a tenant and layer, different across tenants and
    /// layers, and absent when the layer's lookup field is.
    /// </summary>
    [Fact(DisplayName = "BlindIndex_MatchesNormalizedValues_WithinOneTenantAndLayer")]
    public void BlindIndexMatchesNormalizedValuesWithinOneTenantAndLayer()
    {
        var sealer = Sealer();

        var index = sealer.Seal(Tenant, IdentityLayer.Person, Subject, Jane).BlindIndex;

        index.Should().Equal(sealer.Seal(Tenant, IdentityLayer.Person, Guid.CreateVersion7(), Jane with { Email = " jane.doe@hospital.TEST " }).BlindIndex);
        index.Should().Equal(sealer.BlindIndex(Tenant, IdentityLayer.Person, "jane.doe@hospital.test"));
        index.Should().NotEqual(sealer.Seal(TenantId.New(), IdentityLayer.Person, Subject, Jane).BlindIndex);
        index.Should().NotEqual(sealer.BlindIndex(Tenant, IdentityLayer.SiteLocal, "jane.doe@hospital.test"));
        sealer.Seal(Tenant, IdentityLayer.Person, Subject, Jane with { Email = null }).BlindIndex.Should().BeNull();
    }

    /// <summary>
    /// Validates the local key hierarchy: another master key cannot open a record, an unknown key id is refused, and a
    /// master key that is not 32 bytes of base64 fails at registration instead of at first use.
    /// </summary>
    [Fact(DisplayName = "LocalKeys_AreDerivedFromTheMasterKey_AndFailClosed")]
    public void LocalKeysAreDerivedFromTheMasterKeyAndFailClosed()
    {
        var sealedJane = Sealer().Seal(Tenant, IdentityLayer.Person, Subject, Jane);

        var otherMaster = () => Sealer().Unseal(Tenant, IdentityLayer.Person, Subject, sealedJane);
        otherMaster.Should().Throw<CryptographicException>();
        var sameMasterOtherId = () => Sealer().Unseal(Tenant, IdentityLayer.Person, Subject, sealedJane with { KeyId = "kms-v2" });
        sameMasterOtherId.Should().Throw<CryptographicException>();

        var services = new ServiceCollection();
        var shortKey = () => services.AddKernelIdentityVaultPersistence(Convert.ToBase64String(new byte[16]), null);
        shortKey.Should().Throw<ArgumentException>();
        var notBase64 = () => services.AddKernelIdentityVaultPersistence("not base64!", null);
        notBase64.Should().Throw<ArgumentException>();
        var missing = () => services.AddKernelIdentityVaultPersistence(" ", null);
        missing.Should().Throw<ArgumentException>();
    }

    /// <summary>Validates that a vault serves unrestricted tenants and tenants of its own region only (spec §13).</summary>
    [Fact(DisplayName = "Sealer_ServesItsOwnRegionAndUnrestrictedTenants")]
    public void SealerServesItsOwnRegionAndUnrestrictedTenants()
    {
        var eu = new IdentitySealer(new LocalVaultKeyProvider(RandomNumberGenerator.GetBytes(32), "EU"));
        var unplaced = new IdentitySealer(new LocalVaultKeyProvider(RandomNumberGenerator.GetBytes(32), null));

        eu.Serves(null).Should().BeTrue();
        eu.Serves("EU").Should().BeTrue();
        eu.Serves("US").Should().BeFalse();
        unplaced.Serves(null).Should().BeTrue();
        unplaced.Serves("EU").Should().BeFalse();
    }

    /// <summary>Validates the layer rules: each layer takes only its own fields, at least one, well formed.</summary>
    [Theory(DisplayName = "Details_AreValidatedPerLayer")]
    [InlineData(IdentityLayer.Person, "person", null)]
    [InlineData(IdentityLayer.Person, "empty", "K.IdentityVault.DetailsRequired")]
    [InlineData(IdentityLayer.Person, "site", "K.IdentityVault.DetailsInvalid")]
    [InlineData(IdentityLayer.Person, "blank", "K.IdentityVault.DetailsInvalid")]
    [InlineData(IdentityLayer.Person, "long", "K.IdentityVault.DetailsInvalid")]
    [InlineData(IdentityLayer.Person, "bad-email", "K.IdentityVault.DetailsInvalid")]
    [InlineData(IdentityLayer.Person, "birth-only", null)]
    [InlineData(IdentityLayer.SiteLocal, "site", null)]
    [InlineData(IdentityLayer.SiteLocal, "person", "K.IdentityVault.DetailsInvalid")]
    [InlineData(IdentityLayer.SiteLocal, "empty", "K.IdentityVault.DetailsRequired")]
    public void DetailsAreValidatedPerLayer(IdentityLayer layer, string sample, string? error)
    {
        var details = sample switch
        {
            "person" => Jane,
            "empty" => new IdentityDetails(),
            "site" => new IdentityDetails { SiteLocalNumber = "SCR-001" },
            "blank" => new IdentityDetails { GivenName = "  " },
            "long" => new IdentityDetails { FamilyName = new string('x', IdentityDetails.MaxFieldLength + 1) },
            "bad-email" => new IdentityDetails { Email = "not-an-email" },
            "birth-only" => new IdentityDetails { BirthDate = new DateOnly(1980, 1, 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };

        details.Validate(layer)?.Code.Should().Be(error);
        if (error is null)
        {
            details.Validate(layer).Should().BeNull();
        }
    }

    /// <summary>
    /// Validates that plaintext identifiers never leave memory through a command: the details print no value, are not
    /// serialized, and do not change the request hash the pipeline stores.
    /// </summary>
    [Fact(DisplayName = "RecordCommands_NeverSerializeOrPrintTheDetails")]
    public void RecordCommandsNeverSerializeOrPrintTheDetails()
    {
        var command = new RecordPersonIdentity(Tenant, PersonId.New(), Jane, "key-1", "Registration at screening");
        var other = command with { Details = new IdentityDetails { GivenName = "Somebody", FamilyName = "Else" } };

        foreach (var text in new[] { JsonSerializer.Serialize(command), command.ToString(), Jane.ToString() })
        {
            text.Should().NotContainAny("Jane", "Doe", "1970-03-14", "Hospital", "555 0100");
        }

        Jane.ToString().Should().Be("IdentityDetails { GivenName, FamilyName, BirthDate, Email, Phone }");
        KernelCommandPipeline.ComputeRequestHash(other, typeof(RecordPersonIdentity))
            .Should().Be(KernelCommandPipeline.ComputeRequestHash(command, typeof(RecordPersonIdentity)));
    }

    /// <summary>
    /// Validates the schema rule behind K-IDN-1 in the EF model: no kernel column is named like a direct identifier,
    /// and <c>people</c> is pseudonymous.
    /// </summary>
    [Fact(DisplayName = "KernelModel_HasNoDirectIdentifierColumn")]
    public void KernelModelHasNoDirectIdentifierColumn()
    {
        using var context = new KernelDbContextDesignTimeFactory().CreateDbContext(
            [CadenceDatabaseExtensions.DesignTimeConnectionArgument, TestHostSettings.PostgresConnectionString]);

        var columns = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => $"{e.GetTableName()}.{p.GetColumnName()}"))
            .ToList();

        columns.Where(c => DirectIdentifierColumns.IsIdentifier(c.Split('.')[1])).Should().BeEmpty();
        columns.Where(c => c.StartsWith("people.", StringComparison.Ordinal)).Should().BeEquivalentTo(
            "people.id", "people.is_active", "people.created_at", "people.created_by", "people.reason_for_change", "people.version_index");
        DirectIdentifierColumns.IsIdentifier("email").Should().BeTrue();
    }

    private static IdentitySealer Sealer() => new(new LocalVaultKeyProvider(RandomNumberGenerator.GetBytes(32), "EU"));

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0;
}
