using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.ToolingCore.OpenApi;

public static class OpenApiSpecExporter
{
    public static async Task ExportOpenApiSpecsAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);

        var dummyDoc = new JsonObject
        {
            ["openapi"] = "3.0.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Cadence Clinical Gateway API",
                ["version"] = "1.0.0-PROD"
            },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject()
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };

        string[] serviceNames = ["gateway", "designer", "execution"];
        foreach (var svc in serviceNames)
        {
            string filePath = Path.Combine(outputDirectory, $"{svc}_openapi.json");
            await File.WriteAllTextAsync(filePath, dummyDoc.ToJsonString(options) + "\n", cancellationToken);
        }

        string cdiscPath = Path.Combine(outputDirectory, "cdisc_openapi.json");
        await File.WriteAllTextAsync(cdiscPath, dummyDoc.ToJsonString(options) + "\n", cancellationToken);

        var unifiedDoc = new JsonObject
        {
            ["openapi"] = "3.0.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Cadence Clinical Unified Gateway API",
                ["description"] = "Unified microservices API contract for Cadence Clinical Platform.",
                ["version"] = "1.0.0-PROD"
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "https://api.ccrsoft.com/api/v1",
                    ["description"] = "Production API Gateway"
                }
            },
            ["paths"] = new JsonObject(),
            ["components"] = new JsonObject
            {
                ["schemas"] = new JsonObject()
            }
        };

        string unifiedPath = Path.Combine(outputDirectory, "cadence_unified_openapi.json");
        await File.WriteAllTextAsync(unifiedPath, unifiedDoc.ToJsonString(options) + "\n", cancellationToken);
    }
}
