namespace Cadence.Cli.Services;

public static class QueryStyleLinterService
{
    public static bool LintQueryStyle(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.QueryLinterService.LintQueries(targetFiles) == 0;
    }
}
