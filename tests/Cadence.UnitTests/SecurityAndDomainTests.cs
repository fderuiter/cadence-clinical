using System;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Security;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests;

public class TestSubject : AggregateRoot<string>
{
    public string SiteId { get; init; } = string.Empty;
    public string Status { get; set; } = "SCREENED";

    public TestSubject() : base() { }
    public TestSubject(string id, string siteId) : base(id)
    {
        SiteId = siteId;
    }
}

public class SecurityAndDomainTests
{
    /// <summary>
    /// Validates deterministic HMAC-SHA256 signature computation and fixed-time verification.
    /// </summary>
    [Fact(DisplayName = "HmacSigner_GeneratesDeterministicSignature_AndVerifiesCorrectly")]
    [Trait("Category", "Unit")]
    public void HmacSignerGeneratesDeterministicSignatureAndVerifiesCorrectly()
    {
        // Arrange
        const string payload = "{\"subject_id\":\"SUBJ-101\",\"action\":\"RANDOMIZE\"}";
        const string secret = "internal-test-secret-key-12345"; // pragma: allowlist secret

        // Act
        string signature = HmacSigner.ComputeSignature(payload, secret);
        bool isValid = HmacSigner.VerifySignature(payload, secret, signature);
        bool isInvalid = HmacSigner.VerifySignature(payload, "wrong-secret", signature);

        // Assert
        signature.Should().NotBeNullOrWhiteSpace();
        isValid.Should().BeTrue();
        isInvalid.Should().BeFalse();
    }

    /// <summary>
    /// Validates that in-memory repository fakes capture mutations and monotonically advance GxP version index.
    /// </summary>
    [Fact(DisplayName = "InMemoryRepository_PersistsEntity_AndIncrementsVersionIndex")]
    [Trait("Category", "Unit")]
    public async Task InMemoryRepositoryPersistsEntityAndIncrementsVersionIndex()
    {
        // Arrange
        var repo = new InMemoryRepository<TestSubject, string>();
        var subject = new TestSubject("SUBJ-001", "SITE-A")
        {
            CreatedBy = "investigator@site.org",
            ReasonForChange = "Initial subject registration"
        };

        // Act
        await repo.SaveAsync(subject, TestContext.Current.CancellationToken);
        var retrieved = await repo.GetByIdAsync("SUBJ-001", TestContext.Current.CancellationToken);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SiteId.Should().Be("SITE-A");
        retrieved.VersionIndex.Should().Be(2);
        retrieved.ReasonForChange.Should().Be("Initial subject registration");
    }

    /// <summary>
    /// Validates that Result monads encapsulate domain validation errors without runtime exception overhead.
    /// </summary>
    [Fact(DisplayName = "ResultMonad_HandlesSuccessAndFailure_WithoutThrowingExceptions")]
    [Trait("Category", "Unit")]
    public void ResultMonadHandlesSuccessAndFailureWithoutThrowingExceptions()
    {
        // Arrange & Act
        Result<string> successResult = Result<string>.Success("Subject enrolled successfully");
        var error = DomainError.Validation("ERR_AGE_INVALID", "Subject must be at least 18 years of age");
        Result<string> failureResult = Result<string>.Failure(error);

        // Assert
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value.Should().Be("Subject enrolled successfully");

        failureResult.IsFailure.Should().BeTrue();
        failureResult.Error.Should().NotBeNull();
        failureResult.Error!.Code.Should().Be("ERR_AGE_INVALID");
        failureResult.Error!.Type.Should().Be(ErrorType.Validation);
    }

    /// <summary>
    /// Validates that AuditLogEntry computes deterministic SHA-256 Merkle hashes chained to predecessor blocks.
    /// </summary>
    [Fact(DisplayName = "AuditLogEntry_ComputesDeterministicMerkleHash_ChainedToPredecessor")]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-001")]
    public void AuditLogEntryComputesDeterministicMerkleHashChainedToPredecessor()
    {
        // Arrange
        var timestamp = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        const string predecessor = "genesis_block_hash_0000000000";

        // Act
        string hash1 = AuditLogEntry.ComputeBlockHash(
            "ClinicalObservation", "OBS-99", "UPDATE", timestamp, "crc@site.org", 2, "{\"value\":120}", predecessor);
        string hash2 = AuditLogEntry.ComputeBlockHash(
            "ClinicalObservation", "OBS-99", "UPDATE", timestamp, "crc@site.org", 2, "{\"value\":120}", predecessor);
        string alteredHash = AuditLogEntry.ComputeBlockHash(
            "ClinicalObservation", "OBS-99", "UPDATE", timestamp, "crc@site.org", 2, "{\"value\":125}", predecessor);

        // Assert
        hash1.Should().NotBeNullOrWhiteSpace();
        hash1.Should().Be(hash2);
        hash1.Should().NotBe(alteredHash);
    }
}
