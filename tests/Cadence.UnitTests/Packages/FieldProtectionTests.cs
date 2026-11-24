using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Field-level protection primitives in <c>Cadence.Security.FieldProtection</c> (#4770): AES-256-GCM column
/// encryption, HMAC-SHA256 lookup hashes and their fail-closed key configuration.
/// </summary>
public class FieldProtectionTests
{
    private const string Purpose = "tests.table.column";

    /// <summary>
    /// An encrypted value round-trips, carries the format prefix and key id, and never contains the plaintext.
    /// </summary>
    [Fact]
    public void AesGcmFieldEncryptor_RoundTrips_AndStoredValueHidesPlaintext()
    {
        var encryptor = TestFieldProtection.CreateEncryptor();
        const string plaintext = "signer@example.org";

        string stored = encryptor.Encrypt(plaintext, Purpose);

        stored.Should().StartWith($"{AesGcmFieldEncryptor.FormatPrefix}{encryptor.KeyId}:");
        stored.Should().NotContain(plaintext);
        stored.Should().NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext)));
        stored.Length.Should().BeLessThanOrEqualTo(AesGcmFieldEncryptor.GetMaxStoredLength(plaintext.Length));
        encryptor.Decrypt(stored, Purpose).Should().Be(plaintext);
    }

    /// <summary>
    /// Encryption is randomised: the same plaintext yields different stored values.
    /// </summary>
    [Fact]
    public void AesGcmFieldEncryptor_SamePlaintext_ProducesDifferentCiphertexts()
    {
        var encryptor = TestFieldProtection.CreateEncryptor();

        encryptor.Encrypt("same", Purpose).Should().NotBe(encryptor.Encrypt("same", Purpose));
    }

    /// <summary>
    /// A value decrypted under another purpose (column), another key, after tampering, or that was never
    /// encrypted (legacy plaintext) fails loudly instead of returning data.
    /// </summary>
    [Fact]
    public void AesGcmFieldEncryptor_RejectsWrongPurposeWrongKeyTamperingAndPlaintext()
    {
        var encryptor = TestFieldProtection.CreateEncryptor();
        var otherKey = new AesGcmFieldEncryptor(TestFieldProtection.AlternateEncryptionKeyBase64);
        string stored = encryptor.Encrypt("secret-value", Purpose);

        int payloadStart = stored.LastIndexOf(':') + 1;
        byte[] blob = Convert.FromBase64String(stored[payloadStart..]);
        blob[blob.Length / 2] ^= 0x01;
        string tampered = stored[..payloadStart] + Convert.ToBase64String(blob);
        string relabelled = stored.Replace(encryptor.KeyId, otherKey.KeyId, StringComparison.Ordinal);

        FluentActions.Invoking(() => encryptor.Decrypt(stored, "tests.other.column")).Should().Throw<FieldDecryptionException>();
        FluentActions.Invoking(() => otherKey.Decrypt(stored, Purpose)).Should().Throw<FieldDecryptionException>();
        FluentActions.Invoking(() => otherKey.Decrypt(relabelled, Purpose)).Should().Throw<FieldDecryptionException>();
        FluentActions.Invoking(() => encryptor.Decrypt(tampered, Purpose)).Should().Throw<FieldDecryptionException>();
        FluentActions.Invoking(() => encryptor.Decrypt("secret-value", Purpose)).Should().Throw<FieldDecryptionException>();
        otherKey.KeyId.Should().NotBe(encryptor.KeyId);
    }

    /// <summary>
    /// There is no default or fallback key: missing, malformed and wrong-length keys are rejected.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 !!")]
    [InlineData("c2hvcnQta2V5")]
    [InlineData("Y2FkZW5jZS10ZXN0LWZpZWxkLWVuY3J5cHRpb24tMDEtdG9vLWxvbmc=")] // pragma: allowlist secret
    public void FieldProtectionKeys_MissingOrInvalid_Throw(string? base64Key)
    {
        FieldProtectionKey.IsValid(base64Key).Should().BeFalse();
        FluentActions.Invoking(() => new AesGcmFieldEncryptor(base64Key)).Should().Throw<InvalidOperationException>()
            .WithMessage($"*{FieldEncryptionOptions.ConfigurationKey}*");
        FluentActions.Invoking(() => new HmacLookupHasher(base64Key)).Should().Throw<InvalidOperationException>()
            .WithMessage($"*{LookupHashOptions.ConfigurationKey}*");
    }

    /// <summary>
    /// Lookup hashes are deterministic 64-character hex HMACs that depend on the key and purpose and differ from an
    /// unkeyed SHA-256 of the value (so they cannot be recomputed without the key).
    /// </summary>
    [Fact]
    public void HmacLookupHasher_IsDeterministicKeyedAndPurposeSeparated()
    {
        var hasher = TestFieldProtection.CreateLookupHasher();
        var otherKey = new HmacLookupHasher(TestFieldProtection.EncryptionKeyBase64);
        const string token = "device-token-0001";

        string hash = hasher.ComputeHash(token, Purpose);

        hash.Should().MatchRegex("^[0-9a-f]{64}$").And.HaveLength(ILookupHasher.HashLength);
        hasher.ComputeHash(token, Purpose).Should().Be(hash);
        hasher.ComputeHash(token, "tests.other.column").Should().NotBe(hash);
        otherKey.ComputeHash(token, Purpose).Should().NotBe(hash);
        hash.Should().NotBe(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        hash.Should().NotContain(token);
    }

    /// <summary>
    /// Batch encryption reuses one cipher context per batch while drawing a fresh 96-bit nonce per item, producing
    /// wire-compatible values that round-trip through <see cref="IFieldEncryptor.Decrypt"/> and bind associated data.
    /// </summary>
    [Fact]
    public void AesGcmFieldEncryptor_EncryptBatch_RoundTripsUsesFreshNoncesAndAuthenticatesPurpose()
    {
        var encryptor = TestFieldProtection.CreateEncryptor();
        var otherKey = new AesGcmFieldEncryptor(TestFieldProtection.AlternateEncryptionKeyBase64);
        string largePlaintext = new('X', 2_048);
        string largePurpose = $"tests.batch.{new string('p', 1_024)}";

        var items = new List<(string Plaintext, string Purpose)>
        {
            ("ACTIVE", "execution.randomization.slot:1:DEFAULT:0"),
            ("ACTIVE", "execution.randomization.slot:1:DEFAULT:0"),
            ("ACTIVE", "execution.randomization.slot:1:DEFAULT:1"),
            ("PLACEBO", "execution.randomization.slot:1:DEFAULT:2"),
            (largePlaintext, largePurpose),
        };
        for (int i = 3; i < 64; i++)
        {
            items.Add(("ACTIVE", Purpose));
        }

        IReadOnlyList<string> stored = encryptor.EncryptBatch(items);

        stored.Should().HaveCount(items.Count);
        encryptor.EncryptBatch([]).Should().BeEmpty();

        int payloadOffset = $"{AesGcmFieldEncryptor.FormatPrefix}{encryptor.KeyId}:".Length;
        var nonces = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            stored[i].Should().StartWith($"{AesGcmFieldEncryptor.FormatPrefix}{encryptor.KeyId}:");
            encryptor.Decrypt(stored[i], items[i].Purpose).Should().Be(items[i].Plaintext);

            byte[] raw = Convert.FromBase64String(stored[i][payloadOffset..]);
            nonces.Add(Convert.ToHexString(raw.AsSpan(0, 12))).Should().BeTrue("every item in a batch must draw a unique 96-bit nonce");
        }

        stored[0].Should().NotBe(stored[1]);
        FluentActions.Invoking(() => encryptor.Decrypt(stored[0], items[2].Purpose)).Should().Throw<FieldDecryptionException>();
        FluentActions.Invoking(() => otherKey.Decrypt(stored[0], items[0].Purpose)).Should().Throw<FieldDecryptionException>();
    }

    /// <summary>
    /// Singleton <see cref="AesGcmFieldEncryptor"/> instances safely execute concurrent <see cref="IFieldEncryptor.EncryptBatch"/> calls.
    /// </summary>
    [Fact]
    public async Task AesGcmFieldEncryptor_EncryptBatch_IsThreadSafeAcrossConcurrentCallers()
    {
        var encryptor = TestFieldProtection.CreateEncryptor();
        var tasks = Enumerable.Range(0, 16).Select(worker => Task.Run(() =>
        {
            var batch = Enumerable.Range(0, 128)
                .Select(seq => ($"ARM-{worker}-{seq}", $"execution.randomization.slot:{worker}:DEFAULT:{seq}"))
                .ToArray();
            var encrypted = encryptor.EncryptBatch(batch);
            for (int i = 0; i < batch.Length; i++)
            {
                encryptor.Decrypt(encrypted[i], batch[i].Item2).Should().Be(batch[i].Item1);
            }
        }, TestContext.Current.CancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// The design-time encryptor used by <c>dotnet ef</c> factories cannot encrypt or decrypt, so it can never act
    /// as a keyless runtime fallback.
    /// </summary>
    [Fact]
    public void DesignTimeFieldEncryptor_RefusesToConvertValues()
    {
        FluentActions.Invoking(() => DesignTimeFieldEncryptor.Instance.Encrypt("x", Purpose)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => DesignTimeFieldEncryptor.Instance.EncryptBatch([("x", Purpose)])).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => DesignTimeFieldEncryptor.Instance.Decrypt("x", Purpose)).Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// A host that registers field encryption and lookup hashing without keys refuses to start (fail closed).
    /// </summary>
    [Fact]
    public async Task Host_WithoutFieldProtectionKeys_FailsAtStartup()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.AddCadenceFieldEncryption();
        builder.Services.AddCadenceLookupHashing();
        using var host = builder.Build();

        var start = () => host.StartAsync(TestContext.Current.CancellationToken);

        // Both registrations fail validation, so the host reports them together.
        var failure = await start.Should().ThrowAsync<AggregateException>();
        failure.Which.InnerExceptions.Should().AllBeOfType<OptionsValidationException>();
        failure.Which.Message.Should().Contain(FieldEncryptionOptions.ConfigurationKey).And.Contain(LookupHashOptions.ConfigurationKey);
    }

    /// <summary>
    /// With keys supplied through configuration the host starts and resolves working services.
    /// </summary>
    [Fact]
    public async Task Host_WithFieldProtectionKeys_StartsAndResolvesServices()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(TestFieldProtection.ConfigurationValues));
        builder.Services.AddCadenceFieldEncryption();
        builder.Services.AddCadenceLookupHashing();
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var encryptor = host.Services.GetRequiredService<IFieldEncryptor>();
        var hasher = host.Services.GetRequiredService<ILookupHasher>();
        encryptor.KeyId.Should().Be(TestFieldProtection.CreateEncryptor().KeyId);
        hasher.ComputeHash("v", Purpose).Should().Be(TestFieldProtection.CreateLookupHasher().ComputeHash("v", Purpose));

        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
