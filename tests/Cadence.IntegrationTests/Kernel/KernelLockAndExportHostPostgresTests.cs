using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Compliance;
using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Rules.Packs;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Signed kernel lock routes, streaming bitemporal study snapshot exports, participant casebooks, and <c>K-AUTH-4</c>
/// caller visibility and blinding masking through the real kernel host on PostgreSQL (#5466, #5592, #5042).
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelLockAndExportHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string DataManager = "dm@kernel-lock-export.test";
    private const string SiteACoordinator = "crc.site-a@kernel-lock-export.test";
    private const string Monitor = "cra@kernel-lock-export.test";
    private const string BlindedStatistician = "stat.blinded@kernel-lock-export.test";
    private const string UnblindedPharmacist = "pharm.unblinded@kernel-lock-export.test";
    private const string Stranger = "stranger@kernel-lock-export.test";
    private const string LockReason = "Interim clean-data review complete";
    private const string ReleaseReason = "Source addendum requires blood pressure correction";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// <c>POST /api/v1/kernel/locks</c> and <c>POST /api/v1/kernel/locks/{lockRef}/release</c> fail closed with 401 on
    /// missing, expired, replayed, wrong-action or wrong-signer <c>X-Sig-Token</c> values, 400 on missing
    /// <c>Idempotency-Key</c> or <c>X-Change-Reason</c>, and 403 when the caller lacks <c>lock.apply</c> or
    /// <c>lock.release</c>, writing no orphan <c>Attestation</c> rows.
    /// </summary>
    [Fact(DisplayName = "HostLock_RefusesEveryUnusableStepUpToken_MissingHeaders_AndCallersWithoutTheGrant")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task HostLockRefusesEveryUnusableStepUpTokenMissingHeadersAndCallersWithoutTheGrant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, siteA, _, enrollmentA, _, _, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentRef = NodeRef.For(enrollmentA).ToString();

        using var dm = Client(code, DataManager);
        using var crc = Client(code, SiteACoordinator);

        var applyBody = new { study_ref = studyRef, scope_ref = enrollmentRef, kind = "Locked", meaning = "Approved" };
        var unusableApplyTokens = new (string Name, string? Token)[]
        {
            ("missing", null),
            ("expired", TestSecurityContext.CreateSigToken(
                DataManager,
                KernelLockEndpoints.ApplyLockAction,
                KernelLockEndpoints.ApplyLockSemanticAction,
                issuedAt: SystemClock.Instance.UtcNow.AddMinutes(-3))),
            ("wrong action path", TestSecurityContext.CreateSigToken(
                DataManager,
                "/api/v1/kernel/locks/other",
                KernelLockEndpoints.ApplyLockSemanticAction)),
            ("wrong semantic action", TestSecurityContext.CreateSigToken(
                DataManager,
                KernelLockEndpoints.ApplyLockAction,
                KernelLockEndpoints.ReleaseLockSemanticAction)),
            ("wrong signer", ApplyToken(SiteACoordinator)),
        };

        foreach (var (name, token) in unusableApplyTokens)
        {
            using var refused = await dm.SendAsync(BuildRequest(HttpMethod.Post, "locks", LockReason, token, KernelProtocolScenario.Key(), applyBody), ct);
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
            (await refused.Content.ReadAsStringAsync(ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        }

        // Missing Idempotency-Key -> 400; missing X-Change-Reason -> 400.
        using (var noIdempotency = await dm.SendAsync(BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), idempotencyKey: null, applyBody), ct))
        {
            noIdempotency.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var noReason = await dm.SendAsync(BuildRequest(HttpMethod.Post, "locks", reason: null, ApplyToken(DataManager), KernelProtocolScenario.Key(), applyBody), ct))
        {
            noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // Coordinator has attestation.sign on siteA, but lacks lock.apply: 403 and zero orphan Attestations!
        int attestationsBefore;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsBefore = await db.Attestations.CountAsync(ct);
        }

        using (var forbiddenApply = await crc.SendAsync(BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(SiteACoordinator), KernelProtocolScenario.Key(), applyBody), ct))
        {
            forbiddenApply.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(forbiddenApply, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(attestationsBefore, "a refused lock must not leave an orphan Attestation");
        }

        // Apply one valid lock, then verify token replay fails with 401 and release enforces the same guardrails.
        var singleUseApply = ApplyToken(DataManager);
        var applied = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", LockReason, singleUseApply, KernelProtocolScenario.Key(), applyBody),
            HttpStatusCode.Created,
            ct);
        applied.Ref.Should().StartWith("assertion:");

        using (var replayApply = await dm.SendAsync(BuildRequest(HttpMethod.Post, "locks", LockReason, singleUseApply, KernelProtocolScenario.Key(), new
        {
            study_ref = studyRef,
            scope_ref = NodeRef.For(siteA).ToString(),
            kind = "Frozen",
        }), ct))
        {
            replayApply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a step-up token is single-use");
        }

        var releaseBody = new { study_ref = studyRef, meaning = "Responsibility" };
        using (var noReleaseToken = await dm.SendAsync(BuildRequest(HttpMethod.Post, $"locks/{applied.Ref}/release", ReleaseReason, token: null, KernelProtocolScenario.Key(), releaseBody), ct))
        {
            noReleaseToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var forbiddenRelease = await crc.SendAsync(BuildRequest(HttpMethod.Post, $"locks/{applied.Ref}/release", ReleaseReason, ReleaseToken(SiteACoordinator, applied.Ref!), KernelProtocolScenario.Key(), releaseBody), ct);
        forbiddenRelease.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(forbiddenRelease, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
    }

    /// <summary>
    /// Applies and releases both <c>Frozen</c> and <c>Locked</c> assertions across all five scopes in
    /// <see cref="LockScopes.Kinds"/> (<c>study</c>, <c>study_site</c>, <c>enrollment</c>, <c>collection_instance</c>,
    /// <c>observation</c>), verifies <c>GET /api/v1/kernel/studies/{studyRef}/locks</c>, refuses edits with 423 while
    /// locked, and permits correction again once released.
    /// </summary>
    [Fact(DisplayName = "HostLock_AppliesAndReleasesFrozenAndLockedAcrossAllFiveScopes_AndEnforces423")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostLockAppliesAndReleasesFrozenAndLockedAcrossAllFiveScopesAndEnforces423()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (_, code, study, _, siteA, _, enrollmentA, _, formA, sbpObservationA) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();

        using var dm = Client(code, DataManager);
        using var crc = Client(code, SiteACoordinator);

        var scopes = new (string ExpectedKind, NodeRef Scope)[]
        {
            (NodeKinds.Study, NodeRef.For(study)),
            (NodeKinds.StudySite, NodeRef.For(siteA)),
            (NodeKinds.Enrollment, NodeRef.For(enrollmentA)),
            (NodeKinds.CollectionInstance, NodeRef.For(formA)),
            (NodeKinds.Observation, NodeRef.For(sbpObservationA)),
        };

        scopes.Select(s => s.ExpectedKind).Should().BeEquivalentTo(LockScopes.Kinds);

        long expectedVersion = 1;
        foreach (var kind in new[] { "Frozen", "Locked" })
        {
            foreach (var (scopeKind, scope) in scopes)
            {
                Tick();
                var created = await SendAsync<KernelLockResponse>(
                    dm,
                    BuildRequest(HttpMethod.Post, "locks", $"{kind} on {scopeKind}", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
                    {
                        study_ref = studyRef,
                        scope_ref = scope.ToString(),
                        kind,
                        meaning = "Approved",
                    }),
                    HttpStatusCode.Created,
                    ct);
                created.ScopeKind.Should().Be(scopeKind);
                created.Kind.Should().Be(kind);
                created.Released.Should().BeFalse();
                created.AttestationRef.Should().StartWith("attestation:");

                var activeList = await SendAsync<KernelStudyLocksResponse>(
                    dm,
                    BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks?active_only=true", reason: null, token: null, idempotencyKey: null, body: null),
                    HttpStatusCode.OK,
                    ct);
                activeList.Locks.Should().ContainSingle(l => l.Ref == created.Ref && !l.Released);

                // Observation correction is rejected with 423 while covered by any of the 5 scopes.
                using (var blocked = await crc.SendAsync(BuildRequest(
                    HttpMethod.Post,
                    $"observations/{NodeRef.For(sbpObservationA)}/corrections",
                    "Attempted edit under lock",
                    token: null,
                    KernelProtocolScenario.Key(),
                    new
                    {
                        enrollment_ref = NodeRef.For(enrollmentA).ToString(),
                        value = new { type = "quantity", value = 121.25m, unit = "mm[Hg]" },
                        expected_version = expectedVersion,
                    }), ct))
                {
                    blocked.StatusCode.Should().Be((HttpStatusCode)423, $"{kind} at {scopeKind} must block observation corrections");
                    (await ProblemCodeAsync(blocked, ct)).Should().Be(ObservationErrors.Locked.Code);
                }

                Tick();
                var released = await SendAsync<KernelLockResponse>(
                    dm,
                    BuildRequest(HttpMethod.Post, $"locks/{created.Ref}/release", $"Release {kind} on {scopeKind}", ReleaseToken(DataManager, created.Ref!), KernelProtocolScenario.Key(), new
                    {
                        study_ref = studyRef,
                        meaning = "Responsibility",
                    }),
                    HttpStatusCode.OK,
                    ct);
                released.Released.Should().BeTrue();
                released.ReleasedBy.Should().Be(DataManager);
            }
        }

        // After releasing all 10 locks, active_only=true is empty while active_only=false retains all 10 historical locks.
        var remainingActive = await SendAsync<KernelStudyLocksResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks?active_only=true", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        remainingActive.Locks.Should().BeEmpty();

        var fullHistory = await SendAsync<KernelStudyLocksResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks?active_only=false", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        fullHistory.Locks.Should().HaveCount(10);
        fullHistory.Locks.Should().OnlyContain(l => l.Released && l.ReleaseReason != null);

        // Corrections succeed again now that locks are released.
        var corrected = await SendAsync<KernelObservationStateResponse>(
            crc,
            BuildRequest(
                HttpMethod.Post,
                $"observations/{NodeRef.For(sbpObservationA)}/corrections",
                "Corrected after unlock",
                token: null,
                KernelProtocolScenario.Key(),
                new
                {
                    enrollment_ref = NodeRef.For(enrollmentA).ToString(),
                    value = new { type = "quantity", value = 124.50m, unit = "mm[Hg]" },
                    expected_version = expectedVersion,
                }),
            HttpStatusCode.OK,
            ct);
        corrected.Version.Should().Be(2);
    }

    /// <summary>
    /// Bitemporal snapshots (<c>K-TRUTH-1</c>) reproduce historical <c>as_of</c> cuts after unlock, correction and relock,
    /// preserve exact <c>decimal</c> precision (<c>120.50</c> vs <c>132.75</c>) across CSV, CDISC ODM v1.3.2 XML and
    /// Dataset-JSON, embed protocol <c>MetaDataVersion</c>, <c>AuditRecord</c> and <c>Attestation</c> signatures, verify
    /// <c>X-Content-Sha256</c> casebook digests across JSON, ODM XML and HTML, and exclude <c>IdentityVault</c> direct
    /// identifiers.
    /// </summary>
    [Fact(DisplayName = "HostExportsAndCasebook_ReproduceHistoricalAsOfSnapshots_PreserveDecimalPrecision_AndVerifySha256Digest")]
    [KernelRule("K-TRUTH-1")]
    public async Task HostExportsAndCasebookReproduceHistoricalAsOfSnapshotsPreserveDecimalPrecisionAndVerifySha256Digest()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, studyCode, _, _, enrollmentA, _, formA, sbpObservationA) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentRef = NodeRef.For(enrollmentA).ToString();
        var obsRef = NodeRef.For(sbpObservationA).ToString();

        using var dm = Client(code, DataManager);
        using var crc = Client(code, SiteACoordinator);
        using var monitor = Client(code, Monitor);

        // Verify revision 1 (120.50 mm[Hg]) via HTTP and raise a query assertion + form signature in the kernel.
        Tick();
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            BuildRequest(HttpMethod.Post, $"observations/{obsRef}/verifications", "Verified against inpatient vital chart", null, KernelProtocolScenario.Key(), new
            {
                enrollment_ref = enrollmentRef,
                revision = 1,
            }),
            HttpStatusCode.Created,
            ct);

        Tick();
        var raisedQuery = await _scenario.Protocol.OkAsync(
            new RaiseQuery(
                tenant,
                AssertionId.New(),
                enrollmentA,
                sbpObservationA,
                "Confirm cuff size used at screening",
                KernelProtocolScenario.Key(),
                "Monitor query raised"),
            ct);
        Tick();
        await _scenario.Protocol.OkAsync(
            new AnswerQuery(
                tenant,
                AssertionId.New(),
                enrollmentA,
                raisedQuery.AssertionId,
                "Standard adult cuff verified in source chart",
                KernelProtocolScenario.Key(),
                "Site coordinator response"),
            ct);
        Tick();
        await _scenario.Protocol.OkAsync(
            new RetractAssertion(
                tenant,
                AssertionId.New(),
                enrollmentA,
                raisedQuery.AssertionId,
                KernelProtocolScenario.Key(),
                "Monitor closed query before lock"),
            ct);

        // Sign the form instance over HTTP (/api/v1/execution/forms/{formA}/signatures) so an Attestation covers formA too.
        using (var formGet = await dm.GetAsync(new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}", UriKind.Relative), ct))
        {
            formGet.StatusCode.Should().Be(HttpStatusCode.OK);
            using var formJson = JsonDocument.Parse(await formGet.Content.ReadAsStringAsync(ct));
            var contentHash = formJson.RootElement.GetProperty("content_hash").GetString()!;
            using var signReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}/signatures", UriKind.Relative));
            signReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            signReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("PI approved vitals"));
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(DataManager, FormSignatureEndpoints.SignAction(formA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq.Content = JsonContent.Create(new { meaning = "approved", content_hash = contentHash });
            using var signRes = await dm.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        // Lock 1 at tLock1 while systolic is 120.50.
        var lock1 = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = studyRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        var tLock1 = lock1.LockedAt!.Value;

        // Release Lock 1, correct systolic to 132.75, and apply Lock 2 strictly after tLock1.
        await Task.Delay(15, ct);
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, $"locks/{lock1.Ref}/release", ReleaseReason, ReleaseToken(DataManager, lock1.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);

        await Task.Delay(15, ct);
        await SendAsync<KernelObservationStateResponse>(
            crc,
            BuildRequest(HttpMethod.Post, $"observations/{obsRef}/corrections", "Addendum chart reads 132.75", null, KernelProtocolScenario.Key(), new
            {
                enrollment_ref = enrollmentRef,
                value = new { type = "quantity", value = 132.75m, unit = "mm[Hg]" },
                expected_version = 1,
            }),
            HttpStatusCode.OK,
            ct);

        await Task.Delay(15, ct);
        var lock2 = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Relocked after addendum correction", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = studyRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        var asOfParam = Uri.EscapeDataString(tLock1.ToString("O"));
        var asOfLock2Param = Uri.EscapeDataString(lock2.LockedAt!.Value.ToString("O"));

        // 1. Historical CSV at tLock1 reproduces 120.50 (with trailing zero preserved) and not 132.75.
        var csvAtLock1 = await GetTextAsync(dm, $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&as_of={asOfParam}", "text/csv", ct);
        csvAtLock1.Should().Contain("120.50");
        csvAtLock1.Should().NotContain("132.75");
        AssertNoDirectIdentifiers(csvAtLock1);

        // 2. Historical ODM v1.3.2 XML at tLock1 carries MetaDataVersion, ClinicalData, AuditRecord and Signature.
        var odmAtLock1 = await GetTextAsync(dm, $"studies/{studyRef}/exports?format=odm_xml&lock_filter=locked_only&as_of={asOfParam}", "application/xml", ct);
        odmAtLock1.Should().Contain("120.50").And.NotContain("132.75");
        var odmDoc1 = XDocument.Parse(odmAtLock1);
        XNamespace odmNs = "http://www.cdisc.org/ns/odm/v1.3";
        odmDoc1.Root!.Attribute("ODMVersion")!.Value.Should().Be("1.3.2");
        odmDoc1.Descendants(odmNs + "MetaDataVersion").Should().ContainSingle();
        odmDoc1.Descendants(odmNs + "FormDef").Select(e => e.Attribute("OID")!.Value).Should().Contain($"FORM.{SpecId}");
        odmDoc1.Descendants(odmNs + "ClinicalData").Should().ContainSingle();
        odmDoc1.Descendants(odmNs + "AuditRecord").Should().NotBeEmpty();
        odmDoc1.Descendants(odmNs + "Signature").Should().NotBeEmpty();

        // 3. Historical Dataset-JSON at tLock1 vs current Dataset-JSON at Lock 2.
        var dsAtLock1 = await GetTextAsync(dm, $"studies/{studyRef}/exports?format=dataset_json&lock_filter=locked_only&as_of={asOfParam}", "application/json", ct);
        using (var dsDoc1 = JsonDocument.Parse(dsAtLock1))
        {
            dsDoc1.RootElement.GetProperty("studyOID").GetString().Should().Be(studyCode);
            dsAtLock1.Should().Contain("120.50").And.NotContain("132.75");
        }

        var odmLatest = await GetTextAsync(dm, $"studies/{studyRef}/exports?format=odm_xml&lock_filter=locked_only", "application/xml", ct);
        odmLatest.Should().Contain("132.75").And.Contain("120.50", "ODM includes superseded revision audit history");

        // 4. Participant Casebook in JSON, ODM XML, and HTML: verify X-Content-Sha256 and rich clinical provenance.
        var (casebookJsonBytes, casebookJsonSha) = await GetBytesWithShaHeaderAsync(
            dm, $"enrollments/{enrollmentRef}/casebook?format=json&as_of={asOfLock2Param}", "application/json", ct);
        Convert.ToHexStringLower(SHA256.HashData(casebookJsonBytes)).Should().Be(casebookJsonSha);
        var casebook = JsonSerializer.Deserialize<KernelCasebookResponse>(casebookJsonBytes, Json)!;
        casebook.CasebookDigestSha256.Should().HaveLength(64);
        casebook.SubjectCode.Should().Be("SUBJ-A");
        casebook.Forms.Should().ContainSingle(f => f.CollectionSpecId == SpecId);
        var sbpCasebook = casebook.Observations.Single(o => o.ItemId == Systolic);
        sbpCasebook.DisplayValue.Should().Contain("132.75");
        sbpCasebook.NumericValue.Should().Be(132.75m);
        sbpCasebook.Revisions.Select(r => (r.Revision, r.NumericValue, r.Superseded))
            .Should().Equal((1, 120.50m, true), (2, 132.75m, false));
        casebook.Queries.Should().ContainSingle(q => q.Text == "Confirm cuff size used at screening");
        casebook.Verifications.Should().ContainSingle(v => v.Revision == 1 && v.VerifiedBy == Monitor);
        casebook.Attestations.Should().NotBeEmpty();
        AssertNoDirectIdentifiers(System.Text.Encoding.UTF8.GetString(casebookJsonBytes));

        // Historical casebook at tLock1 shows revision 1 (120.50) as current and no revision 2.
        var (casebookAtLock1Bytes, casebookAtLock1Sha) = await GetBytesWithShaHeaderAsync(
            dm, $"enrollments/{enrollmentRef}/casebook?format=json&as_of={asOfParam}", "application/json", ct);
        Convert.ToHexStringLower(SHA256.HashData(casebookAtLock1Bytes)).Should().Be(casebookAtLock1Sha);
        var casebookAtLock1 = JsonSerializer.Deserialize<KernelCasebookResponse>(casebookAtLock1Bytes, Json)!;
        casebookAtLock1.Observations.Single(o => o.ItemId == Systolic).NumericValue.Should().Be(120.50m);

        var (casebookOdmBytes, casebookOdmSha) = await GetBytesWithShaHeaderAsync(
            dm, $"enrollments/{enrollmentRef}/casebook?format=odm_xml&as_of={asOfLock2Param}", "application/xml", ct);
        Convert.ToHexStringLower(SHA256.HashData(casebookOdmBytes)).Should().Be(casebookOdmSha);
        var casebookOdmText = System.Text.Encoding.UTF8.GetString(casebookOdmBytes);
        XDocument.Parse(casebookOdmText).Root!.Name.LocalName.Should().Be("ODM");
        casebookOdmText.Should().Contain(casebook.CasebookDigestSha256);

        var (casebookHtmlBytes, casebookHtmlSha) = await GetBytesWithShaHeaderAsync(
            dm, $"enrollments/{enrollmentRef}/casebook?format=html&as_of={asOfLock2Param}", "text/html", ct);
        Convert.ToHexStringLower(SHA256.HashData(casebookHtmlBytes)).Should().Be(casebookHtmlSha);
        var htmlText = System.Text.Encoding.UTF8.GetString(casebookHtmlBytes);
        htmlText.Should().Contain("SUBJ-A").And.Contain("132.75").And.Contain(casebook.CasebookDigestSha256);
        AssertNoDirectIdentifiers(htmlText);
    }

    /// <summary>
    /// Under <c>K-AUTH-4</c>, a site coordinator granted only on <c>SITE-A</c> receives explicit <c>masked</c>
    /// (<c>NotPermitted</c>) placeholders for <c>SITE-B</c> rows and locks, and a blinded statistician without
    /// <c>enrollment.unblind</c> receives explicit <c>masked</c> (<c>Blinded</c>) placeholders for randomized treatment
    /// observations and locks across lock lists, CSV, ODM v1.3.2 XML, Dataset-JSON, and casebook exports, while an
    /// unblinded pharmacist sees the unmasked treatment arm.
    /// </summary>
    [Fact(DisplayName = "HostExportsLocksAndCasebook_MaskUnauthorizedSitesAndBlindedTreatmentInPlace")]
    [KernelRule("K-AUTH-4")]
    public async Task HostExportsLocksAndCasebookMaskUnauthorizedSitesAndBlindedTreatmentInPlace()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, _, siteB, enrollmentA, enrollmentB, _, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();
        var enrollmentBRef = NodeRef.For(enrollmentB).ToString();

        // Define a randomization_treatment_assignment concept and record a blinded treatment observation on SUBJ-A.
        var treatmentConcept = await DefineConceptWithCodeAsync(tenant, "randomization_treatment_assignment", "Randomized treatment arm", ct);
        var blindedObsId = ObservationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                blindedObsId,
                enrollmentA,
                treatmentConcept,
                new TextValue("Active Drug 100mg"),
                Tick().AddMinutes(-2),
                KernelProtocolScenario.Key(),
                "Interactive response randomization allocation"),
            ct);

        using var dm = Client(code, DataManager);
        using var siteACrc = Client(code, SiteACoordinator);
        using var blindedStat = Client(code, BlindedStatistician);
        using var unblindedPharm = Client(code, UnblindedPharmacist);
        using var stranger = Client(code, Stranger);

        // Lock SITE-B's enrollment AND lock the blinded treatment observation on SUBJ-A.
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Lock Site B participant", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentBRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze randomization arm record", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = NodeRef.For(blindedObsId).ToString(),
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        // Stranger with no grant gets 403 on study locks, study exports, and casebook.
        (await stranger.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/locks", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/exports?format=csv", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync(new Uri($"/api/v1/kernel/enrollments/{enrollmentARef}/casebook", UriKind.Relative), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Site A coordinator: sees SITE-B lock as Masked (NotPermitted) and blinded observation lock as Masked (Blinded).
        var crcLocks = await SendAsync<KernelStudyLocksResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcLocks.Locks.Should().HaveCount(2);
        crcLocks.Locks.Should().ContainSingle(l => l.ScopeKind == NodeKinds.Enrollment && l.Masked && l.Mask == "NotPermitted" && l.SubjectCode == null && l.ScopeRef == null);
        crcLocks.Locks.Should().ContainSingle(l => l.ScopeKind == NodeKinds.Observation && l.Masked && l.Mask == "Blinded" && l.ScopeRef == null);

        // Site A coordinator: exports omit per-record cross-site placeholders, collapse unauthorized Site B into a single
        // hidden-scope indicator (ADR-2590), and partition visibility BEFORE lock_filter.
        using (var csvRes = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/exports?format=csv&lock_filter=all", UriKind.Relative), ct))
        {
            var crcCsv = await csvRes.Content.ReadAsStringAsync(ct);
            csvRes.StatusCode.Should().Be(HttpStatusCode.OK, crcCsv);
            csvRes.Headers.TryGetValues(KernelExportEndpoints.HiddenScopesHeader, out var hiddenValues).Should().BeTrue();
            hiddenValues!.Single().Should().Be("more_exist=true;reasons=NotPermitted");
            crcCsv.Should().Contain("SUBJ-A").And.Contain("120.50").And.Contain("Blinded");
            crcCsv.Should().NotContain("SUBJ-B").And.NotContain("138.25").And.NotContain("Active Drug 100mg").And.NotContain("NotPermitted");
        }

        // Under lock_filter=locked_only, Site B's Locked enrollment must NOT appear as a cross-site row (Site A has zero Locked rows, only Frozen).
        var crcLockedOnlyCsv = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only", "text/csv", ct);
        crcLockedOnlyCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1, "header only: Site B's Locked enrollment was excluded before lock_filter");
        crcLockedOnlyCsv.Should().NotContain("SUBJ-B").And.NotContain("NotPermitted");

        var crcUnlockedOnlyCsv = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=csv&lock_filter=unlocked_only", "text/csv", ct);
        crcUnlockedOnlyCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "header + 2 unlocked Site A vitals (Frozen blinded arm and Site B excluded)");
        crcUnlockedOnlyCsv.Should().Contain("SUBJ-A").And.Contain("120.50").And.NotContain("SUBJ-B").And.NotContain("NotPermitted");

        var crcOdm = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=odm_xml&lock_filter=all", "application/xml", ct);
        crcOdm.Should().Contain("SUBJ-A").And.Contain("120.50").And.Contain("Blinded");
        crcOdm.Should().NotContain("SUBJ-B").And.NotContain("138.25").And.NotContain("Active Drug 100mg").And.NotContain("PlaceholderSequence");
        var crcOdmDoc = XDocument.Parse(crcOdm);
        XNamespace odmNs = "http://www.cdisc.org/ns/odm/v1.3";
        XNamespace cadenceNs = "https://cadence.clinical/ns/kernel-export/v1";
        crcOdmDoc.Descendants(odmNs + "SubjectData").Should().ContainSingle();
        var hiddenOdm = crcOdmDoc.Descendants(cadenceNs + "HiddenScopes").Single();
        hiddenOdm.Attribute("MoreExist")!.Value.Should().Be("true");
        hiddenOdm.Attribute("Reasons")!.Value.Should().Be("NotPermitted");

        var crcDatasetJson = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=dataset_json&lock_filter=all", "application/json", ct);
        crcDatasetJson.Should().Contain("SUBJ-A").And.Contain("120.50").And.Contain("Blinded");
        crcDatasetJson.Should().NotContain("SUBJ-B").And.NotContain("138.25").And.NotContain("Active Drug 100mg").And.NotContain("IG.masked");
        using (var dsDoc = JsonDocument.Parse(crcDatasetJson))
        {
            var hiddenJson = dsDoc.RootElement.GetProperty("hiddenScopes");
            hiddenJson.GetProperty("more_exist").GetBoolean().Should().BeTrue();
            hiddenJson.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).Should().Equal("NotPermitted");
        }

        var (crcArchiveBytes, _) = await GetBytesWithShaHeaderAsync(siteACrc, $"studies/{studyRef}/exports?format=study_archive&lock_filter=all", "application/zip", ct);
        using (var zip = new ZipArchive(new MemoryStream(crcArchiveBytes), ZipArchiveMode.Read))
        {
            var entryNames = zip.Entries.Select(e => e.FullName).ToList();
            entryNames.Should().NotContain(p => p.Contains("collection_masked_records", StringComparison.OrdinalIgnoreCase));
            entryNames.Should().NotContain(p => p.Contains("masked-", StringComparison.OrdinalIgnoreCase));
            await using var manifestStream = await zip.GetEntry("manifest.json")!.OpenAsync(ct);
            using var manifestReader = new StreamReader(manifestStream, Encoding.UTF8);
            using var manifestDoc = JsonDocument.Parse(await manifestReader.ReadToEndAsync(ct));
            manifestDoc.RootElement.GetProperty("hidden_scopes").GetProperty("more_exist").GetBoolean().Should().BeTrue();
            await using var locksStream = await zip.GetEntry("locks/locks.json")!.OpenAsync(ct);
            using var locksReader = new StreamReader(locksStream, Encoding.UTF8);
            var locksJson = await locksReader.ReadToEndAsync(ct);
            locksJson.Should().NotContain("NotPermitted").And.NotContain("SUBJ-B");
            await using var auditStream = await zip.GetEntry("audit/audit_trail.json")!.OpenAsync(ct);
            using var auditReader = new StreamReader(auditStream, Encoding.UTF8);
            var auditJson = await auditReader.ReadToEndAsync(ct);
            auditJson.Should().NotContain("SUBJ-B").And.NotContain("138.25");
        }

        // ADR-2590 count invariance: adding another participant + observations on Site B changes ZERO bytes/counts of Site A's hidden-scope exports.
        ProtocolVersionId versionB2;
        await using (var db = fixture.CreateContext(tenant))
        {
            versionB2 = await db.ProtocolVersions.AsNoTracking().Where(v => v.StudyId == study).Select(v => v.Id).SingleAsync(ct);
        }
        var participantB2 = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented B2"), ct);
        SubjectCode.TryFrom("SUBJ-B2", out var subjB2);
        var screenedB2 = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantB2, siteB, subjB2, Tick().AddDays(-1), KernelProtocolScenario.Key(), "Screened B2"), ct);
        var subjectObjB2 = new KernelObservationScenario.Subject(tenant, study, siteB, screenedB2.EnrollmentId);
        var openedB2 = await _scenario.Protocol.OkAsync(Open(subjectObjB2, CollectionInstanceId.New(), Tick()), ct);
        await _scenario.Protocol.OkAsync(
            Save(new Form(subjectObjB2, versionB2, openedB2.CollectionInstanceId), Value(Systolic, new QuantityValue(144.00m, "mm[Hg]"))),
            ct);

        var crcCsvAfterB2 = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=csv&lock_filter=all", "text/csv", ct);
        crcCsvAfterB2.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(4, "Site B growth must not add rows to Site A's export");
        crcCsvAfterB2.Should().NotContain("SUBJ-B2").And.NotContain("144.00");

        // Site A coordinator requesting SUBJ-B's casebook receives an explicit masked NotPermitted casebook with collapsed empty collections.
        var (maskedCasebookBytes, _) = await GetBytesWithShaHeaderAsync(siteACrc, $"enrollments/{enrollmentBRef}/casebook?format=json", "application/json", ct);
        var maskedCasebookText = System.Text.Encoding.UTF8.GetString(maskedCasebookBytes);
        maskedCasebookText.Should().NotContain("SUBJ-B").And.NotContain("138.25");
        var maskedCasebook = JsonSerializer.Deserialize<KernelCasebookResponse>(maskedCasebookBytes, Json)!;
        maskedCasebook.Masked.Should().BeTrue();
        maskedCasebook.Mask.Should().Be("NotPermitted");
        maskedCasebook.SubjectCode.Should().Be("masked");
        maskedCasebook.Forms.Should().BeEmpty();
        maskedCasebook.Observations.Should().BeEmpty();
        maskedCasebook.Locks.Should().BeEmpty();

        // Blinded statistician (Study-wide grant, no enrollment.unblind): sees both SUBJ-A and SUBJ-B vitals, but treatment arm is Blinded!
        var statCsv = await GetTextAsync(blindedStat, $"studies/{studyRef}/exports?format=csv&lock_filter=all", "text/csv", ct);
        statCsv.Should().Contain("SUBJ-A").And.Contain("SUBJ-B").And.Contain("138.25");
        statCsv.Should().NotContain("Active Drug 100mg");
        statCsv.Should().Contain("Blinded");

        var (statCasebookBytes, _) = await GetBytesWithShaHeaderAsync(blindedStat, $"enrollments/{enrollmentARef}/casebook?format=json", "application/json", ct);
        var statCasebook = JsonSerializer.Deserialize<KernelCasebookResponse>(statCasebookBytes, Json)!;
        statCasebook.Observations.Should().ContainSingle(o => o.Masked && o.Mask == "Blinded" && o.DisplayValue == "masked");
        System.Text.Encoding.UTF8.GetString(statCasebookBytes).Should().NotContain("Active Drug 100mg");

        // Unblinded pharmacist (Study-wide grant + enrollment.unblind): sees "Active Drug 100mg" unmasked across locks, exports, and casebook!
        var pharmLocks = await SendAsync<KernelStudyLocksResponse>(
            unblindedPharm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        pharmLocks.Locks.Should().OnlyContain(l => !l.Masked);

        foreach (var (format, mediaType) in new[] { ("csv", "text/csv"), ("odm_xml", "application/xml"), ("dataset_json", "application/json") })
        {
            var pharmExport = await GetTextAsync(unblindedPharm, $"studies/{studyRef}/exports?format={format}&lock_filter=all", mediaType, ct);
            pharmExport.Should().Contain("Active Drug 100mg", $"Unblinded pharmacist sees treatment arm in {format}");
        }

        var (pharmCasebookBytes, _) = await GetBytesWithShaHeaderAsync(unblindedPharm, $"enrollments/{enrollmentARef}/casebook?format=json", "application/json", ct);
        System.Text.Encoding.UTF8.GetString(pharmCasebookBytes).Should().Contain("Active Drug 100mg");
    }

    /// <summary>
    /// Independent <c>(valid_at, known_at)</c> bitemporal cuts evaluate lock assertions and releases on the
    /// <c>known_at</c> axis (CLINICAL_KERNEL_SPECIFICATION §4.1), and repeated live casebook downloads without
    /// <c>as_of</c> return identical <c>X-Content-Sha256</c> digests across wall-clock ticks while appending fresh
    /// <c>AuditEntry</c> and <c>BiostatExportAuditLog</c> rows on every download.
    /// </summary>
    [Fact(DisplayName = "HostSplitBitemporalLockCutsAndLiveCasebookDigest_PreserveLockKnownAtAndDeterministicSha256")]
    [KernelRule("K-TRUTH-1")]
    [KernelRule("K-PROV-1")]
    public async Task HostSplitBitemporalLockCutsAndLiveCasebookDigestPreserveLockKnownAtAndDeterministicSha256()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, _, _, enrollmentA, _, _, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();

        using var dm = Client(code, DataManager);

        // Capture a valid_at cutoff strictly after observations were recorded, but BEFORE the lock is applied!
        await Task.Delay(20, ct);
        var tValidBeforeLock = SystemClock.Instance.UtcNow;
        await Task.Delay(20, ct);

        var appliedLock = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentARef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        var tKnownAfterLock = appliedLock.LockedAt!.Value.AddMilliseconds(5);

        // AC 5: valid_at < lock_ts and known_at >= lock_ts MUST return locked observations under locked_only!
        var vBeforeLockParam = Uri.EscapeDataString(tValidBeforeLock.ToString("O"));
        var kAfterLockParam = Uri.EscapeDataString(tKnownAfterLock.ToString("O"));
        var splitLockedCsv = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&valid_at={vBeforeLockParam}&known_at={kAfterLockParam}",
            "text/csv",
            ct);
        splitLockedCsv.Should().Contain("SUBJ-A").And.Contain("120.50");

        // Whereas when known_at is ALSO before lock_ts, zero observations are locked yet.
        var preLockCsv = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&valid_at={vBeforeLockParam}&known_at={vBeforeLockParam}",
            "text/csv",
            ct);
        preLockCsv.Should().NotContain("SUBJ-A");

        var splitLocks = await SendAsync<KernelStudyLocksResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks?valid_at={vBeforeLockParam}&known_at={kAfterLockParam}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        splitLocks.Locks.Should().ContainSingle(l => l.Ref == appliedLock.Ref && l.ReleasedAt == null);

        // Release the lock strictly after tValidBeforeRelease.
        await Task.Delay(20, ct);
        var tValidBeforeRelease = SystemClock.Instance.UtcNow;
        await Task.Delay(20, ct);

        var releasedLock = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, $"locks/{appliedLock.Ref}/release", ReleaseReason, ReleaseToken(DataManager, appliedLock.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);
        var tKnownAfterRelease = releasedLock.ReleasedAt!.Value.AddMilliseconds(5);

        // AC 6: known_at >= release_ts excludes that released lock even when valid_at < release_ts!
        var vBeforeReleaseParam = Uri.EscapeDataString(tValidBeforeRelease.ToString("O"));
        var kAfterReleaseParam = Uri.EscapeDataString(tKnownAfterRelease.ToString("O"));
        var postReleaseLockedCsv = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&valid_at={vBeforeReleaseParam}&known_at={kAfterReleaseParam}",
            "text/csv",
            ct);
        postReleaseLockedCsv.Should().NotContain("SUBJ-A");

        var postReleaseUnlockedCsv = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=unlocked_only&valid_at={vBeforeReleaseParam}&known_at={kAfterReleaseParam}",
            "text/csv",
            ct);
        postReleaseUnlockedCsv.Should().Contain("SUBJ-A").And.Contain("120.50");

        // AC 8: Repeated live casebook downloads without as_of return identical X-Content-Sha256 and wire bytes across formats,
        // while still appending a new BiostatExportAuditLog row on every download!
        int biostatLogsBefore;
        await using (var db = fixture.CreateAdminContext())
        {
            biostatLogsBefore = await db.BiostatExportAuditLogs.CountAsync(l => l.TenantId == tenant && l.EnrollmentId == enrollmentA, ct);
        }

        foreach (var (fmt, media) in new[] { ("json", "application/json"), ("odm_xml", "application/xml"), ("html", "text/html"), ("pdf", "application/pdf") })
        {
            var (bytes1, sha1) = await GetBytesWithShaHeaderAsync(dm, $"enrollments/{enrollmentARef}/casebook?format={fmt}", media, ct);
            fixture.Clock.Advance(TimeSpan.FromSeconds(7));
            await Task.Delay(15, ct);
            var (bytes2, sha2) = await GetBytesWithShaHeaderAsync(dm, $"enrollments/{enrollmentARef}/casebook?format={fmt}", media, ct);
            sha2.Should().Be(sha1, $"repeated live {fmt} casebook downloads on unchanged enrollment data must return identical X-Content-Sha256");
            bytes2.Should().Equal(bytes1, $"repeated live {fmt} casebook downloads on unchanged enrollment data must return identical bytes");
        }

        await using (var db = fixture.CreateAdminContext())
        {
            var biostatLogsAfter = await db.BiostatExportAuditLogs.CountAsync(l => l.TenantId == tenant && l.EnrollmentId == enrollmentA, ct);
            biostatLogsAfter.Should().Be(biostatLogsBefore + 8, "every live casebook download must persist an immutable BiostatExportAuditLog entry");
        }
    }

    /// <summary>
    /// A study-scoped data manager holding only <c>lock.apply</c> and <c>lock.release</c> at <see cref="AuthorityScope.Study"/>
    /// (with no tenant-scoped <c>attestation.sign</c> grant) applies and releases signed locks atomically, failed lock
    /// preconditions leave zero orphan <see cref="Attestation"/> rows, and <see cref="KernelLockResponse"/> hydrates
    /// <c>study_site_ref</c> and <c>enrollment_ref</c> on visible locks while masking them to <see langword="null"/> on
    /// restricted locks.
    /// </summary>
    [Fact(DisplayName = "HostLock_StudyScopedDataManagerWithoutTenantAttestGrant_SignsAtomically_AndHydratesLineageRefs")]
    [KernelRule("K-AUTH-4")]
    public async Task HostLockStudyScopedDataManagerWithoutTenantAttestGrantSignsAtomicallyAndHydratesLineageRefs()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, siteA, siteB, enrollmentA, enrollmentB, _, sbpObservationA) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var siteARef = NodeRef.For(siteA).ToString();
        var siteBRef = NodeRef.For(siteB).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();
        var enrollmentBRef = NodeRef.For(enrollmentB).ToString();
        var obsARef = NodeRef.For(sbpObservationA).ToString();

        const string studyScopedDm = "dm.study-only@kernel-lock-export.test";
        await IssueAsync(tenant, studyScopedDm, "lock.apply", AuthorityScope.Study(study), ct);
        await IssueAsync(tenant, studyScopedDm, "lock.release", AuthorityScope.Study(study), ct);

        using var studyDm = Client(code, studyScopedDm);
        using var siteACrc = Client(code, SiteACoordinator);

        // Apply Frozen lock on observation A as study-scoped DM (no tenant attestation.sign grant).
        var appliedObs = await SendAsync<KernelLockResponse>(
            studyDm,
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(studyScopedDm), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = obsARef,
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        appliedObs.StudySiteRef.Should().Be(siteARef);
        appliedObs.EnrollmentRef.Should().Be(enrollmentARef);
        appliedObs.AttestationRef.Should().StartWith("attestation:");

        // Duplicate Frozen lock on observation A returns 409 AlreadyLocked and writes zero orphan Attestation rows.
        int attestationsBeforeConflict;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsBeforeConflict = await db.Attestations.CountAsync(ct);
        }

        using (var conflict = await studyDm.SendAsync(BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(studyScopedDm), KernelProtocolScenario.Key(), new
        {
            study_ref = studyRef,
            scope_ref = obsARef,
            kind = "Frozen",
            meaning = "Approved",
        }), ct))
        {
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(conflict, ct)).Should().Be(AssertionErrors.AlreadyLocked.Code);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(
                attestationsBeforeConflict,
                "a refused duplicate lock must roll back or skip inline attestation creation");
        }

        // Apply Locked on Site B enrollment: visible to study DM with hydrated study_site_ref + enrollment_ref, masked to null for Site A CRC.
        var appliedSiteBEnrollment = await SendAsync<KernelLockResponse>(
            studyDm,
            BuildRequest(HttpMethod.Post, "locks", "Lock Site B enrollment", ApplyToken(studyScopedDm), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentBRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        appliedSiteBEnrollment.StudySiteRef.Should().Be(siteBRef);
        appliedSiteBEnrollment.EnrollmentRef.Should().Be(enrollmentBRef);

        var crcList = await SendAsync<KernelStudyLocksResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks?active_only=true", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        var visibleCrcLock = crcList.Locks.Should().ContainSingle(l => !l.Masked && l.Ref == appliedObs.Ref).Subject;
        visibleCrcLock.StudySiteRef.Should().Be(siteARef);
        visibleCrcLock.EnrollmentRef.Should().Be(enrollmentARef);

        var maskedCrcLock = crcList.Locks.Should().ContainSingle(l => l.Masked && l.ScopeKind == NodeKinds.Enrollment).Subject;
        maskedCrcLock.Mask.Should().Be("NotPermitted");
        maskedCrcLock.StudySiteRef.Should().BeNull();
        maskedCrcLock.EnrollmentRef.Should().BeNull();

        // Release observation A lock as study-scoped DM without tenant attestation.sign grant.
        var releasedObs = await SendAsync<KernelLockResponse>(
            studyDm,
            BuildRequest(HttpMethod.Post, $"locks/{appliedObs.Ref}/release", ReleaseReason, ReleaseToken(studyScopedDm, appliedObs.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);
        releasedObs.Released.Should().BeTrue();
        releasedObs.StudySiteRef.Should().Be(siteARef);
        releasedObs.EnrollmentRef.Should().Be(enrollmentARef);
    }

    /// <summary>
    /// <c>GET /api/v1/kernel/nodes/{ref}/children</c> resolves <c>collection_instance</c> children under
    /// <c>enrollment</c> and <c>observation</c> children under <c>collection_instance</c>, masking unauthorized site
    /// nodes with <c>NotPermitted</c> and randomized treatment observations with <c>Blinded</c> unless the caller holds
    /// <c>enrollment.unblind</c>.
    /// </summary>
    [Fact(DisplayName = "HostNavigation_EnrollmentAndCollectionChildren_EnforceSiteVisibilityAndBlindingMasks")]
    [KernelRule("K-AUTH-4")]
    public async Task HostNavigationEnrollmentAndCollectionChildrenEnforceSiteVisibilityAndBlindingMasks()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, siteA, _, enrollmentA, enrollmentB, formA, sbpObservationA) = await SeedStudyAsync(ct);
        ProtocolVersionId version;
        await using (var db = fixture.CreateContext(tenant))
        {
            version = await db.ProtocolVersions.AsNoTracking().Where(v => v.StudyId == study).Select(v => v.Id).SingleAsync(ct);
        }
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();
        var enrollmentBRef = NodeRef.For(enrollmentB).ToString();
        var formARef = NodeRef.For(formA).ToString();

        // Define a randomization concept, add a second protocol form with it, and save a blinded observation inside a collection instance.
        var treatmentConcept = await DefineConceptWithCodeAsync(tenant, "randomization_treatment_assignment", "Randomized treatment arm", ct);
        var blindedFormId = CollectionInstanceId.New();
        var subjectObjA = new KernelObservationScenario.Subject(tenant, study, siteA, enrollmentA);
        await _scenario.Protocol.OkAsync(Open(subjectObjA, blindedFormId, Tick()), ct);
        var savedBlinded = await _scenario.Protocol.OkAsync(
            Save(new Form(subjectObjA, version, blindedFormId), Value(Systolic, new QuantityValue(118.0m, "mm[Hg]"))),
            ct);
        var regularObsInSecondForm = savedBlinded.Values.Single().ObservationId;

        // Also record a blinded treatment observation and verify direct node & collection children masking.
        var blindedObsId = ObservationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                blindedObsId,
                enrollmentA,
                treatmentConcept,
                new TextValue("Active Drug 100mg"),
                Tick().AddMinutes(-1),
                KernelProtocolScenario.Key(),
                "Randomization arm allocation"),
            ct);

        using var siteACrc = Client(code, SiteACoordinator);
        using var blindedStat = Client(code, BlindedStatistician);
        using var unblindedPharm = Client(code, UnblindedPharmacist);

        // 1. Enrollment A children return both opened collection_instance nodes with child_count and the standalone blinded observation masked in place.
        using (var childrenResp = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{enrollmentARef}/children", UriKind.Relative), ct))
        {
            var body = await childrenResp.Content.ReadAsStringAsync(ct);
            childrenResp.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var doc = JsonDocument.Parse(body);
            var children = doc.RootElement.EnumerateArray().ToList();
            var forms = children.Where(c => c.GetProperty("node").GetProperty("kind").GetString() == NodeKinds.CollectionInstance).ToList();
            forms.Should().HaveCount(2);
            forms.Should().Contain(f =>
                f.GetProperty("node").GetProperty("ref").GetString()!.StartsWith($"{formARef}@", StringComparison.Ordinal)
                && f.GetProperty("child_count").GetInt32() == 2);
            children.Should().ContainSingle(c =>
                c.GetProperty("node").GetProperty("kind").GetString() == NodeKinds.Observation
                && c.GetProperty("node").GetProperty("mask").GetString() == "Blinded"
                && c.GetProperty("node").GetProperty("ref").ValueKind == JsonValueKind.Null);
        }

        // 2. CollectionInstance A children return its two recorded observations (Systolic and Diastolic), unmasked for Site A CRC.
        using (var obsResp = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{formARef}/children", UriKind.Relative), ct))
        {
            var body = await obsResp.Content.ReadAsStringAsync(ct);
            obsResp.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var doc = JsonDocument.Parse(body);
            var observations = doc.RootElement.EnumerateArray().ToList();
            observations.Should().HaveCount(2);
            observations.Should().Contain(o =>
                o.GetProperty("node").GetProperty("kind").GetString() == NodeKinds.Observation
                && o.GetProperty("node").GetProperty("ref").GetString()!.StartsWith($"{NodeRef.For(sbpObservationA)}@", StringComparison.Ordinal)
                && o.GetProperty("child_count").GetInt32() == 0);
            observations.Should().ContainSingle(o =>
                !o.GetProperty("node").GetProperty("ref").GetString()!.StartsWith($"observation:{regularObsInSecondForm}", StringComparison.Ordinal)
                && o.GetProperty("node").GetProperty("code").GetString() == Diastolic);
        }

        // 3. Site A coordinator requesting Site B enrollment's children receives NotPermitted problem (cannot enumerate another site's forms).
        using (var otherSiteChildren = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{enrollmentBRef}/children", UriKind.Relative), ct))
        {
            otherSiteChildren.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 4. Blinded observation node resolves as Masked (Blinded) to BlindedStatistician and unmasked to UnblindedPharmacist.
        var blindedObsRef = NodeRef.For(blindedObsId).ToString();
        using (var statNodeResp = await blindedStat.GetAsync(new Uri($"/api/v1/kernel/nodes/{blindedObsRef}", UriKind.Relative), ct))
        {
            var body = await statNodeResp.Content.ReadAsStringAsync(ct);
            statNodeResp.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("kind").GetString().Should().Be(NodeKinds.Observation);
            doc.RootElement.GetProperty("mask").GetString().Should().Be("Blinded");
            doc.RootElement.GetProperty("ref").ValueKind.Should().Be(JsonValueKind.Null);
            doc.RootElement.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);
        }

        using (var pharmNodeResp = await unblindedPharm.GetAsync(new Uri($"/api/v1/kernel/nodes/{blindedObsRef}", UriKind.Relative), ct))
        {
            var body = await pharmNodeResp.Content.ReadAsStringAsync(ct);
            pharmNodeResp.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("kind").GetString().Should().Be(NodeKinds.Observation);
            doc.RootElement.GetProperty("mask").ValueKind.Should().Be(JsonValueKind.Null);
            doc.RootElement.GetProperty("ref").GetString().Should().StartWith($"{blindedObsRef}@");
        }
    }

    /// <summary>
    /// <c>GET /api/v1/kernel/studies/{studyRef}/locks/readiness</c> reports open query and missing required item counts,
    /// masks unauthorized site and blinded treatment blockers under <c>K-AUTH-4</c>, <c>POST /api/v1/kernel/locks</c>
    /// rejects <c>Locked</c> with <c>409 K.Assertion.ScopeNotReady</c> + structured <c>Explanation</c> and zero orphan
    /// <c>Attestation</c> rows while keeping <c>Frozen</c> available, and navigation <c>/nodes/{ref}/children</c> resolves
    /// <c>collection_instance</c> and <c>observation</c> nodes under <c>K-AUTH-4</c> masking.
    /// </summary>
    [Fact(DisplayName = "HostLockReadiness_BlocksLockedWithExplanation_AllowsFrozen_WritesZeroOrphanAttestations_AndMasksNavigation")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostLockReadinessBlocksLockedWithExplanationAllowsFrozenWritesZeroOrphanAttestationsAndMasksNavigation()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, siteA, siteB, enrollmentA, enrollmentB, formA, sbpObservationA) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();
        var formARef = NodeRef.For(formA).ToString();

        // Open a second form on Site B with missing Diastolic AND raise an open query on Site A's systolic BP + a blinded treatment observation.
        ProtocolVersionId version;
        await using (var db = fixture.CreateContext(tenant))
        {
            version = (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.StudyId == study, ct)).Id;
        }
        var subjectB = new KernelObservationScenario.Subject(tenant, study, siteB, enrollmentB);
        var incompleteFormB = await _scenario.Protocol.OkAsync(Open(subjectB, CollectionInstanceId.New(), Tick()), ct);
        await _scenario.Protocol.OkAsync(
            Save(new Form(subjectB, version, incompleteFormB.CollectionInstanceId), Value(Systolic, new QuantityValue(134.00m, "mm[Hg]"))),
            ct);

        var openQueryA = await _scenario.Protocol.OkAsync(
            new RaiseQuery(
                tenant,
                AssertionId.New(),
                enrollmentA,
                sbpObservationA,
                "Verify supine rest duration prior to reading",
                KernelProtocolScenario.Key(),
                "Monitor query on Site A"),
            ct);

        var treatmentConcept = await DefineConceptWithCodeAsync(tenant, "randomization_treatment_assignment", "Randomized treatment arm", ct);
        var blindedObsId = ObservationId.New();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                blindedObsId,
                enrollmentA,
                treatmentConcept,
                new TextValue("Active Drug 200mg"),
                Tick().AddMinutes(-1),
                KernelProtocolScenario.Key(),
                "Randomization allocation"),
            ct);
        var blindedQuery = await _scenario.Protocol.OkAsync(
            new RaiseQuery(
                tenant,
                AssertionId.New(),
                enrollmentA,
                blindedObsId,
                "Confirm pharmacy kit reconciliation",
                KernelProtocolScenario.Key(),
                "Unblinded pharmacy query"),
            ct);

        using var dm = Client(code, DataManager);
        using var siteACrc = Client(code, SiteACoordinator);
        using var blindedStat = Client(code, BlindedStatistician);

        // 1. Readiness endpoint for DataManager (study-scoped, blinded by default) reports 2 open queries + 1 missing required item,
        // with the blinded treatment query masked as Blinded!
        var dmReadiness = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        dmReadiness.ReadyForLock.Should().BeFalse();
        dmReadiness.OpenQueryCount.Should().Be(2);
        dmReadiness.MissingRequiredItemCount.Should().Be(1);
        dmReadiness.Blockers.Should().ContainSingle(b => !b.Masked && b.TargetRef == NodeRef.For(sbpObservationA).ToString() && b.Owner != null);
        dmReadiness.Blockers.Should().ContainSingle(b => b.Masked && b.Mask == "Blinded" && b.TargetRef == null);
        dmReadiness.Blockers.Should().ContainSingle(b => !b.Masked && b.Kind == "MissingRequiredItem" && b.TargetRef == NodeRef.For(incompleteFormB.CollectionInstanceId).ToString());

        // Site A coordinator sees Site B's missing required item masked as NotPermitted and blinded query masked as Blinded.
        var crcReadiness = await SendAsync<KernelLockReadinessResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcReadiness.Blockers.Should().ContainSingle(b => b.Masked && b.Mask == "NotPermitted" && b.TargetRef == null);
        crcReadiness.Blockers.Should().ContainSingle(b => b.Masked && b.Mask == "Blinded" && b.TargetRef == null);

        // 2. Navigation /nodes/{ref}/children resolves collection_instance and observation scopes and masks unauthorized/blinded nodes.
        using (var enrollFormsRes = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{enrollmentARef}/children", UriKind.Relative), ct))
        {
            enrollFormsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var enrollFormsText = await enrollFormsRes.Content.ReadAsStringAsync(ct);
            enrollFormsText.Should().Contain(formARef);
            enrollFormsText.Should().Contain("Blinded").And.NotContain(blindedObsId.Value.ToString("D"));
        }

        using (var formObsRes = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{formARef}/children", UriKind.Relative), ct))
        {
            formObsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var formObsText = await formObsRes.Content.ReadAsStringAsync(ct);
            formObsText.Should().Contain(NodeRef.For(sbpObservationA).ToString());
        }

        using (var siteBFormsForCrc = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/nodes/{NodeRef.For(enrollmentB)}/children", UriKind.Relative), ct))
        {
            siteBFormsForCrc.StatusCode.Should().Be(HttpStatusCode.Forbidden, "enumerating children of an unauthorized enrollment fails closed with 403");
        }

        // 3. Attempting Locked on enrollmentA fails with 409 K.Assertion.ScopeNotReady + structured Explanation and writes ZERO orphan Attestations!
        int attestationsBefore;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsBefore = await db.Attestations.CountAsync(ct);
        }

        using (var blockedLock = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentARef,
                kind = "Locked",
                meaning = "Approved",
            }),
            ct))
        {
            blockedLock.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var bodyText = await blockedLock.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(bodyText);
            doc.RootElement.GetProperty("code").GetString().Should().Be(AssertionErrors.ScopeNotReady.Code);
            var explanation = doc.RootElement.GetProperty("explanation");
            explanation.GetProperty("subject").GetString().Should().Be(enrollmentARef);
            explanation.GetProperty("conditions").GetArrayLength().Should().BeGreaterThan(0);
            explanation.GetProperty("next_actions").GetArrayLength().Should().BeGreaterThan(0);
        }

        // Unknown target scope also fails BEFORE Attest -> still zero new Attestation rows!
        using (var missingScopeLock = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = $"enrollment:{Guid.CreateVersion7():D}",
                kind = "Frozen",
                meaning = "Approved",
            }),
            ct))
        {
            missingScopeLock.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(
                attestationsBefore,
                "rejected Lock requests must validate readiness and scope existence before writing Attestations");
        }

        // 4. Frozen succeeds on enrollmentA even with open queries, allowing query answer and closure while Frozen!
        var frozen = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze enrollment during query closure", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentARef,
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        // Duplicate Frozen on enrollmentA fails with 409 AlreadyLocked and writes zero extra Attestations!
        int attestationsAfterFreeze;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsAfterFreeze = await db.Attestations.CountAsync(ct);
        }

        using (var duplicateFreeze = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", "Duplicate freeze", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentARef,
                kind = "Frozen",
                meaning = "Approved",
            }),
            ct))
        {
            duplicateFreeze.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(duplicateFreeze, ct)).Should().Be(AssertionErrors.AlreadyLocked.Code);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(attestationsAfterFreeze);
        }

        // Answer and close both queries on enrollmentA while Frozen!
        await _scenario.Protocol.OkAsync(
            new AnswerQuery(tenant, AssertionId.New(), enrollmentA, openQueryA.AssertionId, "5 min supine rest documented", KernelProtocolScenario.Key(), "Site response"),
            ct);
        await _scenario.Protocol.OkAsync(
            new RetractAssertion(tenant, AssertionId.New(), enrollmentA, openQueryA.AssertionId, KernelProtocolScenario.Key(), "Monitor closed query"),
            ct);
        await _scenario.Protocol.OkAsync(
            new RetractAssertion(tenant, AssertionId.New(), enrollmentA, blindedQuery.AssertionId, KernelProtocolScenario.Key(), "Pharmacy closed query"),
            ct);

        // Release Frozen, verify enrollmentA readiness is now ready_for_lock == true, and apply Locked!
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, $"locks/{frozen.Ref}/release", "Release freeze for hard lock", ReleaseToken(DataManager, frozen.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);

        var readyNow = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(enrollmentARef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        readyNow.ReadyForLock.Should().BeTrue();
        readyNow.OpenQueryCount.Should().Be(0);
        readyNow.MissingRequiredItemCount.Should().Be(0);

        var lockedEnrollmentA = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = enrollmentARef,
                kind = "Locked",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        lockedEnrollmentA.Kind.Should().Be("Locked");
        _ = siteA;
        _ = blindedStat;
    }

    /// <summary>
    /// Streams valid <c>%PDF-1.7</c> casebooks (<c>application/pdf</c>) and <c>sas_bundle</c>, <c>r_bundle</c>, and
    /// <c>study_archive</c> ZIP archives (<c>application/zip</c>) with verified <c>X-Content-Sha256</c> headers,
    /// <c>manifest.json</c> per-file SHA-256 digests, statistical syntax scripts, zero <c>IdentityVault</c> PII, and
    /// <c>K-AUTH-4</c> masking.
    /// </summary>
    [Fact(DisplayName = "HostExportsAndCasebook_StreamValidPdf17Casebook_AndSasRAndStudyArchiveZipsWithVerifiedManifests")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostExportsAndCasebookStreamValidPdf17CasebookAndSasRAndStudyArchiveZipsWithVerifiedManifests()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, studyCode, _, _, enrollmentA, enrollmentB, _, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();
        var enrollmentBRef = NodeRef.For(enrollmentB).ToString();

        var treatmentConcept = await DefineConceptWithCodeAsync(tenant, "randomization_treatment_assignment", "Randomized treatment arm", ct);
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                ObservationId.New(),
                enrollmentA,
                treatmentConcept,
                new TextValue("Investigational Arm X"),
                Tick().AddMinutes(-1),
                KernelProtocolScenario.Key(),
                "Randomization allocation"),
            ct);

        using var dm = Client(code, DataManager);
        using var siteACrc = Client(code, SiteACoordinator);
        using var unblindedPharm = Client(code, UnblindedPharmacist);

        // 1. PDF-1.7 casebook stream + SHA-256 + K-AUTH-4 masking
        var (pdfBytes, pdfSha) = await GetBytesWithShaHeaderAsync(dm, $"enrollments/{enrollmentARef}/casebook?format=pdf", "application/pdf", ct);
        Convert.ToHexStringLower(SHA256.HashData(pdfBytes)).Should().Be(pdfSha);
        var pdfText = Encoding.ASCII.GetString(pdfBytes);
        pdfText.Should().StartWith("%PDF-1.7\n");
        pdfText.Should().Contain("/Type /Catalog").And.Contain("/Type /Pages").And.Contain("\nxref\n").And.Contain("%%EOF");
        pdfText.Should().Contain("SUBJ-A").And.Contain("120.50");
        pdfText.Should().NotContain("Investigational Arm X", "DataManager lacks enrollment.unblind");
        pdfText.Should().Contain("Blinded");
        AssertNoDirectIdentifiers(pdfText);

        var (maskedPdfBytes, maskedPdfSha) = await GetBytesWithShaHeaderAsync(siteACrc, $"enrollments/{enrollmentBRef}/casebook?format=pdf", "application/pdf", ct);
        Convert.ToHexStringLower(SHA256.HashData(maskedPdfBytes)).Should().Be(maskedPdfSha);
        var maskedPdfText = Encoding.ASCII.GetString(maskedPdfBytes);
        maskedPdfText.Should().StartWith("%PDF-1.7\n");
        maskedPdfText.Should().NotContain("SUBJ-B").And.NotContain("138.25").And.Contain("NotPermitted");

        var (pharmPdfBytes, _) = await GetBytesWithShaHeaderAsync(unblindedPharm, $"enrollments/{enrollmentARef}/casebook?format=pdf", "application/pdf", ct);
        Encoding.ASCII.GetString(pharmPdfBytes).Should().Contain("Investigational Arm X");

        // 2. SAS bundle, R bundle, and Study Archive ZIP streams + manifest SHA-256 verification + K-AUTH-4 masking
        foreach (var format in new[] { "sas_bundle", "r_bundle", "study_archive" })
        {
            var (zipBytes, zipSha) = await GetBytesWithShaHeaderAsync(
                siteACrc,
                $"studies/{studyRef}/exports?format={format}&lock_filter=all",
                "application/zip",
                ct);
            Convert.ToHexStringLower(SHA256.HashData(zipBytes)).Should().Be(zipSha);

            using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json");
            manifestEntry.Should().NotBeNull($"{format} must include manifest.json");

            string manifestText;
            await using var manifestStream = await manifestEntry!.OpenAsync(ct);
            using (var reader = new StreamReader(manifestStream, Encoding.UTF8))
            {
                manifestText = await reader.ReadToEndAsync(ct);
            }

            using var manifestDoc = JsonDocument.Parse(manifestText);
            manifestDoc.RootElement.GetProperty("study_code").GetString().Should().Be(studyCode);
            manifestDoc.RootElement.GetProperty("format").GetString().Should().Be(format);

            var filesArray = manifestDoc.RootElement.GetProperty("files");
            filesArray.GetArrayLength().Should().BeGreaterThan(0);

            foreach (var fileElem in filesArray.EnumerateArray())
            {
                var entryPath = fileElem.GetProperty("path").GetString()!;
                var expectedSha = fileElem.GetProperty("sha256").GetString()!;
                var expectedSize = fileElem.GetProperty("size_bytes").GetInt64();

                var entry = archive.GetEntry(entryPath);
                entry.Should().NotBeNull($"manifest entry {entryPath} must exist in {format}");
                using var ms = new MemoryStream();
                await using (var entryStream = await entry!.OpenAsync(ct))
                {
                    await entryStream.CopyToAsync(ms, ct);
                }

                var entryBytes = ms.ToArray();
                entryBytes.LongLength.Should().Be(expectedSize, $"byte length of {entryPath} in {format}");
                Convert.ToHexStringLower(SHA256.HashData(entryBytes)).Should().Be(expectedSha, $"SHA-256 digest of {entryPath} in {format}");

                var asText = Encoding.UTF8.GetString(entryBytes);
                AssertNoDirectIdentifiers(asText);
                asText.Should().NotContain("SUBJ-B", $"Site A coordinator ZIP {format} entry {entryPath} must mask Site B");
                asText.Should().NotContain("138.25", $"Site A coordinator ZIP {format} entry {entryPath} must mask Site B values");
                asText.Should().NotContain("Investigational Arm X", $"Site A coordinator ZIP {format} entry {entryPath} must mask blinded treatment");
            }

            var entryNames = archive.Entries.Select(e => e.FullName).ToList();
            entryNames.Should().Contain("data/observations.csv");
            entryNames.Should().Contain("data/dataset.json");

            if (format == "sas_bundle")
            {
                entryNames.Should().Contain(n => n.EndsWith(".sas", StringComparison.Ordinal));
            }
            else if (format == "r_bundle")
            {
                entryNames.Should().Contain(n => n.EndsWith(".R", StringComparison.Ordinal));
            }
            else
            {
                entryNames.Should().Contain(n => n.EndsWith(".sas", StringComparison.Ordinal));
                entryNames.Should().Contain(n => n.EndsWith(".R", StringComparison.Ordinal));
                entryNames.Should().Contain(n => n.EndsWith(".odm.xml", StringComparison.Ordinal));
                entryNames.Should().Contain(n => n.StartsWith("casebooks/", StringComparison.Ordinal) && n.EndsWith(".pdf", StringComparison.Ordinal));
                entryNames.Should().Contain("locks/locks.json");
                entryNames.Should().Contain("audit/audit_trail.json");
            }
        }
    }

    private static string ApplyToken(string signer) =>
        TestSecurityContext.CreateSigToken(signer, KernelLockEndpoints.ApplyLockAction, KernelLockEndpoints.ApplyLockSemanticAction);

    private static string ReleaseToken(string signer, string lockRef) =>
        TestSecurityContext.CreateSigToken(signer, KernelLockEndpoints.ReleaseLockAction(lockRef), KernelLockEndpoints.ReleaseLockSemanticAction);

    private static void AssertNoDirectIdentifiers(string payload)
    {
        foreach (var forbidden in new[] { "Jane Legal Doe", "1980-04-15", "jane.doe@personal.example", "national_id", "legal_name" })
        {
            payload.Should().NotContain(forbidden);
        }
    }

    private async Task<(
        TenantId Tenant,
        string Code,
        StudyId Study,
        string StudyCode,
        StudySiteId SiteA,
        StudySiteId SiteB,
        EnrollmentId EnrollmentA,
        EnrollmentId EnrollmentB,
        CollectionInstanceId FormA,
        ObservationId SbpObservationA)> SeedStudyAsync(CancellationToken ct)
    {
        var code = $"T-LCK-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Kernel lock and export tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);

        string studyCode;
        StudySiteId siteA;
        StudySiteId siteB;
        await using (var db = fixture.CreateContext(tenant))
        {
            var studyRow = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == study, ct);
            studyCode = studyRow.StudyCode;
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var rawSiteA = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-A-{suffix}", "Memorial Hospital A", "US", KernelPostgresFixture.ActorId, "Seeded Site A");
            var rawSiteB = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-B-{suffix}", "City Clinic B", "US", KernelPostgresFixture.ActorId, "Seeded Site B");
            var linkA = new StudySite(StudySiteId.New(), tenant, study, rawSiteA.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site A");
            var linkB = new StudySite(StudySiteId.New(), tenant, study, rawSiteB.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site B");
            await fixture.SeedAsync(rawSiteA, rawSiteB, linkA, linkB);
            siteA = linkA.Id;
            siteB = linkB.Id;
        }

        var participantA = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented A"), ct);
        SubjectCode.TryFrom("SUBJ-A", out var subjA);
        var screenedA = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantA, siteA, subjA, Tick().AddDays(-2), KernelProtocolScenario.Key(), "Screened A"), ct);

        var participantB = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented B"), ct);
        SubjectCode.TryFrom("SUBJ-B", out var subjB);
        var screenedB = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantB, siteB, subjB, Tick().AddDays(-2), KernelProtocolScenario.Key(), "Screened B"), ct);

        // Open Vital Signs forms on both enrollments and record decimal observations (120.50 on SUBJ-A, 138.25 on SUBJ-B).
        var subjectObjA = new KernelObservationScenario.Subject(tenant, study, siteA, screenedA.EnrollmentId);
        var openedA = await _scenario.Protocol.OkAsync(Open(subjectObjA, CollectionInstanceId.New(), Tick()), ct);
        var savedA = await _scenario.Protocol.OkAsync(
            Save(new Form(subjectObjA, version, openedA.CollectionInstanceId), Value(Systolic, new QuantityValue(120.50m, "mm[Hg]")), Value(Diastolic, new QuantityValue(80.00m, "mm[Hg]"))),
            ct);

        var subjectObjB = new KernelObservationScenario.Subject(tenant, study, siteB, screenedB.EnrollmentId);
        var openedB = await _scenario.Protocol.OkAsync(Open(subjectObjB, CollectionInstanceId.New(), Tick()), ct);
        await _scenario.Protocol.OkAsync(
            Save(new Form(subjectObjB, version, openedB.CollectionInstanceId), Value(Systolic, new QuantityValue(138.25m, "mm[Hg]")), Value(Diastolic, new QuantityValue(88.00m, "mm[Hg]"))),
            ct);

        // Issue scoped grants:
        // - DataManager: attestation.sign at Tenant(), plus lock.apply, lock.release, collection.sign at Study(study)
        await IssueAsync(tenant, DataManager, "attestation.sign", AuthorityScope.Tenant(), ct);
        foreach (var cap in new[] { "lock.apply", "lock.release", "collection.sign", "study.export", "enrollment.export" })
        {
            await IssueAsync(tenant, DataManager, cap, AuthorityScope.Study(study), ct);
        }

        // - SiteACoordinator: observation.record, observation.correct, study.export, enrollment.export ONLY at StudySite(siteA) (no tenant/study grant!)
        foreach (var cap in new[] { "observation.record", "observation.correct", "study.export", "enrollment.export" })
        {
            await IssueAsync(tenant, SiteACoordinator, cap, AuthorityScope.StudySite(siteA), ct);
        }

        // - Monitor: observation.verify, assertion.retract at Study(study)
        foreach (var cap in new[] { "observation.verify", "assertion.retract" })
        {
            await IssueAsync(tenant, Monitor, cap, AuthorityScope.Study(study), ct);
        }

        // - BlindedStatistician: study-scoped read/export grant without enrollment.unblind
        foreach (var cap in new[] { "observation.verify", "study.export", "enrollment.export" })
        {
            await IssueAsync(tenant, BlindedStatistician, cap, AuthorityScope.Study(study), ct);
        }

        // - UnblindedPharmacist: study-scoped read/export grant PLUS enrollment.unblind
        foreach (var cap in new[] { "observation.verify", "study.export", "enrollment.export", "enrollment.unblind" })
        {
            await IssueAsync(tenant, UnblindedPharmacist, cap, AuthorityScope.Study(study), ct);
        }

        Tick();
        var sbpObsA = savedA.Values.Single(v => v.ItemId == Systolic).ObservationId;
        return (tenant, code, study, studyCode, siteA, siteB, screenedA.EnrollmentId, screenedB.EnrollmentId, openedA.CollectionInstanceId, sbpObsA);
    }

    private async Task<ConceptRef> DefineConceptWithCodeAsync(TenantId tenant, string code, string label, CancellationToken ct) =>
        await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                $"{label} ({code})",
                $"{label} (definition).",
                KernelProtocolScenario.Key(),
                "Defined for blinding test"),
            ct);

    private Task<GrantState> IssueAsync(TenantId tenant, string userId, string capability, AuthorityScope scope, CancellationToken ct) =>
        _scenario.Protocol.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(userId), capability, null, scope, Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
            ct);

    private HttpClient Client(string tenantCode, string userId, bool tenantReadOnly = false)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode, TenantReadOnly: tenantReadOnly));
    }

    /// <summary>
    /// On an expired workspace subscription, closeout lock application/release and form instance electronic signatures
    /// remain active, while non-closeout step-up endpoints such as <c>POST /api/v1/kernel/library-items/{itemRef}/approve</c>
    /// fail closed with <c>403 K.Subscription.Expired</c> without writing orphan <c>Attestation</c> rows.
    /// </summary>
    [Fact(DisplayName = "ExpiredWorkspace_PreservesCloseoutLocksAndFormSignatures_WhileBlockingLibraryItemApprovalWithoutOrphanAttestation")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ExpiredWorkspacePreservesCloseoutLocksAndFormSignaturesWhileBlockingLibraryItemApprovalWithoutOrphanAttestation()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, _, _, _, _, formA, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();

        await IssueAsync(tenant, DataManager, "library_item.draft", AuthorityScope.Tenant(), ct);
        await IssueAsync(tenant, DataManager, "library_item.approve", AuthorityScope.Tenant(), ct);

        var drafted = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(
                tenant,
                LibraryItemId.New(),
                LibraryItemKind.RulePack,
                "vitals-closeout-check",
                "Vitals Closeout Rule Pack",
                RulePackJson.ToJson(SampleRulePacks.Sample()),
                [],
                KernelProtocolScenario.Key(),
                "Draft rule pack prior to trial expiry"),
            ct);
        var packRef = NodeRef.For(drafted.Id).ToString();

        // Provision and expire the workspace subscription in PostgreSQL.
        var subscriptionId = SubscriptionId.New(Tick());
        await _scenario.Protocol.OkAsync(
            new ProvisionSubscription(
                tenant,
                subscriptionId,
                CommercialTier.Individual,
                SubscriptionStatus.Trialing,
                Tick().AddDays(30),
                KernelProtocolScenario.Key(),
                "Provision trial subscription"),
            ct);
        await _scenario.Protocol.OkAsync(
            new ExpireSubscription(
                tenant,
                subscriptionId,
                KernelProtocolScenario.Key(),
                "Expire trial subscription"),
            ct);

        using var expiredDm = Client(code, DataManager, tenantReadOnly: true);
        using var staleHeaderDm = Client(code, DataManager, tenantReadOnly: false);

        int attestationsBefore;
        await using (var db = fixture.CreateContext(tenant))
        {
            attestationsBefore = await db.Attestations.CountAsync(ct);
        }

        // 1. Non-closeout step-up approval (library-items/{packRef}/approve) is blocked by both ExpiredSubscriptionWriteGuardMiddleware
        //    (when X-Tenant-Read-Only: true) and KernelValidationEndpoints (when gateway cache is briefly stale), writing zero orphan Attestations!
        foreach (var client in new[] { expiredDm, staleHeaderDm })
        {
            var approveSigToken = TestSecurityContext.CreateSigToken(
                DataManager,
                KernelValidationEndpoints.ApproveLibraryItemAction(packRef),
                KernelValidationEndpoints.ApproveLibraryItemSemanticAction);
            using var approveReq = BuildRequest(
                HttpMethod.Post,
                $"library-items/{packRef}/approve",
                "Attempt rule pack approval after trial expiry",
                approveSigToken,
                KernelProtocolScenario.Key(),
                new { });
            using var approveRes = await client.SendAsync(approveReq, ct);
            approveRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(approveRes, ct)).Should().Be(KernelErrors.SubscriptionExpired.Code);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Attestations.CountAsync(ct)).Should().Be(
                attestationsBefore,
                "blocked library-item approval on an expired workspace must never leave an orphan Attestation row");
        }

        // 2. Regulated closeout form signature (/api/v1/execution/forms/{formA}/signatures) remains active while expired!
        using (var formGet = await expiredDm.GetAsync(new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}", UriKind.Relative), ct))
        {
            formGet.StatusCode.Should().Be(HttpStatusCode.OK);
            using var formJson = JsonDocument.Parse(await formGet.Content.ReadAsStringAsync(ct));
            string contentHash = formJson.RootElement.GetProperty("content_hash").GetString()!;
            using var signReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}/signatures", UriKind.Relative));
            signReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            signReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("PI closeout signature on expired workspace"));
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(DataManager, FormSignatureEndpoints.SignAction(formA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq.Content = JsonContent.Create(new { meaning = "approved", content_hash = contentHash });
            using var signRes = await expiredDm.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        // 3. Regulated closeout lock application and release (/api/v1/kernel/locks) remain active while expired!
        var appliedLock = await SendAsync<KernelLockResponse>(
            expiredDm,
            BuildRequest(
                HttpMethod.Post,
                "locks",
                LockReason,
                ApplyToken(DataManager),
                KernelProtocolScenario.Key(),
                new { study_ref = studyRef, scope_ref = studyRef, kind = "Locked", meaning = "Approved" }),
            HttpStatusCode.Created,
            ct);
        appliedLock.Ref.Should().StartWith("assertion:");

        var releasedLock = await SendAsync<KernelLockResponse>(
            expiredDm,
            BuildRequest(
                HttpMethod.Post,
                $"locks/{appliedLock.Ref}/release",
                ReleaseReason,
                ReleaseToken(DataManager, appliedLock.Ref!),
                KernelProtocolScenario.Key(),
                new { study_ref = studyRef, meaning = "Responsibility" }),
            HttpStatusCode.OK,
            ct);
        releasedLock.ReleasedAt.Should().NotBeNull();
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        string? reason,
        string? token,
        string? idempotencyKey,
        object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"/api/v1/kernel/{path}", UriKind.Relative));
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (token is not null)
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client,
        HttpRequestMessage request,
        HttpStatusCode expected,
        CancellationToken ct)
    {
        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<string> GetTextAsync(HttpClient client, string path, string expectedMediaType, CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/kernel/{path}", UriKind.Relative), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        response.Content.Headers.ContentType?.MediaType.Should().Be(expectedMediaType);
        return text;
    }

    private static async Task<(byte[] Body, string ShaHeader)> GetBytesWithShaHeaderAsync(
        HttpClient client,
        string path,
        string expectedMediaType,
        CancellationToken ct)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/kernel/{path}", UriKind.Relative), ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes));
        response.Content.Headers.ContentType?.MediaType.Should().Be(expectedMediaType);
        response.Headers.TryGetValues(KernelExportEndpoints.ContentSha256Header, out var values).Should().BeTrue();
        return (bytes, values!.Single());
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));
}
