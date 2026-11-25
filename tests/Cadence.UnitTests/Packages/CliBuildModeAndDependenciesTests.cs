using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class CliBuildModeAndDependenciesTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        $"cadence-cli-build-mode-test-{Guid.NewGuid():N}");

    public CliBuildModeAndDependenciesTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(_tempDir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void CliProjectAndTransitiveClosure_ConditionPublishAotPruneUnusedDepsAndMatchRunCliAndWorkflowCaches()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string cliCsprojPath = Path.Combine(repoRoot, "packages", "cli", "src", "Cadence.Cli.csproj");

        XDocument cliDoc = XDocument.Load(cliCsprojPath);
        XElement[] isAotElements = cliDoc.Descendants("IsAotCompatible").ToArray();
        isAotElements.Should().ContainSingle();
        isAotElements[0].Attribute("Condition").Should().BeNull();
        isAotElements[0].Value.Trim().Should().Be("true");

        XElement[] publishAotElements = cliDoc.Descendants("PublishAot").ToArray();
        publishAotElements.Should().ContainSingle();
        publishAotElements[0].Attribute("Condition")?.Value.Should().Be(
            "'$(Configuration)' == 'Release' and '$(RuntimeIdentifier)' != ''");
        publishAotElements[0].Value.Trim().Should().Be("true");

        string cliProjectDir = Path.GetDirectoryName(cliCsprojPath)!;
        string[] directProjectRefs = cliDoc
            .Descendants("ProjectReference")
            .Select(el => el.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetRelativePath(
                repoRoot,
                Path.GetFullPath(Path.Combine(cliProjectDir, include.Replace('\\', Path.DirectorySeparatorChar)))).Replace('\\', '/'))
            .ToArray();
        directProjectRefs.Should().NotContain(path => path.Contains("engine_core", StringComparison.Ordinal));
        directProjectRefs.Should().NotContain(path => path.Contains("database", StringComparison.Ordinal));
        directProjectRefs.Should().Contain("packages/compliance/src/Cadence.Compliance.csproj");
        directProjectRefs.Should().Contain("packages/security/src/Cadence.Security.csproj");
        directProjectRefs.Should().Contain("packages/tooling_core/src/Cadence.ToolingCore.csproj");
        directProjectRefs.Should().Contain("packages/kernel/src/Cadence.Kernel.Abstractions/Cadence.Kernel.Abstractions.csproj");

        string[] directPackageRefs = cliDoc
            .Descendants("PackageReference")
            .Select(el => el.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        directPackageRefs.Should().Contain("Npgsql");
        directPackageRefs.Should().NotContain("Npgsql.EntityFrameworkCore.PostgreSQL");

        XDocument buildPropsDoc = XDocument.Load(Path.Combine(repoRoot, "Directory.Build.props"));
        string? npgsqlVersion = buildPropsDoc.Descendants("NpgsqlVersion").SingleOrDefault()?.Value.Trim();
        npgsqlVersion.Should().NotBeNullOrWhiteSpace();

        XDocument packagesPropsDoc = XDocument.Load(Path.Combine(repoRoot, "Directory.Packages.props"));
        Dictionary<string, string> packageVersions = packagesPropsDoc
            .Descendants("PackageVersion")
            .Where(el => el.Attribute("Include") is not null)
            .ToDictionary(
                el => el.Attribute("Include")!.Value,
                el => el.Attribute("Version")?.Value ?? string.Empty,
                StringComparer.Ordinal);
        packageVersions.Should().ContainKey("Npgsql").WhoseValue.Should().Be("$(NpgsqlVersion)");
        packageVersions.Should().ContainKey("Npgsql.EntityFrameworkCore.PostgreSQL").WhoseValue.Should().Be("$(NpgsqlVersion)");

        HashSet<string> expectedClosureDirs = CollectTransitiveProjectDirectories(repoRoot, cliCsprojPath);
        expectedClosureDirs.Should().NotContain("packages/engine_core/src");
        expectedClosureDirs.Should().NotContain("packages/database/src");

        string? nodeExe = PosixProcessLauncher.ResolveExecutablePath("node");
        nodeExe.Should().NotBeNullOrWhiteSpace("node must be available to inspect scripts/run-cli.js exports");

        string probeScript =
            """
            const mod = require(process.argv[1]);
            console.log(JSON.stringify({
              watchedDirs: mod.CLI_WATCHED_SOURCE_DIRS,
              hashExpr: mod.buildWorkflowCacheHashFilesExpression(),
              flags: {
                trueText: mod.isEnvFlagEnabled("true"),
                oneText: mod.isEnvFlagEnabled(" 1 "),
                zeroText: mod.isEnvFlagEnabled("0"),
                falseText: mod.isEnvFlagEnabled("false"),
                emptyText: mod.isEnvFlagEnabled(""),
              },
              modes: {
                defaultEnv: mod.shouldPublishAot({}),
                ghaTrue: mod.shouldPublishAot({ GITHUB_ACTIONS: "true" }),
                aotOne: mod.shouldPublishAot({ CADENCE_CLI_AOT: "1" }),
                aotZero: mod.shouldPublishAot({ CADENCE_CLI_AOT: "0" }),
              },
              rids: {
                linuxX64: mod.resolveHostRuntimeIdentifier("linux", "x64"),
                linuxArm64: mod.resolveHostRuntimeIdentifier("linux", "arm64"),
                osxArm64: mod.resolveHostRuntimeIdentifier("darwin", "arm64"),
                winX64: mod.resolveHostRuntimeIdentifier("win32", "x64"),
              },
            }));
            """;

        (int probeExit, string probeOut, string probeErr) = RunProcess(
            nodeExe!,
            ["-e", probeScript, Path.Combine(repoRoot, "scripts", "run-cli.js")],
            repoRoot);
        probeExit.Should().Be(0, probeOut + probeErr);

        using JsonDocument probeJson = JsonDocument.Parse(probeOut);
        JsonElement root = probeJson.RootElement;

        string[] watchedDirs = root
            .GetProperty("watchedDirs")
            .EnumerateArray()
            .Select(el => el.GetString()!)
            .ToArray();
        watchedDirs.Should().BeEquivalentTo(
            expectedClosureDirs,
            "scripts/run-cli.js CLI_WATCHED_SOURCE_DIRS must match the exact transitive ProjectReference closure of Cadence.Cli.csproj");

        string expectedHashExpr = root.GetProperty("hashExpr").GetString()!;
        expectedHashExpr.Should().NotContain("packages/engine_core/src");
        expectedHashExpr.Should().NotContain("packages/database/src");

        foreach (string workflowRelPath in new[]
        {
            ".github/workflows/ci.yml",
            ".github/workflows/production-pipeline.yml",
            ".github/workflows/deploy-docs.yml",
        })
        {
            string workflowContent = File.ReadAllText(Path.Combine(repoRoot, workflowRelPath));
            workflowContent.Should().Contain(
                expectedHashExpr,
                $"{workflowRelPath} Cache Cadence CLI key must stay in parity with scripts/run-cli.js");
        }

        JsonElement flags = root.GetProperty("flags");
        flags.GetProperty("trueText").GetBoolean().Should().BeTrue();
        flags.GetProperty("oneText").GetBoolean().Should().BeTrue();
        flags.GetProperty("zeroText").GetBoolean().Should().BeFalse();
        flags.GetProperty("falseText").GetBoolean().Should().BeFalse();
        flags.GetProperty("emptyText").GetBoolean().Should().BeFalse();

        JsonElement modes = root.GetProperty("modes");
        modes.GetProperty("defaultEnv").GetBoolean().Should().BeFalse();
        modes.GetProperty("ghaTrue").GetBoolean().Should().BeTrue();
        modes.GetProperty("aotOne").GetBoolean().Should().BeTrue();
        modes.GetProperty("aotZero").GetBoolean().Should().BeFalse();

        JsonElement rids = root.GetProperty("rids");
        rids.GetProperty("linuxX64").GetString().Should().Be("linux-x64");
        rids.GetProperty("linuxArm64").GetString().Should().Be("linux-arm64");
        rids.GetProperty("osxArm64").GetString().Should().Be("osx-arm64");
        rids.GetProperty("winX64").GetString().Should().Be("win-x64");
    }

    [Fact]
    public void RunCli_LocalDebugVsAotModesAndPrunedWatchedDirs_CleanStaleOutputsAndPreserveSolutionObjAssets()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string nodeExe = PosixProcessLauncher.ResolveExecutablePath("node")!;
        string realNodeDir = Path.GetDirectoryName(nodeExe)!;

        string cliRepo = Path.Combine(_tempDir, "cli-repo");
        Directory.CreateDirectory(Path.Combine(cliRepo, "scripts"));
        Directory.CreateDirectory(Path.Combine(cliRepo, "packages", "cli", "src", "obj"));
        Directory.CreateDirectory(Path.Combine(cliRepo, "packages", "tooling_core", "src"));
        Directory.CreateDirectory(Path.Combine(cliRepo, "packages", "engine_core", "src"));
        Directory.CreateDirectory(Path.Combine(cliRepo, "packages", "database", "src"));

        File.Copy(Path.Combine(repoRoot, "scripts", "run-cli.js"), Path.Combine(cliRepo, "scripts", "run-cli.js"));
        File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(cliRepo, "global.json"));
        File.WriteAllText(Path.Combine(cliRepo, "packages", "cli", "src", "Cadence.Cli.csproj"), "<Project />\n");

        const string doctorAssetsSentinel = "{\"sentinel\":\"doctor-obj-assets\"}\n";
        string cliObjAssetsPath = Path.Combine(cliRepo, "packages", "cli", "src", "obj", "project.assets.json");
        File.WriteAllText(cliObjAssetsPath, doctorAssetsSentinel);

        string watchedSourceFile = Path.Combine(cliRepo, "packages", "tooling_core", "src", "Gate.cs");
        string prunedEngineFile = Path.Combine(cliRepo, "packages", "engine_core", "src", "Engine.cs");
        string prunedDatabaseFile = Path.Combine(cliRepo, "packages", "database", "src", "Db.cs");
        File.WriteAllText(watchedSourceFile, "// watched\n");
        File.WriteAllText(prunedEngineFile, "// pruned engine\n");
        File.WriteAllText(prunedDatabaseFile, "// pruned db\n");

        string mockDotnetDir = Path.Combine(_tempDir, "mock-dotnet");
        Directory.CreateDirectory(Path.Combine(mockDotnetDir, "host", "fxr", "10.0.0"));
        Directory.CreateDirectory(Path.Combine(mockDotnetDir, "sdk", "10.0.100"));
        string publishLogPath = Path.Combine(_tempDir, "publish-log.txt");
        string mockDotnetExe = Path.Combine(mockDotnetDir, "dotnet");
        File.WriteAllText(
            mockDotnetExe,
            "#!/bin/sh\n" +
            "if [ \"${1:-}\" = \"--version\" ]; then\n" +
            "  echo '10.0.100'\n" +
            "  exit 0\n" +
            "fi\n" +
            "if [ \"${1:-}\" = \"publish\" ]; then\n" +
            $"  printf '%s\\n' \"$*\" >> '{publishLogPath}'\n" +
            "  out=''\n" +
            "  cfg=''\n" +
            "  prev=''\n" +
            "  for arg in \"$@\"; do\n" +
            "    if [ \"$prev\" = \"-o\" ]; then out=\"$arg\"; fi\n" +
            "    if [ \"$prev\" = \"-c\" ]; then cfg=\"$arg\"; fi\n" +
            "    prev=\"$arg\"\n" +
            "  done\n" +
            "  mkdir -p \"$out\"\n" +
            "  printf '#!/bin/sh\\necho \"cli-%s:%s\"\\n' \"$cfg\" \"$*\" > \"$out/cadence\"\n" +
            "  chmod +x \"$out/cadence\"\n" +
            "  if [ \"$cfg\" = \"Debug\" ]; then\n" +
            "    touch -t 202001010000 \"$out/cadence\"\n" +
            "    printf 'managed-dll\\n' > \"$out/cadence.dll\"\n" +
            "    printf '{}\\n' > \"$out/cadence.runtimeconfig.json\"\n" +
            "  else\n" +
            "    printf 'native-symbols\\n' > \"$out/cadence.dbg\"\n" +
            "  fi\n" +
            "  exit 0\n" +
            "fi\n" +
            "exit 0\n");
        File.SetUnixFileMode(mockDotnetExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string homeDir = Path.Combine(_tempDir, "home");
        Directory.CreateDirectory(homeDir);
        string pathEnv = $"{mockDotnetDir}:{realNodeDir}:/bin:/usr/bin";
        string runCliPath = Path.Combine(cliRepo, "scripts", "run-cli.js");
        string binaryPath = Path.Combine(cliRepo, "bin", "cadence");
        string dllPath = Path.Combine(cliRepo, "bin", "cadence.dll");
        string runtimeConfigPath = Path.Combine(cliRepo, "bin", "cadence.runtimeconfig.json");
        string dbgPath = Path.Combine(cliRepo, "bin", "cadence.dbg");
        string modeMarkerPath = Path.Combine(cliRepo, ".cadence", "cli-artifacts", "build-mode.txt");

        // 1. Default local invocation publishes Debug (-p:PublishAot=false) into isolated debug artifacts.
        (int exit1, string out1, string err1) = RunCliScript(nodeExe, runCliPath, ["--version"], cliRepo, pathEnv, homeDir);
        exit1.Should().Be(0, out1 + err1);
        string[] publishLines = File.ReadAllLines(publishLogPath);
        publishLines.Should().HaveCount(1);
        publishLines[0].Should().Contain("-c Debug");
        publishLines[0].Should().Contain("-p:PublishAot=false");
        publishLines[0].Should().Contain(Path.Combine(".cadence", "cli-artifacts", "debug"));
        publishLines[0].Should().NotContain("--self-contained");
        File.ReadAllText(modeMarkerPath).Trim().Should().Be("debug");
        File.Exists(dllPath).Should().BeTrue();
        File.Exists(runtimeConfigPath).Should().BeTrue();

        // 2. Changes under pruned engine_core/database do not rebuild; changes under watched tooling_core do rebuild.
        DateTime binaryMtime = File.GetLastWriteTimeUtc(binaryPath);
        File.SetLastWriteTimeUtc(prunedEngineFile, binaryMtime.AddMinutes(2));
        File.SetLastWriteTimeUtc(prunedDatabaseFile, binaryMtime.AddMinutes(2));

        (int exit2, string out2, string err2) = RunCliScript(nodeExe, runCliPath, ["--version"], cliRepo, pathEnv, homeDir);
        exit2.Should().Be(0, out2 + err2);
        File.ReadAllLines(publishLogPath).Should().HaveCount(1, "pruned engine_core/database directories must not invalidate bin/cadence");

        File.SetLastWriteTimeUtc(binaryPath, File.GetLastWriteTimeUtc(watchedSourceFile).AddMinutes(-2));
        (int exit3, string out3, string err3) = RunCliScript(nodeExe, runCliPath, ["--version"], cliRepo, pathEnv, homeDir);
        exit3.Should().Be(0, out3 + err3);
        File.ReadAllLines(publishLogPath).Should().HaveCount(2, "watched transitive CLI source directories must invalidate bin/cadence");

        // 3. Incomplete managed sidecars trigger a self-healing Debug rebuild.
        File.Delete(runtimeConfigPath);
        (int exit4, string out4, string err4) = RunCliScript(nodeExe, runCliPath, ["--version"], cliRepo, pathEnv, homeDir);
        exit4.Should().Be(0, out4 + err4);
        File.ReadAllLines(publishLogPath).Should().HaveCount(3);
        File.Exists(runtimeConfigPath).Should().BeTrue();

        // 4. CADENCE_CLI_AOT=1 switches to Release Native AOT, cleans stale managed sidecars, and CADENCE_CLI_AOT=true reuses it.
        (int exit5, string out5, string err5) = RunCliScript(
            nodeExe,
            runCliPath,
            ["--version"],
            cliRepo,
            pathEnv,
            homeDir,
            new Dictionary<string, string?> { ["CADENCE_CLI_AOT"] = "1" });
        exit5.Should().Be(0, out5 + err5);
        publishLines = File.ReadAllLines(publishLogPath);
        publishLines.Should().HaveCount(4);
        publishLines[3].Should().Contain("-c Release");
        publishLines[3].Should().Contain("--self-contained");
        publishLines[3].Should().Contain(Path.Combine(".cadence", "cli-artifacts", "aot"));
        File.ReadAllText(modeMarkerPath).Trim().Should().Be("aot");
        File.Exists(dllPath).Should().BeFalse("switching to AOT must purge stale managed sidecars from ./bin");
        File.Exists(runtimeConfigPath).Should().BeFalse();
        File.Exists(dbgPath).Should().BeTrue();

        (int exit6, string out6, string err6) = RunCliScript(
            nodeExe,
            runCliPath,
            ["--version"],
            cliRepo,
            pathEnv,
            homeDir,
            new Dictionary<string, string?> { ["CADENCE_CLI_AOT"] = "true" });
        exit6.Should().Be(0, out6 + err6);
        File.ReadAllLines(publishLogPath).Should().HaveCount(4);

        // 5. CADENCE_CLI_AOT=0 switches back to Debug and purges stale native AOT artifacts from ./bin.
        (int exit7, string out7, string err7) = RunCliScript(
            nodeExe,
            runCliPath,
            ["--version"],
            cliRepo,
            pathEnv,
            homeDir,
            new Dictionary<string, string?> { ["CADENCE_CLI_AOT"] = "0" });
        exit7.Should().Be(0, out7 + err7);
        publishLines = File.ReadAllLines(publishLogPath);
        publishLines.Should().HaveCount(5);
        publishLines[4].Should().Contain("-c Debug");
        File.ReadAllText(modeMarkerPath).Trim().Should().Be("debug");
        File.Exists(dbgPath).Should().BeFalse("switching back to Debug must purge stale AOT outputs from ./bin");
        File.Exists(dllPath).Should().BeTrue();
        File.Exists(runtimeConfigPath).Should().BeTrue();

        // 6. GITHUB_ACTIONS=true publishes Native AOT on cache miss and skips mtime rebuilds when bin/cadence exists.
        Directory.Delete(Path.Combine(cliRepo, "bin"), recursive: true);
        (int exit8, string out8, string err8) = RunCliScript(
            nodeExe,
            runCliPath,
            ["--version"],
            cliRepo,
            pathEnv,
            homeDir,
            new Dictionary<string, string?> { ["GITHUB_ACTIONS"] = "true" });
        exit8.Should().Be(0, out8 + err8);
        publishLines = File.ReadAllLines(publishLogPath);
        publishLines.Should().HaveCount(6);
        publishLines[5].Should().Contain("-c Release");
        publishLines[5].Should().Contain("--self-contained");

        File.SetLastWriteTimeUtc(binaryPath, File.GetLastWriteTimeUtc(watchedSourceFile).AddMinutes(-5));
        (int exit9, string out9, string err9) = RunCliScript(
            nodeExe,
            runCliPath,
            ["--version"],
            cliRepo,
            pathEnv,
            homeDir,
            new Dictionary<string, string?> { ["GITHUB_ACTIONS"] = "true" });
        exit9.Should().Be(0, out9 + err9);
        File.ReadAllLines(publishLogPath).Should().HaveCount(6, "GITHUB_ACTIONS=true must reuse restored ./bin/cadence without rebuilding");

        // 7. Solution obj/project.assets.json remains untouched for read-only cadence doctor checks.
        File.ReadAllText(cliObjAssetsPath).Should().Be(doctorAssetsSentinel);
    }

    private static HashSet<string> CollectTransitiveProjectDirectories(string repoRoot, string rootCsprojPath)
    {
        var visitedProjects = new HashSet<string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(Path.GetFullPath(rootCsprojPath));

        while (queue.Count > 0)
        {
            string currentCsproj = queue.Dequeue();
            if (!visitedProjects.Add(currentCsproj))
            {
                continue;
            }

            string projectDir = Path.GetDirectoryName(currentCsproj)!;
            string relDir = Path.GetRelativePath(repoRoot, projectDir).Replace('\\', '/');
            directories.Add(relDir);

            XDocument doc = XDocument.Load(currentCsproj);
            foreach (XElement projRef in doc.Descendants("ProjectReference"))
            {
                string? include = projRef.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                string normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar);
                string resolvedRef = Path.GetFullPath(Path.Combine(projectDir, normalizedInclude));
                queue.Enqueue(resolvedRef);
            }
        }

        return directories;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCliScript(
        string nodeExe,
        string scriptPath,
        string[] args,
        string workingDirectory,
        string pathEnv,
        string homeDir,
        IReadOnlyDictionary<string, string?>? extraEnv = null)
    {
        var psi = new ProcessStartInfo(nodeExe)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(scriptPath);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.Environment.Clear();
        psi.Environment["PATH"] = pathEnv;
        psi.Environment["HOME"] = homeDir;
        if (extraEnv is not null)
        {
            foreach ((string key, string? value) in extraEnv)
            {
                if (value is null)
                {
                    psi.Environment.Remove(key);
                }
                else
                {
                    psi.Environment[key] = value;
                }
            }
        }

        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        string fileName,
        string[] args,
        string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process proc = Process.Start(psi)!;
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, stderr);
    }
}
