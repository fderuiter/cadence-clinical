using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Randomization;
using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Randomization;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Supply;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Execution.Randomization;

/// <summary>
/// The randomisation scheme version (#5607, ADR-2640): what a draft must declare before it can go live, the
/// second-person approval, and the canonical strata a participant's factor values map to.
/// </summary>
public sealed class RandomizationSchemeTests
{
    private static readonly StudyCode Study = new("STUDY-RND");
    private static readonly FakeClock Clock = new();

    private static readonly Dictionary<string, int> TwoArms = new() { ["PLACEBO"] = 1, ["ACTIVE"] = 1 };

    private static readonly Dictionary<string, IReadOnlyList<string>> SexAndAge = new()
    {
        ["sex"] = ["F", "M"],
        ["age_group"] = ["<50", ">=50"],
    };

    private static Result<RandomizationScheme> DraftBlock(
        IReadOnlyDictionary<string, int>? arms = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? factors = null,
        IReadOnlyList<int>? blockSizes = null,
        int slotsPerStratum = 8) =>
        RandomizationScheme.Draft(Study, 1, RandomizationMethod.PermutedBlock, arms ?? TwoArms, factors, blockSizes,
            slotsPerStratum, preferredArmProbability: 0.8, requiresKit: false, "statistician-a", "Initial list", Clock);

    [Fact]
    [GxPRequirement("PRD-SUB-003")]
    public void Draft_PermutedBlock_StratifiesByEveryFactorLevelCombination()
    {
        var scheme = DraftBlock(factors: SexAndAge, blockSizes: [4, 2]).Value!;

        scheme.Status.Should().Be(RandomizationSchemeStatus.Draft);
        scheme.BlockSizes().Should().Equal(2, 4);
        scheme.Strata().Should().Equal(
            "age_group=<50|sex=F", "age_group=<50|sex=M", "age_group=>=50|sex=F", "age_group=>=50|sex=M");
        scheme.ListSize.Should().Be(4 * 8);
    }

    [Fact]
    public void Draft_WithoutFactors_HasTheSingleDefaultStratum()
    {
        var scheme = DraftBlock().Value!;

        scheme.Strata().Should().Equal(RandomizationScheme.DefaultStratum);
        scheme.StratumFor(null).Value.Should().Be(RandomizationScheme.DefaultStratum);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Draft_RefusesABlockSizeThatIsNotAMultipleOfTheRatioSum(int blockSize)
    {
        var result = DraftBlock(blockSizes: [blockSize]);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Draft_RefusesASingleArm()
    {
        var result = DraftBlock(arms: new Dictionary<string, int> { ["ACTIVE"] = 1 });

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Draft_RefusesAListLargerThanTheLimit()
    {
        var result = DraftBlock(factors: SexAndAge, slotsPerStratum: RandomizationScheme.MaxListSize);

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.2)]
    [GxPRequirement("PRD-SUB-004")]
    public void Draft_Minimization_NeedsFactorsAndAPreferredArmProbabilityAboveOneHalf(double probability)
    {
        var withoutFactors = RandomizationScheme.Draft(Study, 1, RandomizationMethod.Minimization, TwoArms, null, null,
            0, 0.8, false, "statistician-a", "Initial", Clock);
        var badProbability = RandomizationScheme.Draft(Study, 1, RandomizationMethod.Minimization, TwoArms, SexAndAge, null,
            0, probability, false, "statistician-a", "Initial", Clock);

        withoutFactors.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
        badProbability.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_INVALID");
    }

    [Fact]
    public void Approve_ByTheDrafter_IsRefused_AndTheVersionStaysDraft()
    {
        var scheme = DraftBlock().Value!;

        var result = scheme.Approve("STATISTICIAN-A", "Go live", Clock);

        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_SELF_APPROVAL");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        scheme.Status.Should().Be(RandomizationSchemeStatus.Draft);
    }

    [Fact]
    public void Approve_ByASecondPerson_MakesItLive_AndItCannotBeApprovedTwice()
    {
        var scheme = DraftBlock().Value!;

        scheme.Approve("statistician-b", "Go live", Clock).IsSuccess.Should().BeTrue();

        scheme.Status.Should().Be(RandomizationSchemeStatus.Approved);
        scheme.ApprovedBy.Should().Be("statistician-b");
        scheme.Approve("statistician-c", "Again", Clock).Error!.Code.Should().Be("RANDOMIZATION_SCHEME_NOT_DRAFT");
    }

    [Fact]
    public void Retire_EndsAnApprovedVersion()
    {
        var scheme = DraftBlock().Value!;
        scheme.Approve("statistician-b", "Go live", Clock);

        scheme.Retire("Superseded by version 2", Clock);

        scheme.Status.Should().Be(RandomizationSchemeStatus.Retired);
        scheme.RetiredAt.Should().Be(Clock.UtcNow);
    }

    [Fact]
    public void StratumFor_IsCanonical_WhateverOrderAndSpacingTheValuesArriveIn()
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;

        var stratum = scheme.StratumFor(new Dictionary<string, string> { ["sex"] = " M ", ["age_group"] = ">=50" });

        stratum.Value.Should().Be("age_group=>=50|sex=M");
    }

    [Theory]
    [InlineData("sex", "X", "STRATIFICATION_LEVEL_UNKNOWN")]
    [InlineData("site", "S1", "STRATIFICATION_FACTOR_UNKNOWN")]
    public void StratumFor_RefusesUndeclaredValues(string factor, string level, string code)
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;
        var values = new Dictionary<string, string> { ["sex"] = "F", ["age_group"] = "<50", [factor] = level };

        scheme.StratumFor(values).Error!.Code.Should().Be(code);
    }

