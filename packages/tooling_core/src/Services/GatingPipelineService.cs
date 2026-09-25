using System;

namespace Cadence.ToolingCore.Services;

public static class GatingPipelineService
{
    public static int RunGatingPipeline(string? repoRoot = null)
    {
        Console.WriteLine("Executing complete CI quality gating pipeline...");
        int r1 = DuplicationDetectorService.DetectDuplication(null, repoRoot);
        if (r1 != 0) return r1;

        int r2 = ImportValidatorService.ValidateImports(repoRoot);
        if (r2 != 0) return r2;

        int r3 = OpenApiDriftValidatorService.ValidateDrift(repoRoot);
        if (r3 != 0) return r3;

        Console.WriteLine("✔ All gating pipeline checks passed cleanly.");
        return 0;
    }
}
