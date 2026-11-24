using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Audit;
using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.TestSupport.Database;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// Applies every service's migrations the way a deployed service does (<c>AddCadencePostgresDbContext</c> plus
/// <see cref="CadenceMigrationHostedService{TContext}"/>) into one shared database, and checks that each service's
/// tables and migration history land in its own schema (ADR-2247). Needs <c>CADENCE_TEST_POSTGRES</c> or
/// <c>CADENCE_TEST_DOCKER=1</c>; without either the tests report as skipped.
/// </summary>
public sealed class ServiceSchemaPostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    public static IEnumerable<TheoryDataRow<string>> Factories() => ServiceDbContextFactories.All.Select(t => new TheoryDataRow<string>(t.AssemblyQualifiedName!));

    [Theory]
    [MemberData(nameof(Factories))]
    public async Task ServiceMigrations_ApplyIntoTheSharedDatabase_InsideTheServicesOwnSchema(string factoryTypeName)
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        Type factoryType = Type.GetType(factoryTypeName, throwOnError: true)!;
        string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
        Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;

        var migrate = typeof(ServiceSchemaPostgresTests)
            .GetMethod(nameof(MigrateLikeADeployedServiceAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);
        var expectedTables = await (Task<IReadOnlyList<string>>)migrate.Invoke(null, [database.ConnectionString!, schema, ct])!;

        expectedTables.Should().NotBeEmpty();
        var tablesInSchema = await TablesInSchemaAsync(schema, ct);
        tablesInSchema.Should().Contain("__EFMigrationsHistory", "the migration history belongs to the module's schema");
        tablesInSchema.Should().Contain(expectedTables, $"every table in {contextType.Name}'s model is created in \"{schema}\"");

        (await TablesInSchemaAsync("public", ct)).Should().NotContain(expectedTables,
            "a module's tables must never land in the shared public schema");
    }

    private static async Task<IReadOnlyList<string>> MigrateLikeADeployedServiceAsync<TContext>(
        string connectionString, string schema, CancellationToken ct)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = connectionString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        // Payments and eConsent contexts take the field encryptor (ADR-2494).
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(ct);

        await using var scope = app.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        (await context.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();

        return [.. context.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && e.GetViewName() is null && e.GetSchema() is null)
            .Select(e => e.GetTableName()!)
            .Distinct()];
    }

    private async Task<IReadOnlyList<string>> TablesInSchemaAsync(string schema, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema AND table_type = 'BASE TABLE'",
            connection);
        command.Parameters.AddWithValue("schema", schema);

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task AllStandaloneSchemas_InstallTemporalAndInsertOnlyTriggers()
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        foreach (Type factoryType in ServiceDbContextFactories.All)
        {
            string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
            Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;

            var migrate = typeof(ServiceSchemaPostgresTests)
                .GetMethod(nameof(MigrateLikeADeployedServiceAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contextType);
            await (Task)migrate.Invoke(null, [database.ConnectionString!, schema, ct])!;
        }

        await using var verifyConn = new NpgsqlConnection(database.ConnectionString);
        await verifyConn.OpenAsync(ct);

        string[] expectedBitemporalTables =
        [
            "ctms.ctms_delegation_periods",
            "ctms.doa_delegation_periods",
            "edc.site_activation_periods",
            "org.delegation_of_authority_periods",
            "knowledge.delegation_record_periods",
        ];

        foreach (string qualified in expectedBitemporalTables)
        {
            string[] parts = qualified.Split('.');
            string expectedRowTrigger = StandaloneTemporalSql.RowTriggerName(parts[1]);

            await using var trigCmd = verifyConn.CreateCommand();
            trigCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND t.tgname = @trigger;
                """;
            trigCmd.Parameters.AddWithValue("schema", parts[0]);
            trigCmd.Parameters.AddWithValue("table", parts[1]);
            trigCmd.Parameters.AddWithValue("trigger", expectedRowTrigger);
            long trigCount = (long)(await trigCmd.ExecuteScalarAsync(ct))!;
            trigCount.Should().Be(1L, $"{qualified} must install bitemporal trigger {expectedRowTrigger}");

            await using var exclCmd = verifyConn.CreateCommand();
            exclCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND con.contype = 'x';
                """;
            exclCmd.Parameters.AddWithValue("schema", parts[0]);
            exclCmd.Parameters.AddWithValue("table", parts[1]);
            long exclCount = (long)(await exclCmd.ExecuteScalarAsync(ct))!;
            exclCount.Should().Be(1L, $"{qualified} must enforce a GiST exclusion constraint");
        }

        string[] sampleInsertOnlyTables =
        [
            "payments.payments_participant_transactions",
            "tickets.ticket_revisions",
            "tickets.ticket_audit_logs",
            "safety.periodic_safety_report_revisions",
            "safety.safety_narrative_revisions",
            "etmf.tmf_document_revisions",
            "econsent.consent_document_revisions",
            "eisf.eisf_document_revisions",
            "knowledge.knowledge_article_versions",
        ];

        foreach (string qualified in sampleInsertOnlyTables)
        {
            string[] parts = qualified.Split('.');
            string expectedTrigger = StandaloneInsertOnlySql.RowTriggerName(parts[1]);

            await using var ioCmd = verifyConn.CreateCommand();
            ioCmd.CommandText = """
                SELECT COUNT(*)
                FROM pg_trigger t
                JOIN pg_class c ON c.oid = t.tgrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = @schema AND c.relname = @table AND t.tgname = @trigger;
                """;
            ioCmd.Parameters.AddWithValue("schema", parts[0]);
            ioCmd.Parameters.AddWithValue("table", parts[1]);
            ioCmd.Parameters.AddWithValue("trigger", expectedTrigger);
            long ioCount = (long)(await ioCmd.ExecuteScalarAsync(ct))!;
            ioCount.Should().Be(1L, $"{qualified} must install insert-only trigger {expectedTrigger}");
        }
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("ADR-2446")]
    [GxPRequirement("PRD-SYS-001")]
    [GxPRequirement("PRD-SYS-003")]
    public async Task AllStandaloneDbContexts_WriteVerifiableAuditChains_FlushStatefulOutbox_AndEnforceKa001Immutability()
    {
        database.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        int contextsVerified = 0;
        var auditTables = new List<(string Schema, string Table, Guid TenantId)>();

        foreach (Type factoryType in ServiceDbContextFactories.All)
        {
            string schema = (string)factoryType.GetField("Schema")!.GetRawConstantValue()!;
            Type contextType = factoryType.GetMethod(nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!.ReturnType;
            if (!contextType.FullName!.StartsWith("Cadence.Apps.", StringComparison.Ordinal))
            {
                continue;
            }

            var verify = typeof(ServiceSchemaPostgresTests)
                .GetMethod(nameof(VerifyDefaultAuditChainAndOutboxForContextAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contextType);
            var (auditTableName, tenantId) = await (Task<(string, Guid)>)verify.Invoke(null, [database.ConnectionString!, schema, ct])!;
            auditTables.Add((schema, auditTableName, tenantId));
            contextsVerified++;
        }

        contextsVerified.Should().Be(15, "all 15 standalone DbContext compositions must write SHA-256 audit chains via default GxPAuditInterceptor");

        await using var conn = new NpgsqlConnection(database.ConnectionString);
        await conn.OpenAsync(ct);

        // 1. Every standalone schema's AuditLogEntries / audit_log_entries table rejects UPDATE, DELETE, and TRUNCATE with KA001.
        foreach (var (schema, table, tenantId) in auditTables)
        {
            string quoted = $"\"{schema}\".\"{table}\"";
            await AssertKa001Async(conn, $"UPDATE {quoted} SET \"Action\" = 'TAMPERED';", tenantId, ct);
            await AssertKa001Async(conn, $"DELETE FROM {quoted};", tenantId, ct);
            await AssertKa001Async(conn, $"TRUNCATE TABLE {quoted};", tenantId, ct);
        }

        // 2. Module-specific audit tables reject row-level UPDATE/DELETE (when populated) and statement-level TRUNCATE with KA001.
        Guid ctmsTenantId = auditTables.First(a => a.Schema == "ctms").TenantId;
        await AssertKa001Async(conn, "UPDATE \"ctms\".\"ctms_audit_logs\" SET \"Action\" = 'TAMPERED';", ctmsTenantId, ct);
        await AssertKa001Async(conn, "DELETE FROM \"ctms\".\"ctms_audit_logs\";", ctmsTenantId, ct);

        string[] moduleAuditTables =
        [
            "ctms.ctms_audit_logs",
            "econsent.consent_audit_logs",
            "eisf.isf_audit_logs",
            "etmf.tmf_audit_logs",
            "fileshare.file_audit_logs",
            "imaging.imaging_audit_logs",
            "interop.interop_audit_logs",
            "knowledge.knowledge_article_audit_logs",
            "notifications.notification_audit_logs",
            "org.org_audit_logs",
            "payments.payments_audit_logs",
            "quality.quality_audit_logs",
            "safety.safety_audit_logs",
            "tickets.ticket_audit_logs",
        ];

        foreach (string qualified in moduleAuditTables)
        {
            string[] parts = qualified.Split('.');
            string quoted = $"\"{parts[0]}\".\"{parts[1]}\"";
            await AssertKa001Async(conn, $"TRUNCATE TABLE {quoted};", ctmsTenantId, ct);
            await AssertInsertOnlyTriggersRegisteredAsync(conn, parts[0], parts[1], ct);
        }
    }

    private static async Task<(string AuditTableName, Guid TenantId)> VerifyDefaultAuditChainAndOutboxForContextAsync<TContext>(
        string connectionString,
        string schema,
        CancellationToken ct)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = connectionString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);
        builder.Services.AddCadenceInMemoryTenancy();
        builder.Services.RemoveAll<CurrentTenantAccessor>();
        builder.Services.AddSingleton<CurrentTenantAccessor>(sp =>
        {
            var accessor = new CurrentTenantAccessor(
                sp.GetRequiredService<ITenantCodeResolver>(),
                sp.GetService<IHttpContextAccessor>());
            accessor.UseNonHttpSeedingFallbackTenant(TestSecurityContext.TenantCode);
            return accessor;
        });

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(ct);

        string probeEntityName = $"GxPIqProbe_{typeof(TContext).Name}_{Guid.NewGuid():N}";
        string probeEntityId = Guid.NewGuid().ToString("N");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<IAuditActorScope>()
                .Begin("svc:gxp-iq-probe", "GxP IQ default audit interceptor verification");
            var db = scope.ServiceProvider.GetRequiredService<TContext>();

            AuditActions.Record(db, new AuditAction(
                probeEntityName,
                probeEntityId,
                "PROBE_CREATED",
                "svc:gxp-iq-probe",
                "Initial GxP IQ probe block",
                "{\"step\":1}"));
            await db.SaveChangesAsync(ct);

            AuditActions.Record(db, new AuditAction(
                probeEntityName,
                probeEntityId,
                "PROBE_VERIFIED",
                "svc:gxp-iq-probe",
                "Follow-up GxP IQ probe block",
                "{\"step\":2}"));
            await db.SaveChangesAsync(ct);

            if (db is CtmsDbContext ctmsDb)
            {
                string visitId = $"VISIT-OUTBOX-{Guid.NewGuid():N}"[..24];
                var now = SystemClock.Instance.UtcNow;
                ctmsDb.CtmsAuditLogs.Add(new CtmsAuditLog
                {
                    Timestamp = now,
                    UserId = "svc:gxp-iq-probe",
                    UserRole = "service",
                    Action = "IQ_PROBE",
                    Details = "Module audit immutability probe",
                    ReasonForChange = "Verify KA001 trigger on module audit table",
                });
                var visit = new CtmsMonitoringVisit(visitId)
                {
                    StudyId = "STUDY-OUTBOX-IQ",
                    SiteId = "SITE-OUTBOX-IQ",
                    CraId = "svc:gxp-iq-probe",
                    VisitType = "IMV",
                    ScheduledDate = now,
                    ReasonForChange = "Schedule visit for outbox verification",
                };
                ctmsDb.CtmsMonitoringVisits.Add(visit);
                await ctmsDb.SaveChangesAsync(ct);

                visit.Complete(now.AddHours(2), "Complete visit to emit StateTransitioned outbox event", "svc:gxp-iq-probe", now.AddHours(2), "cra");
                visit.OutboxMessages.Should().ContainSingle("StatefulAggregateRoot queues StateTransitioned in [NotMapped] OutboxMessages prior to SaveChangesAsync");
                await ctmsDb.SaveChangesAsync(ct);

                visit.OutboxMessages.Should().BeEmpty("GxPAuditInterceptor must drain OutboxMessages during SaveChangesAsync");
                bool flushed = await ctmsDb.Set<TransactionalOutboxMessage>()
                    .AsNoTracking()
                    .AnyAsync(
                        m => m.EventType == "CtmsMonitoringVisitStateTransitioned" && m.Payload.Contains(visitId),
                        ct);
                flushed.Should().BeTrue("StatefulAggregateRoot OutboxMessages must be persisted to transactional_outbox during SaveChangesAsync");
            }
        }

        await using var verifyScope = app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TContext>();
        var chainEntries = await verifyDb.Set<AuditLogEntry>()
            .AsNoTracking()
            .Where(e => e.EntityName == probeEntityName)
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.VersionIndex)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        chainEntries.Should().HaveCount(2, $"{typeof(TContext).Name} must persist AuditLogEntry blocks via default GxPAuditInterceptor");
        chainEntries[0].PredecessorHash.Should().BeEmpty();
        chainEntries[1].PredecessorHash.Should().Be(chainEntries[0].BlockHash);
        AuditLogChainVerifier.Verify(chainEntries)
            .Should().ContainSingle()
            .Which.Valid.Should().BeTrue($"{typeof(TContext).Name} SHA-256 audit hash chain must verify cleanly");

        Guid tenantId = (await verifyScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().GetCurrentTenantIdAsync(ct))!.Value;
        return (verifyDb.Model.FindEntityType(typeof(AuditLogEntry))!.GetTableName()!, tenantId);
    }

    private static async Task AssertInsertOnlyTriggersRegisteredAsync(
        NpgsqlConnection conn,
        string schema,
        string table,
        CancellationToken ct)
    {
        const string sql = """
            SELECT t.tgname
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @table AND NOT t.tgisinternal;
            """;

        var triggers = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            triggers.Add(reader.GetString(0));
        }

        triggers.Should().Contain(
            StandaloneInsertOnlySql.RowTriggerName(table),
            $"{schema}.{table} must register row-level BEFORE UPDATE OR DELETE KA001 trigger");
        triggers.Should().Contain(
            StandaloneInsertOnlySql.TruncateTriggerName(table),
            $"{schema}.{table} must register statement-level BEFORE TRUNCATE KA001 trigger");
    }

    private static async Task AssertKa001Async(NpgsqlConnection conn, string sql, Guid tenantId, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await using (var tenantCmd = new NpgsqlCommand("SELECT set_config('cadence.tenant_id', @tid, true), set_config('cadence.tenant_code', @tcode, true);", conn, tx))
            {
                tenantCmd.Parameters.AddWithValue("tid", tenantId.ToString("D"));
                tenantCmd.Parameters.AddWithValue("tcode", TestSecurityContext.TenantCode);
                await tenantCmd.ExecuteNonQueryAsync(ct);
            }

            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync(ct));
            ex.SqlState.Should().Be(StandaloneInsertOnlySql.ViolationSqlState, $"SQL '{sql}' must fail closed with KA001");
        }
        finally
        {
            await tx.RollbackAsync(ct);
        }
    }
}
