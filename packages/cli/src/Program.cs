using System;
using System.CommandLine;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli;

/// <summary>
/// Entry point of the <c>cadence</c> developer CLI. Commands are parsed with System.CommandLine, which is trim- and
/// Native AOT-safe; Spectre.Console is used only to render output. Every command and branch is registered here with
/// <see cref="AddCommand(Command, string, string, Func{int})"/> or <see cref="AddBranch"/>, which is what the
/// <c>cli-docs</c> gate reads to check that <c>docs/cli.md</c> documents each one.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // Without a terminal (a pipe, a log file, a git hook, an agent shell) Spectre can measure the console as -1
        // columns and drop every markup line, so pick a plain console first (#5417).
        CliConsole.ConfigureForCurrentProcess();

        // Hooks talk to an agent runtime over standard streams, so they print no banner.
        if (args is not ["hook", ..])
        {
            WriteBanner();
        }

        return BuildRootCommand().Parse(args).Invoke();
    }

    private static void WriteBanner()
    {
        AnsiConsole.Write(
            new FigletText("Cadence")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold cyan]Cadence Clinical Research Software Platform[/] [grey](.NET 10 / C# 14)[/]\n");
    }

    /// <summary>Builds the full command tree; exposed so tests can parse arguments without running a command.</summary>
    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Cadence developer CLI: quality gates, tests, documentation generators and wizards");

        AddCommand(root, "doctor", "Validates system dependencies and local datastores", DoctorCommand.Configure);
        AddCommand(root, "check", "Runs architecture sentinels and quality gates", CheckCommand.Configure);
        AddCommand(root, "test", "Runs fast unit tests or testcontainer integration suites", TestCommand.Configure);
        AddCommand(root, "ci", "Runs the pull-request jobs of .github/workflows/ci.yml locally for pre-push CI parity", LocalCiCommand.Configure);
        AddCommand(root, "rulesets", "Validates CI Gate coverage and .github/rulesets/*.json (when present) and syncs rulesets via gh api", RulesetsCommand.Configure);
        AddCommand(root, "dev", "Starts the .NET Aspire AppHost with all local services and datastores", DevCommand.Execute);
        AddCommand(root, "fix", "Executes self-healing formatting, index sync, schema and OpenAPI pipeline", ToolCommands.ConfigureFix);

        AddCommand(root, "detect-duplication", "Finds duplicated C# and TypeScript code blocks", () => DuplicationDetectorService.DetectDuplication());
        AddCommand(root, "validate-imports", "Checks cross-service import boundaries", () => ImportValidatorService.ValidateImports());
        AddCommand(root, "validate-dependencies", "Checks project and package dependency rules", () => DependencyValidatorService.ValidateDependencies());
        AddCommand(root, "validate-path-patterns", "Checks file path and directory conventions", () => PathPatternValidatorService.ValidatePathPatterns());
        AddCommand(root, "validate-readmes", "Checks that every app and package has a README", () => ReadmeValidatorService.ValidateReadmes());
        AddCommand(root, "validate-codeowners", "Checks the CODEOWNERS file", () => CodeownersValidatorService.ValidateCodeowners());
        AddCommand(root, "validate-workflow-security", "Checks GitHub Actions SHA pinning, checkout credentials and least-privilege permissions", () => WorkflowSecurityValidatorService.ValidateWorkflowSecurity());
        AddCommand(root, "validate-markdown", "Checks Markdown formatting and workspace links", () => MarkdownValidatorService.ValidateMarkdown());
        AddCommand(root, "query-linter", "Lints database and ORM query statements", () => QueryLinterService.LintQueries());
        AddCommand(root, "extract-env-vars", "Verifies bidirectional parity and file citations between code, .env.example and docs/ENVIRONMENT_VARIABLES.md", ToolCommands.ConfigureExtractEnvVars);
        AddCommand(root, "audit-security", "Scans for hardcoded secrets", () => SecurityAuditorService.AuditSecurity());
        AddCommand(root, "validate-public-bundle", "Verifies built VitePress and demo web bundles with zero comment exemptions", () => PublicBundleValidatorService.ValidatePublicBundles());
        AddCommand(root, "validate-vulnerabilities", "Audits dependency vulnerabilities against the exclusions ledger", () => VulnerabilityAuditorService.AuditVulnerabilities());
        AddCommand(root, "clean-secrets-baseline", "Normalizes or checks the detect-secrets baseline", ToolCommands.ConfigureCleanSecretsBaseline);
        AddCommand(root, "validate-architecture-drift", "Verifies compose, topology, database schema, electronic signature and audit-host parity across architecture docs", () => ArchitectureDriftValidatorService.ValidateArchitectureDrift());
        AddCommand(root, "sync-openapi-spec", "Exports each service's OpenAPI document into docs/openapi", ToolCommands.ConfigureSyncOpenApiSpec);
        AddCommand(root, "validate-openapi-drift", "Compares docs/openapi with the services", () => OpenApiDriftValidatorService.ValidateDrift());
        AddCommand(root, "generate-api-docs", "Renders docs/api from docs/openapi", ToolCommands.ConfigureGenerateApiDocs);
        AddCommand(root, "generate-schema-doc", "Renders docs/schema from the EF Core models", ToolCommands.ConfigureGenerateSchemaDoc);
        AddCommand(root, "pre-commit-openapi", "Runs the quick OpenAPI check used by the pre-commit hook", () => PreCommitOpenApiService.ExecutePreCommitOpenApi(quick: true));
        AddCommand(root, "validate-api-clients", "Typechecks the web API clients against their OpenAPI types", () => ApiClientValidatorService.ValidateApiClients());
        AddCommand(root, "generate-cli-docs", "Synthesizes generated doc regions in CLI, CI, dev-env, architecture, database and signature guides; --check verifies parity", ToolCommands.ConfigureGenerateCliDocs);
        AddCommand(root, "generate-doc-index", "Synchronizes docs/DOCUMENTATION_INDEX.md with all repository markdown documents", ToolCommands.ConfigureGenerateDocIndex);
        AddCommand(root, "generate-demo-manifest", "Exports the canonical synthetic demo study manifest into packages/demo-fixtures; --check verifies parity", ToolCommands.ConfigureGenerateDemoManifest);
        AddCommand(root, "generate-rtm", "Regenerates the Requirements Traceability Matrix", ToolCommands.ConfigureGenerateRtm);
        AddCommand(root, "compliance-utility", "Sanitizes document paths and requirement prefixes", ToolCommands.ConfigureComplianceUtility);
        AddCommand(root, "check-ports", "Reports which local service ports are in use", () => PortCheckerService.CheckPorts());
        AddCommand(root, "run-gating-pipeline", "Runs the full gating pipeline", () => GatingPipelineService.RunGatingPipeline());

        var gxp = AddBranch(root, "gxp", "GxP requirement traceability");
        AddCommand(gxp, "sync", "Runs the tests and validates requirement tags and results on a branch; writes a preview, never docs/SDLC", GxPSyncCommand.Configure);
        AddCommand(gxp, "snapshot", "Integration owner only: runs the tests and writes and stages the RTM and IQ/OQ/PQ snapshot", GxPSnapshotCommand.Configure);
        AddCommand(gxp, "validate", "Validates requirement tags and integrity stamps without running tests", Services.GxPComplianceService.ValidateCompliance);
        AddCommand(gxp, "diff", "Compares the regenerated RTM and IQ/OQ/PQ report with the committed copies, ignoring the evidence block",
            () => Services.GxpDocsDiffService.Compare(Services.QualityGateOrchestrator.FindRepoRoot()));

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

        return root;
    }

    /// <summary>Adds a command without options or arguments that runs <paramref name="execute"/>.</summary>
    private static void AddCommand(Command parent, string name, string description, Func<int> execute) =>
        AddCommand(parent, name, description, command => command.SetAction(_ => execute()));

    /// <summary>Adds a command whose options, arguments and action are set by <paramref name="configure"/>.</summary>
    private static void AddCommand(Command parent, string name, string description, Action<Command> configure)
    {
        var command = new Command(name, description);
        configure(command);
        parent.Subcommands.Add(command);
    }

    /// <summary>Adds a group of subcommands. Invoking the branch alone reports that a subcommand is required.</summary>
    private static Command AddBranch(Command parent, string name, string description)
    {
        var branch = new Command(name, description);
        parent.Subcommands.Add(branch);
        return branch;
    }
}
