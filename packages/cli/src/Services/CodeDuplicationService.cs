namespace Cadence.Cli.Services;

public static class CodeDuplicationService
{
    public static bool DetectDuplication(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.DuplicationDetectorService.DetectDuplication(targetFiles) == 0;
    }
}
