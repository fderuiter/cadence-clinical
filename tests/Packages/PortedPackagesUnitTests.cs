using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;

using Xunit;

using Cadence.Compliance;
using Cadence.Ddf;
using Cadence.Deid;
using Cadence.Hexagonal;
using Cadence.Sentinels;
using Cadence.Storage;
using Cadence.Testing;
using Cadence.ToolingCore;

namespace Cadence.UnitTests.Packages;

public class PortedPackagesUnitTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void Compliance_AuditDigestCalculator_ComputesExpectedHash()
    {
        var digest = AuditDigestCalculator.ComputeAuditDigest(
            eventId: "evt-001",
            serviceName: "test-service",
            actionType: "CREATE",
            entityName: "subject",
            entityId: "subj-101",
            userId: "user-1",
            tenantId: "tenant-1",
            reasonForChange: "Initial registration",
            timestamp: "2026-09-16T00:00:00Z",
            previousDigest: "GENESIS_BLOCK",
            secretKey: "test-secret"
        );

        digest.Should().NotBeNullOrWhiteSpace();
        digest.Length.Should().Be(64);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void Compliance_AuditChainVerifier_ValidatesIntactChain()
    {
        var rec = new ComplianceAuditLogRecord(
            RecordId: "rec-1",
            Action: "CREATE",
            UserId: "user-1",
            ChangeReason: "Test change",
            PreviousDigest: "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000"
        );

        var result = AuditChainVerifier.VerifyAuditChain([rec], secretKey: "test-secret");
        result.Valid.Should().BeTrue();
        result.Status.Should().Be("INTACT");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-049")]
    public void Deid_DeidDetector_DetectsAndRedactsEmail()
    {
        var detector = new DeidDetector();
        var text = "Patient email is john.doe@example.com for contact.";
        var results = detector.Detect(text);

        results.Should().ContainSingle();
        results[0].Category.Should().Be(DetectorCategory.Email);

        var redacted = DeidDetector.RedactText(text, results);
        redacted.Should().Be("Patient email is [EMAIL] for contact.");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Ddf_Models_ConstructsSuccessfully()
    {
        var item = new DDFItemIR(Id: "item-1", Name: "Height", Type: "integer");
        var study = new DDFStudyIR(StudyId: "study-1", Name: "Protocol 101", Items: [item]);

        study.StudyId.Should().Be("study-1");
        study.Items.Should().HaveCount(1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Hexagonal_DomainPrimitives_And_ExceptionMapper_Work()
    {
        var exc = new EntityNotFoundException("Subject SUBJ-101 not found");
        var problem = DomainErrorMapper.CreateProblemDetailsFromDomainException(exc);

        problem.Status.Should().Be(404);
        problem.Title.Should().Be("Entity Not Found");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Sentinels_Engine_ExecutesRegisteredCheck()
    {
        var engine = new SentinelEngine();
        var result = await engine.ExecuteAllAsync(ct: TestContext.Current.CancellationToken);
        result.Should().BeEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task Storage_InMemoryBlobStore_StoresAndRetrievesPayload()
    {
        var store = new InMemoryBlobStore();
        var data = "Hello GxP Storage"u8.ToArray();

        var hash = await store.PutObjectAsync("test/file.txt", data, ct: TestContext.Current.CancellationToken);
        hash.Should().NotBeNullOrWhiteSpace();

        var exists = await store.ExistsAsync("test/file.txt", TestContext.Current.CancellationToken);
        exists.Should().BeTrue();

        var (retrieved, retrievedHash) = await store.GetObjectAsync("test/file.txt", TestContext.Current.CancellationToken);
        retrievedHash.Should().Be(hash);
        retrieved.Should().Equal(data);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void ToolingCore_Handlers_ReturnsSuccessfulEnvelope()
    {
        var req = new FastTestRequest(Subsystem: "UnitTests");
        var env = ToolingHandlers.HandleFastTests(req);

        env.Success.Should().BeTrue();
        env.Data.Should().NotBeNull();
        env.Data!.Passed.Should().Be(1);
    }
}
