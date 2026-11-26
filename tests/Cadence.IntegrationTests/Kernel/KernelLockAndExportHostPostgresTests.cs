using System;
using System.Globalization;
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
using Cadence.Ddf;
using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
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
                activeList.Locks.Should().ContainSingle(l => l.Ref == created.Ref && l.Released == false);

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
        fullHistory.Locks.Should().OnlyContain(l => l.Released == true && l.ReleaseReason != null);

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
        var (tenant, code, study, studyCode, _, _, enrollmentA, _, formA, sbpObservationA) = await SeedStudyAsync(ct, verifyAndSignA: false);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentRef = NodeRef.For(enrollmentA).ToString();
        var obsRef = NodeRef.For(sbpObservationA).ToString();

        ObservationId dbpObservationA;
        await using (var db = fixture.CreateContext(tenant))
        {
            var memberIds = await db.CollectionInstances.AsNoTracking()
                .Where(c => c.Id == formA)
                .SelectMany(c => c.Members.Select(m => m.ObservationId))
                .ToListAsync(ct);
            dbpObservationA = memberIds.Single(id => id != sbpObservationA);
        }

        using var dm = Client(code, DataManager);
        using var crc = Client(code, SiteACoordinator);
        using var monitor = Client(code, Monitor);

        // Verify revision 1 (120.50 mm[Hg] and 80.00 mm[Hg]) via HTTP and raise a query assertion + form signature in the kernel.
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
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            BuildRequest(HttpMethod.Post, $"observations/{NodeRef.For(dbpObservationA)}/verifications", "Verified diastolic against inpatient vital chart", null, KernelProtocolScenario.Key(), new
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

        // Relocking over unverified revision 2 fails with 409 Conflict (K.Assertion.ScopeNotReady).
        await Task.Delay(15, ct);
        using (var unverifiedRelock = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", "Attempted relock over unverified revision 2", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = studyRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            ct))
        {
            unverifiedRelock.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(unverifiedRelock, ct)).Should().Be(AssertionErrors.ScopeNotReady.Code);
        }

        // Verify revision 2 via HTTP; relocking STILL fails with 409 Conflict until the superseded signature on formA is re-signed.
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            BuildRequest(HttpMethod.Post, $"observations/{obsRef}/verifications", "Verified addendum reading 132.75 against source", null, KernelProtocolScenario.Key(), new
            {
                enrollment_ref = enrollmentRef,
                revision = 2,
            }),
            HttpStatusCode.Created,
            ct);

        using (var supersededSigRelock = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", "Attempted relock over superseded form signature", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = studyRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            ct))
        {
            supersededSigRelock.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(supersededSigRelock, ct)).Should().Be(AssertionErrors.ScopeNotReady.Code);
        }

        // Re-sign formA over its updated content hash, then apply Lock 2 cleanly.
        using (var formGet2 = await dm.GetAsync(new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}", UriKind.Relative), ct))
        {
            formGet2.StatusCode.Should().Be(HttpStatusCode.OK);
            using var formJson2 = JsonDocument.Parse(await formGet2.Content.ReadAsStringAsync(ct));
            var contentHash2 = formJson2.RootElement.GetProperty("content_hash").GetString()!;
            using var signReq2 = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}/signatures", UriKind.Relative));
            signReq2.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            signReq2.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("PI re-approved corrected vitals"));
            signReq2.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(DataManager, FormSignatureEndpoints.SignAction(formA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq2.Content = JsonContent.Create(new { meaning = "approved", content_hash = contentHash2 });
            using var signRes2 = await dm.SendAsync(signReq2, ct);
            signRes2.StatusCode.Should().Be(HttpStatusCode.Created, await signRes2.Content.ReadAsStringAsync(ct));
        }

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
        casebook.Verifications.Should().Contain(v => v.Revision == 1 && v.VerifiedBy == Monitor);
        casebook.Verifications.Should().ContainSingle(v => v.Revision == 2 && v.VerifiedBy == Monitor);
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
    /// Under <c>K-AUTH-4</c> and <c>ADR-2590</c>, a site coordinator granted only on <c>SITE-A</c> receives scope-filtered
    /// rows and locks with a collapsed <c>hidden</c> (<c>NotPermitted</c>) indicator and <c>403 Forbidden</c> on another
    /// site's casebook, while a blinded statistician without <c>enrollment.unblind</c> receives explicit <c>masked</c>
    /// (<c>Blinded</c>) placeholders for randomized treatment observations and locks across lock lists, CSV, ODM v1.3.2 XML,
    /// Dataset-JSON, and casebook exports, and an unblinded pharmacist sees the unmasked treatment arm.
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

        // Site A coordinator: out-of-scope SITE-B lock is excluded in-place with a collapsed hidden indicator (ADR-2590),
        // and the visible blinded observation lock hides kind, scope_kind, and released state.
        var crcLocks = await SendAsync<KernelStudyLocksResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcLocks.Hidden.MoreExist.Should().BeTrue();
        crcLocks.Hidden.Reasons.Should().Equal("NotPermitted");
        var blindedLock = crcLocks.Locks.Should().ContainSingle().Subject;
        blindedLock.Masked.Should().BeTrue();
        blindedLock.Mask.Should().Be("Blinded");
        blindedLock.ScopeRef.Should().BeNull();
        blindedLock.ScopeKind.Should().BeNull();
        blindedLock.Kind.Should().BeNull();
        blindedLock.Released.Should().BeNull();

        // Site A coordinator: exports omit per-record cross-site placeholders, collapse unauthorized Site B into a single
        // hidden-scope indicator (ADR-2590), and partition visibility BEFORE lock_filter.
        foreach (var (format, mediaType) in new[] { ("csv", "text/csv"), ("odm_xml", "application/xml"), ("dataset_json", "application/json") })
        {
            using var crcExportRes = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/studies/{studyRef}/exports?format={format}&lock_filter=all", UriKind.Relative), ct);
            var crcExport = await crcExportRes.Content.ReadAsStringAsync(ct);
            crcExportRes.StatusCode.Should().Be(HttpStatusCode.OK, crcExport);
            crcExportRes.Content.Headers.ContentType?.MediaType.Should().Be(mediaType);
            crcExportRes.Headers.GetValues(KernelExportEndpoints.HiddenMoreExistHeader).Single().Should().Be("true");
            crcExportRes.Headers.GetValues(KernelExportEndpoints.HiddenReasonsHeader).Single().Should().Be("NotPermitted");
            crcExportRes.Headers.GetValues(KernelExportEndpoints.HiddenScopesHeader).Single().Should().Be("more_exist=true;reasons=NotPermitted");
            crcExportRes.Headers.TryGetValues(KernelExportEndpoints.GeneratedAtHeader, out var genAtValues).Should().BeTrue();
            DateTimeOffset.TryParse(genAtValues!.Single(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _).Should().BeTrue();

            crcExport.Should().Contain("SUBJ-A", $"Site A coordinator may see SUBJ-A in {format}");
            crcExport.Should().Contain("120.50", $"Site A coordinator may see SUBJ-A vitals in {format}");
            crcExport.Should().NotContain("SUBJ-B", $"Site B subject code must be excluded in {format}");
            crcExport.Should().NotContain("138.25", $"Site B vitals value must be excluded in {format}");
            crcExport.Should().NotContain("Active Drug 100mg", $"Blinded treatment arm must be masked in {format}");
            crcExport.Should().Contain("Blinded", $"Blinded treatment rows must carry Blinded mask in {format}");

            if (format == "csv")
            {
                crcExport.Should().NotContain("NotPermitted", "CSV rows must be filtered to visible scope without per-row NotPermitted placeholders");
            }
            else if (format == "odm_xml")
            {
                crcExport.Should().NotContain("PlaceholderSequence");
                var crcOdm = XDocument.Parse(crcExport);
                XNamespace cadenceNs = "https://cadence.clinical/ns/kernel-export/v1";
                XNamespace odmNs = "http://www.cdisc.org/ns/odm/v1.3";
                crcOdm.Root!.Attribute(cadenceNs + "HiddenMoreExist")!.Value.Should().Be("true");
                crcOdm.Root!.Attribute(cadenceNs + "HiddenReasons")!.Value.Should().Be("NotPermitted");
                var hiddenOdm = crcOdm.Descendants(cadenceNs + "HiddenScopes").Single();
                hiddenOdm.Attribute("MoreExist")!.Value.Should().Be("true");
                hiddenOdm.Attribute("Reasons")!.Value.Should().Be("NotPermitted");
                crcOdm.Descendants(odmNs + "SubjectData").Should().ContainSingle();
                crcOdm.Descendants(odmNs + "ClinicalData").ToString().Should().NotContain("NotPermitted");
            }
            else if (format == "dataset_json")
            {
                using var crcDs = JsonDocument.Parse(crcExport);
                var hiddenElem = crcDs.RootElement.GetProperty("hidden");
                hiddenElem.GetProperty("more_exist").GetBoolean().Should().BeTrue();
                hiddenElem.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).Should().Equal("NotPermitted");
                var hiddenScopesElem = crcDs.RootElement.GetProperty("hiddenScopes");
                hiddenScopesElem.GetProperty("more_exist").GetBoolean().Should().BeTrue();
                hiddenScopesElem.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).Should().Equal("NotPermitted");
                crcDs.RootElement.GetProperty("itemGroupData").ToString().Should().NotContain("NotPermitted").And.NotContain("IG.masked");
            }
        }

        // Under lock_filter=locked_only, Site B's Locked enrollment must NOT appear as a cross-site row (Site A has zero Locked rows, only Frozen).
        var crcLockedOnlyCsv = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only", "text/csv", ct);
        crcLockedOnlyCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1, "header only: Site B's Locked enrollment was excluded before lock_filter");
        crcLockedOnlyCsv.Should().NotContain("SUBJ-B").And.NotContain("NotPermitted");

        var crcUnlockedOnlyCsv = await GetTextAsync(siteACrc, $"studies/{studyRef}/exports?format=csv&lock_filter=unlocked_only", "text/csv", ct);
        crcUnlockedOnlyCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "header + 2 unlocked Site A vitals (Frozen blinded arm and Site B excluded)");
        crcUnlockedOnlyCsv.Should().Contain("SUBJ-A").And.Contain("120.50").And.NotContain("SUBJ-B").And.NotContain("NotPermitted");

        var (crcArchiveBytes, _) = await GetBytesWithShaHeaderAsync(siteACrc, $"studies/{studyRef}/exports?format=study_archive&lock_filter=all", "application/zip", ct);
        using (var zip = new ZipArchive(new MemoryStream(crcArchiveBytes), ZipArchiveMode.Read))
        {
            var entryNames = zip.Entries.Select(e => e.FullName).ToList();
            entryNames.Should().NotContain(p => p.Contains("collection_masked_records", StringComparison.OrdinalIgnoreCase));
            entryNames.Should().NotContain(p => p.Contains("masked-", StringComparison.OrdinalIgnoreCase));
            await using var manifestStream = await zip.GetEntry("manifest.json")!.OpenAsync(ct);
            using var manifestReader = new StreamReader(manifestStream, Encoding.UTF8);
            using var manifestDoc = JsonDocument.Parse(await manifestReader.ReadToEndAsync(ct));
            manifestDoc.RootElement.GetProperty("hidden").GetProperty("more_exist").GetBoolean().Should().BeTrue();
            manifestDoc.RootElement.GetProperty("hidden_scopes").GetProperty("more_exist").GetBoolean().Should().BeTrue();
            await using var locksStream = await zip.GetEntry("locks/locks.json")!.OpenAsync(ct);
            using var locksReader = new StreamReader(locksStream, Encoding.UTF8);
            var locksJson = await locksReader.ReadToEndAsync(ct);
            locksJson.Should().NotContain("SUBJ-B");
            using var locksDoc = JsonDocument.Parse(locksJson);
            locksDoc.RootElement.GetProperty("hidden").GetProperty("more_exist").GetBoolean().Should().BeTrue();
            locksDoc.RootElement.GetProperty("locks").ToString().Should().NotContain("NotPermitted");
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

        // Site A coordinator requesting SUBJ-B's casebook is refused 403 Forbidden and writes a Denied AuditEntry.
        int deniedBefore;
        await using (var db = fixture.CreateAdminContext())
        {
            deniedBefore = await db.AuditEntries.CountAsync(a => a.TenantId == tenant && a.Action == "Denied" && a.ActorId == SiteACoordinator, ct);
        }

        using (var forbiddenCasebook = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/enrollments/{enrollmentBRef}/casebook?format=json", UriKind.Relative), ct))
        {
            var forbiddenBody = await forbiddenCasebook.Content.ReadAsStringAsync(ct);
            forbiddenCasebook.StatusCode.Should().Be(HttpStatusCode.Forbidden, forbiddenBody);
            forbiddenBody.Should().NotContain("SUBJ-B").And.NotContain("138.25");
            using var problem = JsonDocument.Parse(forbiddenBody);
            problem.RootElement.GetProperty("code").GetString().Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.AuditEntries.CountAsync(a => a.TenantId == tenant && a.Action == "Denied" && a.ActorId == SiteACoordinator, ct))
                .Should().Be(deniedBefore + 1, "denied cross-site casebook export must record a Denied audit entry");
        }

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
        pharmLocks.Hidden.MoreExist.Should().BeFalse();
        pharmLocks.Hidden.Reasons.Should().BeEmpty();

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
        var visibleCrcLock = crcList.Locks.Should().ContainSingle().Subject;
        visibleCrcLock.Masked.Should().BeFalse();
        visibleCrcLock.Ref.Should().Be(appliedObs.Ref);
        visibleCrcLock.StudySiteRef.Should().Be(siteARef);
        visibleCrcLock.EnrollmentRef.Should().Be(enrollmentARef);
        crcList.Hidden.MoreExist.Should().BeTrue();
        crcList.Hidden.Reasons.Should().Equal("NotPermitted");

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
        await _scenario.Protocol.OkAsync(Open(subjectObjA, blindedFormId, Tick(), visit: null), ct);
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
        var incompleteFormB = await _scenario.Protocol.OkAsync(Open(subjectB, CollectionInstanceId.New(), Tick(), visit: null), ct);
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

        // 1. Readiness endpoint for DataManager (study-scoped, blinded by default) reports 2 open queries + 1 missing required item
        // + 2 unverified values (1 on incompleteFormB + 1 blinded on blindedObsId) + 1 unsigned form (incompleteFormB),
        // with the blinded treatment blockers masked as Blinded!
        var dmReadiness = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        dmReadiness.ReadyForLock.Should().BeFalse();
        dmReadiness.OpenQueryCount.Should().Be(2);
        dmReadiness.MissingRequiredItemCount.Should().Be(1);
        dmReadiness.UnverifiedValueCount.Should().Be(2);
        dmReadiness.UnsignedFormCount.Should().Be(1);
        dmReadiness.Blockers.Should().ContainSingle(b => !b.Masked && b.Kind == "OpenQuery" && b.TargetRef == NodeRef.For(sbpObservationA).ToString() && b.Owner != null);
        dmReadiness.Blockers.Where(b => b.Masked && b.Mask == "Blinded" && b.TargetRef == null).Should().HaveCount(2);
        dmReadiness.Blockers.Should().ContainSingle(b => !b.Masked && b.Kind == "MissingRequiredItem" && b.TargetRef == NodeRef.For(incompleteFormB.CollectionInstanceId).ToString());
        dmReadiness.Blockers.Should().ContainSingle(b => !b.Masked && b.Kind == "UnsignedForm" && b.TargetRef == NodeRef.For(incompleteFormB.CollectionInstanceId).ToString());

        // Site A coordinator sees Site B blockers masked as NotPermitted and blinded blockers masked as Blinded.
        var crcReadiness = await SendAsync<KernelLockReadinessResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcReadiness.Blockers.Where(b => b.Masked && b.Mask == "NotPermitted" && b.TargetRef == null).Should().HaveCount(3);
        crcReadiness.Blockers.Where(b => b.Masked && b.Mask == "Blinded" && b.TargetRef == null).Should().HaveCount(2);

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
        await _scenario.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollmentA, blindedObsId, 1, KernelProtocolScenario.Key(), "Unblinded pharmacy SDV while frozen"),
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

        using (var forbiddenPdfRes = await siteACrc.GetAsync(new Uri($"/api/v1/kernel/enrollments/{enrollmentBRef}/casebook?format=pdf", UriKind.Relative), ct))
        {
            var forbiddenPdfBody = await forbiddenPdfRes.Content.ReadAsStringAsync(ct);
            forbiddenPdfRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, forbiddenPdfBody);
            forbiddenPdfBody.Should().NotContain("SUBJ-B").And.NotContain("138.25");
            (await ProblemCodeAsync(forbiddenPdfRes, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        var (pharmPdfBytes, _) = await GetBytesWithShaHeaderAsync(unblindedPharm, $"enrollments/{enrollmentARef}/casebook?format=pdf", "application/pdf", ct);
        Encoding.ASCII.GetString(pharmPdfBytes).Should().Contain("Investigational Arm X");

        // 2. SAS bundle, R bundle, and Study Archive ZIP streams + manifest SHA-256 verification + K-AUTH-4 in-place scope filtering
        foreach (var format in new[] { "sas_bundle", "r_bundle", "study_archive" })
        {
            using var zipRes = await siteACrc.GetAsync(
                new Uri($"/api/v1/kernel/studies/{studyRef}/exports?format={format}&lock_filter=all", UriKind.Relative),
                ct);
            var zipBytes = await zipRes.Content.ReadAsByteArrayAsync(ct);
            zipRes.StatusCode.Should().Be(HttpStatusCode.OK);
            zipRes.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
            zipRes.Headers.GetValues(KernelExportEndpoints.HiddenMoreExistHeader).Single().Should().Be("true");
            zipRes.Headers.GetValues(KernelExportEndpoints.HiddenReasonsHeader).Single().Should().Be("NotPermitted");
            var zipSha = zipRes.Headers.GetValues(KernelExportEndpoints.ContentSha256Header).Single();
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
            manifestDoc.RootElement.GetProperty("hidden").GetProperty("more_exist").GetBoolean().Should().BeTrue();
            manifestDoc.RootElement.GetProperty("hidden").GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).Should().Equal("NotPermitted");

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
                var casebookPdfs = entryNames.Where(n => n.StartsWith("casebooks/", StringComparison.Ordinal) && n.EndsWith(".pdf", StringComparison.Ordinal)).ToList();
                casebookPdfs.Should().ContainSingle().Which.Should().Contain("SUBJ-A");
                entryNames.Should().Contain("locks/locks.json");
                entryNames.Should().Contain("audit/audit_trail.json");

                await using var auditStream = await archive.GetEntry("audit/audit_trail.json")!.OpenAsync(ct);
                using var auditDoc = await JsonDocument.ParseAsync(auditStream, cancellationToken: ct);
                auditDoc.RootElement.GetProperty("hidden").GetProperty("more_exist").GetBoolean().Should().BeTrue();
                auditDoc.RootElement.GetProperty("entries").ToString().Should().NotContain("NotPermitted");
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
        ObservationId SbpObservationA)> SeedStudyAsync(CancellationToken ct, bool verifyAndSignA = true, bool verifyAndSignB = true)
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

        if (verifyAndSignA)
        {
            await _scenario.VerifyAndSignAsync(new Form(subjectObjA, version, openedA.CollectionInstanceId), ct);
        }

        if (verifyAndSignB)
        {
            await _scenario.VerifyAndSignAsync(new Form(subjectObjB, version, openedB.CollectionInstanceId), ct);
        }

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

    private HttpClient Client(string tenantCode, string userId, bool tenantReadOnly = false) =>
        ClientWithRoles(tenantCode, userId, [], tenantReadOnly);

    private HttpClient ClientWithRoles(string tenantCode, string userId, System.Collections.Generic.IReadOnlyList<string> roles, bool tenantReadOnly = false)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseTestBiometricLivenessKey()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, roles, [], TenantId: tenantCode, TenantReadOnly: tenantReadOnly));
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

    /// <summary>
    /// Evaluates post-visit locks and releases recorded up to <c>known_at</c> when <c>valid_at &lt; known_at</c> without
    /// leaking locks on scopes recorded after <c>valid_at</c>, anchors payload timestamps to <c>known_at</c> so identical
    /// <c>(valid_at, known_at)</c> cuts produce byte-for-byte identical files and <c>X-Content-Sha256</c> digests across
    /// all 6 study formats and all 4 casebook formats, and exposes wall-clock generation time in <c>X-Generated-At</c>.
    /// </summary>
    [Fact(DisplayName = "BitemporalCuts_ReflectPostVisitLocksAndReleasesAtKnownAt_AndProduceByteIdenticalArtifactsAcrossAllFormats")]
    [KernelRule("K-TRUTH-1")]
    public async Task BitemporalCutsReflectPostVisitLocksAndReleasesAtKnownAtAndProduceByteIdenticalArtifactsAcrossAllFormats()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, _, _, _, enrollmentA, _, _, _) = await SeedStudyAsync(ct);
        var studyRef = NodeRef.For(study).ToString();
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();

        using var dm = Client(code, DataManager);

        // Anchor valid_at strictly after initial screening/vitals on SUBJ-A, before post-visit lock/release/observations.
        await Task.Delay(20, ct);
        var tValidCut = Tick();
        await Task.Delay(20, ct);

        // Apply post-visit Locked assertion on enrollmentA strictly AFTER tValidCut.
        Tick();
        var postVisitLock = await SendAsync<KernelLockResponse>(
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
        var tKnownAfterLock = postVisitLock.LockedAt!.Value;

        // Advance clock, release the enrollment lock, record a post-valid_at observation, and freeze that post-valid_at observation.
        await Task.Delay(20, ct);
        Tick();
        var releasedPostVisitLock = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, $"locks/{postVisitLock.Ref}/release", ReleaseReason, ReleaseToken(DataManager, postVisitLock.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);

        var postValidConcept = await DefineConceptWithCodeAsync(tenant, "post_visit_temp", "Post-visit temperature", ct);
        var postValidObsId = ObservationId.New();
        var tObservedAfterValid = Tick();
        await _scenario.Protocol.OkAsync(
            new RecordObservation(
                tenant,
                postValidObsId,
                enrollmentA,
                postValidConcept,
                new QuantityValue(37.10m, "Cel"),
                tObservedAfterValid,
                KernelProtocolScenario.Key(),
                "Recorded at later unscheduled check"),
            ct);

        Tick();
        var futureObsLock = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze post-valid observation", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = NodeRef.For(postValidObsId).ToString(),
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        var tKnownAfterReleaseAndFutureObs = futureObsLock.LockedAt!.Value;

        var validParam = Uri.EscapeDataString(tValidCut.ToString("O"));
        var knownLockedParam = Uri.EscapeDataString(tKnownAfterLock.ToString("O"));
        var knownReleasedParam = Uri.EscapeDataString(tKnownAfterReleaseAndFutureObs.ToString("O"));

        // 1. At (valid_at = tValidCut, known_at = tKnownAfterLock), SUBJ-A's visit observation IS locked by postVisitLock!
        var csvLockedCut = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&valid_at={validParam}&known_at={knownLockedParam}",
            "text/csv",
            ct);
        csvLockedCut.Should().Contain("SUBJ-A").And.Contain("120.50");

        // 2. At (valid_at = tValidCut, known_at = tKnownAfterReleaseAndFutureObs), the enrollment lock was released by known_at
        //    and the Frozen lock on postValidObsId targets an observation that did not exist at valid_at = tValidCut!
        var csvReleasedCut = await GetTextAsync(
            dm,
            $"studies/{studyRef}/exports?format=csv&lock_filter=locked_only&valid_at={validParam}&known_at={knownReleasedParam}",
            "text/csv",
            ct);
        csvReleasedCut.Should().NotContain("SUBJ-A").And.NotContain("37.10");

        var (cbReleasedBytes, _) = await GetBytesWithShaHeaderAsync(
            dm,
            $"enrollments/{enrollmentARef}/casebook?format=json&valid_at={validParam}&known_at={knownReleasedParam}",
            "application/json",
            ct);
        var cbReleased = JsonSerializer.Deserialize<KernelCasebookResponse>(cbReleasedBytes, Json)!;
        cbReleased.Observations.Should().NotContain(o => o.NumericValue == 37.10m);
        cbReleased.Locks.Should().ContainSingle().Which.Should().Match<KernelLockResponse>(
            l => l.Ref == postVisitLock.Ref && l.Released == true && l.ReleasedAt == releasedPostVisitLock.ReleasedAt);

        // 3. Byte-for-byte deterministic cuts across all 6 study formats and all 4 casebook formats when wall-clock advances,
        //    while X-Generated-At reflects wall-clock generation time outside the hashed payload.
        var studyFormats = new (string Format, string MediaType)[]
        {
            ("csv", "text/csv"),
            ("odm_xml", "application/xml"),
            ("dataset_json", "application/json"),
            ("sas_bundle", "application/zip"),
            ("r_bundle", "application/zip"),
            ("study_archive", "application/zip"),
        };
        var casebookFormats = new (string Format, string MediaType)[]
        {
            ("json", "application/json"),
            ("odm_xml", "application/xml"),
            ("html", "text/html"),
            ("pdf", "application/pdf"),
        };

        foreach (var (format, mediaType) in studyFormats)
        {
            var url = $"/api/v1/kernel/studies/{studyRef}/exports?format={format}&lock_filter=all&valid_at={validParam}&known_at={knownLockedParam}";
            using var res1 = await dm.GetAsync(new Uri(url, UriKind.Relative), ct);
            var bytes1 = await res1.Content.ReadAsByteArrayAsync(ct);
            res1.StatusCode.Should().Be(HttpStatusCode.OK);
            res1.Content.Headers.ContentType?.MediaType.Should().Be(mediaType);
            var sha1 = res1.Headers.GetValues(KernelExportEndpoints.ContentSha256Header).Single();
            var gen1 = DateTimeOffset.Parse(res1.Headers.GetValues(KernelExportEndpoints.GeneratedAtHeader).Single(), CultureInfo.InvariantCulture);

            fixture.Clock.Advance(TimeSpan.FromMinutes(5));

            using var res2 = await dm.GetAsync(new Uri(url, UriKind.Relative), ct);
            var bytes2 = await res2.Content.ReadAsByteArrayAsync(ct);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
            var sha2 = res2.Headers.GetValues(KernelExportEndpoints.ContentSha256Header).Single();
            var gen2 = DateTimeOffset.Parse(res2.Headers.GetValues(KernelExportEndpoints.GeneratedAtHeader).Single(), CultureInfo.InvariantCulture);

            sha2.Should().Be(sha1, $"identical (valid_at, known_at) study export ({format}) must produce the exact same SHA-256 digest");
            bytes2.Should().Equal(bytes1, $"identical (valid_at, known_at) study export ({format}) must be byte-for-byte identical");
            gen2.Should().BeAfter(gen1, $"X-Generated-At header on study export ({format}) must reflect wall-clock generation time");
        }

        foreach (var (format, mediaType) in casebookFormats)
        {
            var url = $"/api/v1/kernel/enrollments/{enrollmentARef}/casebook?format={format}&valid_at={validParam}&known_at={knownLockedParam}";
            using var res1 = await dm.GetAsync(new Uri(url, UriKind.Relative), ct);
            var bytes1 = await res1.Content.ReadAsByteArrayAsync(ct);
            res1.StatusCode.Should().Be(HttpStatusCode.OK);
            res1.Content.Headers.ContentType?.MediaType.Should().Be(mediaType);
            var sha1 = res1.Headers.GetValues(KernelExportEndpoints.ContentSha256Header).Single();
            var gen1 = DateTimeOffset.Parse(res1.Headers.GetValues(KernelExportEndpoints.GeneratedAtHeader).Single(), CultureInfo.InvariantCulture);

            fixture.Clock.Advance(TimeSpan.FromMinutes(5));

            using var res2 = await dm.GetAsync(new Uri(url, UriKind.Relative), ct);
            var bytes2 = await res2.Content.ReadAsByteArrayAsync(ct);
            res2.StatusCode.Should().Be(HttpStatusCode.OK);
            var sha2 = res2.Headers.GetValues(KernelExportEndpoints.ContentSha256Header).Single();
            var gen2 = DateTimeOffset.Parse(res2.Headers.GetValues(KernelExportEndpoints.GeneratedAtHeader).Single(), CultureInfo.InvariantCulture);

            sha2.Should().Be(sha1, $"identical (valid_at, known_at) casebook export ({format}) must produce the exact same SHA-256 digest");
            bytes2.Should().Equal(bytes1, $"identical (valid_at, known_at) casebook export ({format}) must be byte-for-byte identical");
            gen2.Should().BeAfter(gen1, $"X-Generated-At header on casebook export ({format}) must reflect wall-clock generation time");
        }
    }

    /// <summary>
    /// Casebook CDISC ODM v1.3.2 XML exports carry <c>&lt;Study&gt;</c> (<c>&lt;GlobalVariables&gt;</c> and
    /// <c>&lt;MetaDataVersion&gt;</c>), <c>&lt;AdminData&gt;&lt;SignatureDef&gt;</c>, and the
    /// <c>StudyEventData -&gt; FormData -&gt; ItemGroupData -&gt; ItemData</c> hierarchy, resolve every
    /// <c>SignatureOID</c> reference, and import cleanly through <see cref="OdmDesignImporter.ImportAsync(string, ProtocolConceptResolver, System.Collections.Generic.ISet{string}?, CancellationToken, ProtocolTerminologyResolver?)"/>.
    /// </summary>
    [Fact(DisplayName = "CasebookOdmXml_CarriesStudyProtocolSignatureDefsAndVisitHierarchy_AndRoundTripsThroughOdmDesignImporter")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CasebookOdmXmlCarriesStudyProtocolSignatureDefsAndVisitHierarchyAndRoundTripsThroughOdmDesignImporter()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (_, code, _, studyCode, _, _, enrollmentA, _, formA, _) = await SeedStudyAsync(ct);
        var enrollmentARef = NodeRef.For(enrollmentA).ToString();

        using var dm = Client(code, DataManager);

        // Sign formA so the exported casebook ODM XML includes electronic Signature and SignatureDef elements.
        using (var formGet = await dm.GetAsync(new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}", UriKind.Relative), ct))
        {
            formGet.StatusCode.Should().Be(HttpStatusCode.OK);
            using var formJson = JsonDocument.Parse(await formGet.Content.ReadAsStringAsync(ct));
            var contentHash = formJson.RootElement.GetProperty("content_hash").GetString()!;
            using var signReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{formA.Value:D}/signatures", UriKind.Relative));
            signReq.Headers.Add("Idempotency-Key", KernelProtocolScenario.Key());
            signReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("PI approved vitals for ODM round-trip"));
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(DataManager, FormSignatureEndpoints.SignAction(formA.Value), FormSignatureEndpoints.SignSemanticAction));
            signReq.Content = JsonContent.Create(new { meaning = "approved", content_hash = contentHash });
            using var signRes = await dm.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        var (odmBytes, odmSha) = await GetBytesWithShaHeaderAsync(
            dm,
            $"enrollments/{enrollmentARef}/casebook?format=odm_xml",
            "application/xml",
            ct);
        Convert.ToHexStringLower(SHA256.HashData(odmBytes)).Should().Be(odmSha);

        var odmXml = Encoding.UTF8.GetString(odmBytes);
        var doc = XDocument.Parse(odmXml);
        XNamespace odmNs = "http://www.cdisc.org/ns/odm/v1.3";

        var studyEl = doc.Root!.Element(odmNs + "Study");
        studyEl.Should().NotBeNull();
        studyEl!.Attribute("OID")!.Value.Should().Be(studyCode);
        studyEl.Element(odmNs + "GlobalVariables").Should().NotBeNull();
        var mdv = studyEl.Elements(odmNs + "MetaDataVersion").Should().ContainSingle().Subject;
        mdv.Element(odmNs + "Protocol").Should().NotBeNull();
        mdv.Elements(odmNs + "StudyEventDef").Should().NotBeEmpty();
        mdv.Elements(odmNs + "FormDef").Select(f => f.Attribute("OID")!.Value).Should().Contain($"FORM.{SpecId}");
        mdv.Elements(odmNs + "ItemGroupDef").Should().NotBeEmpty();
        mdv.Elements(odmNs + "ItemDef").Select(i => i.Attribute("OID")!.Value).Should().Contain([$"IT.{Systolic}", $"IT.{Diastolic}"]);

        var adminData = doc.Root.Element(odmNs + "AdminData");
        adminData.Should().NotBeNull();
        var signatureDefOids = adminData!.Elements(odmNs + "SignatureDef").Select(sd => sd.Attribute("OID")!.Value).ToHashSet(StringComparer.Ordinal);
        signatureDefOids.Should().NotBeEmpty();

        var clinicalData = doc.Root.Element(odmNs + "ClinicalData");
        clinicalData.Should().NotBeNull();
        clinicalData!.Attribute("MetaDataVersionOID")!.Value.Should().Be(mdv.Attribute("OID")!.Value);

        var subjectData = clinicalData.Elements(odmNs + "SubjectData").Should().ContainSingle().Subject;
        var studyEventData = subjectData.Elements(odmNs + "StudyEventData").Should().ContainSingle().Subject;
        studyEventData.Attribute("StudyEventOID")!.Value.Should().Be($"SE.{VisitId}");
        var formData = studyEventData.Elements(odmNs + "FormData").Should().ContainSingle().Subject;
        formData.Attribute("FormOID")!.Value.Should().Be($"FORM.{SpecId}");
        var itemGroupData = formData.Elements(odmNs + "ItemGroupData").Should().ContainSingle().Subject;
        itemGroupData.Attribute("ItemGroupOID")!.Value.Should().Be($"IG.{SpecId}");
        itemGroupData.Elements(odmNs + "ItemData").Select(i => i.Attribute("ItemOID")!.Value)
            .Should().Contain([$"IT.{Systolic}", $"IT.{Diastolic}"]);

        var referencedSigOids = clinicalData.Descendants(odmNs + "SignatureRef")
            .Select(sr => sr.Attribute("SignatureOID")!.Value)
            .ToList();
        referencedSigOids.Should().NotBeEmpty();
        referencedSigOids.Should().OnlyContain(oid => signatureDefOids.Contains(oid));

        var importResult = await OdmDesignImporter.ImportAsync(
            odmXml,
            (_, _) => ValueTask.FromResult(new ProtocolConceptResolution(new ConceptRef(ConceptId.New(), 1), UsedFallbackSponsorConcept: false)),
            cancellationToken: ct);
        importResult.IsSuccess.Should().BeTrue(string.Join("; ", importResult.Problems));
        importResult.Problems.Should().BeEmpty();
        importResult.StudyOid.Should().Be(studyCode);
        var importedSpec = importResult.CollectionSpecs.Should().ContainSingle(s => s.Id == $"FORM.{SpecId}").Subject;
        importedSpec.Items.Select(i => i.Id).Should().Contain([$"IT.{Systolic}", $"IT.{Diastolic}"]);
    }

    /// <summary>
    /// Validates the scheduled-visit and validation-finding readiness gates: active enrollments with due or closed
    /// scheduled visits lacking opened forms block <see cref="LockKind.Locked"/> (<c>UnopenedVisit</c>) while
    /// screen-failed enrollments and withdrawn enrollments whose scheduled visits fall after participation ended do not
    /// trigger blockers; open and investigating <see cref="ValidationSeverity.Error"/> findings block
    /// <see cref="LockKind.Locked"/> (<c>ErrorFinding</c>) while warnings and explained/accepted findings do not.
    /// </summary>
    [Fact(DisplayName = "HostLockReadiness_GatesUnopenedScheduledVisitsAndErrorFindings_WhileSkippingPostTerminationVisits")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostLockReadinessGatesUnopenedScheduledVisitsAndErrorFindingsWhileSkippingPostTerminationVisits()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenantCode = $"T-SCH-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, tenantCode, "Scheduled visit and finding gate tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var sbp = await DefineConceptWithCodeAsync(tenant, "sbp_sched", "Systolic BP", ct);
        var design = new ProtocolDesign
        {
            Title = "SCHED-GATE-1",
            Epochs = [new Epoch("ep.treat", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity("act.day1", "Day 1 Baseline", "ep.treat", [ActivityLocation.Site], [SpecId], new ActivityTiming(1, 1, 1)),
                new PlannedActivity("act.day28", "Day 28 Follow-up", "ep.treat", [ActivityLocation.Site], [SpecId], new ActivityTiming(28, 2, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(SpecId, "Vital signs", [new CollectionItem(Systolic, sbp, "mm[Hg]", Required: true)]),
            ],
        };

        var version = ProtocolVersionId.New();
        var baseTime = Tick();
        var originAt = baseTime.AddDays(-32);
        await _scenario.Protocol.OkAsync(new CreateProtocolDraft(tenant, study, version, ProtocolDesignDocument.Write(design), KernelProtocolScenario.Key(), "Draft timed SOA"), ct);
        Tick();
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Review timed SOA"), ct);
        var protoSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(AttestationTarget.ProtocolVersionKind, version.Value, inReview.ContentHash!)),
            ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, protoSig, KernelProtocolScenario.Key(), "Approved"), ct);

        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVer = ArtifactVersionId.New();
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethicsVer, ArtifactKind.EthicsApproval, study, "Ethics approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://ethics/1"), KernelProtocolScenario.Key(), "Ethics"),
            ct);
        var effId = EffectivityId.New();
        await _scenario.Protocol.OkAsync(new DeclareEffectivity(tenant, effId, version, EffectivityScope.Study(), originAt, KernelProtocolScenario.Key(), "Declare"), ct);
        Tick();
        await _scenario.Protocol.OkAsync(new ChangeEffectivityStatus(tenant, effId, EffectivityStatus.Effective, originAt.AddMinutes(1), [ethicsVer], KernelProtocolScenario.Key(), "Effective"), ct);

        StudySiteId site;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var rawSite = new Site(SiteId.New(), tenant, sponsor, $"SITE-SCH-{Guid.NewGuid():N}"[..12], "Schedule Clinic", "US", KernelPostgresFixture.ActorId, "Seeded");
            var link = new StudySite(StudySiteId.New(), tenant, study, rawSite.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            await fixture.SeedAsync(rawSite, link);
            site = link.Id;
        }

        // 1. Active enrolled participant (enrolled 30 days ago): Day 1 opened/verified/signed; Day 28 unopened & overdue!
        var partActive = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Active"), ct);
        var screenedActive = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), partActive, site, SubjectCode.From("SUBJ-ACT"), baseTime.AddDays(-31), KernelProtocolScenario.Key(), "Screened active"), ct);
        Tick();
        var enrolledActive = await _scenario.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, screenedActive.EnrollmentId, EnrollmentStatus.Enrolled, baseTime.AddDays(-30), KernelProtocolScenario.Key(), "Enrolled active"), ct);
        var subjActive = new KernelObservationScenario.Subject(tenant, study, site, enrolledActive.EnrollmentId);
        var day1Active = await _scenario.Protocol.OkAsync(
            new OpenCollectionInstance(tenant, CollectionInstanceId.New(), enrolledActive.EnrollmentId, SpecId, "act.day1", baseTime.AddDays(-30).AddHours(1), KernelProtocolScenario.Key(), "Day 1 active"), ct);
        await _scenario.Protocol.OkAsync(Save(new Form(subjActive, version, day1Active.CollectionInstanceId), Value(Systolic, Mmhg(122))), ct);
        await _scenario.VerifyAndSignAsync(new Form(subjActive, version, day1Active.CollectionInstanceId), ct);

        // 2. Screen-failed participant (screen-failed 29 days ago): unopened visits must NOT block!
        var partFailed = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "ScreenFailed"), ct);
        var screenedFailed = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), partFailed, site, SubjectCode.From("SUBJ-SF"), baseTime.AddDays(-30), KernelProtocolScenario.Key(), "Screened SF"), ct);
        Tick();
        await _scenario.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, screenedFailed.EnrollmentId, EnrollmentStatus.ScreenFailed, baseTime.AddDays(-29), KernelProtocolScenario.Key(), "Failed screening"), ct);

        // 3. Withdrawn participant (enrolled 30 days ago, Day 1 completed/verified/signed, withdrawn 10 days after enrollment — before Day 28 target!):
        var partWithdrawn = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Withdrawn"), ct);
        var screenedWithdrawn = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), partWithdrawn, site, SubjectCode.From("SUBJ-WD"), baseTime.AddDays(-31), KernelProtocolScenario.Key(), "Screened WD"), ct);
        Tick();
        var enrolledWithdrawn = await _scenario.Protocol.OkAsync(
            new ChangeEnrollmentStatus(tenant, screenedWithdrawn.EnrollmentId, EnrollmentStatus.Enrolled, baseTime.AddDays(-30), KernelProtocolScenario.Key(), "Enrolled WD"), ct);
        var subjWithdrawn = new KernelObservationScenario.Subject(tenant, study, site, enrolledWithdrawn.EnrollmentId);
        var day1Withdrawn = await _scenario.Protocol.OkAsync(
            new OpenCollectionInstance(tenant, CollectionInstanceId.New(), enrolledWithdrawn.EnrollmentId, SpecId, "act.day1", baseTime.AddDays(-30).AddHours(1), KernelProtocolScenario.Key(), "Day 1 WD"), ct);
        await _scenario.Protocol.OkAsync(Save(new Form(subjWithdrawn, version, day1Withdrawn.CollectionInstanceId), Value(Systolic, Mmhg(119))), ct);
        await _scenario.VerifyAndSignAsync(new Form(subjWithdrawn, version, day1Withdrawn.CollectionInstanceId), ct);
        var wdSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Withdrawn, new AttestationTarget(NodeKinds.Enrollment, enrolledWithdrawn.EnrollmentId.Value, enrolledWithdrawn.ContentHash)), ct);
        await _scenario.Protocol.OkAsync(
            new WithdrawParticipant(tenant, enrolledWithdrawn.EnrollmentId, WithdrawalKind.WithdrawalBySubject, wdSig, baseTime.AddDays(-20), KernelProtocolScenario.Key(), "Withdrew before Day 28"), ct);

        // 4. Seed a released RulePack and record one Error finding + one Warning finding on the study.
        var draftedPack = await _scenario.Protocol.OkAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.RulePack, "sched-pack", "Schedule Pack", RulePackJson.ToJson(SampleRulePacks.Sample()), [], KernelProtocolScenario.Key(), "Draft pack"), ct);
        var packSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, draftedPack.Id.Value, draftedPack.ContentHash)), ct);
        await _scenario.Protocol.OkAsync(new ApproveLibraryItem(tenant, draftedPack.Id, packSig, KernelProtocolScenario.Key(), "Approved pack"), ct);

        var valRunId = ValidationRunId.New();
        var runAt = Tick();
        await _scenario.Protocol.OkAsync(
            new RecordValidationRun(
                tenant,
                valRunId,
                study,
                ValidationStage.Collection,
                [draftedPack.Id],
                DateOnly.FromDateTime(runAt.UtcDateTime),
                null,
                null,
                null,
                [],
                ["VITALS"],
                runAt.AddMinutes(-2),
                runAt.AddMinutes(-1),
                [
                    new ValidationDetection("sched-pack", "R1", 1, ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", day1Active.CollectionInstanceId.Value.ToString("D"), Systolic),
                        "Systolic out of range", null, [new ObservedVariable("SYS", "122")]),
                    new ValidationDetection("sched-pack", "R2", 1, ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", day1Active.CollectionInstanceId.Value.ToString("D"), Systolic),
                        "Advisory check", null, [new ObservedVariable("SYS", "122")]),
                ],
                KernelProtocolScenario.Key(),
                "Record findings"),
            ct);

        await IssueAsync(tenant, DataManager, "attestation.sign", AuthorityScope.Tenant(), ct);
        foreach (var cap in new[] { "lock.apply", "lock.release", "collection.sign" })
        {
            await IssueAsync(tenant, DataManager, cap, AuthorityScope.Study(study), ct);
        }

        using var dm = Client(tenantCode, DataManager);
        var studyRef = NodeRef.For(study).ToString();

        var readiness = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        readiness.ReadyForLock.Should().BeFalse();
        readiness.CanFreeze.Should().BeTrue();
        readiness.UnopenedVisitCount.Should().Be(1, "only SUBJ-ACT's overdue Day 28 visit blocks; ScreenFailed and post-withdrawal visits are excluded");
        readiness.OpenErrorFindingCount.Should().Be(1, "only the Error finding blocks; the Warning finding does not");
        readiness.Blockers.Should().ContainSingle(b =>
            b.Kind == "UnopenedVisit"
            && b.SubjectCode == "SUBJ-ACT"
            && b.PlannedActivityId == "act.day28"
            && b.Owner == "Site coordinator / investigator"
            && b.NextCapability == "collection.open");
        readiness.Blockers.Should().ContainSingle(b =>
            b.Kind == "ErrorFinding"
            && b.RuleId == "sched-pack/R1"
            && b.Owner == LockReadinessEvaluator.MonitorOwner
            && b.NextCapability == "validation_finding.investigate");

        using (var blockedRes = await dm.SendAsync(
            BuildRequest(HttpMethod.Post, "locks", LockReason, ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = studyRef,
                kind = "Locked",
                meaning = "Approved",
            }),
            ct))
        {
            blockedRes.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var body = await blockedRes.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            doc.RootElement.GetProperty("code").GetString().Should().Be(AssertionErrors.ScopeNotReady.Code);
            var conditionCodes = doc.RootElement.GetProperty("explanation").GetProperty("conditions")
                .EnumerateArray()
                .Where(c => !c.GetProperty("met").GetBoolean())
                .Select(c => c.GetProperty("code").GetString())
                .ToList();
            conditionCodes.Should().Contain([
                LockReadinessEvaluator.ScheduledVisitsOpenedCondition,
                LockReadinessEvaluator.ErrorFindingsResolvedCondition,
            ]);
        }

        // Move the Error finding to Investigating -> still blocks Locked!
        var errorFinding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Severity = ValidationSeverity.Error }, ct)).Findings.Single();
        await _scenario.Protocol.OkAsync(new InvestigateFinding(tenant, errorFinding.Id, KernelProtocolScenario.Key(), "Investigating R1"), ct);

        var investigatingReadiness = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        investigatingReadiness.OpenErrorFindingCount.Should().Be(1, "Investigating error findings still block hard locks");

        // Explain the Error finding with a signed attestation -> OpenErrorFindingCount drops to 0, while UnopenedVisitCount == 1 still blocks!
        const string FindingExplanation = "Confirmed clinical exception with investigator.";
        var explainSig = await _scenario.Protocol.OkAsync(
            _scenario.Protocol.AttestCommand(
                tenant,
                AttestationMeaning.Approved,
                new AttestationTarget(NodeKinds.ValidationFinding, errorFinding.Id.Value, FindingDecisionContent.Hash(errorFinding.Id, valRunId, FindingStatus.Explained, FindingExplanation))),
            ct);
        await _scenario.Protocol.OkAsync(new ExplainFinding(tenant, errorFinding.Id, FindingExplanation, explainSig, KernelProtocolScenario.Key(), "Explained R1"), ct);

        var afterExplain = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        afterExplain.OpenErrorFindingCount.Should().Be(0);
        afterExplain.UnopenedVisitCount.Should().Be(1);
        afterExplain.ReadyForLock.Should().BeFalse();

        // Open, complete, verify, and sign SUBJ-ACT's Day 28 visit -> ReadyForLock becomes true and hard lock succeeds!
        var day28Active = await _scenario.Protocol.OkAsync(
            new OpenCollectionInstance(tenant, CollectionInstanceId.New(), enrolledActive.EnrollmentId, SpecId, "act.day28", Tick().AddMinutes(-1), KernelProtocolScenario.Key(), "Day 28 performed"), ct);
        await _scenario.Protocol.OkAsync(Save(new Form(subjActive, version, day28Active.CollectionInstanceId), Value(Systolic, Mmhg(118))), ct);
        await _scenario.VerifyAndSignAsync(new Form(subjActive, version, day28Active.CollectionInstanceId), ct);

        var ready = await SendAsync<KernelLockReadinessResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"studies/{studyRef}/locks/readiness?scope_ref={Uri.EscapeDataString(studyRef)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        ready.ReadyForLock.Should().BeTrue();
        ready.UnopenedVisitCount.Should().Be(0);
        ready.OpenErrorFindingCount.Should().Be(0);

        var applied = await SendAsync<KernelLockResponse>(
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
        applied.Kind.Should().Be("Locked");
    }

    /// <summary>
    /// <c>GET /api/v1/kernel/locks/in-force</c> reports covering <c>Frozen</c> and <c>Locked</c> write locks across
    /// study, site, and subject scopes, and <c>POST /api/v1/kernel/enrollments/screen</c> rejects screening under a
    /// covering lock with <c>423 K.Observation.Locked</c> before registering any orphan <see cref="Participant"/> row.
    /// </summary>
    [Fact(DisplayName = "HostLocksInForceAndScreenEndpoint_EnforceCoveringLocksWithoutOrphanParticipants")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostLocksInForceAndScreenEndpointEnforceCoveringLocksWithoutOrphanParticipants()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, studyCode, siteA, _, _, _, _, _) = await SeedStudyAsync(ct);
        await IssueAsync(tenant, SiteACoordinator, "participant.register", AuthorityScope.Tenant(), ct);
        await IssueAsync(tenant, SiteACoordinator, "participant.screen", AuthorityScope.StudySite(siteA), ct);

        string siteACode;
        await using (var db = fixture.CreateContext(tenant))
        {
            siteACode = await (from ss in db.StudySites.AsNoTracking()
                               join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                               where ss.Id == siteA
                               select s.SiteCode).SingleAsync(ct);
        }

        using var dm = Client(code, DataManager);
        using var siteACrc = Client(code, SiteACoordinator);
        var studyRef = NodeRef.For(study).ToString();
        var siteARef = NodeRef.For(siteA).ToString();

        var unlocked = await SendAsync<KernelScopeWriteLockResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&site_code={Uri.EscapeDataString(siteACode)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        unlocked.Locked.Should().BeFalse();

        // Freeze Site A -> locks/in-force reports locked == true for Site A and SUBJ-A, but locked == false for study-only query!
        var frozenSiteA = await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze Site A", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = siteARef,
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        var studyCheck = await SendAsync<KernelScopeWriteLockResponse>(
            dm,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        studyCheck.Locked.Should().BeFalse("a site-level lock does not lock the whole study");

        var siteACheck = await SendAsync<KernelScopeWriteLockResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&site_code={Uri.EscapeDataString(siteACode)}", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        siteACheck.Locked.Should().BeTrue();
        siteACheck.LockKind.Should().Be("Frozen");

        var subjACheck = await SendAsync<KernelScopeWriteLockResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&subject_code=SUBJ-A", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        subjACheck.Locked.Should().BeTrue("SUBJ-A is enrolled at frozen Site A");

        // Attempting POST /api/v1/kernel/enrollments/screen on frozen Site A returns 423 K.Observation.Locked and writes ZERO orphan Participants!
        int participantsBefore;
        await using (var db = fixture.CreateContext(tenant))
        {
            participantsBefore = await db.Participants.CountAsync(ct);
        }

        using (var screenRes = await siteACrc.SendAsync(
            BuildRequest(HttpMethod.Post, "enrollments/screen", "Attempt screening on frozen site", null, KernelProtocolScenario.Key(), new
            {
                study_code = studyCode,
                site_code = siteACode,
                subject_code = "SUBJ-NEW-LOCKED",
                screened_at = SystemClock.Instance.UtcNow.AddMinutes(-1),
            }),
            ct))
        {
            screenRes.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(screenRes, ct)).Should().Be(ObservationErrors.Locked.Code);
        }

        await using (var db = fixture.CreateContext(tenant))
        {
            (await db.Participants.CountAsync(ct)).Should().Be(
                participantsBefore,
                "screening rejected by a covering lock must not register an orphan Participant row");
        }

        // Release Site A freeze -> screening succeeds!
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, $"locks/{frozenSiteA.Ref}/release", ReleaseReason, ReleaseToken(DataManager, frozenSiteA.Ref!), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                meaning = "Responsibility",
            }),
            HttpStatusCode.OK,
            ct);

        await SendAsync<KernelEnrollmentStateResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Post, "enrollments/screen", "Screen after unlock", null, KernelProtocolScenario.Key(), new
            {
                study_code = studyCode,
                site_code = siteACode,
                subject_code = "SUBJ-NEW-UNLOCKED",
                screened_at = SystemClock.Instance.UtcNow.AddMinutes(-1),
            }),
            HttpStatusCode.Created,
            ct);
    }

    /// <summary>
    /// On PostgreSQL, <c>GET /api/v1/kernel/locks/in-force</c> returns uniform non-confirming 404 problem responses
    /// for zero-grant callers and out-of-scope explicit <c>site_code</c> probes, ignores masked cross-site enrollments
    /// when <c>site_code</c> is omitted, preserves 200 OK for authorized pre-enrollment checks, and allows verified
    /// <c>svc:</c> <c>SYSTEM_SERVICE</c> background accounts to evaluate tenant-wide covering locks.
    /// </summary>
    [Fact(DisplayName = "HostLocksInForce_EnforcesNonConfirmingVisibilityAndSystemServiceBypassOnPostgres")]
    [KernelRule("K-AUTH-4")]
    public async Task HostLocksInForceEnforcesNonConfirmingVisibilityAndSystemServiceBypassOnPostgres()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, study, studyCode, siteA, siteB, _, enrollmentB, _, _) = await SeedStudyAsync(ct);

        string siteACode;
        string siteBCode;
        await using (var db = fixture.CreateContext(tenant))
        {
            siteACode = await (from ss in db.StudySites.AsNoTracking()
                               join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                               where ss.Id == siteA
                               select s.SiteCode).SingleAsync(ct);
            siteBCode = await (from ss in db.StudySites.AsNoTracking()
                               join s in db.Sites.AsNoTracking() on ss.SiteId equals s.Id
                               where ss.Id == siteB
                               select s.SiteCode).SingleAsync(ct);
        }

        using var dm = Client(code, DataManager);
        using var siteACrc = Client(code, SiteACoordinator);
        using var stranger = Client(code, Stranger);
        using var econsentSvc = ClientWithRoles(code, "svc:econsent-consumer", ["SYSTEM_SERVICE"]);
        using var seederSvc = ClientWithRoles(code, "svc:execution-db-seeder", ["SYSTEM_SERVICE"]);
        using var unprivilegedSvc = ClientWithRoles(code, "svc:econsent-consumer", ["Service"]);

        var studyRef = NodeRef.For(study).ToString();

        // Freeze Site B and SUBJ-B so cross-site locks exist in PostgreSQL.
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze Site B", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = NodeRef.For(siteB).ToString(),
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);
        await SendAsync<KernelLockResponse>(
            dm,
            BuildRequest(HttpMethod.Post, "locks", "Freeze SUBJ-B", ApplyToken(DataManager), KernelProtocolScenario.Key(), new
            {
                study_ref = studyRef,
                scope_ref = NodeRef.For(enrollmentB).ToString(),
                kind = "Frozen",
                meaning = "Approved",
            }),
            HttpStatusCode.Created,
            ct);

        // Zero-grant caller gets identical 404 on existing and nonexistent study codes.
        using var strangerHidden = await stranger.GetAsync(
            new Uri($"/api/v1/kernel/locks/in-force?study_code={Uri.EscapeDataString(studyCode)}", UriKind.Relative), ct);
        using var strangerMissing = await stranger.GetAsync(
            new Uri("/api/v1/kernel/locks/in-force?study_code=STD-NONEXISTENT-PG", UriKind.Relative), ct);
        strangerHidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        strangerMissing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var strangerHiddenDoc = JsonDocument.Parse(await strangerHidden.Content.ReadAsStringAsync(ct));
        using var strangerMissingDoc = JsonDocument.Parse(await strangerMissing.Content.ReadAsStringAsync(ct));
        strangerHiddenDoc.RootElement.GetProperty("title").GetString()
            .Should().Be(strangerMissingDoc.RootElement.GetProperty("title").GetString());
        strangerHiddenDoc.RootElement.GetProperty("detail").GetString()
            .Should().Be(strangerMissingDoc.RootElement.GetProperty("detail").GetString());

        // Site A coordinator gets 404 on explicit out-of-scope siteBCode, 200 OK (locked == false) on cross-site SUBJ-B without site_code,
        // and 200 OK (locked == false) on Site A with a new pre-enrollment subject_code.
        using var crcCrossSite = await siteACrc.GetAsync(
            new Uri($"/api/v1/kernel/locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&site_code={Uri.EscapeDataString(siteBCode)}", UriKind.Relative), ct);
        crcCrossSite.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crcCrossSubject = await SendAsync<KernelScopeWriteLockResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&subject_code=SUBJ-B", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcCrossSubject.StudyExists.Should().BeTrue();
        crcCrossSubject.Locked.Should().BeFalse();

        var crcPreEnrollment = await SendAsync<KernelScopeWriteLockResponse>(
            siteACrc,
            BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&site_code={Uri.EscapeDataString(siteACode)}&subject_code=SUBJ-PRE-ENROLL", null, null, null, null),
            HttpStatusCode.OK,
            ct);
        crcPreEnrollment.StudyExists.Should().BeTrue();
        crcPreEnrollment.Locked.Should().BeFalse();

        // Verified svc: SYSTEM_SERVICE accounts see tenant-wide locks, whereas svc: without SYSTEM_SERVICE receives 404.
        foreach (var svcClient in new[] { econsentSvc, seederSvc })
        {
            var svcSiteB = await SendAsync<KernelScopeWriteLockResponse>(
                svcClient,
                BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&site_code={Uri.EscapeDataString(siteBCode)}", null, null, null, null),
                HttpStatusCode.OK,
                ct);
            svcSiteB.Locked.Should().BeTrue();
            svcSiteB.LockKind.Should().Be("Frozen");

            var svcSubjB = await SendAsync<KernelScopeWriteLockResponse>(
                svcClient,
                BuildRequest(HttpMethod.Get, $"locks/in-force?study_code={Uri.EscapeDataString(studyCode)}&subject_code=SUBJ-B", null, null, null, null),
                HttpStatusCode.OK,
                ct);
            svcSubjB.Locked.Should().BeTrue();
        }

        using var unprivRes = await unprivilegedSvc.GetAsync(
            new Uri($"/api/v1/kernel/locks/in-force?study_code={Uri.EscapeDataString(studyCode)}", UriKind.Relative), ct);
        unprivRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
