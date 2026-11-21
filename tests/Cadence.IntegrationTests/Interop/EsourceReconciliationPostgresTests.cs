using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Interop;

/// <summary>
/// eSource reconciliation through the real interop host on PostgreSQL, as the application role (#5383). These tests are
/// the evidence for PRD-ESR-002/003. The host registers a retrying execution strategy, under which EF Core refuses a
/// transaction opened outside the strategy; before the fix both batch routes failed with 500 on PostgreSQL, while the
/// in-memory unit tests, which never open a transaction, passed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EsourceReconciliationPostgresTests(InteropPostgresApiFactory factory) : IClassFixture<InteropPostgresApiFactory>
{
    private const string Base = "/api/v1/interop/esource/reconcile";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A batch accept commits every candidate into the eCRF value with the coordinator's signature, and writes one
    /// audit entry per candidate naming its source, in one transaction.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-ESR-003")]
    public async Task BatchAccept_CommitsEveryCandidateWithSignatureAndProvenanceAudit()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var ids = await SeedCandidatesAsync(study, 3);
        var coordinator = Coordinator(study);
        using var client = factory.CreateSignedClient(coordinator);

        var response = await client.PostAsJsonAsync($"{Base}/batch-accept",
            new { candidateIds = ids, commitReason = "Matches the source chart", changeReason = "Batch reconciliation" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var stored = await CandidatesAsync(ids);
        stored.Should().HaveCount(3).And.OnlyContain(c =>
            c.Status == "ACCEPTED" && c.ResolvedBy == coordinator.UserId && c.VersionIndex == 2
            && c.CurrentEcrfValueJson == c.CandidateValueJson && c.ElectronicSignatureJson!.Contains(coordinator.UserId));
        var audit = await AuditAsync(coordinator.UserId, "ESOURCE_CANDIDATE_BATCH_ACCEPTED");
        audit.Should().HaveCount(3);
        audit.Should().OnlyContain(a => ids.Any(id => a.Details.Contains(id)));
    }

    /// <summary>A batch reject records the clinical rationale and the signature on every candidate, in one transaction.</summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task BatchReject_RejectsEveryCandidateWithRationale()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var ids = await SeedCandidatesAsync(study, 2);
        var coordinator = Coordinator(study);
        using var client = factory.CreateSignedClient(coordinator);

        var response = await client.PostAsJsonAsync($"{Base}/batch-reject",
            new { candidateIds = ids, rejectionReason = "Value belongs to a different visit", changeReason = "Batch reconciliation" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await CandidatesAsync(ids)).Should().HaveCount(2).And.OnlyContain(c =>
            c.Status == "REJECTED" && c.ResolutionReason == "Value belongs to a different visit" && c.ElectronicSignatureJson != null);
        (await AuditAsync(coordinator.UserId, "ESOURCE_CANDIDATE_BATCH_REJECTED")).Should().HaveCount(2);
    }

    /// <summary>A batch naming one unknown candidate changes none of the others: the unit of work is all or nothing.</summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task BatchAccept_WithAnUnknownCandidate_ChangesNothing()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        var ids = await SeedCandidatesAsync(study, 2);
        var coordinator = Coordinator(study);
        using var client = factory.CreateSignedClient(coordinator);

        var response = await client.PostAsJsonAsync($"{Base}/batch-accept",
            new { candidateIds = ids.Append($"missing-{Guid.NewGuid():N}").ToList() }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(Ct));
        (await CandidatesAsync(ids)).Should().OnlyContain(c => c.Status == "PENDING_REVIEW" && c.VersionIndex == 1);
        (await AuditAsync(coordinator.UserId, "ESOURCE_CANDIDATE_BATCH_ACCEPTED")).Should().BeEmpty();
    }

    /// <summary>
    /// Accept &amp; Commit writes the candidate into the eCRF value under the coordinator's signature, and the provenance
    /// view then shows the source system, the FHIR resource and the audit trail of that commit.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-ESR-003")]
    public async Task Accept_CommitsTheValueAndTheProvenanceViewShowsSourceAndAudit()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        string id = (await SeedCandidatesAsync(study, 1)).Single();
        var coordinator = Coordinator(study);
        using var client = factory.CreateSignedClient(coordinator);

        var accept = await client.PostAsJsonAsync($"{Base}/accept",
            new { candidateId = id, commitReason = "Verified against the chart", changeReason = "Reconciliation" }, Ct);

        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync(Ct));
        var stored = (await CandidatesAsync([id])).Single();
        stored.Status.Should().Be("ACCEPTED");
        stored.CurrentEcrfValueJson.Should().Be(stored.CandidateValueJson);
        stored.ElectronicSignatureJson.Should().Contain(coordinator.UserId);

        var provenance = await client.GetFromJsonAsync<JsonObject>($"/api/v1/interop/esource/provenance/{id}", Ct);
        string text = provenance!.ToJsonString();
        text.Should().Contain("epic-test").And.Contain(stored.FhirResourceId).And.Contain("ESOURCE_CANDIDATE_ACCEPTED");
    }

    /// <summary>
    /// Reject needs a clinical rationale; with one it records the rejection and leaves the source payload untouched,
    /// so nothing is overwritten silently.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    [GxPRequirement("PRD-ESR-003")]
    public async Task Reject_NeedsARationaleAndKeepsTheSourcePayload()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        string id = (await SeedCandidatesAsync(study, 1)).Single();
        var coordinator = Coordinator(study);
        using var client = factory.CreateSignedClient(coordinator);
        var before = (await CandidatesAsync([id])).Single();

        var empty = await client.PostAsJsonAsync($"{Base}/reject", new { candidateId = id, rejectionReason = "" }, Ct);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest, await empty.Content.ReadAsStringAsync(Ct));

        var reject = await client.PostAsJsonAsync($"{Base}/reject",
            new { candidateId = id, rejectionReason = "Taken before consent", changeReason = "Reconciliation" }, Ct);

        reject.StatusCode.Should().Be(HttpStatusCode.OK, await reject.Content.ReadAsStringAsync(Ct));
        var stored = (await CandidatesAsync([id])).Single();
        stored.Status.Should().Be("REJECTED");
        stored.ResolutionReason.Should().Be("Taken before consent");
        stored.CandidateValueJson.Should().Be(before.CandidateValueJson);
        stored.CurrentEcrfValueJson.Should().BeNull("a rejected candidate never reaches the eCRF");
        (await AuditAsync(coordinator.UserId, "ESOURCE_CANDIDATE_REJECTED")).Should().ContainSingle();
    }

    /// <summary>A monitor raises a discrepancy query on a candidate instead of deciding it; the candidate waits on the query.</summary>
    [Fact]
    [GxPRequirement("PRD-ESR-002")]
    public async Task RaiseQuery_OpensAQueryAndHoldsTheCandidate()
    {
        factory.SkipIfUnavailable();
        string study = NewStudy();
        string id = (await SeedCandidatesAsync(study, 1)).Single();
        using var client = factory.CreateSignedClient(
            TestSecurityContext.Identity($"cra-{Guid.NewGuid():N}", "cra") with { StudyIds = [study], SiteIds = ["SITE-01"] });

        var query = await client.PostAsJsonAsync($"{Base}/query",
            new { candidateId = id, queryExplanation = "EHR shows 128 mmHg, the eCRF 140 mmHg", discrepancyType = "VALUE_MISMATCH" }, Ct);

        query.StatusCode.Should().Be(HttpStatusCode.OK, await query.Content.ReadAsStringAsync(Ct));
        var stored = (await CandidatesAsync([id])).Single();
        stored.Status.Should().Be("QUERIED");
        await using var scope = factory.Services.CreateAsyncScope();
        var opened = await scope.ServiceProvider.GetRequiredService<InteropDbContext>().ClinicalQueries.AsNoTracking()
            .SingleAsync(q => q.Id == stored.ClinicalQueryId, Ct);
        opened.Status.Should().Be("OPEN");
    }

    private static GatewayIdentity Coordinator(string study) =>
        TestSecurityContext.Identity($"crc-{Guid.NewGuid():N}", "crc") with { StudyIds = [study], SiteIds = ["SITE-01"] };

    private static string NewStudy() => $"STUDY-ESR-{Guid.NewGuid():N}";

    private async Task<List<string>> SeedCandidatesAsync(string study, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InteropDbContext>();
        var candidates = Enumerable.Range(1, count).Select(i => new EhrReconciliationCandidateRecord
        {
            StudyId = study,
            SiteId = "SITE-01",
            SubjectId = $"SUBJ-{i:D3}",
            SubjectPseudonym = $"P-{i:D3}",
            EhrSystemId = "epic-test",
            FhirResourceType = "Observation",
            FhirResourceId = $"obs-{Guid.NewGuid():N}",
            SourceTimestamp = DateTimeOffset.UnixEpoch,
            TargetDomain = "VS",
            TargetVariable = "VSORRES",
            CandidateValueJson = $"{{\"value\":{120 + i}}}",
            MappingProvenance = "LOINC 8480-6 to VS.SYSBP",
            CreatedAt = DateTimeOffset.UnixEpoch,
        }).ToList();
        db.EhrReconciliationCandidateRecords.AddRange(candidates);
        await db.SaveChangesAsync(Ct);
        return candidates.Select(c => c.Id).ToList();
    }

    private async Task<List<EhrReconciliationCandidateRecord>> CandidatesAsync(IReadOnlyCollection<string> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InteropDbContext>().EhrReconciliationCandidateRecords
            .AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(Ct);
    }

    private async Task<List<InteropAuditLog>> AuditAsync(string user, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InteropDbContext>().InteropAuditLogs
            .AsNoTracking().Where(a => a.UserId == user && a.Action == action).ToListAsync(Ct);
    }
}