    [Fact]
    public void StratumFor_RefusesAMissingFactor()
    {
        var scheme = DraftBlock(factors: SexAndAge).Value!;

        scheme.StratumFor(new Dictionary<string, string> { ["sex"] = "F" }).Error!.Code
            .Should().Be("STRATIFICATION_FACTOR_MISSING");
    }

    [Fact]
    public void ListSlot_IsConsumedOnce()
    {
        var slot = RandomizationListSlot.Create(Guid.NewGuid(), RandomizationScheme.DefaultStratum, 1, "enc:v1:k:x");
        slot.Consume(Guid.NewGuid());

        var again = () => slot.Consume(Guid.NewGuid());

        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void Subject_IsRandomisedOnlyFromEnrolled_AndOnlyOnce()
    {
        var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-RND-1"), Study, null, 1, null, null);

        subject.Randomize(Guid.NewGuid(), null, new Dictionary<string, object?>()).IsFailure
            .Should().BeTrue("a screening participant cannot skip enrolment");
        subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
        subject.Randomize(Guid.NewGuid(), "KIT-1", new Dictionary<string, object?> { ["sex"] = "F" }).IsSuccess.Should().BeTrue();

        subject.Status.Should().Be(SubjectState.Randomized);
        subject.KitReference.Should().Be("KIT-1");
        subject.Randomize(Guid.NewGuid(), null, new Dictionary<string, object?>()).Error!.Code
            .Should().Be("SUBJECT_ALREADY_RANDOMIZED");
    }

    [Fact]
    public void AdvisoryLockSql_ForRandomizationScheme_KeysLockToSchemeId()
    {
        var schemeId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        FormattableString lockSql = AdvisoryLockSql.ForRandomizationScheme(schemeId);

        lockSql.Format.Should().Contain("pg_advisory_xact_lock")
            .And.Contain("current_setting('cadence.tenant_id', true)")
            .And.Contain("hashtext");
        lockSql.GetArguments().Should().Equal($"randomization:scheme:{schemeId:D}");
    }

    [Fact]
    public async Task ApproveSchemeAsync_PreLockIneligibleCaller_ShortCircuitsBeforeEncryptingOrOpeningTransaction()
    {
        var scheme = DraftBlock(slotsPerStratum: 16).Value!;
        var repo = new RecordingRandomizationRepository(scheme);
        var protector = new CountingAllocationProtector();
        var service = CreateService(repo, protector, userId: "STATISTICIAN-A");

        var result = await service.ApproveSchemeAsync(scheme.Id, "Self sign-off attempt", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_SELF_APPROVAL");
        protector.ProtectBatchCalls.Should().Be(0, "ineligible approval requests must fail before generating or encrypting slots");
        protector.ProtectCalls.Should().Be(0);
        repo.Operations.Should().Equal(["GetScheme"],
            "ineligible approval requests must not open a transaction, acquire advisory locks, or stage slots");
    }

    [Fact]
    public async Task ApproveSchemeAsync_ConcurrentApprovalObservedAfterLockScheme_RefusesBeforeStagingSlotsOrLockingStudy()
    {
        var initialDraft = DraftBlock(slotsPerStratum: 8).Value!;
        var concurrentlyApproved = DraftBlock(slotsPerStratum: 8).Value!;
        concurrentlyApproved.Approve("statistician-other", "Won race", Clock).IsSuccess.Should().BeTrue();

        var repo = new RecordingRandomizationRepository(initialDraft, postSchemeLockScheme: concurrentlyApproved);
        var protector = new CountingAllocationProtector();
        var service = CreateService(repo, protector, userId: "statistician-b");

        var result = await service.ApproveSchemeAsync(initialDraft.Id, "Second concurrent sign-off", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("RANDOMIZATION_SCHEME_NOT_DRAFT");
        protector.ProtectBatchCalls.Should().Be(1, "list generation occurred ahead of the transaction");
        repo.Operations.Should().Equal(["GetScheme", "BeginTransaction", "LockScheme", "GetScheme"],
            "once LockScheme reveals a concurrent approval, the service must abort before AddListSlots or LockStudy");
    }

    [Fact]
    public async Task ApproveSchemeAsync_StagesSlotsAfterLockSchemeAndBeforeLockStudy()
    {
        var draft = DraftBlock(slotsPerStratum: 8).Value!;
        var repo = new RecordingRandomizationRepository(draft);
        var protector = new CountingAllocationProtector();
        var service = CreateService(repo, protector, userId: "statistician-b");

        var result = await service.ApproveSchemeAsync(draft.Id, "Sign off v1", TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        protector.ProtectBatchCalls.Should().Be(1);
        protector.ProtectCalls.Should().Be(0);
        repo.Operations.Should().Equal(
            "GetScheme",
            "BeginTransaction",
            "LockScheme",
            "GetScheme",
            "AddListSlots",
            "LockStudy",
            "GetApprovedScheme",
            "SaveChanges");
    }

    [Fact]
    public async Task ApproveSchemeAsync_OnRelationalRetry_ReplaysImmutableSlotsWithoutTrackingAndKeepsAuditChainValid()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        Guid schemeId;
        Guid v1SchemeId;
        string subjectCode = "SUBJ-RETRY-01";
        await using (var setup = CreateSqliteContext(connection, "statistician-a"))
        {
            await setup.Database.EnsureCreatedAsync(ct);
            var v1 = DraftBlock(slotsPerStratum: 4).Value!;
            v1SchemeId = v1.Id;
            v1.Approve("statistician-b", "Approve v1", Clock).IsSuccess.Should().BeTrue();
            setup.RandomizationSchemes.Add(v1);
            AuditActions.Record(setup, new AuditAction(
                nameof(RandomizationScheme), v1.Id.ToString(), "APPROVE_RANDOMIZATION_SCHEME", "statistician-b", "Approve v1"));

            var draft = RandomizationScheme.Draft(Study, 2, RandomizationMethod.PermutedBlock, TwoArms, null, null,
                slotsPerStratum: 12, preferredArmProbability: 0.8, requiresKit: false, "statistician-a", "Draft v2", Clock).Value!;
            schemeId = draft.Id;
            setup.RandomizationSchemes.Add(draft);
            AuditActions.Record(setup, new AuditAction(
                nameof(RandomizationScheme), draft.Id.ToString(), "DRAFT_RANDOMIZATION_SCHEME", "statistician-a", "Draft v2"));

            var site = new SiteCode("SITE-RND-01");
            var subject = ClinicalSubject.Create(new SubjectCode(subjectCode), Study, site, 1, "enc-demo", null);
            subject.TransitionTo(SubjectState.Enrolled).IsSuccess.Should().BeTrue();
            setup.ClinicalSubjects.Add(subject);
            setup.SubjectConsents.Add(SubjectConsent.Create(
                subject.SubjectCode, new ProtocolVersionRef(Study, "1.0", 1), icfSigned: true, Clock.UtcNow, requiresReconsent: false, isPaperOverride: false));
            setup.SiteComplianceCaches.Add(SiteComplianceCache.Create(Study, site, "SITE_ACTIVATION", true, []));
            await setup.SaveChangesAsync(ct);
        }

        var failFirstSchemeUpdate = new FailFirstSchemeUpdateInterceptor();
        var trackerProbe = new TrackedSlotCountInterceptor();
        var protector = new CountingAllocationProtector();
        await using (var approveDb = CreateSqliteContext(connection, "statistician-b", failFirstSchemeUpdate, trackerProbe))
        {
            // Prime multiple schemes in the identity map before approval to verify GetSchemeAsync and GetApprovedSchemeAsync reload cleanly.
            _ = await approveDb.RandomizationSchemes.OrderBy(s => s.Version).ToListAsync(ct);

            var service = new RandomizationApplicationService(
                new RandomizationRepository(approveDb),
                new SubjectRepository(approveDb),
                new ConsentRepository(approveDb),
                new SiteComplianceRepository(approveDb),
                protector,
                new EfAuditLogWriter(approveDb),
                new FixedAuditContext("statistician-b", "Approve after transient failure"),
                Clock);

            var approved = await service.ApproveSchemeAsync(schemeId, "Approve after transient failure", ct);
            approved.IsSuccess.Should().BeTrue();
        }

        failFirstSchemeUpdate.Failures.Should().Be(1, "the first commit attempt failed after staging slots");
        protector.ProtectBatchCalls.Should().Be(1, "transient transaction retries must reuse pre-encrypted slots without re-encrypting");
        trackerProbe.MaxTrackedSlotsOnSave.Should().Be(0, "SaveChangesAsync must never scan RandomizationListSlot entries");

        await using var verifyDb = CreateSqliteContext(connection, "crc-1");
        (await verifyDb.RandomizationSchemes.AsNoTracking().SingleAsync(s => s.Id == v1SchemeId, ct)).Status
            .Should().Be(RandomizationSchemeStatus.Retired);
        (await verifyDb.RandomizationSchemes.AsNoTracking().SingleAsync(s => s.Id == schemeId, ct)).Status
            .Should().Be(RandomizationSchemeStatus.Approved);
        (await verifyDb.RandomizationListSlots.AsNoTracking().CountAsync(s => s.SchemeId == schemeId, ct))
            .Should().Be(12, "rolled-back first attempt left no duplicate slots and retry persisted all 12 slots");
        var auditEntries = await verifyDb.AuditLogEntries.AsNoTracking().ToListAsync(ct);
        auditEntries.Count(e => e.Action == "RETIRE_RANDOMIZATION_SCHEME").Should().Be(1);
        auditEntries.Count(e => e.Action == "APPROVE_RANDOMIZATION_SCHEME" && e.EntityId == schemeId.ToString()).Should().Be(1);
        AuditLogChainVerifier.Verify(auditEntries).Should().OnlyContain(r => r.Valid);

        var randomizeService = new RandomizationApplicationService(
            new RandomizationRepository(verifyDb),
            new SubjectRepository(verifyDb),
            new ConsentRepository(verifyDb),
            new SiteComplianceRepository(verifyDb),
            protector,
            new EfAuditLogWriter(verifyDb),
            new FixedAuditContext("crc-1", "Randomize enrolled subject"),
            Clock);

        var allocation = await randomizeService.RandomizeAsync(subjectCode, new RandomizeSubjectRequest(null), "Randomize enrolled subject", ct);
        allocation.IsSuccess.Should().BeTrue();
        var revealed = await randomizeService.RevealAsync(subjectCode, ct);
        revealed.Should().NotBeNull();
        revealed!.Arm.Should().BeOneOf("PLACEBO", "ACTIVE");
    }

    private static ExecutionDbContext CreateSqliteContext(SqliteConnection connection, string actor, params IInterceptor[] extra) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseSqlite(connection, sqlite => sqlite.ExecutionStrategy(deps => new ImmediateSqliteRetryStrategy(deps)))
            .AddInterceptors((IInterceptor[])[new ConcurrencyTokenIncrementInterceptor(), new GxPAuditInterceptor(() => actor, Clock), .. extra])
            .Options);

    private static RandomizationApplicationService CreateService(
        IRandomizationRepository repository,
        IAllocationProtector protector,
        string userId) =>
        new(
            repository,
            new InMemorySubjectRepository(),
            new InMemoryConsentRepository(),
            new InMemorySiteComplianceRepository(),
            protector,
            new NullAuditLogWriter(),
            new FixedAuditContext(userId, "Unit test reason"),
            Clock);

    private sealed record FixedAuditContext(string UserId, string ChangeReason) : ICurrentAuditContextAccessor;

    private sealed class CountingAllocationProtector : IAllocationProtector
    {
        private readonly AesGcmAllocationProtector _inner = new(
            Options.Create(new AllocationProtectionOptions { KeyBase64 = TestFieldProtection.EncryptionKeyBase64 }));

        public int ProtectCalls { get; private set; }

        public int ProtectBatchCalls { get; private set; }

        public string Protect(string arm, string purpose)
        {
            ProtectCalls++;
            return _inner.Protect(arm, purpose);
        }

        public IReadOnlyList<string> ProtectBatch(IReadOnlyList<(string Arm, string Purpose)> items)
        {
            ProtectBatchCalls++;
            return _inner.ProtectBatch(items);
        }

        public string Reveal(string ciphertext, string purpose) => _inner.Reveal(ciphertext, purpose);
    }

    private sealed class RecordingRandomizationRepository(
        RandomizationScheme initialScheme,
        RandomizationScheme? postSchemeLockScheme = null) : IRandomizationRepository
    {
        private bool _schemeLocked;

        public List<string> Operations { get; } = [];

        public Task LockStudyAsync(StudyCode studyCode, CancellationToken cancellationToken)
        {
            Operations.Add("LockStudy");
            return Task.CompletedTask;
        }

        public Task LockSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
        {
            Operations.Add("LockScheme");
            _schemeLocked = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RandomizationScheme>> ListSchemesAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RandomizationScheme>>([initialScheme]);

        public Task<RandomizationScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
        {
            Operations.Add("GetScheme");
            var current = _schemeLocked && postSchemeLockScheme is not null ? postSchemeLockScheme : initialScheme;
            return Task.FromResult<RandomizationScheme?>(current.Id == schemeId || _schemeLocked ? current : null);
        }

        public Task<RandomizationScheme?> GetApprovedSchemeAsync(StudyCode studyCode, CancellationToken cancellationToken)
        {
            Operations.Add("GetApprovedScheme");
            return Task.FromResult<RandomizationScheme?>(null);
        }

        public Task<int> GetLatestVersionAsync(StudyCode studyCode, CancellationToken cancellationToken) => Task.FromResult(1);

        public Task AddSchemeAsync(RandomizationScheme scheme, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddListSlotsAsync(IReadOnlyCollection<RandomizationListSlot> slots, CancellationToken cancellationToken)
        {
            Operations.Add("AddListSlots");
            return Task.CompletedTask;
        }

        public Task<RandomizationListSlot?> GetNextFreeSlotAsync(Guid schemeId, string stratumKey, CancellationToken cancellationToken) =>
            Task.FromResult<RandomizationListSlot?>(null);

        public Task<RandomizationAllocation?> GetAllocationForSubjectAsync(SubjectId subjectId, CancellationToken cancellationToken) =>
            Task.FromResult<RandomizationAllocation?>(null);

        public Task<IReadOnlyList<RandomizationAllocation>> ListAllocationsAsync(StudyCode studyCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RandomizationAllocation>>([]);

        public Task AddAllocationAsync(RandomizationAllocation allocation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<IpKitInventoryItem>> ListAssignableKitsAsync(
            StudyCode studyCode, SiteCode siteCode, DateTimeOffset at, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IpKitInventoryItem>>([]);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Operations.Add("SaveChanges");
            return Task.CompletedTask;
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> unitOfWork, CancellationToken cancellationToken)
        {
            Operations.Add("BeginTransaction");
            return unitOfWork(cancellationToken);
        }
    }

    private sealed class FailFirstSchemeUpdateInterceptor : DbCommandInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void MaybeFail(DbCommand command)
        {
            if (Failures == 0 && command.CommandText.Contains("UPDATE \"randomization_schemes\"", StringComparison.OrdinalIgnoreCase))
            {
                Failures++;
                throw new PostgresException("simulated serialization failure", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
            }
        }
    }

    private sealed class TrackedSlotCountInterceptor : SaveChangesInterceptor
    {
        public int MaxTrackedSlotsOnSave { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } db)
            {
                int trackedSlots = db.ChangeTracker.Entries<RandomizationListSlot>().Count();
                if (trackedSlots > MaxTrackedSlotsOnSave)
                {
                    MaxTrackedSlotsOnSave = trackedSlots;
                }
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ImmediateSqliteRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
        protected override bool ShouldRetryOn(Exception exception) =>
            exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };
    }

    private sealed class NullAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            string entityName,
            string recordId,
            string action,
            string userId,
            string changeReason,
            IReadOnlyDictionary<string, object?>? oldValues,
            IReadOnlyDictionary<string, object?>? newValues,
            CancellationToken ct) => Task.CompletedTask;
    }
}
