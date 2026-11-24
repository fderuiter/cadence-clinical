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
    public void FindDrift_ReportsMissingChangedAndStaleSpecs_AndIgnoresLegacy()
    {
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_exported, "org_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec.Replace("CtmsStatus", "Renamed"));
        File.WriteAllText(Path.Combine(_committed, "gateway_openapi.json"), Spec);
        Directory.CreateDirectory(Path.Combine(_committed, "legacy"));
        File.WriteAllText(Path.Combine(_committed, "legacy", "payments_openapi.json"), Spec);

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
    [InlineData("apps/gateway/src/Endpoints/SignatureTokenEndpoints.cs", "gateway")]
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
              "components": {
                "securitySchemes": { "BearerAuth": { "type": "http", "scheme": "bearer" } },
                "schemas": { "Ctms_SiteDto": { "type": "object" } }
              }
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
        aggregatedForward["components"]!["securitySchemes"]!.AsObject().Select(kv => kv.Key)
            .Should().Equal("BearerAuth");
        aggregatedForward["servers"]!.AsArray().Select(n => n!["url"]!.GetValue<string>())
            .Should().Equal("https://api.ccrsoft.com", "http://localhost:8000");
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

    private static string WriteTimestampedFile(string rootDir, string relativePath, string content, DateTime lastWriteUtc)
    {
        string fullPath = Path.Combine(rootDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, lastWriteUtc);
        return fullPath;
    }

    [Fact]
    public void FindOperationMetadataGaps_ReportsMissingOperationIdAndBlankSummary_AndCommittedSpecsHaveZeroGaps()
    {
        File.WriteAllText(
            Path.Combine(_committed, "sample_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Sample", "version": "1.0.0" },
              "paths": {
                "/api/v1/sample/ok": {
                  "get": { "operationId": "GetSample", "summary": "Returns sample." }
                },
                "/api/v1/sample/no-op-id": {
                  "post": { "summary": "Creates sample." }
                },
                "/api/v1/sample/blank-summary": {
                  "delete": { "operationId": "DeleteSample", "summary": "   " }
                }
              }
            }
            """);

        var gaps = OpenApiDriftValidatorService.FindOperationMetadataGaps(_committed);
        gaps.Should().Equal(
            "sample_openapi.json: DELETE /api/v1/sample/blank-summary (DeleteSample) is missing summary",
            "sample_openapi.json: POST /api/v1/sample/no-op-id is missing operationId");

        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        OpenApiDriftValidatorService.FindOperationMetadataGaps(Path.Combine(repoRoot, "docs", "openapi"))
            .Should().BeEmpty("every committed OpenAPI operation must declare a non-empty operationId and summary");
    }

    [Fact]
    public void RenderAll_EmitsDetailsSchemaLinksMaturityBadgesStubCalloutsAndCompleteSchemaCatalog()
    {
        File.WriteAllText(
            Path.Combine(_committed, "imaging_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Apps.Imaging | v1", "version": "1.0.0" },
              "components": {
                "securitySchemes": { "BearerAuth": { "type": "http", "scheme": "bearer" } },
                "schemas": {
                  "RoiSeedRequest": {
                    "type": "object",
                    "required": ["seedX"],
                    "properties": { "seedX": { "type": "integer", "format": "int32" } }
                  },
                  "ProblemDetails": {
                    "type": "object",
                    "properties": { "title": { "type": "string" }, "status": { "type": "integer" } }
                  }
                }
              },
              "paths": {
                "/api/v1/imaging/studies/{studyId}/roi-seed": {
                  "post": {
                    "operationId": "SegmentRoiSeed",
                    "summary": "Queues ROI seed segmentation (stub).",
                    "security": [{ "BearerAuth": [] }],
                    "x-cadence-permissions": ["imaging:write"],
                    "x-cadence-electronic-signature": true,
                    "parameters": [
                      { "name": "studyId", "in": "path", "required": true, "schema": { "type": "string", "format": "uuid" } },
                      { "name": "X-Sig-Token", "in": "header", "required": true, "schema": { "type": "string" } },
                      { "name": "X-Change-Reason", "in": "header", "required": true, "schema": { "type": "string" } }
                    ],
                    "requestBody": {
                      "required": true,
                      "content": { "application/json": { "schema": { "$ref": "#/components/schemas/RoiSeedRequest" } } }
                    },
                    "responses": {
                      "501": {
                        "description": "Not Implemented",
                        "content": { "application/problem+json": { "schema": { "$ref": "#/components/schemas/ProblemDetails" } } }
                      }
                    }
                  }
                }
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(_committed, "designer_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Apps.Designer | v1", "version": "1.0.0" },
              "paths": {
                "/api/v1/designer/studies": {
                  "get": { "operationId": "ListDesignerStudies", "summary": "Lists scaffold studies.", "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(_committed, "kernel_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Kernel.Host | kernel", "version": "1.0.0" },
              "paths": {
                "/api/v1/kernel/nodes/{nodeRef}": {
                  "get": {
                    "operationId": "GetKernelNode",
                    "summary": "Gets a kernel node.",
                    "x-kernel-errors": [{ "code": "K.Node.NotFound", "status": 404, "title": "Not Found" }],
                    "responses": { "200": { "description": "OK" } }
                  }
                },
                "/api/v1/ai/mcp": {
                  "post": { "operationId": "IntelligenceMcpStreamableHttp", "summary": "Streamable HTTP MCP endpoint.", "x-cadence-signed-in-caller": true, "responses": { "200": { "description": "OK" } } }
                },
                "/api/v1/execution/forms": {
                  "post": { "operationId": "OpenExecutionForm", "summary": "Opens an eCRF form.", "responses": { "200": { "description": "OK" } } }
                }
              }
            }
            """);

        var pages = ApiDocsGeneratorService.RenderAll(_committed);

        pages["imaging.md"].Should()
            .Contain("<span class=\"VPBadge tip\">Active</span>")
            .And.Contain("501 Not Implemented")
            .And.Contain("<details class=\"api-operation-details\">")
            .And.Contain("`X-Sig-Token`")
            .And.Contain("`X-Change-Reason`")
            .And.Contain("[`RoiSeedRequest`](#schema-roiseedrequest)")
            .And.Contain("### `RoiSeedRequest` {#schema-roiseedrequest}")
            .And.Contain("### `ProblemDetails` {#schema-problemdetails}");

        pages["designer.md"].Should()
            .Contain("<span class=\"VPBadge warning\">Scaffold</span>")
            .And.Contain("> **Scaffold Service (`ServiceTopology`):**");

        pages["kernel.md"].Should()
            .Contain("## Kernel Module")
            .And.Contain("## Intelligence Module")
            .And.Contain("## Execution Module")
            .And.Contain("`/api/v1/ai/mcp`")
            .And.Contain("`K.Node.NotFound`");
    }

    [Fact]
    public void RenderAll_EscapesVitePressHtmlPlaceholdersAndMustachesWhilePreservingCodeSpansAndComments()
    {
        File.WriteAllText(
            Path.Combine(_committed, "kernel_openapi.json"),
            """
            {
              "openapi": "3.1.1",
              "info": { "title": "Cadence.Kernel.Host | kernel", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "Operator": {
                    "type": "string",
                    "enum": ["<", "<=", ">", ">="],
                    "description": "Value <em>as entered</em> <!-- deid-ignore: ISO 8601 format examples, not PHI -->`2026-03-14` or <c>ProtocolVersion</c>."
                  }
                }
              },
              "paths": {
                "/api/v1/kernel/work-queues": {
                  "get": {
                    "operationId": "KernelReadWorkQueues",
                    "summary": "Reads queues for <tenant>.",
                    "description": "within is study:<uuid> or {{scope}}; inline `study:<uuid>` stays literal.",
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);

        var pages = ApiDocsGeneratorService.RenderAll(_committed);

        pages["kernel.md"].Should()
            .Contain("Reads queues for &lt;tenant&gt;.")
            .And.Contain("within is study:&lt;uuid&gt; or &#123;&#123;scope&#125;&#125;; inline `study:<uuid>` stays literal.")
            .And.Contain("- **Allowed Values:** `<`, `<=`, `>`, `>=`")
            .And.Contain("Value *as entered* <!-- deid-ignore: ISO 8601 format examples, not PHI -->`2026-03-14` or `ProtocolVersion`.");
    }

    [Fact]
    public void CommittedApiReferencePages_AreSynchronizedWithOpenApiSpecs_AndIncludeGatewayWithoutVueFiles()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string specDir = Path.Combine(repoRoot, "docs", "openapi");
        string apiDocsDir = Path.Combine(repoRoot, "docs", "api");

        Directory.GetFiles(apiDocsDir, "*.vue", SearchOption.AllDirectories)
            .Should().BeEmpty("API reference rendering must stay pure Markdown + native HTML <details>");
        PathPatternValidatorService.ValidatePathPatterns(repoRoot: repoRoot)
            .Should().Be(0, "no .vue files may exist outside allowed paths");

        var expectedPages = ApiDocsGeneratorService.RenderAll(specDir);
        expectedPages.Should().ContainKey("gateway.md").And.ContainKey("kernel.md").And.ContainKey("index.md");
        expectedPages.Count.Should().Be(21);

        foreach (var (fileName, expectedContent) in expectedPages)
        {
            string actualPath = Path.Combine(apiDocsDir, fileName);
            File.Exists(actualPath).Should().BeTrue($"{fileName} must be committed in docs/api/");
            File.ReadAllText(actualPath).ReplaceLineEndings("\n")
                .Should().Be(expectedContent.ReplaceLineEndings("\n"), $"{fileName} must not drift from docs/openapi/*.json");
        }
    }
}
