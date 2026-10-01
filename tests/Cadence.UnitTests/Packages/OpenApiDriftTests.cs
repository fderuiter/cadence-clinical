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
    public void FindDrift_WhenTargetedExport_DoesNotReportStaleSpecsForUntargetedServices()
    {
        File.WriteAllText(Path.Combine(_exported, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "ctms_openapi.json"), Spec);
        File.WriteAllText(Path.Combine(_committed, "org_openapi.json"), Spec);

        OpenApiSyncService.FindDrift(_exported, _committed, isTargetedExport: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData("apps/execution/src/Endpoints/SubjectEndpoints.cs", "execution")]
    [InlineData("apps/kernel-api/src/Host/Program.cs", "kernel")]
    [InlineData("apps/ctms/src/Endpoints/CtmsEndpoints.cs", "ctms")]
    [InlineData("apps/ai_gateway/src/Program.cs", "ai_gateway")]
    [InlineData("packages/contracts/src/Dto/SubjectDto.cs", null)]
    [InlineData("apps/web/src/components/Footer.vue", null)]
    public void ResolveServiceFromPath_MapsMicroservicePathsCorrectly(string path, string? expectedService)
    {
        PreCommitOpenApiService.ResolveServiceFromPath(path).Should().Be(expectedService);
    }

    [Fact]
    public void ExecutePreCommitOpenApi_WhenNoCsharpFilesStaged_BypassesServiceStartup()
    {
        string[] targetFiles = ["apps/web/src/components/Footer.vue", "docs/README.md"];
        int result = PreCommitOpenApiService.ExecutePreCommitOpenApi(quick: true, targetFiles: targetFiles);
        result.Should().Be(0);
    }
}
