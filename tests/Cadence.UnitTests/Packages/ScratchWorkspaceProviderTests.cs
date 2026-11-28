using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Storage;
using Cadence.Topology;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ScratchWorkspaceProviderTests
{
    [Fact]
    public void AddCadenceScratchWorkspace_CreatesOwnerOnlyDirectoryAndFile_AndValidatesOnStart()
    {
        string root = Path.Combine(Path.GetTempPath(), $"cadence-scratch-unit-{Guid.NewGuid():N}");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Scratch:RootPath"] = root
                })
                .Build();

            var services = new ServiceCollection();
            services.AddCadenceScratchWorkspace(config);
            using var provider = services.BuildServiceProvider();

            var scratch = provider.GetRequiredService<IScratchWorkspaceProvider>();
            scratch.RootPath.Should().Be(Path.GetFullPath(root));
            Directory.Exists(root).Should().BeTrue();

            string subDir = scratch.CreateDirectory("sce_run");
            subDir.Should().StartWith(scratch.RootPath);
            Directory.Exists(subDir).Should().BeTrue();

            var (filePath, stream) = scratch.CreateFile("slice", ".tmp");
            using (stream)
            {
                stream.WriteByte(0x42);
            }

            filePath.Should().StartWith(scratch.RootPath);
            File.Exists(filePath).Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                var rootMode = File.GetUnixFileMode(root);
                (rootMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute))
                    .Should().Be(UnixFileMode.None);

                var fileMode = File.GetUnixFileMode(filePath);
                (fileMode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                             UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute))
                    .Should().Be(UnixFileMode.None);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/scratch")]
    public void AddCadenceScratchWorkspace_RejectsBlankOrRelativeRootPath(string? rootPath)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Scratch:RootPath"] = rootPath })
            .Build();
        var services = new ServiceCollection();
        services.AddCadenceScratchWorkspace(config);
        using var provider = services.BuildServiceProvider();

        Action resolve = () => _ = provider.GetRequiredService<IScratchWorkspaceProvider>();
        resolve.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("Scratch:RootPath");
    }

    [Fact]
    public void ValidateAndPrepareRoot_RejectsTraversalRootSharedTempSymlinkAndGroupAccessibleDirectories()
    {
        string traversal = Path.Combine(Path.GetTempPath(), "cadence-scratch", "..", "escaped");
        Action traversalAct = () => _ = new FileSystemScratchWorkspaceProvider(new ScratchWorkspaceOptions { RootPath = traversal });
        traversalAct.Should().Throw<InvalidOperationException>().WithMessage("*'..'*");

        string fsRoot = Path.GetPathRoot(Path.GetTempPath())!;
        Action rootAct = () => _ = new FileSystemScratchWorkspaceProvider(new ScratchWorkspaceOptions { RootPath = fsRoot });
        rootAct.Should().Throw<InvalidOperationException>().WithMessage("*filesystem root*");

        string sharedTemp = Path.GetTempPath();
        Action tempAct = () => _ = new FileSystemScratchWorkspaceProvider(new ScratchWorkspaceOptions { RootPath = sharedTemp });
        tempAct.Should().Throw<InvalidOperationException>().WithMessage("*shared system temporary directory*");

        if (!OperatingSystem.IsWindows())
        {
            string groupReadable = Path.Combine(Path.GetTempPath(), $"cadence-scratch-0755-{Guid.NewGuid():N}");
            string symlinkTarget = Path.Combine(Path.GetTempPath(), $"cadence-scratch-target-{Guid.NewGuid():N}");
            string symlinkPath = Path.Combine(Path.GetTempPath(), $"cadence-scratch-link-{Guid.NewGuid():N}");
            Directory.CreateDirectory(groupReadable);
            Directory.CreateDirectory(symlinkTarget);
            File.SetUnixFileMode(symlinkTarget, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(
                groupReadable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            Directory.CreateSymbolicLink(symlinkPath, symlinkTarget);

            try
            {
                Action permAct = () => _ = new FileSystemScratchWorkspaceProvider(new ScratchWorkspaceOptions { RootPath = groupReadable });
                permAct.Should().Throw<InvalidOperationException>().WithMessage("*group or other permissions*");

                Action linkAct = () => _ = new FileSystemScratchWorkspaceProvider(new ScratchWorkspaceOptions { RootPath = symlinkPath });
                linkAct.Should().Throw<InvalidOperationException>().WithMessage("*symbolic link*");
            }
            finally
            {
                if (Directory.Exists(symlinkPath)) Directory.Delete(symlinkPath);
                if (Directory.Exists(symlinkTarget)) Directory.Delete(symlinkTarget, recursive: true);
                if (Directory.Exists(groupReadable)) Directory.Delete(groupReadable, recursive: true);
            }
        }
    }

    [Fact]
    public void ServiceTopology_EnforcesFailClosedUrlAndDisabledContracts()
    {
        var execution = ServiceTopology.Get("execution");

        Action prodUnset = () => ServiceTopology.ResolveServiceUrl(execution, null, isDevelopment: false, allowLocalhostFallbacks: true);
        prodUnset.Should().Throw<InvalidOperationException>().WithMessage("EXECUTION_SERVICE_URL*outside Development*");

        Action devWithoutOptIn = () => ServiceTopology.ResolveServiceUrl(execution, null, isDevelopment: true, allowLocalhostFallbacks: false);
        devWithoutOptIn.Should().Throw<InvalidOperationException>().WithMessage($"*{ServiceTopology.AllowLocalhostFallbacksKey}*");

        var devOptIn = ServiceTopology.ResolveServiceUrl(execution, null, isDevelopment: true, allowLocalhostFallbacks: true);
        devOptIn.Should().Be(execution.LocalUrl);

        Action disabledRequired = () => ServiceTopology.ResolveServiceUrl(execution, "disabled", isDevelopment: false, allowLocalhostFallbacks: false);
        disabledRequired.Should().Throw<InvalidOperationException>();

        var disabledOptional = ServiceTopology.ResolveServiceUrlOrDisabled(execution, "disabled", isDevelopment: false, allowLocalhostFallbacks: false);
        disabledOptional.Should().BeNull();
    }

    [Fact]
    public void AppsDirectoryBuildProps_BansPathGetTempPathAcrossAllProductionApps()
    {
        string repoRoot = FindRepoRoot();
        string appsProps = File.ReadAllText(Path.Combine(repoRoot, "apps", "Directory.Build.props"));
        string appsBanned = File.ReadAllText(Path.Combine(repoRoot, "apps", "BannedSymbols.txt"));

        appsProps.Should().Contain("BannedSymbols.txt");
        appsBanned.Should().Contain("M:System.IO.Path.GetTempPath");

        var appCsFiles = Directory.EnumerateFiles(Path.Combine(repoRoot, "apps"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (string csFile in appCsFiles)
        {
            File.ReadAllText(csFile).Should().NotContain("GetTempPath(", $"production app file {csFile} must not call Path.GetTempPath");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cadence.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate Cadence.sln.");
    }
}
