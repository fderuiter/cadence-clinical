using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static class ScaffoldServiceService
{
    public static int ScaffoldService(string serviceName, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine($"Scaffolding new Clean Architecture C# microservice '{serviceName}'...");

        string appDir = Path.Combine(repoRoot, "apps", serviceName, "src");
        Directory.CreateDirectory(appDir);

        string csprojPath = Path.Combine(appDir, $"Cadence.Apps.{Capitalize(serviceName)}.csproj");
        string csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <RootNamespace>Cadence.Apps." + Capitalize(serviceName) + @"</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include=""..\..\..\packages\engine_core\src\Cadence.Core.Domain.csproj"" />
    <ProjectReference Include=""..\..\..\packages\security\src\Cadence.Security.csproj"" />
    <ProjectReference Include=""..\..\..\packages\telemetry\src\Cadence.Telemetry.csproj"" />
  </ItemGroup>
</Project>
";
        File.WriteAllText(csprojPath, csprojContent);

        string programPath = Path.Combine(appDir, "Program.cs");
        string programContent = @"using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet(""/health"", () => Results.Ok(new { status = ""healthy"", service = """ + serviceName + @""" }));

app.Run();

public partial class Program { }
";
        File.WriteAllText(programPath, programContent);

        Console.WriteLine($"✔ Scaffolded service '{serviceName}' at {appDir}");
        return 0;
    }

    private static string Capitalize(string str) =>
        string.IsNullOrEmpty(str) ? str : char.ToUpperInvariant(str[0]) + str[1..];

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
