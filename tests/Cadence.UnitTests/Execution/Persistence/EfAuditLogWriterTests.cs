using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;
using Cadence.Core.Domain.Audit;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Polly;

using Xunit;

namespace Cadence.UnitTests.Execution.Persistence;

/// <summary>
/// Coverage for the single audit writer (#4768): <see cref="EfAuditLogWriter"/> only labels the next save, and
/// <see cref="GxPAuditInterceptor"/> writes exactly one entry per changed entity on one chain per aggregate
/// type, which <see cref="AuditLogChainVerifier"/> verifies end to end.
/// @req:PRD-SYS-001
/// </summary>
[GxPRequirement("PRD-SYS-001")]
public class EfAuditLogWriterTests
{
    private const string Actor = "qa.reviewer@site.org";

    private static ExecutionDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName)
            .AddInterceptors(new GxPAuditInterceptor(() => Actor, SystemClock.Instance))
            .Options);

    private static ClinicalSubject NewSubject(string code) =>
        ClinicalSubject.Create(new SubjectCode(code), new StudyCode("STUDY-CHAIN"), siteCode: null,
            enrollmentIndex: 1, encryptedDemographics: "cipher-1", stratFactors: null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SubjectUpdate_WritesOneEntryPerChange_AndTheChainVerifies()
    {
        string db = Guid.NewGuid().ToString();
        SubjectId id;
        await using (var context = CreateContext(db))
        {
            var writer = new EfAuditLogWriter(context);
            var subject = NewSubject("SUBJ-ONE");
            id = subject.Id;
            context.ClinicalSubjects.Add(subject);
            await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "ENROLL_SUBJECT", "user-1", "Enrollment", null, null, Ct);
            await context.SaveChangesAsync(Ct);
        }

        await using (var context = CreateContext(db))
        {
            var writer = new EfAuditLogWriter(context);
            var subject = await context.ClinicalSubjects.SingleAsync(s => s.Id == id, Ct);
            subject.UpdateDemographics("cipher-2");
            subject.ReasonForChange = "Correction";
            await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "UPDATE_DEMOGRAPHICS", "user-1", "Correction", null, null, Ct);
            await context.SaveChangesAsync(Ct);
        }

        await using var verify = CreateContext(db);
        var entries = await verify.AuditLogEntries.AsNoTracking().ToListAsync(Ct);

        entries.Should().HaveCount(2, "one entry per change, not one from the writer and another from the interceptor");
        entries.Should().OnlyContain(e => e.EntityName == nameof(ClinicalSubject) && e.EntityId == id.ToString());
        entries.Select(e => e.Action).Should().BeEquivalentTo("ENROLL_SUBJECT", "UPDATE_DEMOGRAPHICS");
        var update = entries.Single(e => e.Action == "UPDATE_DEMOGRAPHICS");
        update.UserId.Should().Be("user-1");
        update.ReasonForChange.Should().Be("Correction");
        update.VersionIndex.Should().Be(2, "the entry carries the entity's own version");

        AuditLogChainVerifier.Verify(entries).Should().ContainSingle()
            .Which.Should().Be(new AuditLogChainResult(nameof(ClinicalSubject), true, 2, 2, null));
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ChangeWithoutARecordedAction_IsStillAudited_WithTheEntityStateAsAction()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        context.ClinicalSubjects.Add(NewSubject("SUBJ-PLAIN"));

        await context.SaveChangesAsync(Ct);

        var entry = await context.AuditLogEntries.AsNoTracking().SingleAsync(Ct);
        entry.Action.Should().Be("Added");
        entry.UserId.Should().Be(Actor);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ActionWithNoEntityChange_IsWrittenAsAnActionOnlyEntryOnTheSameChain()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context);
        var subject = NewSubject("SUBJ-BLOCKED");
        context.ClinicalSubjects.Add(subject);
        await context.SaveChangesAsync(Ct);

        await writer.WriteAsync(nameof(ClinicalSubject), subject.Id.ToString(), "BLOCKED_ENROLLMENT", "user-2", "Site not activated",
            oldValues: null, newValues: new Dictionary<string, object?> { ["target"] = "ENROLLED" }, Ct);
        await context.SaveChangesAsync(Ct);

        var entries = await context.AuditLogEntries.AsNoTracking().ToListAsync(Ct);
        var blocked = entries.Single(e => e.Action == "BLOCKED_ENROLLMENT");
        blocked.VersionIndex.Should().Be(0, "no entity version changed");
        blocked.UserId.Should().Be("user-2");
        blocked.ChangesJson.Should().Contain("ENROLLED");
        AuditLogChainVerifier.Verify(entries).Should().OnlyContain(r => r.Valid);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task WriteAsync_DoesNotItselfSave()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var writer = new EfAuditLogWriter(context);

        await writer.WriteAsync(nameof(ClinicalSubject), "SUBJ-1", "ENROLL_SUBJECT", "user-1", "Enrollment", null, null, Ct);

        (await context.AuditLogEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_DetectsATamperedEntry()
    {
        var chain = BuildChain("ClinicalSubject", 3);
        var original = chain[1];
        chain[1] = new AuditLogEntry
        {
            Id = original.Id,
            EntityName = original.EntityName,
            EntityId = original.EntityId,
            Action = original.Action,
            Timestamp = original.Timestamp,
            UserId = "someone-else",
            ReasonForChange = original.ReasonForChange,
            VersionIndex = original.VersionIndex,
            ChangesJson = original.ChangesJson,
            PredecessorHash = original.PredecessorHash,
            BlockHash = original.BlockHash,
        };

        var result = AuditLogChainVerifier.Verify(chain).Single();

        result.Valid.Should().BeFalse();
        result.VerifiedCount.Should().Be(1);
        result.Error.Should().Contain("block hash mismatch");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_DetectsAFork()
    {
        var chain = BuildChain("ClinicalSubject", 2);
        chain.Add(Entry("ClinicalSubject", "fork", chain[0].BlockHash));

        var result = AuditLogChainVerifier.Verify(chain).Single();

        result.Valid.Should().BeFalse();
        result.Error.Should().Contain("forks");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void Verifier_VerifiesLegacyTableNameChainsAlongsideNewOnes_WithoutRewritingThem()
    {
        // Rows written before #4768 by the old explicit writer are keyed by table name ("clinical_subjects").
        // They stay as their own read-only chain; new entries chain under the aggregate type name.
        var entries = BuildChain("clinical_subjects", 2).Concat(BuildChain(nameof(ClinicalSubject), 3)).ToList();

        var results = AuditLogChainVerifier.Verify(entries);

        results.Should().HaveCount(2).And.OnlyContain(r => r.Valid);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task FormSubmissionUnlock_CommitsRevokeSignatureAuditEntry_AndTheChainVerifies()
    {
        string db = Guid.NewGuid().ToString();
        string formIdStr;

        await using (var context = CreateContext(db))
        {
            var formRepo = new FormSubmissionRepository(context, ResiliencePipeline.Empty);
            var obsRepo = new ObservationRepository(context, ResiliencePipeline.Empty);
            var ucum = new UcumNormalizationService();
            var audit = new FakeCurrentAuditContextAccessor("pi-investigator-01", "Initial form submission");
            var auditLogWriter = new EfAuditLogWriter(context);

            var service = new FormSubmissionApplicationService(
                formRepo,
                obsRepo,
                ucum,
                audit,
                auditLogWriter: auditLogWriter);

            var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
            var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
                StudyId: "STUDY-001",
                SubjectId: "SUBJ-001",
                FormId: "FORM-VS",
                Payload: payload), Ct);

            formIdStr = createResult.Value!.Id;
            await service.CompleteAsync(formIdStr, Ct);

            Environment.SetEnvironmentVariable("GATEWAY_SECRET", TestSecurityContext.GatewaySecret);
            var validHash = FormSubmissionApplicationService.ComputePayloadHash(payload)!;
            var validHmac = Cadence.Security.Authentication.HmacSigner.ComputeSignature(validHash, TestSecurityContext.GatewaySecret);

            var piCaller = new AuthenticatedCaller("pi-user-01", ["PRINCIPAL_INVESTIGATOR"]);
            var approveResult = await service.ApproveAsync(formIdStr, FormSubmissionApproveRequest.Create(
                SignerId: "pi-user-01",
                SignerRole: "PRINCIPAL_INVESTIGATOR",
                SigningReason: "I attest that I have reviewed and approved these clinical data.",
                SignedAt: FakeClock.DefaultStart.UtcDateTime,
                PayloadHash: validHash,
                HmacSignature: validHmac),
                piCaller,
                Ct);

            approveResult.IsSuccess.Should().BeTrue();
        }

        await using (var context = CreateContext(db))
        {
            var formRepo = new FormSubmissionRepository(context, ResiliencePipeline.Empty);
            var obsRepo = new ObservationRepository(context, ResiliencePipeline.Empty);
            var ucum = new UcumNormalizationService();
            var audit = new FakeCurrentAuditContextAccessor("pi-investigator-01", "Unlock rationale for correction");
            var auditLogWriter = new EfAuditLogWriter(context);

            var service = new FormSubmissionApplicationService(
                formRepo,
                obsRepo,
                ucum,
                audit,
                auditLogWriter: auditLogWriter);

            var unlockResult = await service.UnlockAsync(formIdStr, "Unlock rationale for correction", Ct);
            unlockResult.IsSuccess.Should().BeTrue();
        }

        await using var verifyContext = CreateContext(db);
        var entries = await verifyContext.AuditLogEntries.AsNoTracking().ToListAsync(Ct);

        entries.Should().Contain(e => e.EntityName == nameof(FormSubmission) && e.EntityId == formIdStr && e.Action == "REVOKE_SIGNATURE");
        var revokeEntry = entries.Single(e => e.Action == "REVOKE_SIGNATURE");
        revokeEntry.UserId.Should().Be("pi-investigator-01");
        revokeEntry.ReasonForChange.Should().Be("Unlock rationale for correction");
        revokeEntry.Action.Should().Be("REVOKE_SIGNATURE");
        revokeEntry.ChangesJson.Should().Contain("pi-user-01");
        revokeEntry.ChangesJson.Should().Contain("PRINCIPAL_INVESTIGATOR");

        var chainResults = AuditLogChainVerifier.Verify(entries);
        chainResults.Should().ContainSingle(r => r.EntityName == nameof(FormSubmission))
            .Which.Valid.Should().BeTrue();
    }

    private static List<AuditLogEntry> BuildChain(string entityName, int length)
    {
        var chain = new List<AuditLogEntry>();
        string predecessor = string.Empty;
        for (int i = 0; i < length; i++)
        {
            var entry = Entry(entityName, $"step-{i}", predecessor);
            chain.Add(entry);
            predecessor = entry.BlockHash;
        }

        return chain;
    }

    private static AuditLogEntry Entry(string entityName, string action, string predecessor)
    {
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new AuditLogEntry
        {
            EntityName = entityName,
            EntityId = "SUBJ-1",
            Action = action,
            Timestamp = timestamp,
            UserId = "user-1",
            VersionIndex = 1,
            PredecessorHash = predecessor,
            BlockHash = AuditLogEntry.ComputeBlockHash(entityName, "SUBJ-1", action, timestamp, "user-1", 1, null, predecessor),
        };
    }
}
