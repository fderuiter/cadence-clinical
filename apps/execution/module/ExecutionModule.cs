using System;

using Cadence.Database.Signatures;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Execution;

/// <summary>
/// Execution as a module of the kernel composition host (ADR-2601): eCRF forms over kernel form instances (ADR-2622) and the
/// data queries about their values, stated as kernel assertions (#5461), and form signatures, recorded as kernel
/// attestations (#5463).
/// The host calls <see cref="AddCadenceExecutionModule"/> after registering the kernel and maps the routes with
/// <see cref="MapExecutionModule"/>.
/// </summary>
public static class ExecutionModule
{
    /// <summary>
    /// Registers the module: its commands with the kernel's authorization, its command store, migrations and tenant-scoped
    /// reads in the <c>execution</c> schema, its handlers and validators, the consumer that opens an Execution form when the kernel opens one, the consumer
    /// that states each query step as a kernel assertion, and the durable single-use store for the step-up tokens its
    /// signature route spends (ADR-2515).
    /// </summary>
    public static IServiceCollection AddCadenceExecutionModule(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services
            .AddCadenceModule(ExecutionModuleDbContext.Module, typeof(OpenFormInstance).Assembly)
            .AddModuleCommandStore<ExecutionModuleDbContext>(
                ExecutionModuleDbContext.Module,
                options => new ExecutionModuleDbContext(options),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, ExecutionModuleDbContext.Schema))
            .AddModuleMigrations<ExecutionModuleDbContext>(
                ExecutionModuleDbContext.Module, ExecutionModuleDbContext.Schema, options => new ExecutionModuleDbContext(options), ExecutionModuleDatabase.GrantAppRoleSql)
            .AddModuleTenantContext<ExecutionModuleDbContext>(connectionString, options => new ExecutionModuleDbContext(options))
            .AddCommandHandler<OpenFormInstance, FormInstanceState, OpenFormInstanceHandler>()
            .AddCommandValidator<OpenFormInstance, OpenFormInstanceValidator>()
            .AddKernelEventConsumer<OpenFormOnCollectionOpened>(OpenFormOnCollectionOpened.Name)
            .AddCommandHandler<RaiseFormQuery, FormQueryState, RaiseFormQueryHandler>()
            .AddCommandValidator<RaiseFormQuery, RaiseFormQueryValidator>()
            .AddCommandHandler<AnswerFormQuery, FormQueryState, AnswerFormQueryHandler>()
            .AddCommandValidator<AnswerFormQuery, AnswerFormQueryValidator>()
            .AddCommandHandler<ReopenFormQuery, FormQueryState, ReopenFormQueryHandler>()
            .AddCommandValidator<ReopenFormQuery, ReopenFormQueryValidator>()
            .AddCommandHandler<CloseFormQuery, FormQueryState, CloseFormQueryHandler>()
            .AddCommandValidator<CloseFormQuery, CloseFormQueryValidator>()
            .AddCommandHandler<CancelFormQuery, FormQueryState, CancelFormQueryHandler>()
            .AddCommandValidator<CancelFormQuery, CancelFormQueryValidator>()
            .AddKernelEventConsumer<MirrorFormQueryToKernel>(MirrorFormQueryToKernel.Name)
            .AddKernelEventConsumer<EvaluateEditChecksOnCollectionChanged>(EvaluateEditChecksOnCollectionChanged.Name)
            .AddDbContext<ExecutionSignatureTokenDbContext>(options => options.UseNpgsql(connectionString))
            .AddCadenceDurableSigTokenStore<ExecutionSignatureTokenDbContext>();
    }

    /// <summary>Maps the module's routes under <c>/api/v1/execution/forms</c>.</summary>
    public static IEndpointRouteBuilder MapExecutionModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapFormEndpoints();
        endpoints.MapFormQueryEndpoints();
        endpoints.MapFormSignatureEndpoints();
        return endpoints;
    }
}
