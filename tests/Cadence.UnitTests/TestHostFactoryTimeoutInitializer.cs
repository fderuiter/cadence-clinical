using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using Cadence.TestSupport.Hosting;

namespace Cadence.UnitTests;

internal static class TestHostFactoryTimeoutInitializer
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries", Justification = "Test assembly module initializer configures HostFactoryResolver timeout before xUnit runs parallel WebApplicationFactory hosts.")]
    internal static void Initialize() => TestHostFactoryTimeout.EnsureDefaultTimeout();
}
