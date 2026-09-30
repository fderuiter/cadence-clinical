using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Persistence;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Econsent;

/// <summary>
/// #4770: consent signatures store the signer e-mail encrypted and bind the signature capture to the signed content
/// with a SHA-256 hash instead of storing the raw capture.
/// </summary>
public sealed class ConsentSignatureSecretsAtRestTests : IDisposable
{
    private const string SignerEmail = "legally.authorized.rep@example.org";
    private const string DocumentDigest = "abc123";
    private const string SignatureCapture = "data:image/png;base64,AAAA";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ConsentSignatureSecretsAtRestTests()
    {
        _connection.Open();
        using var context = CreateContext(TestFieldProtection.CreateEncryptor());
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// The signer e-mail column holds AES-GCM ciphertext on disk (raw SQL read), no column holds the raw signature
    /// capture, and the entity read through EF Core decrypts the e-mail.
    /// </summary>
    [Fact]
    public async Task SignerEmail_IsEncryptedOnDisk_AndSignatureCaptureIsNotStored()
    {
        var ct = TestContext.Current.CancellationToken;
        var signature = NewSignature();
        await using (var write = CreateContext(TestFieldProtection.CreateEncryptor()))
        {
            write.ConsentSignatures.Add(signature);
            await write.SaveChangesAsync(ct);
        }

        var columns = (await StoredRowReader.ReadAllAsync(_connection, "consent_signatures", ct)).Single();

        columns.Should().NotContainKey("SignatureData");
        columns["SignerEmail"].Should().StartWith(AesGcmFieldEncryptor.FormatPrefix).And.NotContain(SignerEmail);
        columns["SignatureBindingSha256"].Should().Be(ConsentSignatureBinding.Compute(DocumentDigest, SignatureCapture));
        columns.Values.Should().NotContain(value => value != null && (value.Contains(SignerEmail) || value.Contains(SignatureCapture)));

        await using var read = CreateContext(TestFieldProtection.CreateEncryptor());
        var loaded = await read.ConsentSignatures.AsNoTracking().SingleAsync(s => s.Id == signature.Id, ct);
        loaded.SignerEmail.Should().Be(SignerEmail);
    }

    /// <summary>
    /// Reading an encrypted e-mail with a different key fails loudly.
    /// </summary>
    [Fact]
    public async Task SignerEmail_ReadWithAnotherKey_Fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var signature = NewSignature();
        await using (var write = CreateContext(TestFieldProtection.CreateEncryptor()))
        {
            write.ConsentSignatures.Add(signature);
            await write.SaveChangesAsync(ct);
        }

        await using var read = CreateContext(new AesGcmFieldEncryptor(TestFieldProtection.AlternateEncryptionKeyBase64));
        var load = () => read.ConsentSignatures.AsNoTracking().SingleAsync(s => s.Id == signature.Id, ct);

        await load.Should().ThrowAsync<FieldDecryptionException>();
    }

    /// <summary>
    /// The signature binding is SHA-256 over the documented canonical input (the same expression the migration
    /// evaluates in SQL), verifies the original capture and rejects a different capture or document digest.
    /// </summary>
    [Fact]
    public void SignatureBinding_MatchesCanonicalSha256_AndDetectsSubstitution()
    {
        // printf 'cadence.econsent.signature-binding.v1\nabc123\ndata:image/png;base64,AAAA' | sha256sum
        const string expected = "03193dd31367955ba2679fcc2265eb23a387ee34ca96fc9b723f7680db5b1351"; // pragma: allowlist secret

        string binding = ConsentSignatureBinding.Compute(DocumentDigest, SignatureCapture);

        binding.Should().Be(expected);
        ConsentSignatureBinding.Matches(binding, DocumentDigest, SignatureCapture).Should().BeTrue();
        ConsentSignatureBinding.Matches(binding.ToUpperInvariant(), DocumentDigest, SignatureCapture).Should().BeTrue();
        ConsentSignatureBinding.Matches(binding, DocumentDigest, SignatureCapture + "A").Should().BeFalse();
        ConsentSignatureBinding.Matches(binding, "def456", SignatureCapture).Should().BeFalse();
        ConsentSignatureBinding.Matches(null, DocumentDigest, SignatureCapture).Should().BeFalse();
    }

    /// <summary>
    /// The model maps no raw signature column and encrypts the signer e-mail.
    /// </summary>
    [Fact]
    public void ConsentSignatureModel_EncryptsEmail_AndHasNoRawSignatureColumn()
    {
        using var context = CreateContext(TestFieldProtection.CreateEncryptor());
        var entity = context.Model.FindEntityType(typeof(ConsentSignature))!;

        entity.GetProperties().Select(p => p.Name).Should().NotContain("SignatureData");
        entity.FindProperty(nameof(ConsentSignature.SignerEmail))!.GetValueConverter().Should().BeOfType<EncryptedStringConverter>();
        entity.FindProperty(nameof(ConsentSignature.SignatureBindingSha256))!.GetMaxLength().Should().Be(64);
    }

    private EconsentDbContext CreateContext(IFieldEncryptor encryptor) =>
        new(new DbContextOptionsBuilder<EconsentDbContext>().UseSqlite(_connection).Options, encryptor);

    private static ConsentSignature NewSignature() => new()
    {
        TemplateId = "TPL-1",
        VersionIndex = 1,
        SubjectPseudonym = "SUBJ-101",
        Role = "LAR",
        SignerName = "A. Representative",
        SignerEmail = SignerEmail,
        Meaning = "Consent on behalf of subject",
        DigestSha256 = DocumentDigest,
        SignatureBindingSha256 = ConsentSignatureBinding.Compute(DocumentDigest, SignatureCapture),
        SignedAt = FakeClock.DefaultStart,
        CreatedBy = "investigator-1",
        ReasonForChange = "Consent signed",
    };
}
