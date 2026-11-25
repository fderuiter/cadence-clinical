using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace Cadence.Kernel.Host;

/// <summary>Readiness: startup tenant bootstrap/seeding (when configured) has completed and the kernel database accepts a connection.</summary>
internal sealed class KernelDatabaseHealthCheck(
    string connectionString,
    DevelopmentTenantBootstrapReadiness? bootstrapReadiness = null) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (bootstrapReadiness is { IsCompleted: false })
        {
            return HealthCheckResult.Unhealthy("Development tenant bootstrap and demo seeding have not completed.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("The kernel database is unreachable.", exception);
        }
        catch (TimeoutException exception)
        {
            return HealthCheckResult.Unhealthy("The kernel database did not answer in time.", exception);
        }
    }
}
