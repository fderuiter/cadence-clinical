using System;
using System.IO;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class EnvVarExtractorServiceTests
{
    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode0_WhenRepoIsSynchronized()
    {
        string repoRoot = QualityGateOrchestratorFindRepoRoot();
        int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: repoRoot);
        exitCode.Should().Be(0);
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenCodeVarIsMissingFromEnvExample()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"MISSING_ENV_VAR_ABC\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "EXISTING_ENV_VAR=default\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| `EXISTING_ENV_VAR` | description | default |");

            int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenEnvExampleVarIsMissingFromDocs()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"VAR_ONE\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "VAR_ONE=value1\nVAR_TWO=value2\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| `VAR_ONE` | description | default |");

            int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static string QualityGateOrchestratorFindRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
