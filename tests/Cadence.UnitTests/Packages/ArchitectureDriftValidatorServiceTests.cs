using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ArchitectureDriftValidatorServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "arch-drift-validator-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ValidateArchitectureDrift_ReturnsZero_WhenAllServicesAreDocumented()
    {
        Write("docker/docker-compose.yml", @"
services:
  gateway:
    image: gateway
  execution:
    image: execution
");
        Write("ARCHITECTURE.md", "# Architecture\nIncludes gateway and execution services.");
        Write("docs/SDLC/02_Technical_Design_Document_TDD.md", "# TDD\nIncludes gateway and execution services.");

        int result = ArchitectureDriftValidatorService.ValidateArchitectureDrift(_root);
        result.Should().Be(0);
    }

    [Fact]
    public void ValidateArchitectureDrift_ReturnsOne_WhenServiceIsMissingFromArchitectureDocs()
    {
        Write("docker/docker-compose.yml", @"
services:
  gateway:
    image: gateway
  new-service:
    image: new-service
");
        Write("ARCHITECTURE.md", "# Architecture\nIncludes gateway service only.");
        Write("docs/SDLC/02_Technical_Design_Document_TDD.md", "# TDD\nIncludes gateway and new-service services.");

        int result = ArchitectureDriftValidatorService.ValidateArchitectureDrift(_root);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateArchitectureDrift_ReturnsOne_WhenServiceIsMissingFromTddDocs()
    {
        Write("docker/docker-compose.yml", @"
services:
  gateway:
    image: gateway
  new-service:
    image: new-service
");
        Write("ARCHITECTURE.md", "# Architecture\nIncludes gateway and new-service services.");
        Write("docs/SDLC/02_Technical_Design_Document_TDD.md", "# TDD\nIncludes gateway service only.");

        int result = ArchitectureDriftValidatorService.ValidateArchitectureDrift(_root);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateArchitectureDrift_ReturnsZero_WhenMissingServiceIsIgnoredInExclusionLedger()
    {
        Write("docker/docker-compose.yml", @"
services:
  gateway:
    image: gateway
  dotnet-dev:
    image: dotnet-dev
");
        Write("ARCHITECTURE.md", "# Architecture\nIncludes gateway service.");
        Write("docs/SDLC/02_Technical_Design_Document_TDD.md", "# TDD\nIncludes gateway service.");
        Write(".arch-drift-ignore", "# Exclusion Ledger\ndotnet-dev\n");

        int result = ArchitectureDriftValidatorService.ValidateArchitectureDrift(_root);
        result.Should().Be(0);
    }

    [Fact]
    public void ValidateArchitectureDrift_ReturnsOne_WhenComposeFileIsMissing()
    {
        Write("ARCHITECTURE.md", "# Architecture\n");
        Write("docs/SDLC/02_Technical_Design_Document_TDD.md", "# TDD\n");

        int result = ArchitectureDriftValidatorService.ValidateArchitectureDrift(_root);
        result.Should().Be(1);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
