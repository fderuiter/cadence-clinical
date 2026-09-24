using Cadence.Cli.Commands;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("Cadence")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold cyan]Cadence Clinical Research Software Platform[/] [grey](.NET 10 / C# 14)[/]\n");

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("cadence");

            config.AddCommand<DoctorCommand>("doctor")
                .WithDescription("Validates system dependencies and local datastores");

            config.AddCommand<CheckCommand>("check")
                .WithDescription("Runs architecture sentinels and quality gates");

            config.AddCommand<TestCommand>("test")
                .WithDescription("Runs fast unit tests or testcontainer integration suites");

            config.AddCommand<DevCommand>("dev")
                .WithDescription("Launches multi-service developer process orchestrator");

            config.AddCommand<FixCommand>("fix")
                .WithDescription("Executes self-healing auto-formatting and ADR validation pipeline");

            config.AddBranch("gxp", gxp =>
            {
                gxp.AddCommand<GxPSyncCommand>("sync")
                    .WithDescription("Executes test suites and regenerates RTM compliance docs");
                gxp.AddCommand<GxPValidateCommand>("validate")
                    .WithDescription("Validates RTM compliance matrix against design requirements");
            });

            config.AddBranch("db", db =>
            {
                db.SetDescription("Database management and migrations");
                db.AddCommand<DbResetCommand>("reset")
                    .WithDescription("Resets PostgreSQL, SQLite, and Neo4j datastores");
                db.AddCommand<DbMigrateCommand>("migrate")
                    .WithDescription("Runs pre-boot EF Core database migrations");
                db.AddCommand<DbStatusCommand>("status")
                    .WithDescription("Checks status of local databases");
            });

            config.AddBranch("scaffold", scaffold =>
            {
                scaffold.SetDescription("Architecture scaffolding tools");
                scaffold.AddCommand<ScaffoldServiceCommand>("service")
                    .WithDescription("Scaffolds a new C# Clean Architecture microservice");
                scaffold.AddCommand<ScaffoldAdrCommand>("adr")
                    .WithDescription("Scaffolds a new Architectural Decision Record (ADR)");
            });

            config.AddBranch("setup", setup =>
            {
                setup.SetDescription("Environment and credentials setup");
                setup.AddCommand<SetupCredentialsCommand>("credentials")
                    .WithDescription("Configures local development credentials and secrets");
            });

            config.AddBranch("record", record =>
            {
                record.SetDescription("E2E recording and media tools");
                record.AddCommand<RecordSetupGDriveCommand>("setup-gdrive")
                    .WithDescription("Configures Google Drive integration for session recordings");
                record.AddCommand<RecordListCommand>("list")
                    .WithDescription("Lists recorded session videos");
                record.AddCommand<RecordRunCommand>("run")
                    .WithDescription("Runs Playwright E2E recording session");
            });

            config.AddBranch("cdisc", cdisc =>
            {
                cdisc.SetDescription("CDISC / USDM data standards tools");
                cdisc.AddCommand<CdiscExportCommand>("export")
                    .WithDescription("Exports USDM / CDISC compliance artifacts");
            });

            config.AddBranch("mcp", mcp =>
            {
                mcp.SetDescription("Model Context Protocol (MCP) AI integration server");
                mcp.AddCommand<McpServerCommand>("server")
                    .WithDescription("Starts Model Context Protocol stdio JSON-RPC server");
            });

            config.AddBranch("wizard", wizard =>
            {
                wizard.SetDescription("Interactive onboarding and scaffolding wizards");
                wizard.AddCommand<WizardOnboardingCommand>("onboarding")
                    .WithDescription("Interactive full-stack developer setup wizard");
                wizard.AddCommand<WizardDotnetCommand>("dotnet")
                    .WithDescription("Interactive .NET 10 onboarding wizard");
                wizard.AddCommand<WizardMigrateCommand>("migrate")
                    .WithDescription("Interactive microservice transcription wizard");
                wizard.AddCommand<WizardDecommissionCommand>("decommission")
                    .WithDescription("Interactive Python decommissioning wizard");
            });
        });

        return app.Run(args);
    }
}
