using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.IntegrationTests.Safety;

/// <summary>
/// Every safety column that holds a signed <see cref="GxPSigner"/> manifest is <c>text</c> on PostgreSQL and keeps a
/// whole manifest (#5482, ADR-2519). Before the fix four of them were <c>varchar(255)</c>, which a signed manifest never
/// fits; the DSUR one was widened by #5462. Runs as the application role the fixture connects with.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SafetySignatureManifestPostgresTests(SafetyPostgresApiFactory factory) : IClassFixture<SafetyPostgresApiFactory>
{
    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("pg-manifest-compiler", "Sponsor Medical Monitor");
    private static readonly GatewayIdentity Approver = TestSecurityContext.Identity("pg-manifest-approver", "Sponsor Medical Monitor");

    private static readonly string[] ManifestTables =
    [
        "safety_reporting_evaluations",
        "safety_narratives",
        "safety_signal_alerts",
        "periodic_safety_reports",
        "device_safety_investigations",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Each table's <c>EsignatureManifestId</c> column is unbounded <c>text</c> in the migrated database.</summary>
    [Fact]
    public async Task ManifestColumns_AreUnboundedText()
    {
        factory.SkipIfUnavailable();
        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync(Ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT table_name, data_type, character_maximum_length FROM information_schema.columns "
            + "WHERE table_schema = current_schema() AND column_name = 'EsignatureManifestId' ORDER BY table_name";

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                bool unbounded = await reader.IsDBNullAsync(2, Ct);
                columns[reader.GetString(0)] = reader.GetString(1) + "|" + (unbounded ? "unbounded" : reader.GetInt32(2).ToString(CultureInfo.InvariantCulture));
            }
        }

        columns.Keys.Should().BeEquivalentTo(ManifestTables);
        columns.Values.Should().OnlyContain(v => v == "text|unbounded");
    }

    /// <summary>
    /// A DSUR approved through the real host stores the whole signed manifest: the stored value is the one the approval
    /// returned, longer than the old 255-character limit, names the approver and still verifies after the round trip.
    /// </summary>
    [Fact]
    public async Task DsurApproval_StoresAndReadsBackTheWholeSignedManifest()
    {
        factory.SkipIfUnavailable();
        string id;
        using (var client = factory.CreateSignedClient(Compiler))
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/safety/dsur/compile",
                new
                {
                    compoundName = "Cadence-Compound-Manifest",
                    studyId = "STUDY-MANIFEST",
                    periodStart = "2025-01-01T00:00:00Z",
                    periodEnd = "2025-12-31T23:59:59Z",
                    reasonForChange = "Annual DSUR compilation",
                },
                Ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
            id = (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.Id;
        }

        string returned;
        using (var client = factory.CreateSignedClient(Approver))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/safety/dsur/{id}/approve")
            {
                Content = JsonContent.Create(new { reasonForChange = "DSUR medical review complete" }),
            };
            request.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(Approver.UserId, DsurEndpoints.ApproveAction(id), DsurEndpoints.ApproveSemanticAction));
            using var response = await client.SendAsync(request, Ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
            returned = (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.EsignatureManifestId!;
        }

        await using var db = CreateContext();
        var stored = (await db.PeriodicSafetyReports.AsNoTracking().SingleAsync(r => r.Id == id, Ct)).EsignatureManifestId;

        stored.Should().Be(returned);
        stored!.Length.Should().BeGreaterThan(255);
        stored.Should().Contain(Approver.UserId);
        new ESignatureVerifier().VerifySignature(Encoding.UTF8.GetBytes(stored)).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// The four other approval records have no signing path yet; a full signed manifest written to each as the
    /// application role reads back unchanged and still verifies, where <c>varchar(255)</c> refused it.
    /// </summary>
    [Theory]
    [InlineData("safety_reporting_evaluations")]
    [InlineData("safety_narratives")]
    [InlineData("safety_signal_alerts")]
    [InlineData("device_safety_investigations")]
    public async Task ApprovalRecord_StoresAndReadsBackAWholeSignedManifest(string table)
    {
        factory.SkipIfUnavailable();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 20, 9, 30, 0, TimeSpan.Zero));
        string manifest = GxPSigner.SignGxpMarkdown(
            content: $"# Safety approval ({table})\n\nApproved and locked under 21 CFR Part 11.\n",
            signerId: "pg-manifest-physician",
            signingReason: "Safety physician approval",
            timestamp: "2026-10-20 09:30:00 UTC",
            clock: clock);
        manifest.Length.Should().BeGreaterThan(255, "the test must exceed the old column limit");
        string id = Guid.NewGuid().ToString();

        await using (var db = CreateContext())
        {
            db.Add(NewRecord(table, id, manifest, clock.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = CreateContext())
        {
            string? stored = table switch
            {
                "safety_reporting_evaluations" => (await db.SafetyReportingEvaluations.AsNoTracking().SingleAsync(r => r.Id == id, Ct)).EsignatureManifestId,
                "safety_narratives" => (await db.SafetyNarratives.AsNoTracking().SingleAsync(r => r.Id == id, Ct)).EsignatureManifestId,
                "safety_signal_alerts" => (await db.SafetySignalAlerts.AsNoTracking().SingleAsync(r => r.Id == id, Ct)).EsignatureManifestId,
                _ => (await db.DeviceSafetyInvestigations.AsNoTracking().SingleAsync(r => r.Id == id, Ct)).EsignatureManifestId,
            };

            stored.Should().Be(manifest);
            new ESignatureVerifier().VerifySignature(Encoding.UTF8.GetBytes(stored!)).IsValid.Should().BeTrue();
        }
    }

    private static object NewRecord(string table, string id, string manifest, DateTimeOffset now) => table switch
    {
        "safety_reporting_evaluations" => new IcsrReportingEvaluation
        {
            Id = id,
            CaseId = "CASE-MANIFEST",
            StudyId = "STUDY-MANIFEST",
            SubjectId = "SUBJ-MANIFEST",
            ReceiptDate = now,
            Causality = "RELATED",
            Expectedness = "UNEXPECTED",
            ApprovalStatus = "APPROVED",
            ApprovedByUserId = "pg-manifest-physician",
            ApprovedAt = now,
            EsignatureManifestId = manifest,
            CreatedAt = now,
            CreatedBy = "pg-manifest-physician",
            ReasonForChange = "Expedited reporting evaluation approved",
        },
        "safety_narratives" => new SafetyNarrative
        {
            Id = id,
            StudyId = "STUDY-MANIFEST",
            SubjectId = "SUBJ-MANIFEST",
            CaseId = "CASE-MANIFEST",
            SaeEventKey = "SAE-1",
            Title = "SAE narrative",
            ReviewStatus = "APPROVED",
            ApprovedByUserId = "pg-manifest-physician",
            ApprovedAt = now,
            EsignatureManifestId = manifest,
            CreatedAt = now,
            CreatedBy = "pg-manifest-physician",
            ReasonForChange = "Narrative approved",
        },
        "safety_signal_alerts" => new SafetySignalAlert
        {
            Id = id,
            TargetDrug = "Cadence-Compound-Manifest",
            MeddraTerm = "Hepatotoxicity",
            Status = "REVIEWED",
            TriageSummary = "Disproportionality above threshold",
            ReviewDecision = "CONFIRMED",
            ReviewedBy = "pg-manifest-physician",
            ReviewedAt = now,
            EsignatureManifestId = manifest,
            CreatedAt = now,
            CreatedBy = "pg-manifest-physician",
            ReasonForChange = "Signal review signed",
        },
        _ => new DeviceSafetyInvestigation
        {
            Id = id,
            CaseNumber = "DEV-" + id,
            StudyId = "STUDY-MANIFEST",
            SiteId = "SITE-MANIFEST",
            DeficiencyId = "DEF-1",
            DeviceName = "Infusion pump",
            DeviceUdiDi = "UDI-DI-1",
            LotNumber = "LOT-1",
            VigilanceDueDate = now.AddDays(15),
            Status = "ADJUDICATED",
            SafetyPhysicianUserId = "pg-manifest-physician",
            AdjudicatedAt = now,
            EsignatureManifestId = manifest,
            CreatedAt = now,
            CreatedBy = "pg-manifest-physician",
            ReasonForChange = "Device investigation adjudicated",
        },
    };

    /// <summary>
    /// A context on the fixture's application-role connection without the host's audit interceptor, whose actor is the
    /// verified caller of an HTTP request and so does not exist for a direct write.
    /// </summary>
    private SafetyDbContext CreateContext()
    {
        string connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("cadence")!;
        return new SafetyDbContext(new DbContextOptionsBuilder<SafetyDbContext>().UseNpgsql(connectionString).Options);
    }
}
