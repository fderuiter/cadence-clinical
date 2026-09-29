namespace Cadence.Cli.Services;

public static class PathPatternsValidatorService
{
    public static bool ValidatePathPatterns(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.PathPatternValidatorService.ValidatePathPatterns(targetFiles) == 0;
    }
}
