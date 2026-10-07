namespace Cadence.Cli.Services;

public static class CliDocsValidatorService
{
    public static bool ValidateCliDocs()
    {
        return Cadence.ToolingCore.Services.CliDocsGeneratorService.GenerateCliDocs(Program.BuildRootCommand(), check: true) == 0;
    }
}
