using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.IntegrationTests.Safety;

/// <summary>
/// Safety persistence on PostgreSQL (#5464): every migration applies and matches the model, and a DSUR compiled,
/// updated and approved through the real host stores its instants as UTC <c>timestamp with time zone</c> and fills the
/// audit fields of each write from the verified caller. Builds on <see cref="DsurApprovalPostgresTests"/> (#5462).
/// </summary>
[Trait("Category", "Integration")]
public sealed class SafetyPersistencePostgresTests(SafetyPostgresApiFactory factory) : IClassFixture<SafetyPostgresApiFactory>
{
    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("pg-persist-compiler", "Sponsor Medical Monitor");
    private static readonly GatewayIdentity Editor = TestSecurityContext.Identity("pg-persist-editor", "sysadmin");
    private static readonly GatewayIdentity Approver = TestSecurityContext.Identity("pg-persist-approver", "Sponsor Medical Monitor");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] InstantColumns = ["CreatedAt", "ApprovedAt", "PeriodStart"];

    private const string StoredColumnsSql =
        "SELECT pg_typeof(\"CreatedAt\")::text, to_char(\"CreatedAt\" AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"'), "
        + "pg_typeof(\"ApprovedAt\")::text, to_char(\"ApprovedAt\" AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"'), "
        + "pg_typeof(\"PeriodStart\")::text, to_char(\"PeriodStart\" AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US\"Z\"') "
        + "FROM periodic_safety_reports WHERE \"Id\" = @id";

    /// <summary>The fixture applied every safety migration; none is pending and the model has no unmigrated change.</summary>
    [Fact]
    public async Task Migrations_AllApply_AndMatchTheModel()
    {
        factory.SkipIfUnavailable();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();

        var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

        applied.Should().Equal(db.Database.GetMigrations());
        applied.Should().Contain(m => m.EndsWith("_InitialCreate", StringComparison.Ordinal));
        (await db.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("a model change needs a migration");
    }

    /// <summary>
    /// Compile, update and approve on PostgreSQL with a fixed clock. The report's instants are stored as
    /// <c>timestamp with time zone</c> and read back as the same UTC instants, including period bounds given with
    /// +02:00 and -05:00 offsets (which PostgreSQL refused with a 500 before the engine normalised them, #5464). Each
    /// write carries the verified caller, its reason and the next version index, and the audit entries name the caller
    /// of each write at the clock's time in UTC.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CompileUpdateApprove_StoresUtcInstantsAndTheVerifiedCallerOfEachWrite()
    {
        factory.SkipIfUnavailable();
        // The clock starts at the real time because the host checks each gateway signature against it, and holds whole
        // microseconds because PostgreSQL stores no finer.
        var now = SystemClock.Instance.UtcNow;
        var clock = new FakeClock(new DateTimeOffset(now.UtcTicks - (now.UtcTicks % 10), TimeSpan.Zero));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(clock);
        }));
        var compiledAt = clock.UtcNow;

        string id;
        using (var client = host.CreateSignedClient(Compiler))
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/safety/dsur/compile",
                new
                {
                    compoundName = "Cadence-Compound-UTC",
                    studyId = "STUDY-UTC",
                    periodStart = "2025-01-01T00:00:00+02:00",
                    periodEnd = "2025-12-31T23:59:59Z",
                    reasonForChange = "Annual DSUR compilation",
                },
                Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            id = (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.Id;
        }

        var updatedAt = clock.Advance(TimeSpan.FromMilliseconds(1500));
        using (var client = host.CreateSignedClient(Editor))
        {
            using var response = await client.PutAsJsonAsync(
                $"/api/v1/safety/dsur/{id}", new { studyId = "STUDY-UTC-2", periodEnd = "2025-12-31T18:59:59-05:00", reasonForChange = "Corrected study identifier" }, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        var approvedAt = clock.Advance(TimeSpan.FromMilliseconds(1500));
        using (var client = host.CreateSignedClient(Approver))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/safety/dsur/{id}/approve")
            {
                Content = JsonContent.Create(new { reasonForChange = "DSUR medical review complete" }),
            };
            request.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(
                    Approver.UserId, DsurEndpoints.ApproveAction(id), DsurEndpoints.ApproveSemanticAction, issuedAt: clock.UtcNow));
            using var response = await client.SendAsync(request, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        }

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();

        var record = await db.PeriodicSafetyReports.AsNoTracking().SingleAsync(r => r.Id == id, Ct);
        record.CreatedAt.Should().Be(compiledAt);
        record.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        record.ApprovedAt.Should().Be(approvedAt);
        record.ApprovedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
        record.PeriodStart.Should().Be(new DateTimeOffset(2024, 12, 31, 22, 0, 0, TimeSpan.Zero));
        record.PeriodStart.Offset.Should().Be(TimeSpan.Zero);
        record.PeriodEnd.Should().Be(new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero));
        record.StudyId.Should().Be("STUDY-UTC-2");
        record.CreatedBy.Should().Be(Compiler.UserId);
        record.ApprovedByUserId.Should().Be(Approver.UserId);
        record.ReasonForChange.Should().Be("DSUR medical review complete");
        record.VersionIndex.Should().Be(3, "compiled at 1, updated to 2, approved to 3");

        var columns = await StoredColumnsAsync(db, id);
        columns.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["CreatedAt"] = "timestamp with time zone|" + Iso(compiledAt),
            ["ApprovedAt"] = "timestamp with time zone|" + Iso(approvedAt),
            ["PeriodStart"] = "timestamp with time zone|2024-12-31T22:00:00.000000Z",
        });

        var entries = await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == DsurCompilerEngine.AuditEntityName && e.EntityId == id)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(Ct);
        entries.Select(e => (e.Action, e.UserId, e.ReasonForChange, e.Timestamp)).Should().Equal(
            (DsurCompilerEngine.CompiledAuditAction, Compiler.UserId, "Annual DSUR compilation", compiledAt.UtcDateTime),
            (DsurCompilerEngine.UpdatedAuditAction, Editor.UserId, "Corrected study identifier", updatedAt.UtcDateTime),
            (DsurCompilerEngine.ApprovedAuditAction, Approver.UserId, "DSUR medical review complete", approvedAt.UtcDateTime));
        entries.Should().OnlyContain(e => e.Timestamp.Kind == DateTimeKind.Utc && !string.IsNullOrEmpty(e.BlockHash));
    }

    private static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The PostgreSQL type of each instant column and its stored value rendered in UTC, read past EF Core.</summary>
    private static async Task<Dictionary<string, string>> StoredColumnsAsync(SafetyDbContext db, string id)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(Ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = StoredColumnsSql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < InstantColumns.Length; i++)
            {
                result[InstantColumns[i]] = $"{reader.GetString(2 * i)}|{reader.GetString((2 * i) + 1)}";
            }

            return result;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
