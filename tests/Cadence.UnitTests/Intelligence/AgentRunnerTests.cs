using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// The agent runtime (ADR-2630 §4.2): what it refuses before calling a model, what it records in the ledger, and that a
/// failing or slow model fails the run without escaping as an error.
/// </summary>
public sealed class AgentRunnerTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f6a3b1e-1111-4c2d-9e8f-000000000001"));

    private static readonly TaskConfiguration Configuration = new(
        "data_review.query_draft", 3, FakeChatClient.ProviderName, "tier:deep", "Review {{form}} and draft queries.", 2000, AutonomyLevel.ApproveToExecute);

    private readonly FakeClock _clock = new();
    private readonly RecordingSender _sender = new();
    private readonly FakeLedger _ledger = new();

    /// <summary>A run records its start and completion as kernel commands and returns the fake model's answer.</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_RecordsStartAndCompletion_WithConfigurationVersionAndUsage()
    {
        var runner = Runner(new FakeChatClient((_, _) => "Two queries drafted."));

        var result = await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.Status.Should().Be(AgentRunStatus.Succeeded);
        result.Value.Text.Should().Be("Two queries drafted.");
        result.Value.Model.Should().Be("fake-deep", "the tier resolves to the provider's model");
        var start = _sender.Sent.OfType<StartAgentRun>().Single();
        start.Configuration.Should().Be(Configuration);
        start.Model.Should().Be("fake-deep");
        start.RequestJson.Should().Contain("Review demographics and draft queries.", "the instructions template is rendered");
        start.ReasonForChange.Should().Be("Agent run of data_review.query_draft v3");
        var complete = _sender.Sent.OfType<CompleteAgentRun>().Single();
        complete.RunId.Should().Be(start.RunId);
        complete.Succeeded.Should().BeTrue();
        complete.InputTokens.Should().BePositive();
        complete.OutputTokens.Should().Be(FakeChatClient.Tokens("Two queries drafted."));
        complete.CostUsd.Should().Be(new ModelPrice { InputPerMillion = 5m, OutputPerMillion = 25m }.Of(complete.InputTokens, complete.OutputTokens));
    }

    /// <summary>A switched-off scope, tenant-wide or for the task, refuses the run before any model call or ledger write.</summary>
    [Theory]
    [InlineData(AgentControl.AllTasks)]
    [InlineData("data_review.query_draft")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_WhenKillSwitchIsOff_RefusesWithoutCallingTheModel(string scope)
    {
        var model = new FakeChatClient();
        _ledger.Controls.Add(Control(scope, enabled: false, limit: null));

        var result = await Runner(model).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error.Should().Be(IntelligenceErrors.AgentDisabled);
        model.Calls.Should().Be(0);
        _sender.Sent.Should().BeEmpty();
    }

    /// <summary>A kill switch on another task does not stop this one.</summary>
    [Fact]
    public async Task RunAsync_KillSwitchOnAnotherTask_DoesNotApply()
    {
        _ledger.Controls.Add(Control("medical_coding.suggest", enabled: false, limit: null));

        var result = await Runner(new FakeChatClient()).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>A spent monthly budget refuses the run; usage counts from the first of the month (UTC).</summary>
    [Theory]
    [InlineData(AgentControl.AllTasks, null)]
    [InlineData("data_review.query_draft", "data_review.query_draft")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_WhenBudgetIsSpent_Refuses(string scope, string? countedTask)
    {
        var model = new FakeChatClient();
        _clock.Set(new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero));
        _ledger.Controls.Add(Control(scope, enabled: true, limit: 1000));
        _ledger.TokensUsed = 1000;

        var result = await Runner(model).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error.Should().Be(IntelligenceErrors.BudgetExhausted);
        model.Calls.Should().Be(0);
        _ledger.TokenQueries.Should().ContainSingle().Which.Should().Be((countedTask, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>Under the budget, the run goes ahead.</summary>
    [Fact]
    public async Task RunAsync_UnderBudget_Runs()
    {
        _ledger.Controls.Add(Control(AgentControl.AllTasks, enabled: true, limit: 1000));
        _ledger.TokensUsed = 999;

        var result = await Runner(new FakeChatClient()).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>An unknown task and an unconfigured provider are refused with their registered errors.</summary>
    [Fact]
    public async Task RunAsync_UnknownTaskOrProvider_IsRefused()
    {
        var unknown = await Runner(new FakeChatClient()).RunAsync(Request() with { TaskKey = "no.such_task" }, TestContext.Current.CancellationToken);
        var noProvider = await Runner(new FakeChatClient(), Configuration with { Provider = "anthropic" })
            .RunAsync(Request(), TestContext.Current.CancellationToken);
        var noTier = await Runner(new FakeChatClient(), Configuration with { Model = "tier:unknown" })
            .RunAsync(Request(), TestContext.Current.CancellationToken);

        unknown.Error.Should().Be(IntelligenceErrors.TaskUnknown);
        noProvider.Error.Should().Be(IntelligenceErrors.ProviderUnavailable);
        noTier.Error.Should().Be(IntelligenceErrors.ProviderUnavailable);
        _sender.Sent.Should().BeEmpty();
    }

    /// <summary>A refused start (for example, a switch turned off in between) calls no model.</summary>
    [Fact]
    public async Task RunAsync_WhenStartIsRefused_CallsNoModel()
    {
        var model = new FakeChatClient();
        _sender.StartError = IntelligenceErrors.AgentDisabled;

        var result = await Runner(model).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error.Should().Be(IntelligenceErrors.AgentDisabled);
        model.Calls.Should().Be(0);
    }

    /// <summary>A model that throws fails the run, which is still completed in the ledger, with no answer kept.</summary>
    [Fact]
    public async Task RunAsync_WhenTheModelThrows_RecordsAFailedRun()
    {
        var model = new FakeChatClient((_, _) => throw new InvalidOperationException("provider down"));

        var result = await Runner(model).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Value!.Status.Should().Be(AgentRunStatus.Failed);
        result.Value.Text.Should().BeNull();
        var complete = _sender.Sent.OfType<CompleteAgentRun>().Single();
        complete.Succeeded.Should().BeFalse();
        complete.FailureReason.Should().Be("The fake model call failed (InvalidOperationException).");
        complete.ResponseText.Should().BeNull();
    }

    /// <summary>A model that outlasts the timeout fails the run with the reason.</summary>
    [Fact]
    public async Task RunAsync_WhenTheModelTimesOut_RecordsAFailedRun()
    {
        var runner = Runner(new SlowChatClient(), options: new AgentRunnerOptions(TimeSpan.FromMilliseconds(50)));

        var result = await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Value!.Status.Should().Be(AgentRunStatus.Failed);
        _sender.Sent.OfType<CompleteAgentRun>().Single().FailureReason.Should().Be("The model did not answer within 0 seconds.");
    }

    /// <summary>The month starts at midnight UTC on the first, whatever the clock's offset.</summary>
    [Fact]
    public void MonthStart_IsTheFirstOfTheMonthInUtc() =>
        AgentRunner.MonthStart(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.FromHours(5)))
            .Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

    private static AgentRequest Request() =>
        new(Tenant, Configuration.Key, new Dictionary<string, string> { ["form"] = "demographics" }, [new ChatMessage(ChatRole.User, "Please review.")]);

    private static AgentControl Control(string scope, bool enabled, long? limit)
    {
        var control = AgentControl.For(Tenant, scope);
        control.Set(enabled, limit);
        return control;
    }

    /// <summary>An unqualified configuration is refused before any model call or ledger write, saying why (ADR-2631).</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_WhenTheConfigurationIsNotQualified_RefusesWithoutCallingTheModel()
    {
        var model = new FakeChatClient();

        var result = await Runner(model, qualify: false).RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        result.Error.Message.Should().Contain("data_review.query_draft v3 is not qualified in this tenant");
        model.Calls.Should().Be(0);
        _sender.Sent.Should().BeEmpty();
    }

    /// <summary>A suspended configuration is refused with the suspension reason.</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_WhenTheConfigurationIsSuspended_Refuses()
    {
        var model = new FakeChatClient();
        var runner = Runner(model);
        _ledger.Qualifications.Single().Suspend("Override rate above threshold.", _clock.UtcNow);

        var result = await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        result.Error.Message.Should().Contain("is suspended: Override rate above threshold.");
        model.Calls.Should().Be(0);
    }

    /// <summary>A configuration whose content changed after it was qualified is refused, naming both hashes.</summary>
    [Fact]
    [GxPRequirement("PRD-SYS-051")]
    public async Task RunAsync_WhenTheConfigurationChangedSinceQualification_Refuses()
    {
        var model = new FakeChatClient();
        var changed = Configuration with { Instructions = "Review {{form}} and draft more queries." };
        var runner = Runner(model, changed, qualify: false);
        _ledger.Qualifications.Add(TaskQualification.Qualify(Tenant, Configuration, Evidence(), Guid.NewGuid(), "dm-lead", _clock.UtcNow));

        var result = await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        result.Error.Message.Should().Contain("changed after it was qualified").And.Contain(Configuration.ContentHash).And.Contain(changed.ContentHash);
        model.Calls.Should().Be(0);
    }

    /// <summary>The runner uses the newest version that is qualified, skipping a newer one that is not.</summary>
    [Fact]
    public async Task RunAsync_RoutesToTheNewestQualifiedVersion()
    {
        var newer = Configuration with { Version = 4, Instructions = "Review {{form}} carefully." };
        var runner = Runner(new FakeChatClient(), qualify: false, extra: [newer]);
        _ledger.Qualifications.Add(TaskQualification.Qualify(Tenant, Configuration, Evidence(), Guid.NewGuid(), "dm-lead", _clock.UtcNow));

        var result = await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        _sender.Sent.OfType<StartAgentRun>().Single().Configuration.Version.Should().Be(3);

        _ledger.Qualifications.Add(TaskQualification.Qualify(Tenant, newer, Evidence(), Guid.NewGuid(), "dm-lead", _clock.UtcNow));
        await runner.RunAsync(Request(), TestContext.Current.CancellationToken);

        _sender.Sent.OfType<StartAgentRun>().Last().Configuration.Version.Should().Be(4);
    }

    private static EvaluationEvidence Evidence() => new("eval-run-1", "sha256:" + new string('b', 64), "sha256:" + new string('a', 64), "{}");

    private AgentRunner Runner(
        IChatClient client, TaskConfiguration? task = null, AgentRunnerOptions? options = null, bool qualify = true, TaskConfiguration[]? extra = null)
    {
        TaskConfiguration[] configurations = [task ?? Configuration, .. extra ?? []];
        if (qualify)
        {
            _ledger.Qualifications.RemoveAll(q => configurations.Any(c => c.Key == q.TaskKey && c.Version == q.TaskVersion));
            _ledger.Qualifications.AddRange(configurations.Select(c => TaskQualification.Qualify(Tenant, c, Evidence(), Guid.NewGuid(), "dm-lead", _clock.UtcNow)));
        }

        var prices = new Dictionary<string, ModelPrice> { ["fake-deep"] = new() { InputPerMillion = 5m, OutputPerMillion = 25m } };
        var provider = new ChatClientModelProvider(FakeChatClient.ProviderName, client, new Dictionary<string, string> { ["deep"] = "fake-deep" }, prices);
        return new AgentRunner(
            new Cadence.Modules.Intelligence.Infrastructure.Tasks.RegisteredTaskConfigurations(configurations),
            new ModelProviderRegistry([provider]),
            _ledger,
            _sender,
            _clock,
            options ?? AgentRunnerOptions.Default,
            NullLogger<AgentRunner>.Instance);
    }

    private sealed class RecordingSender : IKernelCommandSender
    {
        public List<ICommand> Sent { get; } = [];

        public KernelError? StartError { get; set; }

        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command, CommandMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            object result = command switch
            {
                StartAgentRun when StartError is { } error => CommandResult<Guid>.Failure(error),
                StartAgentRun start => RecordAndReturn(start, CommandResult<Guid>.Success(start.RunId)),
                CompleteAgentRun complete => RecordAndReturn(
                    complete, CommandResult<AgentRunStatus>.Success(complete.Succeeded ? AgentRunStatus.Succeeded : AgentRunStatus.Failed)),
                _ => throw new InvalidOperationException($"Unexpected {command.GetType().Name}."),
            };
            return Task.FromResult((CommandResult<TResult>)result);
        }

        private T RecordAndReturn<T>(ICommand command, T result)
        {
            Sent.Add(command);
            return result;
        }
    }

    private sealed class FakeLedger : IAgentLedger
    {
        public List<AgentControl> Controls { get; } = [];

        public long TokensUsed { get; set; }

        public List<(string? TaskKey, DateTimeOffset Since)> TokenQueries { get; } = [];

        public List<TaskQualification> Qualifications { get; } = [];

        public Task<IReadOnlyList<TaskQualification>> QualificationsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TaskQualification>>(Qualifications);

        public Task<IReadOnlyList<AgentControl>> ControlsAsync(TenantId tenant, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentControl>>(Controls);

        public Task<long> TokensUsedAsync(TenantId tenant, string? taskKey, DateTimeOffset since, CancellationToken cancellationToken)
        {
            TokenQueries.Add((taskKey, since));
            return Task.FromResult(TokensUsed);
        }

        public Task<IReadOnlyList<AgentRun>> RunsAsync(TenantId tenant, string requestedBy, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentRun>>([]);

        public Task<AgentRun?> FindRunAsync(TenantId tenant, Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentRun?>(null);
    }

    private sealed class SlowChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
