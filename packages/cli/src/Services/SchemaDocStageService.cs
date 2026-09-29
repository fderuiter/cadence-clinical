namespace Cadence.Cli.Services;

public static class SchemaDocStageService
{
    public static bool StageSchemaDoc()
    {
        return Cadence.ToolingCore.Services.PreCommitSchemaDocService.ExecutePreCommitSchemaDoc() == 0;
    }
}
