using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Fileshare.Workers;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Fileshare;

public sealed class FileshareWorkerBaseTests
{
    private sealed class TestFileshareWorker : FileshareWorkerBase
    {
        private readonly Func<CancellationToken, Task>? _onProcess;

        public int ProcessCount { get; private set; }

        public TestFileshareWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<FileshareWorkerOptions> options,
            ILogger logger,
            Func<CancellationToken, Task>? onProcess = null)
            : base(scopeFactory, options, logger)
        {
            _onProcess = onProcess;
        }

        protected override TimeSpan PollInterval => TimeSpan.FromMilliseconds(10);

        protected override async Task ProcessPendingAsync(CancellationToken stoppingToken)
        {
            ProcessCount++;
            if (_onProcess is not null)
            {
                await _onProcess(stoppingToken).ConfigureAwait(false);
            }
        }

        public Task RunThrottledAsync(Func<Task> action, CancellationToken ct)
            => ExecuteThrottledAsync(action, ct);

        public Task StartWorkerAsync(CancellationToken ct)
            => ExecuteAsync(ct);
    }

    [Fact]
    public void CalculateBackoffDelay_CalculatesExponentialValuesCorrectly()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new FileshareWorkerOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(1)
        });

        var worker = new TestFileshareWorker(scopeFactory, options, NullLogger<TestFileshareWorker>.Instance);

        worker.CalculateBackoffDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        worker.CalculateBackoffDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        worker.CalculateBackoffDelay(3).Should().Be(TimeSpan.FromSeconds(4));
        worker.CalculateBackoffDelay(4).Should().Be(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void CalculateBackoffDelay_WithLargeAttempt_DoesNotOverflow()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new FileshareWorkerOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(2)
        });

        var worker = new TestFileshareWorker(scopeFactory, options, NullLogger<TestFileshareWorker>.Instance);

        var largeAttemptDelay = worker.CalculateBackoffDelay(100);
        largeAttemptDelay.Should().BeGreaterThan(TimeSpan.Zero);
        largeAttemptDelay.TotalMilliseconds.Should().BeLessThanOrEqualTo(int.MaxValue);

        var extremeAttemptDelay = worker.CalculateBackoffDelay(int.MaxValue);
        extremeAttemptDelay.Should().BeGreaterThan(TimeSpan.Zero);
        extremeAttemptDelay.TotalMilliseconds.Should().BeLessThanOrEqualTo(int.MaxValue);
    }

    [Fact]
    public async Task ExecuteThrottledAsync_RespectsMaxConcurrentJobs()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new FileshareWorkerOptions
        {
            MaxConcurrentJobs = 2
        });

        var worker = new TestFileshareWorker(scopeFactory, options, NullLogger<TestFileshareWorker>.Instance);

        int currentConcurrency = 0;
        int maxObservedConcurrency = 0;
        var gate = new SemaphoreSlim(0, 10);

        var tasks = new Task[6];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = worker.RunThrottledAsync(async () =>
            {
                int current = Interlocked.Increment(ref currentConcurrency);
                lock (gate)
                {
                    if (current > maxObservedConcurrency)
                    {
                        maxObservedConcurrency = current;
                    }
                }

                await Task.Delay(20, TestContext.Current.CancellationToken).ConfigureAwait(false);
                Interlocked.Decrement(ref currentConcurrency);
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);
        maxObservedConcurrency.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task Delay_WhenCancelled_CompletesCleanlyWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Func<Task> act = async () =>
        {
            await FileshareWorkerBase.Delay(TimeSpan.FromSeconds(10), cts.Token);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_StopsCleanlyWhenCancellationRequested()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        var options = Options.Create(new FileshareWorkerOptions
        {
            ConversionPollInterval = TimeSpan.FromMilliseconds(5)
        });

        using var cts = new CancellationTokenSource();

        var worker = new TestFileshareWorker(scopeFactory, options, NullLogger<TestFileshareWorker>.Instance, async ct =>
        {
            await Task.Yield();
            await cts.CancelAsync();
        });

        await worker.StartWorkerAsync(cts.Token);
        worker.ProcessCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
