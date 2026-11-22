using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Execution's data queries on the kernel command path, against a real PostgreSQL server as the application role (#5461,
/// ADR-2621): a query targets one revision of a form's value, each of its steps is stated as a kernel <c>Queried</c> or
/// <c>QueryAnswered</c> assertion or retraction by the consumer, once however often the event is delivered, and a Locked
/// lock refuses it. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormQueryPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>
{
    private const string Reason = "Source document review";

    /// <summary>
    /// Validates the query's life in the kernel: a query on revision 1 of the systolic value becomes an open
    /// <c>Queried</c> assertion about its observation; a query on a stale revision or on another form's value is refused;
    /// the answer becomes a <c>QueryAnswered</c> assertion of it, and closing retracts the kernel query; a redelivered
    /// event writes nothing twice.
    /// </summary>
    [Fact(DisplayName = "FormQuery_TargetsARevision_AndIsStatedAsKernelAssertionsOnce")]
    public async Task FormQueryTargetsARevisionAndIsStatedAsKernelAssertionsOnce()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var tenant = form.Subject.Tenant;
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();

        var stale = await RaiseAsync(form, value.ObservationId, value.Revision + 1, ct);
        var foreign = await RaiseAsync(form, ObservationId.New(), 1, ct);
        var raised = await RaiseAsync(form, value.ObservationId, value.Revision, ct);
        stale.Error!.Code.Should().Be(FormQueryErrors.RevisionStale.Code);
        foreign.Error!.Code.Should().Be(FormQueryErrors.ObservationUnknown.Code);
        raised.Value!.Should().Match<FormQueryState>(q => q.Status == FormQueryStatus.Open && q.ItemId == Systolic && q.Revision == 1);

        await RelayAsync(ct);
        var query = await ReadQueryAsync(raised.Value!.QueryId, ct);
        var open = await KernelAssertionsAsync(tenant, ct);
        open.Should().ContainSingle().Which.Should().Match<Assertion>(a =>
            a.Id == query.KernelQueryId && a.Kind == AssertionKind.Queried && a.TargetId == value.ObservationId.Value
            && a.Text == "Please confirm against the source" && a.AssertedBy == $"svc:{MirrorFormQueryToKernel.Name}");

        (await StepAsync(new AnswerFormQuery(tenant, query.Id, form.Subject.Enrollment, "Confirmed on the source document", Key(), Reason), ct))
            .Value!.Status.Should().Be(FormQueryStatus.Answered);
        (await StepAsync(new CloseFormQuery(tenant, query.Id, form.Subject.Enrollment, Key(), "The answer settles it"), ct))
            .Value!.Status.Should().Be(FormQueryStatus.Closed);
        await RelayAsync(ct);

        var settled = await KernelAssertionsAsync(tenant, ct);
        settled.Should().HaveCount(3);
        settled.Should().ContainSingle(a => a.Kind == AssertionKind.QueryAnswered && a.RetractsId == null && a.TargetId == query.KernelQueryId.Value);
        settled.Should().ContainSingle(a => a.RetractsId == query.KernelQueryId, "closing the query retracts the kernel query");

        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        await RelayAsync(ct);
        (await KernelAssertionsAsync(tenant, ct)).Should().HaveCount(3, "a redelivered event replays its stored outcome");
    }

    /// <summary>
    /// Validates the two reopenings: reopening an answered query withdraws the kernel answer and keeps the kernel query;
    /// reopening a closed one raises a new kernel query, which the module then answers against. A cancelled query is
    /// final.
    /// </summary>
    [Fact(DisplayName = "FormQuery_Reopen_WithdrawsTheAnswerOrRaisesANewKernelQuery_AndCancelIsFinal")]
    public async Task FormQueryReopenWithdrawsTheAnswerOrRaisesANewKernelQueryAndCancelIsFinal()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var (tenant, enrollment) = (form.Subject.Tenant, form.Subject.Enrollment);
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();
        var id = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value!.QueryId;

        await StepAsync(new AnswerFormQuery(tenant, id, enrollment, "Confirmed", Key(), Reason), ct);
        await StepAsync(new ReopenFormQuery(tenant, id, enrollment, "The source reads 128", Key(), Reason), ct);
        await RelayAsync(ct);
        var reopenedAnswer = await KernelAssertionsAsync(tenant, ct);
        var firstQuery = reopenedAnswer.Single(a => a.Kind == AssertionKind.Queried && a.RetractsId == null);
        var answer = reopenedAnswer.Single(a => a.Kind == AssertionKind.QueryAnswered && a.RetractsId == null);
        reopenedAnswer.Should().ContainSingle(a => a.RetractsId == answer.Id, "reopening an answered query withdraws the answer");
        reopenedAnswer.Should().NotContain(a => a.RetractsId == firstQuery.Id, "the kernel query stays open");

        await StepAsync(new CloseFormQuery(tenant, id, enrollment, Key(), "Corrected at the site"), ct);
        var reopened = await StepAsync(new ReopenFormQuery(tenant, id, enrollment, "The correction is still wrong", Key(), Reason), ct);
        reopened.Value!.Status.Should().Be(FormQueryStatus.Open);
        await StepAsync(new AnswerFormQuery(tenant, id, enrollment, "Corrected again", Key(), Reason), ct);
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

    /// <summary>
    /// Validates that a Locked lock on the form refuses raising and answering a query about its values, and a Frozen one
    /// does not (spec §10.5: a freeze stops data entry, not review).
    /// </summary>
    [Fact(DisplayName = "FormQuery_UnderALockedForm_IsRefused_AndUnderAFrozenOneIsNot")]
    public async Task FormQueryUnderALockedFormIsRefusedAndUnderAFrozenOneIsNot()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelCollectionScenario(fixture);
        var form = await scenario.NewFormAsync(ct);
        var (tenant, study, enrollment) = (form.Subject.Tenant, form.Subject.Study, form.Subject.Enrollment);
        var value = (await scenario.Protocol.OkAsync(Save(form, Value(Systolic, Mmhg(120))), ct)).Values.Single();
        var id = (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Value!.QueryId;

        var freeze = await scenario.Protocol.OkAsync(await scenario.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct, LockKind.Frozen), ct);
        var secondRaised = await RaiseAsync(form, value.ObservationId, value.Revision, ct);
        secondRaised.Value.Should().NotBeNull("a freeze stops data entry, not queries");
        var secondId = secondRaised.Value!.QueryId;
        await scenario.Protocol.OkAsync(await scenario.Observations.ReleaseAsync(tenant, study, freeze.LockId, ct), ct);

        await RelayAsync(ct);

        // Locked is refused with ScopeNotReady while open queries or missing required items remain (#5466).
        var prematureLock = await fixture.Sender.SendAsync(await scenario.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct), cancellationToken: ct);
        prematureLock.Error!.Code.Should().Be(AssertionErrors.ScopeNotReady.Code);
        prematureLock.Error.Explanation.Should().NotBeNull();

        // Complete required Diastolic and close both open queries, relay to the kernel, then apply Locked.
        await scenario.Protocol.OkAsync(Save(form, Value(Diastolic, Mmhg(80))), ct);
        await scenario.Protocol.OkAsync(new CloseFormQuery(tenant, id, enrollment, Key(), "Confirmed against source"), ct);
        await scenario.Protocol.OkAsync(new CloseFormQuery(tenant, secondId, enrollment, Key(), "Duplicate closed"), ct);
        await RelayAsync(ct);

        await scenario.Protocol.OkAsync(await scenario.Observations.LockAsync(tenant, study, NodeRef.For(form.Instance), ct), ct);
        (await RaiseAsync(form, value.ObservationId, value.Revision, ct)).Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        (await StepAsync(new ReopenFormQuery(tenant, id, enrollment, "Reopen under lock", Key(), Reason), ct)).Error!.Code.Should().Be(AssertionErrors.Locked.Code);
        (await ReadQueryAsync(id, ct)).Status.Should().Be(FormQueryStatus.Closed);
    }

    private static string Key() => KernelProtocolScenario.Key();

    private Task<CommandResult<FormQueryState>> RaiseAsync(Form form, ObservationId observation, int revision, CancellationToken ct) =>
        fixture.Sender.SendAsync(
            new RaiseFormQuery(
                form.Subject.Tenant, Guid.CreateVersion7(), form.Instance, form.Subject.Enrollment, observation, revision,
                "Please confirm against the source", Key(), Reason),
            cancellationToken: ct);

    private Task<CommandResult<FormQueryState>> StepAsync(ICommand<FormQueryState> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private async Task<FormQuery> ReadQueryAsync(Guid id, CancellationToken ct) =>
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
