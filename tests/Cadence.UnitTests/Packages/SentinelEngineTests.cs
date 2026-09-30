using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Sentinels;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Behavioural tests for the C# port of <c>packages/sentinels/engine.py</c>, deleted once these tests landed and kept in git history, using fake checks.
/// </summary>
public class SentinelEngineTests
{
    private sealed class FakeCheck(string name, Func<CancellationToken, Task<SentinelResult>> run) : ISentinelCheck
    {
        private int _runCount;

        public FakeCheck(string name, bool passed = true)
            : this(name, _ => Task.FromResult(new SentinelResult(name, passed)))
        {
        }

        public string Name => name;

        public string Description => $"Fake check {name}";

        public int RunCount => Volatile.Read(ref _runCount);

        public Task<SentinelResult> RunAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _runCount);
            return run(ct);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExecuteAll_WithNoRegisteredChecks_ReturnsEmpty()
    {
        var engine = new SentinelEngine();

        var results = await engine.ExecuteAllAsync(ct: Ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAll_RunsEveryRegisteredCheckOnce_AndReturnsResultsInRegistrationOrder()
    {
        var engine = new SentinelEngine();
        var checks = new[] { new FakeCheck("arch"), new FakeCheck("secrets"), new FakeCheck("docs") };
        foreach (var check in checks)
        {
            engine.Register(check);
        }

        var results = await engine.ExecuteAllAsync(ct: Ct);

        results.Select(r => r.Name).Should().Equal("arch", "secrets", "docs");
        checks.Should().OnlyContain(c => c.RunCount == 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ExecuteAll_WithoutTargetGate_RunsAllChecks(string? targetGate)
    {
        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("arch"));
        engine.Register(new FakeCheck("docs"));

        var results = await engine.ExecuteAllAsync(targetGate, Ct);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAll_StartsChecksConcurrently()
    {
        // Each check waits until both have started, so running them one after another would never finish.
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<SentinelResult> Rendezvous(string name, CancellationToken ct)
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult();
            }

            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            return new SentinelResult(name, Passed: true);
        }

        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("first", ct => Rendezvous("first", ct)));
        engine.Register(new FakeCheck("second", ct => Rendezvous("second", ct)));

        var results = await engine.ExecuteAllAsync(ct: Ct);

        results.Select(r => r.Name).Should().Equal("first", "second");
        results.Should().OnlyContain(r => r.Passed);
    }

    [Fact]
    public async Task ExecuteAll_WithTargetGate_RunsOnlyTheMatchingCheck()
    {
        var arch = new FakeCheck("arch");
        var docs = new FakeCheck("docs");
        var engine = new SentinelEngine();
        engine.Register(arch);
        engine.Register(docs);

        var results = await engine.ExecuteAllAsync("docs", Ct);

        results.Should().ContainSingle().Which.Name.Should().Be("docs");
        docs.RunCount.Should().Be(1);
        arch.RunCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAll_TargetGateMatchIgnoresCase()
    {
        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("arch-drift"));

        var results = await engine.ExecuteAllAsync("ARCH-Drift", Ct);

        results.Should().ContainSingle().Which.Name.Should().Be("arch-drift");
    }

    [Fact]
    public async Task ExecuteAll_WithUnknownTargetGate_ReturnsEmptyWithoutRunningChecks()
    {
        var arch = new FakeCheck("arch");
        var engine = new SentinelEngine();
        engine.Register(arch);

        var results = await engine.ExecuteAllAsync("no-such-gate", Ct);

        results.Should().BeEmpty();
        arch.RunCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAll_RunsEveryCheckRegisteredUnderTheTargetGateName()
    {
        var first = new FakeCheck("arch");
        var second = new FakeCheck("arch");
        var engine = new SentinelEngine();
        engine.Register(first);
        engine.Register(second);
        engine.Register(new FakeCheck("docs"));

        var results = await engine.ExecuteAllAsync("arch", Ct);

        results.Should().HaveCount(2).And.OnlyContain(r => r.Name == "arch");
        first.RunCount.Should().Be(1);
        second.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAll_ReportsFailedChecksAlongsidePassingOnes()
    {
        var failure = new SentinelResult(
            "secrets",
            Passed: false,
            ExitCode: 1,
            DurationSeconds: 0.25,
            Summary: "1 secret found",
            Violations: ["appsettings.json:3 hard-coded key"],
            Remediation: "Move the key to the environment.");
        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("arch", passed: true));
        engine.Register(new FakeCheck("secrets", _ => Task.FromResult(failure)));

        var results = await engine.ExecuteAllAsync(ct: Ct);

        results.Should().HaveCount(2);
        results[0].Passed.Should().BeTrue();
        results[1].Should().Be(failure);
        results[1].Violations.Should().Equal("appsettings.json:3 hard-coded key");
        results[1].Remediation.Should().Be("Move the key to the environment.");
    }

    [Fact]
    public async Task ExecuteAll_PropagatesAnExceptionThrownByACheck()
    {
        var healthy = new FakeCheck("arch");
        var engine = new SentinelEngine();
        engine.Register(healthy);
        engine.Register(new FakeCheck("broken", _ => Task.FromException<SentinelResult>(new InvalidOperationException("sentinel crashed"))));

        var act = () => engine.ExecuteAllAsync(ct: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("sentinel crashed");
        healthy.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAll_PropagatesASynchronousThrowFromACheck()
    {
        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("broken", _ => throw new InvalidOperationException("failed before returning a task")));

        var act = () => engine.ExecuteAllAsync(ct: Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("failed before returning a task");
    }

    [Fact]
    public async Task ExecuteAll_PassesTheCancellationTokenToEachCheck()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        var engine = new SentinelEngine();
        engine.Register(new FakeCheck("arch", ct =>
        {
            lock (seen)
            {
                seen.Add(ct);
            }

            return Task.FromResult(new SentinelResult("arch", Passed: true));
        }));

        await engine.ExecuteAllAsync(ct: cts.Token);

        seen.Should().ContainSingle().Which.Should().Be(cts.Token);
    }
}
