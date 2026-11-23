using System;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DbCommands
{
    public static int Reset() =>
        DatabaseResetService.ResetDatabasesAsync().GetAwaiter().GetResult();

    public static int Migrate()
    {
        AnsiConsole.MarkupLine("[cyan]Applying EF Core PostgreSQL migrations across all schemas...[/]");
        try
        {
            string pgConnStr = DatabaseResetService.ResolvePostgresConnectionString();
            return DatabaseResetService.RunDatabaseMigrator(pgConnStr);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]ERROR: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }
}
