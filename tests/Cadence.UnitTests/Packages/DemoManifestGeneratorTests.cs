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

    [Fact]
    public void TryBuildExpectedArtifacts_RejectsDriftedParticipantRealmSiteOrStaffGrantViolation()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        string realmPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.RealmRelativePath);
        string originalRealm = File.ReadAllText(realmPath, Encoding.UTF8);

        // Participant realm user pointing to a drifted site code must fail validation
        File.WriteAllText(
            realmPath,
            originalRealm.Replace("\"site_id\": [\"SITE-101\"]", "\"site_id\": [\"SITE-999\"]", StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var siteErrors).Should().BeFalse();
        siteErrors.Should().Contain(e => e.Contains("SITE-999", StringComparison.Ordinal));

        // Granting a Subject-only realm account a staff kernel role must fail K027 validation
        File.WriteAllText(realmPath, originalRealm, Encoding.UTF8);
        string kernelPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.KernelSeedRelativePath);
        string originalKernel = File.ReadAllText(kernelPath, Encoding.UTF8);
        File.WriteAllText(
            kernelPath,
            originalKernel.Replace(
                "(\"auditor.lead\", \"d43095eb-1912-40dd-a528-fa04335a2342\", \"DEMO_AUDITOR\", null),",
                "(\"auditor.lead\", \"d43095eb-1912-40dd-a528-fa04335a2342\", \"DEMO_AUDITOR\", null),\n        (\"subject.site101\", \"7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d\", \"DEMO_CRC\", \"SITE-101\"),",
                StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var grantErrors).Should().BeFalse();
        grantErrors.Should().Contain(e => e.Contains("K027", StringComparison.Ordinal));

        // Referencing an undefined C# constant identifier in Users must fail closed with a clear diagnostic
        File.WriteAllText(
            kernelPath,
            originalKernel.Replace(
                "(\"designer.lead\", DesignerLeadSubject, \"DEMO_SPONSOR_ADMIN\", null),",
                "(\"designer.lead\", MissingSubjectConst, \"DEMO_SPONSOR_ADMIN\", null),",
                StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var constErrors).Should().BeFalse();
        constErrors.Should().Contain(e => e.Contains("MissingSubjectConst", StringComparison.Ordinal));
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
