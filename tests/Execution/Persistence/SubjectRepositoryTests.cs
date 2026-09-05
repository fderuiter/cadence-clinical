using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Polly;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-013`'s <see cref="SubjectRepository"/>: sequential enrollment-index assignment
/// (`02_technical_design.md` §4.2), the dual-lookup methods' independent null-on-miss contracts, and the
/// advisory-lock statement's study-code-derived (never global) key. A real Postgres connection is required to
/// exercise `pg_advisory_xact_lock` itself, so these tests use file-backed SQLite (a real relational provider
/// with real transactions, unlike InMemory) — <see cref="SubjectRepository.EnrollNextIndexAsync"/> skips the
/// Postgres-only advisory-lock statement under any non-Npgsql provider, per its own doc comment, while still
/// exercising the surrounding transaction/lock *scoping* end to end. The N-parallel-callers test below relies
/// on an empirically-verified property of `Microsoft.Data.Sqlite`: `BeginTransactionAsync` blocks a second
/// connection until the first connection's transaction commits (it acquires a write-intent lock immediately,
/// not only on the first write) — so the exact same "open a transaction, read, hold, insert, commit" sequence
/// this repository uses for the Postgres advisory lock also happens to genuinely serialize concurrent callers
/// under SQLite, even without any Postgres-specific mechanism. This is a real, structural proof of the
/// enroll-then-insert transaction *scoping*, not a substitute for MIG-SUBJ-028's own Testcontainers-backed
/// Postgres integration test (which remains the authoritative proof of the actual `pg_advisory_xact_lock`
/// cross-study non-blocking behavior against the real target database).
/// @req:PRD-SUB-001
/// </summary>
public class SubjectRepositoryTests
{
    private static readonly StudyCode StudyA = new("STUDY-ENROLL-A");
    private static readonly StudyCode StudyB = new("STUDY-ENROLL-B");

    private static string NewTempDbPath() => Path.Combine(Path.GetTempPath(), $"subject-repo-{Guid.NewGuid():N}.db");

    private static DbContextOptions<ExecutionDbContext> FileOptions(string path) =>
        new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite($"Data Source={path}").Options;

    private static async Task<string> CreateSchemaAsync()
    {
        string path = NewTempDbPath();
        await using var context = new ExecutionDbContext(FileOptions(path));
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return path;
    }

    private static SubjectRepository CreateRepository(string path, ResiliencePipeline? pipeline = null) =>
        new(new ExecutionDbContext(FileOptions(path)), pipeline ?? SaveChangesResiliencePipelineFactory.Create());

    private static void DeleteDbFileBestEffort(string path)
    {
        foreach (var candidate in new[] { path, path + "-journal", path + "-wal", path + "-shm" })
        {
            try
            {
                File.Delete(candidate);
            }
            catch (IOException)
            {
                // Best-effort temp-file cleanup only; a lingering file does not fail the test.
            }
        }
    }

    // ---- EnrollNextIndexAsync: sequential index assignment (single-caller) -------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollNextIndexAsync_GivenEmptyStudy_ReturnsZero()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);
            int next = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);

            next.Should().Be(0);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollNextIndexAsync_GivenStudyWithSubjectsAtIndicesZeroOneTwo_ReturnsThree()
    {
        string path = await CreateSchemaAsync();
        try
        {
            await using (var seed = new ExecutionDbContext(FileOptions(path)))
            {
                for (int i = 0; i < 3; i++)
                {
                    seed.ClinicalSubjects.Add(ClinicalSubject.Create(
                        new SubjectCode($"SUBJ-SEED-{i}"), StudyA, siteCode: null,
                        enrollmentIndex: i, encryptedDemographics: null, stratFactors: null));
                }

                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var repository = CreateRepository(path);
            int next = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);

            next.Should().Be(3);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollNextIndexAsync_ThenAddAsyncThenSaveChangesAsync_PersistsSubjectAtComputedIndexAndCommitsTransaction()
    {
        // Single-caller structural smoke test: the transaction EnrollNextIndexAsync opens and leaves pending
        // must actually participate in the subsequent insert and be committed by SaveChangesAsync.
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);
            int next = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);
            var subject = ClinicalSubject.Create(
                new SubjectCode("SUBJ-STRUCTURAL"), StudyA, siteCode: null,
                enrollmentIndex: next, encryptedDemographics: null, stratFactors: null);
            await repository.AddAsync(subject, TestContext.Current.CancellationToken);
            await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

            await using var readContext = new ExecutionDbContext(FileOptions(path));
            var persisted = await readContext.ClinicalSubjects.AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken);
            persisted.EnrollmentIndex.Should().Be(0);
            persisted.SubjectCode.Should().Be(subject.SubjectCode);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollNextIndexAsync_CalledTwiceSequentiallyOnSameRepository_ProducesDistinctSequentialIndices()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);

            int first = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);
            await repository.AddAsync(
                ClinicalSubject.Create(new SubjectCode("SUBJ-SEQ-0"), StudyA, null, first, null, null),
                TestContext.Current.CancellationToken);
            await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

            int second = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);
            await repository.AddAsync(
                ClinicalSubject.Create(new SubjectCode("SUBJ-SEQ-1"), StudyA, null, second, null, null),
                TestContext.Current.CancellationToken);
            await repository.SaveChangesAsync(TestContext.Current.CancellationToken);

            first.Should().Be(0);
            second.Should().Be(1);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    // ---- EnrollNextIndexAsync: genuine parallel concurrency ------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task EnrollNextIndexAsync_GivenNParallelCallersForSameStudy_YieldsDistinctSequentialIndicesWithNoDuplicatesOrGaps()
    {
        const int callerCount = 8;
        string path = await CreateSchemaAsync();
        try
        {
            var results = new ConcurrentBag<int>();

            async Task EnrollOneAsync(int callerIndex)
            {
                var repository = CreateRepository(path);
                int index = await repository.EnrollNextIndexAsync(StudyA, TestContext.Current.CancellationToken);

                // Widen the race window between computing the index and actually inserting -- if the
                // transaction/lock scoping were wrong (e.g. the lock released before the insert), this delay
                // would make two callers collide on the same computed index.
                await Task.Delay(10, TestContext.Current.CancellationToken);

                await repository.AddAsync(
                    ClinicalSubject.Create(new SubjectCode($"SUBJ-PARALLEL-{callerIndex}"), StudyA, null, index, null, null),
                    TestContext.Current.CancellationToken);
                await repository.SaveChangesAsync(TestContext.Current.CancellationToken);
                results.Add(index);
            }

            await Task.WhenAll(Enumerable.Range(0, callerCount).Select(EnrollOneAsync));

            results.Should().HaveCount(callerCount);
            results.Distinct().Should().HaveCount(callerCount, "no two concurrent callers may be assigned the same index");
            results.OrderBy(x => x).Should().Equal(Enumerable.Range(0, callerCount),
                "the assigned indices must be exactly {0}..{1} with no gaps", 0, callerCount - 1);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    // ---- Advisory lock key derivation (unit-level -- Postgres itself cannot be exercised here) --------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void AdvisoryLockSql_ForStudy_UsesTheSameParameterizedSqlTemplateRegardlessOfStudyCode()
    {
        var sqlForA = AdvisoryLockSql.ForStudy(StudyA);
        var sqlForB = AdvisoryLockSql.ForStudy(StudyB);

        sqlForA.Format.Should().Be(sqlForB.Format);
        sqlForA.Format.Should().Contain("pg_advisory_xact_lock").And.Contain("hashtext");
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public void AdvisoryLockSql_ForStudy_DerivesItsLockKeyFromTheStudyCodeNotAGlobalConstant()
    {
        var sqlForA = AdvisoryLockSql.ForStudy(StudyA);
        var sqlForB = AdvisoryLockSql.ForStudy(StudyB);

        // The interpolation hole is the ONLY thing that may differ between two different studies -- proving
        // the lock key tracks the study code itself, never a hardcoded/global value. This is exactly the
        // boundary check the ticket's AC requires ("verify via a unit-level assertion that the lock key is
        // derived from the study code, not a global constant") without needing a live Postgres connection to
        // observe actual lock contention (which is Postgres-specific and deferred to MIG-SUBJ-028).
        sqlForA.GetArguments().Should().ContainSingle().Which.Should().Be(StudyA.Value);
        sqlForB.GetArguments().Should().ContainSingle().Which.Should().Be(StudyB.Value);
        sqlForA.GetArguments().Should().NotEqual(sqlForB.GetArguments());
    }

    // ---- GetByIdAsync / GetByCodeAsync: dual-lookup method isolation -----------------------------------

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByIdAsync_GivenNoMatchingRow_ReturnsNull()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);
            var found = await repository.GetByIdAsync(Guid.NewGuid().ToString(), TestContext.Current.CancellationToken);

            found.Should().BeNull();
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByIdAsync_GivenMalformedIdString_ReturnsNullWithoutThrowing()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);
            var act = async () => await repository.GetByIdAsync("not-a-guid", TestContext.Current.CancellationToken);

            (await act.Should().NotThrowAsync()).Which.Should().BeNull();
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByIdAsync_GivenMatchingSurrogateId_ReturnsTheSubjectTracked()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-BY-ID"), StudyA, null, 0, null, null);
            await using (var seed = new ExecutionDbContext(FileOptions(path)))
            {
                seed.ClinicalSubjects.Add(subject);
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var context = new ExecutionDbContext(FileOptions(path));
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());

            var found = await repository.GetByIdAsync(subject.Id.Value.ToString(), TestContext.Current.CancellationToken);

            found.Should().NotBeNull();
            found!.SubjectCode.Should().Be(subject.SubjectCode);
            // Tracked (not AsNoTracking): a mutation followed by SaveChangesAsync must be enough to persist
            // it, matching how SubjectApplicationService.AdvanceStateAsync uses this method.
            context.Entry(found).State.Should().Be(EntityState.Unchanged);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByCodeAsync_GivenNoMatchingRow_ReturnsNull()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var repository = CreateRepository(path);
            var found = await repository.GetByCodeAsync("NO-SUCH-SUBJECT", TestContext.Current.CancellationToken);

            found.Should().BeNull();
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByCodeAsync_GivenMatchingSubjectCode_ReturnsTheSubjectTracked()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-BY-CODE"), StudyA, null, 0, null, null);
            await using (var seed = new ExecutionDbContext(FileOptions(path)))
            {
                seed.ClinicalSubjects.Add(subject);
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var context = new ExecutionDbContext(FileOptions(path));
            var repository = new SubjectRepository(context, SaveChangesResiliencePipelineFactory.Create());

            var found = await repository.GetByCodeAsync("SUBJ-BY-CODE", TestContext.Current.CancellationToken);

            found.Should().NotBeNull();
            found!.Id.Should().Be(subject.Id);
            context.Entry(found).State.Should().Be(EntityState.Unchanged);
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByIdAsync_GivenAStringThatIsOnlyValidAsACodeNotAGuid_DoesNotFallBackToCodeLookup()
    {
        // Proves method isolation (the ticket's explicit requirement): GetByIdAsync never internally tries
        // the code lookup -- that sequencing is the application service's job, not this repository's.
        string path = await CreateSchemaAsync();
        try
        {
            var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-ISOLATION"), StudyA, null, 0, null, null);
            await using (var seed = new ExecutionDbContext(FileOptions(path)))
            {
                seed.ClinicalSubjects.Add(subject);
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var repository = CreateRepository(path);
            var found = await repository.GetByIdAsync("SUBJ-ISOLATION", TestContext.Current.CancellationToken);

            found.Should().BeNull();
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task GetByCodeAsync_GivenAValidSurrogateIdStringThatIsNotAKnownCode_DoesNotFallBackToIdLookup()
    {
        string path = await CreateSchemaAsync();
        try
        {
            var subject = ClinicalSubject.Create(new SubjectCode("SUBJ-ISOLATION-2"), StudyA, null, 0, null, null);
            await using (var seed = new ExecutionDbContext(FileOptions(path)))
            {
                seed.ClinicalSubjects.Add(subject);
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var repository = CreateRepository(path);
            var found = await repository.GetByCodeAsync(subject.Id.Value.ToString(), TestContext.Current.CancellationToken);

            found.Should().BeNull();
        }
        finally
        {
            DeleteDbFileBestEffort(path);
        }
    }
}
