using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Infrastructure.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Intelligence;

/// <summary>
/// The kernel database plus the Intelligence module, registered the way the composition host registers it: its schema is
/// applied through the module SDK's migrations, the application role gets the module's grant, and the module runs with
/// the deterministic fake provider, so no model is ever called.
/// </summary>
public sealed class IntelligenceModulePostgresFixture : KernelPostgresFixture
{
    /// <summary>The task the tests run.</summary>
    public static readonly TaskConfiguration EchoTask = new(
        "test.echo", 1, "fake", "tier:fast", "Answer briefly about {{topic}}.", 200, AutonomyLevel.Inform);

    /// <summary>The runtime, in a new scope.</summary>
    public AgentRunner Runner => Scope().GetRequiredService<AgentRunner>();

    /// <summary>The ledger reader.</summary>
    public IAgentLedger Ledger => Scope().GetRequiredService<IAgentLedger>();

    protected override async ValueTask OnMigratedAsync(string connectionString)
    {
        var migrations = new ServiceCollection()
            .AddModuleMigrations<IntelligenceDbContext>(
                IntelligenceDbContext.Module, IntelligenceDbContext.Schema, o => new IntelligenceDbContext(o), IntelligenceDatabase.GrantAppRoleSql)
            .BuildServiceProvider()
            .GetRequiredService<ModuleMigrationRegistration>();
        await migrations.MigrateAsync(connectionString, CancellationToken.None).ConfigureAwait(false);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var grant = new NpgsqlCommand(migrations.GrantAppRoleSql(AppRole!), connection);
        await grant.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Intelligence:FakeProvider"] = "true" })
            .Build();
        services
            .AddCadenceIntelligenceModule(configuration, new TestingEnvironment(), ConnectionString!)
            .AddAgentTask(EchoTask);
    }

    /// <summary>Runs <paramref name="sql"/> as the application role, with no tenant set.</summary>
    public async Task<object?> ScalarAsAppRoleAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(ct);
    }

    private sealed class TestingEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = IntelligenceModule.TestingEnvironment;

        public string ApplicationName { get; set; } = "Cadence.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>
