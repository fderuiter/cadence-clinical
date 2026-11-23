using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
        await _scenario.Protocol.OkAsync(
            new RaiseQuery(
                tenant,
                AssertionId.New(),
                enrollmentA,
                sbpObservationA,
                "Confirm cuff size used at screening",
                KernelProtocolScenario.Key(),
                "Monitor query raised"),
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
        var (tenant, code, study, _, _, _, enrollmentA, enrollmentB, _, _) = await SeedStudyAsync(ct);
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

        foreach (var (format, mediaType) in new[] { ("csv", "text/csv"), ("odm_xml", "application/xml"), ("dataset_json", "application/json") })
        {
            var crcExport = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format={format}&lock_filter=all", mediaType, ct);
            crcExport.Should().Contain("SUBJ-A", $"Site A coordinator may see SUBJ-A in {format}");
            crcExport.Should().Contain("120.50", $"Site A coordinator may see SUBJ-A vitals in {format}");
            crcExport.Should().NotContain("SUBJ-B", $"Site B subject code must be masked in {format}");
            crcExport.Should().NotContain("138.25", $"Site B vitals value must be masked in {format}");
            crcExport.Should().NotContain("Active Drug 100mg", $"Blinded treatment arm must be masked in {format}");
            crcExport.Should().Contain("NotPermitted", $"Unauthorized site rows must carry NotPermitted mask in {format}");
            crcExport.Should().Contain("Blinded", $"Blinded treatment rows must carry Blinded mask in {format}");
        }

        // Site A coordinator requesting SUBJ-B's casebook receives an explicit masked NotPermitted casebook without leaking SUBJ-B.
        var (maskedCasebookBytes, _) = await GetBytesWithShaHeaderAsync(siteACrc, $"enrollments/{enrollmentBRef}/casebook?format=json", "application/json", ct);
        var maskedCasebookText = System.Text.Encoding.UTF8.GetString(maskedCasebookBytes);
        maskedCasebookText.Should().NotContain("SUBJ-B").And.NotContain("138.25");
        var maskedCasebook = JsonSerializer.Deserialize<KernelCasebookResponse>(maskedCasebookBytes, Json)!;
        maskedCasebook.Masked.Should().BeTrue();
        maskedCasebook.Mask.Should().Be("NotPermitted");
        maskedCasebook.SubjectCode.Should().Be("masked");

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
        foreach (var cap in new[] { "lock.apply", "lock.release", "collection.sign" })
        {
            await IssueAsync(tenant, DataManager, cap, AuthorityScope.Study(study), ct);
        }

        // - SiteACoordinator: observation.record, observation.correct ONLY at StudySite(siteA) (no tenant/study grant!)
        foreach (var cap in new[] { "observation.record", "observation.correct" })
        {
            await IssueAsync(tenant, SiteACoordinator, cap, AuthorityScope.StudySite(siteA), ct);
        }

        // - Monitor: observation.verify, assertion.retract at Study(study)
        foreach (var cap in new[] { "observation.verify", "assertion.retract" })
        {
            await IssueAsync(tenant, Monitor, cap, AuthorityScope.Study(study), ct);
        }

        // - BlindedStatistician: study-scoped read grant without enrollment.unblind
        await IssueAsync(tenant, BlindedStatistician, "observation.verify", AuthorityScope.Study(study), ct);

        // - UnblindedPharmacist: study-scoped read grant PLUS enrollment.unblind
        await IssueAsync(tenant, UnblindedPharmacist, "observation.verify", AuthorityScope.Study(study), ct);
        await IssueAsync(tenant, UnblindedPharmacist, "enrollment.unblind", AuthorityScope.Study(study), ct);

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
