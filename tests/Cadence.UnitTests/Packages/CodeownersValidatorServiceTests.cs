using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class CodeownersValidatorServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "codeowners-validator-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ValidateCodeowners_ReturnsError_WhenFileIsMissing()
    {
        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(1);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsError_WhenFileIsEmpty()
    {
        Write(".github/CODEOWNERS", "   \n\n  ");
        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(1);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsSuccess_WhenFileIsValid()
    {
        Write("apps/econsent/src/Program.cs", "class Program {}");
        Write("packages/kernel/src/Kernel.cs", "class Kernel {}");

        Write(".github/CODEOWNERS", "# Global ownership\n* @fderuiter\n/apps/econsent/ @org/team-econsent user@cadence.org\n/packages/kernel/ @fderuiter # kernel owners\n");

        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(0);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsError_WhenPathPatternDoesNotExist()
    {
        Write("apps/econsent/src/Program.cs", "class Program {}");
        Write(".github/CODEOWNERS", "* @fderuiter\n/apps/econsents/ @fderuiter\n");

        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(1);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsError_WhenOwnerHandleIsInvalid()
    {
        Write("apps/econsent/src/Program.cs", "class Program {}");

        // "fderuiter" is missing '@', "invalid_email@" is invalid email syntax
        Write(".github/CODEOWNERS", "* fderuiter\n/apps/econsent/ @fderuiter invalid_email@\n");

        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(1);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsSuccess_ForGlobWildcardsAndDirectories()
    {
        Write("apps/web/src/App.vue", "template");
        Write("docs/cli.md", "# CLI");

        Write(".github/CODEOWNERS", "apps/**/src @fderuiter\n*.md @fderuiter\n/docs/ @fderuiter\n");

        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(0);
    }

    [Fact]
    public void ValidateCodeowners_ReturnsError_WhenRuleHasNoOwners()
    {
        Write("docs/cli.md", "# CLI");

        Write(".github/CODEOWNERS", "docs/cli.md\n");

        CodeownersValidatorService.ValidateCodeowners(_root).Should().Be(1);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
