using System;

namespace Cadence.TestSupport.Hosting;

/// <summary>
/// Configures <c>Microsoft.Extensions.Hosting.HostFactoryResolver</c>'s wait window for parallel xUnit runs.
/// </summary>
public static class TestHostFactoryTimeout
{
    /// <summary>
    /// Raises <c>HOST_FACTORY_RESOLVER_DEFAULT_TIMEOUT_IN_SECONDS</c> from its 5-second default when unset so parallel
    /// <c>WebApplicationFactory</c> runs do not stop and dispose <c>IServiceProvider</c> mid-request under CPU load.
    /// </summary>
    public static void EnsureDefaultTimeout()
    {
        const string variable = "HOST_FACTORY_RESOLVER_DEFAULT_TIMEOUT_IN_SECONDS";
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
        {
            Environment.SetEnvironmentVariable(variable, "60");
        }
    }
}
