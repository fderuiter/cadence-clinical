using Cadence.Kernel.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The kernel's command pipeline and outbox relay open their transactions directly, which EF Core allows only while
/// the kernel context has no retrying execution strategy (#5383). Adding one must come with moving those
/// transactions inside the strategy; this test fails first.
/// </summary>
public sealed class KernelExecutionStrategyTests
{
    [Fact]
    public void KernelContexts_RegisterNoRetryingExecutionStrategy()
    {
        var options = new DbContextOptionsBuilder<KernelDbContext>();
        KernelDatabase.UseKernelNpgsql(options, "Host=localhost;Database=kernel_strategy_check");

        using var db = new KernelDbContext(options.Options);

        db.Database.CreateExecutionStrategy().RetriesOnFailure.Should().BeFalse();
    }
}
