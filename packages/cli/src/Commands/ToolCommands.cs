using System.ComponentModel;

using Cadence.Compliance;
using Cadence.ToolingCore.Services;

using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class DetectDuplicationCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return DuplicationDetectorService.DetectDuplication();
    }
}

public class ValidateImportsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return ImportValidatorService.ValidateImports();
    }
}

public class ValidateDependenciesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return DependencyValidatorService.ValidateDependencies();
    }
}

public class ValidatePathPatternsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return PathPatternValidatorService.ValidatePathPatterns();
    }
}

public class ValidateReadmesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return ReadmeValidatorService.ValidateReadmes();
    }
}

public class ValidateCodeownersCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return CodeownersValidatorService.ValidateCodeowners();
    }
}

public class ValidateMarkdownCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return MarkdownValidatorService.ValidateMarkdown();
    }
}

public class QueryLinterCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return QueryLinterService.LintQueries();
    }
}

public class ExtractEnvVarsCommand : Command<ExtractEnvVarsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--check")]
        [Description("Check environment variables without modifying documentation")]
        public bool Check { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: settings.Check);
    }
}

public class VerifyContractsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return ContractVerifierService.VerifyContracts();
    }
}

public class AuditSecurityCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return SecurityAuditorService.AuditSecurity();
    }
}

public class ValidateVulnerabilitiesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return VulnerabilityAuditorService.AuditVulnerabilities();
    }
}

public class CleanSecretsBaselineCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return SecretsBaselineCleanerService.CleanSecretsBaseline();
    }
}

public class ValidateArchitectureDriftCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return ArchitectureDriftValidatorService.ValidateArchitectureDrift();
    }
}

public class SyncOpenApiSpecCommand : Command<SyncOpenApiSpecCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--validate")]
        public bool ValidateOnly { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return OpenApiSyncService.SyncOpenApiSpec(validate: settings.ValidateOnly);
    }
}

public class ValidateOpenApiDriftCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return OpenApiDriftValidatorService.ValidateDrift();
    }
}

public class GenerateApiDocsCommand : Command<GenerateApiDocsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--validate")]
        public bool ValidateOnly { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ApiDocsGeneratorService.GenerateApiDocs(validate: settings.ValidateOnly);
    }
}

public class GenerateSchemaDocCommand : Command<GenerateSchemaDocCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--validate")]
        [Description("Only report whether docs/schema matches the EF Core models; exit 1 when it does not")]
        public bool ValidateOnly { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: settings.ValidateOnly);
    }
}

public class PreCommitOpenApiCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return PreCommitOpenApiService.ExecutePreCommitOpenApi();
    }
}

public class ValidateApiClientsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return ApiClientValidatorService.ValidateApiClients();
    }
}

public class GenerateCliDocsCommand : Command<GenerateCliDocsCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--check")]
        public bool Check { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return CliDocsGeneratorService.GenerateCliDocs(check: settings.Check);
    }
}

public class GenerateRtmCommand : Command<GenerateRtmCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--validate")]
        public bool ValidateOnly { get; set; }

        [CommandOption("--draft")]
        public bool Draft { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return RtmGeneratorService.GenerateRtm(draft: settings.Draft, validateOnly: settings.ValidateOnly);
    }
}

public class ComplianceUtilityCommand : Command<ComplianceUtilityCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("--sanitize")]
        public bool Sanitize { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return ComplianceUtilityService.ExecuteComplianceUtility(sanitize: settings.Sanitize);
    }
}

public class VerifyGxPSignaturesCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return VerifyGxPSignaturesService.VerifySignatures();
    }
}

public class CleanTestDbsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return CleanTestDbsService.CleanTestDatabases();
    }
}

public class CheckPortsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return PortCheckerService.CheckPorts();
    }
}

public class RunGatingPipelineCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return GatingPipelineService.RunGatingPipeline();
    }
}
