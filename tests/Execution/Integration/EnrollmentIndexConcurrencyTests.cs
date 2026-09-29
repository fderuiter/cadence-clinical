using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Consumers;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.UnitTests.Execution.Fakes;

using FluentAssertions;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Integration;

/// <summary>
/// Stress-tests concurrent subject enrollment and sequential index assignment across 20 parallel requests.
/// Validates that advisory-locked PostgreSQL transactions produce race-free,
/// gap-free, duplicate-free enrollment_index sequences ({0, ..., 19}) under high concurrency.
/// Skipped unless a PostgreSQL source is configured (see <see cref="ExecutionPostgresFixture"/>).
/// @req:PRD-SUB-001
/// </summary>
[Trait("Category", "Integration")]
public sealed class EnrollmentIndexConcurrencyTests(ExecutionPostgresFixture fixture) : IClassFixture<ExecutionPostgresFixture>
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class TestAuditContextAccessor : ICurrentAuditContextAccessor
    {
        public string UserId => "concurrency-test-user";
        public string ChangeReason => "Concurrency Stress Test";
    }

    private sealed class TestDemographicsEncryptionService : IDemographicsEncryptionService
    {
        public string Encrypt(IReadOnlyDictionary<string, object?> demographics) => "ENCRYPTED_DEMOGRAPHICS";
        public IReadOnlyDictionary<string, object?> Decrypt(string ciphertext) => new Dictionary<string, object?>();
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(string tableName, string recordId, string action, string userId, string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues, IReadOnlyDictionary<string, object?>? newValues, CancellationToken ct) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Validates that 20 concurrent CreateAsync calls for the same study assign sequential indices {0, ..., 19} without gaps or duplicates.
    /// @req:PRD-SUB-001
    /// </summary>
    [Fact(DisplayName = "CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates")]
    [Trait("Category", "Integration")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_ConcurrentCalls_AssignsSequentialIndicesWithoutGapsOrDuplicates()
    {
        // Arrange
        const int concurrencyLevel = 20;
        var studyCode = new StudyCode($"STUDY-CONCUR-{Guid.NewGuid():N}");
        var clock = new TestClock();
        var auditContext = new TestAuditContextAccessor();
        var auditWriter = new NoOpAuditLogWriter();
        var encryption = new TestDemographicsEncryptionService();
        var consents = new InMemoryConsentRepository();
        var compliance = new InMemorySiteComplianceRepository();

        fixture.SkipIfUnavailable();
        string connectionString = fixture.ConnectionString!;

        // Real PostgreSQL execution with transaction advisory locks
        var tasks = Enumerable.Range(0, concurrencyLevel).Select(async i =>
        {
            await using var context = ExecutionPostgresFixture.CreateDbContext(connectionString);
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var service = new SubjectApplicationService(repository, consents, compliance, encryption, auditWriter, clock, auditContext);

            var request = new SubjectCreateRequest($"SUBJ-PG-{i:D3}", studyCode.Value, "SITE-001", ArmId: null, Demographics: null);
            var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
        });

        // Act
        await Task.WhenAll(tasks);

        // Assert
        await using var verifyContext = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var persisted = await verifyContext.ClinicalSubjects.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .OrderBy(s => s.EnrollmentIndex)
            .ToListAsync(TestContext.Current.CancellationToken);

        persisted.Should().HaveCount(concurrencyLevel);
        var indices = persisted.Select(s => s.EnrollmentIndex).ToList();
        indices.Should().Equal(Enumerable.Range(0, concurrencyLevel));
    }

    /// <summary>
    /// Validates that 20 concurrent EConsentCompletedEvent consumptions for the same study assign sequential indices {0, ..., 19} without gaps or duplicates.
    /// @req:PRD-SUB-001
    /// </summary>
    [Fact(DisplayName = "EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates")]
    [Trait("Category", "Integration")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EConsentCompletedConsumer_ConcurrentEvents_AssignsSequentialIndicesWithoutGapsOrDuplicates()
    {
        // Arrange
        const int concurrencyLevel = 20;
        var studyCode = new StudyCode($"STUDY-EVENT-{Guid.NewGuid():N}");
        var clock = new TestClock();
        var auditWriter = new NoOpAuditLogWriter();
        var consents = new InMemoryConsentRepository();

        fixture.SkipIfUnavailable();
        string connectionString = fixture.ConnectionString!;

        var tasks = Enumerable.Range(0, concurrencyLevel).Select(async i =>
        {
            await using var context = ExecutionPostgresFixture.CreateDbContext(connectionString);
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var consumer = new EConsentCompletedConsumer(repository, consents, auditWriter, new AmbientAuditContextAccessor(new HttpContextAccessor()), clock);

            var consumeContext = Substitute.For<ConsumeContext<EConsentCompletedEvent>>();
            consumeContext.Message.Returns(new EConsentCompletedEvent($"SUBJ-EVT-PG-{i:D3}", studyCode.Value, "SITE-001", "v1.0", 1));
            consumeContext.CancellationToken.Returns(TestContext.Current.CancellationToken);

            await consumer.Consume(consumeContext);
        });

        // Act
        await Task.WhenAll(tasks);

        // Assert
        await using var verifyContext = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var persisted = await verifyContext.ClinicalSubjects.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .OrderBy(s => s.EnrollmentIndex)
            .ToListAsync(TestContext.Current.CancellationToken);

        persisted.Should().HaveCount(concurrencyLevel);
        var indices = persisted.Select(s => s.EnrollmentIndex).ToList();
        indices.Should().Equal(Enumerable.Range(0, concurrencyLevel));
    }

    /// <summary>
    /// Validates that concurrent calls split across two independent studies assign independent sequences {0, ..., 9} each.
    /// @req:PRD-SUB-001
    /// </summary>
    [Fact(DisplayName = "CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices")]
    [Trait("Category", "Integration")]
    [GxPRequirement("PRD-SUB-001")]
    public async Task CreateAsync_ConcurrentCallsAcrossTwoStudies_AssignsIndependentIndices()
    {
        // Arrange
        const int perStudyCount = 10;
        var studyA = new StudyCode($"STUDY-A-{Guid.NewGuid():N}");
        var studyB = new StudyCode($"STUDY-B-{Guid.NewGuid():N}");
        var clock = new TestClock();
        var auditContext = new TestAuditContextAccessor();
        var auditWriter = new NoOpAuditLogWriter();
        var encryption = new TestDemographicsEncryptionService();
        var consents = new InMemoryConsentRepository();
        var compliance = new InMemorySiteComplianceRepository();

        fixture.SkipIfUnavailable();
        string connectionString = fixture.ConnectionString!;

        var tasksA = Enumerable.Range(0, perStudyCount).Select(async i =>
        {
            await using var context = ExecutionPostgresFixture.CreateDbContext(connectionString);
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var service = new SubjectApplicationService(repository, consents, compliance, encryption, auditWriter, clock, auditContext);

            var request = new SubjectCreateRequest($"SUBJ-A-{i:D3}", studyA.Value, "SITE-001", ArmId: null, Demographics: null);
            var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
        });

        var tasksB = Enumerable.Range(0, perStudyCount).Select(async i =>
        {
            await using var context = ExecutionPostgresFixture.CreateDbContext(connectionString);
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());
            var service = new SubjectApplicationService(repository, consents, compliance, encryption, auditWriter, clock, auditContext);

            var request = new SubjectCreateRequest($"SUBJ-B-{i:D3}", studyB.Value, "SITE-001", ArmId: null, Demographics: null);
            var result = await service.CreateAsync(request, TestContext.Current.CancellationToken);
            result.IsSuccess.Should().BeTrue();
        });

        // Act
        await Task.WhenAll(tasksA.Concat(tasksB));

        // Assert
        await using var verifyContext = ExecutionPostgresFixture.CreateDbContext(connectionString);
        var persistedA = await verifyContext.ClinicalSubjects.AsNoTracking().Where(s => s.StudyCode == studyA).OrderBy(s => s.EnrollmentIndex).ToListAsync(TestContext.Current.CancellationToken);
        var persistedB = await verifyContext.ClinicalSubjects.AsNoTracking().Where(s => s.StudyCode == studyB).OrderBy(s => s.EnrollmentIndex).ToListAsync(TestContext.Current.CancellationToken);

        persistedA.Select(s => s.EnrollmentIndex).Should().Equal(Enumerable.Range(0, perStudyCount));
        persistedB.Select(s => s.EnrollmentIndex).Should().Equal(Enumerable.Range(0, perStudyCount));
    }
}
