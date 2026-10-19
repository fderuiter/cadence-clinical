using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Regression coverage for mutations that were silently dropped: the observation, form and query application
/// services loaded aggregates through the <c>AsNoTracking()</c> read path, mutated them, then called
/// <c>SaveChangesAsync</c>, which persisted nothing. Each test loads through the repository's tracked
/// <c>GetForUpdateAsync</c>, mutates, saves, and re-reads through a fresh context to prove the change reached the
/// database. Uses SQLite for the same reason as <see cref="ConsentRepositoryTests"/>.
/// </summary>
public class MutationRepositoryTrackingTests
{
    private static readonly StudyCode Study = new("STUDY-TRACKING");
    private static readonly SubjectCode Subject = new("SUBJ-TRACKING");

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);

    private static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ObservationRepository_GetForUpdateAsync_ThenVerifySdv_PersistsVerification()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = OpenSqliteConnection();
        await EnsureSchemaAsync(connection);

        var observation = ClinicalObservation.Create(
            Study, Subject, new DomainCode("VS"), new TestCode("SYSBP"), "Systolic BP", new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), value: 120).Value!;
        var seedRepository = new ObservationRepository(CreateContext(connection));
        await seedRepository.AddAsync(observation, ct);
        await seedRepository.SaveChangesAsync(ct);

        var repository = new ObservationRepository(CreateContext(connection));
        var loaded = await repository.GetForUpdateAsync(observation.Id, ct);
        loaded!.VerifySdv("monitor-1", new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc)).IsSuccess.Should().BeTrue();
        await repository.SaveChangesAsync(ct);

        await using var verify = CreateContext(connection);
        var persisted = await verify.ClinicalObservations.AsNoTracking().SingleAsync(o => o.Id == observation.Id, ct);
        persisted.SdvVerifiedBy.Should().Be("monitor-1");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task FormSubmissionRepository_GetForUpdateAsync_ThenComplete_PersistsStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = OpenSqliteConnection();
        await EnsureSchemaAsync(connection);

        var submission = FormSubmission.Initialize(Study, Subject, new FormId("VS_FORM"), initialPayload: "{}").Value!;
        var seedRepository = new FormSubmissionRepository(CreateContext(connection));
        await seedRepository.AddAsync(submission, ct);
        await seedRepository.SaveChangesAsync(ct);

        var repository = new FormSubmissionRepository(CreateContext(connection));
        var loaded = await repository.GetForUpdateAsync(submission.Id, ct);
        loaded!.Complete().IsSuccess.Should().BeTrue();
        await repository.SaveChangesAsync(ct);

        await using var verify = CreateContext(connection);
        var persisted = await verify.FormSubmissions.AsNoTracking().SingleAsync(f => f.Id == submission.Id, ct);
        persisted.Status.Should().Be(FormSubmissionStatus.Completed);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ClinicalQueryRepository_GetForUpdateAsync_ThenAnswer_PersistsResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = OpenSqliteConnection();
        await EnsureSchemaAsync(connection);

        var query = ClinicalQuery.Raise(Study, Subject, new TestCode("SYSBP"), "Value out of range").Value!;
        var seedRepository = new ClinicalQueryRepository(CreateContext(connection));
        await seedRepository.AddAsync(query, ct);
        await seedRepository.SaveChangesAsync(ct);

        var repository = new ClinicalQueryRepository(CreateContext(connection));
        var loaded = await repository.GetForUpdateAsync(query.Id, ct);
        loaded!.Answer("Confirmed with source", "site-user-1").IsSuccess.Should().BeTrue();
        await repository.SaveChangesAsync(ct);

        await using var verify = CreateContext(connection);
        var persisted = await verify.ClinicalQueries.AsNoTracking().SingleAsync(q => q.Id == query.Id, ct);
        persisted.Status.Should().Be(QueryStatus.Answered);
    }
}
