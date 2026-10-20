using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Commands;

using FluentAssertions;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Holds the command sent through <see cref="HoldAsync{T}"/> when it reaches its commit (at
/// <see cref="CommitStage.StateStamped"/>, inside the advisory lock and before any row is written) until
/// <see cref="Release"/>. Every other commit passes through, so no unrelated command can take the pause. The held
/// command runs on the thread pool, so its blocked thread is never one the test runner needs to resume the test, and
/// it stays held until the test releases it: a pause that ran out first would let it commit early and make the
/// "still waiting" assertions meaningless, so <see cref="Release"/> fails the test instead.
/// </summary>
internal sealed class KernelCommitPause : IDisposable
{
    private readonly AsyncLocal<bool> _target = new();
    private readonly KernelPostgresFixture _fixture;
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new();
    private static readonly TimeSpan HoldLimit = TimeSpan.FromMinutes(5);

    private int _held;
    private volatile bool _expired;

    public KernelCommitPause(KernelPostgresFixture fixture)
    {
        _fixture = fixture;
        fixture.UnitOfWork.FaultInjector = stage =>
        {
            if (stage == CommitStage.StateStamped && _target.Value && Interlocked.Exchange(ref _held, 1) == 0)
            {
                _reached.TrySetResult();
                if (!_release.Wait(HoldLimit))
                {
                    _expired = true;
                }
            }
        };
    }

    public Task Reached => _reached.Task;

    /// <summary>Sends the command to hold; the flag flows only into this send's execution context.</summary>
    public async Task<CommandResult<T>> HoldAsync<T>(Func<Task<CommandResult<T>>> send)
    {
        _target.Value = true;
        return await Task.Run(send);
    }

    public void Release()
    {
        _expired.Should().BeFalse("the held command must still be held when the test releases it");
        _release.Set();
    }

    public void Dispose()
    {
        _release.Set();
        _fixture.UnitOfWork.FaultInjector = null;
        _release.Dispose();
    }
}
