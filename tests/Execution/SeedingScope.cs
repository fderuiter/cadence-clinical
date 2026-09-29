using System;

using Cadence.Apps.Execution.Application.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.UnitTests.Execution;

/// <summary>
/// Test seeding runs outside any request, so it has no caller to attribute writes to. These scopes declare a
/// named test service account, the same way production background work must.
/// </summary>
public static class SeedingScope
{
    public static readonly ServiceAccount TestSeeder = new("svc:test-seeder", "Test fixture seeding");

    /// <summary>Creates a DI scope whose audited writes are attributed to <see cref="TestSeeder"/>.</summary>
    public static IServiceScope CreateSeedingScope(this IServiceProvider services)
    {
        IServiceScope scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditActorScope>().RunAs(TestSeeder);
        return scope;
    }
}
