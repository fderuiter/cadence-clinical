using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class OpenApiDriftTests : IDisposable
{
    private const string Spec = """
        {
          "openapi": "3.1.1",
          "info": { "title": "Cadence.Apps.Ctms | v1", "version": "1.0.0" },
          "paths": {
            "/api/v1/ctms/status": { "get": { "operationId": "CtmsStatus", "summary": "Returns CTMS status." } },
            "/api/v1/ctms/letters/render": { "post": { "operationId": "RenderLetter", "summary": "Renders a | letter." } }
          }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "openapi-drift-" + Guid.NewGuid().ToString("N"));
    private readonly string _exported;
    private readonly string _committed;

    public OpenApiDriftTests()
    {
        _exported = Directory.CreateDirectory(Path.Combine(_root, "exported")).FullName;
        _committed = Directory.CreateDirectory(Path.Combine(_root, "committed")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindDrift_WhenSpecsAreEqualUpToFormatting_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec.ReplaceLineEndings("").Replace("  ", ""));

        OpenApiSyncService.FindDrift(_exported, _committed).Should().BeEmpty();
    }

    [Fact]
    public void FindDrift_ReportsMissingChangedAndStaleSpecs()
    {
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_exported, "org_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec.Replace("CtmsStatus", "Renamed"));
        File.WriteAllText(Path.Combine(_committed, "gateway_openapi.json"), Spec);

        OpenApiSyncService.FindDrift(_exported, _committed).Should().Equal(
            "ctms_openapi.json (out of date)",
            "org_openapi.json (missing)",
            "gateway_openapi.json (no service serves it)");
    }

    [Fact]
    public void RenderAll_ListsEachOperationAndSkipsTheAggregate()
    {
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "cdisc_openapi.json"), Spec);

        var pages = ApiDocsGeneratorService.RenderAll(_committed);

        pages.Keys.Should().Equal("ctms.md", "index.md");
        pages["ctms.md"].Should().Contain("| `GET` | `/api/v1/ctms/status` | CtmsStatus | Returns CTMS status. |")
            .And.Contain("| `POST` | `/api/v1/ctms/letters/render` | RenderLetter | Renders a \\| letter. |");
        pages["index.md"].Should().Contain("| [ctms](ctms.md) | 2 |");
    }

    [Fact]
    public void RenderAll_RendersSeparatePagesForKernelAndConsolidatedModules()
    {
        File.WriteAllText(
            Path.Combine(_committed, "kernel_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Kernel.Host | kernel", "version": "1.0.0" },
              "paths": {
                "/api/v1/kernel/status": { "get": { "operationId": "KernelStatus", "summary": "Returns kernel status." } }
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(_committed, "intelligence_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Kernel.Host | intelligence", "version": "1.0.0" },
              "paths": {
                "/api/v1/ai/status": { "get": { "operationId": "AiStatus", "summary": "Returns AI status." } }
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(_committed, "execution_forms_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Kernel.Host | execution_forms", "version": "1.0.0" },
              "paths": {
                "/api/v1/execution/forms": { "post": { "operationId": "ExecutionOpenForm", "summary": "Opens an eCRF form." } }
              }
            }
            """);

        var pages = ApiDocsGeneratorService.RenderAll(_committed);

        pages.Keys.Should().Equal("execution_forms.md", "index.md", "intelligence.md", "kernel.md");
        pages["kernel.md"].Should().Contain("# kernel API").And.Contain("`/api/v1/kernel/status`");
        pages["intelligence.md"].Should().Contain("# intelligence API").And.Contain("`/api/v1/ai/status`");
        pages["execution_forms.md"].Should().Contain("# execution_forms API").And.Contain("`/api/v1/execution/forms`");
        pages["index.md"].Should().Contain("| [execution_forms](execution_forms.md) | 1 |")
            .And.Contain("| [intelligence](intelligence.md) | 1 |")
            .And.Contain("| [kernel](kernel.md) | 1 |");
    }

    [Fact]
    public void FindDrift_WhenTargetedExport_DoesNotReportStaleSpecsForUntargetedServices()
    {
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "org_openapi.json"), Spec);

        OpenApiSyncService.FindDrift(_exported, _committed, isTargetedExport: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData("apps/execution/src/Endpoints/SubjectEndpoints.cs", "execution")]
    [InlineData("apps/execution/module/ExecutionModule.cs", "execution_forms")]
    [InlineData("apps/kernel-api/src/Host/Program.cs", "kernel")]
    [InlineData("apps/kernel/src/Sample.cs", "kernel")]
    [InlineData("apps/ctms/src/Endpoints/CtmsEndpoints.cs", "ctms")]
    [InlineData("apps/intelligence/src/IntelligenceModule.cs", "intelligence")]
    [InlineData("packages/contracts/src/Dto/SubjectDto.cs", null)]
    [InlineData("apps/web/src/components/Footer.vue", null)]
    public void ResolveServiceFromPath_MapsMicroservicePathsCorrectly(string path, string? expectedService)
    {
        PreCommitOpenApiService.ResolveServiceFromPath(path).Should().Be(expectedService);
    }

    [Fact]
    public void ResolveTargetClosureProjects_IncludesModuleAndKernelHostForConsolidatedModules()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        var executionClosure = PreCommitOpenApiService.ResolveTargetClosureProjects(
            repoRoot,
            ["apps/execution/module/ExecutionModule.cs"],
            ["execution_forms"],
            out bool requireRepoWideExecution);
        requireRepoWideExecution.Should().BeFalse();
        executionClosure.Should().BeEquivalentTo(
            "apps/execution/module/Cadence.Modules.Execution.csproj",
            "apps/kernel-api/src/Cadence.Kernel.Host.csproj");

        var intelligenceClosure = PreCommitOpenApiService.ResolveTargetClosureProjects(
            repoRoot,
            ["apps/intelligence/src/IntelligenceModule.cs"],
            ["intelligence"],
            out bool requireRepoWideIntelligence);
        requireRepoWideIntelligence.Should().BeFalse();
        intelligenceClosure.Should().BeEquivalentTo(
            "apps/intelligence/src/Cadence.Modules.Intelligence.csproj",
            "apps/kernel-api/src/Cadence.Kernel.Host.csproj");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExecutePreCommitOpenApi_WhenTargetFilesContainNoCsharpFiles_BypassesServiceStartup(bool quick)
    {
        string[] targetFiles = ["apps/web/src/components/Footer.vue", "docs/README.md"];
        int result = PreCommitOpenApiService.ExecutePreCommitOpenApi(quick: quick, targetFiles: targetFiles);
        result.Should().Be(0);
    }

    [Fact]
    public void AnalyzeBackendContractChanges_WhenTargetingSingleServiceProgramCs_SelectsOnlyThatService()
    {
        var (shouldExport, targetServices, hasCsharpFiles) =
            PreCommitOpenApiService.AnalyzeBackendContractChanges(
                ["apps/tickets/src/Program.cs"],
                PreCommitOpenApiService.FindRepoRoot());

        hasCsharpFiles.Should().BeTrue();
        shouldExport.Should().BeTrue();
        targetServices.Should().Equal("tickets");
    }

    [Theory]
    [InlineData(new string[0], 4)]
    [InlineData(new[] { "--max-parallelism", "1" }, 1)]
    [InlineData(new[] { "--output-dir", "/tmp/out", "--max-parallelism=8" }, 8)]
    public void TryParseMaxParallelism_ParsesDefaultAndExplicitWorkerCounts(string[] args, int expected)
    {
        bool ok = OpenApiSyncService.TryParseMaxParallelism(args, out int actual, out string? error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("--max-parallelism")]
    [InlineData("--max-parallelism", "0")]
    [InlineData("--max-parallelism", "-2")]
    [InlineData("--max-parallelism=abc")]
    public void TryParseMaxParallelism_RejectsMissingOrNonPositiveValues(params string[] args)
    {
        bool ok = OpenApiSyncService.TryParseMaxParallelism(args, out _, out string? error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AggregateOpenApiSpecs_PreservesOrdinalOrderRegardlessOfConcurrentExportOrder()
    {
        string ctmsJson = """
            {
              "openapi": "3.1.1",
              "paths": { "/api/v1/ctms/sites": { "get": { "operationId": "ListSites" } } },
              "components": { "schemas": { "Ctms_SiteDto": { "type": "object" } } }
            }
            """;
        string ticketsJson = """
            {
              "openapi": "3.1.1",
              "paths": { "/api/v1/tickets": { "get": { "operationId": "ListTickets" } } },
              "components": { "schemas": { "Tickets_TicketDto": { "type": "object" } } }
            }
            """;

        File.WriteAllText(Path.Combine(_exported, "tickets_openapi.json"), ticketsJson);
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), ctmsJson);

        var forwardOrder = new Dictionary<string, System.Text.Json.Nodes.JsonObject>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctms"] = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ctmsJson)!,
            ["tickets"] = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ticketsJson)!,
        };
        var reverseOrder = new Dictionary<string, System.Text.Json.Nodes.JsonObject>(StringComparer.OrdinalIgnoreCase)
        {
            ["tickets"] = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ticketsJson)!,
            ["ctms"] = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ctmsJson)!,
        };

        var aggregatedForward = OpenApiSyncService.AggregateOpenApiSpecs(_exported, _root, forwardOrder);
        var aggregatedReverse = OpenApiSyncService.AggregateOpenApiSpecs(_exported, _root, reverseOrder);

        aggregatedForward.ToJsonString().Should().Be(aggregatedReverse.ToJsonString());
        aggregatedForward["paths"]!.AsObject().Select(kv => kv.Key)
            .Should().Equal("/api/v1/ctms/sites", "/api/v1/tickets");
        aggregatedForward["components"]!["schemas"]!.AsObject().Select(kv => kv.Key)
            .Should().Equal("Ctms_SiteDto", "Tickets_TicketDto");
    }

    [Fact]
    public void ExporterBuildFreshness_SkipsBuildOnlyWhenCompiledOutputsPostdateAllTransitiveProjectInputs()
    {
        string repoDir = Directory.CreateDirectory(Path.Combine(_root, "freshness-repo")).FullName;
        var sourceTime = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var binaryTime = new DateTime(2026, 6, 1, 10, 5, 0, DateTimeKind.Utc);
        var editedTime = new DateTime(2026, 6, 1, 10, 10, 0, DateTimeKind.Utc);

        string globalJson = WriteTimestampedFile(repoDir, "global.json", "{}\n", sourceTime);
        string dirProps = WriteTimestampedFile(repoDir, "Directory.Build.props", "<Project />\n", sourceTime);
        WriteTimestampedFile(repoDir, "docs/shared.sql", "SELECT 1;\n", sourceTime);

        string libProject = WriteTimestampedFile(
            repoDir,
            "packages/lib/Cadence.SampleLib.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <EmbeddedResource Include="..\..\docs\shared.sql" />
              </ItemGroup>
            </Project>
            """,
            sourceTime);
        string libSource = WriteTimestampedFile(repoDir, "packages/lib/Model.cs", "namespace SampleLib; public sealed class Model {}\n", sourceTime);
        WriteTimestampedFile(repoDir, "packages/lib/obj/project.assets.json", "{}\n", sourceTime);

        string exporterProject = WriteTimestampedFile(
            repoDir,
            "packages/exporter/Cadence.SampleExporter.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\lib\Cadence.SampleLib.csproj" />
              </ItemGroup>
            </Project>
            """,
            sourceTime);
        WriteTimestampedFile(repoDir, "packages/exporter/Program.cs", "Console.WriteLine(\"ok\");\n", sourceTime);

        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeFalse("exporter binaries and restore assets do not exist yet");

        WriteTimestampedFile(repoDir, "packages/exporter/obj/project.assets.json", "{}\n", sourceTime);
        WriteTimestampedFile(repoDir, "packages/exporter/bin/Debug/net10.0/Cadence.SampleExporter.dll", "dll", binaryTime);
        WriteTimestampedFile(repoDir, "packages/exporter/bin/Debug/net10.0/Cadence.SampleExporter.deps.json", "{}", binaryTime);
        WriteTimestampedFile(repoDir, "packages/exporter/bin/Debug/net10.0/Cadence.SampleExporter.runtimeconfig.json", "{}", binaryTime);

        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeTrue("compiled exporter outputs postdate all closure inputs");

        File.SetLastWriteTimeUtc(libSource, editedTime);
        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeFalse("editing a transitive ProjectReference .cs file invalidates --no-build");
        File.SetLastWriteTimeUtc(libSource, sourceTime);
        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeTrue();

        File.SetLastWriteTimeUtc(Path.Combine(repoDir, "docs/shared.sql"), editedTime);
        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeFalse("editing an external EmbeddedResource invalidates --no-build");
        File.SetLastWriteTimeUtc(Path.Combine(repoDir, "docs/shared.sql"), sourceTime);

        File.SetLastWriteTimeUtc(dirProps, binaryTime);
        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeFalse("equal timestamp on Directory.Build.props must conservatively rebuild");
        File.SetLastWriteTimeUtc(dirProps, sourceTime);
        File.SetLastWriteTimeUtc(globalJson, sourceTime);
        File.SetLastWriteTimeUtc(libProject, sourceTime);
        ExporterBuildFreshness.CanSkipBuild(repoDir, exporterProject).Should().BeTrue();
    }

    [Fact]
    public void AggregateOpenApiSpecs_PublishesRootGatewayOriginsWithoutApiV1Suffix()
    {
        string ctmsJson = """
            {
              "openapi": "3.1.1",
              "paths": { "/api/v1/ctms/status": { "get": { "operationId": "CtmsStatus" } } }
            }
            """;
        var specs = new Dictionary<string, System.Text.Json.Nodes.JsonObject>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctms"] = (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ctmsJson)!,
        };

        var aggregated = OpenApiSyncService.AggregateOpenApiSpecs(_exported, _root, specs);
        var serverUrls = aggregated["servers"]!.AsArray()
            .Select(node => node!["url"]!.GetValue<string>())
            .ToList();

        serverUrls.Should().Equal("https://api.ccrsoft.com", "http://localhost:8000");
        serverUrls.Should().NotContain(url => url.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CommittedOpenApiSpecs_ContainNoLocalhostDefaultServersOrApiV1ServerDuplication()
    {
        string repoRoot = PreCommitOpenApiService.FindRepoRoot();
        string openApiDir = Path.Combine(repoRoot, "docs", "openapi");
        var specFiles = Directory.GetFiles(openApiDir, "*_openapi.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var perServiceSpecs = specFiles
            .Where(f => !string.Equals(Path.GetFileName(f), "cdisc_openapi.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        perServiceSpecs.Should().HaveCount(19);

        foreach (string specFile in perServiceSpecs)
        {
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(specFile))!.AsObject();
            var urls = doc["servers"]!.AsArray().Select(s => s!["url"]!.GetValue<string>()).ToList();
            urls.Should().NotBeEmpty($"'{Path.GetFileName(specFile)}' must declare servers");
            urls.Should().NotContain("http://localhost/", $"'{Path.GetFileName(specFile)}' must not emit http://localhost/");
            urls.Should().Equal("/");
        }

        var unified = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(openApiDir, "cdisc_openapi.json")))!.AsObject();
        var unifiedUrls = unified["servers"]!.AsArray().Select(s => s!["url"]!.GetValue<string>()).ToList();
        unifiedUrls.Should().Equal("https://api.ccrsoft.com", "http://localhost:8000");
        unifiedUrls.Should().NotContain(u => u.Contains("/api/v1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrontProxyConfigurations_KeepDefaultPlainHttpAndProvideOptInTlsRedirectWithProxyHealth()
    {
        string repoRoot = PreCommitOpenApiService.FindRepoRoot();
        string defaultConf = File.ReadAllText(Path.Combine(repoRoot, "docker", "nginx", "conf.d", "default.conf"));
        defaultConf.Should().Contain("listen 8000;")
            .And.Contain("location /_proxy_health")
            .And.NotContain("ssl_certificate");

        string tlsConfPath = Path.Combine(repoRoot, "docker", "nginx", "tls", "default.conf");
        File.Exists(tlsConfPath).Should().BeTrue();
        string tlsConf = File.ReadAllText(tlsConfPath);
        tlsConf.Should().Contain("listen 80;")
            .And.Contain("listen 443 ssl;")
            .And.Contain("location = /_proxy_health")
            .And.Contain("return 308 https://$host$request_uri;")
            .And.Contain("proxy_set_header X-Forwarded-Proto https;")
            .And.Contain("proxy_set_header X-Forwarded-Host ")
            .And.Contain("proxy_pass http://cadence_gateway;");

        string tlsComposePath = Path.Combine(repoRoot, "docker", "docker-compose.tls.yml");
        File.Exists(tlsComposePath).Should().BeTrue();
        File.ReadAllText(tlsComposePath).Should().Contain("./nginx/tls/default.conf:/etc/nginx/conf.d/default.conf:ro");
    }

    private static string WriteTimestampedFile(string rootDir, string relativePath, string content, DateTime lastWriteUtc)
    {
        string fullPath = Path.Combine(rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
        return fullPath;
    }
}
