using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.E2ETests.Journeys;

/// <summary>
/// Carries a kernel command into the single host for a journey step whose command exists in the kernel but whose HTTP
/// route the host does not map yet (enrollment, queries, SDV, locks, withdrawal and signatures; each step names the
/// issue that adds its route). Forms already have theirs (the Execution module's eCRF routes, #5576). The request is signed by the gateway test identity like any other, so the host's own
/// authentication verifies the caller, its tenant directory resolves the kernel tenant from the signed tenant code, the
/// reason for change comes from the signed <c>X-Change-Reason</c> header and the idempotency key from its header, exactly
/// as the observation routes take them. The command then runs through the host's command pipeline, authorized by the
/// host's authority evaluator against the caller's grants. Nothing in the kernel is replaced: only the missing route is.
/// </summary>
internal sealed class KernelHostCommandRelay
{
    /// <summary>Where the relay listens in the host; no host route starts with it.</summary>
    public const string PathPrefix = "/e2e/kernel-commands/";

    private readonly ConcurrentDictionary<Guid, Func<RelayedWrite, Task<(object Result, KernelError? Error)>>> _pending = new();
    private readonly ConcurrentDictionary<Guid, object> _results = new();

    /// <summary>Registers a write to run as the next signed caller of <c>PathPrefix + id</c>; returns the id.</summary>
    public Guid Register<TResult>(Func<RelayedWrite, ICommand<TResult>> build)
    {
        var id = Guid.NewGuid();
        _pending[id] = async write =>
        {
            var result = await write.Sender.SendAsync(build(write), cancellationToken: write.Cancellation).ConfigureAwait(false);
            return (result, result.Error);
        };
        return id;
    }

    /// <summary>The result of the write <paramref name="id"/> ran, once the host has answered its request.</summary>
    public CommandResult<TResult> Take<TResult>(Guid id) =>
        _results.TryRemove(id, out var result)
            ? (CommandResult<TResult>)result
            : throw new InvalidOperationException($"The host ran no write {id}: the request never reached the relay.");

    /// <summary>Handles one relayed request inside the host, after its authentication middleware has run.</summary>
    public async Task HandleAsync(HttpContext context, Guid id)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var services = context.RequestServices;
        var tenant = await services.GetRequiredService<IKernelTenantResolver>().ResolveAsync(context.User, context.RequestAborted).ConfigureAwait(false);
        if (tenant.Tenant is not { } resolved)
        {
            context.Response.StatusCode = tenant.FailureStatus;
            return;
        }

        if (!_pending.TryRemove(id, out var write))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var (result, error) = await write(new RelayedWrite(
            resolved,
            context.Request.Headers[KernelObservationEndpoints.IdempotencyKeyHeader].ToString(),
            services.GetRequiredService<IKernelChangeReason>().Read(context) ?? string.Empty,
            services.GetRequiredService<IKernelCommandSender>(),
            context.RequestAborted)).ConfigureAwait(false);
        _results[id] = result;
        context.Response.StatusCode = error?.HttpStatus ?? StatusCodes.Status200OK;
    }

    /// <summary>Adds the relay at the end of the host's pipeline, where only requests no host route matched arrive.</summary>
    public sealed class StartupFilter(KernelHostCommandRelay relay) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (HttpContext context, RequestDelegate _) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (HttpMethods.IsPost(context.Request.Method)
                    && path.StartsWith(PathPrefix, StringComparison.Ordinal)
                    && Guid.TryParse(path[PathPrefix.Length..], out var id))
                {
                    await relay.HandleAsync(context, id).ConfigureAwait(false);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        };
    }
}

/// <summary>What a relayed write gets from its signed request: the resolved tenant, idempotency key and reason.</summary>
public sealed record RelayedWrite(
    TenantId Tenant,
    string IdempotencyKey,
    string ReasonForChange,
    IKernelCommandSender Sender,
    CancellationToken Cancellation);
