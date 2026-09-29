using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Cadence.Config;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Storage;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class TestClinicalEntity : AggregateRootBase, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public class TestDatabaseContext(DbContextOptions<TestDatabaseContext> options) : CadenceDbContextBase(options)
{
    public DbSet<TestClinicalEntity> TestEntities => Set<TestClinicalEntity>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
}

public class DatabaseAndStorageUnitTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task Database_CadenceDbContextBase_And_EfRepository_SoftDeleteAndMerkleAudit_Succeeds()
    {
        var interceptor = new GxPAuditInterceptor(() => "test-user-001");
        var options = new DbContextOptionsBuilder<TestDatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new TestDatabaseContext(options);
        var repository = new EfRepository<TestClinicalEntity>(context);

        var entity1 = new TestClinicalEntity { Name = "Subject Alpha" };
        var entity2 = new TestClinicalEntity { Name = "Subject Beta" };

        await repository.AddAsync(entity1, TestContext.Current.CancellationToken);
        await repository.AddAsync(entity2, TestContext.Current.CancellationToken);
        await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity1.CreatedAt.Should().NotBe(default);
        entity1.CreatedBy.Should().Be("test-user-001");
        entity1.VersionIndex.Should().Be(1);

        var auditEntries = await context.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken);
        auditEntries.Should().HaveCount(2);
        auditEntries.All(a => !string.IsNullOrEmpty(a.BlockHash)).Should().BeTrue();

        // Soft delete entity1
        await repository.SoftDeleteAsync(entity1, TestContext.Current.CancellationToken);
        await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity1.IsDeleted.Should().BeTrue();
        entity1.DeletedAt.Should().NotBeNull();

        var queryResults = await repository.Query().ToListAsync(TestContext.Current.CancellationToken);
        queryResults.Should().ContainSingle(e => e.Name == "Subject Beta");

        var allResults = await repository.Query(includeSoftDeleted: true).ToListAsync(TestContext.Current.CancellationToken);
        allResults.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("MATCH (p:Protocol)-[r:HAS_STUDY_DESIGN*1..5]->(d:Design) RETURN p, d")]
    [InlineData("MATCH (a)-[:REL*..10]->(b) RETURN a, b")]
    [InlineData("MATCH (a)-[:REL*3]->(b) RETURN a, b")]
    [InlineData("MATCH (a:Subject)-[r:CONSENTED_TO]->(c:Consent) RETURN a")]
    [GxPRequirement("PRD-SYS-001")]
    public void Database_CypherQueryValidator_ValidQueries_PassesValidation(string query)
    {
        Action act = () => CypherQueryValidator.ValidateCypherQuery(query);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("MATCH (a)-[*]-(b) RETURN a, b")]
    [InlineData("MATCH (a)-[:REL*]->(b) RETURN a, b")]
    [InlineData("MATCH (a)-[r*1..]->(b) RETURN a, b")]
    [InlineData("MATCH (a)-[*..]->(b) RETURN a, b")]
    [InlineData("MATCH (a)-[:REL*0..]->(b) RETURN a, b")]
    [GxPRequirement("PRD-SYS-001")]
    public void Database_CypherQueryValidator_UnboundedQueries_ThrowsInvalidOperationException(string query)
    {
        Action act = () => CypherQueryValidator.ValidateCypherQuery(query);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unbounded variable-length traversal pattern detected*");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-001")]
    public async Task Storage_S3StoragePort_PutObjectAsync_ValidatesSha256AndEncryption()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var options = new StorageOptions
        {
            StorageBucket = "clinical-trials-bucket",
            StorageSseAlgorithm = "AES256",
        };
        var port = new S3StoragePort(s3Client, options);

        byte[] payload = "Clinical Trial Document Content"u8.ToArray();
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(payload));

        string returnedHash = await port.PutObjectAsync(
            key: "docs/protocol.pdf",
            data: payload,
            contentType: "application/pdf",
            metadata: new Dictionary<string, string> { { "author", "Dr. Smith" } },
            expectedSha256: expectedHash,
            ct: TestContext.Current.CancellationToken);

        returnedHash.Should().Be(expectedHash);

        await s3Client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r =>
                r.BucketName == "clinical-trials-bucket" &&
                r.Key == "docs/protocol.pdf" &&
                r.ContentType == "application/pdf" &&
                r.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AES256 &&
                r.Metadata["sha256"] == expectedHash &&
                r.Metadata["author"] == "Dr. Smith"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-001")]
    public async Task Storage_S3StoragePort_PutObjectAsync_MismatchedSha256_ThrowsStorageIntegrityException()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var port = new S3StoragePort(s3Client, "test-bucket");

        byte[] payload = "Corrupted Payload"u8.ToArray();
        string invalidExpectedHash = "0000000000000000000000000000000000000000000000000000000000000000";

        Func<Task> act = async () => await port.PutObjectAsync(
            key: "docs/bad.pdf",
            data: payload,
            expectedSha256: invalidExpectedHash,
            ct: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<StorageIntegrityException>()
            .WithMessage("*Storage integrity verification failed*");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-001")]
    public async Task Storage_S3StoragePort_GetObjectAsync_RetrievesDataAndVerifiesHash()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var port = new S3StoragePort(s3Client, "test-bucket");

        byte[] payload = "Retrieved Document Body"u8.ToArray();
        string expectedHash = Convert.ToHexStringLower(SHA256.HashData(payload));

        var getResponse = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(payload),
        };
        getResponse.Metadata["sha256"] = expectedHash;

        s3Client.GetObjectAsync(Arg.Is<GetObjectRequest>(r => r.Key == "docs/valid.pdf"), Arg.Any<CancellationToken>())
            .Returns(getResponse);

        var (data, sha256) = await port.GetObjectAsync("docs/valid.pdf", TestContext.Current.CancellationToken);

        data.Should().Equal(payload);
        sha256.Should().Be(expectedHash);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-001")]
    public async Task Storage_S3StoragePort_GetObjectAsync_MissingKey_ThrowsNotFoundException()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var port = new S3StoragePort(s3Client, "test-bucket");

        var ex = new AmazonS3Exception("The specified key does not exist.", Amazon.Runtime.ErrorType.Sender, "NoSuchKey", "req-1", HttpStatusCode.NotFound);
        s3Client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(ex);

        Func<Task> act = async () => await port.GetObjectAsync("docs/missing.pdf", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<StorageObjectNotFoundException>()
            .WithMessage("*Object not found: docs/missing.pdf*");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-002")]
    public async Task Storage_S3StoragePort_PresignedUrls_ExecutesUnderPerformanceGuardrail()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        s3Client.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>())
            .Returns(Task.FromResult("https://s3.amazonaws.com/test-bucket/doc.pdf?signature=123"));

        var port = new S3StoragePort(s3Client, "test-bucket");

        var sw = Stopwatch.StartNew();
        string getUrl = await port.GeneratePresignedGetUrlAsync("doc.pdf", expiresIn: 1800, responseContentDisposition: "attachment; filename=\"doc.pdf\"", ct: TestContext.Current.CancellationToken);
        string putUrl = await port.GeneratePresignedPutUrlAsync("doc.pdf", expiresIn: 1800, contentType: "application/pdf", ct: TestContext.Current.CancellationToken);
        sw.Stop();

        getUrl.Should().Contain("signature=123");
        putUrl.Should().Contain("signature=123");
        sw.ElapsedMilliseconds.Should().BeLessThan(50, "Presigned URL generation must complete instantly");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-DOC-002")]
    public async Task Storage_S3StoragePort_MultipartUploadLifecycle_Succeeds()
    {
        var s3Client = Substitute.For<IAmazonS3>();
        var port = new S3StoragePort(s3Client, "test-bucket");

        s3Client.InitiateMultipartUploadAsync(Arg.Any<InitiateMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitiateMultipartUploadResponse { UploadId = "upload-999" });

        s3Client.GetPreSignedURLAsync(Arg.Any<GetPreSignedUrlRequest>())
            .Returns(Task.FromResult("https://s3.amazonaws.com/test-bucket/large.bin?partNumber=1&uploadId=upload-999"));

        s3Client.CompleteMultipartUploadAsync(Arg.Any<CompleteMultipartUploadRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CompleteMultipartUploadResponse { ETag = "\"etag-completed-123\"" });

        string uploadId = await port.CreateMultipartUploadAsync("large.bin", contentType: "application/octet-stream", ct: TestContext.Current.CancellationToken);
        uploadId.Should().Be("upload-999");

        var partUrls = await port.GeneratePresignedMultipartUrlsAsync("large.bin", uploadId, [1, 2], expiresIn: 3600, ct: TestContext.Current.CancellationToken);
        partUrls.Should().HaveCount(2);
        partUrls[1].Should().Contain("uploadId=upload-999");

        var parts = new List<IReadOnlyDictionary<string, object>>
        {
            new Dictionary<string, object> { { "PartNumber", 2 }, { "ETag", "\"etag-2\"" } },
            new Dictionary<string, object> { { "PartNumber", 1 }, { "ETag", "\"etag-1\"" } },
        };

        string finalETag = await port.CompleteMultipartUploadAsync("large.bin", uploadId, parts, ct: TestContext.Current.CancellationToken);
        finalETag.Should().Be("\"etag-completed-123\"");

        await port.AbortMultipartUploadAsync("large.bin", uploadId, ct: TestContext.Current.CancellationToken);
        await s3Client.Received(1).AbortMultipartUploadAsync(Arg.Is<AbortMultipartUploadRequest>(r => r.UploadId == "upload-999"), Arg.Any<CancellationToken>());
    }
}
