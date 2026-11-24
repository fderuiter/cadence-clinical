using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Quality.Domain;
using Cadence.Apps.Quality.Infrastructure.Persistence;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Cadence.IntegrationTests.Database;

/// <summary>
/// Verifies on real PostgreSQL that standalone <see cref="DbContext"/> instances registered via
/// <see cref="CadenceDatabaseExtensions.AddCadencePostgresDbContext{TContext}"/> attach <see cref="GxPAuditInterceptor"/>
/// by default, enforce fail-closed actor attribution outside HTTP unless a named <c>svc:*</c> service account is bound via
/// <see cref="StandaloneAuditActorScope"/>, persist verifiable SHA-256 Merkle chains in <c>audit_logs</c>, and drain
/// <see cref="CtmsMonitoringVisit"/> and <see cref="CtmsSiteMilestone"/> domain events into <c>ctms.transactional_outbox</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandaloneGxPAuditAndOutboxPostgresTests(ThrowawayPostgresDatabase database) : IClassFixture<ThrowawayPostgresDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CtmsDbContext_DefaultGxPAuditInterceptor_WritesVerifiedMerkleChain_AndDrainsStatefulOutboxMessages()
    {
        database.SkipIfUnavailable();
        var now = new DateTimeOffset(2026, 11, 23, 14, 0, 0, TimeSpan.Zero);

        await using var app = await MigrateAndBuildStandaloneAppAsync<CtmsDbContext>(CtmsDbContextFactory.Schema);

        // 1. Saving an IGxPAuditable aggregate without an HTTP caller or StandaloneAuditActorScope fails closed.
        await using (var unattributedScope = app.Services.CreateAsyncScope())
        {
            using var tenantBinding = unattributedScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            var db = unattributedScope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            db.CtmsSiteMilestones.Add(new CtmsSiteMilestone("MS-UNATTRIBUTED")
            {
                StudyId = "STUDY-AUDIT-PG",
                SiteId = "SITE-01",
                MilestoneType = "SIV",
                CreatedAt = now.UtcDateTime,
                CreatedBy = "unattributed",
                ReasonForChange = "Should fail closed",
            });

            var act = () => db.SaveChangesAsync(Ct);
            (await act.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should().Contain("service-account audit actor scope");
        }

        // 2. Under a named svc:* service account, CtmsMonitoringVisit and CtmsSiteMilestone transitions write SHA-256
        // Merkle-chained AuditLogEntry rows AND drain OutboxMessages into ctms.transactional_outbox.
        string visitId = $"VIS-OUTBOX-{Guid.NewGuid():N}"[..20];
        string milestoneId = $"MS-OUTBOX-{Guid.NewGuid():N}"[..20];

        await using (var scope = app.Services.CreateAsyncScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            using var actorScope = StandaloneAuditActorScope.Begin("svc:ctms-audit-verifier");
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();

            var visit = new CtmsMonitoringVisit(visitId)
            {
                StudyId = "STUDY-AUDIT-PG",
                SiteId = "SITE-01",
                CraId = "cra-01",
                VisitType = "IMV",
                ScheduledDate = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "svc:ctms-audit-verifier",
                ReasonForChange = "Schedule monitoring visit",
            };
            visit.Complete(now.AddHours(2), "Complete monitoring visit report", "svc:ctms-audit-verifier", now.AddHours(2), "cra")
                .IsSuccess.Should().BeTrue();

            var milestone = new CtmsSiteMilestone(milestoneId)
            {
                StudyId = "STUDY-AUDIT-PG",
                SiteId = "SITE-01",
                MilestoneType = "SITE_ACTIVATION",
                PlannedDate = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "svc:ctms-audit-verifier",
                ReasonForChange = "Create site activation milestone",
            };
            milestone.Complete("svc:ctms-audit-verifier", now.AddHours(3), "Site activation completed", now.AddHours(3))
                .IsSuccess.Should().BeTrue();

            visit.OutboxMessages.Should().NotBeEmpty("state transitions queue domain events before SaveChangesAsync");
            milestone.OutboxMessages.Should().NotBeEmpty("state transitions queue domain events before SaveChangesAsync");

            db.CtmsMonitoringVisits.Add(visit);
            db.CtmsSiteMilestones.Add(milestone);
            await db.SaveChangesAsync(Ct);

            visit.OutboxMessages.Should().BeEmpty("GxPAuditInterceptor must drain CtmsMonitoringVisit.OutboxMessages");
            milestone.OutboxMessages.Should().BeEmpty("GxPAuditInterceptor must drain CtmsSiteMilestone.OutboxMessages");

            // Second mutation to verify PredecessorHash -> BlockHash Merkle chaining across commits
            visit.SignOff(
                    signer: "pi-01",
                    signingTimestamp: now.AddHours(4),
                    signatureManifestation: "{\"meaning\":\"APPROVAL\",\"signer\":\"pi-01\"}",
                    reasonForChange: "PI co-signed monitoring visit",
                    userId: "svc:ctms-audit-verifier",
                    userRole: "principal_investigator")
                .IsSuccess.Should().BeTrue();
            await db.SaveChangesAsync(Ct);
            visit.OutboxMessages.Should().BeEmpty();
        }

        await using (var verifyScope = app.Services.CreateAsyncScope())
        {
            using var tenantBinding = verifyScope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            var db = verifyScope.ServiceProvider.GetRequiredService<CtmsDbContext>();

            var outboxMessages = await db.Set<TransactionalOutboxMessage>()
                .AsNoTracking()
                .Where(m => m.AggregateId == visitId || m.AggregateId == milestoneId)
                .ToListAsync(Ct);

            outboxMessages.Select(m => m.AggregateId).Should().Contain([visitId, milestoneId]);
            outboxMessages.Count(m => m.AggregateId == visitId).Should().Be(2, "Complete and SignOff each emit an outbox event");
            outboxMessages.Count(m => m.AggregateId == milestoneId).Should().Be(1, "Complete emits an outbox event");

            var visitChain = await db.Set<AuditLogEntry>()
                .AsNoTracking()
                .Where(e => e.EntityName == nameof(CtmsMonitoringVisit))
                .OrderBy(e => e.Timestamp)
                .ToListAsync(Ct);

            var targetVisitEntries = visitChain.Where(e => e.EntityId == visitId).ToList();
            targetVisitEntries.Should().HaveCount(2);
            targetVisitEntries.Should().OnlyContain(e => e.UserId == "svc:ctms-audit-verifier");
            targetVisitEntries[1].PredecessorHash.Should().Be(targetVisitEntries[0].BlockHash);
            AuditLogChainVerifier.Verify(visitChain).Should().ContainSingle().Which.Valid.Should().BeTrue();
        }
    }

    [Fact]
    public async Task StandaloneContexts_DefaultGxPAuditInterceptor_PersistsValidMerkleAuditChainsAcrossServices()
    {
        database.SkipIfUnavailable();
        var now = new DateTimeOffset(2026, 11, 23, 15, 0, 0, TimeSpan.Zero);

        // Exercise QualityDbContext, FileshareDbContext, and EtmfDbContext via AddCadencePostgresDbContext
        await using (var qualityApp = await MigrateAndBuildStandaloneAppAsync<QualityDbContext>(QualityDbContextFactory.Schema))
        await using (var scope = qualityApp.Services.CreateAsyncScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            using var actorScope = StandaloneAuditActorScope.Begin("svc:quality-seeder");
            var db = scope.ServiceProvider.GetRequiredService<QualityDbContext>();

            string devId = $"DEV-{Guid.NewGuid():N}"[..16];
            var dev = new QualityDeviation(devId)
            {
                StudyId = "STUDY-Q",
                SiteId = "SITE-01",
                Title = "Protocol window excursion",
                Type = "PROTOCOL",
                Severity = "MAJOR",
                Status = "OPEN",
                Description = "Initial deviation",
                CreatedAt = now.UtcDateTime,
                CreatedBy = "svc:quality-seeder",
                ReasonForChange = "Open deviation",
            };
            db.QualityDeviations.Add(dev);
            await db.SaveChangesAsync(Ct);

            dev.Description = "Updated deviation investigation summary";
            dev.ReasonForChange = "Append root cause";
            await db.SaveChangesAsync(Ct);

            var entries = await db.Set<AuditLogEntry>().AsNoTracking()
                .Where(e => e.EntityName == nameof(QualityDeviation))
                .ToListAsync(Ct);
            entries.Where(e => e.EntityId == devId).Should().HaveCount(2);
            AuditLogChainVerifier.Verify(entries).Should().ContainSingle().Which.Valid.Should().BeTrue();
        }

        await using (var fileshareApp = await MigrateAndBuildStandaloneAppAsync<FileshareDbContext>(FileshareDbContextFactory.Schema))
        await using (var scope = fileshareApp.Services.CreateAsyncScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            using var actorScope = StandaloneAuditActorScope.Begin("svc:fileshare-quarantine-worker");
            var db = scope.ServiceProvider.GetRequiredService<FileshareDbContext>();

            string fileId = Guid.NewGuid().ToString("D");
            var record = new FileRecord(fileId)
            {
                StudyId = "STUDY-FS",
                Filename = "protocol.pdf",
                MimeType = "application/pdf",
                SizeBytes = 1024,
                ObjectKey = $"studies/STUDY-FS/{fileId}/protocol.pdf",
                ChecksumSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
                UploadedBy = "svc:fileshare-quarantine-worker",
                UploadedAt = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "svc:fileshare-quarantine-worker",
                ReasonForChange = "Register uploaded file",
            };
            db.FileRecords.Add(record);
            await db.SaveChangesAsync(Ct);

            var entries = await db.Set<AuditLogEntry>().AsNoTracking()
                .Where(e => e.EntityName == nameof(FileRecord))
                .ToListAsync(Ct);
            entries.Where(e => e.EntityId == fileId).Should().ContainSingle().Which.UserId.Should().Be("svc:fileshare-quarantine-worker");
            AuditLogChainVerifier.Verify(entries).Should().ContainSingle().Which.Valid.Should().BeTrue();
        }

        await using (var etmfApp = await MigrateAndBuildStandaloneAppAsync<EtmfDbContext>(EtmfDbContextFactory.Schema))
        await using (var scope = etmfApp.Services.CreateAsyncScope())
        {
            using var tenantBinding = scope.ServiceProvider.GetRequiredService<ITenantScope>().Bind("tenant-test");
            using var actorScope = StandaloneAuditActorScope.Begin("svc:etmf-db-seeder");
            var db = scope.ServiceProvider.GetRequiredService<EtmfDbContext>();

            string docId = $"TMF-{Guid.NewGuid():N}"[..16];
            var doc = new TmfDocument(docId)
            {
                StudyId = "STUDY-ETMF",
                SiteId = "SITE-01",
                Zone = 5,
                Section = "05.01",
                ArtifactCode = "05.01.01",
                ArtifactType = "Investigator CV",
                Filename = "investigator_cv.pdf",
                MimeType = "application/pdf",
                ObjectKey = $"etmf/{docId}.pdf",
                ContentChecksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
                CreatedAt = now.UtcDateTime,
                CreatedBy = "svc:etmf-db-seeder",
                ReasonForChange = "Initial TMF filing",
            };
            db.TmfDocuments.Add(doc);
            await db.SaveChangesAsync(Ct);

            var entries = await db.Set<AuditLogEntry>().AsNoTracking()
                .Where(e => e.EntityName == nameof(TmfDocument))
                .ToListAsync(Ct);
            entries.Where(e => e.EntityId == docId).Should().ContainSingle().Which.UserId.Should().Be("svc:etmf-db-seeder");
            AuditLogChainVerifier.Verify(entries).Should().ContainSingle().Which.Valid.Should().BeTrue();
        }
    }

    private async Task<WebApplication> MigrateAndBuildStandaloneAppAsync<TContext>(string schema)
        where TContext : DbContext
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration[$"ConnectionStrings:{CadenceDatabaseExtensions.DefaultConnectionName}"] = database.ConnectionString!;
        builder.Configuration["Database:MigrateOnStartup"] = "true";
        builder.Services.AddSingleton(TestFieldProtection.CreateEncryptor());
        builder.AddCadencePostgresDbContext<TContext>(schema);

        var app = builder.Build();
        var migrator = app.Services.GetServices<IHostedService>().OfType<CadenceMigrationHostedService<TContext>>().Single();
        await migrator.StartingAsync(Ct);
        return app;
    }
}
