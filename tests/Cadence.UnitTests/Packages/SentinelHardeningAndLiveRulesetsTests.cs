using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class SentinelHardeningAndLiveRulesetsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"cadence-sentinel-tests-{Guid.NewGuid():N}");

    public SentinelHardeningAndLiveRulesetsTests()
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

    [Theory]
    [InlineData("Database resolution falls back to the local database in Development.")]
    [InlineData("When unset, it falls back to localhost:5432.")]
    [InlineData("Falls back to PostgreSQL on localhost:5432 for local runs.")]
    public void PostgresFallbackDocGuard_RejectsStaleFallbackClaims(string staleClaim)
    {
        PostgresFallbackDocGuard.ValidateNoStalePostgresFallback(staleClaim, "docs/OPERATIONAL_DATABASE.md")
            .Should().BeFalse();
    }

    [Fact]
    public void PostgresFallbackDocGuard_AllowsFailClosedProse()
    {
        string validProse = "Services fail closed at startup without ConnectionStrings__cadence; there is no fallback to localhost:5432.";
        PostgresFallbackDocGuard.ValidateNoStalePostgresFallback(validProse, "docs/OPERATIONAL_DATABASE.md")
            .Should().BeTrue();
    }

    [Fact]
    public void EnvExampleStartupValidator_CurrentRepositoryPasses()
    {
        string repoRoot = FindRepoRoot();
        string envExamplePath = Path.Combine(repoRoot, ".env.example");

        var values = EnvExampleStartupValidator.ExtractKeyValuesFromEnvFile(envExamplePath, out var duplicateKeys);

        duplicateKeys.Should().BeEmpty();
        EnvExampleStartupValidator.ValidateEnvExampleStartupValues(repoRoot, envExamplePath, values, duplicateKeys)
            .Should().BeTrue();
    }

    [Fact]
    public void EnvExampleStartupValidator_RejectsDuplicateKeysUnquotedDelimitersBlankSecretsAndReusedBase64Keys()
    {
        string realRoot = FindRepoRoot();
        File.Copy(Path.Combine(realRoot, "Cadence.sln"), Path.Combine(_tempRoot, "Cadence.sln"));
        File.Copy(Path.Combine(realRoot, "README.md"), Path.Combine(_tempRoot, "README.md"));
        string envPath = Path.Combine(_tempRoot, ".env.example");

        string baseEnv = File.ReadAllText(Path.Combine(realRoot, ".env.example"));

        // 1. Duplicate key
        File.WriteAllText(envPath, baseEnv + "\nGATEWAY_SECRET=\"duplicate-gateway-secret-at-least-32-chars\"\n");
        var dupValues = EnvExampleStartupValidator.ExtractKeyValuesFromEnvFile(envPath, out var dupKeys);
        dupKeys.Should().ContainSingle().Which.Should().Be("GATEWAY_SECRET");
        EnvExampleStartupValidator.ValidateEnvExampleStartupValues(_tempRoot, envPath, dupValues, dupKeys)
            .Should().BeFalse();

        // 2. Unquoted semicolon / whitespace breaking POSIX `. ./.env`
        File.WriteAllText(
            envPath,
            baseEnv.Replace(
                "ConnectionStrings__cadence=\"Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password\"",
                "ConnectionStrings__cadence=Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence_password",
                StringComparison.Ordinal));
        var unquotedValues = EnvExampleStartupValidator.ExtractKeyValuesFromEnvFile(envPath, out var noDups);
        EnvExampleStartupValidator.ValidateEnvExampleStartupValues(_tempRoot, envPath, unquotedValues, noDups)
            .Should().BeFalse();

        // 3. Reused 32-byte Base64 cryptographic key across distinct domains
        string reusedKey = unquotedValues["FIELD_ENCRYPTION_KEY"];
        string currentImagingKey = unquotedValues["CADENCE_IMAGING_DEID_SIGNING_KEY"];
        File.WriteAllText(
            envPath,
            baseEnv.Replace(
                $"CADENCE_IMAGING_DEID_SIGNING_KEY={currentImagingKey}",
                $"CADENCE_IMAGING_DEID_SIGNING_KEY={reusedKey}",
                StringComparison.Ordinal));
        var reusedValues = EnvExampleStartupValidator.ExtractKeyValuesFromEnvFile(envPath, out var reusedDups);
        EnvExampleStartupValidator.ValidateEnvExampleStartupValues(_tempRoot, envPath, reusedValues, reusedDups)
            .Should().BeFalse();

        // 4. Missing cryptographic key in README.md
        File.WriteAllText(envPath, baseEnv);
        File.WriteAllText(
            Path.Combine(_tempRoot, "README.md"),
            File.ReadAllText(Path.Combine(realRoot, "README.md"))
                .Replace("CADENCE_BIOMETRIC_LIVENESS_KEY", "OMITTED_KEY", StringComparison.Ordinal));
        var cleanValues = EnvExampleStartupValidator.ExtractKeyValuesFromEnvFile(envPath, out var cleanDups);
        EnvExampleStartupValidator.ValidateEnvExampleStartupValues(_tempRoot, envPath, cleanValues, cleanDups)
            .Should().BeFalse();
    }

    [Fact]
    public void DocIndexStatusHeaderParser_AndSyncDocIndex_ReconcileExplicitStatusHeadersWhilePreservingNormative()
    {
        DocIndexStatusHeaderParser.TryExtractExplicitHeaderStatus(
            "# Clinical Canvas\n\n| | |\n|---|---|\n| **Status** | Proposed. Normative for kernel read APIs once landed. |\n",
            out string canvasStatus,
            out bool normativelyQualified).Should().BeTrue();
        canvasStatus.Should().Be("Proposed");
        normativelyQualified.Should().BeTrue();

        string docsDir = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(Path.Combine(docsDir, "ux"));
        File.WriteAllText(
            Path.Combine(docsDir, "ux", "SPEC_A.md"),
            "# Spec A\n\n| | |\n|---|---|\n| **Status** | Accepted |\n");
        File.WriteAllText(
            Path.Combine(docsDir, "ux", "SPEC_B.md"),
            "# Spec B\n\n| | |\n|---|---|\n| **Status** | Proposed. Normative once accepted. |\n");

        string indexPath = Path.Combine(docsDir, "DOCUMENTATION_INDEX.md");
        File.WriteAllText(
            indexPath,
            """
            # Documentation Index

            ## Design and product specifications

            | Document | Status | Summary |
            | --- | --- | --- |
            | [Spec A](ux/SPEC_A.md) | Proposed | Spec A summary |
            | [Spec B](ux/SPEC_B.md) | Normative | Spec B summary |
            """);

        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempRoot).Should().Be(1);
        DocIndexSyncService.SyncDocIndex(check: false, repoRoot: _tempRoot).Should().Be(0);
        DocIndexSyncService.SyncDocIndex(check: true, repoRoot: _tempRoot).Should().Be(0);

        string updatedIndex = File.ReadAllText(indexPath);
        updatedIndex.Should().Contain("| [Spec A](ux/SPEC_A.md) | Current | Spec A summary |");
        updatedIndex.Should().Contain("| [Spec B](ux/SPEC_B.md) | Normative | Spec B summary |");
    }

    [Theory]
    [InlineData("README.md", "16 ASP.NET Core 10 product services", "17 ASP.NET Core 10 product services")]
    [InlineData("ARCHITECTURE.md", "Each of the 16 product services", "Each of the 17 product services")]
    [InlineData("ARCHITECTURE.md", "Features on its own schema", "Scaffold")]
    [InlineData("ARCHITECTURE.md", "`econsent` (`EconsentDbContext`)", "`missing_outbox` (`MissingDbContext`)")]
    [InlineData("docs/ux/CLINICAL_CANVAS.md", "`34` routes across `10` hosts", "`11` routes across `10` hosts")]
    [InlineData("docs/OPERATIONAL_DATABASE.md", "# Database Layout and Migrations Runbook", "# Database Layout and Migrations Runbook\n\nDatabase resolution falls back to localhost:5432 in Development.")]
    public void ArchitectureDriftValidator_FailsClosedOnExpandedProseAndSummaryMutations(
        string relativePath,
        string originalFragment,
        string mutatedFragment)
    {
        string realRoot = FindRepoRoot();
        SeedArchitectureMirror(realRoot, _tempRoot);

        string targetPath = Path.Combine(_tempRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string content = File.ReadAllText(targetPath);
        content.Should().Contain(originalFragment);
        File.WriteAllText(targetPath, content.Replace(originalFragment, mutatedFragment, StringComparison.Ordinal));

        ArchitectureDriftValidatorService.ValidateArchitectureDrift(_tempRoot).Should().Be(1);
    }

    [Fact]
    public void RulesetService_DefaultProfileIsActiveActions_AndVerifyLiveDetectsParityAndDrift()
    {
        RulesetService.DefaultProfile.Should().Be(RulesetService.ActiveActionsProfile);
        RulesetService.TryNormalizeProfile(null, out string defaultNormalized).Should().BeTrue();
        defaultNormalized.Should().Be(RulesetService.ActiveActionsProfile);

        string realRoot = FindRepoRoot();
        using var conflictFlagWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: realRoot,
            dryRun: true,
            verifyLive: true,
            repoOverride: "fderuiter/cadence-clinical",
            output: conflictFlagWriter).Should().Be(1);
        conflictFlagWriter.ToString().Should().Contain("'--dry-run' and '--verify-live' cannot be used together");

        string mainJson = File.ReadAllText(Path.Combine(realRoot, ".github", "rulesets", "main.json"));
        string releaseJson = File.ReadAllText(Path.Combine(realRoot, ".github", "rulesets", "release_branches.json"));
        string tagsJson = File.ReadAllText(Path.Combine(realRoot, ".github", "rulesets", "version_tags.json"));

        // Simulate GitHub's live GET response omitting default false `do_not_enforce_on_create`
        string mainLiveJson = mainJson.Replace("\"do_not_enforce_on_create\": false,", string.Empty, StringComparison.Ordinal);

        RulesetService.ProcessRunner matchingRunner = (_, file, args) =>
        {
            if (file != "gh")
            {
                return new RulesetService.ProcessOutcome(1, "", "unexpected process");
            }

            if (args.Count >= 3 && args[1] == "--paginate")
            {
                return new RulesetService.ProcessOutcome(
                    0,
                    """[{"id":101,"name":"main-branch-protection"},{"id":102,"name":"release-branches-protection"},{"id":103,"name":"version-tags-protection"}]""",
                    "");
            }

            return args[1] switch
            {
                "repos/fderuiter/cadence-clinical/rulesets/101" => new RulesetService.ProcessOutcome(0, mainLiveJson, ""),
                "repos/fderuiter/cadence-clinical/rulesets/102" => new RulesetService.ProcessOutcome(0, releaseJson, ""),
                "repos/fderuiter/cadence-clinical/rulesets/103" => new RulesetService.ProcessOutcome(0, tagsJson, ""),
                _ => new RulesetService.ProcessOutcome(1, "", $"unexpected endpoint {args[1]}"),
            };
        };

        using var cleanWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: realRoot,
            verifyLive: true,
            repoOverride: "fderuiter/cadence-clinical",
            runner: matchingRunner,
            environment: _ => null,
            output: cleanWriter).Should().Be(0, cleanWriter.ToString());
        cleanWriter.ToString().Should().Contain("Verified 3 live GitHub rulesets");

        // Drift: live main ruleset enforcement disabled or missing required_status_checks
        string driftedMain = mainJson.Replace("\"enforcement\": \"active\"", "\"enforcement\": \"disabled\"", StringComparison.Ordinal);
        RulesetService.ProcessRunner driftedRunner = (dir, file, args) =>
            args.Count >= 2 && args[1] == "repos/fderuiter/cadence-clinical/rulesets/101"
                ? new RulesetService.ProcessOutcome(0, driftedMain, "")
                : matchingRunner(dir, file, args);

        using var driftWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: realRoot,
            verifyLive: true,
            repoOverride: "fderuiter/cadence-clinical",
            runner: driftedRunner,
            environment: _ => null,
            output: driftWriter).Should().Be(1);
        driftWriter.ToString().Should().Contain("'enforcement' is 'disabled', expected 'active'");

        // Duplicate ruleset names on GitHub must fail closed
        RulesetService.ProcessRunner duplicateRunner = (dir, file, args) =>
            args.Count >= 3 && args[1] == "--paginate"
                ? new RulesetService.ProcessOutcome(
                    0,
                    """[{"id":101,"name":"main-branch-protection"},{"id":999,"name":"main-branch-protection"},{"id":102,"name":"release-branches-protection"},{"id":103,"name":"version-tags-protection"}]""",
                    "")
                : matchingRunner(dir, file, args);

        using var dupWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: realRoot,
            verifyLive: true,
            repoOverride: "fderuiter/cadence-clinical",
            runner: duplicateRunner,
            environment: _ => null,
            output: dupWriter).Should().Be(1);
        dupWriter.ToString().Should().Contain("duplicate rulesets named 'main-branch-protection'");

        // Malformed bypass_actors on GitHub must fail closed instead of looking empty
        string malformedBypassTags = tagsJson.Replace(
            "\"bypass_actors\": [",
            "\"bypass_actors\": [\"not-an-object\",",
            StringComparison.Ordinal);
        RulesetService.ProcessRunner malformedBypassRunner = (dir, file, args) =>
            args.Count >= 2 && args[1] == "repos/fderuiter/cadence-clinical/rulesets/103"
                ? new RulesetService.ProcessOutcome(0, malformedBypassTags, "")
                : matchingRunner(dir, file, args);

        using var malformedBypassWriter = new StringWriter();
        RulesetService.Sync(
            repoRoot: realRoot,
            verifyLive: true,
            repoOverride: "fderuiter/cadence-clinical",
            runner: malformedBypassRunner,
            environment: _ => null,
            output: malformedBypassWriter).Should().Be(1);
        malformedBypassWriter.ToString().Should().Contain("every entry in 'bypass_actors' must be an object");
    }

    private static void SeedArchitectureMirror(string realRoot, string destRoot)
    {
        foreach (string rel in new[]
                 {
                     "Cadence.sln",
                     "README.md",
                     "AGENTS.md",
                     "ARCHITECTURE.md",
                     "docker/docker-compose.yml",
                     "docs/OPERATIONAL_DATABASE.md",
                     "docs/ux/CLINICAL_CANVAS.md",
                     "docs/SDLC/Signature_Token_Cryptographic_Contract.md",
                 })
        {
            string norm = rel.Replace('/', Path.DirectorySeparatorChar);
            string dst = Path.Combine(destRoot, norm);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(Path.Combine(realRoot, norm), dst, overwrite: true);
        }

        string srcSchemaDir = Path.Combine(realRoot, "docs", "schema");
        string dstSchemaDir = Path.Combine(destRoot, "docs", "schema");
        Directory.CreateDirectory(dstSchemaDir);
        foreach (string schemaFile in Directory.EnumerateFiles(srcSchemaDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            File.Copy(schemaFile, Path.Combine(dstSchemaDir, Path.GetFileName(schemaFile)), overwrite: true);
        }

        Directory.CreateSymbolicLink(Path.Combine(destRoot, "apps"), Path.Combine(realRoot, "apps"));
        Directory.CreateSymbolicLink(Path.Combine(destRoot, "packages"), Path.Combine(realRoot, "packages"));
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Cadence.sln not found.");
    }
}
