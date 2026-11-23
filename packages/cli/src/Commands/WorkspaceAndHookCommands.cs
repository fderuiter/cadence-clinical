using System;
using System.CommandLine;

using static Cadence.Cli.Commands.CommandRegistrationHelpers;

namespace Cadence.Cli.Commands;

/// <summary>Registers database, migrations, settings, scaffolding, agent hook, and interactive wizard branches.</summary>
internal static class WorkspaceAndHookCommands
{
    public static void Register(RootCommand root)
    {
        var db = AddBranch(root, "db", "Database management and migrations");
        AddCommand(db, "reset", "Resets the local PostgreSQL database and applies all migrations", DbCommands.Reset);
        AddCommand(db, "migrate", "Applies pending EF Core migrations across all PostgreSQL schemas", DbCommands.Migrate);

        var migrations = AddBranch(root, "migrations", "Unified EF Core migration tooling across all 18 persistence modules");
        AddCommand(migrations, "add", "Scaffolds a new EF Core migration for a persistence module", MigrationCommands.ConfigureAdd);
        AddCommand(migrations, "script", "Generates idempotent SQL review scripts in TestResults/migrations/", MigrationCommands.ConfigureScript);
        AddCommand(migrations, "list", "Lists migrations across one or all 18 persistence modules", MigrationCommands.ConfigureList);
        AddCommand(migrations, "verify", "Verifies snapshots, .Designer.cs companions, attributes and DDL uniqueness across modules", MigrationCommands.ConfigureVerify);

        var settings = AddBranch(root, "settings", "Manage microservice configuration settings models and template exports");
        AddCommand(settings, "export", "Checks that environment variables read in code are listed in .env.example and docs", ToolCommands.ConfigureExtractEnvVars);

        var scaffold = AddBranch(root, "scaffold", "Architecture scaffolding tools");
        AddCommand(scaffold, "service", "Scaffolds a new C# Clean Architecture microservice", ScaffoldCommands.ConfigureService);
        AddCommand(scaffold, "adr", "Scaffolds a new Architectural Decision Record (ADR)", ScaffoldCommands.ConfigureAdr);
        AddCommand(scaffold, "post", "Scaffolds a new engineering blog post in docs/blog/", ScaffoldCommands.ConfigurePost);

        var hook = AddBranch(root, "hook", "Agent lifecycle hooks: read the hook payload on stdin, exit 2 to block (PRD-SYS-053)");
        AddCommand(hook, "pre-tool", "Blocks destructive git and shell commands and hand edits to generated GxP documents",
            () => HookCommands.PreTool(Console.In, Console.Error));
        AddCommand(hook, "post-write", "Lints the query style of an edited C# file",
            () => HookCommands.PostWrite(Console.In, Console.Error));
        AddCommand(hook, "pre-invocation", "Prints the GxP working rules for the agent",
            () => HookCommands.PreInvocation(Console.Out));
        AddCommand(hook, "stop", "Blocks finishing while the traceability matrix is out of date",
            () => HookCommands.Stop(Console.Error));

        var wizard = AddBranch(root, "wizard", "Interactive onboarding and scaffolding wizards");
        AddCommand(wizard, "onboarding", "Interactive full-stack developer setup wizard", () => WizardCommands.Run("setup_wizard"));
        AddCommand(wizard, "dotnet", "Interactive .NET 10 onboarding wizard", () => WizardCommands.Run("setup_dotnet_wizard"));
        AddCommand(wizard, "migrate", "Interactive microservice transcription wizard", () => WizardCommands.Run("migrate_service_wizard"));
    }
}
