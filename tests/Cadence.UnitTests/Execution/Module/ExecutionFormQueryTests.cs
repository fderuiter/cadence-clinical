using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Kernel;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// The query workflow without a database (#5461): which steps each status allows, the kernel assertion each step states,
/// and how a query read shows that its value changed.
/// </summary>
public sealed class ExecutionFormQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Raising states a kernel query; answering answers it; reopening an answered query retracts the answer and keeps the
    /// kernel query; closing retracts the kernel query; reopening a closed one raises a new kernel query.
    /// </summary>
    [Fact(DisplayName = "FormQuery_Steps_StateTheMatchingKernelAssertions")]
    public void FormQueryStepsStateTheMatchingKernelAssertions()
    {
        var (query, raised) = Raise();
        raised.Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, query.KernelQueryId, null, "Confirm the value"));
        var firstQuery = query.KernelQueryId;

        var answerId = AssertionId.New();
        query.Answer(" Confirmed ", answerId, "crc", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Answer, answerId, firstQuery, "Confirmed"));
        query.KernelAnswerId.Should().Be(answerId);

        var withdrawal = AssertionId.New();
        query.Reopen("Source reads 128", withdrawal, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, withdrawal, answerId, null));
        query.Should().Match<FormQuery>(q => q.Status == FormQueryStatus.Open && q.KernelQueryId == firstQuery && q.KernelAnswerId == null);

        var close = AssertionId.New();
        query.Close("Corrected", close, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, close, firstQuery, null));

        var second = AssertionId.New();
        query.Reopen("Still wrong", second, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, second, null, "Still wrong"));
        query.KernelQueryId.Should().Be(second);
        query.Messages.Select(m => m.Kind).Should().Equal(
            FormQueryMessageKind.Raised, FormQueryMessageKind.Answered, FormQueryMessageKind.Reopened, FormQueryMessageKind.Closed, FormQueryMessageKind.Reopened);
    }

    /// <summary>An open query cannot be reopened, a closed one cannot be answered or closed again, and a cancelled one allows nothing.</summary>
    [Fact(DisplayName = "FormQuery_RefusesStepsItsStatusDoesNotAllow")]
    public void FormQueryRefusesStepsItsStatusDoesNotAllow()
    {
        var (query, _) = Raise();
        query.Reopen("Why", AssertionId.New(), "cra", At).Should().BeNull();

        query.Close("Done", AssertionId.New(), "cra", At).Should().NotBeNull();
        query.Answer("Late", AssertionId.New(), "crc", At).Should().BeNull();
        query.Close("Again", AssertionId.New(), "cra", At).Should().BeNull();
        query.Cancel("Error", AssertionId.New(), "cra", At).Should().BeNull();

        var (cancelled, _) = Raise();
        cancelled.Cancel("Raised on the wrong value", AssertionId.New(), "cra", At).Should().NotBeNull();
        cancelled.Answer("Late", AssertionId.New(), "crc", At).Should().BeNull();
        cancelled.Reopen("Late", AssertionId.New(), "cra", At).Should().BeNull();
        cancelled.Status.Should().Be(FormQueryStatus.Cancelled);

        var empty = () => Raise(text: " ");
        var overlong = () => Raise(text: new string('x', 4001));
        empty.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A query read shows the value changed once its current revision passed the queried one, the question as last
    /// reopened, and no current revision when the form no longer shows the value.
    /// </summary>
    [Fact(DisplayName = "FormQueryResponse_ShowsTheValueChanged_AndTheQuestionAsItStands")]
    public void FormQueryResponseShowsTheValueChangedAndTheQuestionAsItStands()
    {
        var (query, _) = Raise();
        query.Answer("Confirmed", AssertionId.New(), "crc", At);
        query.Reopen("Source reads 128", AssertionId.New(), "cra", At);
        var state = FormQueryState.Of(query, 3);

        var changed = FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 2 });
        changed.Should().Match<FormQueryResponse>(r =>
            r.Status == "open" && r.CurrentRevision == 2 && r.ValueChanged && r.Text == "Source reads 128" && r.RaisedBy == "cra" && r.Version == 3);
        changed.Messages.Select(m => m.Kind).Should().Equal("raised", "answered", "reopened");

        FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 1 }).ValueChanged.Should().BeFalse();
        FormQueryResponse.From(state, new Dictionary<ObservationId, int>()).Should().Match<FormQueryResponse>(r => r.CurrentRevision == null && !r.ValueChanged);
    }

    private static (FormQuery Query, KernelAssertionStep Step) Raise(string text = "Confirm the value") =>
        FormQuery.Raise(
            Guid.CreateVersion7(), TenantId.New(), CollectionInstanceId.New(), EnrollmentId.New(), "item.sbp", ObservationId.New(), 1, text, AssertionId.New(), "cra", At);
}
