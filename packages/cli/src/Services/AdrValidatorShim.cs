namespace Cadence.Cli.Services;

public static class AdrValidatorService
{
    public static bool ValidateAdrs(bool checkGitDiff = true, string[]? targets = null)
    {
        return Cadence.ToolingCore.Services.AdrValidatorService.ValidateAdrs(checkGitDiff, targets);
    }
}
