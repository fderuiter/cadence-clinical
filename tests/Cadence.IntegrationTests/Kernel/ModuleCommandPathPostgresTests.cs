using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Kernel database plus a test module with its own schema, tenant-isolated table and command store, wired the way a
/// module registers itself with the composition host (ADR-2600): <c>AddModuleCommandStore</c> and
/// <c>AddCommandHandler</c>. The module's table is granted to the same application role, never the owner.
/// </summary>
public sealed class ModuleCommandPathPostgresFixture : KernelPostgresFixture
{
    /// <summary>Lets a test act between a handler's read and the commit, as a concurrent writer would.</summary>
    public TestModuleProbe Probe { get; } = new();

    /// <summary>The module's command store, as the pipeline gets it.</summary>
    public IModuleCommandStore Store => Scope().GetServices<IModuleCommandStore>().Single();

    protected override async ValueTask OnMigratedAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        var ddl = TestModuleDbContext.CreateTableSql
            + ModuleTenantIsolationSql.EnableForTenantColumn(TestModuleDbContext.Schema, TestModuleDbContext.Table)
            + $"""

                GRANT USAGE ON SCHEMA {TestModuleDbContext.Schema} TO "{AppRole}";
                GRANT SELECT, INSERT, UPDATE ON {TestModuleDbContext.Schema}.{TestModuleDbContext.Table} TO "{AppRole}";
                """;
        await using var command = new NpgsqlCommand(ddl, connection);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services
            .AddSingleton(Probe)
            .AddModuleCommandStore<TestModuleDbContext>(TestModuleDbContext.Module, options => new TestModuleDbContext(options))
            .AddCommandHandler<CreateTestNote, Guid, CreateTestNoteHandler>()
            .AddCommandHandler<EditTestNote, long, EditTestNoteHandler>();

    /// <summary>A superuser connection to the test database's module table, which bypasses Row-Level Security.</summary>
    public async Task<TestNoteRow?> ReadNoteAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            $"SELECT tenant_id, text, created_by, reason_for_change, version_index FROM {TestModuleDbContext.Schema}.{TestModuleDbContext.Table} WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new TestNoteRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4))
            : null;
    }

    public async Task ExecuteAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>A module row read back as the superuser.</summary>
public sealed record TestNoteRow(Guid TenantId, string Text, string CreatedBy, string ReasonForChange, long VersionIndex);

/// <summary>A hook the edit handler calls after reading the note and before the commit.</summary>
public sealed class TestModuleProbe
{
    public Func<Guid, Task>? AfterRead { get; set; }
}

/// <summary>The test module's one aggregate.</summary>
public sealed class TestNote : AggregateRoot<Guid>
{
    private TestNote()
    {
    }

    public TestNote(Guid id, TenantId tenantId, string text)
        : base(id)
    {
        TenantId = tenantId;
        Text = text;
    }

