using System.CommandLine;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Commands;

/// <summary>
/// Unified EF Core migration commands (<c>cadence migrations add|script|list|verify|apply|bundle|optimize|seed</c>) across all 18 persistence modules.
/// </summary>
public static class MigrationCommands
{
    public static void ConfigureAdd(Command command)
    {
        var moduleArg = new Argument<string>("module")
        {
            Description = $"Target persistence module ({PersistenceModuleCatalog.ModuleList})",
        };
        var nameArg = new Argument<string>("name")
        {
            Description = "PascalCase EF Core migration name (for example AddParticipantConsentTimestamp)",
        };
        moduleArg.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        nameArg.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(moduleArg);
        command.Arguments.Add(nameArg);
        command.SetAction(result => MigrationToolingService.Add(
            result.GetValue(moduleArg)!,
            result.GetValue(nameArg)!));
    }

    public static void ConfigureScript(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        var outputOption = new Option<string?>("--output-dir", "-o")
        {
            Description = $"Output directory for generated idempotent SQL files (default: {MigrationToolingService.DefaultScriptRelativeDir})",
        };
        outputOption.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(moduleArg);
        command.Options.Add(outputOption);
        command.SetAction(result => MigrationToolingService.Script(
            moduleOrAll: result.GetValue(moduleArg),
            outputDir: result.GetValue(outputOption)));
    }

    public static void ConfigureList(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        command.Arguments.Add(moduleArg);
        command.SetAction(result => MigrationToolingService.List(result.GetValue(moduleArg)));
    }

    public static void ConfigureVerify(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        command.Arguments.Add(moduleArg);
        command.SetAction(result => MigrationToolingService.Verify(result.GetValue(moduleArg)));
    }

    public static void ConfigureApply(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        var connectionOption = new Option<string?>("--connection", "-c")
        {
            Description = "Explicit PostgreSQL connection string (overrides ConnectionStrings__cadence and POSTGRES_CONNECTION_STRING)",
        };
        connectionOption.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(moduleArg);
        command.Options.Add(connectionOption);
        command.SetAction(result => MigrationToolingService.Apply(
            moduleOrAll: result.GetValue(moduleArg),
            connectionString: result.GetValue(connectionOption)));
    }

    public static void ConfigureBundle(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        var outputOption = new Option<string?>("--output-dir", "-o")
        {
            Description = $"Output directory for self-contained migration bundles (default: {MigrationToolingService.DefaultScriptRelativeDir})",
        };
        outputOption.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(moduleArg);
        command.Options.Add(outputOption);
        command.SetAction(result => MigrationToolingService.Bundle(
            moduleOrAll: result.GetValue(moduleArg),
            outputDir: result.GetValue(outputOption)));
    }

    public static void ConfigureOptimize(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        var outputOption = new Option<string?>("--output-dir", "-o")
        {
            Description = $"Project-relative output directory for compiled EF Core models (default: {MigrationToolingService.DefaultCompiledModelsRelativeDir})",
        };
        outputOption.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        command.Arguments.Add(moduleArg);
        command.Options.Add(outputOption);
        command.SetAction(result => MigrationToolingService.Optimize(
            moduleOrAll: result.GetValue(moduleArg),
            outputDir: result.GetValue(outputOption)));
    }

    public static void ConfigureSeed(Command command)
    {
        var moduleArg = CreateModuleOrAllArgument();
        command.Arguments.Add(moduleArg);
        command.SetAction(result => MigrationToolingService.Seed(result.GetValue(moduleArg)));
    }

    private static Argument<string?> CreateModuleOrAllArgument()
    {
        var moduleArg = new Argument<string?>("module")
        {
            Description = "Target persistence module or 'all' (defaults to 'all')",
            Arity = ArgumentArity.ZeroOrOne,
        };
        moduleArg.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        return moduleArg;
    }
}
