using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Trust;
using Cadence.Modules.Intelligence;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Evaluation.Checks;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;
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
    /// <summary>The task the tests run, complete enough to be qualified (ADR-2631).</summary>
    public static readonly TaskConfiguration EchoTask = new(
        "test.echo", 1, "fake", "tier:fast", "Answer briefly about {{topic}}.", 200, AutonomyLevel.Inform)
    {
        EvaluationSet = new EvaluationSetRef("test.echo_gold", 1, "sha256:" + new string('e', 64)),
        AcceptanceCriteria = new AcceptanceCriteria(MinimumPassRate: 0.9, MinimumReviewedPassRate: 0.95),
        Credibility = new CredibilityAssessment(
            "When is the participant's next visit?", "A coordinator reads the answer and checks the schedule.", ModelInfluence.Low, DecisionConsequence.Low),
    };

    /// <summary>A task registered without evaluation set, criteria or credibility assessment, so it cannot be qualified.</summary>
    public static readonly TaskConfiguration UngovernedTask = new(
        "test.ungoverned", 1, "fake", "tier:fast", "Answer.", 200, AutonomyLevel.Inform);

    /// <summary>The runtime, in a new scope.</summary>
    public AgentRunner Runner => Scope().GetRequiredService<AgentRunner>();

    /// <summary>The port monitoring uses to suspend a configuration.</summary>
    public ITaskConfigurationSuspension Suspension => Scope().GetRequiredService<ITaskConfigurationSuspension>();

    /// <summary>The ledger reader.</summary>
    public IAgentLedger Ledger => Scope().GetRequiredService<IAgentLedger>();

    /// <summary>The stored evaluation runs' reader.</summary>
    public IEvaluationRuns EvaluationRuns => Scope().GetRequiredService<IEvaluationRuns>();

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
            .AddAgentTask(EchoTask)
            .AddAgentTask(UngovernedTask);
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
        var tenant = await QualifiedTenantAsync(ct);

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
        run.InferenceSupplier.Should().Be("in-process", "the fake provider runs in the host and sends nothing (ADR-2636)");
        run.InferenceLocality.Should().Be("InDeployment");
        run.InferencePolicy.Should().Be("ResidencyBound", "a tenant without a policy is residency-bound");
        run.InferenceResidency.Should().BeNull();
        run.ScrubCounts.Should().Be("{}", "the outbound guard ran and found nothing to scrub");
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
        var tenant = await QualifiedTenantAsync(ct);

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
        var tenant = await QualifiedTenantAsync(ct);
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
        var tenant = await QualifiedTenantAsync(ct);
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
        var tenant = await QualifiedTenantAsync(ct);
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

    /// <summary>
    /// ADR-2631: a configuration the tenant has not qualified is refused by the runtime and by the start command, before
    /// any model call or ledger write, and the refusal says why.
    /// </summary>
    [Fact(DisplayName = "UnqualifiedConfiguration_IsRefused_ByTheRuntimeAndTheStartCommand")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task UnqualifiedConfigurationIsRefusedByTheRuntimeAndTheStartCommand()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);

        var run = await fixture.Runner.RunAsync(Request(tenant), ct);
        var direct = await fixture.Sender.SendAsync(
            new StartAgentRun(tenant, Guid.CreateVersion7(), IntelligenceModulePostgresFixture.EchoTask, "fake-fast", "[]", Key(), Reason),
            cancellationToken: ct);

        run.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        run.Error.Message.Should().Contain("test.echo v1 is not qualified in this tenant");
        direct.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        (await fixture.Ledger.RunsAsync(tenant, KernelPostgresFixture.ActorId, 10, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// ADR-2631, K-AI-5: a configuration whose content differs from the registered version (here, a changed prompt) is
    /// refused at qualification and at run start, so a changed prompt cannot run on an earlier qualification.
    /// </summary>
    [Fact(DisplayName = "ChangedConfiguration_IsRefused_AtQualificationAndRunStart")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task ChangedConfigurationIsRefusedAtQualificationAndRunStart()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await QualifiedTenantAsync(ct);
        var changed = IntelligenceModulePostgresFixture.EchoTask with { Instructions = "Answer at length about {{topic}}." };

        var qualify = await QualifyAsync(tenant, await AttestAsync(tenant, IntelligenceModulePostgresFixture.EchoTask, ct), ct, configuration: changed);
        var start = await fixture.Sender.SendAsync(
            new StartAgentRun(tenant, Guid.CreateVersion7(), changed, "fake-fast", "[]", Key(), Reason), cancellationToken: ct);

        qualify.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationChanged.Code);
        start.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationChanged.Code);
        (await fixture.Ledger.RunsAsync(tenant, KernelPostgresFixture.ActorId, 10, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// ADR-2631: suspending a qualified configuration takes it out of use with its reason; it is idempotent; only a new
    /// qualification, on new evidence and a new signature, restores it, and both changes are audited versions.
    /// </summary>
    [Fact(DisplayName = "SuspendedConfiguration_IsRefused_UntilQualifiedAgain")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task SuspendedConfigurationIsRefusedUntilQualifiedAgain()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await QualifiedTenantAsync(ct);
        var reference = IntelligenceModulePostgresFixture.EchoTask.Ref;

        var suspended = await fixture.Suspension.SuspendAsync(tenant, reference, "Answer accuracy fell below the acceptance criterion.", ct);
        var again = await fixture.Suspension.SuspendAsync(tenant, reference, "Answer accuracy fell below the acceptance criterion.", ct);
        var refused = await fixture.Runner.RunAsync(Request(tenant), ct);

        suspended.IsSuccess.Should().BeTrue(suspended.Error?.Message);
        suspended.Value!.Status.Should().Be(QualificationStatus.Suspended);
        suspended.Value.VersionIndex.Should().Be(2);
        again.Value!.Status.Should().Be(QualificationStatus.Suspended);
        refused.Error!.Message.Should().Contain("is suspended: Answer accuracy fell below the acceptance criterion.");

        var requalified = await QualifyAsync(tenant, await AttestAsync(tenant, IntelligenceModulePostgresFixture.EchoTask, ct), ct);
        var restored = await fixture.Runner.RunAsync(Request(tenant), ct);

        requalified.IsSuccess.Should().BeTrue(requalified.Error?.Message);
        requalified.Value!.Status.Should().Be(QualificationStatus.Qualified);
        requalified.Value.VersionIndex.Should().Be(3);
        restored.IsSuccess.Should().BeTrue(restored.Error?.Message);
        var stored = (await fixture.Ledger.QualificationsAsync(tenant, ct)).Should().ContainSingle().Subject;
        stored.Status.Should().Be(QualificationStatus.Qualified);
        stored.SuspensionReason.Should().BeNull();
        stored.QualifiedBy.Should().Be(KernelPostgresFixture.ActorId);
    }

    /// <summary>
    /// ADR-2631, 21 CFR 11.50/11.70: qualification needs the qualifier's own signature over this version, a stored
    /// evaluation run of this version that met its criteria against the pinned set (#5574), and a configuration complete
    /// enough to qualify; it is not repeated while qualified.
    /// </summary>
    [Fact(DisplayName = "Qualification_NeedsTheQualifiersSignature_AcceptedEvidence_AndACompleteConfiguration")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task QualificationNeedsTheQualifiersSignatureAcceptedEvidenceAndACompleteConfiguration()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var mine = await AttestAsync(tenant, IntelligenceModulePostgresFixture.EchoTask, ct);
        var otherTarget = await AttestAsync(tenant, IntelligenceModulePostgresFixture.UngovernedTask, ct);
        var failedRun = await RecordRunAsync(tenant, ct, passed: false);
        failedRun.IsSuccess.Should().BeTrue(failedRun.Error?.Message);
        failedRun.Value!.Accepted.Should().BeFalse();

        fixture.CurrentActor.Actor = Actor.Person("someone-else@cadence.test");
        var byAnother = await QualifyAsync(tenant, mine, ct);
        fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        var wrongTarget = await QualifyAsync(tenant, otherTarget, ct);
        var failed = await QualifyAsync(tenant, mine, ct, failedRun.Value.RunId);
        var missing = await QualifyAsync(tenant, mine, ct, Guid.NewGuid());
        var ungoverned = await QualifyAsync(tenant, otherTarget, ct, configuration: IntelligenceModulePostgresFixture.UngovernedTask);

        byAnother.Error!.Code.Should().Be(IntelligenceErrors.AttestationRejected.Code);
        wrongTarget.Error!.Code.Should().Be(IntelligenceErrors.AttestationRejected.Code);
        failed.Error!.Code.Should().Be(IntelligenceErrors.EvaluationNotAccepted.Code);
        missing.Error!.Code.Should().Be(IntelligenceErrors.EvaluationRunNotFound.Code);
        ungoverned.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationNotQualifiable.Code);
        ungoverned.Error.Message.Should().Contain("Pin an evaluation set.");
        (await fixture.Ledger.QualificationsAsync(tenant, ct)).Should().BeEmpty();

        var accepted = await RecordRunAsync(tenant, ct);
        var qualified = await QualifyAsync(tenant, mine, ct, accepted.Value!.RunId);
        var twice = await QualifyAsync(tenant, mine, ct);

        qualified.IsSuccess.Should().BeTrue(qualified.Error?.Message);
        qualified.Value!.RiskClass.Should().Be(ModelRiskClass.Low);
        twice.Error.Should().Be(IntelligenceErrors.AlreadyQualified);
        var stored = (await fixture.Ledger.QualificationsAsync(tenant, ct)).Should().ContainSingle().Subject;
        stored.EvaluationRunReference.Should().Be(accepted.Value.RunId.ToString("D"));
        stored.EvaluationRunHash.Should().Be(accepted.Value.RecordHash);
        stored.EvaluationSetHash.Should().Be(IntelligenceModulePostgresFixture.EchoTask.EvaluationSet!.ContentHash);
    }

    /// <summary>
    /// ADR-2634 §4.4, #5574: the server stores an evaluation run only when it evaluated a shipped configuration version
    /// against its pinned set, criteria and provider and its verdict follows from its cases; the stored record reads back
    /// exactly as hashed, once per run id, and only in its tenant.
    /// </summary>
    [Fact(DisplayName = "EvaluationRun_IsStoredOnlyWhenItIsEvidenceForItsConfiguration")]
    [GxPRequirement("PRD-SYS-051")]
    public async Task EvaluationRunIsStoredOnlyWhenItIsEvidenceForItsConfiguration()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var other = await ProvisionTenantAsync(ct);

        var forged = await RecordRunAsync(tenant, ct, passed: false, edit: r => r with { Verdict = new AcceptanceVerdict(true, []) });
        var wrongSet = await RecordRunAsync(tenant, ct, edit: r => r with { Set = r.Set with { ContentHash = "sha256:" + new string('0', 64) } });
        var ungoverned = await RecordRunAsync(tenant, ct, configuration: IntelligenceModulePostgresFixture.UngovernedTask);
        var changed = await RecordRunAsync(tenant, ct, configuration: IntelligenceModulePostgresFixture.EchoTask with { Instructions = "Answer at length." });
        var notJson = await fixture.Sender.SendAsync(new RecordEvaluationRun(tenant, "{\"runId\": 1}", Key(), Reason), cancellationToken: ct);

        forged.Error!.Code.Should().Be(IntelligenceErrors.EvaluationRunInvalid.Code);
        forged.Error.Message.Should().Contain("verdict");
        wrongSet.Error!.Code.Should().Be(IntelligenceErrors.EvaluationRunInvalid.Code);
        ungoverned.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationNotQualifiable.Code);
        changed.Error!.Code.Should().Be(IntelligenceErrors.ConfigurationChanged.Code);
        notJson.Error!.Code.Should().Be(IntelligenceErrors.EvaluationRunInvalid.Code);

        var record = Record(IntelligenceModulePostgresFixture.EchoTask, passed: true);
        var recorded = await RecordRunAsync(tenant, ct, edit: _ => record);
        var again = await RecordRunAsync(tenant, ct, edit: _ => record);

        recorded.IsSuccess.Should().BeTrue(recorded.Error?.Message);
        recorded.Value!.RecordHash.Should().Be(record.ContentHash());
        again.Error.Should().Be(KernelErrors.AlreadyExists);
        var stored = await fixture.EvaluationRuns.FindAsync(tenant, record.RunId, ct);
        stored!.RecordJson.Should().Be(record.ToJson());
        ContentHash.Compute(stored.RecordJson).Should().Be(stored.RecordHash);
        (await fixture.EvaluationRuns.ForTaskAsync(tenant, "test.echo", 1, 10, ct)).Should().ContainSingle();

        (await fixture.EvaluationRuns.FindAsync(other, record.RunId, ct)).Should().BeNull();
        var elsewhere = await QualifyAsync(other, await AttestAsync(other, IntelligenceModulePostgresFixture.EchoTask, ct), ct, record.RunId);
        elsewhere.Error!.Code.Should().Be(IntelligenceErrors.EvaluationRunNotFound.Code);
        (await fixture.ScalarAsAppRoleAsync("SELECT count(*) FROM intelligence.evaluation_runs", ct)).Should().Be(0L);
    }

    /// <summary>Qualifications are tenant data under Row-Level Security: another tenant sees none and cannot run on them.</summary>
    [Fact(DisplayName = "Qualifications_AreTenantIsolated_ByRowLevelSecurity")]
    public async Task QualificationsAreTenantIsolatedByRowLevelSecurity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await QualifiedTenantAsync(ct);
        var other = await ProvisionTenantAsync(ct);

        (await fixture.Ledger.QualificationsAsync(tenant, ct)).Should().ContainSingle();
        (await fixture.Ledger.QualificationsAsync(other, ct)).Should().BeEmpty();
        (await fixture.Runner.RunAsync(Request(other), ct)).Error!.Code.Should().Be(IntelligenceErrors.ConfigurationUnqualified.Code);
        (await fixture.ScalarAsAppRoleAsync("SELECT count(*) FROM intelligence.task_qualifications", ct)).Should().Be(0L);
    }

    /// <summary>The module's grant gives the application role no delete on the ledger and nothing on its migrations history.</summary>
    [Fact(DisplayName = "AppRole_CannotDeleteLedgerEntries_OrReadTheMigrationsHistory")]
    public async Task AppRoleCannotDeleteLedgerEntriesOrReadTheMigrationsHistory()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var delete = () => fixture.ScalarAsAppRoleAsync("DELETE FROM intelligence.agent_runs", ct);
        var deleteQualification = () => fixture.ScalarAsAppRoleAsync("DELETE FROM intelligence.task_qualifications", ct);
        var deleteEvaluationRun = () => fixture.ScalarAsAppRoleAsync("DELETE FROM intelligence.evaluation_runs", ct);
        var history = () => fixture.ScalarAsAppRoleAsync("SELECT count(*) FROM intelligence.__ef_migrations_history", ct);

        (await delete.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await deleteQualification.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await deleteEvaluationRun.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        (await history.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>Signs the configuration as approved, as the current person, and returns the attestation.</summary>
    private async Task<AttestationId> AttestAsync(TenantId tenant, TaskConfiguration configuration, CancellationToken ct)
    {
        var target = new AttestationTarget(
            TaskConfigurationAttestationCheck.TargetKind,
            TaskConfiguration.AttestationTargetId(configuration.Key, configuration.Version),
            configuration.ContentHash);
        var result = await fixture.Sender.SendAsync(
            new Attest(tenant, AttestationId.New(), AttestationMeaning.Approved, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), "Approving the configuration"),
            cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value;
    }

    private async Task<CommandResult<TaskQualificationState>> QualifyAsync(
        TenantId tenant, AttestationId attestation, CancellationToken ct, Guid? evaluationRunId = null, TaskConfiguration? configuration = null)
    {
        if (evaluationRunId is null)
        {
            var recorded = await RecordRunAsync(tenant, ct);
            recorded.IsSuccess.Should().BeTrue(recorded.Error?.Message);
            evaluationRunId = recorded.Value!.RunId;
        }

        return await fixture.Sender.SendAsync(
            new QualifyTaskConfiguration(
                tenant,
                (configuration ?? IntelligenceModulePostgresFixture.EchoTask).Ref,
                evaluationRunId.Value,
                attestation,
                Key(),
                Reason),
            cancellationToken: ct);
    }

    /// <summary>Stores a run record of <paramref name="configuration"/> (the echo task by default), optionally edited first.</summary>
    private Task<CommandResult<EvaluationRunState>> RecordRunAsync(
        TenantId tenant,
        CancellationToken ct,
        bool passed = true,
        TaskConfiguration? configuration = null,
        Func<EvaluationRunRecord, EvaluationRunRecord>? edit = null)
    {
        var record = Record(configuration ?? IntelligenceModulePostgresFixture.EchoTask, passed);
        return fixture.Sender.SendAsync(new RecordEvaluationRun(tenant, (edit ?? (r => r))(record).ToJson(), Key(), Reason), cancellationToken: ct);
    }

    /// <summary>
    /// A run record as the harness writes it for <paramref name="configuration"/> on the fake provider: two reviewed
    /// cases and one adversarial case, all passing, or with the adversarial case failing its outcome check.
    /// </summary>
    private EvaluationRunRecord Record(TaskConfiguration configuration, bool passed)
    {
        var usage = new RunUsage(120, 30, 0m, TimeSpan.FromMilliseconds(5));
        CheckResult[] good = [new(CheckKind.Outcome, true), new(CheckKind.Schema, true), new(CheckKind.Fields, true), new(CheckKind.Citations, true), new(CheckKind.Grounding, true), new(CheckKind.ForbiddenContent, true)];
        IReadOnlyList<CaseResult> cases =
        [
            new("visit-2", CaseKind.Reviewed, true, good, usage),
            new("visit-3", CaseKind.Reviewed, true, good, usage),
            passed
                ? new("injection", CaseKind.Adversarial, true, good, usage, AdversarialCategory.PromptInjection)
                : new("injection", CaseKind.Adversarial, false, [new(CheckKind.Outcome, false, "Expected refuse, got propose.")], usage, AdversarialCategory.PromptInjection),
        ];
        var criteria = configuration.AcceptanceCriteria ?? new AcceptanceCriteria(0.9, 0.95);
        var metrics = EvaluationHarness.Measure(cases);
        var started = fixture.Clock.UtcNow.AddMinutes(-5);
        return new EvaluationRunRecord(
            Guid.CreateVersion7(),
            configuration.Ref,
            configuration.EvaluationSet ?? new EvaluationSetRef("test.none", 1, "sha256:" + new string('f', 64)),
            new ProviderInfo(configuration.Provider, "fake-fast"),
            started,
            started.AddMinutes(1),
            criteria,
            cases,
            metrics,
            EvaluationHarness.Decide(criteria, cases, metrics));
    }

    /// <summary>A tenant in which the echo task is qualified.</summary>
    private async Task<TenantId> QualifiedTenantAsync(CancellationToken ct)
    {
        var tenant = await ProvisionTenantAsync(ct);
        var qualified = await QualifyAsync(tenant, await AttestAsync(tenant, IntelligenceModulePostgresFixture.EchoTask, ct), ct);
        qualified.IsSuccess.Should().BeTrue(qualified.Error?.Message);
        return tenant;
    }

    private static string Key() => $"it-{Guid.NewGuid():N}";

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
