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
    public void GenerateDemoManifest_ValidatesCanonicalSeedAndRealmInMemoryWithoutWritingManifestFiles()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);
        File.Exists(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestJsRelativePath)).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestDtsRelativePath)).Should().BeFalse();

        DemoManifestGeneratorService.TryBuildSnapshot(
            _tempRoot,
            out var snapshot,
            out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        snapshot.Should().NotBeNull();
        snapshot!.EpochIso.Should().Be("2026-01-05T09:00:00Z"); // deid-ignore: canonical demo epoch assertion
        snapshot.TenantUuid.Should().Be("3f40baf9-2549-8f65-b106-a1ccdca57d83");
        snapshot.Sponsor.Code.Should().Be("CADENCE-PHARMA");
        snapshot.Sponsor.OrganizationUuid.Should().Be("2cb1062c-ed44-860a-ba91-c09142e293d0");
        snapshot.Study.Code.Should().Be("CADENCE-101");
        snapshot.Study.Uuid.Should().Be("535717ef-3976-8a05-9b8f-16701fe0febe");
        snapshot.Sites.Select(s => (s.Code, s.ComplianceStage, s.SiteUuid)).Should().Equal(
            ("SITE-101", "SITE_ACTIVATION", "dd8bf79d-67bd-872a-8299-23eff41807f0"),
            ("SITE-102", "CONDUCT", "4be976d7-2880-892b-85e5-5d990257cd51"));
        snapshot.Participants.Select(p => (p.SubjectCode, p.SiteCode)).Should().Equal(
            ("SUBJ-101-001", "SITE-101"),
            ("SUBJ-101-002", "SITE-101"),
            ("SUBJ-102-001", "SITE-102"));
        snapshot.Participants[0].EnrollmentUuid.Should().Be("04468158-43bd-80f8-92d0-27759ba5b15f");
        snapshot.Personas.Select(p => p.Username).Should().Equal(
            "crc.site101",
            "pi.site101",
            "cra.monitor",
            "dm.lead",
            "designer_demo",
            "designer.lead",
            "safety.officer",
            "auditor.lead",
            "stat.author",
            "stat.approver",
            "pharmacist.site101");
    }

    [Fact]
    public void GenerateDemoManifest_FailsWhenLegacyManifestExistsOrSeedAndRealmDiverge()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        // Legacy committed manifest.js or manifest.d.ts must fail validation
        string manifestPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestJsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "export const DEMO_MANIFEST = {};\n", Encoding.UTF8);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
        File.Delete(manifestPath);

        string dtsPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.ManifestDtsRelativePath);
        File.WriteAllText(dtsPath, "export interface DemoManifestFixture {}\n", Encoding.UTF8);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
        File.Delete(dtsPath);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);

        // Mutate canonical demo-seed.json to diverge from docker/cadence-realm.json
        string seedPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.DemoSeedRelativePath);
        string seedContent = File.ReadAllText(seedPath, Encoding.UTF8);
        File.WriteAllText(
            seedPath,
            seedContent.Replace(
                "\"subject\": \"1a4345cc-364c-4b8b-881e-93bbea528b0a\"",
                "\"subject\": \"00000000-0000-0000-0000-000000000000\"",
                StringComparison.Ordinal),
            Encoding.UTF8);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
    }

    [Fact]
    public void TryBuildSnapshot_RejectsDriftedParticipantRealmSiteOrStaffGrantViolation()
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
        DemoManifestGeneratorService.TryBuildSnapshot(_tempRoot, out _, out var siteErrors).Should().BeFalse();
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
        DemoManifestGeneratorService.TryBuildSnapshot(_tempRoot, out _, out var grantErrors).Should().BeFalse();
        grantErrors.Should().Contain(e => e.Contains("K027", StringComparison.Ordinal));

        // Missing complianceStage on a site in demo-seed.json must fail closed
        File.WriteAllText(
            seedPath,
            originalSeed.Replace("\"complianceStage\": \"SITE_ACTIVATION\"", "\"complianceStage\": \"\"", StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildSnapshot(_tempRoot, out _, out var stageErrors).Should().BeFalse();
        stageErrors.Should().Contain(e => e.Contains("complianceStage", StringComparison.Ordinal));

        // Malformed JSON in demo-seed.json must fail closed with a clear diagnostic
        File.WriteAllText(seedPath, "{ invalid json", Encoding.UTF8);
        DemoManifestGeneratorService.TryBuildSnapshot(_tempRoot, out _, out var jsonErrors).Should().BeFalse();
        jsonErrors.Should().Contain(e => e.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    private void CopySeedFilesToTempRepo(string repoRoot)
    {
        foreach (string rel in new[]
        {
            DemoManifestGeneratorService.DemoSeedRelativePath,
            DemoManifestGeneratorService.RealmRelativePath,
        })
        {
            string dest = Path.Combine(_tempRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(repoRoot, rel), dest, overwrite: true);
        }
    }
}
