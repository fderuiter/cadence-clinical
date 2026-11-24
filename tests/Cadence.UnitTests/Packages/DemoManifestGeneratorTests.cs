using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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
    public void ValidateSeedContract_VerifiesCanonicalStudySitesParticipantsAndElevenStaffPersonasWithZeroGeneratedFiles()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        // Confirm DemoManifestGeneratorService.cs contains zero regex extractors over C# source
        string generatorSource = File.ReadAllText(
            Path.Combine(repoRoot, "packages/tooling_core/src/Services/DemoManifestGeneratorService.cs"),
            Encoding.UTF8);
        generatorSource.Should().NotContain("System.Text.RegularExpressions");
        generatorSource.Should().NotContain("GeneratedRegex");

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        File.Exists(Path.Combine(_tempRoot, DemoManifestGeneratorService.LegacyManifestJsRelativePath)).Should().BeFalse();
        File.Exists(Path.Combine(_tempRoot, DemoManifestGeneratorService.LegacyManifestDtsRelativePath)).Should().BeFalse();

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);

        DemoManifestGeneratorService.TryValidateSeedContract(
            _tempRoot,
            out var snapshot,
            out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        snapshot.Should().NotBeNull();
        snapshot!.EpochIso.Should().Be("2026-01-05T09:00:00Z"); // deid-ignore: canonical demo epoch assertion
        snapshot.Sponsor.Code.Should().Be("CADENCE-PHARMA");
        snapshot.Study.Code.Should().Be("CADENCE-101");
        snapshot.Sites.Select(s => (s.Code, s.ComplianceStage)).Should().Equal(
            ("SITE-101", "SITE_ACTIVATION"),
            ("SITE-102", "CONDUCT"));
        snapshot.Participants.Select(p => (p.SubjectCode, p.SiteCode, p.Status)).Should().Equal(
            ("SUBJ-101-001", "SITE-101", "SCREENED"),
            ("SUBJ-101-002", "SITE-101", "SCREENED"),
            ("SUBJ-102-001", "SITE-102", "SCREENED"));
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
    public void JsDemoFixturesIndex_ProducesByteIdenticalUuidV8AndManifestParityWithCSharp()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        DemoManifestGeneratorService.TryValidateSeedContract(repoRoot, out var csharpSnapshot, out var errors).Should().BeTrue();
        errors.Should().BeEmpty();
        csharpSnapshot.Should().NotBeNull();

        const string script = """
            import { DEMO_MANIFEST, deriveDemoUuidV8 } from './packages/demo-fixtures/src/index.js';
            console.log(JSON.stringify({
              sampleProbeUuid: deriveDemoUuidV8('cadence-local', 'study', 'CADENCE-101'),
              manifest: DEMO_MANIFEST
            }));
            """;

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--input-type=module");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var proc = PosixProcessLauncher.Start(psi);
        proc.Should().NotBeNull();
        string stdOut = proc!.StandardOutput.ReadToEnd();
        string stdErr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        proc.ExitCode.Should().Be(0, $"Node evaluation failed: {stdErr}");

        using var doc = JsonDocument.Parse(stdOut);
        var root = doc.RootElement;
        string expectedProbe = DemoManifestGeneratorService.DeriveUuidV8("cadence-local", "study", "CADENCE-101").ToString("D");
        root.GetProperty("sampleProbeUuid").GetString().Should().Be(expectedProbe);

        var jsManifest = root.GetProperty("manifest");
        jsManifest.GetProperty("tenantCode").GetString().Should().Be(csharpSnapshot!.TenantCode);
        jsManifest.GetProperty("tenantUuid").GetString().Should().Be(csharpSnapshot.TenantUuid);
        jsManifest.GetProperty("epochIso").GetString().Should().Be(csharpSnapshot.EpochIso);
        jsManifest.GetProperty("epochMs").GetInt64().Should().Be(csharpSnapshot.EpochMs);

        var jsSponsor = jsManifest.GetProperty("sponsor");
        jsSponsor.GetProperty("code").GetString().Should().Be(csharpSnapshot.Sponsor.Code);
        jsSponsor.GetProperty("name").GetString().Should().Be(csharpSnapshot.Sponsor.Name);
        jsSponsor.GetProperty("organizationUuid").GetString().Should().Be(csharpSnapshot.Sponsor.OrganizationUuid);

        var jsStudy = jsManifest.GetProperty("study");
        jsStudy.GetProperty("code").GetString().Should().Be(csharpSnapshot.Study.Code);
        jsStudy.GetProperty("uuid").GetString().Should().Be(csharpSnapshot.Study.Uuid);
        jsStudy.GetProperty("ref").GetString().Should().Be(csharpSnapshot.Study.Ref);

        var jsSites = jsManifest.GetProperty("sites").EnumerateArray().ToArray();
        jsSites.Should().HaveCount(csharpSnapshot.Sites.Count);
        for (int i = 0; i < csharpSnapshot.Sites.Count; i++)
        {
            var expectedSite = csharpSnapshot.Sites[i];
            jsSites[i].GetProperty("code").GetString().Should().Be(expectedSite.Code);
            jsSites[i].GetProperty("complianceStage").GetString().Should().Be(expectedSite.ComplianceStage);
            jsSites[i].GetProperty("institutionUuid").GetString().Should().Be(expectedSite.InstitutionUuid);
            jsSites[i].GetProperty("siteUuid").GetString().Should().Be(expectedSite.SiteUuid);
            jsSites[i].GetProperty("studySiteUuid").GetString().Should().Be(expectedSite.StudySiteUuid);
            jsSites[i].GetProperty("studySiteRef").GetString().Should().Be(expectedSite.StudySiteRef);
        }

        var jsParticipants = jsManifest.GetProperty("participants").EnumerateArray().ToArray();
        jsParticipants.Should().HaveCount(csharpSnapshot.Participants.Count);
        for (int i = 0; i < csharpSnapshot.Participants.Count; i++)
        {
            var expectedParticipant = csharpSnapshot.Participants[i];
            jsParticipants[i].GetProperty("subjectCode").GetString().Should().Be(expectedParticipant.SubjectCode);
            jsParticipants[i].GetProperty("siteCode").GetString().Should().Be(expectedParticipant.SiteCode);
            jsParticipants[i].GetProperty("status").GetString().Should().Be(expectedParticipant.Status);
            jsParticipants[i].GetProperty("screenedAt").GetString().Should().Be(expectedParticipant.ScreenedAtIso);
            jsParticipants[i].GetProperty("participantUuid").GetString().Should().Be(expectedParticipant.ParticipantUuid);
            jsParticipants[i].GetProperty("enrollmentUuid").GetString().Should().Be(expectedParticipant.EnrollmentUuid);
            jsParticipants[i].GetProperty("enrollmentRef").GetString().Should().Be(expectedParticipant.EnrollmentRef);
        }

        var jsRoles = jsManifest.GetProperty("roles").EnumerateArray().ToArray();
        jsRoles.Should().HaveCount(csharpSnapshot.Roles.Count);
        for (int i = 0; i < csharpSnapshot.Roles.Count; i++)
        {
            jsRoles[i].GetProperty("code").GetString().Should().Be(csharpSnapshot.Roles[i].Code);
            jsRoles[i].GetProperty("roleUuid").GetString().Should().Be(csharpSnapshot.Roles[i].RoleUuid);
        }

        var jsPersonas = jsManifest.GetProperty("personas").EnumerateArray().ToArray();
        jsPersonas.Should().HaveCount(csharpSnapshot.Personas.Count);
        for (int i = 0; i < csharpSnapshot.Personas.Count; i++)
        {
            var expectedPersona = csharpSnapshot.Personas[i];
            jsPersonas[i].GetProperty("username").GetString().Should().Be(expectedPersona.Username);
            jsPersonas[i].GetProperty("id").GetString().Should().Be(expectedPersona.KeycloakSub);
            jsPersonas[i].GetProperty("grantUuid").GetString().Should().Be(expectedPersona.GrantUuid);
            jsPersonas[i].GetProperty("workflow").GetString().Should().Be(expectedPersona.Workflow);
            jsPersonas[i].GetProperty("workspacePersona").GetString().Should().Be(expectedPersona.WorkspacePersona);
        }
    }

    [Fact]
    public void GenerateDemoManifest_ValidateOnly_FailsOnLegacyManifestArtifactOrRealmDrift()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        CopySeedFilesToTempRepo(repoRoot);

        // Stale generated manifest.js must be rejected in --check mode and cleaned up in non-check mode
        string legacyManifestPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.LegacyManifestJsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyManifestPath)!);
        File.WriteAllText(legacyManifestPath, "export const DEMO_MANIFEST = {};\n", Encoding.UTF8);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: _tempRoot).Should().Be(0);
        File.Exists(legacyManifestPath).Should().BeFalse();
        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(0);

        // Drifting email or subject UUID in demo-seed.json away from cadence-realm.json must fail validation
        string seedPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.DemoSeedRelativePath);
        string seedContent = File.ReadAllText(seedPath, Encoding.UTF8);
        File.WriteAllText(
            seedPath,
            seedContent.Replace(
                "\"email\": \"crc.site101@example.com\"",
                "\"email\": \"drifted.crc@example.com\"",
                StringComparison.Ordinal),
            Encoding.UTF8);

        DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: true, repoRoot: _tempRoot).Should().Be(1);
    }

    [Fact]
    public void TryValidateSeedContract_RejectsDriftedParticipantRealmSiteOrK027StaffGrantOrInvalidSchemaShape()
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
        DemoManifestGeneratorService.TryValidateSeedContract(_tempRoot, out _, out var siteErrors).Should().BeFalse();
        siteErrors.Should().Contain(e => e.Contains("SITE-999", StringComparison.Ordinal));

        // Granting a Subject-only realm account a staff kernel role must fail K027 validation
        File.WriteAllText(realmPath, originalRealm, Encoding.UTF8);
        string seedPath = Path.Combine(_tempRoot, DemoManifestGeneratorService.DemoSeedRelativePath);
        string originalSeed = File.ReadAllText(seedPath, Encoding.UTF8);
        const string subjectStaffGrant = """
                {
                  "username": "pharmacist.site101",
                  "subject": "c3d4e5f6-a7b8-4c9d-8e1f-a3b4c5d6e7f8",
                  "email": "pharmacist.site101@example.com",
                  "firstName": "Site",
                  "lastName": "Pharmacist",
                  "tenantCode": "cadence-local",
                  "role": "DEMO_PHARMACIST",
                  "site": "SITE-101",
                  "workflow": "pharmacist",
                  "workspacePersona": "pharmacist",
                  "realmRoles": ["pharmacist"]
                },
                {
                  "username": "subject.site101",
                  "subject": "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d",
                  "email": "subject.site101@example.com",
                  "firstName": "Subject",
                  "lastName": "Participant",
                  "tenantCode": "cadence-local",
                  "role": "DEMO_CRC",
                  "site": "SITE-101",
                  "workflow": "coordinator",
                  "workspacePersona": "site_crc",
                  "realmRoles": ["Subject"]
                }
            """;
        File.WriteAllText(
            seedPath,
            originalSeed.Replace(
                """
                    {
                      "username": "pharmacist.site101",
                      "subject": "c3d4e5f6-a7b8-4c9d-8e1f-a3b4c5d6e7f8",
                      "email": "pharmacist.site101@example.com",
                      "firstName": "Site",
                      "lastName": "Pharmacist",
                      "tenantCode": "cadence-local",
                      "role": "DEMO_PHARMACIST",
                      "site": "SITE-101",
                      "workflow": "pharmacist",
                      "workspacePersona": "pharmacist",
                      "realmRoles": ["pharmacist"]
                    }
                """.ReplaceLineEndings("\n"),
                subjectStaffGrant.ReplaceLineEndings("\n"),
                StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryValidateSeedContract(_tempRoot, out _, out var grantErrors).Should().BeFalse();
        grantErrors.Should().Contain(e => e.Contains("K027", StringComparison.Ordinal));

        // Disallowed additional JSON property must fail schema shape validation
        File.WriteAllText(
            seedPath,
            originalSeed.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1,\n  \"unexpectedField\": true,", StringComparison.Ordinal),
            Encoding.UTF8);
        DemoManifestGeneratorService.TryValidateSeedContract(_tempRoot, out _, out var shapeErrors).Should().BeFalse();
        shapeErrors.Should().Contain(e => e.Contains("unexpectedField", StringComparison.Ordinal));

        // Malformed JSON in demo-seed.json must fail closed with a clear diagnostic
        File.WriteAllText(seedPath, "{ invalid json", Encoding.UTF8);
        DemoManifestGeneratorService.TryValidateSeedContract(_tempRoot, out _, out var jsonErrors).Should().BeFalse();
        jsonErrors.Should().Contain(e => e.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    private void CopySeedFilesToTempRepo(string repoRoot)
    {
        foreach (string rel in new[]
        {
            DemoManifestGeneratorService.DemoSeedRelativePath,
            DemoManifestGeneratorService.DemoSeedSchemaRelativePath,
            DemoManifestGeneratorService.RealmRelativePath,
        })
        {
            string dest = Path.Combine(_tempRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(repoRoot, rel), dest, overwrite: true);
        }
    }
}
