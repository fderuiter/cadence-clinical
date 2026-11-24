using System;
using System.IO;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ExporterBinaryResolverTests : IDisposable
{
    private const string ExporterProject = "packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj";
    private const string ExporterAssembly = "Cadence.OpenApiExporter";
    private const string ServiceProject = "apps/tickets/src/Cadence.Apps.Tickets.csproj";
    private const string ServiceAssembly = "Cadence.Apps.Tickets";

    private static readonly DateTime BaseTimestampUtc = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BuildTimestampUtc = new(2026, 6, 1, 10, 5, 0, DateTimeKind.Utc);
    private static readonly DateTime NewerBuildTimestampUtc = new(2026, 6, 1, 10, 10, 0, DateTimeKind.Utc);
    private static readonly DateTime StaleEditTimestampUtc = new(2026, 6, 1, 10, 15, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "exporter-resolver-" + Guid.NewGuid().ToString("N"));

    public ExporterBinaryResolverTests()
    {
        Directory.CreateDirectory(_root);
        WriteWithTimestamp("Directory.Build.props", "<Project />\n", BaseTimestampUtc);
        WriteWithTimestamp("Directory.Packages.props", "<Project />\n", BaseTimestampUtc);
        WriteWithTimestamp("BannedSymbols.txt", "# symbols\n", BaseTimestampUtc);
        WriteWithTimestamp(
            ServiceProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n",
            BaseTimestampUtc);
        WriteWithTimestamp("apps/tickets/src/Program.cs", "namespace Tickets;\npublic static class Program { }\n", BaseTimestampUtc);
        WriteWithTimestamp(
            ExporterProject,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\..\..\apps\tickets\src\Cadence.Apps.Tickets.csproj" />
              </ItemGroup>
            </Project>
            """,
            BaseTimestampUtc);
        WriteWithTimestamp(
            "packages/tooling_core/src/Cadence.OpenApiExporter/Program.cs",
            "namespace Cadence.OpenApiExporter;\npublic static class Program { }\n",
            BaseTimestampUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ResolveInvocation_WhenNoPrebuiltBinaryExists_FallsBackToDotnetRun()
    {
        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeFalse();
        invocation.PrebuiltDllPath.Should().BeNull();
        invocation.FileName.Should().Be("dotnet");
        invocation.Arguments.Should().Equal("run", "--project", ExporterProject, "--", "--output-dir", "/tmp/out");
    }

    [Fact]
    public void ResolveInvocation_WhenDebugBinaryIsFresh_ExecutesPrebuiltAssemblyWithoutDotnetRun()
    {
        string expectedDll = WritePrebuiltOutput("Debug", BuildTimestampUtc, BuildTimestampUtc);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out", "--services", "tickets"]);

        invocation.ReusedPrebuilt.Should().BeTrue();
        invocation.PrebuiltDllPath.Should().Be(expectedDll);
        invocation.FileName.Should().Be("dotnet");
        invocation.Arguments.Should().Equal(expectedDll, "--output-dir", "/tmp/out", "--services", "tickets");
    }

    [Fact]
    public void ResolveInvocation_WhenBothDebugAndReleaseAreFresh_SelectsNewestBuild()
    {
        WritePrebuiltOutput("Debug", BuildTimestampUtc, BuildTimestampUtc);
        string releaseDll = WritePrebuiltOutput("Release", NewerBuildTimestampUtc, NewerBuildTimestampUtc);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeTrue();
        invocation.PrebuiltDllPath.Should().Be(releaseDll);
    }

    [Fact]
    public void ResolveInvocation_WhenRuntimeConfigOrDepsJsonIsMissing_FallsBackToDotnetRun()
    {
        string debugDll = WritePrebuiltOutput("Debug", BuildTimestampUtc, BuildTimestampUtc);
        string runtimeConfig = Path.Combine(Path.GetDirectoryName(debugDll)!, $"{ExporterAssembly}.runtimeconfig.json");
        File.Delete(runtimeConfig);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeFalse();
        invocation.Arguments.Should().StartWith(["run", "--project", ExporterProject, "--"]);
    }

    [Fact]
    public void ResolveInvocation_WhenServiceSourceIsNewerThanBuiltAssembly_FallsBackToDotnetRun()
    {
        WritePrebuiltOutput("Debug", BuildTimestampUtc, BuildTimestampUtc);
        WriteWithTimestamp("apps/tickets/src/Program.cs", "namespace Tickets;\npublic static class Program { public static int V => 2; }\n", StaleEditTimestampUtc);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeFalse();
        invocation.Arguments.Should().StartWith(["run", "--project", ExporterProject, "--"]);
    }

    [Fact]
    public void ResolveInvocation_WhenRootMsBuildPropsIsNewerThanExporterAssembly_FallsBackToDotnetRun()
    {
        WritePrebuiltOutput("Debug", BuildTimestampUtc, BuildTimestampUtc);
        WriteWithTimestamp("Directory.Build.props", "<Project><!-- bumped --></Project>\n", StaleEditTimestampUtc);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeFalse();
    }

    [Fact]
    public void ResolveInvocation_WhenIncrementalBuildUpdatesReferencedDllInsideExporterBin_ReusesPrebuilt()
    {
        // Simulates MSBuild reference-assembly optimization: Program.cs in tickets was edited after Exporter.dll was compiled,
        // then 'dotnet build Cadence.sln' recompiled Cadence.Apps.Tickets.dll and copied it into Exporter's bin/Debug/net10.0.
        WriteWithTimestamp("apps/tickets/src/Program.cs", "namespace Tickets;\npublic static class Program { public static int V => 2; }\n", NewerBuildTimestampUtc);
        string debugDll = WritePrebuiltOutput(
            "Debug",
            exporterTimestampUtc: BuildTimestampUtc,
            serviceDllTimestampUtc: StaleEditTimestampUtc);

        var invocation = ExporterBinaryResolver.ResolveInvocation(_root, ExporterProject, ["--output-dir", "/tmp/out"]);

        invocation.ReusedPrebuilt.Should().BeTrue();
        invocation.PrebuiltDllPath.Should().Be(debugDll);
    }

    private string WritePrebuiltOutput(string configuration, DateTime exporterTimestampUtc, DateTime serviceDllTimestampUtc)
    {
        string binRel = $"packages/tooling_core/src/Cadence.OpenApiExporter/bin/{configuration}/net10.0";
        string dllRel = $"{binRel}/{ExporterAssembly}.dll";
        WriteWithTimestamp(dllRel, "MZ", exporterTimestampUtc);
        WriteWithTimestamp($"{binRel}/{ExporterAssembly}.deps.json", "{}\n", exporterTimestampUtc);
        WriteWithTimestamp($"{binRel}/{ExporterAssembly}.runtimeconfig.json", "{}\n", exporterTimestampUtc);
        WriteWithTimestamp($"{binRel}/{ServiceAssembly}.dll", "MZ", serviceDllTimestampUtc);
        return Path.Combine(Path.GetFullPath(_root), dllRel.Replace('/', Path.DirectorySeparatorChar));
    }

    private void WriteWithTimestamp(string relativePath, string content, DateTime writeTimeUtc)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, writeTimeUtc);
    }
}
