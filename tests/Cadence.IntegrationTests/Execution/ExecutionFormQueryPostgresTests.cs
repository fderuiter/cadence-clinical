using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Execution's bitemporal data queries on the kernel command path, against a real PostgreSQL server as the application role
/// (#5461, ADR-2621, <c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>): revision-targeted assertions with real actors and verbatim
/// change reasons, separation of duties, and PostgreSQL <c>KT001</c> / <c>KA001</c> immutability guards.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormQueryPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>
{
    private const string Reason = "Source document review";
    private const string SiteAnswerer = "crc@query-pg.test";

    [Fact(DisplayName = "FormQuery_TargetsARevision_ForwardsRealActorAndChangeReason_AndMirrorsOnce")]
    [GxPRequirement("PRD-QRY-001")]
    [GxPRequirement("PRD-QRY-002")]
    public async Task FormQueryTargetsARevisionForwardsRealActorAndChangeReasonAndMirrorsOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var tenant = form.Subject.Tenant;
        await GrantSiteAnswererAsync(scenario, tenant, form.Subject.StudySite, ct);
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();

        var stale = await RaiseAsync(form, value.ObservationId, value.Revision + 1, ct);
        var foreign = await RaiseAsync(form, ObservationId.New(), 1, ct);
        var raised = await RaiseAsync(form, value.ObservationId, value.Revision, ct);
        stale.Error!.Code.Should().Be(FormQueryErrors.RevisionStale.Code);
        foreign.Error!.Code.Should().Be(FormQueryErrors.ObservationUnknown.Code);
        raised.Value!.Should().Match<FormQueryState>(q =>
            q.Status == FormQueryStatus.Open
            && q.Origin == FormQueryOrigin.Manual
            && q.OwnerRole == FormQueryOwnerRole.Site
            && q.ItemId == Systolic
            && q.Revision == 1);

        await RelayAsync(ct);
        var query = await ReadQueryAsync(raised.Value!.QueryId, ct);
        var open = await KernelAssertionsAsync(tenant, ct);
        open.Should().ContainSingle().Which.Should().Match<Assertion>(a =>
            a.Id == query.KernelQueryId
            && a.Kind == AssertionKind.Queried
            && a.TargetId == value.ObservationId.Value
            && a.TargetRevision == 1
            && a.Text == "Please confirm against the source"
            && a.AssertedBy == KernelPostgresFixture.ActorId
            && a.ReasonForChange == Reason);

        // Site answers as SiteAnswerer; monitor (default ActorId) closes with separation of duties satisfied.
        (await SendAsAsync(
                Actor.Person(SiteAnswerer),
                new AnswerFormQuery(tenant, query.Id, form.Subject.Enrollment, "Confirmed on the source document", Key(), "Checked chart worksheet"),
                ct))
            .Value!.Should().Match<FormQueryState>(q => q.Status == FormQueryStatus.Answered && q.OwnerRole == FormQueryOwnerRole.Monitor);

        // Self-close by SiteAnswerer is rejected with 409 SeparationOfDuties.
        await GrantCapabilityAsync(scenario, tenant, Actor.Person(SiteAnswerer), "execution.query.manage", AuthorityScope.StudySite(form.Subject.StudySite), ct);
        (await SendAsAsync(
                Actor.Person(SiteAnswerer),
                new CloseFormQuery(tenant, query.Id, form.Subject.Enrollment, Key(), "Attempted self-close"),
                ct))
            .Error!.Code.Should().Be(FormQueryErrors.SeparationOfDuties.Code);

        (await StepAsync(new CloseFormQuery(tenant, query.Id, form.Subject.Enrollment, Key(), "The answer settles it"), ct))
            .Value!.Status.Should().Be(FormQueryStatus.Closed);
        await RelayAsync(ct);

        var settled = await KernelAssertionsAsync(tenant, ct);
        settled.Should().HaveCount(3);
        settled.Should().ContainSingle(a =>
            a.Kind == AssertionKind.QueryAnswered
            && a.RetractsId == null
            && a.TargetId == query.KernelQueryId.Value
            && a.TargetRevision == 1
            && a.AssertedBy == SiteAnswerer
            && a.ReasonForChange == "Checked chart worksheet");
        settled.Should().ContainSingle(a =>
            a.RetractsId == query.KernelQueryId
            && a.AssertedBy == KernelPostgresFixture.ActorId
            && a.ReasonForChange == "The answer settles it");

        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        await RelayAsync(ct);
        (await KernelAssertionsAsync(tenant, ct)).Should().HaveCount(3, "a redelivered event replays its stored outcome");
    }

    [Fact(DisplayName = "FormQuery_Reopen_WithdrawsTheAnswerOrRaisesANewKernelQuery_AndCancelIsFinal")]
    public async Task FormQueryReopenWithdrawsTheAnswerOrRaisesANewKernelQueryAndCancelIsFinal()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var (tenant, enrollment) = (form.Subject.Tenant, form.Subject.Enrollment);
        await GrantSiteAnswererAsync(scenario, tenant, form.Subject.StudySite, ct);
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();
        var id = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value!.QueryId;

        // Open manual query cannot be closed directly.
        (await StepAsync(new CloseFormQuery(tenant, id, enrollment, Key(), "Direct close on open manual query"), ct))
            .Error!.Code.Should().Be(FormQueryErrors.InvalidTransition.Code);

        await SendAsAsync(Actor.Person(SiteAnswerer), new AnswerFormQuery(tenant, id, enrollment, "Confirmed", Key(), Reason), ct);
        await StepAsync(new ReopenFormQuery(tenant, id, enrollment, "The source reads 128", Key(), Reason), ct);
        await RelayAsync(ct);
        var reopenedAnswer = await KernelAssertionsAsync(tenant, ct);
        var firstQuery = reopenedAnswer.Single(a => a.Kind == AssertionKind.Queried && a.RetractsId == null);
        var answer = reopenedAnswer.Single(a => a.Kind == AssertionKind.QueryAnswered && a.RetractsId == null);
        reopenedAnswer.Should().ContainSingle(a => a.RetractsId == answer.Id, "reopening an answered query withdraws the answer");
        reopenedAnswer.Should().NotContain(a => a.RetractsId == firstQuery.Id, "the kernel query stays open");

        await SendAsAsync(Actor.Person(SiteAnswerer), new AnswerFormQuery(tenant, id, enrollment, "Updated to 128", Key(), Reason), ct);
        await StepAsync(new CloseFormQuery(tenant, id, enrollment, Key(), "Corrected at the site"), ct);
        var reopened = await StepAsync(new ReopenFormQuery(tenant, id, enrollment, "The correction is still wrong", Key(), Reason), ct);
        reopened.Value!.Status.Should().Be(FormQueryStatus.Open);
        await SendAsAsync(Actor.Person(SiteAnswerer), new AnswerFormQuery(tenant, id, enrollment, "Corrected again", Key(), Reason), ct);
        await RelayAsync(ct);
        var query = await ReadQueryAsync(id, ct);
        query.KernelQueryId.Should().NotBe(firstQuery.Id, "a closed query reopens as a new kernel query");
        (await KernelAssertionsAsync(tenant, ct)).Should().ContainSingle(a => a.Kind == AssertionKind.QueryAnswered && a.RetractsId == null && a.TargetId == query.KernelQueryId.Value);

        (await StepAsync(new CancelFormQuery(tenant, id, enrollment, Key(), "Raised on the wrong value"), ct)).Value!.Status.Should().Be(FormQueryStatus.Cancelled);
        foreach (var command in new ICommand<FormQueryState>[]
        {
            new AnswerFormQuery(tenant, id, enrollment, "Too late", Key(), Reason),
            new ReopenFormQuery(tenant, id, enrollment, "Too late", Key(), Reason),
            new CloseFormQuery(tenant, id, enrollment, Key(), "Too late"),
        })
        {
            (await StepAsync(command, ct)).Error!.Code.Should().Be(FormQueryErrors.InvalidTransition.Code);
        }
    }

    [Fact(DisplayName = "FormQuery_BitemporalPeriodsAndMessages_EnforceKT001AndKA001InPostgres")]
    public async Task FormQueryBitemporalPeriodsAndMessagesEnforceKT001AndKA001InPostgres()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();
        var queryId = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value!.QueryId;

        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);

        // KT001: mutating non-recorded_period columns or deleting from execution.form_query_periods is rejected.
        var updatePeriod = async () =>
        {
            await using var cmd = new NpgsqlCommand(
                $"UPDATE execution.form_query_periods SET priority = 'High' WHERE query_id = '{queryId.Value:D}'",
                connection);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        (await updatePeriod.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(ModuleBitemporalSql.AppendOnlyViolationSqlState);

        var deletePeriod = async () =>
        {
            await using var cmd = new NpgsqlCommand(
                $"DELETE FROM execution.form_query_periods WHERE query_id = '{queryId.Value:D}'",
                connection);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        (await deletePeriod.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(ModuleBitemporalSql.AppendOnlyViolationSqlState);

        // KA001: any UPDATE or DELETE on execution.form_query_messages is rejected.
        var updateMessage = async () =>
        {
            await using var cmd = new NpgsqlCommand(
                $"UPDATE execution.form_query_messages SET text = 'Tampered' WHERE query_id = '{queryId.Value:D}'",
                connection);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        (await updateMessage.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(ModuleInsertOnlySql.ViolationSqlState);

        var deleteMessage = async () =>
        {
            await using var cmd = new NpgsqlCommand(
                $"DELETE FROM execution.form_query_messages WHERE query_id = '{queryId.Value:D}'",
                connection);
            await cmd.ExecuteNonQueryAsync(ct);
        };
        (await deleteMessage.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(ModuleInsertOnlySql.ViolationSqlState);
    }

    [Fact(DisplayName = "FormQuery_UnderALockedForm_IsRefused_AndUnderAFrozenOneIsNot")]
    public async Task FormQueryUnderALockedFormIsRefusedAndUnderAFrozenOneIsNot()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var (tenant, study, enrollment) = (form.Subject.Tenant, form.Subject.Study, form.Subject.Enrollment);
        var saved = await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120)), Value(Diastolic, Mmhg(80))), ct);
        var value = saved.Values.Single(v => v.ItemId == Systolic);
        await GrantSiteAnswererAsync(scenario, tenant, form.Subject.StudySite, ct);

        var freeze = await scenario.Protocol.OkAsync(await scenario.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct, LockKind.Frozen), ct);
        var underFreeze = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value;
        underFreeze.Should().NotBeNull("a freeze stops data entry, not queries");
        (await SendAsAsync(Actor.Person(SiteAnswerer), new AnswerFormQuery(tenant, underFreeze!.QueryId, enrollment, "Confirmed on source", Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        (await StepAsync(new CloseFormQuery(tenant, underFreeze.QueryId, enrollment, Key(), "Verified and closed under freeze"), ct)).IsSuccess.Should().BeTrue();
        await scenario.Protocol.OkAsync(await scenario.Observations.ReleaseAsync(tenant, study, freeze.LockId, ct), ct);

        await RelayAsync(ct);

        // An Execution query raised just before a Locked assertion commits (before outbox relay) remains Open and cannot be answered under Locked (#5586).
        var id = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value!.QueryId;
        var locked = await scenario.Protocol.OkAsync(await scenario.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct), ct);
        (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        (await StepAsync(new AnswerFormQuery(tenant, id, enrollment, "Confirmed", Key(), Reason), ct)).Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        (await ReadQueryAsync(id, ct)).Status.Should().Be(FormQueryStatus.Open);

        await scenario.Protocol.OkAsync(await scenario.Observations.ReleaseAsync(tenant, study, locked.LockId, ct), ct);
        await RelayAsync(ct);
    }

    private static string Key() => KernelProtocolScenario.Key();

    private static Task GrantSiteAnswererAsync(KernelCollectionScenario scenario, TenantId tenant, StudySiteId site, CancellationToken ct) =>
        GrantCapabilityAsync(scenario, tenant, Actor.Person(SiteAnswerer), "execution.query.answer", AuthorityScope.StudySite(site), ct);

    private static Task GrantCapabilityAsync(KernelCollectionScenario scenario, TenantId tenant, Actor actor, string capability, AuthorityScope scope, CancellationToken ct) =>
        scenario.Protocol.OkAsync(
            new IssueGrant(tenant, GrantId.New(), actor, capability, null, scope, scenario.Tick(), null, false, Key(), "Granted for query test"),
            ct);

    private Task<CommandResult<FormQueryState>> RaiseAsync(Form form, ObservationId observation, int revision, CancellationToken ct) =>
        fixture.Sender.SendAsync(
            new RaiseFormQuery(
                form.Subject.Tenant,
                FormQueryId.New(),
                form.Instance,
                form.Subject.Enrollment,
                observation,
                revision,
                "Please confirm against the source",
                Key(),
                Reason),
            cancellationToken: ct);

    private Task<CommandResult<FormQueryState>> StepAsync(ICommand<FormQueryState> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private async Task<CommandResult<FormQueryState>> SendAsAsync(Actor actor, ICommand<FormQueryState> command, CancellationToken ct)
    {
        var previous = fixture.CurrentActor.Actor;
        fixture.CurrentActor.Actor = actor;
        try
        {
            return await fixture.Sender.SendAsync(command, cancellationToken: ct);
        }
        finally
        {
            fixture.CurrentActor.Actor = previous;
        }
    }

    private async Task<FormQuery> ReadQueryAsync(FormQueryId id, CancellationToken ct) =>
        (await fixture.ReadQueryAsync(id, ct))!;

    private async Task<List<Assertion>> KernelAssertionsAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return await db.Assertions.AsNoTracking().Where(a => a.TenantId == tenant).OrderBy(a => a.AssertedAt).ToListAsync(ct);
    }

    private async Task RelayAsync(CancellationToken ct)
    {
        var relayed = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
        relayed.Failed.Should().Be(0);
    }
}
