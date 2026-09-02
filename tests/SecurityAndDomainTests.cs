using System;
using System.Threading.Tasks;

using FluentAssertions;
using Xunit;

using Cadence.Core.Domain;
using Cadence.Security;
using Cadence.Testing;

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
    [Fact]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-053")]
    public void HmacSigner_GeneratesDeterministicSignature_AndVerifiesCorrectly()
    {
        /// <summary>
        /// Validates deterministic HMAC-SHA256 signature computation and fixed-time verification.
        /// @req:PRD-SYS-053
        /// </summary>
        
        // Arrange
        const string payload = "{\"subject_id\":\"SUBJ-101\",\"action\":\"RANDOMIZE\"}";
        const string secret = "internal-test-secret-key-12345";

        // Act
        string signature = HmacSigner.ComputeSignature(payload, secret);
        bool isValid = HmacSigner.VerifySignature(payload, secret, signature);
        bool isInvalid = HmacSigner.VerifySignature(payload, "wrong-secret", signature);

        // Assert
        signature.Should().NotBeNullOrWhiteSpace();
        isValid.Should().BeTrue();
        isInvalid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Unit")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryRepository_PersistsEntity_AndIncrementsVersionIndex()
    {
        /// <summary>
        /// Validates that in-memory repository fakes capture mutations and monotonically advance GxP version index.
        /// @req:PRD-SYS-053
        /// </summary>

        // Arrange
        var repo = new InMemoryRepository<TestSubject, string>();
        var subject = new TestSubject("SUBJ-001", "SITE-A")
        {
            CreatedBy = "investigator@site.org",
            ReasonForChange = "Initial subject registration"
        };

        // Act
        await repo.SaveAsync(subject);
        var retrieved = await repo.GetByIdAsync("SUBJ-001");

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.SiteId.Should().Be("SITE-A");
        retrieved.VersionIndex.Should().Be(2);
        retrieved.ReasonForChange.Should().Be("Initial subject registration");
    }
}

