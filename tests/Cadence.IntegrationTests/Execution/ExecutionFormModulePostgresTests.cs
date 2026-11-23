using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Application.Events;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Kernel database plus the Execution module as the composition host wires it (ADR-2601): its migrations applied by the
/// schema owner and its tables granted to the application role, its command store and handlers, and the outbox relay
/// delivering committed kernel events to its consumer.
/// </summary>
public sealed class ExecutionFormModulePostgresFixture : KernelPostgresFixture
{
    protected override async ValueTask OnMigratedAsync(string connectionString)
    {
        var migrations = new ServiceCollection()
            .AddModuleMigrations<ExecutionModuleDbContext>(
                ExecutionModuleDbContext.Module, ExecutionModuleDbContext.Schema, o => new ExecutionModuleDbContext(o), ExecutionModuleDatabase.GrantAppRoleSql)
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
        services.RemoveAll<IKernelEventPublisher>();
        services
            .AddKernelInProcessEvents()
            .AddCadenceExecutionModule(ConnectionString!);
    }

    /// <summary>The Execution form instance with <paramref name="id"/>, read as the superuser past Row-Level Security.</summary>
    public async Task<FormInstanceRow?> ReadFormAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT tenant_id, enrollment_id, collection_spec_id, planned_activity_id, item_order, created_by, version_index
            FROM {ExecutionModuleDbContext.Schema}.{ExecutionModuleDbContext.FormInstancesTable} WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        string? visit = await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3);
        var itemOrder = await reader.GetFieldValueAsync<string[]>(4, ct);
        return new FormInstanceRow(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), visit, itemOrder, reader.GetString(5), reader.GetInt64(6));
    }

    /// <summary>The Execution query with <paramref name="id"/>, read as the superuser past Row-Level Security.</summary>
    public Task<FormQuery?> ReadQueryAsync(Guid id, CancellationToken ct) =>
        ReadQueryAsync(FormQueryId.FromGuid(id), ct);

    /// <summary>The Execution query with <paramref name="id"/>, read as the superuser past Row-Level Security.</summary>
    public async Task<FormQuery?> ReadQueryAsync(FormQueryId id, CancellationToken ct)
    {
        var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>().UseNpgsql(AdminConnectionString).Options;
        await using var db = new ExecutionModuleDbContext(options);
        return await db.FormQueries.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id, ct);
    }

    /// <summary>How many Execution form instances the module holds for <paramref name="tenant"/>, read as the superuser.</summary>
    public async Task<long> CountFormsAsync(TenantId tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM {ExecutionModuleDbContext.Schema}.{ExecutionModuleDbContext.FormInstancesTable} WHERE tenant_id = @tenant",
            connection);
        command.Parameters.AddWithValue("tenant", tenant.Value);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    public async Task ExecuteAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>An Execution form instance read back as the superuser.</summary>
public sealed record FormInstanceRow(
    Guid TenantId, Guid EnrollmentId, string CollectionSpecId, string? PlannedActivityId, string[] ItemOrder, string CreatedBy, long VersionIndex);

