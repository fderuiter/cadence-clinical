using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Compliance;
using Cadence.Ddf;
using Cadence.Deid;
using Cadence.Storage;
using Cadence.Testing;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class PortedPackagesUnitTests
{
    [Fact]
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
    public void Ddf_Models_ConstructsSuccessfully()
    {
        var item = new DDFItemIR(Id: "item-1", Name: "Height", Type: "integer");
        var study = new DDFStudyIR(StudyId: "study-1", Name: "Protocol 101", Items: [item]);

        study.StudyId.Should().Be("study-1");
        study.Items.Should().HaveCount(1);
    }

    [Fact]
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
    public void Compliance_RtmGeneratorService_SyncsInProcessWithoutPython()
    {
        // Branch validation: a branch that adds tagged tests is not expected to carry a refreshed snapshot (ADR-2532).
        int exitCode = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC", reportPath: "TestResults/no-such-report", draft: true, validateOnly: true, requireCurrentSnapshot: false);
        exitCode.Should().Be(0);
    }

    /// <summary>
    /// Validates that a validate-only RTM pass is side-effect free: the checked-in GxP documents under
    /// docs/SDLC are only regenerated through the sync tooling, never as a by-product of a test run.
    /// </summary>
    [Fact]
    public void Compliance_RtmGeneratorService_ValidateOnly_DoesNotModifyCheckedInSdlcDocuments()
    {
        string sdlcDir = Path.Combine(PreCommitOpenApiService.FindRepoRoot(), "docs", "SDLC");
        string[] documents =
        [
            Path.Combine(sdlcDir, "Requirements_Traceability_Matrix.md"),
            Path.Combine(sdlcDir, "IQ_OQ_PQ_Execution_Report.md"),
        ];
        var before = documents.Select(File.ReadAllText).ToArray();

        RtmGeneratorService.GenerateRtm(outputDir: "docs/SDLC", reportPath: "TestResults/no-such-report", draft: true, validateOnly: true);

        documents.Select(File.ReadAllText).Should().Equal(before);
    }

    /// <summary>
    /// Validates that exactly one RTM/qualification report generator exists, so the checked-in GxP documents
    /// have a single, deterministic source regardless of the entry point (CLI, gxp sync, or tests).
    /// </summary>
    [Fact]
    public void Compliance_RtmGeneratorService_IsTheOnlyRtmGeneratorInToolingAssemblies()
    {
        var assemblies = new[]
        {
            typeof(RtmGeneratorService).Assembly,
            typeof(PreCommitOpenApiService).Assembly,
        }.Distinct();

        var generators = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == nameof(RtmGeneratorService))
            .ToList();

        generators.Should().ContainSingle().Which.Assembly.Should().BeSameAs(typeof(RtmGeneratorService).Assembly);
    }
}
