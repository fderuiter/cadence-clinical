using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Regression coverage for mutations that were silently dropped: an application service loaded aggregates
/// through the <c>AsNoTracking()</c> read path, mutated them, then called <c>SaveChangesAsync</c>, which persisted
/// nothing. Each test loads through the repository's tracked
/// <c>Get…ForUpdateAsync</c>, mutates, saves, and re-reads through a fresh context to prove the change reached the
/// database. Uses SQLite for the same reason as <see cref="ConsentRepositoryTests"/>.
/// </summary>
public class MutationRepositoryTrackingTests
{
    private static readonly DateTime SignedAt = new(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SignatureManifest ChairSignature = SignatureManifest.Create(
        SignerId: "chair-user-1",
        SignerRole: "CEC_CHAIR",
        SigningReason: "I confirm this clinical endpoint adjudication determination is final",
        SignedAt: SignedAt,
        PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"); // pragma: allowlist secret

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
    public async Task CecRepository_GetDossierForUpdateAsync_ThenAdjudicate_PersistsDetermination()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = OpenSqliteConnection();
        await EnsureSchemaAsync(connection);

        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-TRACKING",
            eventId: "EVT-TRACKING",
            studyId: "STUDY-TRACKING",
            subjectId: "SUBJ-TRACKING",
            maskedSubjectId: "MASK-TRACKING",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");
        var seedRepository = new CecRepository(CreateContext(connection));
        await seedRepository.AddDossierAsync(dossier, ct);
        await seedRepository.SaveChangesAsync(ct);

        var repository = new CecRepository(CreateContext(connection));
        var loaded = await repository.GetDossierForUpdateAsync(dossier.Id, ct);
        loaded!.AdjudicateChair(
            finalClassification: "ISCHEMIC_STROKE",
            finalSubtype: null,
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "Confirmed by MRI",
            signatureManifest: ChairSignature,
            finalizedAt: SignedAt).IsSuccess.Should().BeTrue();
        await repository.SaveChangesAsync(ct);

        await using var verify = CreateContext(connection);
        var persisted = await verify.CecDossiers.AsNoTracking().SingleAsync(d => d.Id == dossier.Id, ct);
        persisted.WorkflowStatus.Should().Be("FINALIZED_BY_CHAIR");
        persisted.FinalConsensusClassification.Should().Be("ISCHEMIC_STROKE");
    }
}
