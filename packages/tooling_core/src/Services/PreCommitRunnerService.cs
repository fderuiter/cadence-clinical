using System;

namespace Cadence.ToolingCore.Services;

public static class PreCommitRunnerService
{
    public static int RunPreCommitHooks(string? repoRoot = null)
    {
        Console.WriteLine("Executing native .NET pre-commit quality hooks...");
        return GatingPipelineService.RunGatingPipeline(repoRoot);
    }
}
