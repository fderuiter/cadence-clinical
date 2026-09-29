using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Xunit;

using Cadence.Core.Domain;
using Cadence.Storage;
using Cadence.Testing;
using Cadence.Testing.Fakes;

namespace Cadence.UnitTests.Testing;

public class HexagonalFakesTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryStoragePort_PutsAndGetsObjectSuccessfully()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryStoragePort();
        byte[] content = Encoding.UTF8.GetBytes("Test Document Content");
        string expectedSha = Convert.ToHexStringLower(SHA256.HashData(content));

        string sha = await storage.PutObjectAsync("docs/test.txt", content, contentType: "text/plain", ct: ct);
        sha.Should().Be(expectedSha);

        bool exists = await storage.ExistsAsync("docs/test.txt", ct: ct);
        exists.Should().BeTrue();

        var (data, retrievedSha) = await storage.GetObjectAsync("docs/test.txt", ct: ct);
        data.Should().Equal(content);
        retrievedSha.Should().Be(expectedSha);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryStoragePort_ThrowsIntegrityExceptionOnShaMismatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryStoragePort();
        byte[] content = Encoding.UTF8.GetBytes("Test Document Content");

        var act = async () => await storage.PutObjectAsync("docs/test.txt", content, expectedSha256: "invalid_hash_value", ct: ct);
        await act.Should().ThrowAsync<StorageIntegrityException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryStoragePort_SupportsMultipartUploadFlow()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryStoragePort();
        string uploadId = await storage.CreateMultipartUploadAsync("docs/large.bin", contentType: "application/octet-stream", ct: ct);
        uploadId.Should().StartWith("mpu-");

        var urls = await storage.GeneratePresignedMultipartUrlsAsync("docs/large.bin", uploadId, new[] { 1, 2, 3 }, ct: ct);
        urls.Should().HaveCount(3);

        string result = await storage.CompleteMultipartUploadAsync("docs/large.bin", uploadId, new List<IReadOnlyDictionary<string, object>>(), ct: ct);
        result.Should().NotBeNullOrEmpty();

        bool exists = await storage.ExistsAsync("docs/large.bin", ct: ct);
        exists.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task FakeAccessibilityValidator_ReturnsPassAndFailReports()
    {
        var ct = TestContext.Current.CancellationToken;
        var passValidator = new FakeAccessibilityValidator(shouldFail: false);
        var passReport = await passValidator.ValidateAsync("<openrosa>valid</openrosa>", ct: ct);
        passReport.Status.Should().Be("PASS");
        passReport.FormHash.Should().NotBeEmpty();

        var failValidator = new FakeAccessibilityValidator(shouldFail: true);
        var failReport = await failValidator.ValidateAsync("<openrosa>invalid</openrosa>", ct: ct);
        failReport.Status.Should().Be("FAIL");
        failReport.Violations.Should().NotBeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryNotificationDispatcher_RecordsDispatchedMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var dispatcher = new InMemoryNotificationDispatcher();
        await dispatcher.DispatchAsync("cra@example.com", "Alert", "Observation requires review", ct: ct);

        dispatcher.DispatchedMessages.Should().HaveCount(1);
        dispatcher.DispatchedMessages[0].Recipient.Should().Be("cra@example.com");
        dispatcher.DispatchedMessages[0].Subject.Should().Be("Alert");

        dispatcher.Clear();
        dispatcher.DispatchedMessages.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task InMemoryRepository_StoresAndRetrievesEntities()
    {
        var ct = TestContext.Current.CancellationToken;
        var repo = new InMemoryRepository<TestAggregate, string>();
        var entity = new TestAggregate("E-001");

        await repo.SaveAsync(entity, ct);
        var retrieved = await repo.GetByIdAsync("E-001", ct);
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be("E-001");

        var all = await repo.GetAllAsync(ct);
        all.Should().HaveCount(1);

        bool deleted = await repo.DeleteAsync("E-001", ct);
        deleted.Should().BeTrue();
    }

    private class TestAggregate : AggregateRoot<string>
    {
        public TestAggregate(string id)
        {
            Id = id;
        }
    }
}