/// <summary>
/// The Execution module on the kernel command path, against a real PostgreSQL server as the application role (ADR-2601,
/// ADR-2622): when the kernel opens a form instance, the module opens its own form for it with the layout snapshotted from
/// the protocol version, as its service account and once in effect however often the event is delivered. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormModulePostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>
{
    private const string Reason = "Execution form test";

    /// <summary>
    /// Validates the saga's first step: the opened kernel form instance reaches the module, which opens an Execution form
    /// with the same id, enrollment, spec and visit, the items in the CollectionSpec's order, written as
    /// <c>svc:execution-forms</c> with the delivery's idempotency key.
    /// </summary>
    [Fact(DisplayName = "KernelFormOpened_OpensExecutionFormWithTheSpecsItemOrder")]
    public async Task KernelFormOpenedOpensExecutionFormWithTheSpecsItemOrder()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await new KernelCollectionScenario(fixture).NewFormAsync(ct);

        await RelayAsync(ct);

        var row = await fixture.ReadFormAsync(form.Instance.Value, ct);
        row.Should().NotBeNull();
        row!.TenantId.Should().Be(form.Subject.Tenant.Value);
        row.EnrollmentId.Should().Be(form.Subject.Enrollment.Value);
        row.CollectionSpecId.Should().Be(KernelCollectionScenario.SpecId);
        row.PlannedActivityId.Should().Be(KernelCollectionScenario.VisitId);
        row.ItemOrder.Should().Equal(KernelCollectionScenario.Systolic, KernelCollectionScenario.Diastolic, KernelCollectionScenario.Comment);
        row.CreatedBy.Should().Be($"svc:{OpenFormOnCollectionOpened.Name}");
        row.VersionIndex.Should().Be(1);

        await using var db = fixture.CreateAdminContext();
        var opened = await db.DomainEvents.AsNoTracking()
            .SingleAsync(e => e.TenantId == form.Subject.Tenant && e.Type == nameof(CollectionInstanceOpened), ct);
        var activity = await db.ProvenanceActivities.AsNoTracking()
            .SingleAsync(a => a.TenantId == form.Subject.Tenant && a.CommandType == nameof(OpenFormInstance), ct);
        activity.ActorId.Should().Be($"svc:{OpenFormOnCollectionOpened.Name}");
        activity.IdempotencyKey.Should().Be($"{OpenFormOnCollectionOpened.Name}:{opened.EventId:D}");
        activity.CausationId.Should().Be(opened.EventId);
    }

    /// <summary>
    /// Validates that a redelivered event acts once in effect: the consumer's command replays its stored outcome, so the
    /// module still holds one form and one opening activity.
    /// </summary>
    [Fact(DisplayName = "RedeliveredKernelFormOpened_OpensOneExecutionForm")]
    public async Task RedeliveredKernelFormOpenedOpensOneExecutionForm()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await new KernelCollectionScenario(fixture).NewFormAsync(ct);
        await RelayAsync(ct);

        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{form.Subject.Tenant.Value:D}'", ct);
        await RelayAsync(ct);

        (await fixture.CountFormsAsync(form.Subject.Tenant, ct)).Should().Be(1);
        await using var db = fixture.CreateAdminContext();
        (await db.ProvenanceActivities.CountAsync(a => a.TenantId == form.Subject.Tenant && a.CommandType == nameof(OpenFormInstance), ct))
            .Should().Be(1);
    }

    /// <summary>
    /// Validates the refusals: a form instance the kernel does not hold, or holds for another enrollment, is
    /// <c>M.Execution.Form.CollectionInstanceUnknown</c> and writes nothing; opening a form the module already holds is
    /// <c>AlreadyExists</c>.
    /// </summary>
    [Fact(DisplayName = "OpenFormInstance_RefusesUnknownMismatchedAndExistingForms")]
    public async Task OpenFormInstanceRefusesUnknownMismatchedAndExistingForms()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var form = await new KernelCollectionScenario(fixture).NewFormAsync(ct);
        var tenant = form.Subject.Tenant;

        var unknown = await OpenAsync(tenant, CollectionInstanceId.New(), form.Subject.Enrollment, ct);
        var mismatched = await OpenAsync(tenant, form.Instance, EnrollmentId.New(), ct);
        await RelayAsync(ct);
        var existing = await OpenAsync(tenant, form.Instance, form.Subject.Enrollment, ct);

        unknown.Error!.Code.Should().Be(FormErrors.CollectionInstanceUnknown.Code);
        mismatched.Error!.Code.Should().Be(FormErrors.CollectionInstanceUnknown.Code);
        existing.Error!.Code.Should().Be(KernelErrors.AlreadyExists.Code);
        (await fixture.CountFormsAsync(tenant, ct)).Should().Be(1);
    }

    private Task<CommandResult<FormInstanceState>> OpenAsync(
        TenantId tenant, CollectionInstanceId instance, EnrollmentId enrollment, CancellationToken ct) =>
        fixture.Sender.SendAsync(
            new OpenFormInstance(tenant, instance, enrollment, $"open-{Guid.NewGuid():N}", Reason), cancellationToken: ct);

    private async Task RelayAsync(CancellationToken ct)
    {
        var relayed = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
        relayed.Failed.Should().Be(0);
    }
}
