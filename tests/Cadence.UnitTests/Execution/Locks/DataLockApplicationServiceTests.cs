using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Locks;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Locks;

public sealed class DataLockApplicationServiceTests
{
    private sealed class FakeAuditLogWriter : IAuditLogWriter
    {
        public List<(string Action, string RecordId, string Reason)> AuditEntries { get; } = new();

        public Task WriteAsync(
            string entityName,
            string recordId,
            string action,
            string userId,
            string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues,
            CancellationToken ct)
        {
            AuditEntries.Add((action, recordId, changeReason));
            return Task.CompletedTask;
        }
    }

    private static ExecutionDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ExecutionDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task LockAsync_WhenDiscrepanciesExistWithoutOverride_BlocksLockExecution()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);
        var auditWriter = new FakeAuditLogWriter();

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("SYSBP"),
            explanation: "Open query blocking lock.",
            siteId: new SiteCode("SITE-01")
        ).Value!;

        dbContext.ClinicalQueries.Add(query);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engine = new PreLockValidationEngine(dbContext, fakeClock);
        var service = new DataLockApplicationService(dbContext, engine, fakeClock, auditWriter);

        var lockReq = new DataLockExecuteRequest(
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            Action: "HARD_LOCK",
            ReasonForChange: "Attempting subject hard lock"
        );

        // Act
        var result = await service.LockAsync(lockReq, "dm-user-1", TestContext.Current.CancellationToken);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("PRE_LOCK_CHECK_BLOCKED");
        auditWriter.AuditEntries.Should().BeEmpty();
        (await dbContext.DataLocks.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task LockAsync_WhenDiscrepanciesExistWithApprovedOverride_ExecutesLockAndLogsAudit()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);
        var auditWriter = new FakeAuditLogWriter();

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("SYSBP"),
            explanation: "Open query override.",
            siteId: new SiteCode("SITE-01")
        ).Value!;

        dbContext.ClinicalQueries.Add(query);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engine = new PreLockValidationEngine(dbContext, fakeClock);
        var service = new DataLockApplicationService(dbContext, engine, fakeClock, auditWriter);

        var lockReq = new DataLockExecuteRequest(
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            Action: "HARD_LOCK",
            ReasonForChange: "Subject hard lock with DM override",
            OverrideApproved: true,
            OverrideJustification: "Approved by Lead Data Manager Dr. Smith for interim database lock snapshot."
        );

        // Act
        var result = await service.LockAsync(lockReq, "dm-user-1", TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var record = result.Value!;
        record.IsActive.Should().BeTrue();
        record.IsOverride.Should().BeTrue();
        record.OverrideJustification.Should().Contain("Dr. Smith");
        record.OpenQueryCount.Should().Be(1);

        auditWriter.AuditEntries.Should().HaveCount(1);
        auditWriter.AuditEntries[0].Action.Should().Be("EXECUTE_OVERRIDE_LOCK_SUBJECT");
        auditWriter.AuditEntries[0].Reason.Should().Contain("Dr. Smith");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task UnlockAsync_WithValidJustification_UnlocksScopeAndLogsAudit()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);
        var auditWriter = new FakeAuditLogWriter();

        var engine = new PreLockValidationEngine(dbContext, fakeClock);
        var service = new DataLockApplicationService(dbContext, engine, fakeClock, auditWriter);

        // Create initial clean lock
        var lockReq = new DataLockExecuteRequest(
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            Action: "HARD_LOCK",
            ReasonForChange: "Initial clean hard lock"
        );

        var lockRes = await service.LockAsync(lockReq, "dm-user-1", TestContext.Current.CancellationToken);
        lockRes.IsSuccess.Should().BeTrue();
        var lockId = lockRes.Value!.LockId;

        var unlockReq = new DataLockUnlockRequest(
            LockId: lockId,
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            Justification: "Unlocking subject scope to resolve late-emerging CRA monitoring query regarding adverse event coding."
        );

        // Act
        var unlockRes = await service.UnlockAsync(unlockReq, "dm-user-1", TestContext.Current.CancellationToken);

        // Assert
        unlockRes.IsSuccess.Should().BeTrue();
        var unlockedRecord = unlockRes.Value!;
        unlockedRecord.IsActive.Should().BeFalse();
        unlockedRecord.Status.Should().Be("UNLOCKED");
        unlockedRecord.UnlockJustification.Should().Contain("adverse event coding");

        auditWriter.AuditEntries.Should().HaveCount(2);
        auditWriter.AuditEntries[1].Action.Should().Be("UNLOCK_OVERRIDE_SUBJECT");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task UnlockAsync_WithShortJustification_FailsValidation()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);
        var auditWriter = new FakeAuditLogWriter();

        var engine = new PreLockValidationEngine(dbContext, fakeClock);
        var service = new DataLockApplicationService(dbContext, engine, fakeClock, auditWriter);

        var unlockReq = new DataLockUnlockRequest(
            LockId: "LOCK-1",
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            Justification: "Short unlock reason"
        );

        // Act
        var unlockRes = await service.UnlockAsync(unlockReq, "dm-user-1", TestContext.Current.CancellationToken);

        // Assert
        unlockRes.IsFailure.Should().BeTrue();
        unlockRes.Error!.Code.Should().Be("MANDATORY_JUSTIFICATION_REQUIRED");
    }
}
