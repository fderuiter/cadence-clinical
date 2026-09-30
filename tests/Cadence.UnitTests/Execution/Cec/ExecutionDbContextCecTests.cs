using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Cec;
using Cadence.Apps.Execution.Domain.Forms;
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
        await context.SaveChangesAsync();

        var auditEntriesFirstSave = await context.AuditLogEntries.ToListAsync();
        auditEntriesFirstSave.Should().NotIsEmpty();
        auditEntriesFirstSave.Should().Contain(e => e.EntityName == nameof(CecDossier));

        var manifest = SignatureManifest.Create(
            SignerId: "chair_user_1",
            SignerRole: "CEC_CHAIR",
            SigningReason: "I confirm this clinical endpoint adjudication determination is final",
            SignedAt: DateTime.UtcNow,
            PayloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            SignerName: "Dr. Chair");

        dossier.AdjudicateChair(
            finalClassification: "ISCHEMIC_STROKE",
            finalSubtype: "THROMBOTIC",
            finalAdjudicatedOnsetDate: "2026-02-10",
            adjudicationRationale: "Confirmed by MRI",
            signatureManifest: manifest,
            finalizedAt: DateTime.UtcNow);

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
            finalizedAt: DateTime.UtcNow,
            createdBy: "chair_user_1");

        context.CecChairAdjudications.Add(adjudication);
        await context.SaveChangesAsync();

        var auditEntriesSecondSave = await context.AuditLogEntries.ToListAsync();
        auditEntriesSecondSave.Should().Contain(e => e.EntityName == nameof(CecChairAdjudication));

        var cecDossierAuditChain = await context.AuditLogEntries
            .Where(e => e.EntityName == nameof(CecDossier))
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync();

        cecDossierAuditChain.Count.Should().BeGreaterThanOrEqualTo(2);
        cecDossierAuditChain[1].PredecessorHash.Should().Be(cecDossierAuditChain[0].BlockHash);
    }
}
