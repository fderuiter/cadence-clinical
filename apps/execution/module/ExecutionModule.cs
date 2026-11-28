using System;

using Cadence.Database.Signatures;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Modules.Execution;

/// <summary>
/// Execution as a module of the kernel composition host (ADR-2601): eCRF forms over kernel form instances (ADR-2622) and the
/// bitemporal data queries about their values, stated as kernel assertions (#5461, <c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>),
/// and form signatures, recorded as kernel attestations (#5463).
/// The host calls <see cref="AddCadenceExecutionModule"/> after registering the kernel and maps the routes with
/// <see cref="MapExecutionModule"/>.
/// </summary>
public static class ExecutionModule
{
    /// <summary>
    /// The named OpenAPI document for the Execution module's eCRF routes (<c>/openapi/execution_forms.json</c>),
    /// distinct from standalone <c>execution</c> while <c>apps/execution/src</c> remains deployed alongside the host.
    /// </summary>
    public const string OpenApiDocument = "execution_forms";

    /// <summary>
    /// Registers the module: its commands with the kernel's authorization, its command store, migrations and tenant-scoped
    /// reads in the <c>execution</c> schema, its handlers and validators, the consumer that opens an Execution form when the
    /// kernel opens one, the consumer that states each query step as a kernel assertion, the edit-check and auto-answer
    /// consumer, the leader-locked 14-day SLA scheduler and de-identified digest alert consumer, the CRA SDV modification
    /// alert consumer, and the durable single-use store for step-up signature tokens (ADR-2515).
    /// </summary>
    public static IServiceCollection AddCadenceExecutionModule(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services
            .AddCadenceModule(ExecutionModuleDbContext.Module, typeof(OpenFormInstance).Assembly, OpenApiDocument)
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
            .AddCommandHandler<AutoAnswerFormQuery, FormQueryState, AutoAnswerFormQueryHandler>()
            .AddCommandValidator<AutoAnswerFormQuery, AutoAnswerFormQueryValidator>()
            .AddCommandHandler<ReopenFormQuery, FormQueryState, ReopenFormQueryHandler>()
            .AddCommandValidator<ReopenFormQuery, ReopenFormQueryValidator>()
            .AddCommandHandler<ReassignFormQuery, FormQueryState, ReassignFormQueryHandler>()
            .AddCommandValidator<ReassignFormQuery, ReassignFormQueryValidator>()
            .AddCommandHandler<CloseFormQuery, FormQueryState, CloseFormQueryHandler>()
            .AddCommandValidator<CloseFormQuery, CloseFormQueryValidator>()
            .AddCommandHandler<CancelFormQuery, FormQueryState, CancelFormQueryHandler>()
            .AddCommandValidator<CancelFormQuery, CancelFormQueryValidator>()
            .AddCommandHandler<EscalateFormQuery, FormQueryState, EscalateFormQueryHandler>()
            .AddCommandValidator<EscalateFormQuery, EscalateFormQueryValidator>()
            .AddCommandHandler<GenerateFormQueryAgingDigest, FormQueryAgingDigestState, GenerateFormQueryAgingDigestHandler>()
            .AddCommandValidator<GenerateFormQueryAgingDigest, GenerateFormQueryAgingDigestValidator>()
            .AddCommandHandler<RecordFormSdvAlert, FormSdvAlertState, RecordFormSdvAlertHandler>()
            .AddCommandValidator<RecordFormSdvAlert, RecordFormSdvAlertValidator>()
            .AddScoped<IFormQueryEscalationService, FormQueryEscalationService>()
            .AddScoped<IFormQueryKernelProjector, FormQueryKernelProjector>()
            .AddScoped<FormSdvAlertQueryService>()
            .AddScoped<IFormSdvAlertQueryService>(sp => sp.GetRequiredService<FormSdvAlertQueryService>())
            .AddScoped<IStudySdvAlertReader>(sp => sp.GetRequiredService<FormSdvAlertQueryService>())
            .AddKernelEventConsumer<MirrorFormQueryToKernel>(MirrorFormQueryToKernel.Name)
            .AddKernelEventConsumer<EvaluateEditChecksOnCollectionChanged>(EvaluateEditChecksOnCollectionChanged.Name)
            .AddKernelEventConsumer<SendFormQueryAgingDigestAlerts>(SendFormQueryAgingDigestAlerts.Name)
            .AddKernelEventConsumer<AlertMonitorOnVerifiedObservationCorrected>(AlertMonitorOnVerifiedObservationCorrected.Name)
            .AddDbContext<ExecutionSignatureTokenDbContext>(options => options.UseNpgsql(connectionString))
            .AddCadenceDurableSigTokenStore<ExecutionSignatureTokenDbContext>();

        services.TryAddSingleton<IFormQueryAgingDigestAlertDispatcher, InMemoryFormQueryAgingDigestAlertDispatcher>();
        services.TryAddSingleton<FormQueryEscalationOptions>();
        services.TryAddSingleton<FormQueryEscalationHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<FormQueryEscalationHostedService>());
        return services;
    }

    /// <summary>Maps the module's routes under <c>/api/v1/execution/forms</c> in the <c>execution_forms</c> OpenAPI group.</summary>
    public static IEndpointRouteBuilder MapExecutionModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(string.Empty).WithGroupName(OpenApiDocument);
        group.MapFormEndpoints();
        group.MapFormQueryEndpoints();
        group.MapFormSdvAlertEndpoints();
        group.MapFormSignatureEndpoints();
        return endpoints;
    }
}
