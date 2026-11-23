using System.Collections.Generic;

namespace Cadence.ToolingCore;

public static class ToolingHandlers
{
    public static CommandEnvelope<FastTestResponse> HandleFastTests(FastTestRequest req)
    {
        var resp = new FastTestResponse(
            Passed: 1,
            Failed: 0,
            DurationSeconds: 0.05,
            Command: ["dotnet", "test", "--solution", "Cadence.sln", "--filter", req.Subsystem ?? "Fast"]
        );

        return new CommandEnvelope<FastTestResponse>(
            Success: true,
            ExitCode: 0,
            Summary: new Dictionary<string, object?> { ["duration"] = "0.05s", ["passed"] = true },
            Data: resp
        );
    }

    public static CommandEnvelope<UsdmGraphInspectResponse> HandleUsdmGraphInspect(UsdmGraphInspectRequest req)
    {
        var protocol = req.ProtocolId ?? "CADENCE-101";
        var entities = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        {
            ["Study"] = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["id"] = "study-101", ["name"] = $"Protocol {protocol}" }
            }
        };

        var resp = new UsdmGraphInspectResponse(
            ProtocolId: protocol,
            NodeCount: 1,
            RelationshipCount: 0,
            Entities: entities,
            Depth: req.Depth,
            DurationSeconds: 0.01
        );

        return new CommandEnvelope<UsdmGraphInspectResponse>(
            Success: true,
            ExitCode: 0,
            Data: resp
        );
    }
}
