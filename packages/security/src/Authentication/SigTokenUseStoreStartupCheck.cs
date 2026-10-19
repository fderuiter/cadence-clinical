using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Cadence.Security.Authentication;

/// <summary>
/// Refuses to start a host outside <c>Development</c> that maps an e-signature route but keeps token uses only in
/// process memory, so a restart or a second instance could accept a used token again (21 CFR 11.200(a), ADR-2515,
/// #5434). Endpoints are mapped before the host starts, so they are all visible here.
/// </summary>
public sealed class SigTokenUseStoreStartupCheck(
    IServiceProvider services,
    ISigTokenUseStore store,
    IHostEnvironment environment) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A host without routing maps no routes at all.
        if (environment.IsDevelopment() || store is IDurableSigTokenUseStore
            || services.GetService(typeof(EndpointDataSource)) is not EndpointDataSource endpoints)
        {
            return Task.CompletedTask;
        }

        var signed = endpoints.Endpoints
            .Where(e => e.Metadata.GetMetadata<ElectronicSignatureRequired>() is not null)
            .Select(e => (e as RouteEndpoint)?.RoutePattern.RawText ?? e.DisplayName)
            .ToList();
        if (signed.Count > 0)
        {
            throw new InvalidOperationException(
                $"This host maps e-signature routes ({string.Join(", ", signed)}) but registers {store.GetType().Name}, " +
                "which keeps token uses in process memory. Call AddCadenceDurableSigTokenStore<TContext>() (ADR-2515).");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
