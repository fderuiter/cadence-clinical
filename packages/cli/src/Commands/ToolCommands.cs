using System.CommandLine;

using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Commands;

/// <summary>Options for the tool commands that take any; the rest are registered directly in <c>Program.cs</c>.</summary>
public static class ToolCommands
{
    public static void ConfigureFix(Command command)
    {
        var full = new Option<bool>("--full")
        {
            Description = "Force full regeneration of EF Core schema docs, all OpenAPI specs, API docs, and web client types",
        };
        var openApi = new Option<bool>("--openapi")
        {
            Description = "Force OpenAPI specification export, API docs generation, and web client type generation",
        };
        command.Options.Add(full);
        command.Options.Add(openApi);
        command.SetAction(result => SelfHealService.ExecuteSelfHealing(
            full: result.GetValue(full),
            openApi: result.GetValue(openApi),
            syncCliDocs: GateDocs.Write));
    }

    public static void ConfigureCleanSecretsBaseline(Command command)
    {
        var check = new Option<bool>("--check")
        {
            Description = "Only check that .secrets.baseline is normalized without modifying it; exit 1 when it is not",
        };
        command.Options.Add(check);
        command.SetAction(result => SecretsBaselineCleanerService.CleanSecretsBaseline(check: result.GetValue(check)));
    }

    public static void ConfigureExtractEnvVars(Command command)
    {
        var check = new Option<bool>("--check") { Description = "Check environment variables without modifying documentation" };
        command.Options.Add(check);
        command.SetAction(result => EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: result.GetValue(check)));
    }

    public static void ConfigureSyncOpenApiSpec(Command command)
    {
        var validate = new Option<bool>("--validate") { Description = "Only report whether docs/openapi matches the services; exit 1 when it does not" };
        command.Options.Add(validate);
        command.SetAction(result => OpenApiSyncService.SyncOpenApiSpec(validate: result.GetValue(validate)));
    }

    public static void ConfigureGenerateApiDocs(Command command)
    {
        var validate = new Option<bool>("--validate") { Description = "Only report whether docs/api matches docs/openapi; exit 1 when it does not" };
        command.Options.Add(validate);
        command.SetAction(result => ApiDocsGeneratorService.GenerateApiDocs(validate: result.GetValue(validate)));
    }

    public static void ConfigureGenerateSchemaDoc(Command command)
    {
        var validate = new Option<bool>("--validate") { Description = "Only report whether docs/schema matches the EF Core models; exit 1 when it does not" };
        command.Options.Add(validate);
        command.SetAction(result => SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: result.GetValue(validate)));
    }

    public static void ConfigureGenerateCliDocs(Command command)
    {
        var check = new Option<bool>("--check")
        {
            Description = "Only check that docs/cli.md documents every command and that the gate tables match the registry",
        };
        command.Options.Add(check);
        command.SetAction(result =>
        {
            string repoRoot = QualityGateOrchestrator.FindRepoRoot();
            if (result.GetValue(check))
            {
                return QualityGates.Find("cli-docs")!.Run(new GateContext(repoRoot)) ? 0 : 1;
            }

            return GateDocs.Write(repoRoot) | CliDocsGeneratorService.GenerateCliDocs(check: false, repoRoot: repoRoot);
        });
    }

    public static void ConfigureGenerateDocIndex(Command command)
    {
        var check = new Option<bool>("--check") { Description = "Only check that docs/DOCUMENTATION_INDEX.md is complete without modifying it" };
        command.Options.Add(check);
        command.SetAction(result => DocIndexSyncService.SyncDocIndex(check: result.GetValue(check)));
    }

    public static void ConfigureGenerateDemoManifest(Command command)
    {
        var check = new Option<bool>("--check")
        {
            Description = "Only check that packages/demo-fixtures matches the backend demo seed without modifying files",
        };
        command.Options.Add(check);
        command.SetAction(result => DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: result.GetValue(check)));
    }

    public static void ConfigureGenerateRtm(Command command)
    {
        var validate = new Option<bool>("--validate") { Description = "Write nothing; fail if a mapped test failed" };
        var draft = new Option<bool>("--draft") { Description = "Mark tests as unverified when the test report is missing" };
        command.Options.Add(validate);
        command.Options.Add(draft);
        command.SetAction(result => RtmGeneratorService.GenerateRtm(
            draft: result.GetValue(draft), validateOnly: result.GetValue(validate), requireCurrentSnapshot: GxpSnapshotPolicy.IsSnapshotMode()));
    }

    public static void ConfigureComplianceUtility(Command command)
    {
        var sanitize = new Option<bool>("--sanitize") { Description = "Upper-case @req:PRD-... tags in docs/**/*.md" };
        command.Options.Add(sanitize);
        command.SetAction(result => ComplianceUtilityService.ExecuteComplianceUtility(sanitize: result.GetValue(sanitize)));
    }
}
