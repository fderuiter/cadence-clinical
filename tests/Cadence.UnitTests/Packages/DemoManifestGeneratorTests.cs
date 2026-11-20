using System;
using System.IO;
using System.Linq;
using System.Text;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DemoManifestGeneratorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"demo-manifest-test-{Guid.NewGuid():N}");

    public DemoManifestGeneratorTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void DeriveUuidV8_ProducesDeterministicRfc9562Version8Uuid()
    {
        var first = DemoManifestGeneratorService.DeriveUuidV8("cadence-local", "study", "CADENCE-101");
        var second = DemoManifestGeneratorService.DeriveUuidV8("cadence-local", "study", "CADENCE-101");
        var other = DemoManifestGeneratorService.DeriveUuidV8("cadence-local", "study", "CADENCE-102");

        first.Should().Be(second);
        first.Should().NotBe(other);

        byte[] bytes = first.ToByteArray(bigEndian: true);
        ((bytes[6] >> 4) & 0x0F).Should().Be(8, "UUID version nibble must be 8 (RFC 9562)");
        ((bytes[8] >> 6) & 0x03).Should().Be(2, "UUID variant bits must be RFC 9562 (10xx)");
    }

    [Fact]
    public void GenerateDemoManifest_WritesByteIdenticalManifestForCanonicalStudyAndEightRealmPersonas()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        byte[] firstJsBytes = File.ReadAllBytes(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestJsRelativePath));
        byte[] firstDtsBytes = File.ReadAllBytes(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestDtsRelativePath));

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        byte[] secondJsBytes = File.ReadAllBytes(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestJsRelativePath));
        byte[] secondDtsBytes = File.ReadAllBytes(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestDtsRelativePath));

        secondJsBytes.Should().Equal(firstJsBytes);
        secondDtsBytes.Should().Equal(firstDtsBytes);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);

        DemoManifestGeneratorService.TryBuildExpectedArtifacts(
            _tempRoot,
            out var snapshot,
            out _,
            out _,
            out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        snapshot.Should().NotBeNull();
        snapshot!.EpochIso.Should().Be("2026-01-05T09:00:00Z"); // deid-ignore: canonical demo epoch assertion
        snapshot.Sponsor.Code.Should().Be("CADENCE-PHARMA");
        snapshot.Study.Code.Should().Be("CADENCE-101");
        snapshot.Sites.Select(s => s.Code).Should().Equal("SITE-101", "SITE-102");
        snapshot.Participants.Select(p => (p.SubjectCode, p.SiteCode)).Should().Equal(
            ("SUBJ-101-001", "SITE-101"),
            ("SUBJ-101-002", "SITE-101"),
            ("SUBJ-102-001", "SITE-102"));
        snapshot.Personas.Select(p => p.Username).Should().Equal(
            "crc.site101",
            "pi.site101",
            "cra.monitor",
            "dm.lead",
            "designer_demo",
            "designer.lead",
            "safety.officer",
            "auditor.lead");
    }

    [Fact]
    public void GenerateDemoManifest_ValidateOnly_FailsWhenManifestOrBackendSeedDrifts()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);

        string manifestPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestJsRelativePath);
        string originalManifest = File.ReadAllText(manifestPath, Encoding.UTF8);
        File.WriteAllText(manifestPath, originalManifest.Replace("CADENCE-101", "DRIFTED-999", StringComparison.Ordinal), Encoding.UTF8);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);

        // Restore manifest, then mutate kernel seed to simulate backend drift without regenerating frontend fixtures
        File.WriteAllText(manifestPath, originalManifest, Encoding.UTF8);
        string kernelPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.KernelSeedRelativePath);
        string kernelContent = File.ReadAllText(kernelPath, Encoding.UTF8);
        File.WriteAllText(
            kernelPath,
            kernelContent.Replace("(\"SUBJ-102-001\", \"SITE-102\"),", "(\"SUBJ-102-001\", \"SITE-102\"),\n        (\"SUBJ-102-002\", \"SITE-102\"),", StringComparison.Ordinal),
            Encoding.UTF8);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);
    }

    private void CopySeedFilesToTempRepo(string repoRoot)
    {
        foreach (string rel in new[]
        {
            DemoManifestGeneratorService.KernelSeedRelativePath,
            DemoManifestGeneratorService.ExecutionSeedRelativePath,
            DemoManifestGeneratorService.RealmRelativePath,
        })
        {
            string dest = Path.Combine(_tempRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(repoRoot, rel), dest, overwrite: true);
        }
    }
}
