using System;
using System.CommandLine;

using Cadence.Cli.Commands;
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
        AddCommand(root, "ci", "Runs the jobs of .github/workflows/ci.yml on this machine while GitHub Actions is off", LocalCiCommand.Configure);
        AddCommand(root, "dev", "Starts the .NET Aspire AppHost with all local services and datastores", DevCommand.Execute);
        AddCommand(root, "fix", "Executes self-healing auto-formatting and ADR validation pipeline", () => SelfHealService.ExecuteSelfHealing());

        AddCommand(root, "detect-duplication", "Finds duplicated C# and TypeScript code blocks", () => DuplicationDetectorService.DetectDuplication());
        AddCommand(root, "validate-imports", "Checks cross-service import boundaries", () => ImportValidatorService.ValidateImports());
        AddCommand(root, "validate-dependencies", "Checks project and package dependency rules", () => DependencyValidatorService.ValidateDependencies());
        AddCommand(root, "validate-path-patterns", "Checks file path and directory conventions", () => PathPatternValidatorService.ValidatePathPatterns());
        AddCommand(root, "validate-readmes", "Checks that every app and package has a README", () => ReadmeValidatorService.ValidateReadmes());
        AddCommand(root, "validate-codeowners", "Checks the CODEOWNERS file", () => CodeownersValidatorService.ValidateCodeowners());
        AddCommand(root, "validate-markdown", "Checks Markdown formatting and workspace links", () => MarkdownValidatorService.ValidateMarkdown());
        AddCommand(root, "query-linter", "Lints database and ORM query statements", () => QueryLinterService.LintQueries());
        AddCommand(root, "extract-env-vars", "Checks that environment variables read in code are documented", ToolCommands.ConfigureExtractEnvVars);
        AddCommand(root, "verify-contracts", "Verifies hexagonal port definitions and contracts", () => ContractVerifierService.VerifyContracts());
        AddCommand(root, "audit-security", "Scans for hardcoded secrets", () => SecurityAuditorService.AuditSecurity());
        AddCommand(root, "validate-vulnerabilities", "Audits dependency vulnerabilities against the exclusions ledger", () => VulnerabilityAuditorService.AuditVulnerabilities());
        AddCommand(root, "clean-secrets-baseline", "Normalizes the detect-secrets baseline", () => SecretsBaselineCleanerService.CleanSecretsBaseline());
        AddCommand(root, "validate-architecture-drift", "Checks that compose services are described in the architecture docs", () => ArchitectureDriftValidatorService.ValidateArchitectureDrift());
        AddCommand(root, "sync-openapi-spec", "Exports each service's OpenAPI document into docs/openapi", ToolCommands.ConfigureSyncOpenApiSpec);
        AddCommand(root, "validate-openapi-drift", "Compares docs/openapi with the services", () => OpenApiDriftValidatorService.ValidateDrift());
        AddCommand(root, "generate-api-docs", "Renders docs/api from docs/openapi", ToolCommands.ConfigureGenerateApiDocs);
        AddCommand(root, "generate-schema-doc", "Renders docs/schema from the EF Core models", ToolCommands.ConfigureGenerateSchemaDoc);
        AddCommand(root, "pre-commit-openapi", "Runs the quick OpenAPI check used by the pre-commit hook", () => PreCommitOpenApiService.ExecutePreCommitOpenApi());
        AddCommand(root, "validate-api-clients", "Typechecks the web API clients against their OpenAPI types", () => ApiClientValidatorService.ValidateApiClients());
        AddCommand(root, "generate-cli-docs", "Checks that docs/cli.md documents every command", ToolCommands.ConfigureGenerateCliDocs);
        AddCommand(root, "generate-doc-index", "Synchronizes docs/DOCUMENTATION_INDEX.md with all repository markdown documents", ToolCommands.ConfigureGenerateDocIndex);
        AddCommand(root, "generate-rtm", "Regenerates the Requirements Traceability Matrix", ToolCommands.ConfigureGenerateRtm);
        AddCommand(root, "compliance-utility", "Sanitizes document paths and requirement prefixes", ToolCommands.ConfigureComplianceUtility);
        AddCommand(root, "verify-gxp-signatures", "Not implemented yet: verifies electronic signatures on signed GxP reports", () => VerifyGxPSignaturesService.VerifySignatures());
        AddCommand(root, "clean-test-dbs", "Drops leftover integration test databases", () => CleanTestDbsService.CleanTestDatabases());
        AddCommand(root, "check-ports", "Reports which local service ports are in use", () => PortCheckerService.CheckPorts());
        AddCommand(root, "run-gating-pipeline", "Runs the full gating pipeline", () => GatingPipelineService.RunGatingPipeline());

        var gxp = AddBranch(root, "gxp", "GxP requirement traceability");
        AddCommand(gxp, "sync", "Executes test suites and regenerates RTM compliance docs", GxPSyncCommand.Configure);
        AddCommand(gxp, "validate", "Validates RTM compliance matrix against design requirements", Services.GxPComplianceService.ValidateCompliance);
        AddCommand(gxp, "diff", "Compares the regenerated RTM and IQ/OQ/PQ report with the committed copies, ignoring the evidence block",
            () => Services.GxpDocsDiffService.Compare(Services.QualityGateOrchestrator.FindRepoRoot()));
        AddCommand(gxp, "verify", "Not implemented yet: verifies electronic signatures on signed GxP reports", () => VerifyGxPSignaturesService.VerifySignatures());

        var db = AddBranch(root, "db", "Database management and migrations");
        AddCommand(db, "reset", "Resets the local PostgreSQL and SQLite datastores", DbCommands.Reset);
        AddCommand(db, "migrate", "Runs pre-boot EF Core database migrations", DbCommands.Migrate);
        AddCommand(db, "status", "Not implemented yet: checks status of local databases",
            () => NotImplemented("'db status' is not implemented yet. Use 'cadence doctor' to check datastore connectivity."));
        AddCommand(db, "seed", "Not implemented yet: populates databases with trial datasets",
            () => NotImplemented("'db seed' is not implemented yet; no data was seeded."));

        var settings = AddBranch(root, "settings", "Manage microservice configuration settings models and template exports");
        AddCommand(settings, "export", "Checks that environment variables read in code are listed in .env.example and docs", ToolCommands.ConfigureExtractEnvVars);

        var scaffold = AddBranch(root, "scaffold", "Architecture scaffolding tools");
        AddCommand(scaffold, "service", "Scaffolds a new C# Clean Architecture microservice", ScaffoldCommands.ConfigureService);
        AddCommand(scaffold, "adr", "Scaffolds a new Architectural Decision Record (ADR)", ScaffoldCommands.ConfigureAdr);

        var setup = AddBranch(root, "setup", "Environment and credentials setup");
        AddCommand(setup, "credentials", "Not implemented yet: configures local development credentials",
            () => NotImplemented("'setup credentials' is not implemented yet. Copy .env.example to .env and fill in the values described in docs/ENVIRONMENT_VARIABLES.md."));

        var record = AddBranch(root, "record", "E2E recording and media tools");
        AddCommand(record, "list", "Not implemented yet: lists recorded session videos",
            () => NotImplemented("'record list' is not implemented yet."));
        AddCommand(record, "run", "Not implemented yet: runs a Playwright E2E recording session",
            () => NotImplemented("'record run' is not implemented yet."));

        var cdisc = AddBranch(root, "cdisc", "CDISC / USDM data standards tools");
        AddCommand(cdisc, "export", "Not implemented yet: exports USDM / CDISC artifacts",
            () => NotImplemented("'cdisc export' is not implemented yet. The USDM JSON schemas in packages/usdm-schemas/src are maintained by hand until the kernel's USDM export lands."));

        var mcp = AddBranch(root, "mcp", "Model Context Protocol (MCP) AI integration server");
        AddCommand(mcp, "server", "Not implemented yet: starts an MCP stdio JSON-RPC server",
            () => NotImplemented("'mcp server' is not implemented yet."));

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

    private static int NotImplemented(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
        return 1;
    }
}
