using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Execution;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Host.Onboarding;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL verification and wire-roundtrip benchmark for batched concept lookups and bulk concept definitions
/// under <c>NOSUPERUSER NOBYPASSRLS</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProtocolImportBatchPostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output) : IClassFixture<KernelPostgresFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact(DisplayName = "Benchmark_500ItemImport_And_500PromptlessDesignSummary_OnPostgres")]
    public async Task Benchmark500ItemImportAnd500PromptlessDesignSummaryOnPostgres()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var wireProxy = new PostgresWireProxy(fixture.ConnectionString!);
        var recorder = new PostgresWireRecorder(wireProxy);

        await using var dataSource = CreateInstrumentedDataSource(wireProxy.ProxyConnectionString, recorder);
        using var host = CreateHost(dataSource, recorder);

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenantCode = $"T-IMP-{suffix}";
        var tenant = TenantId.New();
        var sponsor = OrganizationId.New();
        var study = StudyId.New();
        const string designer = "designer@import-bench.test";

        var scenario = new KernelProtocolScenario(fixture);
        await scenario.OkAsync(new ProvisionTenant(tenant, tenantCode, $"Import Bench {suffix}", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new RegisterOrganization(tenant, sponsor, $"ORG-{suffix}", $"Sponsor {suffix}", OrganizationKind.Sponsor, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new RegisterStudy(tenant, study, sponsor, $"STD-{suffix}", "Cardio Outcomes 500", "Phase 3", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var roleId = RoleId.New();
        await scenario.OkAsync(
            new DefineRole(
                tenant,
                roleId,
                "STUDY_DESIGNER",
                "Study Designer",
                ["study.register", "protocol.draft", "protocol.revise_draft", "protocol.submit_for_review", "protocol.approve", "protocol.amend", "attestation.record", "concept.define", "library_item.draft"],
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);
        await scenario.OkAsync(
            new IssueGrant(
                tenant,
                GrantId.New(),
                Actor.Person(designer),
                null,
                roleId,
                AuthorityScope.Tenant(),
                fixture.Clock.UtcNow.AddMinutes(-5),
                null,
                false,
                KernelProtocolScenario.Key(),
                KernelProtocolScenario.Reason),
            ct);

        using var client = host.CreateSignedClient(new GatewayIdentity(designer, [], [], TenantId: tenantCode));

        // Warm up host / DI / connection pool on a lightweight GET first so JIT & startup don't skew SQL counts.
        using (var warmup = await client.GetAsync(new Uri("/api/v1/kernel/studies", UriKind.Relative), ct))
        {
            warmup.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 1. Initial 500-item ODM import
        var odm500 = BuildOdmXml(500);
        recorder.Reset();
        var swImport = Stopwatch.StartNew();
        using var importReq = MultipartOdmPost(
            $"/api/v1/kernel/studies/{NodeRef.For(study)}/protocol-imports",
            odm500,
            "casebook-500.xml",
            $"idem-500-init-{suffix}",
            "Initial 500-item ODM import");
        using var importResp = await client.SendAsync(importReq, ct);
        swImport.Stop();
        var importBody = await importResp.Content.ReadAsStringAsync(ct);
        importResp.StatusCode.Should().Be(HttpStatusCode.Created, importBody);
        var importSnap = recorder.Snapshot(swImport.Elapsed.TotalMilliseconds);

        // 2. Re-import same 500 items into existing draft (new idempotency key)
        recorder.Reset();
        var swReimport = Stopwatch.StartNew();
        using var reimportReq = MultipartOdmPost(
            $"/api/v1/kernel/studies/{NodeRef.For(study)}/protocol-imports",
            odm500,
            "casebook-500.xml",
            $"idem-500-reimport-{suffix}",
            "Re-import 500-item ODM into draft");
        using var reimportResp = await client.SendAsync(reimportReq, ct);
        swReimport.Stop();
        var reimportBody = await reimportResp.Content.ReadAsStringAsync(ct);
        reimportResp.StatusCode.Should().Be(HttpStatusCode.OK, reimportBody);
        var reimportSnap = recorder.Snapshot(swReimport.Elapsed.TotalMilliseconds);

        // 2.5 Idempotent replay with same Idempotency-Key returns identical 201 Created response without creating duplicate concepts or versions
        using (var replayReq = MultipartOdmPost(
            $"/api/v1/kernel/studies/{NodeRef.For(study)}/protocol-imports",
            odm500,
            "casebook-500.xml",
            $"idem-500-init-{suffix}",
            "Initial 500-item ODM import"))
        {
            using var replayResp = await client.SendAsync(replayReq, ct);
            replayResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var replaySummary = JsonSerializer.Deserialize<KernelProtocolDraftSummaryResponse>(
                await replayResp.Content.ReadAsStringAsync(ct),
                Json)!;
            replaySummary.Version.Ref.Should().Be(JsonSerializer.Deserialize<KernelProtocolDraftSummaryResponse>(importBody, Json)!.Version.Ref);
        }

        // 3. Stage a second study with 500 promptless CollectionItems referencing the 500 created concepts, then GET design-summary
        var importedSummary = JsonSerializer.Deserialize<KernelProtocolDraftSummaryResponse>(importBody, Json)!;
        importedSummary.Forms.Should().ContainSingle().Which.Items.Should().HaveCount(500);
        var promptlessStudy = StudyId.New();
        await scenario.OkAsync(new RegisterStudy(tenant, promptlessStudy, sponsor, $"STD-PL-{suffix}", "Promptless 500", "Phase 3", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        await using var readDb = fixture.CreateContext(tenant);
        var conceptIds = await readDb.Concepts.AsNoTracking()
            .OrderBy(c => c.PreferredLabel)
            .Select(c => new ConceptRef(c.Id, c.Version))
            .ToListAsync(ct);
        conceptIds.Should().HaveCount(500);

        // 2.75 Fork import of 500 NEW unique items onto an approved protocol version (DefineConcepts + ForkProtocolVersion + ReviseProtocolDraft = 3 audit locks)
        var forkStudy = StudyId.New();
        await scenario.OkAsync(new RegisterStudy(tenant, forkStudy, sponsor, $"STD-FK-{suffix}", "Fork 500 Study", "Phase 3", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.ApprovedAsync(tenant, forkStudy, ct);
        var odmFork500 = BuildOdmXml(500, itemPrefix: "FK");
        recorder.Reset();
        var swFork = Stopwatch.StartNew();
        using var forkReq = MultipartOdmPost(
            $"/api/v1/kernel/studies/{NodeRef.For(forkStudy)}/protocol-imports",
            odmFork500,
            "casebook-fork-500.xml",
            $"idem-500-fork-{suffix}",
            "Fork 500-item ODM import onto approved version");
        using var forkResp = await client.SendAsync(forkReq, ct);
        swFork.Stop();
        var forkBody = await forkResp.Content.ReadAsStringAsync(ct);
        forkResp.StatusCode.Should().Be(HttpStatusCode.Created, forkBody);
        var forkSnap = recorder.Snapshot(swFork.Elapsed.TotalMilliseconds);

        var promptlessItems = conceptIds
            .Select((cRef, idx) => new CollectionItem($"IT_{idx + 1:D4}", cRef, Required: idx % 2 == 0, ValueKind: ObservationValueKind.Quantity, Prompt: null))
            .ToList();
        var promptlessDesign = new ProtocolDesign
        {
            Title = "Promptless 500 Study",
            CollectionSpecs = [new CollectionSpec("FORM_500", "Promptless 500 Form", promptlessItems)],
        };
        var promptlessVersionId = ProtocolVersionId.New();
        (await scenario.CreateDraftAsync(tenant, promptlessStudy, promptlessVersionId, ct, ProtocolDesignDocument.Write(promptlessDesign))).IsSuccess.Should().BeTrue();

        recorder.Reset();
        var swSummary = Stopwatch.StartNew();
        using var summaryResp = await client.GetAsync(
            new Uri($"/api/v1/kernel/protocol-versions/{NodeRef.For(promptlessVersionId)}/design-summary", UriKind.Relative),
            ct);
        swSummary.Stop();
        var summaryBody = await summaryResp.Content.ReadAsStringAsync(ct);
        summaryResp.StatusCode.Should().Be(HttpStatusCode.OK, summaryBody);
        var summarySnap = recorder.Snapshot(swSummary.Elapsed.TotalMilliseconds);
        var summaryDto = JsonSerializer.Deserialize<KernelProtocolDraftSummaryResponse>(summaryBody, Json)!;
        summaryDto.Forms.Single().Items.Should().HaveCount(500);
        summaryDto.Forms.Single().Items[0].Prompt.Should().Be("Benchmark Measurement 0001");
        summaryDto.Forms.Single().Items[499].Prompt.Should().Be("Benchmark Measurement 0500");

        // 4. Verify unbroken audit hash chain and Merkle sealing across all batch-defined Concept AuditEntryRecord rows + ProtocolVersion rows
        await using var adminDb = fixture.CreateAdminContext();
        var conceptAuditCount = await adminDb.AuditEntries.AsNoTracking()
            .CountAsync(a => a.TenantId == tenant && a.AggregateType == "Concept", ct);
        conceptAuditCount.Should().Be(1000);

        var chainBeforeSeal = await fixture.AuditChainVerifier.VerifyAsync(tenant, ct);
        chainBeforeSeal.IsIntact.Should().BeTrue(chainBeforeSeal.Problem);
        await fixture.AuditChainSealer.SealAllAsync(ct);
        var chainAfterSeal = await fixture.AuditChainVerifier.VerifyAsync(tenant, ct);
        chainAfterSeal.IsIntact.Should().BeTrue(chainAfterSeal.Problem);

        // Also verify that the 500-concept registry label resolution itself uses strictly 1 connection lease and O(1) queries under cadence.tenant_id RLS.
        var hostRegistry = host.Services.GetRequiredService<IConceptRegistry>();
        recorder.Reset();
        var swRegistry = Stopwatch.StartNew();
        var resolved500 = await hostRegistry.ResolveManyAsync(tenant, conceptIds, ct);
        swRegistry.Stop();
        var registrySnap = recorder.Snapshot(swRegistry.Elapsed.TotalMilliseconds);
        resolved500.Should().HaveCount(500);

        // Simulate 2 ms network RTT on the PostgreSQL TCP wire proxy and verify both 500-item initial import and re-import finish under 2 s.
        var rttStudy = StudyId.New();
        await scenario.OkAsync(new RegisterStudy(tenant, rttStudy, sponsor, $"STD-RTT-{suffix}", "RTT 2ms 500 Study", "Phase 3", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var odmRtt500 = BuildOdmXml(500, itemPrefix: "RTT");
        wireProxy.SetSimulatedRttMs(2);
        BenchmarkSnapshot rttInitialSnap;
        BenchmarkSnapshot rttReimportSnap;
        try
        {
            recorder.Reset();
            var swRttInit = Stopwatch.StartNew();
            using (var rttInitReq = MultipartOdmPost(
                $"/api/v1/kernel/studies/{NodeRef.For(rttStudy)}/protocol-imports",
                odmRtt500,
                "casebook-rtt-500.xml",
                $"idem-500-rtt-init-{suffix}",
                "500-item ODM initial import at 2ms RTT"))
            {
                using var rttInitResp = await client.SendAsync(rttInitReq, ct);
                swRttInit.Stop();
                rttInitResp.StatusCode.Should().Be(HttpStatusCode.Created);
                rttInitialSnap = recorder.Snapshot(swRttInit.Elapsed.TotalMilliseconds);
            }

            recorder.Reset();
            var swRttReimport = Stopwatch.StartNew();
            using var rttReimportReq = MultipartOdmPost(
                $"/api/v1/kernel/studies/{NodeRef.For(rttStudy)}/protocol-imports",
                odmRtt500,
                "casebook-rtt-500.xml",
                $"idem-500-rtt-reimport-{suffix}",
                "500-item ODM re-import at 2ms RTT");
            using var rttReimportResp = await client.SendAsync(rttReimportReq, ct);
            swRttReimport.Stop();
            rttReimportResp.StatusCode.Should().Be(HttpStatusCode.OK);
            rttReimportSnap = recorder.Snapshot(swRttReimport.Elapsed.TotalMilliseconds);
        }
        finally
        {
            wireProxy.SetSimulatedRttMs(0);
        }

        var artifactPath = Environment.GetEnvironmentVariable("CADENCE_BENCHMARK_OUT") ?? "/tmp/stitch/scratch/benchmark-latest.json";
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        await File.WriteAllTextAsync(
            artifactPath,
            JsonSerializer.Serialize(
                new
                {
                    initial_import_500 = importSnap,
                    fork_import_500 = forkSnap,
                    reimport_500 = reimportSnap,
                    promptless_design_summary_500 = summarySnap,
                    registry_resolve_500_single_lease = registrySnap,
                    initial_import_500_at_2ms_rtt = rttInitialSnap,
                    reimport_500_at_2ms_rtt = rttReimportSnap,
                },
                new JsonSerializerOptions { WriteIndented = true }),
            ct);

        output.WriteLine($"[BENCHMARK] Initial 500-item import: WireRoundtrips={importSnap.WireCommands}, SqlStatements={importSnap.SqlStatements}, ConnectionsOpened={importSnap.ConnectionsOpened}, SetConfigs={importSnap.SetConfigCommands}, AuditHeadLocks={importSnap.AuditHeadLocks}, Transactions={importSnap.TransactionsCommitted}, ElapsedMs={importSnap.ElapsedMs:F2}");
        output.WriteLine($"[BENCHMARK] Fork 500-item import: WireRoundtrips={forkSnap.WireCommands}, SqlStatements={forkSnap.SqlStatements}, ConnectionsOpened={forkSnap.ConnectionsOpened}, SetConfigs={forkSnap.SetConfigCommands}, AuditHeadLocks={forkSnap.AuditHeadLocks}, Transactions={forkSnap.TransactionsCommitted}, ElapsedMs={forkSnap.ElapsedMs:F2}");
        output.WriteLine($"[BENCHMARK] Re-import 500-item draft: WireRoundtrips={reimportSnap.WireCommands}, SqlStatements={reimportSnap.SqlStatements}, ConnectionsOpened={reimportSnap.ConnectionsOpened}, SetConfigs={reimportSnap.SetConfigCommands}, AuditHeadLocks={reimportSnap.AuditHeadLocks}, Transactions={reimportSnap.TransactionsCommitted}, ElapsedMs={reimportSnap.ElapsedMs:F2}");
        output.WriteLine($"[BENCHMARK] GET 500-item promptless design-summary: WireRoundtrips={summarySnap.WireCommands}, SqlStatements={summarySnap.SqlStatements}, ConnectionsOpened={summarySnap.ConnectionsOpened}, SetConfigs={summarySnap.SetConfigCommands}, ElapsedMs={summarySnap.ElapsedMs:F2}");
        output.WriteLine($"[BENCHMARK] 2ms RTT 500-item initial={rttInitialSnap.ElapsedMs:F2}ms (WireRoundtrips={rttInitialSnap.WireCommands}), reimport={rttReimportSnap.ElapsedMs:F2}ms (WireRoundtrips={rttReimportSnap.WireCommands})");

        // 5. Quantitative acceptance assertions on PostgreSQL wire roundtrips, latency at 2ms RTT, and audit_chain_heads locks
        importSnap.WireCommands.Should().BeLessThan(60, "500-item initial import must drop from ~18,561 wire roundtrips to < 60");
        importSnap.AuditHeadLocks.Should().BeLessThanOrEqualTo(2, "500-item initial import must lock audit_chain_heads at most twice (DefineConcepts + CreateProtocolDraft)");
        importSnap.TransactionsCommitted.Should().BeLessThanOrEqualTo(2);

        forkSnap.AuditHeadLocks.Should().BeLessThanOrEqualTo(3, "500-item fork import must lock audit_chain_heads at most 3 times (DefineConcepts + ForkProtocolVersion + ReviseProtocolDraft)");
        forkSnap.TransactionsCommitted.Should().BeLessThanOrEqualTo(3);
        forkSnap.WireCommands.Should().BeLessThan(80, "500-item fork import must execute O(1) wire roundtrips");

        reimportSnap.WireCommands.Should().BeLessThan(60, "500-item re-import with active concepts must skip DefineConcepts and run < 60 wire roundtrips");
        reimportSnap.AuditHeadLocks.Should().Be(1, "re-import with active concepts must commit only ReviseProtocolDraft");
        reimportSnap.TransactionsCommitted.Should().Be(1);

        rttInitialSnap.WireCommands.Should().BeLessThan(60);
        rttInitialSnap.ElapsedMs.Should().BeLessThan(2000, "500-item initial import must finish under 2 s at 2 ms RTT");
        rttReimportSnap.WireCommands.Should().BeLessThan(60);
        rttReimportSnap.ElapsedMs.Should().BeLessThan(2000, "500-item re-import must finish under 2 s at 2 ms RTT");

        summarySnap.WireCommands.Should().BeLessThan(20, "GET design-summary for 500 promptless items must batch-resolve labels in O(1) queries");
        summarySnap.ConnectionsOpened.Should().BeLessThanOrEqualTo(4);

        registrySnap.ConnectionsOpened.Should().Be(1, "batch concept resolution must hold strictly 1 connection lease");
        registrySnap.SetConfigCommands.Should().Be(2, "single lease sets cadence.tenant_id on open and clears it on close");
        registrySnap.WireCommands.Should().BeLessThanOrEqualTo(6, "batch concept resolution runs 3 SELECTs + 2 set_configs on 1 lease");
    }

    [Fact(DisplayName = "ImportProtocolDraft_FailureModes_WriteZeroConceptRowsOnPostgres")]
    public async Task ImportProtocolDraftFailureModesWriteZeroConceptRowsOnPostgres()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var wireProxy = new PostgresWireProxy(fixture.ConnectionString!);
        var recorder = new PostgresWireRecorder(wireProxy);
        await using var dataSource = CreateInstrumentedDataSource(wireProxy.ProxyConnectionString, recorder);
        using var host = CreateHost(dataSource, recorder);

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenantCode = $"T-FAIL-{suffix}";
        var tenant = TenantId.New();
        var sponsor = OrganizationId.New();
        var study = StudyId.New();
        const string designer = "designer@import-fail.test";
        const string authorNoDefine = "author-nodefine@import-fail.test";

        var scenario = new KernelProtocolScenario(fixture);
        await scenario.OkAsync(new ProvisionTenant(tenant, tenantCode, $"Import Fail {suffix}", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new RegisterOrganization(tenant, sponsor, $"ORG-{suffix}", $"Sponsor {suffix}", OrganizationKind.Sponsor, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new RegisterStudy(tenant, study, sponsor, $"STD-{suffix}", "Rollback Verification Study", "Phase 2", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var fullRole = RoleId.New();
        await scenario.OkAsync(
            new DefineRole(tenant, fullRole, "DESIGNER_FULL", "Designer", ["protocol.draft", "protocol.revise_draft", "concept.define"], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        await scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(designer), null, fullRole, AuthorityScope.Tenant(), fixture.Clock.UtcNow.AddMinutes(-5), null, false, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);

        var noDefineRole = RoleId.New();
        await scenario.OkAsync(
            new DefineRole(tenant, noDefineRole, "AUTHOR_NO_CONCEPT", "Author Without Concept Define", ["protocol.draft", "protocol.revise_draft"], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        await scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(authorNoDefine), null, noDefineRole, AuthorityScope.Tenant(), fixture.Clock.UtcNow.AddMinutes(-5), null, false, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);

        using var designerClient = host.CreateSignedClient(new GatewayIdentity(designer, [], [], TenantId: tenantCode));
        using var noDefineClient = host.CreateSignedClient(new GatewayIdentity(authorNoDefine, [], [], TenantId: tenantCode));
        var endpoint = $"/api/v1/kernel/studies/{NodeRef.For(study)}/protocol-imports";

        async Task<int> CountConceptsAsync()
        {
            await using var db = fixture.CreateContext(tenant);
            return await db.Concepts.AsNoTracking().CountAsync(ct);
        }

        // 1. Malformed XML -> 422, 0 concepts
        using (var r = await designerClient.SendAsync(MultipartOdmPost(endpoint, "<ODM><Study>", "bad.xml", $"k1-{suffix}", "Malformed XML"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 2. Malformed JSON -> 422, 0 concepts
        using (var r = await designerClient.SendAsync(MultipartBytesPost(endpoint, Encoding.UTF8.GetBytes("{broken"), "bad.json", "application/json", $"k2-{suffix}", "Malformed JSON"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 3. DTD / XXE declaration -> 422, 0 concepts
        const string dtdXml = "<?xml version=\"1.0\"?><!DOCTYPE ODM [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><ODM>&xxe;</ODM>";
        using (var r = await designerClient.SendAsync(MultipartOdmPost(endpoint, dtdXml, "dtd.xml", $"k3-{suffix}", "DTD check"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 4. Payload > 10 MB -> 413, 0 concepts
        var oversized = new byte[10 * 1024 * 1024 + 64];
        Array.Fill(oversized, (byte)'x');
        using (var r = await designerClient.SendAsync(MultipartBytesPost(endpoint, oversized, "oversized.xml", "application/xml", $"k4-{suffix}", "Oversized check"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 5. Multi-form ODM where Form 1 is valid and Form 2 has a missing ItemDef -> 422, 0 concepts
        const string partialBrokenOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="F1" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S1"><MetaDataVersion OID="M1" Name="v1">
                <FormDef OID="F.1" Name="Form 1"><ItemGroupRef ItemGroupOID="IG.1" OrderNumber="1" Mandatory="Yes"/></FormDef>
                <FormDef OID="F.2" Name="Form 2"><ItemGroupRef ItemGroupOID="IG.2" OrderNumber="1" Mandatory="Yes"/></FormDef>
                <ItemGroupDef OID="IG.1" Name="G1"><ItemRef ItemOID="IT.1" OrderNumber="1" Mandatory="Yes"/></ItemGroupDef>
                <ItemGroupDef OID="IG.2" Name="G2"><ItemRef ItemOID="IT.MISSING" OrderNumber="1" Mandatory="Yes"/></ItemGroupDef>
                <ItemDef OID="IT.1" Name="ITEM1" DataType="text"/>
              </MetaDataVersion></Study>
            </ODM>
            """;
        using (var r = await designerClient.SendAsync(MultipartOdmPost(endpoint, partialBrokenOdm, "partial.xml", $"k5-{suffix}", "Broken second form"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 6. Caller without concept.define -> 403 Forbidden, 0 concepts
        using (var r = await noDefineClient.SendAsync(MultipartOdmPost(endpoint, BuildOdmXml(5), "valid5.xml", $"k6-{suffix}", "Missing concept.define"), ct))
        {
            r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CountConceptsAsync()).Should().Be(0);
        }

        // 7. Deprecated Biomedical Concept -> 422 K.Knowledge.ConceptDeprecated, 0 new concepts written during import
        var depConceptId = ConceptId.New();
        await scenario.OkAsync(new DefineConcept(tenant, depConceptId, ConceptOrigin.CdiscBiomedicalConcept, "C25298", ConceptKind.BiomedicalConcept, "Deprecated BP", "Deprecated", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new DeprecateConcept(tenant, depConceptId, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        int countAfterSeedDeprecated = await CountConceptsAsync();
        countAfterSeedDeprecated.Should().Be(1);

        const string odmWithDeprecatedAndUnmapped = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="F.DEP" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S.DEP"><MetaDataVersion OID="M.DEP" Name="v1">
                <FormDef OID="F.1" Name="Vitals"><ItemGroupRef ItemGroupOID="IG.1" OrderNumber="1" Mandatory="Yes"/></FormDef>
                <ItemGroupDef OID="IG.1" Name="G1">
                  <ItemRef ItemOID="IT.UNMAPPED" OrderNumber="1" Mandatory="Yes"/>
                  <ItemRef ItemOID="IT.DEP" OrderNumber="2" Mandatory="Yes"/>
                </ItemGroupDef>
                <ItemDef OID="IT.UNMAPPED" Name="NEW_SPONSOR_ITEM" DataType="text"/>
                <ItemDef OID="IT.DEP" Name="DEP_ITEM" DataType="integer">
                  <Alias Context="CDASH" Name="extCodeID: C25298"/>
                </ItemDef>
              </MetaDataVersion></Study>
            </ODM>
            """;
        using (var r = await designerClient.SendAsync(MultipartOdmPost(endpoint, odmWithDeprecatedAndUnmapped, "dep.xml", $"k7-{suffix}", "Deprecated BC check"), ct))
        {
            var body = await r.Content.ReadAsStringAsync(ct);
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, body);
            body.Should().Contain("K.Knowledge.ConceptDeprecated");
            (await CountConceptsAsync()).Should().Be(countAfterSeedDeprecated, "deprecated concept must prevent any unmapped sibling concepts in the batch from being written");
        }
    }

    private static string BuildOdmXml(int itemCount, string itemPrefix = "BENCH")
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine($"""<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="{itemPrefix}-500" FileType="Snapshot" CreationDateTime="2026-11-20T00:00:00Z" ODMVersion="1.3.2">""");
        sb.AppendLine($"""  <Study OID="ST.{itemPrefix}.500">""");
        sb.AppendLine("""    <GlobalVariables>""");
        sb.AppendLine($"""      <StudyName>{itemPrefix} 500 Study</StudyName>""");
        sb.AppendLine("""      <StudyDescription>500-item benchmark study</StudyDescription>""");
        sb.AppendLine($"""      <ProtocolName>{itemPrefix}-500</ProtocolName>""");
        sb.AppendLine("""    </GlobalVariables>""");
        sb.AppendLine("""    <MetaDataVersion OID="MDV.1" Name="v1">""");
        sb.AppendLine($"""      <FormDef OID="FORM.{itemPrefix}" Name="{itemPrefix} Form" Repeating="No">""");
        sb.AppendLine($"""        <ItemGroupRef ItemGroupOID="IG.{itemPrefix}" OrderNumber="1" Mandatory="Yes" />""");
        sb.AppendLine("""      </FormDef>""");
        sb.AppendLine($"""      <ItemGroupDef OID="IG.{itemPrefix}" Name="{itemPrefix} Group" Repeating="No">""");
        for (int i = 1; i <= itemCount; i++)
        {
            var mandatory = i % 2 == 0 ? "Yes" : "No";
            sb.AppendLine($"""        <ItemRef ItemOID="IT.{itemPrefix}.{i:D4}" OrderNumber="{i}" Mandatory="{mandatory}" />""");
        }

        sb.AppendLine("""      </ItemGroupDef>""");
        for (int i = 1; i <= itemCount; i++)
        {
            sb.AppendLine($"""      <ItemDef OID="IT.{itemPrefix}.{i:D4}" Name="{itemPrefix}_{i:D4}" DataType="float">""");
            sb.AppendLine($"""        <Question><TranslatedText xml:lang="en">{(itemPrefix == "BENCH" ? "Benchmark" : itemPrefix)} Measurement {i:D4}</TranslatedText></Question>""");
            sb.AppendLine("""      </ItemDef>""");
        }

        sb.AppendLine("""    </MetaDataVersion>""");
        sb.AppendLine("""  </Study>""");
        sb.AppendLine("""</ODM>""");
        return sb.ToString();
    }

    private static HttpRequestMessage MultipartOdmPost(string path, string xml, string fileName, string idempotencyKey, string reason) =>
        MultipartBytesPost(path, Encoding.UTF8.GetBytes(xml), fileName, "application/xml", idempotencyKey, reason);

    private static HttpRequestMessage MultipartBytesPost(string path, byte[] bytes, string fileName, string contentType, string idempotencyKey, string reason)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = multipart,
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        req.Headers.Add(GatewayHeaders.ChangeReason, reason);
        return req;
    }

    private static NpgsqlDataSource CreateInstrumentedDataSource(string connectionString, PostgresWireRecorder recorder)
    {
        var loggerFactory = LoggerFactory.Create(lb =>
        {
            lb.ClearProviders();
            lb.AddFilter("Npgsql.Command", LogLevel.Information);
            lb.AddFilter("Npgsql.Transaction", LogLevel.Debug);
            lb.AddProvider(recorder);
        });
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseLoggerFactory(loggerFactory);
        return builder.Build();
    }

    private WebApplicationFactory<Cadence.Kernel.Host.Program> CreateHost(NpgsqlDataSource dataSource, PostgresWireRecorder recorder)
    {
        var tenantInterceptor = new KernelTenantConnectionInterceptor();
        return new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseTestGatewaySecret()
                .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(fixture.Clock);

                    foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                    {
                        services.Remove(hosted);
                    }

                    foreach (var descriptor in services
                        .Where(d => d.ServiceType == typeof(IDbContextFactory<KernelDbContext>)
                            || d.ServiceType == typeof(DbContextOptions<KernelDbContext>)
                            || d.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true)
                        .ToList())
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContextFactory<KernelDbContext>(options =>
                    {
                        KernelDatabase.UseKernelNpgsql(options, dataSource);
                        options.AddInterceptors(tenantInterceptor, recorder);
                    });
                });
        });
    }

    private sealed record BenchmarkSnapshot(
        int WireCommands,
        int SqlStatements,
        int ConnectionsOpened,
        int SetConfigCommands,
        int AuditHeadLocks,
        int TransactionsCommitted,
        double ElapsedMs);

    private sealed class PostgresWireRecorder(PostgresWireProxy wireProxy) : DbCommandInterceptor, IDbConnectionInterceptor, IDbTransactionInterceptor, ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _sqlStatements = new();
        private int _connectionsOpened;
        private int _transactionsCommitted;

        public void Reset()
        {
            _sqlStatements.Clear();
            Interlocked.Exchange(ref _connectionsOpened, 0);
            Interlocked.Exchange(ref _transactionsCommitted, 0);
            wireProxy.Reset();
        }

        public BenchmarkSnapshot Snapshot(double elapsedMs)
        {
            var commands = _sqlStatements.ToArray();
            var setConfigs = commands.Count(sql => sql.Contains("pg_catalog.set_config", StringComparison.Ordinal));
            var auditLocks = commands.Count(sql =>
                sql.Contains("audit_chain_heads", StringComparison.Ordinal)
                && sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));

            var txCount = Volatile.Read(ref _transactionsCommitted);
            return new BenchmarkSnapshot(
                wireProxy.ReadyForQueryRoundtrips,
                commands.Length,
                Volatile.Read(ref _connectionsOpened),
                setConfigs,
                auditLocks,
                txCount,
                Math.Round(elapsedMs, 2));
        }

        public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            Interlocked.Increment(ref _connectionsOpened);

        public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectionsOpened);
            return Task.CompletedTask;
        }

        public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
            Interlocked.Increment(ref _transactionsCommitted);

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _transactionsCommitted);
            return Task.CompletedTask;
        }

        public ILogger CreateLogger(string categoryName) =>
            string.Equals(categoryName, "Npgsql.Command", StringComparison.Ordinal)
                ? new NpgsqlCommandLogger(_sqlStatements)
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class NpgsqlCommandLogger(ConcurrentQueue<string> commands) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Information;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Information
                    && eventId.Id == 2001
                    && state is IReadOnlyList<KeyValuePair<string, object?>> values
                    && values.FirstOrDefault(kv => string.Equals(kv.Key, "CommandText", StringComparison.Ordinal)).Value is string sql)
                {
                    commands.Enqueue(sql);
                }
            }
        }
    }

    /// <summary>
    /// Loopback PostgreSQL v3 TCP wire proxy that counts exact backend <c>ReadyForQuery</c> (<c>'Z'</c>) protocol frames
    /// (1 per query, <c>set_config</c>, <c>BEGIN</c>, <c>COMMIT</c>, or pipelined <c>NpgsqlBatch</c>) and optionally injects
    /// per-roundtrip RTT latency.
    /// </summary>
    private sealed class PostgresWireProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private readonly ConcurrentBag<TcpClient> _clients = [];
        private readonly string _upstreamHost;
        private readonly int _upstreamPort;
        private int _readyForQueryRoundtrips;
        private int _simulatedRttMs;

        public PostgresWireProxy(string upstreamConnectionString)
        {
            var csb = new NpgsqlConnectionStringBuilder(upstreamConnectionString);
            _upstreamHost = csb.Host ?? "127.0.0.1";
            _upstreamPort = csb.Port;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var localPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            csb.Host = IPAddress.Loopback.ToString();
            csb.Port = localPort;
            csb.SslMode = SslMode.Disable;
            ProxyConnectionString = csb.ConnectionString;

            _acceptLoop = AcceptLoopAsync(_cts.Token);
        }

        public string ProxyConnectionString { get; }

        public int ReadyForQueryRoundtrips => Volatile.Read(ref _readyForQueryRoundtrips);

        public void SetSimulatedRttMs(int rttMs) => Volatile.Write(ref _simulatedRttMs, rttMs);

        public void Reset() => Interlocked.Exchange(ref _readyForQueryRoundtrips, 0);

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
                // Expected when tearing down the wire proxy accept loop.
            }

            _cts.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient downstream;
                try
                {
                    downstream = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                downstream.NoDelay = true;
                _clients.Add(downstream);
                _ = BridgeConnectionAsync(downstream, ct);
            }
        }

        private async Task BridgeConnectionAsync(TcpClient downstream, CancellationToken ct)
        {
            using (downstream)
            using (var upstream = new TcpClient { NoDelay = true })
            {
                try
                {
                    await upstream.ConnectAsync(_upstreamHost, _upstreamPort, ct);
                    _clients.Add(upstream);
                    await using var downStream = downstream.GetStream();
                    await using var upStream = upstream.GetStream();

                    var turnState = new ConnectionTurnState();
                    var c2s = PumpClientToServerAsync(downStream, upStream, turnState, ct);
                    var s2c = PumpBackendMessagesAsync(upStream, downStream, turnState, ct);
                    await Task.WhenAny(c2s, s2c);
                }
                catch (Exception)
                {
                    // Connection closed or disposed at end of test.
                }
            }
        }

        private static async Task PumpClientToServerAsync(
            NetworkStream source,
            NetworkStream destination,
            ConnectionTurnState turnState,
            CancellationToken ct)
        {
            var buffer = new byte[65536];
            while (!ct.IsCancellationRequested)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }

                Volatile.Write(ref turnState.ClientSentSinceLastReady, 1);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        private async Task PumpBackendMessagesAsync(
            NetworkStream upstream,
            NetworkStream downstream,
            ConnectionTurnState turnState,
            CancellationToken ct)
        {
            var buffer = new byte[65536];
            var header = new byte[5];
            var headerFilled = 0;
            var payloadRemaining = 0;

            while (!ct.IsCancellationRequested)
            {
                var read = await upstream.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }

                var readyFramesInChunk = CountReadyForQueryFrames(
                    buffer.AsSpan(0, read),
                    header,
                    ref headerFilled,
                    ref payloadRemaining);

                if (readyFramesInChunk > 0 && Interlocked.Exchange(ref turnState.ClientSentSinceLastReady, 0) == 1)
                {
                    Interlocked.Increment(ref _readyForQueryRoundtrips);
                    var rttMs = Volatile.Read(ref _simulatedRttMs);
                    if (rttMs > 0)
                    {
                        await Task.Delay(rttMs, ct);
                    }
                }

                await downstream.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }

        private sealed class ConnectionTurnState
        {
            public int ClientSentSinceLastReady;
        }

        private static int CountReadyForQueryFrames(
            ReadOnlySpan<byte> chunk,
            byte[] header,
            ref int headerFilled,
            ref int payloadRemaining)
        {
            var readyCount = 0;
            var offset = 0;
            while (offset < chunk.Length)
            {
                if (payloadRemaining > 0)
                {
                    var skip = Math.Min(payloadRemaining, chunk.Length - offset);
                    payloadRemaining -= skip;
                    offset += skip;
                    continue;
                }

                var needed = 5 - headerFilled;
                var take = Math.Min(needed, chunk.Length - offset);
                chunk.Slice(offset, take).CopyTo(header.AsSpan(headerFilled, take));
                headerFilled += take;
                offset += take;

                if (headerFilled == 5)
                {
                    var messageType = header[0];
                    var messageLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));
                    payloadRemaining = Math.Max(0, messageLength - 4);
                    headerFilled = 0;

                    if (messageType == (byte)'Z')
                    {
                        readyCount++;
                    }
                }
            }

            return readyCount;
        }
    }
}
