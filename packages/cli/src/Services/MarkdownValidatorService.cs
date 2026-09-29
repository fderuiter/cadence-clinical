namespace Cadence.Cli.Services;

public static class MarkdownValidatorService
{
    public static bool ValidateMarkdown(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.MarkdownValidatorService.ValidateMarkdown(targetFiles) == 0;
    }
}