    public TenantId TenantId { get; private set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>The test module's context: its own schema, saved only by the kernel command pipeline.</summary>
public sealed class TestModuleDbContext(DbContextOptions<TestModuleDbContext> options) : ModuleDbContext(options)
{
    public const string Module = "test_module";
    public const string Schema = "test_module";
    public const string Table = "notes";

    public const string CreateTableSql = """
        CREATE SCHEMA test_module;
        CREATE TABLE test_module.notes (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            text text NOT NULL,
            created_at timestamp with time zone NOT NULL,
            created_by varchar(255) NOT NULL,
            reason_for_change varchar(1000) NOT NULL,
            version_index bigint NOT NULL
        );

        """;

    public DbSet<TestNote> Notes => Set<TestNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<TestNote>(note =>
        {
            note.ToTable(Table);
            note.HasKey(n => n.Id);
            note.Property(n => n.Id).HasColumnName("id").ValueGeneratedNever();
            note.Property(n => n.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            note.Property(n => n.Text).HasColumnName("text");
            note.HasCommandAuditColumns();
            note.Property(n => n.CreatedAt).HasColumnName("created_at");
            note.Property(n => n.CreatedBy).HasColumnName("created_by");
            note.Property(n => n.ReasonForChange).HasColumnName("reason_for_change");
            note.Property(n => n.VersionIndex).HasColumnName("version_index");
        });
    }
}

/// <summary>Creates a note.</summary>
public sealed record CreateTestNote(TenantId TenantId, Guid NoteId, string Text, string IdempotencyKey, string ReasonForChange)
    : ICommand<Guid>;

/// <summary>Replaces a note's text; the result is the new version.</summary>
public sealed record EditTestNote(TenantId TenantId, Guid NoteId, string Text, string IdempotencyKey, string ReasonForChange)
    : ICommand<long>;

/// <summary>Raised when a note is created.</summary>
[KernelEvent("test_module.NoteCreated", 1)]
public sealed record TestNoteCreated(Guid NoteId);

internal sealed class CreateTestNoteHandler : ICommandHandler<CreateTestNote, Guid>
{
    public async Task<CommandResult<Guid>> HandleAsync(CreateTestNote command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<TestNote>(command.NoteId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        context.Work.Add(new TestNote(command.NoteId, context.TenantId, command.Text));
        context.Work.Raise(new TestNoteCreated(command.NoteId));
        return CommandResult<Guid>.Success(command.NoteId);
    }
}

internal sealed class EditTestNoteHandler(TestModuleProbe probe) : ICommandHandler<EditTestNote, long>
{
    public async Task<CommandResult<long>> HandleAsync(EditTestNote command, ICommandContext context, CancellationToken cancellationToken)
    {
        var note = await context.Work.FindAsync<TestNote>(command.NoteId, cancellationToken).ConfigureAwait(false);
        if (note is null)
        {
            return KernelErrors.NotFound;
        }

        if (probe.AfterRead is { } afterRead)
        {
            await afterRead(command.NoteId).ConfigureAwait(false);
        }

        note.Text = command.Text;
        return CommandResult<long>.Success(note.VersionIndex + 1);
    }
}

/// <summary>
/// A module writes through the kernel command path against a real PostgreSQL server as the application role
/// (ADR-2600): its row, the kernel audit link, provenance, events, outbox and idempotency commit or fail together; a
/// stale version writes nothing; Row-Level Security scopes module rows to the command's tenant; and the module context
/// refuses audited writes outside a command. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c>
/// is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ModuleCommandPathPostgresTests(ModuleCommandPathPostgresFixture fixture)
    : IClassFixture<ModuleCommandPathPostgresFixture>, IDisposable
{
    private const string Reason = "Module integration test";

    public static TheoryData<string> CommitStages => new(Enum.GetNames<CommitStage>());

    public void Dispose()
    {
        if (fixture.ConnectionString is null)
        {
            return;
        }

        fixture.UnitOfWork.FaultInjector = null;
        fixture.Probe.AfterRead = null;
    }

    /// <summary>
    /// Validates that a module command stamps the four audit fields on the module row from the kernel actor and reason,
    /// and commits one provenance activity, one event with its outbox row, one link on the tenant's kernel audit chain
    /// with the namespaced aggregate type, and the idempotency record.
    /// </summary>
    [Fact(DisplayName = "ModuleCommand_Commits_ModuleRowWithKernelAuditProvenanceEventAndIdempotency")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ModuleCommandCommitsModuleRowWithKernelAuditProvenanceEventAndIdempotency()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var create = NewNote(tenant);

        var result = await fixture.Sender.SendAsync(create, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        var row = await fixture.ReadNoteAsync(create.NoteId, ct);
        row.Should().Be(new TestNoteRow(tenant.Value, create.Text, KernelPostgresFixture.ActorId, Reason, 1));

        await using var db = fixture.CreateAdminContext();
        var audit = await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenant).OrderBy(a => a.Sequence).ToListAsync(ct);
        audit.Should().HaveCount(2, "the tenant's provisioning is link 1 and the module record is link 2");
        audit[1].AggregateType.Should().Be("test_module.TestNote");
        audit[1].AggregateId.Should().Be(create.NoteId.ToString("D"));
        audit[1].Action.Should().Be("Created");
        audit[1].PredecessorHash.Should().Be(audit[0].Hash);

        var activity = await db.ProvenanceActivities.AsNoTracking().SingleAsync(p => p.IdempotencyKey == create.IdempotencyKey, ct);
        activity.AggregateType.Should().Be("test_module.TestNote");
        (await db.DomainEvents.AsNoTracking().CountAsync(e => e.TenantId == tenant && e.Type == "test_module.NoteCreated", ct)).Should().Be(1);
        (await db.OutboxMessages.AsNoTracking().CountAsync(o => o.TenantId == tenant && o.Type == "test_module.NoteCreated", ct)).Should().Be(1);
        (await db.IdempotencyRecords.AsNoTracking().CountAsync(r => r.TenantId == tenant && r.IdempotencyKey == create.IdempotencyKey, ct)).Should().Be(1);
    }

    /// <summary>Validates K-TX-3 for a module command: a replay returns the stored outcome and writes nothing more.</summary>
    [Fact(DisplayName = "ModuleCommand_Replay_ReturnsStoredOutcome_AndWritesNothing")]
    public async Task ModuleCommandReplayReturnsStoredOutcomeAndWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var create = NewNote(tenant);

        (await fixture.Sender.SendAsync(create, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var replay = await fixture.Sender.SendAsync(create, cancellationToken: ct);

        replay.Replayed.Should().BeTrue();
        replay.Value.Should().Be(create.NoteId);
        (await AuditLinksAsync(tenant, ct)).Should().Be(2);
    }

    /// <summary>
    /// Validates K-TX-2 across the kernel and a module: a failure at any commit stage, including after the module's
    /// rows are written, leaves neither the module row nor any kernel row behind, and the chain head stays put.
    /// </summary>
    [Theory(DisplayName = "ModuleCommand_FailingAtAnyStage_PersistsNeitherModuleNorKernelRows")]
    [MemberData(nameof(CommitStages))]
    public async Task ModuleCommandFailingAtAnyStagePersistsNeitherModuleNorKernelRows(string stageName)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var stage = Enum.Parse<CommitStage>(stageName);
        fixture.UnitOfWork.FaultInjector = s =>
        {
            if (s == stage)
            {
                throw new InvalidOperationException($"Injected fault at {stage}.");
            }
        };
        var create = NewNote(tenant);

        var act = () => fixture.Sender.SendAsync(create, cancellationToken: ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"Injected fault at {stage}.");
        (await fixture.ReadNoteAsync(create.NoteId, ct)).Should().BeNull();
        (await AuditLinksAsync(tenant, ct)).Should().Be(1);
        await using var db = fixture.CreateAdminContext();
        (await db.ProvenanceActivities.AnyAsync(p => p.IdempotencyKey == create.IdempotencyKey, ct)).Should().BeFalse();
        (await db.IdempotencyRecords.AnyAsync(r => r.IdempotencyKey == create.IdempotencyKey, ct)).Should().BeFalse();
        (await db.OutboxMessages.AnyAsync(o => o.TenantId == tenant && o.Type == "test_module.NoteCreated", ct)).Should().BeFalse();
        (await db.AuditChainHeads.AsNoTracking().SingleAsync(h => h.TenantId == tenant, ct)).LastSequence.Should().Be(1);
    }

    /// <summary>
    /// Validates that an update of a module record increments its version, keeps its creation stamp, and audits the
    /// before and after values of the changed field on the kernel chain.
    /// </summary>
    [Fact(DisplayName = "ModuleCommand_Update_IncrementsVersion_AndAuditsBeforeAndAfter")]
    public async Task ModuleCommandUpdateIncrementsVersionAndAuditsBeforeAndAfter()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var create = NewNote(tenant);
        (await fixture.Sender.SendAsync(create, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var edit = NewEdit(tenant, create.NoteId, "Second text");
        var result = await fixture.Sender.SendAsync(edit, cancellationToken: ct);

        result.Value.Should().Be(2);
        var row = await fixture.ReadNoteAsync(create.NoteId, ct);
        row.Should().Be(new TestNoteRow(tenant.Value, "Second text", KernelPostgresFixture.ActorId, Reason, 2));
        await using var db = fixture.CreateAdminContext();
        var link = await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenant).OrderBy(a => a.Sequence).LastAsync(ct);
        link.Action.Should().Be("Updated");
        link.AggregateType.Should().Be("test_module.TestNote");
        using var changes = JsonDocument.Parse(link.Changes);
        changes.RootElement.GetProperty("before").GetProperty("Text").GetString().Should().Be(create.Text);
        changes.RootElement.GetProperty("after").GetProperty("Text").GetString().Should().Be("Second text");
    }

    /// <summary>
    /// Validates the optimistic version check on module rows: when the row changes between the handler's read and the
    /// commit, the command fails with <c>K.Aggregate.ConcurrencyConflict</c> and writes neither the module row nor a
    /// kernel audit link.
    /// </summary>
    [Fact(DisplayName = "ModuleCommand_StaleVersion_ReturnsConcurrencyConflict_AndWritesNothing")]
    public async Task ModuleCommandStaleVersionReturnsConcurrencyConflictAndWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var create = NewNote(tenant);
        (await fixture.Sender.SendAsync(create, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        fixture.Probe.AfterRead = id => fixture.ExecuteAdminAsync(
            $"UPDATE test_module.notes SET text = 'Concurrent', version_index = version_index + 1 WHERE id = '{id:D}'", ct);

        var result = await fixture.Sender.SendAsync(NewEdit(tenant, create.NoteId, "Lost update"), cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.ConcurrencyConflict);
        (await fixture.ReadNoteAsync(create.NoteId, ct))!.Text.Should().Be("Concurrent");
        (await AuditLinksAsync(tenant, ct)).Should().Be(2);
    }

    /// <summary>
    /// Validates K-ID-2 for module tables: a command in another tenant cannot read or change a module row, because the
    /// module context runs on the kernel's tenant-scoped connection under the same forced Row-Level Security.
    /// </summary>
    [Fact(DisplayName = "ModuleCommand_OtherTenant_CannotSeeOrChangeModuleRow")]
    public async Task ModuleCommandOtherTenantCannotSeeOrChangeModuleRow()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var owner = await ProvisionTenantAsync(ct);
        var other = await ProvisionTenantAsync(ct);
        var create = NewNote(owner);
        (await fixture.Sender.SendAsync(create, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var result = await fixture.Sender.SendAsync(NewEdit(other, create.NoteId, "Cross-tenant"), cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.NotFound);
        (await fixture.ReadNoteAsync(create.NoteId, ct))!.Text.Should().Be(create.Text);
        (await AuditLinksAsync(other, ct)).Should().Be(1);
    }

    /// <summary>
    /// Validates that a module context refuses to save an audited record outside the command pipeline, so a module
    /// cannot write a clinical record that bypasses the kernel audit chain (ADR-2600 §4.3).
    /// </summary>
    [Fact(DisplayName = "ModuleContext_SaveOutsideCommand_IsRefused")]
    public async Task ModuleContextSaveOutsideCommandIsRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await using var context = fixture.Store.Create(connection);
        var id = Guid.CreateVersion7();
        context.Add(new TestNote(id, tenant, "Bypass"));

        var act = () => context.SaveChangesAsync(ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("ADR-2600*");
        (await fixture.ReadNoteAsync(id, ct)).Should().BeNull();
    }

    private async Task<TenantId> ProvisionTenantAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var command = new ProvisionTenant(TenantId.New(), $"M-{suffix}", $"Module tenant {suffix}", $"key-{suffix}", Reason);
        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return command.TenantId;
    }

    private static CreateTestNote NewNote(TenantId tenant) =>
        new(tenant, Guid.CreateVersion7(), "First text", $"note-{Guid.NewGuid():N}", Reason);

    private static EditTestNote NewEdit(TenantId tenant, Guid id, string text) =>
        new(tenant, id, text, $"edit-{Guid.NewGuid():N}", Reason);

    private async Task<int> AuditLinksAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return await db.AuditEntries.CountAsync(a => a.TenantId == tenant, ct);
    }
}
