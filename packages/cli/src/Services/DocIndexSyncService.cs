namespace Cadence.Cli.Services;

public static class DocIndexSyncService
{
    public static int SyncDocIndex()
    {
        return Cadence.ToolingCore.Services.DocIndexSyncService.SyncDocIndex();
    }
}