/// The Intelligence module against a real PostgreSQL server as the application role (ADR-2630 §4.2): a run is recorded
/// and completed through the kernel command pipeline with both versions on the kernel audit chain, the entry's hash
/// verifies after a re-read, kill switches and budgets refuse runs, Row-Level Security keeps tenants apart, only the
/// requester completes a run, and the role cannot delete ledger entries. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IntelligenceModulePostgresTests(IntelligenceModulePostgresFixture fixture)
    : IClassFixture<IntelligenceModulePostgresFixture>, IDisposable
{
    private const string Reason = "Intelligence integration test";

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// A run with the fake provider is recorded in the ledger through two module commands: the entry names the
    /// requester and the configuration version, its hash verifies when read back, both versions are links on the
    /// tenant's kernel audit chain, and completion raises the event the measurement layer reads.
    /// </summary>
    [Fact(DisplayName = "AgentRun_IsRecordedThroughTheKernelPipeline_WithVerifiableHashAndAudit")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task AgentRunIsRecordedThroughTheKernelPipelineWithVerifiableHashAndAudit()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);

        var result = await fixture.Runner.RunAsync(Request(tenant), ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.Status.Should().Be(AgentRunStatus.Succeeded);
        result.Value.Text.Should().StartWith("[fake fake-fast] ");
        var run = await fixture.Ledger.FindRunAsync(tenant, result.Value.RunId, ct);
        run.Should().NotBeNull();
        run!.RequestedBy.Should().Be(KernelPostgresFixture.ActorId);
        run.TaskKey.Should().Be("test.echo");
        run.TaskVersion.Should().Be(1);
        run.ConfigurationHash.Should().Be(IntelligenceModulePostgresFixture.EchoTask.ContentHash);
        run.ResponseText.Should().Be(result.Value.Text);
        run.InputTokens.Should().BePositive();
        run.ContentHash.Should().NotBeNull().And.Be(run.ComputeContentHash(), "the entry reads back exactly as it was hashed");
        run.VersionIndex.Should().Be(2);
        (await fixture.Ledger.RunsAsync(tenant, KernelPostgresFixture.ActorId, 10, ct)).Select(r => r.Id).Should().Equal(run.Id);

        await using var db = fixture.CreateAdminContext();
        var links = await db.AuditEntries.AsNoTracking()
            .Where(a => a.TenantId == tenant && a.AggregateType == "intelligence.AgentRun")
            .OrderBy(a => a.Sequence)
            .ToListAsync(ct);
        links.Select(a => a.Action).Should().Equal("Created", "Updated");
        links.Should().OnlyContain(a => a.AggregateId == run.Id.ToString("D"));
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "intelligence.AgentRunCompleted", ct))
            .Should().Be(1);
    }

    /// <summary>
    /// A kill switch set through its command stops new runs: the runtime refuses before any model call, and the start
    /// command refuses on its own, inside its transaction, for a caller that skipped the runtime's check.
    /// </summary>
    [Fact(DisplayName = "KillSwitch_RefusesRuns_InTheRuntimeAndInTheStartCommand")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task KillSwitchRefusesRunsInTheRuntimeAndInTheStartCommand()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);

        var set = await fixture.Sender.SendAsync(
            new SetAgentControl(tenant, "test.echo", Enabled: false, MonthlyTokenLimit: null, $"control-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct);
        set.IsSuccess.Should().BeTrue(set.Error?.Message);

        (await fixture.Runner.RunAsync(Request(tenant), ct)).Error.Should().Be(IntelligenceErrors.AgentDisabled);
        var direct = await fixture.Sender.SendAsync(
            new StartAgentRun(tenant, Guid.CreateVersion7(), IntelligenceModulePostgresFixture.EchoTask, "fake-fast", "[]", $"start-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct);
        direct.Error.Should().Be(IntelligenceErrors.AgentDisabled);
        (await fixture.Ledger.ControlsAsync(tenant, ct)).Should().ContainSingle(c => c.Scope == "test.echo" && !c.Enabled);

        var reenable = await fixture.Sender.SendAsync(
            new SetAgentControl(tenant, "test.echo", Enabled: true, MonthlyTokenLimit: null, $"control-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct);
        reenable.Value!.VersionIndex.Should().Be(2);
        (await fixture.Runner.RunAsync(Request(tenant), ct)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Once the month's tokens reach the tenant's budget, the next run is refused.</summary>
    [Fact(DisplayName = "Budget_WhenSpent_RefusesTheNextRun")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task BudgetWhenSpentRefusesTheNextRun()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var first = await fixture.Runner.RunAsync(Request(tenant), ct);
        var used = first.Value!.InputTokens + first.Value.OutputTokens;

        (await fixture.Sender.SendAsync(
            new SetAgentControl(tenant, AgentControl.AllTasks, Enabled: true, MonthlyTokenLimit: used, $"control-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct)).IsSuccess.Should().BeTrue();

        (await fixture.Ledger.TokensUsedAsync(tenant, null, DateTimeOffset.MinValue, ct)).Should().Be(used);
        (await fixture.Runner.RunAsync(Request(tenant), ct)).Error.Should().Be(IntelligenceErrors.BudgetExhausted);
    }

    /// <summary>
    /// Row-Level Security: another tenant's reads see neither the run nor its controls, and the application role sees no
    /// ledger rows at all without a tenant.
    /// </summary>
    [Fact(DisplayName = "Ledger_IsTenantIsolated_ByRowLevelSecurity")]
    public async Task LedgerIsTenantIsolatedByRowLevelSecurity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var other = await ProvisionTenantAsync(ct);
        var run = await fixture.Runner.RunAsync(Request(tenant), ct);
        (await fixture.Sender.SendAsync(
            new SetAgentControl(tenant, AgentControl.AllTasks, Enabled: true, MonthlyTokenLimit: 1_000_000, $"control-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct)).IsSuccess.Should().BeTrue();

        (await fixture.Ledger.FindRunAsync(other, run.Value!.RunId, ct)).Should().BeNull();
        (await fixture.Ledger.RunsAsync(other, KernelPostgresFixture.ActorId, 10, ct)).Should().BeEmpty();
        (await fixture.Ledger.ControlsAsync(other, ct)).Should().BeEmpty();
        (await fixture.Ledger.TokensUsedAsync(other, null, DateTimeOffset.MinValue, ct)).Should().Be(0);
        (await fixture.ScalarAsAppRoleAsync("SELECT count(*) FROM intelligence.agent_runs", ct)).Should().Be(0L);
    }

    /// <summary>K-AI-6: only the person a run acts for may complete it.</summary>
    [Fact(DisplayName = "CompleteAgentRun_BySomeoneElse_IsRefused")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task CompleteAgentRunBySomeoneElseIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var runId = Guid.CreateVersion7();
        (await fixture.Sender.SendAsync(
            new StartAgentRun(tenant, runId, IntelligenceModulePostgresFixture.EchoTask, "fake-fast", "[]", $"start-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct)).IsSuccess.Should().BeTrue();

        fixture.CurrentActor.Actor = Actor.Person("someone-else@cadence.test");
        var complete = await fixture.Sender.SendAsync(
            new CompleteAgentRun(tenant, runId, true, "hijacked", null, 1, 1, null, $"complete-{Guid.NewGuid():N}", Reason),
            cancellationToken: ct);

        complete.Error.Should().Be(IntelligenceErrors.RunNotYours);
        (await fixture.Ledger.FindRunAsync(tenant, runId, ct))!.Status.Should().Be(AgentRunStatus.Started);
    }

    /// <summary>The module's grant gives the application role no delete on the ledger and nothing on its migrations history.</summary>
    [Fact(DisplayName = "AppRole_CannotDeleteLedgerEntries_OrReadTheMigrationsHistory")]
    public async Task AppRoleCannotDeleteLedgerEntriesOrReadTheMigrationsHistory()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var delete = () => fixture.ScalarAsAppRoleAsync("DELETE FROM intelligence.agent_runs", ct);
        var history = () => fixture.ScalarAsAppRoleAsync("SELECT count(*) FROM intelligence.__ef_migrations_history", ct);

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await history.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static AgentRequest Request(TenantId tenant) =>
        new(tenant, "test.echo", new Dictionary<string, string> { ["topic"] = "visits" }, [new ChatMessage(ChatRole.User, "When is visit 2?")]);

    private async Task<TenantId> ProvisionTenantAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var command = new ProvisionTenant(TenantId.New(), $"I-{suffix}", $"Intelligence tenant {suffix}", $"key-{suffix}", Reason);
        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return command.TenantId;
    }
}
