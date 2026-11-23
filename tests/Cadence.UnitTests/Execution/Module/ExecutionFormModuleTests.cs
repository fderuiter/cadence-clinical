using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// The Execution module's form rules that need no database (ADR-2622): the layout a form opens with, how a form read
/// derives its order and completeness from the kernel record, enrollment form summary read-authority checks, and the
/// application role grant.
/// </summary>
public sealed class ExecutionFormModuleTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A layout that repeats an item, or names an empty or overlong one, is not a form layout.</summary>
    [Theory(DisplayName = "FormInstance_Open_RefusesALayoutThatIsNotOne")]
    [InlineData("sbp", "sbp")]
    [InlineData("sbp", " ")]
    public void FormInstanceOpenRefusesALayoutThatIsNotOne(string first, string second)
    {
        var open = () => Open([first, second]);
        var overlong = () => Open([new string('x', FormInstance.ElementIdMaxLength + 1)]);

        open.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
        Open(["sbp", "dbp"]).ItemOrder.Should().Equal("sbp", "dbp");
    }

    /// <summary>
    /// A form read lists the items in the module's layout, then any item the layout does not name; a required item is
    /// missing until it has a value, and a reasoned missing value counts as one.
    /// </summary>
    [Fact(DisplayName = "FormResponse_FollowsTheLayout_AndDerivesCompleteness")]
    public void FormResponseFollowsTheLayoutAndDerivesCompleteness()
    {
        var record = Record(
            [Item("sbp", required: true), Item("dbp", required: true), Item("comment"), Item("pulse", required: true)],
            Value("dbp", new MissingValue(NullFlavor.NotDone, "Cuff unavailable")));

        var response = FormResponse.From(record, ["comment", "dbp", "sbp"]);

        response.Items.Select(i => i.ItemId).Should().Equal("comment", "dbp", "sbp", "pulse");
        response.CollectionTitle.Should().Be("Vital signs");
        response.MissingRequired.Should().Equal("sbp", "pulse");
        response.Complete.Should().BeFalse();
        response.Items.Single(i => i.ItemId == "dbp").Revision.Should().Be(1);
        response.Items.Single(i => i.ItemId == "sbp").Revision.Should().Be(0);

        var complete = FormResponse.From(Record([Item("dbp", required: true)], Value("dbp", new MissingValue(NullFlavor.NotDone))), null);
        complete.Complete.Should().BeTrue();
        complete.MissingRequired.Should().BeEmpty();

        var unasked = FormResponse.From(Record([Item("dbp", required: true)], Value("dbp", new MissingValue(NullFlavor.NotAsked))), null);
        unasked.Complete.Should().BeFalse();
        unasked.MissingRequired.Should().Equal("dbp");
        unasked.Items.Single().Revision.Should().Be(1);
    }

    /// <summary>
    /// Listing an enrollment's forms validates enrollment_id (400), checks read access on the enrollment before touching
    /// records (403), and returns mapped form summaries when visible (200).
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_ListForEnrollment_EnforcesReadAuthority_AndMapsSummaries")]
    public async Task FormEndpointsListForEnrollmentEnforcesReadAuthorityAndMapsSummaries()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var enrollment = EnrollmentId.New();
        var summary = new CollectionInstanceSummary(
            CollectionInstanceId.New(),
            enrollment,
            ParticipantId.New(),
            StudyId.New(),
            StudySiteId.New(),
            ProtocolVersionId.New(),
            "cs.vitals",
            "act.week12",
            At,
            "crc@example.test",
            At,
            2,
            false,
            3,
            1,
            "Vital signs");
        var http = await ContextWithTenantAsync(tenant);
        var records = new StubCollectionInstanceRecords([summary]);

        var missing = await FormEndpoints.ListForEnrollmentAsync(null, http, records, new StubReadAccess(true), ct);
        missing.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var emptyGuid = await FormEndpoints.ListForEnrollmentAsync(Guid.Empty, http, records, new StubReadAccess(true), ct);
        emptyGuid.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        records.ListCalls.Should().Be(0);

        var access = new StubReadAccess(false);
        var denied = await FormEndpoints.ListForEnrollmentAsync(enrollment.Value, http, records, access, ct);
        denied.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        access.CheckedNode.Should().Be(NodeRef.For(enrollment));
        records.ListCalls.Should().Be(0);

        var allowed = await FormEndpoints.ListForEnrollmentAsync(enrollment.Value, http, records, new StubReadAccess(true), ct);
        var ok = allowed.Result.Should().BeOfType<Ok<IReadOnlyList<FormSummaryResponse>>>().Subject;
        ok.Value.Should().ContainSingle().Which.Should().Match<FormSummaryResponse>(s =>
            s.FormId == summary.CollectionInstanceId.Value
            && s.EnrollmentId == enrollment.Value
            && s.CollectionSpecId == "cs.vitals"
            && s.CollectionTitle == "Vital signs"
            && s.PlannedActivityId == "act.week12"
            && !s.Complete
            && s.ItemCount == 3
            && s.RecordedCount == 1
            && s.Version == 2);
        records.ListCalls.Should().Be(1);
    }

    /// <summary>
    /// The grant gives the role read, insert and update on the module's schema, delete only on the used signature tokens,
    /// and refuses a name that is not a role.
    /// </summary>
    [Fact(DisplayName = "ExecutionModuleDatabase_GrantAppRoleSql_GrantsTheModuleSchema_AndRefusesBadNames")]
    public void ExecutionModuleDatabaseGrantAppRoleSqlGrantsTheModuleSchemaAndRefusesBadNames()
    {
        var sql = ExecutionModuleDatabase.GrantAppRoleSql("cadence_app");
        var injected = () => ExecutionModuleDatabase.GrantAppRoleSql("app\"; DROP SCHEMA execution; --");

        sql.Should().Contain("GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA \"execution\" TO \"cadence_app\"")
            .And.Contain("REVOKE ALL ON \"execution\".\"__ef_migrations_history\" FROM \"cadence_app\"")
            .And.Contain("GRANT DELETE ON \"execution\".\"consumed_signature_tokens\" TO \"cadence_app\"");
        sql.Split('\n').Where(line => line.Contains("DELETE", StringComparison.Ordinal)).Should().ContainSingle();
        injected.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Previewing form values loads the target record once and passes that <see cref="CollectionInstanceRecord"/> directly
    /// to <see cref="ICollectionValidationService.PreviewFormAsync(TenantId, CollectionInstanceRecord, IReadOnlyDictionary{string, ObservationValue?}, DateOnly, CancellationToken)"/>.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_PreviewAsync_ForwardsLoadedRecordWithoutSecondFindAsync")]
    public async Task FormEndpointsPreviewAsyncForwardsLoadedRecordWithoutSecondFindAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var record = Record([Item("sbp", required: true), Item("dbp", required: true)]);
        var validation = new RecordingValidationService(
        [
            new CollectionPreviewWarning("vitals.pack", "R1", 1, ValidationSeverity.Warning, "cs.vitals", "sbp", "High systolic BP"),
        ]);
        var services = new ServiceCollection()
            .AddSingleton<ICollectionValidationService>(validation)
            .BuildServiceProvider();
        var http = await ContextWithTenantAsync(tenant, services);
        var records = new StubCollectionInstanceRecords([], [record]);
        var request = new PreviewFormValuesRequest([new PreviewFormItemValueRequest("sbp", new QuantityValue(185m, "mm[Hg]"))]);

        var response = await FormEndpoints.PreviewAsync(
            record.CollectionInstanceId.Value, request, http, records, new StubReadAccess(true), new FixedClock(At), ct);

        var ok = response.Result.Should().BeOfType<Ok<FormPreviewResponse>>().Subject;
        ok.Value!.FormId.Should().Be(record.CollectionInstanceId.Value);
        ok.Value.Warnings.Should().ContainSingle().Which.RuleId.Should().Be("R1");
        records.FindCalls.Should().Be(1);
        validation.PreviewRecordCalls.Should().Be(1);
        validation.PreviewByIdCalls.Should().Be(0);
    }

    /// <summary>
    /// Saving form values returns the hydrated <see cref="FormResponse"/> in the same response so callers do not issue a
    /// follow-up <c>GET /forms/{formId}</c> round-trip.
    /// </summary>
    [Fact(DisplayName = "FormEndpoints_SaveAsync_ReturnsHydratedFormResponse")]
    public async Task FormEndpointsSaveAsyncReturnsHydratedFormResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var sbpObs = ObservationId.New();
        var initial = Record([Item("sbp", required: true)]);
        var updated = new CollectionInstanceRecord(
            initial.CollectionInstanceId,
            initial.EnrollmentId,
            initial.ParticipantId,
            initial.StudyId,
            initial.StudySiteId,
            initial.ProtocolVersionId,
            initial.CollectionSpecId,
            initial.PlannedActivityId,
            initial.CollectedAt,
            initial.OpenedBy,
            initial.OpenedAt,
            2,
            initial.Items,
            [new CollectedValueView("sbp", sbpObs, ConceptRef.New(), new QuantityValue(124m, "mm[Hg]"), At, 1, "crc@example.test", At)],
            [],
            [],
            [],
            "hash-v2",
            "Vital signs");
        var http = await ContextWithTenantAsync(tenant);
        http.Request.Headers[FormEndpoints.IdempotencyKeyHeader] = "idem-save-1";
        var records = new StubCollectionInstanceRecords([], [initial, updated]);
        var sender = new StubCommandSender(
            new CollectedValuesState(
                initial.CollectionInstanceId,
                2,
                [new CollectedValueState("sbp", sbpObs, new QuantityValue(124m, "mm[Hg]"), 1, true)]));
        var request = new SaveFormValuesRequest(
            [new FormValueRequest("sbp", new QuantityValue(124m, "mm[Hg]"), 0, null, null)]);

        var response = await FormEndpoints.SaveAsync(
            initial.CollectionInstanceId.Value,
            request,
            http,
            records,
            sender,
            new FixedChangeReason("Initial vitals entry"),
            ct,
            contexts: null,
            access: new StubReadAccess(true, canSign: true));

        var ok = response.Result.Should().BeOfType<Ok<FormSavedResponse>>().Subject;
        ok.Value!.Version.Should().Be(2);
        ok.Value.Values.Should().ContainSingle().Which.Revision.Should().Be(1);
        ok.Value.Form.Should().NotBeNull();
        ok.Value.Form!.Version.Should().Be(2);
        ok.Value.Form.Complete.Should().BeTrue();
        ok.Value.Form.CanSign.Should().BeTrue();
        ok.Value.Form.Items.Should().ContainSingle().Which.Revision.Should().Be(1);
    }

    private static async Task<HttpContext> ContextWithTenantAsync(TenantId tenant, IServiceProvider? services = null)
    {
        var http = new DefaultHttpContext();
        if (services is not null)
        {
            http.RequestServices = services;
        }

        var filter = new KernelTenantFilter(new FixedTenantResolver(tenant));
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), static _ => ValueTask.FromResult<object?>(null));
        return http;
    }

    private static FormInstance Open(string[] layout) =>
        FormInstance.Open(
            CollectionInstanceId.New(), TenantId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, layout);

    private static CollectionItem Item(string id, bool required = false) => new(id, ConceptRef.New(), Required: required);

    private static CollectedValueView Value(string item, ObservationValue value) =>
        new(item, ObservationId.New(), ConceptRef.New(), value, At, 1, "crc@example.test", At);

    private static CollectionInstanceRecord Record(CollectionItem[] items, params CollectedValueView[] values) =>
        new(
            CollectionInstanceId.New(), EnrollmentId.New(), ParticipantId.New(), StudyId.New(), StudySiteId.New(), ProtocolVersionId.New(),
            "cs.vitals", "act.week12", At, "crc@example.test", At, 1 + values.Length, items, values, [], [], [], null, "Vital signs");

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(KernelTenantResolution.Resolved(tenant));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedChangeReason(string reason) : IKernelChangeReason
    {
        public string? Read(HttpContext context) => reason;
    }

    private sealed class StubReadAccess(bool canSee, bool canSign = false) : IKernelReadAccess
    {
        public NodeRef? CheckedNode { get; private set; }

        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken)
        {
            CheckedNode = node;
            return ValueTask.FromResult(canSee);
        }

        public ValueTask<bool> CanExerciseAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(canSign);
    }

    private sealed class StubCollectionInstanceRecords(
        IReadOnlyList<CollectionInstanceSummary> summaries,
        IReadOnlyList<CollectionInstanceRecord>? findQueue = null) : ICollectionInstanceRecords
    {
        private readonly Queue<CollectionInstanceRecord> _records = new(findQueue ?? []);
        public int FindCalls { get; private set; }
        public int ListCalls { get; private set; }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult(_records.Count > 0 ? _records.Dequeue() : null);
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(
            TenantId tenant,
            EnrollmentId enrollment,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult(summaries);
        }
    }

    private sealed class RecordingValidationService(IReadOnlyList<CollectionPreviewWarning> warnings) : ICollectionValidationService
    {
        public int PreviewByIdCalls { get; private set; }
        public int PreviewRecordCalls { get; private set; }

        public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
            TenantId tenant,
            CollectionInstanceId formId,
            IReadOnlyDictionary<string, ObservationValue?> draftValues,
            DateOnly asOf,
            CancellationToken cancellationToken = default)
        {
            PreviewByIdCalls++;
            return Task.FromResult(warnings);
        }

        public Task<IReadOnlyList<CollectionPreviewWarning>> PreviewFormAsync(
            TenantId tenant,
            CollectionInstanceRecord target,
            IReadOnlyDictionary<string, ObservationValue?> draftValues,
            DateOnly asOf,
            CancellationToken cancellationToken = default)
        {
            PreviewRecordCalls++;
            return Task.FromResult(warnings);
        }

        public Task<CollectionRulePackPreviewResult> PreviewRulePackAsync(
            TenantId tenant, StudyId studyId, LibraryItemId rulePackId, DateOnly asOf, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CommandResult<CollectionValidationResult>> ValidateStudyCollectionsAsync(
            TenantId tenant, StudyId studyId, DateOnly asOf, DateTimeOffset evaluatedAt, string idempotencyKey, string reasonForChange,
            CommandMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubCommandSender(CollectedValuesState saved) : IKernelCommandSender
    {
        public Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default) =>
            command is RecordCollectedValues
                ? Task.FromResult(CommandResult<TResult>.Success((TResult)(object)saved))
                : throw new NotSupportedException();
    }
}
