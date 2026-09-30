using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ReadmeValidatorServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "readme-validator-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ValidateReadmes_IgnoresADirectoryHoldingOnlyBuildOutput()
    {
        Write("packages/documented/README.md", "# Documented\n");
        Write("packages/deleted/src/bin/Debug/net10.0/Old.dll", "");
        Write("packages/deleted/src/obj/project.assets.json", "{}");

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(0);
    }

    [Fact]
    public void ValidateReadmes_FailsForAProjectWithoutAReadme()
    {
        Write("packages/documented/README.md", "# Documented\n");
        Write("packages/undocumented/src/Thing.cs", "namespace X;");
        Write("packages/undocumented/src/bin/Debug/Thing.dll", "");

        ReadmeValidatorService.ValidateReadmes(_root).Should().Be(1);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
