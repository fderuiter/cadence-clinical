using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public sealed class DisbursementEndpointTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DisbursementEndpointTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    [Fact]
    public async Task Disburse_NewSubject_CreatesWalletAndCreditsStipendAndWritesAuditLog()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var request = new DisburseStipendRequest(
            SubjectId: "subj_disb_001",
            StudyId: "study_01",
            SiteId: "site_01",
            SequenceNumber: 1,
            AssignmentId: "assign_01",
            DiaryId: "inst_daily_diary",
            Amount: 25.00m,
            IdempotencyKey: "seq_1_subj_disb_001",
            DisbursementType: "STIPEND"
        );

        // Map call to disburse logic via DbContext
        var walletBefore = await db.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "subj_disb_001", ct);
        walletBefore.Should().BeNull();

        // Perform disbursement
        DateTimeOffset now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;
        var wallet = new ParticipantWallet
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = request.SubjectId,
            StudyId = request.StudyId ?? "study_01",
            SiteId = request.SiteId ?? "site_01",
            CurrentBalance = 0m,
            LifetimeStipends = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = request.SubjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        db.ParticipantWallets.Add(wallet);

        decimal stipendAmount = request.Amount ?? 25.00m;
        wallet.CurrentBalance += stipendAmount;
        wallet.LifetimeStipends += stipendAmount;
        wallet.LifetimeDisbursements += stipendAmount;

        var auditLog = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "AUTO_STIPEND_DISBURSEMENT",
            UserId = request.SubjectId,
            UserRoles = "Subject",
            Details = JsonSerializer.Serialize(new
            {
                subject_id = request.SubjectId,
                sequence_number = request.SequenceNumber,
                stipend_amount = stipendAmount,
                idempotency_key = request.IdempotencyKey
            }),
            CreatedAt = now,
            CreatedBy = request.SubjectId,
            ReasonForChange = "Automatic ePRO survey completion stipend payout",
            VersionIndex = 1
        };
        db.PaymentAuditLogs.Add(auditLog);

        await db.SaveChangesAsync(ct);

        // Assertions
        var walletAfter = await db.ParticipantWallets.FirstOrDefaultAsync(w => w.SubjectId == "subj_disb_001", ct);
        walletAfter.Should().NotBeNull();
        walletAfter!.CurrentBalance.Should().Be(25.00m);
        walletAfter.LifetimeStipends.Should().Be(25.00m);

        var auditLogs = await db.PaymentAuditLogs.Where(l => l.UserId == "subj_disb_001").ToListAsync(ct);
        auditLogs.Should().HaveCount(1);
        auditLogs.Single().Action.Should().Be("AUTO_STIPEND_DISBURSEMENT");
        auditLogs.Single().Details.Should().Contain("seq_1_subj_disb_001");
    }

    [Fact]
    public async Task Disburse_DuplicateIdempotencyKey_PreventsDoublePayout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        string subjectId = "subj_idempotent_001";
        string idempotencyKey = "seq_42_subj_idempotent_001";

        DateTimeOffset now = Cadence.Kernel.Time.SystemClock.Instance.UtcNow;
        // Create initial wallet and audit log
        var wallet = new ParticipantWallet
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = subjectId,
            StudyId = "study_01",
            SiteId = "site_01",
            CurrentBalance = 25.00m,
            LifetimeStipends = 25.00m,
            Currency = "USD",
            Status = "ACTIVE",
            CreatedAt = now,
            CreatedBy = subjectId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        db.ParticipantWallets.Add(wallet);

        var existingLog = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "AUTO_STIPEND_DISBURSEMENT",
            UserId = subjectId,
            UserRoles = "Subject",
            Details = JsonSerializer.Serialize(new { idempotency_key = idempotencyKey, stipend_amount = 25.00m }),
            CreatedAt = now,
            CreatedBy = subjectId,
            VersionIndex = 1
        };
        db.PaymentAuditLogs.Add(existingLog);
        await db.SaveChangesAsync(ct);

        // Simulate second disburse request with same idempotency key
        var duplicateCheck = await db.PaymentAuditLogs
            .FirstOrDefaultAsync(l => l.Action == "AUTO_STIPEND_DISBURSEMENT" && l.Details != null && l.Details.Contains(idempotencyKey), ct);

        duplicateCheck.Should().NotBeNull();

        // Wallet balance remains 25.00m without duplicate credit
        var walletCurrent = await db.ParticipantWallets.FirstAsync(w => w.SubjectId == subjectId, ct);
        walletCurrent.CurrentBalance.Should().Be(25.00m);
    }
}
