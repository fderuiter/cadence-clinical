using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
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
/// Coverage for `MIG-SUBJ-013`'s <see cref="ConsentRepository"/>: <see cref="ConsentRepository.ExistsAsync"/>
/// and <see cref="ConsentRepository.AddAsync"/> basic CRUD round-trips. Uses SQLite, not InMemory — matching
/// `SubjectConsentConfigurationTests`' established precedent (MIG-SUBJ-012) — because EF Core 10.0.11's
/// InMemory provider throws materializing a <c>ComplexProperty</c> member (<c>ProtocolVersion.StudyCode</c>)
/// that also carries a value converter, which <see cref="ConsentRepository.ExistsAsync"/>'s query relies on.
/// @req:PRD-SUB-001
/// </summary>
public class ConsentRepositoryTests
{
    private static readonly SubjectCode SubjectCode = new("SUBJ-CONSENT-REPO");
    private static readonly StudyCode StudyCode = new("STUDY-CONSENT-REPO");
    private static readonly ProtocolVersionRef Version = new(StudyCode, "v1.0", 1);

    private static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static ExecutionDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>().UseSqlite(connection).Options);

    private static ConsentRepository CreateRepository(SqliteConnection connection) =>
        new(CreateContext(connection), SaveChangesResiliencePipelineFactory.Create());

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ExistsAsync_GivenNoMatchingConsent_ReturnsFalse()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var repository = CreateRepository(connection);
        bool exists = await repository.ExistsAsync(SubjectCode, StudyCode, versionIndex: 1, TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var consent = SubjectConsent.Create(SubjectCode, Version, icfSigned: true,
            icfSignedDate: null, requiresReconsent: false, isPaperOverride: false, new FakeClock());

        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(consent, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        bool exists = await readRepository.ExistsAsync(SubjectCode, StudyCode, Version.VersionIndex, TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-SUB-001")]
    public async Task ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var consent = SubjectConsent.Create(SubjectCode, Version, icfSigned: true,
            icfSignedDate: null, requiresReconsent: false, isPaperOverride: false, new FakeClock());
        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(consent, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        bool exists = await readRepository.ExistsAsync(SubjectCode, StudyCode, versionIndex: 99, TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }
}
