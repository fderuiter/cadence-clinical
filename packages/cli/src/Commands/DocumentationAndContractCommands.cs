using System.CommandLine;

using Cadence.ToolingCore.Services;

using static Cadence.Cli.Commands.CommandRegistrationHelpers;

namespace Cadence.Cli.Commands;

/// <summary>Registers OpenAPI contract, database schema, CLI docs, doc index, and demo manifest commands.</summary>
internal static class DocumentationAndContractCommands
{
    public static void Register(RootCommand root)
    {
        AddCommand(root, "sync-openapi-spec", "Exports each service's OpenAPI document into docs/openapi", ToolCommands.ConfigureSyncOpenApiSpec);
        AddCommand(root, "validate-openapi-drift", "Compares docs/openapi with the services", () => OpenApiDriftValidatorService.ValidateDrift());
        AddCommand(root, "generate-api-docs", "Renders docs/api from docs/openapi", ToolCommands.ConfigureGenerateApiDocs);
        AddCommand(root, "generate-schema-doc", "Renders docs/schema from the EF Core models", ToolCommands.ConfigureGenerateSchemaDoc);
        AddCommand(root, "pre-commit-openapi", "Runs the quick OpenAPI check used by the pre-commit hook", () => PreCommitOpenApiService.ExecutePreCommitOpenApi());
        AddCommand(root, "validate-api-clients", "Typechecks the web API clients against their OpenAPI types", () => ApiClientValidatorService.ValidateApiClients());
        AddCommand(root, "generate-cli-docs", "Synthesizes generated doc regions in CLI, CI, dev-env, architecture, database and signature guides; --check verifies parity", ToolCommands.ConfigureGenerateCliDocs);
        AddCommand(root, "generate-doc-index", "Synchronizes docs/DOCUMENTATION_INDEX.md with all repository markdown documents", ToolCommands.ConfigureGenerateDocIndex);
        AddCommand(root, "generate-demo-manifest", "Exports the canonical synthetic demo study manifest into packages/demo-fixtures; --check verifies parity", ToolCommands.ConfigureGenerateDemoManifest);
    }
}
