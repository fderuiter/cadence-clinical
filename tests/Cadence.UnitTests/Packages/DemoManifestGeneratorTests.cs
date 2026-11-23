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

        // Restore manifest, then mutate canonical demo-seed.json to simulate seed drift without regenerating frontend fixtures
        File.WriteAllText(manifestPath, originalManifest, Encoding.UTF8);
        string seedPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.DemoSeedRelativePath);
        string seedContent = File.ReadAllText(seedPath, Encoding.UTF8);
        File.WriteAllText(
            seedPath,
            seedContent.Replace(
                "\"subjectCode\": \"SUBJ-102-001\",\n      \"siteCode\": \"SITE-102\"\n    }",
                "\"subjectCode\": \"SUBJ-102-001\",\n      \"siteCode\": \"SITE-102\"\n    },\n    {\n      \"subjectCode\": \"SUBJ-102-002\",\n      \"siteCode\": \"SITE-102\"\n    }",
                StringComparison.Ordinal),
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
        string seedPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.DemoSeedRelativePath);
        string originalSeed = File.ReadAllText(seedPath, Encoding.UTF8);
        File.WriteAllText(
            seedPath,
            originalSeed.Replace(
                "\"username\": \"auditor.lead\",\n      \"subject\": \"d43095eb-1912-40dd-a528-fa04335a2342\",\n      \"role\": \"DEMO_AUDITOR\",\n      \"site\": null\n    }",
                "\"username\": \"auditor.lead\",\n      \"subject\": \"d43095eb-1912-40dd-a528-fa04335a2342\",\n      \"role\": \"DEMO_AUDITOR\",\n      \"site\": null\n    },\n    {\n      \"username\": \"subject.site101\",\n      \"subject\": \"7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d\",\n      \"role\": \"DEMO_CRC\",\n      \"site\": \"SITE-101\"\n    }",
                StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var grantErrors).Should().BeFalse();
        grantErrors.Should().Contain(e => e.Contains("K027", StringComparison.Ordinal));

        // Malformed JSON in demo-seed.json must fail closed with a clear diagnostic
        File.WriteAllText(seedPath, "{ invalid json", Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var jsonErrors).Should().BeFalse();
        jsonErrors.Should().Contain(e => e.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void TryBuildExpectedArtifacts_RejectsDriftedParticipantRealmStudyId()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out var snapshot, out _, out _, out var okErrors).Should().BeTrue();
        okErrors.Should().BeEmpty();
        snapshot!.Personas.Should().ContainSingle(p =>
            p.Username == "designer.lead" &&
            p.KeycloakSub == "8808e058-a472-4f72-9970-a027eddf35bc");

        string realmPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.RealmRelativePath);
        string originalRealm = File.ReadAllText(realmPath, Encoding.UTF8);
        File.WriteAllText(
            realmPath,
            originalRealm.Replace("\"study_ids\": [\"CADENCE-101\"]", "\"study_ids\": [\"DRIFTED-999\"]", StringComparison.Ordinal),
            Encoding.UTF8);

        DemoManifestGeneratorService.TryBuildExpectedArtifacts(_tempRoot, out _, out _, out _, out var studyErrors).Should().BeFalse();
        studyErrors.Should().Contain(e => e.Contains("DRIFTED-999", StringComparison.Ordinal));
    }

    private void CopySeedFilesToTempRepo(string repoRoot)
    {
        foreach (string rel in new[]
        {
            DemoManifestGeneratorService.DemoSeedRelativePath,
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
