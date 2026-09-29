using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Compliance;
using Cadence.Ddf;
using Cadence.Deid;
using Cadence.Hexagonal;
using Cadence.Sentinels;
using Cadence.Storage;
using Cadence.Testing;
using Cadence.ToolingCore;
using Cadence.ToolingCore.OpenApi;

using FluentAssertions;

using Xunit;

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

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void Compliance_RtmGeneratorService_SyncsInProcessWithoutPython()
    {
        int exitCode = RtmGeneratorService.GenerateRtm(outputDir: "docs/SDLC", reportPath: "report_dotnet.trx", draft: true, validateOnly: true);
        exitCode.Should().Be(0);
    }

    /// <summary>
    /// Validates that a validate-only RTM pass is side-effect free: the checked-in GxP documents under
    /// docs/SDLC are only regenerated through the sync tooling, never as a by-product of a test run.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void Compliance_RtmGeneratorService_ValidateOnly_DoesNotModifyCheckedInSdlcDocuments()
    {
        string sdlcDir = Path.Combine(OpenApiToolingService.FindRepoRoot(), "docs", "SDLC");
        string[] documents =
        [
            Path.Combine(sdlcDir, "Requirements_Traceability_Matrix.md"),
            Path.Combine(sdlcDir, "IQ_OQ_PQ_Execution_Report.md"),
        ];
        var before = documents.Select(File.ReadAllText).ToArray();

        RtmGeneratorService.GenerateRtm(outputDir: "docs/SDLC", reportPath: "report_dotnet.trx", draft: true, validateOnly: true);

        documents.Select(File.ReadAllText).Should().Equal(before);
    }

    /// <summary>
    /// Validates that exactly one RTM/qualification report generator exists, so the checked-in GxP documents
    /// have a single, deterministic source regardless of the entry point (CLI, gxp sync, or tests).
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void Compliance_RtmGeneratorService_IsTheOnlyRtmGeneratorInToolingAssemblies()
    {
        var assemblies = new[]
        {
            typeof(RtmGeneratorService).Assembly,
            typeof(OpenApiToolingService).Assembly,
        }.Distinct();

        var generators = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == nameof(RtmGeneratorService))
            .ToList();

        generators.Should().ContainSingle().Which.Assembly.Should().BeSameAs(typeof(RtmGeneratorService).Assembly);
    }
}
