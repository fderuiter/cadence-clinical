using System;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class DevEnvironmentValidatorServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dev-env-validator-tests-" + Guid.NewGuid().ToString("N"));

    public DevEnvironmentValidatorServiceTests()
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
    public void ValidateDevEnvironment_WithCurrentRepo_ReturnsSuccess()
    {
        int result = DevEnvironmentValidatorService.ValidateDevEnvironment();
        result.Should().Be(0);
    }

    [Fact]
    public void NormalizeServiceName_NormalizesVariousFormats()
    {
        DevEnvironmentValidatorService.NormalizeServiceName("front proxy (nginx) → gateway").Should().Be("gateway");
        DevEnvironmentValidatorService.NormalizeServiceName("kernel (`apps/kernel-api`)").Should().Be("kernel");
        DevEnvironmentValidatorService.NormalizeServiceName("DESIGNER_SERVICE_URL").Should().Be("designer");
        DevEnvironmentValidatorService.NormalizeServiceName("Keycloak").Should().Be("keycloak");
    }

    [Fact]
    public void NormalizeServiceName_NormalizesFrontendServiceNames()
    {
        DevEnvironmentValidatorService.NormalizeServiceName("web").Should().Be("web");
        DevEnvironmentValidatorService.NormalizeServiceName("subject portal").Should().Be("subject_portal");
        DevEnvironmentValidatorService.NormalizeServiceName("subject-portal").Should().Be("subject_portal");
    }

    [Fact]
    public void ValidateDevEnvironment_WhenPortMismatchedInGateway_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 9002,
            appHostExecutionPort: 8002);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateDevEnvironment_WhenPortMismatchedInAppHost_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 9002);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Fact]
    public void ValidateDevEnvironment_WhenServiceMissingFromDoc_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 8002,
            includeExtraGatewayService: true);

        int result = DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot);
        result.Should().Be(1);
    }

    [Theory]
    [InlineData("| execution                     | 8002      |", "| execution                     | 8099      |")]
    [InlineData("| execution                     | 8002      | `edc`", "| execution                     | 8002      | `wrong_schema`")]
    [InlineData("5174", "5173")]
    public void ValidateDevEnvironment_WhenTargetGuideMutatesPortOrSchema_FailsClosed(string original, string mutated)
    {
        string realRoot = FindRepoRoot();
        CloneMinimalDevEnvWorkspace(realRoot, _tempRoot);
        string docPath = Path.Combine(_tempRoot, "docs", "LOCAL_DEV_ENVIRONMENT.md");
        string content = File.ReadAllText(docPath);
        content.Should().Contain(original);
        File.WriteAllText(docPath, content.Replace(original, mutated, StringComparison.Ordinal));

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(1);
    }

    [Fact]
    public void ValidateDevEnvironment_ParsesMultiPortSlashCellsAndDetectsFrontendDocDrift()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 8002,
            docFrontendPorts: "5173 / 5174",
            appHostWebPort: 3000,
            appHostSubjectPortalPort: 5174);

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(1);

        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 8002,
            docFrontendPorts: "3000 / 5174",
            appHostWebPort: 3000,
            appHostSubjectPortalPort: 5174);

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(0);
    }

    [Fact]
    public void ValidateDevEnvironment_WhenViteConfigPortDrifts_ReturnsError()
    {
        SetupMockRepo(
            docExecutionPort: 8002,
            gatewayExecutionPort: 8002,
            appHostExecutionPort: 8002,
            docFrontendPorts: "3000 / 5174",
            appHostWebPort: 3000,
            appHostSubjectPortalPort: 5174);

        string webDir = Path.Combine(_tempRoot, "apps", "web");
        string portalDir = Path.Combine(_tempRoot, "apps", "subject-portal");
        Directory.CreateDirectory(webDir);
        Directory.CreateDirectory(portalDir);

        File.WriteAllText(Path.Combine(webDir, "vite.config.js"), "const DEV_PORT = 5173;\nexport default { server: { port: DEV_PORT } };\n");
        File.WriteAllText(Path.Combine(portalDir, "vite.config.js"), "const DEV_PORT = 5174;\nexport default { server: { port: DEV_PORT } };\n");

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(1);

        File.WriteAllText(Path.Combine(webDir, "vite.config.js"), "const DEV_PORT = 3000;\nexport default { server: { port: DEV_PORT } };\n");

        DevEnvironmentValidatorService.ValidateDevEnvironment(_tempRoot).Should().Be(0);
    }

    [Fact]
    public void PortCheckerService_DefaultPorts_MatchesTopologyFrontendsAndDatastores()
    {
        int[] expected =
        [
            ServiceTopology.GatewayPort,
            .. ServiceTopology.Services.Select(s => s.Port),
            3000,
            5174,
            5432,
            8080,
        ];

        PortCheckerService.DefaultPorts.Should().Equal(expected);
        PortCheckerService.DefaultPorts.Should().NotContain([7687, 8017, 8019]);
    }

    private static void CloneMinimalDevEnvWorkspace(string sourceRoot, string destRoot)
    {
        CopyRelativeFile(sourceRoot, destRoot, "Cadence.slnx");
        CopyRelativeFile(sourceRoot, destRoot, "docs/LOCAL_DEV_ENVIRONMENT.md");
        CopyRelativeFile(sourceRoot, destRoot, "packages/topology/src/ServiceTopology.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/gateway/src/Program.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/apphost/src/Program.cs");
        CopyRelativeFile(sourceRoot, destRoot, "apps/web/vite.config.js");
        CopyRelativeFile(sourceRoot, destRoot, "apps/subject-portal/vite.config.js");

        string srcSchemaDir = Path.Combine(sourceRoot, "docs", "schema");
        string dstSchemaDir = Path.Combine(destRoot, "docs", "schema");
        Directory.CreateDirectory(dstSchemaDir);
        foreach (string schemaFile in Directory.EnumerateFiles(srcSchemaDir, "*.md", SearchOption.TopDirectoryOnly))
        {
            File.Copy(schemaFile, Path.Combine(dstSchemaDir, Path.GetFileName(schemaFile)), overwrite: true);
        }
    }

    private static void CopyRelativeFile(string sourceRoot, string destRoot, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string destPath = Path.Combine(destRoot, normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(Path.Combine(sourceRoot, normalized), destPath, overwrite: true);
    }

    private static string FindRepoRoot() => Cadence.Topology.RepositoryRoot.Find(AppContext.BaseDirectory);

    private void SetupMockRepo(
        int docExecutionPort,
        int gatewayExecutionPort,
        int appHostExecutionPort,
        bool includeExtraGatewayService = false,
        string? docFrontendPorts = null,
        int? appHostWebPort = null,
        int? appHostSubjectPortalPort = null)
    {
        File.WriteAllText(Path.Combine(_tempRoot, "Cadence.slnx"), "<Solution />\n");

        string docsDir = Path.Combine(_tempRoot, "docs");
        Directory.CreateDirectory(docsDir);

        string frontendRow = docFrontendPorts is not null
            ? $"| tickets | 8009 | `tickets` | web / subject portal | {docFrontendPorts} | none |"
            : "";

        string docContent = $"""
        # Local Development Environment
        ## 4. Services and ports
        | Service | Host port | Database schema | Service | Host port | Database schema |
        | --- | --- | --- | --- | --- | --- |
        | front proxy (nginx) → gateway | 8000 | none | eisf | 8010 | `eisf` |
        | execution | {docExecutionPort} | `edc` | Keycloak | 8080 | (own store) |
        {frontendRow}
        """;
        File.WriteAllText(Path.Combine(docsDir, "LOCAL_DEV_ENVIRONMENT.md"), docContent);

        string gatewayDir = Path.Combine(_tempRoot, "apps", "gateway", "src");
        Directory.CreateDirectory(gatewayDir);

        string extraLine = includeExtraGatewayService ? "string analyticsUrl = GetServiceUrl(\"ANALYTICS_SERVICE_URL\", 8099);" : "";
        string gatewayContent = $"""
        using System;
        var builder = WebApplication.CreateBuilder(args);
        string executionUrl = GetServiceUrl("EXECUTION_SERVICE_URL", {gatewayExecutionPort});
        {extraLine}
        """;
        File.WriteAllText(Path.Combine(gatewayDir, "Program.cs"), gatewayContent);

        string appHostDir = Path.Combine(_tempRoot, "apps", "apphost", "src");
        Directory.CreateDirectory(appHostDir);

        string viteLines = "";
        if (appHostWebPort is int webPort)
        {
            viteLines += $"""
            var web = builder.AddViteApp("web", "../../web")
                .WithPnpm(install: false)
                .WithEndpoint("http", endpoint => endpoint.Port = {webPort});
            """;
        }

        if (appHostSubjectPortalPort is int portalPort)
        {
            viteLines += $"""

            var subjectPortal = builder.AddViteApp("subject-portal", "../../subject-portal")
                .WithPnpm(install: false)
                .WithEndpoint("http", endpoint => endpoint.Port = {portalPort});
            """;
        }

        string appHostContent = $"""
        using System;
        var builder = DistributedApplication.CreateBuilder(args);
        Service("EXECUTION_SERVICE_URL", builder.AddProject<Projects.Cadence_Apps_Execution>("execution"), {appHostExecutionPort});
        var gateway = builder.AddProject<Projects.Cadence_Apps_Gateway>("gateway")
            .WithHttpEndpoint(port: 8000, name: "http");
        {viteLines}
        """;
        File.WriteAllText(Path.Combine(appHostDir, "Program.cs"), appHostContent);
    }
}
