using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Apps.Tickets.Domain;
using Cadence.Apps.Tickets.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Database.Concurrency;
using Cadence.Database.Tenancy;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Database;

[Trait("Category", "Integration")]
public sealed class StandaloneConcurrencyAndWalletLocksPostgresTests(ThrowawayPostgresDatabase database)
    : IClassFixture<ThrowawayPostgresDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FixedTenantAccessor(Guid tenantId) : ICurrentTenantAccessor
    {
        public Guid? GetCurrentTenantId() => tenantId;

        public ValueTask<Guid?> GetCurrentTenantIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Guid?>(tenantId);

        public bool IsSystemSuppressed => false;

        public bool HasHttpRequestWithoutTenant => false;

        public bool IsCurrentTenantReadOnly => false;

        public bool IsHttpWriteExemptFromExpiry => false;

        public bool IsHttpProductFamilyExempt => false;

        public IReadOnlyList<string>? CurrentTenantProductFamilies => null;

        public TenantScopingCaps? CurrentTenantScoping => null;
    }

    private sealed class PostgresPaymentsHost(string connectionString)
        : WebApplicationFactory<Cadence.Apps.Payments.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseTestGatewaySecret().UseTestFieldProtectionKeys();
            builder.UseSetting("ConnectionStrings:cadence", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITenantCodeResolver>();
                services.AddSingleton<ITenantCodeResolver>(new DeterministicTenantCodeResolver());
            });
            base.ConfigureWebHost(builder);
        }
    }

    private static async Task MigrateSchemaAsync<TContext>(string connectionString, string schema, CancellationToken ct)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = connectionString;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        await using var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(ct);
    }

    [Fact(DisplayName = "WalletRowLocks_SerializeConcurrentPayouts_And_DeduplicateDiaryStipendRaces")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-PAY-003")]
    public async Task WalletRowLocksSerializeConcurrentPayoutsAndDeduplicateDiaryStipendRaces()
    {
        database.SkipIfUnavailable();

        await MigrateSchemaAsync<PaymentsDbContext>(database.ConnectionString!, "payments", Ct);
        string connString = CadenceDatabaseExtensions.WithModuleSchema(database.ConnectionString!, "payments");

        string tenantCode = "tenant-wallet-lock";
        Guid tenantId = DeterministicTenantCodeResolver.ComputeDeterministicGuid(tenantCode);
        var tenantAccessor = new FixedTenantAccessor(tenantId);
        var encryptor = TestFieldProtection.CreateEncryptor();

        DbContextOptions<PaymentsDbContext> BuildOptions()
        {
            var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
            optionsBuilder
                .UseNpgsql(connString)
                .UseCadenceStandaloneTenancy(tenantAccessor, isRelational: true)
                .AddInterceptors(
                    StandaloneTenantConnectionInterceptor.Instance,
                    StandaloneTenantSaveChangesInterceptor.Instance,
                    StandaloneConcurrencyTokenInterceptor.Instance);
            return optionsBuilder.Options;
        }

        string walletId = Guid.NewGuid().ToString();
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

        await using (var seedCtx = new PaymentsDbContext(BuildOptions(), encryptor))
        {
            seedCtx.ParticipantWallets.Add(new ParticipantWallet
            {
                Id = walletId,
                SubjectId = "SUBJ-LOCK-01",
                StudyId = "STUDY-PAY-01",
                SiteId = "SITE-01",
                Currency = "USD",
                CurrentBalance = 100.00m,
                LifetimeStipends = 100.00m,
                LifetimeDisbursements = 0m,
                Status = "ACTIVE",
                CreatedAt = now,
                CreatedBy = "seed",
                UpdatedAt = now,
                ReasonForChange = "Initial balance",
                VersionIndex = 1,
            });
            seedCtx.ParticipantStipendRules.Add(new ParticipantStipendRule
            {
                StudyId = "STUDY-PAY-01",
                VisitCode = "DIARY",
                VisitName = "Daily ePRO Diary",
                Amount = 25.00m,
                AntiCoercionCap = 500.00m,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "seed",
                UpdatedAt = now,
            });
            await seedCtx.SaveChangesAsync(Ct);
        }

        // 1. Fire two concurrent $75 payouts with DISTINCT idempotency keys under SELECT ... FOR UPDATE.
        // Exactly one succeeds (leaving $25.00) and the racing one observes $25.00 < $75.00 and rejects overdraft.
        async Task<bool> TryDisburseAsync(string idempotencyKey)
        {
            await using var ctx = new PaymentsDbContext(BuildOptions(), encryptor);
            return await ctx.ExecuteInTransactionAsync(async ct =>
            {
                var wallet = await ctx.ParticipantWallets
                    .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"Id\" = {walletId} FOR UPDATE")
                    .SingleAsync(ct);

                if (wallet.CurrentBalance < 75.00m)
                {
                    return false;
                }

                wallet.CurrentBalance -= 75.00m;
                wallet.LifetimeDisbursements += 75.00m;
                wallet.UpdatedAt = now;
                ctx.ParticipantDisbursements.Add(new ParticipantDisbursement
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = wallet.SubjectId,
                    Amount = 75.00m,
                    DisbursementType = "VIRTUAL_CARD",
                    ReferenceNumber = $"SIM-{idempotencyKey}",
                    IdempotencyKey = idempotencyKey,
                    Status = "DISPATCHED",
                    CreatedAt = now,
                    CreatedBy = "SUBJ-LOCK-01",
                });
                ctx.ParticipantTransactions.Add(new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = wallet.Id,
                    SubjectId = wallet.SubjectId,
                    StudyId = wallet.StudyId,
                    TransactionType = "DISBURSEMENT_DEBIT",
                    Amount = -75.00m,
                    BalanceAfter = wallet.CurrentBalance,
                    ReferenceId = $"DISB-{idempotencyKey}",
                    IdempotencyKey = idempotencyKey,
                    Description = "Concurrent payout",
                    CreatedAt = now,
                    CreatedBy = "SUBJ-LOCK-01",
                });
                await ctx.SaveChangesAsync(ct);
                return true;
            }, cancellationToken: Ct);
        }

        bool[] payoutOutcomes = await Task.WhenAll(
            TryDisburseAsync("key-payout-a"),
            TryDisburseAsync("key-payout-b"));

        payoutOutcomes.Count(succeeded => succeeded).Should().Be(1);

        // 2. Verify unique (WalletId, TransactionType, ReferenceId) and (WalletId, IdempotencyKey) indexes on ParticipantTransaction
        // while allowing paired REIMBURSEMENT_CREDIT + DISBURSEMENT_DEBIT sharing a claim ReferenceId.
        await using (var claimCtx = new PaymentsDbContext(BuildOptions(), encryptor))
        {
            string sharedClaimId = Guid.NewGuid().ToString();
            claimCtx.ParticipantTransactions.AddRange(
                new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = walletId,
                    SubjectId = "SUBJ-LOCK-01",
                    StudyId = "STUDY-PAY-01",
                    TransactionType = "REIMBURSEMENT_CREDIT",
                    Amount = 20.00m,
                    BalanceAfter = 45.00m,
                    ReferenceId = sharedClaimId,
                    Description = "Approved expense credit",
                    CreatedAt = now,
                    CreatedBy = "SUBJ-LOCK-01",
                },
                new ParticipantTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    WalletId = walletId,
                    SubjectId = "SUBJ-LOCK-01",
                    StudyId = "STUDY-PAY-01",
                    TransactionType = "DISBURSEMENT_DEBIT",
                    Amount = -20.00m,
                    BalanceAfter = 25.00m,
                    ReferenceId = sharedClaimId,
                    Description = "Auto disbursement debit",
                    CreatedAt = now,
                    CreatedBy = "SUBJ-LOCK-01",
                });
            await claimCtx.SaveChangesAsync(Ct);
        }

        // 3. Exercise the real HTTP /participant/me/diary-stipend endpoint concurrently for a brand-new subject
        // (testing both initial wallet creation uniqueness retry AND distinct Idempotency-Key deduplication on the same diary entry).
        await using (var paymentsHost = new PostgresPaymentsHost(database.ConnectionString!))
        {
            using var participantClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
                UserId: "SUBJ-RACE-NEW",
                Roles: ["subject"],
                SiteIds: ["SITE-01"],
                TenantId: tenantCode,
                StudyIds: ["STUDY-PAY-01"],
                ProductFamilies: TestSecurityContext.AllProductFamilies));

            async Task<SelfServiceDiaryStipendResponseDto> PostDiaryAsync(string distinctKey)
            {
                using var resp = await participantClient.PostAsJsonAsync(
                    "/api/v1/payments/participant/me/diary-stipend",
                    new
                    {
                        studyId = "STUDY-PAY-01",
                        siteId = "SITE-01",
                        diaryId = "vas-race-01",
                        sequenceNumber = 7,
                        idempotencyKey = distinctKey,
                    },
                    Ct);
                resp.StatusCode.Should().Be(HttpStatusCode.OK);
                return (await resp.Content.ReadFromJsonAsync<SelfServiceDiaryStipendResponseDto>(Ct))!;
            }

            var raceResponses = await Task.WhenAll(
                PostDiaryAsync("distinct-race-key-1"),
                PostDiaryAsync("distinct-race-key-2"));

            raceResponses.Count(r => r.Status == "CREDITED").Should().Be(1);
            raceResponses.Count(r => r.Status == "ALREADY_PROCESSED").Should().Be(1);

            // 4. Exercise concurrent HTTP self-service (/participant/me/disburse) and staff (/participant/wallets/{id}/disburse)
            // payouts with distinct keys against a $100 wallet: SELECT ... FOR UPDATE serializes them so one succeeds ($25 left)
            // and the racing overdraft fails with 400 BadRequest.
            string httpRaceWalletId = Guid.NewGuid().ToString();
            await using (var seedHttpCtx = new PaymentsDbContext(BuildOptions(), encryptor))
            {
                seedHttpCtx.ParticipantWallets.Add(new ParticipantWallet
                {
                    Id = httpRaceWalletId,
                    SubjectId = "SUBJ-HTTP-MIX",
                    StudyId = "STUDY-PAY-01",
                    SiteId = "SITE-01",
                    Currency = "USD",
                    CurrentBalance = 100.00m,
                    LifetimeStipends = 100.00m,
                    LifetimeDisbursements = 0m,
                    Status = "ACTIVE",
                    W9Status = "VERIFIED",
                    CreatedAt = now,
                    CreatedBy = "seed",
                    UpdatedAt = now,
                    VersionIndex = 1,
                });
                await seedHttpCtx.SaveChangesAsync(Ct);
            }

            using var mixSubjectClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
                UserId: "SUBJ-HTTP-MIX",
                Roles: ["subject"],
                SiteIds: ["SITE-01"],
                TenantId: tenantCode,
                StudyIds: ["STUDY-PAY-01"],
                ProductFamilies: TestSecurityContext.AllProductFamilies));

            using var staffFinanceClient = paymentsHost.CreateSignedClient(new GatewayIdentity(
                UserId: "finance-staff-01",
                Roles: ["Finance Officer", "sysadmin"],
                SiteIds: ["SITE-01"],
                TenantId: tenantCode,
                StudyIds: ["STUDY-PAY-01"],
                ProductFamilies: TestSecurityContext.AllProductFamilies));

            var mixedHttpOutcomes = await Task.WhenAll(
                mixSubjectClient.PostAsJsonAsync(
                    "/api/v1/payments/participant/me/disburse",
                    new
                    {
                        walletId = httpRaceWalletId,
                        disbursementType = "VIRTUAL_CARD",
                        amount = 75.00m,
                        idempotencyKey = "self-http-distinct-key",
                    },
                    Ct),
                staffFinanceClient.PostAsJsonAsync(
                    $"/api/v1/payments/participant/wallets/{httpRaceWalletId}/disburse",
                    new
                    {
                        disbursementType = "DIRECT_ACH",
                        amount = 75.00m,
                    },
                    Ct));

            try
            {
                mixedHttpOutcomes.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
                mixedHttpOutcomes.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(1);
            }
            finally
            {
                foreach (var resp in mixedHttpOutcomes)
                {
                    resp.Dispose();
                }
            }

            await using var verifyHttpMixCtx = new PaymentsDbContext(BuildOptions(), encryptor);
            var mixWallet = await verifyHttpMixCtx.ParticipantWallets.AsNoTracking().SingleAsync(w => w.Id == httpRaceWalletId, Ct);
            mixWallet.CurrentBalance.Should().Be(25.00m);
            mixWallet.LifetimeDisbursements.Should().Be(75.00m);
        }

        await using var verifyCtx = new PaymentsDbContext(BuildOptions(), encryptor);
        var finalWallet = await verifyCtx.ParticipantWallets.AsNoTracking().SingleAsync(w => w.Id == walletId, Ct);
        finalWallet.CurrentBalance.Should().Be(25.00m);
        finalWallet.VersionIndex.Should().Be(2);

        var racedNewWallet = await verifyCtx.ParticipantWallets.AsNoTracking().SingleAsync(w => w.SubjectId == "SUBJ-RACE-NEW", Ct);
        racedNewWallet.CurrentBalance.Should().Be(25.00m);
    }

    [Fact(DisplayName = "StaleStandaloneUpdate_MapsDbUpdateConcurrencyException_ToHttp409_WithoutCompoundingVersion")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-TCK-001")]
    public async Task StaleStandaloneUpdateMapsDbUpdateConcurrencyExceptionToHttp409WithoutCompoundingVersion()
    {
        database.SkipIfUnavailable();

        await MigrateSchemaAsync<TicketsDbContext>(database.ConnectionString!, "tickets", Ct);
        string connString = CadenceDatabaseExtensions.WithModuleSchema(database.ConnectionString!, "tickets");

        Guid tenantId = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-ticket-conc");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<StandaloneConcurrencyExceptionHandler>();
        builder.Services.AddSingleton<ICurrentTenantAccessor>(new FixedTenantAccessor(tenantId));
        builder.Services.AddDbContext<TicketsDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(connString)
                .UseCadenceStandaloneTenancy(sp.GetRequiredService<ICurrentTenantAccessor>(), isRelational: true)
                .AddInterceptors(
                    StandaloneTenantConnectionInterceptor.Instance,
                    StandaloneTenantSaveChangesInterceptor.Instance,
                    StandaloneConcurrencyTokenInterceptor.Instance);
        });

        await using var app = builder.Build();
        app.UseExceptionHandler();

        app.MapPatch("/test/tickets/{id}", async (string id, int expectedVersion, string title, TicketsDbContext db, HttpContext http) =>
        {
            var ticket = await db.Tickets.SingleAsync(t => t.Id == id);
            StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(db, ticket, expectedVersion, http);
            int baseline = db.Entry(ticket).Property(x => x.VersionIndex).OriginalValue;
            ticket.Title = title;
            ticket.VersionIndex = baseline + 99; // Caller manual bump must still normalize to OriginalValue + 1 without compounding
            ticket.ReasonForChange = "Concurrent edit";
            db.TicketRevisions.Add(new TicketRevision
            {
                TicketId = ticket.Id,
                RevisionNumber = baseline + 1,
                Reference = ticket.Reference,
                Title = ticket.Title,
                Description = ticket.Description,
                Category = ticket.Category,
                Priority = ticket.Priority,
                Status = ticket.Status,
                RecordedAt = new DateTimeOffset(2026, 11, 22, 12, 5, 0, TimeSpan.Zero),
                RecordedBy = "user-a",
                ReasonForChange = "Concurrent edit",
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { ticket.Id, ticket.Title, ticket.VersionIndex });
        });

        await app.StartAsync(Ct);

        string ticketId = Guid.NewGuid().ToString();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
            var t0 = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
            var initial = new Ticket
            {
                Id = ticketId,
                Reference = "TCK-CONC-01",
                Title = "Original Title",
                Description = "Initial description",
                Category = "GENERAL",
                Priority = "MEDIUM",
                Reporter = "rep-1",
                CreatedAt = t0,
                CreatedBy = "rep-1",
                ReasonForChange = "Seed",
                VersionIndex = 1,
            };
            db.Tickets.Add(initial);
            db.TicketRevisions.Add(TicketRevision.FromTicket(initial, t0, "rep-1"));
            await db.SaveChangesAsync(Ct);
        }

        using HttpClient client = app.GetTestClient();

        // First update with expectedVersion=1 succeeds and advances VersionIndex to 2 (not 100)
        var firstResp = await client.PatchAsync($"/test/tickets/{ticketId}?expectedVersion=1&title=Committed+Title", content: null, Ct);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = JsonDocument.Parse(await firstResp.Content.ReadAsStringAsync(Ct));
        firstDoc.RootElement.GetProperty("versionIndex").GetInt32().Should().Be(2);

        // Stale second update with expectedVersion=1 receives HTTP 409 ProblemDetails and does NOT overwrite Committed Title
        var staleResp = await client.PatchAsync($"/test/tickets/{ticketId}?expectedVersion=1&title=Stale+Overwrite", content: null, Ct);
        staleResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problemDoc = JsonDocument.Parse(await staleResp.Content.ReadAsStringAsync(Ct));
        problemDoc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        problemDoc.RootElement.GetProperty("code").GetString().Should().Be("CONCURRENCY_CONFLICT");

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var persisted = await verifyDb.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticketId, Ct);
        persisted.Title.Should().Be("Committed Title");
        persisted.VersionIndex.Should().Be(2);
        var revisions = await verifyDb.TicketRevisions.AsNoTracking().Where(r => r.TicketId == ticketId).OrderBy(r => r.RevisionNumber).ToListAsync(Ct);
        revisions.Should().HaveCount(2);
    }

    [Fact(DisplayName = "StaleShadowVersionToken_MapsDbUpdateConcurrencyException_ToHttp409_WithoutOverwritingCommittedFields")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task StaleShadowVersionTokenMapsDbUpdateConcurrencyExceptionToHttp409WithoutOverwritingCommittedFields()
    {
        database.SkipIfUnavailable();

        await MigrateSchemaAsync<ImagingDbContext>(database.ConnectionString!, "imaging", Ct);
        string connString = CadenceDatabaseExtensions.WithModuleSchema(database.ConnectionString!, "imaging");

        Guid tenantId = DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-imaging-conc");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<StandaloneConcurrencyExceptionHandler>();
        builder.Services.AddSingleton<ICurrentTenantAccessor>(new FixedTenantAccessor(tenantId));
        builder.Services.AddDbContext<ImagingDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(connString)
                .UseCadenceStandaloneTenancy(sp.GetRequiredService<ICurrentTenantAccessor>(), isRelational: true)
                .AddInterceptors(
                    StandaloneTenantConnectionInterceptor.Instance,
                    StandaloneTenantSaveChangesInterceptor.Instance,
                    StandaloneConcurrencyTokenInterceptor.Instance);
        });

        await using var app = builder.Build();
        app.UseExceptionHandler();

        app.MapPatch("/test/series/{id}", async (string id, long expectedVersion, string description, ImagingDbContext db, HttpContext http) =>
        {
            var series = await db.ImagingSeriesEnvelopes.SingleAsync(s => s.Id == id);
            StandaloneConcurrencyTokenInterceptor.EnforceExpectedVersion(db, series, expectedVersion, http);
            series.SeriesDescription = description;
            await db.SaveChangesAsync();
            long committedVersion = db.Entry(series).Property<long>(StandaloneConcurrencyTokenInterceptor.ShadowVersionPropertyName).CurrentValue;
            return Results.Ok(new { series.Id, series.SeriesDescription, Version = committedVersion });
        });

        await app.StartAsync(Ct);

        string studyEnvelopeId = Guid.NewGuid().ToString();
        string seriesId = Guid.NewGuid().ToString();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ImagingDbContext>();
            var t0 = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
            db.ImagingStudyEnvelopes.Add(new ImagingStudyEnvelope
            {
                Id = studyEnvelopeId,
                StudyInstanceUid = "1.2.840.113619.2.1",
                StudyId = "STUDY-IMG-01",
                SiteId = "SITE-01",
                SubjectId = "SUBJ-IMG-01",
                CreatedAt = t0,
                CreatedBy = "seed",
                UpdatedAt = t0,
            });
            db.ImagingSeriesEnvelopes.Add(new ImagingSeriesEnvelope
            {
                Id = seriesId,
                StudyEnvelopeId = studyEnvelopeId,
                SeriesInstanceUid = "1.2.840.113619.2.1.1",
                Modality = "MR",
                SeriesDescription = "Baseline T1",
                InstanceCount = 24,
            });
            await db.SaveChangesAsync(Ct);
        }

        using HttpClient client = app.GetTestClient();

        // Initial shadow _version is 0; first update advances _version to 1
        var firstResp = await client.PatchAsync($"/test/series/{seriesId}?expectedVersion=0&description=Axial+T1+Verified", content: null, Ct);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var firstDoc = JsonDocument.Parse(await firstResp.Content.ReadAsStringAsync(Ct));
        firstDoc.RootElement.GetProperty("version").GetInt64().Should().Be(1L);

        // Stale update with expectedVersion=0 receives HTTP 409 ProblemDetails and leaves committed SeriesDescription intact
        var staleResp = await client.PatchAsync($"/test/series/{seriesId}?expectedVersion=0&description=Stale+Series+Overwrite", content: null, Ct);
        staleResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problemDoc = JsonDocument.Parse(await staleResp.Content.ReadAsStringAsync(Ct));
        problemDoc.RootElement.GetProperty("status").GetInt32().Should().Be(409);
        problemDoc.RootElement.GetProperty("code").GetString().Should().Be("CONCURRENCY_CONFLICT");

        using var verifyScope = app.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ImagingDbContext>();
        var persisted = await verifyDb.ImagingSeriesEnvelopes.SingleAsync(s => s.Id == seriesId, Ct);
        persisted.SeriesDescription.Should().Be("Axial T1 Verified");
        verifyDb.Entry(persisted).Property<long>(StandaloneConcurrencyTokenInterceptor.ShadowVersionPropertyName).CurrentValue.Should().Be(1L);
    }
}
