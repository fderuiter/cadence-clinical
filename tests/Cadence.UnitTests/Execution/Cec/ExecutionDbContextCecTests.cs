using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Kernel.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Cec;

public sealed class ExecutionDbContextCecTests
{
    [Fact(DisplayName = "SaveChangesAsync_WhenCecEntitiesSaved_WritesMerkleChainedAuditLogEntries")]
    public async Task SaveChangesAsync_WhenCecEntitiesSaved_WritesMerkleChainedAuditLogEntries()
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName: $"CecAuditTest_{Guid.NewGuid():N}")
            .AddInterceptors(new GxPAuditInterceptor(() => "chair_user_1", SystemClock.Instance))
            .Options;

        await using var context = new ExecutionDbContext(options);

        var dossier = CecDossier.Initialize(
            dossierId: "DOSSIER-MACE-001",
            eventId: "EVT-001",
            studyId: "STUDY-101",
            subjectId: "SUBJ-001",
            maskedSubjectId: "MASK-001",
            candidateEventType: "CARDIOVASCULAR",
            reportedTerm: "Myocardial Infarction",
            reportedOnsetDate: "2026-02-10");

        context.CecDossiers.Add(dossier);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auditEntriesFirstSave = await context.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken);
        auditEntriesFirstSave.Should().NotBeEmpty();
        auditEntriesFirstSave.Should().Contain(e => e.EntityName == nameof(CecDossier));

        var manifest = SignatureManifest.Create(
            SignerId: "chair_user_1",
            SignerRole: "CEC_CHAIR",
            SigningReason: "I confirm this clinical endpoint adjudication determination is final",
            SignedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
            PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            SignerName: "Dr. Chair");

        dossier.AdjudicateChair(
            finalClassification: "ISCHEMIC_STROKE",
            finalSubtype: "THROMBOTIC",
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "Confirmed by MRI",
            signatureManifest: manifest,
            finalizedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));

        var adjudication = CecChairAdjudication.Create(
            determinationId: "DET-123456",
            dossierId: "DOSSIER-MACE-001",
            chairId: "chair_user_1",
            chairName: "Dr. Chair",
            finalClassification: "ISCHEMIC_STROKE",
            finalSubtype: "THROMBOTIC",
            finalAdjudicatedOnsetDate: "2026-02-10",
            bindingReader: "CHAIR_CUSTOM_DETERMINATION",
            criteriaConfirmed: ["MRI_CONFIRMED"],
            adjudicationRationale: "Confirmed by MRI",
            signatureManifest: manifest,
            finalizedAt: new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc),
            createdBy: "chair_user_1");

        context.CecChairAdjudications.Add(adjudication);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var auditEntriesSecondSave = await context.AuditLogEntries.ToListAsync(TestContext.Current.CancellationToken);
        auditEntriesSecondSave.Should().Contain(e => e.EntityName == nameof(CecChairAdjudication));

        var cecDossierAuditChain = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(CecDossier))
            .OrderBy(e => e.VersionIndex)
            .ToListAsync(TestContext.Current.CancellationToken);

        cecDossierAuditChain.Count.Should().BeGreaterThanOrEqualTo(2);
        cecDossierAuditChain[1].PredecessorHash.Should().Be(cecDossierAuditChain[0].BlockHash);
    }
}
