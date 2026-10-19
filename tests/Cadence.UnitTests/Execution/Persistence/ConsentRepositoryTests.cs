using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for `MIG-SUBJ-013`'s <see cref="ConsentRepository"/>: <see cref="ConsentRepository.ExistsAsync"/>
/// and <see cref="ConsentRepository.AddAsync"/> basic CRUD round-trips, plus `MIG-SUBJ-018`'s later additions
/// <see cref="ConsentRepository.GetAsync"/> and <see cref="ConsentRepository.GetOtherRequiringReconsentAsync"/>.
/// Uses SQLite, not InMemory — matching
/// `SubjectConsentConfigurationTests`' established precedent (MIG-SUBJ-012) — because EF Core 10.0.11's
/// InMemory provider throws materializing a <c>ComplexProperty</c> member (<c>ProtocolVersion.StudyCode</c>)
/// that also carries a value converter, which <see cref="ConsentRepository.ExistsAsync"/>'s query relies on.
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
        new(CreateContext(connection));

    private static readonly DateTimeOffset SignedDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
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
    public async Task AddAsync_ThenSaveChangesAsync_ThenExistsAsync_GivenMatchingSubjectStudyAndVersionIndex_ReturnsTrue()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var consent = SubjectConsent.Create(SubjectCode, Version, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: false, isPaperOverride: false);

        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(consent, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        bool exists = await readRepository.ExistsAsync(SubjectCode, StudyCode, Version.VersionIndex, TestContext.Current.CancellationToken);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_GivenSameSubjectAndStudyButDifferentVersionIndex_ReturnsFalse()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var consent = SubjectConsent.Create(SubjectCode, Version, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: false, isPaperOverride: false);
        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(consent, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        bool exists = await readRepository.ExistsAsync(SubjectCode, StudyCode, versionIndex: 99, TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_GivenNoMatchingRow_ReturnsNull()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var repository = CreateRepository(connection);
        var found = await repository.GetAsync(SubjectCode, StudyCode, versionIndex: 1, TestContext.Current.CancellationToken);

        found.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_GivenMatchingRow_ReturnsTheTrackedEntity()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var consent = SubjectConsent.Create(SubjectCode, Version, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);
        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(consent, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        var found = await readRepository.GetAsync(SubjectCode, StudyCode, Version.VersionIndex, TestContext.Current.CancellationToken);

        found.Should().NotBeNull();
        found!.RequiresReconsent.Should().BeTrue();
        found.ProtocolVersion.VersionTag.Should().Be(Version.VersionTag);
    }

    [Fact]
    public async Task GetOtherRequiringReconsentAsync_ReturnsOnlyOtherRowsForTheSameSubjectRequiringReconsent()
    {
        using var connection = OpenSqliteConnection();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        var otherSubjectVersion = new ProtocolVersionRef(StudyCode, "v1.0", 1);
        var sameSubjectClearedVersion = new ProtocolVersionRef(StudyCode, "v2.0", 2);
        var sameSubjectExcludedVersion = new ProtocolVersionRef(StudyCode, "v3.0", 3);
        var differentSubjectCode = new SubjectCode("SUBJ-CONSENT-REPO-OTHER");

        // Row 1: same subject, requires reconsent, different version index -> must be returned.
        var requiresReconsentRow = SubjectConsent.Create(SubjectCode, otherSubjectVersion, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);
        // Row 2: same subject, does NOT require reconsent -> must not be returned.
        var doesNotRequireReconsentRow = SubjectConsent.Create(SubjectCode, sameSubjectClearedVersion, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: false, isPaperOverride: false);
        // Row 3: same subject, requires reconsent, but is the excluded version index -> must not be returned.
        var excludedVersionRow = SubjectConsent.Create(SubjectCode, sameSubjectExcludedVersion, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);
        // Row 4: a different subject entirely, requires reconsent -> must not be returned (boundary check).
        var differentSubjectRow = SubjectConsent.Create(differentSubjectCode, otherSubjectVersion, icfSigned: true,
            icfSignedDate: SignedDate, requiresReconsent: true, isPaperOverride: false);

        var writeRepository = CreateRepository(connection);
        await writeRepository.AddAsync(requiresReconsentRow, TestContext.Current.CancellationToken);
        await writeRepository.AddAsync(doesNotRequireReconsentRow, TestContext.Current.CancellationToken);
        await writeRepository.AddAsync(excludedVersionRow, TestContext.Current.CancellationToken);
        await writeRepository.AddAsync(differentSubjectRow, TestContext.Current.CancellationToken);
        await writeRepository.SaveChangesAsync(TestContext.Current.CancellationToken);

        var readRepository = CreateRepository(connection);
        var others = await readRepository.GetOtherRequiringReconsentAsync(
            SubjectCode, StudyCode, excludeVersionIndex: sameSubjectExcludedVersion.VersionIndex, TestContext.Current.CancellationToken);

        others.Should().ContainSingle().Which.Id.Should().Be(requiresReconsentRow.Id);
    }
}
