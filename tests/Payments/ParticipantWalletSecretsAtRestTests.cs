using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Apps.Payments.Infrastructure.Persistence.Configurations;
using Cadence.Security.FieldProtection;
using Cadence.Testing;
using Cadence.Testing.Persistence;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

/// <summary>
/// #4770: participant wallets keep only processor references (encrypted at rest) and masks; card tokens and bank
/// routing/account numbers are never written to the database.
/// </summary>
public sealed class ParticipantWalletSecretsAtRestTests : IDisposable
{
    private const string CardReference = "card_ref_1NzS9f2eZvKYlo2C";
    private const string BankReference = "ba_ref_1NzSA02eZvKYlo2C";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ParticipantWalletSecretsAtRestTests()
    {
        _connection.Open();
        using var context = CreateContext(TestFieldProtection.CreateEncryptor());
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Processor references are stored as AES-GCM ciphertext: the raw column value read with SQL is not, and does not
    /// contain, the plaintext, while the entity read through EF Core decrypts it.
    /// </summary>
    [Fact]
    public async Task ProcessorReferences_AreEncryptedOnDisk_AndDecryptedOnRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = NewWallet();
        await using (var write = CreateContext(TestFieldProtection.CreateEncryptor()))
        {
            write.ParticipantWallets.Add(wallet);
            await write.SaveChangesAsync(ct);
        }

        var stored = (await StoredRowReader.ReadAllAsync(_connection, "payments_participant_wallets", ct)).Single();

        stored.Should().NotContainKeys("VirtualCardToken", "AchRoutingNumber", "AchAccountNumber");
        stored["VirtualCardProcessorReference"].Should().NotBe(CardReference).And.NotContain(CardReference)
            .And.StartWith(AesGcmFieldEncryptor.FormatPrefix);
        stored["AchProcessorReference"].Should().NotBe(BankReference).And.NotContain(BankReference)
            .And.StartWith(AesGcmFieldEncryptor.FormatPrefix);
        stored["AchRoutingMask"].Should().Be("*****6789");
        stored.Values.Should().NotContain(value => value != null && (value.Contains(CardReference) || value.Contains(BankReference)));

        await using var read = CreateContext(TestFieldProtection.CreateEncryptor());
        var loaded = await read.ParticipantWallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id, ct);
        loaded.VirtualCardProcessorReference.Should().Be(CardReference);
        loaded.AchProcessorReference.Should().Be(BankReference);
    }

    /// <summary>
    /// Reading with a different key fails instead of returning ciphertext or null, and the model cache keeps a
    /// separate model per key.
    /// </summary>
    [Fact]
    public async Task ProcessorReferences_ReadWithAnotherKey_Fail()
    {
        var ct = TestContext.Current.CancellationToken;
        var wallet = NewWallet();
        await using (var write = CreateContext(TestFieldProtection.CreateEncryptor()))
        {
            write.ParticipantWallets.Add(wallet);
            await write.SaveChangesAsync(ct);
        }

        await using var read = CreateContext(new AesGcmFieldEncryptor(TestFieldProtection.AlternateEncryptionKeyBase64));
        var load = () => read.ParticipantWallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id, ct);

        await load.Should().ThrowAsync<FieldDecryptionException>();
    }

    /// <summary>
    /// The wallet has no property or column that could hold a raw card token or bank routing/account number.
    /// </summary>
    [Fact]
    public void ParticipantWallet_HasNoRawCardTokenOrBankNumberColumns()
    {
        using var context = CreateContext(TestFieldProtection.CreateEncryptor());
        var entity = context.Model.FindEntityType(typeof(ParticipantWallet))!;
        var properties = entity.GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain(["VirtualCardToken", "AchRoutingNumber", "AchAccountNumber"]);
        entity.FindProperty(nameof(ParticipantWallet.VirtualCardProcessorReference))!.GetValueConverter()
            .Should().BeOfType<EncryptedStringConverter>();
        entity.FindProperty(nameof(ParticipantWallet.AchProcessorReference))!.GetValueConverter()
            .Should().BeOfType<EncryptedStringConverter>();
        entity.FindProperty(nameof(ParticipantWallet.AchProcessorReference))!.GetMaxLength()
            .Should().Be(AesGcmFieldEncryptor.GetMaxStoredLength(ParticipantWalletConfiguration.MaxProcessorReferenceLength));
    }

    private PaymentsDbContext CreateContext(IFieldEncryptor encryptor) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseSqlite(_connection).Options, encryptor);

    private static ParticipantWallet NewWallet() => new()
    {
        SubjectId = "SUBJ-101",
        StudyId = "STUDY-1",
        SiteId = "SITE-101",
        VirtualCardProcessorReference = CardReference,
        VirtualCardMask = "**** **** **** 4242",
        AchProcessorReference = BankReference,
        AchRoutingMask = "*****6789",
        AchAccountMask = "******1234",
        ReasonForChange = "Wallet provisioning",
        CreatedBy = "payments-test",
    };
}
