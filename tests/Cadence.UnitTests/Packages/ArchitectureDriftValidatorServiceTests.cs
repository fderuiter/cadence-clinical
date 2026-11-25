using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ArchitectureDriftValidatorServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"arch-drift-tests-{Guid.NewGuid():N}");

    public ArchitectureDriftValidatorServiceTests()
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
    public void ValidateArchitectureDrift_OnCurrentRepository_ReturnsZero()
    {
        ArchitectureDriftValidatorService.ValidateArchitectureDrift(FindRepoRoot()).Should().Be(0);
    }

    [Theory]
    [InlineData("docker/docker-compose.yml", "APP_NAME: fileshare", "APP_NAME: missing-service")]
    [InlineData("docker/docker-compose.yml", "APP_NAME: payments", "APP_NAME: gateway")]
    [InlineData("ARCHITECTURE.md", "| [execution](apps/execution/README.md)         | `Cadence.Apps.Execution`     | 8002", "| [execution](apps/execution/README.md)         | `Cadence.Apps.Execution`     | 9002")]
    [InlineData("ARCHITECTURE.md", "| `tickets`       | `TicketsDbContext`       | `tickets`       | `apps/tickets/src/Program.cs`       |", "| `tickets`       | `TicketsDbContext`       | `wrong_aud`     | `apps/tickets/src/Program.cs`       |")]
    [InlineData("ARCHITECTURE.md", "Each of the 16 product services", "Each of the 17 product services")]
    [InlineData("ARCHITECTURE.md", "| [tickets](apps/tickets/README.md)             | `Cadence.Apps.Tickets`       | 8009", "| [tickets](apps/tickets/README.md)             | `Cadence.Apps.Tickets`       | 9009")]
    [InlineData("ARCHITECTURE.md", "Features on its own schema", "Scaffold")]
    [InlineData("ARCHITECTURE.md", "any of the 34 `RequireElectronicSignature` endpoints", "any of the 24 `RequireElectronicSignature` endpoints")]
    [InlineData("ARCHITECTURE.md", "All 15 standalone database-backed service hosts register it", "All 14 standalone database-backed service hosts register it")]
    [InlineData("ARCHITECTURE.md", "4 standalone service hosts (", "3 standalone service hosts (")]
    [InlineData("README.md", "16 ASP.NET Core 10 product services", "17 ASP.NET Core 10 product services")]
    [InlineData("AGENTS.md", "fails at startup in every", "falls back to a local PostgreSQL 18 instance on `localhost:5432` in `Development`")]
    [InlineData("docs/ux/CLINICAL_CANVAS.md", "`18` .NET hosts", "`19` .NET hosts")]
    [InlineData("docs/ux/CLINICAL_CANVAS.md", "`34` routes across `10` hosts", "`24` routes across `9` hosts")]
    [InlineData("docs/OPERATIONAL_DATABASE.md", "| [kernel](schema/kernel.md)                   | `kernel` (`apps/kernel-api`) | `kernel`        | `KernelDbContext`          |", "| [kernel](schema/kernel.md)                   | `kernel` (`apps/kernel-api`) | `wrong_schema`  | `KernelDbContext`          |")]
    [InlineData("docs/OPERATIONAL_DATABASE.md", "Neither connection key set (`ConnectionStrings__cadence` or `POSTGRES_CONNECTION_STRING`)", "Neither connection key set outside `Development`")]
    [InlineData("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "`POST /api/v1/execution/forms/{formId}/signatures`", "`POST /api/v1/execution/forms/{formId}/mutated-signatures`")]
    [InlineData("docs/SDLC/Signature_Token_Cryptographic_Contract.md", "`execution.consumed_signature_tokens`", "`edc_wrong.consumed_signature_tokens`")]
    public void ValidateArchitectureDrift_WhenPortSchemaOrSignedRouteMutated_FailsClosed(
        string relativeDocPath,
        string originalFragment,
        string mutatedFragment)
    {
        string realRoot = FindRepoRoot();
        SeedMirrorWorkspace(realRoot, _tempRoot);

        string targetFile = Path.Combine(_tempRoot, relativeDocPath.Replace('/', Path.DirectorySeparatorChar));
        string text = File.ReadAllText(targetFile);
        text.Should().Contain(originalFragment);
        File.WriteAllText(targetFile, text.Replace(originalFragment, mutatedFragment, StringComparison.Ordinal));

        ArchitectureDriftValidatorService.ValidateArchitectureDrift(_tempRoot).Should().Be(1);
    }

    private static void SeedMirrorWorkspace(string realRoot, string destRoot)
    {
        CopyFile(realRoot, destRoot, "Cadence.sln");
        CopyFile(realRoot, destRoot, "README.md");
        CopyFile(realRoot, destRoot, "AGENTS.md");
        CopyFile(realRoot, destRoot, "docker/docker-compose.yml");
        CopyFile(realRoot, destRoot, "ARCHITECTURE.md");
        CopyFile(realRoot, destRoot, "docs/ux/CLINICAL_CANVAS.md");
        CopyFile(realRoot, destRoot, "docs/OPERATIONAL_DATABASE.md");
        CopyFile(realRoot, destRoot, "docs/SDLC/Signature_Token_Cryptographic_Contract.md");

        string srcSchemaDir = Path.Combine(realRoot, "docs", "schema");
        string dstSchemaDir = Path.Combine(destRoot, "docs", "schema");
        Directory.CreateDirectory(dstSchemaDir);
        foreach (string schemaFile in Directory.EnumerateFiles(srcSchemaDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            File.Copy(schemaFile, Path.Combine(dstSchemaDir, Path.GetFileName(schemaFile)), overwrite: true);
        }

        // Symlink apps/ and packages/ so CodeMetadataCatalog reads live endpoint, DbContext, topology, and Vite metadata without modifying realRoot.
        Directory.CreateSymbolicLink(
            Path.Combine(destRoot, "apps"),
            Path.Combine(realRoot, "apps"));
        Directory.CreateSymbolicLink(
            Path.Combine(destRoot, "packages"),
            Path.Combine(realRoot, "packages"));
    }

    private static void CopyFile(string srcRoot, string dstRoot, string relPath)
    {
        string norm = relPath.Replace('/', Path.DirectorySeparatorChar);
        string dst = Path.Combine(dstRoot, norm);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(Path.Combine(srcRoot, norm), dst, overwrite: true);
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
