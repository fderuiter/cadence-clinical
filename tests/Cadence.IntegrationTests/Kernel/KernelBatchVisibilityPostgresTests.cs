using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Authorization;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Packs;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.TestSupport.Kernel.Rules;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KernelPerformanceQuotaCollection
{
    public const string Name = "KernelPerformanceQuota";

    private KernelPerformanceQuotaCollection()
    {
    }
}

/// <summary>
/// PostgreSQL verification of batched scope lookups over shared connection leases (<c>KernelAuthorityStore</c>,
/// <c>GrantNodeVisibility</c>, <c>GrantChain</c>, <c>KernelLineageStore</c>, and <c>RequesterBoundVisibility</c>):
/// one connection lease and two <c>pg_catalog.set_config</c> statements per bulk authority call (K-ID-2), one set-based
/// <c>SELECT</c> per present kind, exact parity with single-node lookups across all 18 path-bearing kinds, lineage
/// version deduplication with preserved <c>masked:n</c> handles, and the 2,000-node waterfall benchmark.
/// </summary>
[Collection(KernelPerformanceQuotaCollection.Name)]
[Trait("Category", "Integration")]
public sealed class KernelBatchVisibilityPostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output) : IClassFixture<KernelPostgresFixture>
{
    private const string WaterfallArtifactPath = "/tmp/artifacts/kernel-visibility-waterfall-results.json";
    private const string EvidenceBlindingArtifactPath = "/tmp/artifacts/evidence-blinding-loh-results.json";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates <see cref="IAuthorityStore.PathsOfAsync"/> across all 18 path-bearing kinds, every scope level on
    /// grants, qualification requirements, effectivities, assertions, attestations and validation findings, versioned
    /// references, missing ids, and non-path kinds: every returned <see cref="ScopePath"/> matches
    /// <see cref="IAuthorityStore.PathOfAsync"/>, while opening 1 connection lease, running 2 <c>pg_catalog.set_config</c>
    /// statements, and executing 1 <c>SELECT</c> per present kind.
    /// </summary>
    [Fact(DisplayName = "PathsOfAsync_AllEighteenPathBearingKinds_MatchesSingleLookupInOneLeaseAndOneQueryPerKind")]
    [KernelRule("K-ID-2")]
    [KernelRule("K-AUTH-2")]
    public async Task PathsOfAsyncAllEighteenPathBearingKindsMatchesSingleLookupInOneLeaseAndOneQueryPerKind()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedAllKindsWorldAsync(ct);

        await using var harness = CreateInstrumentedStore();

        // Non-path kinds alone short-circuit without opening a connection or executing SQL.
        harness.Recorder.Reset();
        NodeRef[] nonPathOnly =
        [
            NodeRef.For(world.Tenant),
            NodeRef.For(world.ParticipantBoston),
            NodeRef.For(PersonId.New()),
            NodeRef.For(ConceptId.New(), 1),
            NodeRef.For(TaskConfigurationId.New(), 1),
        ];
        var nonPathResult = await harness.AuthorityStore.PathsOfAsync(world.Tenant, nonPathOnly, ct);
        nonPathResult.Values.Should().AllSatisfy(p => p.Should().Be(ScopePath.TenantWide));
        harness.Recorder.ConnectionsOpened.Should().Be(0);
        harness.Recorder.SetConfigStatements.Should().Be(0);
        harness.Recorder.SelectStatements.Should().Be(0);

        // Full batch spanning all 18 path-bearing kinds + versioned duplicates + missing ids + non-path kinds.
        NodeRef[] resources =
        [
            NodeRef.For(world.Sponsor),
            NodeRef.For(world.HospitalBoston),
            NodeRef.For(world.StudyA, 1),
            NodeRef.For(world.StudyA, 2),
            NodeRef.For(world.StudyB),
            NodeRef.For(world.BostonSite),
            NodeRef.For(world.BerlinSite),
            NodeRef.For(world.EnrollmentBoston, 1),
            NodeRef.For(world.EnrollmentBoston, 2),
            NodeRef.For(world.ProtocolVersionA, 1),
            NodeRef.For(world.ProtocolVersionA, 3),
            new(NodeKinds.ProtocolElement, world.ProtocolElementAId, 1),
            new(NodeKinds.ProtocolElement, world.ProtocolElementAId, 2),
            NodeRef.For(world.StudyArtifact),
            NodeRef.For(world.TenantArtifact),
            NodeRef.For(world.StudyArtifactVersion, 1),
            NodeRef.For(world.TenantArtifactVersion, 1),
            NodeRef.For(world.TenantGrant),
            NodeRef.For(world.OrgGrant),
            NodeRef.For(world.StudyGrant),
            NodeRef.For(world.CountryGrant),
            NodeRef.For(world.SiteGrant),
            NodeRef.For(world.OrgRequirement),
            NodeRef.For(world.StudyRequirement),
            NodeRef.For(world.CountryRequirement),
            NodeRef.For(world.SiteRequirement),
            NodeRef.For(world.StudyEffectivity),
            NodeRef.For(world.CountryEffectivity),
            NodeRef.For(world.SiteEffectivity),
            NodeRef.For(world.ProposalA, 1),
            NodeRef.For(world.CollectionInstanceBoston, 1),
            NodeRef.For(world.CollectionInstanceBoston, 2),
            NodeRef.For(world.FormObservationBoston, 1),
            NodeRef.For(world.StandaloneObservationBoston, 1),
            NodeRef.For(world.VerificationAssertionBoston, 1),
            NodeRef.For(world.RecordedFormLockAssertion, 1),
            NodeRef.For(world.SiteLockAssertion, 1),
            NodeRef.For(world.StudyLockAssertion, 1),
            NodeRef.For(world.OpenStudyFinding),
            NodeRef.For(world.DecidedStudyFinding, 1),
            NodeRef.For(world.SiteCollectionFinding),
            NodeRef.For(world.SiteObservationFinding),
            NodeRef.For(world.ProtocolAttestation, 1),
            NodeRef.For(world.EnrollmentAttestation, 1),
            NodeRef.For(world.StudyArtifactAttestation, 1),
            NodeRef.For(world.TenantArtifactAttestation, 1),
            NodeRef.For(world.CollectionAttestation, 1),
            NodeRef.For(world.RecordedLockAttestation, 1),
            NodeRef.For(world.UnrecordedLockAttestation, 1),
            NodeRef.For(world.ProposalAttestation, 1),
            NodeRef.For(world.StudyFindingAttestation, 1),
            NodeRef.For(world.SiteFindingAttestation, 1),
            NodeRef.For(world.StandardAttestation, 1),
            NodeRef.For(world.LibraryItemAttestation, 1),
            NodeRef.For(world.TaskConfigurationAttestation, 1),
            NodeRef.For(StudyId.New()),
            .. nonPathOnly,
        ];

        harness.Recorder.Reset();
        var bulkPaths = await harness.AuthorityStore.PathsOfAsync(world.Tenant, resources, ct);

        var bulkLeaseCount = harness.Recorder.ConnectionsOpened;
        var bulkSetConfigs = harness.Recorder.SetConfigStatements;
        var bulkSelects = harness.Recorder.SelectStatements;

        bulkLeaseCount.Should().Be(1, "PathsOfAsync holds a single connection lease across all per-kind queries");
        bulkSetConfigs.Should().Be(2, "KernelTenantConnectionInterceptor sets the tenant once on open and clears it once on close");
        bulkSelects.Should().Be(18, "each of the 18 path-bearing kinds resolves in 1 set-based SELECT");

        var expectedStudyPath = new ScopePath(world.Sponsor, world.StudyA);
        var expectedBostonSitePath = new ScopePath(world.Sponsor, world.StudyA, "US", world.BostonSite);

        bulkPaths[NodeRef.For(world.CollectionInstanceBoston, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.FormObservationBoston, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.StandaloneObservationBoston, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.VerificationAssertionBoston, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.RecordedFormLockAssertion, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.SiteLockAssertion, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.StudyLockAssertion, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.OpenStudyFinding)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.DecidedStudyFinding, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.SiteCollectionFinding)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.SiteObservationFinding)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.ProtocolAttestation, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.StudyArtifactAttestation, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.ProposalAttestation, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.StudyFindingAttestation, 1)].Should().Be(expectedStudyPath);
        bulkPaths[NodeRef.For(world.EnrollmentAttestation, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.CollectionAttestation, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.RecordedLockAttestation, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.SiteFindingAttestation, 1)].Should().Be(expectedBostonSitePath);
        bulkPaths[NodeRef.For(world.TenantArtifactAttestation, 1)].Should().Be(ScopePath.TenantWide);
        bulkPaths[NodeRef.For(world.UnrecordedLockAttestation, 1)].Should().Be(ScopePath.TenantWide);
        bulkPaths[NodeRef.For(world.StandardAttestation, 1)].Should().Be(ScopePath.TenantWide);
        bulkPaths[NodeRef.For(world.LibraryItemAttestation, 1)].Should().Be(ScopePath.TenantWide);
        bulkPaths[NodeRef.For(world.TaskConfigurationAttestation, 1)].Should().Be(ScopePath.TenantWide);

        // Verify exact parity against single-node PathOfAsync for every distinct NodeRef.
        foreach (var resource in resources.Distinct())
        {
            var expected = await fixture.AuthorityStore.PathOfAsync(world.Tenant, resource, ct);
            bulkPaths[resource].Should().Be(expected, $"bulk path for {resource} must match single-node PathOfAsync");
        }
    }

    /// <summary>
    /// Validates <see cref="IAuthorityStore.StudySitePathsAsync(TenantId, IReadOnlyCollection{NodeRef}, CancellationToken)"/>
    /// and <see cref="IAuthorityStore.FindGrantsAsync"/>: zero connections when no reachable kinds are passed, one
    /// connection lease and two <c>pg_catalog.set_config</c> statements when batching participants and sites or
    /// multi-hop delegation frontiers.
    /// </summary>
    [Fact(DisplayName = "StudySitePathsAndFindGrants_HoldOneLeasePerBulkCall_AndMatchSingleReads")]
    [KernelRule("K-ID-2")]
    [KernelRule("K-AUTH-3")]
    public async Task StudySitePathsAndFindGrantsHoldOneLeasePerBulkCallAndMatchSingleReads()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedAllKindsWorldAsync(ct);

        await using var harness = CreateInstrumentedStore();

        harness.Recorder.Reset();
        var skipped = await harness.AuthorityStore.StudySitePathsAsync(
            world.Tenant, [NodeRef.For(world.StudyA), NodeRef.For(world.BostonSite), NodeRef.For(world.EnrollmentBoston)], ct);
        skipped.Values.Should().AllSatisfy(paths => paths.Should().BeEmpty());
        harness.Recorder.ConnectionsOpened.Should().Be(0);
        harness.Recorder.SetConfigStatements.Should().Be(0);
        harness.Recorder.SelectStatements.Should().Be(0);

        NodeRef[] reachable =
        [
            NodeRef.For(world.ParticipantBoston),
            NodeRef.For(world.ParticipantChicago),
            NodeRef.For(ParticipantId.New()),
            NodeRef.For(world.HospitalBoston),
            NodeRef.For(SiteId.New()),
        ];

        harness.Recorder.Reset();
        var bulkReached = await harness.AuthorityStore.StudySitePathsAsync(world.Tenant, reachable, ct);

        harness.Recorder.ConnectionsOpened.Should().Be(1);
        harness.Recorder.SetConfigStatements.Should().Be(2);
        harness.Recorder.SelectStatements.Should().Be(2, "1 query for participants and 1 query for sites");

        foreach (var node in reachable)
        {
            var single = await fixture.AuthorityStore.StudySitePathsAsync(world.Tenant, node, ct);
            bulkReached[node].Should().BeEquivalentTo(single);
        }

        // Bulk FindGrantsAsync loads bitemporal grant validity periods in 1 query under 1 lease.
        GrantId[] grantIds = [world.TenantGrant, world.OrgGrant, world.StudyGrant, world.CountryGrant, world.SiteGrant, GrantId.New()];
        harness.Recorder.Reset();
        var foundGrants = await harness.AuthorityStore.FindGrantsAsync(world.Tenant, grantIds, ct);

        harness.Recorder.ConnectionsOpened.Should().Be(1);
        harness.Recorder.SetConfigStatements.Should().Be(2);
        harness.Recorder.SelectStatements.Should().Be(1);
        foundGrants.Keys.Should().BeEquivalentTo([world.TenantGrant, world.OrgGrant, world.StudyGrant, world.CountryGrant, world.SiteGrant]);
        foundGrants.Values.Should().OnlyContain(g => g.IsInEffect(fixture.Clock.UtcNow, fixture.Clock.UtcNow));
    }

    /// <summary>
    /// Validates lineage <see cref="IKernelLineage.TraceAsync"/> and <see cref="IKernelLineage.DossierAsync"/> wired to
    /// <see cref="GrantNodeVisibility"/> over <c>KernelAuthorityStore</c>: multiple versions of the same object in a
    /// lineage graph query each distinct unversioned object once and preserve <c>masked:n</c> handle numbering in walk order.
    /// </summary>
    [Fact(DisplayName = "LineageTraceAndDossier_WithGrantVisibility_QueriesEachObjectOnceAndPreservesMaskedHandles")]
    [KernelRule("K-ID-2")]
    public async Task LineageTraceAndDossierWithGrantVisibilityQueriesEachObjectOnceAndPreservesMaskedHandles()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, studyA) = await _scenario.NewStudyAsync(ct);
        var studyB = await NewStudyInTenantAsync(tenant, ct);
        var (boston, _) = await AddStudySiteAsync(tenant, studyA, "US", ct);
        var (berlin, _) = await AddStudySiteAsync(tenant, studyB, "DE", ct);
        var (protocolA, _) = await _scenario.ApprovedAsync(tenant, studyA, ct);
        var (protocolB, _) = await _scenario.ApprovedAsync(tenant, studyB, ct);

        var coordinator = Actor.Person("lineage-crc@visibility.test");
        await IssueGrantAsync(tenant, coordinator, AuthorityScope.StudySite(boston), ct);
        Tick();

        var at = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow);
        var pA1 = NodeRef.For(protocolA.ProtocolVersionId, 1);
        var pA3 = NodeRef.For(protocolA.ProtocolVersionId, 3);
        var pB1 = NodeRef.For(protocolB.ProtocolVersionId, 1);
        var pB3 = NodeRef.For(protocolB.ProtocolVersionId, 3);
        var siteB1 = NodeRef.For(berlin, 1);
        var siteB2 = NodeRef.For(berlin, 2);
        var siteA1 = NodeRef.For(boston, 1);

        // ApprovedAsync already wrote Supersedes edges 1 -> 2 -> 3 on protocolA and protocolB.
        await fixture.SeedAsync(
            Edge(tenant, siteB1, siteB2, LineageEdgeKind.Supersedes, at),
            Edge(tenant, siteB2, pB1, LineageEdgeKind.CollectedBy, at.AddMicroseconds(1)),
            Edge(tenant, pB3, pA1, LineageEdgeKind.DerivedFrom, at.AddMicroseconds(2)),
            Edge(tenant, siteA1, pA1, LineageEdgeKind.CollectedBy, at.AddMicroseconds(3)));
        Tick();

        await using var harness = CreateInstrumentedStore();
        var visibility = new GrantNodeVisibility(harness.AuthorityStore, fixture.Clock);
        var lineage = new KernelLineageStore(harness.TenantContexts, visibility, fixture.Clock);

        harness.Recorder.Reset();
        var trace = await lineage.TraceAsync(tenant, coordinator, new LineageQuery(pA3, LineageDirection.Backward, Depth: 8), ct);

        // 1 walk query in KernelLineageStore + 1 GrantsHeldAsync + 1 PathsOfAsync (grant + study_site + protocol_version = 3 SELECTs).
        harness.Recorder.ConnectionsOpened.Should().Be(3, "1 lineage walk lease + 1 GrantsHeldAsync lease + 1 PathsOfAsync lease");
        harness.Recorder.SetConfigStatements.Should().Be(6);
        harness.Recorder.SelectStatements.Should().Be(5, "1 recursive walk + 1 grants_held + 3 per-kind path queries");

        trace.Nodes.Where(n => n.IsMasked).Select(n => (n.Handle, n.Kind, n.Distance, n.Mask)).Should().Equal(
            ("masked:1", NodeKinds.ProtocolVersion, 3, MaskReason.NotPermitted),
            ("masked:2", NodeKinds.ProtocolVersion, 4, MaskReason.NotPermitted),
            ("masked:3", NodeKinds.ProtocolVersion, 5, MaskReason.NotPermitted),
            ("masked:4", NodeKinds.StudySite, 6, MaskReason.NotPermitted),
            ("masked:5", NodeKinds.StudySite, 7, MaskReason.NotPermitted));

        var serializedTrace = JsonSerializer.Serialize(trace);
        serializedTrace.Should().NotContain(protocolB.ProtocolVersionId.Value.ToString("D"));
        serializedTrace.Should().NotContain(berlin.Value.ToString("D"));

        harness.Recorder.Reset();
        var dossier = await lineage.DossierAsync(tenant, coordinator, pA1, depth: 8, cancellationToken: ct);
        dossier.Backward.Start.Should().Be(pA1.ToString());
        dossier.Backward.Nodes.Where(n => n.IsMasked).Select(n => n.Handle).Should().Equal(
            "masked:1", "masked:2", "masked:3", "masked:4", "masked:5");
        harness.Recorder.ConnectionsOpened.Should().Be(4, "2 dossier walk leases + 1 GrantsHeldAsync lease + 1 PathsOfAsync lease");
        harness.Recorder.SetConfigStatements.Should().Be(8);
    }

    /// <summary>
    /// Runs the 2,000-node visibility masking scenarios against PostgreSQL under Row-Level Security and writes
    /// <c>/tmp/artifacts/kernel-visibility-waterfall-results.json</c>: confirms at most 1 query per present kind, 1
    /// connection lease and 2 <c>pg_catalog.set_config</c> statements per bulk call, agent pass skipped when the
    /// requester masks all 2,000 nodes, and sub-50 ms runtime at 0.5 ms per SQL statement.
    /// </summary>
    [Fact(DisplayName = "MaskAsync_TwoThousandNodesWaterfall_RunsOneQueryPerKindUnderFiftyMilliseconds")]
    [KernelRule("K-ID-2")]
    [KernelRule("K-AI-3")]
    [KernelRule("K-AI-6")]
    public async Task MaskAsyncTwoThousandNodesWaterfallRunsOneQueryPerKindUnderFiftyMilliseconds()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var benchmark = await SeedTwoThousandNodeScenarioAsync(ct);

        await using var harness = CreateInstrumentedStore();
        var visibility = new GrantNodeVisibility(harness.AuthorityStore, fixture.Clock);
        var agentVisibility = new RequesterBoundVisibility(visibility);

        // Warm up EF Core compiled query cache once before measuring.
        _ = await visibility.MaskAsync(benchmark.Tenant, benchmark.Coordinator, benchmark.Nodes[..16], ct);

        // Scenario 1: Site coordinator holding a delegated study-site grant over 2,000 nodes across 18 path-bearing kinds + participants + reference data.
        harness.Recorder.Reset();
        var swCoordinator = Stopwatch.StartNew();
        var coordinatorMasked = await visibility.MaskAsync(benchmark.Tenant, benchmark.Coordinator, benchmark.Nodes, ct);
        swCoordinator.Stop();
        var coordinatorStats = harness.Recorder.Snapshot(swCoordinator.Elapsed.TotalMilliseconds);

        // Scenario 2: Automation agent acting for the coordinator (Study grant ∩ delegated StudySite grant) in one shared path batch.
        harness.Recorder.Reset();
        var swAgentDirect = Stopwatch.StartNew();
        var agentDirectMasked = await visibility.MaskAsync(benchmark.Tenant, benchmark.Principal.Acting, benchmark.Nodes, ct);
        swAgentDirect.Stop();
        var agentDirectStats = harness.Recorder.Snapshot(swAgentDirect.Elapsed.TotalMilliseconds);

        // Scenario 3: RequesterBoundVisibility when the requester holds no active grant (masks all 2,000 nodes -> skips principal.Acting pass).
        harness.Recorder.Reset();
        var swDeniedRequester = Stopwatch.StartNew();
        var deniedMasked = await agentVisibility.MaskAsync(benchmark.UnpermittedPrincipal, benchmark.Nodes, ct);
        swDeniedRequester.Stop();
        var deniedStats = harness.Recorder.Snapshot(swDeniedRequester.Elapsed.TotalMilliseconds);

        // Scenario 4: RequesterBoundVisibility over mixed 2,000 nodes (passes only requester-visible subset to principal.Acting).
        harness.Recorder.Reset();
        var swBoundMixed = Stopwatch.StartNew();
        var boundMixedMasked = await agentVisibility.MaskAsync(benchmark.Principal, benchmark.Nodes, ct);
        swBoundMixed.Stop();
        var boundMixedStats = harness.Recorder.Snapshot(swBoundMixed.Elapsed.TotalMilliseconds);

        benchmark.Nodes.Should().HaveCount(2_000);
        coordinatorMasked.Should().NotBeEmpty().And.HaveCountLessThan(benchmark.Nodes.Count);
        agentDirectMasked.Keys.Should().BeEquivalentTo(coordinatorMasked.Keys);
        boundMixedMasked.Keys.Should().BeEquivalentTo(coordinatorMasked.Keys);
        deniedMasked.Should().HaveCount(benchmark.DistinctInputCount);

        // Coordinator scenario:
        // 1 GrantsHeldAsync (1 lease) + 1 FindGrantsAsync for parent (1 lease) + 1 PathsOfAsync across 18 kinds (1 lease, 18 SELECTs) + 1 StudySitePathsAsync (1 lease, 2 SELECTs) = 4 leases, 8 set_config, 22 SELECTs = 30 wire statements total.
        coordinatorStats.ConnectionsOpened.Should().Be(4);
        coordinatorStats.SetConfigStatements.Should().Be(8);
        coordinatorStats.SelectStatements.Should().Be(22, "1 grants_held + 1 parent_grants + 18 per-kind path queries + 2 study-site reachability queries");
        coordinatorStats.SimulatedAtHalfMsPerStatementMs.Should().BeLessThan(50.0);
        coordinatorMasked.Should().NotContainKey(NodeRef.For(benchmark.AttestedTaskConfiguration, 1), "task_configuration is visible to any tenant grant holder");

        // Agent direct scenario:
        // 2 GrantsHeldAsync + 1 FindGrantsAsync + 1 shared PathsOfAsync (18 SELECTs) + 1 StudySitePathsAsync (2 SELECTs) = 5 leases, 10 set_config, 23 SELECTs = 33 wire statements total.
        agentDirectStats.ConnectionsOpened.Should().Be(5);
        agentDirectStats.SetConfigStatements.Should().Be(10);
        agentDirectStats.SelectStatements.Should().Be(23);
        agentDirectStats.SimulatedAtHalfMsPerStatementMs.Should().BeLessThan(50.0);

        // Denied requester scenario: 1 GrantsHeldAsync only; skips agent pass completely!
        deniedStats.ConnectionsOpened.Should().Be(1);
        deniedStats.SetConfigStatements.Should().Be(2);
        deniedStats.SelectStatements.Should().Be(1);
        deniedStats.SimulatedAtHalfMsPerStatementMs.Should().BeLessThan(50.0);
        deniedMasked.Should().ContainKey(NodeRef.For(benchmark.AttestedTaskConfiguration, 1));

        // Mixed RequesterBoundVisibility scenario: only requester-visible nodes enter principal.Acting, so uncovered participants/sites are not re-queried on the second pass!
        boundMixedStats.SimulatedAtHalfMsPerStatementMs.Should().BeLessThan(50.0);

        var artifactDirectory = Path.GetDirectoryName(WaterfallArtifactPath)!;
        Directory.CreateDirectory(artifactDirectory);
        var report = new
        {
            generatedAtUtc = fixture.Clock.UtcNow,
            nodeCount = benchmark.Nodes.Count,
            distinctNodeRefs = benchmark.DistinctInputCount,
            distinctUnversionedObjects = benchmark.Nodes.Select(n => n.Unversioned()).Distinct().Count(),
            presentPathBearingKinds = 18,
            baselinePreFixWireStatementRange = new { min = 4_004, max = 12_012, minAtHalfMs = 2_002.0, maxAtHalfMs = 6_006.0 },
            scenarios = new
            {
                coordinatorDelegatedStudySite = coordinatorStats,
                automationAgentSharedPathBatch = agentDirectStats,
                requesterBoundAllMaskedSkipsAgentPass = deniedStats,
                requesterBoundMixedSubsetToAgentPass = boundMixedStats,
            },
        };
        var json = JsonSerializer.Serialize(report, IndentedJson);
        await File.WriteAllTextAsync(WaterfallArtifactPath, json, ct);
        output.WriteLine(json);
    }

    /// <summary>
    /// Verifies decoupled evidence classification across all four evidence kinds (<c>observation</c>,
    /// <c>validation_finding</c>, <c>assertion</c>, <c>form_query</c>) on a two-version blinded study:
    /// <list type="bullet">
    /// <item>No SQL query uses <c>regexp_matches</c> or per-row <c>jsonb_agg</c>.</item>
    /// <item>Both <see cref="IAuthorityStore.InspectEvidenceNodesAsync"/> and <see cref="IAuthorityStore.ResolveVisibilityBatchAsync"/>
    /// transfer and parse each protocol version at most once per batch.</item>
    /// <item>Referenced protocol concepts are extracted via <see cref="ProtocolDesignDocument.ExternalReferences"/> and
    /// queried once, resolving blinded concepts added in amended protocol versions.</item>
    /// <item>Sibling observations are deserialized and classified at most once per distinct enrollment per batch, even
    /// when multiple unblinded <c>DatasetRecord</c> findings target forms on the same enrollment.</item>
    /// <item>Batch classifications and <see cref="GrantNodeVisibility.MaskAsync"/> decisions match single-node lookups
    /// across default and custom injected <see cref="IBlindedTreatmentClassifier"/> instances.</item>
    /// </list>
    /// </summary>
    [Fact(DisplayName = "InspectEvidenceAndResolveVisibilityBatch_AllFourEvidenceKinds_DecoupleProtocolConceptAndSiblingQueriesWithExactParity")]
    [KernelRule("K-ID-2")]
    [KernelRule("K-AUTH-4")]
    public async Task InspectEvidenceAndResolveVisibilityBatchAllFourEvidenceKindsDecoupleProtocolConceptAndSiblingQueriesWithExactParity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await SeedFourEvidenceKindsWorldAsync(ct);

        var countingClassifier = new CountingBlindedTreatmentClassifier(BlindedTreatmentClassifier.Instance);
        await using var harness = CreateInstrumentedStore(countingClassifier);

        NodeRef[] findingNodes =
        [
            NodeRef.For(world.DirectBlindedFinding),
            NodeRef.For(world.AmendedConceptBlindedFinding),
            NodeRef.For(world.TargetObsBlindedFinding),
            NodeRef.For(world.CrossFormSiblingBlindedFinding),
            NodeRef.For(world.Enrollment2OpenFindingA),
            NodeRef.For(world.Enrollment2OpenFindingB),
            NodeRef.For(world.Enrollment2OpenFindingC),
            NodeRef.For(world.DesignElementOpenFinding),
        ];

        harness.Recorder.Reset();
        countingClassifier.Reset();
        int findingBatchProtocolReads;
        IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> findingBatch;
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            findingBatch = await harness.AuthorityStore.InspectEvidenceNodesAsync(world.Tenant, findingNodes, ct);
            findingBatchProtocolReads = readScope.Count;
        }

        harness.Recorder.ConnectionsOpened.Should().Be(1);
        harness.Recorder.SetConfigStatements.Should().Be(2);
        findingBatchProtocolReads.Should().Be(2, "each of the two protocol versions in the study is transferred and parsed once");
        harness.Recorder.EfCommands.Should().NotContain(sql => sql.Contains("regexp_matches", StringComparison.OrdinalIgnoreCase));
        harness.Recorder.EfCommands.Should().NotContain(sql => sql.Contains("jsonb_agg", StringComparison.OrdinalIgnoreCase));
        harness.Recorder.EfCommands.Count(sql => sql.Contains("protocol_versions", StringComparison.Ordinal)).Should().Be(1);
        harness.Recorder.EfCommands.Count(sql => sql.Contains("enrollmentIds", StringComparison.Ordinal)).Should().Be(1);

        // Enrollment 2 has 3 unblinded DatasetRecord findings and 2 sibling form observations (sbp and code_note);
        // each sibling observation must be classified at most once per distinct enrollment per batch.
        countingClassifier.ObservationCallsForItem("code_note").Should().Be(
            1,
            "Enrollment 2's sibling observation must be deserialized and classified once across all 3 findings on Enrollment 2");
        countingClassifier.ObservationCallsForItem("aux_note").Should().Be(
            1,
            "Enrollment 1's masked sibling observation is deserialized and classified once");

        findingBatch[NodeRef.For(world.DirectBlindedFinding)].IsBlinded.Should().BeTrue();
        findingBatch[NodeRef.For(world.AmendedConceptBlindedFinding)].IsBlinded.Should().BeTrue(
            "ProtocolDesignDocument.ExternalReferences extracts concepts from amended protocol v2 without SQL regex");
        findingBatch[NodeRef.For(world.TargetObsBlindedFinding)].IsBlinded.Should().BeTrue();
        findingBatch[NodeRef.For(world.CrossFormSiblingBlindedFinding)].IsBlinded.Should().BeTrue(
            "Cross-form reference to blinded sibling observation on Enrollment 1 must classify as blinded");
        findingBatch[NodeRef.For(world.Enrollment2OpenFindingA)].IsBlinded.Should().BeFalse();
        findingBatch[NodeRef.For(world.Enrollment2OpenFindingB)].IsBlinded.Should().BeFalse();
        findingBatch[NodeRef.For(world.Enrollment2OpenFindingC)].IsBlinded.Should().BeFalse();
        findingBatch[NodeRef.For(world.DesignElementOpenFinding)].IsBlinded.Should().BeFalse();

        // Mixed batch spanning all 4 evidence kinds + versioned duplicates + study node.
        NodeRef[] allEvidenceNodes =
        [
            NodeRef.For(world.VitalsObsEnrollment1),
            NodeRef.For(world.VitalsObsEnrollment1, 1),
            NodeRef.For(world.DispenseBlindedObsEnrollment1),
            NodeRef.For(world.StandaloneBlindedObsEnrollment1),
            NodeRef.For(world.VitalsObsEnrollment2),
            NodeRef.For(world.CustomTagObsEnrollment2),
            .. findingNodes,
            NodeRef.For(world.AssertionOnBlindedObs, 1),
            NodeRef.For(world.ChildAssertionOnBlindedAssertion, 1),
            NodeRef.For(world.AssertionOnOpenObs, 1),
            NodeRef.For(world.AssertionOnCustomTagObs, 1),
            new(NodeKinds.FormQuery, world.BlindedFormQueryId, 1),
            new(NodeKinds.FormQuery, world.OpenFormQueryId, 1),
            new(NodeKinds.FormQuery, world.CustomTagFormQueryId, 1),
        ];

        harness.Recorder.Reset();
        countingClassifier.Reset();
        int mixedInspectProtocolReads;
        IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> mixedInspect;
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            mixedInspect = await harness.AuthorityStore.InspectEvidenceNodesAsync(world.Tenant, allEvidenceNodes, ct);
            mixedInspectProtocolReads = readScope.Count;
        }

        harness.Recorder.ConnectionsOpened.Should().Be(1);
        harness.Recorder.SetConfigStatements.Should().Be(2);
        mixedInspectProtocolReads.Should().Be(2, "observations and findings share the single per-study protocol design load");
        harness.Recorder.EfCommands.Count(sql => sql.Contains("protocol_versions", StringComparison.Ordinal)).Should().Be(1);

        NodeRef[] batchWithNonEvidence = [.. allEvidenceNodes, NodeRef.For(world.LondonVitalsObs), NodeRef.For(world.Study), NodeRef.For(world.BostonSite), NodeRef.For(world.LondonSite)];
        harness.Recorder.Reset();
        int resolveBatchProtocolReads;
        (IReadOnlyDictionary<NodeRef, ScopePath> batchPaths, IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> batchEvidence) resolvedBatch;
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            resolvedBatch = await harness.AuthorityStore.ResolveVisibilityBatchAsync(world.Tenant, batchWithNonEvidence, ct);
            resolveBatchProtocolReads = readScope.Count;
        }

        harness.Recorder.ConnectionsOpened.Should().Be(1);
        harness.Recorder.SetConfigStatements.Should().Be(2);
        resolveBatchProtocolReads.Should().Be(2, "ResolveVisibilityBatchAsync transfers each protocol version of a study at most once");
        harness.Recorder.EfCommands.Count(sql => sql.Contains("protocol_versions", StringComparison.Ordinal)).Should().Be(1);

        // Single-node parity across every node in all 4 evidence kinds.
        foreach (var node in allEvidenceNodes.Distinct())
        {
            var singleInspect = await harness.AuthorityStore.InspectEvidenceNodesAsync(world.Tenant, [node], ct);
            var singlePath = await harness.AuthorityStore.PathOfAsync(world.Tenant, node, ct);
            mixedInspect[node].Should().Be(singleInspect[node], $"InspectEvidenceNodesAsync parity for {node}");
            resolvedBatch.batchEvidence[node.Unversioned()].Should().Be(singleInspect[node], $"ResolveVisibilityBatchAsync evidence parity for {node}");
            resolvedBatch.batchPaths[node].Should().Be(singlePath, $"ResolveVisibilityBatchAsync path parity for {node}");
        }

        mixedInspect[NodeRef.For(world.DispenseBlindedObsEnrollment1)].IsBlinded.Should().BeTrue();
        mixedInspect[NodeRef.For(world.VitalsObsEnrollment1)].IsBlinded.Should().BeFalse();
        mixedInspect[NodeRef.For(world.AssertionOnBlindedObs, 1)].IsBlinded.Should().BeTrue();
        mixedInspect[NodeRef.For(world.ChildAssertionOnBlindedAssertion, 1)].IsBlinded.Should().BeTrue();
        mixedInspect[NodeRef.For(world.AssertionOnOpenObs, 1)].IsBlinded.Should().BeFalse();
        mixedInspect[new NodeRef(NodeKinds.FormQuery, world.BlindedFormQueryId, 1)].IsBlinded.Should().BeTrue();
        mixedInspect[new NodeRef(NodeKinds.FormQuery, world.OpenFormQueryId, 1)].IsBlinded.Should().BeFalse();

        // GrantNodeVisibility.MaskAsync parity for blinded tenant monitor, unblinded site pharmacist, and stranger site reader.
        await using var defaultHarness = CreateInstrumentedStore();
        var visibility = new GrantNodeVisibility(defaultHarness.AuthorityStore, fixture.Clock);

        var monitorMasked = await visibility.MaskAsync(world.Tenant, world.TenantMonitor, batchWithNonEvidence, ct);
        monitorMasked[NodeRef.For(world.DispenseBlindedObsEnrollment1)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.StandaloneBlindedObsEnrollment1)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.DirectBlindedFinding)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.AmendedConceptBlindedFinding)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.TargetObsBlindedFinding)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.CrossFormSiblingBlindedFinding)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.AssertionOnBlindedObs, 1)].Should().Be(MaskReason.Blinded);
        monitorMasked[NodeRef.For(world.ChildAssertionOnBlindedAssertion, 1)].Should().Be(MaskReason.Blinded);
        monitorMasked[new NodeRef(NodeKinds.FormQuery, world.BlindedFormQueryId, 1)].Should().Be(MaskReason.Blinded);
        monitorMasked.Should().NotContainKey(NodeRef.For(world.VitalsObsEnrollment1));
        monitorMasked.Should().NotContainKey(NodeRef.For(world.Enrollment2OpenFindingA));
        monitorMasked.Should().NotContainKey(NodeRef.For(world.AssertionOnOpenObs, 1));
        monitorMasked.Should().NotContainKey(new NodeRef(NodeKinds.FormQuery, world.OpenFormQueryId, 1));

        foreach (var node in batchWithNonEvidence)
        {
            var singleMask = await visibility.MaskAsync(world.Tenant, world.TenantMonitor, [node], ct);
            monitorMasked.TryGetValue(node, out var batchReason).Should().Be(singleMask.TryGetValue(node, out var singleReason));
            if (singleMask.ContainsKey(node))
            {
                batchReason.Should().Be(singleReason);
            }
        }

        var pharmMasked = await visibility.MaskAsync(world.Tenant, world.UnblindedBostonPharmacist, batchWithNonEvidence, ct);
        pharmMasked.Should().NotContainKey(NodeRef.For(world.DispenseBlindedObsEnrollment1));
        pharmMasked.Should().NotContainKey(NodeRef.For(world.AmendedConceptBlindedFinding));
        pharmMasked.Should().NotContainKey(NodeRef.For(world.AssertionOnBlindedObs, 1));
        pharmMasked.Should().NotContainKey(new NodeRef(NodeKinds.FormQuery, world.BlindedFormQueryId, 1));
        pharmMasked.Should().NotContainKey(NodeRef.For(world.DesignElementOpenFinding));
        pharmMasked[NodeRef.For(world.LondonVitalsObs)].Should().Be(
            MaskReason.NotPermitted,
            "evidence at another StudySite is masked with NotPermitted for a single-site Boston pharmacist");
        pharmMasked[NodeRef.For(world.LondonSite)].Should().Be(MaskReason.NotPermitted);

        // Requirement 6: custom injected IBlindedTreatmentClassifier governs all 4 evidence kinds even when SQL prefilter would say open.
        var customClassifier = new CustomMarkerBlindedClassifier("SENTINEL_CUSTOM_BLIND");
        await using var customHarness = CreateInstrumentedStore(customClassifier);
        var customVisibility = new GrantNodeVisibility(customHarness.AuthorityStore, fixture.Clock);

        var customMaskedForSiteCoordinator = await customVisibility.MaskAsync(world.Tenant, world.BlindedBostonCoordinator, batchWithNonEvidence, ct);
        customMaskedForSiteCoordinator[NodeRef.For(world.CustomTagObsEnrollment2)].Should().Be(MaskReason.Blinded);
        customMaskedForSiteCoordinator[NodeRef.For(world.Enrollment2OpenFindingC)].Should().Be(
            MaskReason.Blinded,
            "custom classifier marks item.code_note blinded, which propagates to sibling DatasetRecord finding");
        customMaskedForSiteCoordinator[NodeRef.For(world.AssertionOnCustomTagObs, 1)].Should().Be(MaskReason.Blinded);
        customMaskedForSiteCoordinator[new NodeRef(NodeKinds.FormQuery, world.CustomTagFormQueryId, 1)].Should().Be(MaskReason.Blinded);
    }

    /// <summary>
    /// Benchmarks <see cref="IAuthorityStore.InspectEvidenceNodesAsync"/> against a realistic &gt;460.7 KB protocol in
    /// PostgreSQL 18: 5,000 observations classify in under 300 ms with under 5 MB of LOH allocation and zero Gen2
    /// collections, and 200 validation findings classify in under 300 ms with under 5 MB of LOH allocation.
    /// </summary>
    [Fact(DisplayName = "InspectEvidenceNodesAsync_FiveThousandObservationsAndTwoHundredFindingsOnLargeProtocol_MeetsLatencyAndLohBudgets")]
    [KernelRule("K-ID-2")]
    [KernelRule("K-AUTH-4")]
    public async Task InspectEvidenceNodesAsyncFiveThousandObservationsAndTwoHundredFindingsOnLargeProtocolMeetsLatencyAndLohBudgets()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var perf = await SeedLargeProtocolBenchmarkAsync(ct);

        perf.ProtocolUtf8Bytes.Should().BeGreaterThan(460_700, "protocol payload matches or exceeds the 460.7 KB production reproduction");

        await using var harness = CreateInstrumentedStore();

        // Warm up JIT, Npgsql connection pool, and JSON metadata caches on a small slice.
        _ = await harness.AuthorityStore.InspectEvidenceNodesAsync(perf.Tenant, perf.ObservationNodes[..16], ct);
        _ = await harness.AuthorityStore.InspectEvidenceNodesAsync(perf.Tenant, perf.FindingNodes[..8], ct);

        harness.Recorder.Reset();
        long obsLohBytes;
        int obsGen2Delta;
        double obsElapsedMs;
        int obsProtocolReads;
        IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> obsClassified;
        using (var loh = new LohAllocationRecorder())
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            var gen2Before = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            obsClassified = await harness.AuthorityStore.InspectEvidenceNodesAsync(perf.Tenant, perf.ObservationNodes, ct);
            sw.Stop();
            obsGen2Delta = GC.CollectionCount(2) - gen2Before;
            obsElapsedMs = sw.Elapsed.TotalMilliseconds;
            obsProtocolReads = readScope.Count;
            obsLohBytes = loh.AllocatedLohBytes;
        }

        var obsWire = harness.Recorder.Snapshot(obsElapsedMs);
        var obsLohMb = Math.Round(obsLohBytes / (1024.0 * 1024.0), 3);

        harness.Recorder.Reset();
        long findingLohBytes;
        int findingGen2Delta;
        double findingElapsedMs;
        int findingProtocolReads;
        IReadOnlyDictionary<NodeRef, EvidenceNodeClassification> findingsClassified;
        using (var loh = new LohAllocationRecorder())
        using (var readScope = ProtocolDesignDocument.MeasureReads())
        {
            var gen2Before = GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();
            findingsClassified = await harness.AuthorityStore.InspectEvidenceNodesAsync(perf.Tenant, perf.FindingNodes, ct);
            sw.Stop();
            findingGen2Delta = GC.CollectionCount(2) - gen2Before;
            findingElapsedMs = sw.Elapsed.TotalMilliseconds;
            findingProtocolReads = readScope.Count;
            findingLohBytes = loh.AllocatedLohBytes;
        }

        var findingWire = harness.Recorder.Snapshot(findingElapsedMs);
        var findingLohMb = Math.Round(findingLohBytes / (1024.0 * 1024.0), 3);

        var artifactDir = Path.GetDirectoryName(EvidenceBlindingArtifactPath)!;
        Directory.CreateDirectory(artifactDir);
        var report = new
        {
            generatedAtUtc = fixture.Clock.UtcNow,
            protocolUtf8Bytes = perf.ProtocolUtf8Bytes,
            protocolKilobytes = Math.Round(perf.ProtocolUtf8Bytes / 1024.0, 2),
            observations5000 = new
            {
                count = obsClassified.Count,
                blindedCount = obsClassified.Values.Count(v => v.IsBlinded),
                elapsedMs = Math.Round(obsElapsedMs, 2),
                lohBytes = obsLohBytes,
                lohMegabytes = obsLohMb,
                gen2Collections = obsGen2Delta,
                protocolReads = obsProtocolReads,
                wire = obsWire,
            },
            validationFindings200 = new
            {
                count = findingsClassified.Count,
                blindedCount = findingsClassified.Values.Count(v => v.IsBlinded),
                elapsedMs = Math.Round(findingElapsedMs, 2),
                lohBytes = findingLohBytes,
                lohMegabytes = findingLohMb,
                gen2Collections = findingGen2Delta,
                protocolReads = findingProtocolReads,
                wire = findingWire,
            },
        };
        var json = JsonSerializer.Serialize(report, IndentedJson);
        await File.WriteAllTextAsync(EvidenceBlindingArtifactPath, json, ct);
        output.WriteLine(json);

        obsClassified.Should().HaveCount(5_000);
        obsClassified.Values.Count(v => v.IsBlinded).Should().Be(perf.ExpectedBlindedObservations);
        obsProtocolReads.Should().Be(1, "the 460.7+ KB protocol is transferred and parsed once across 5,000 observations");
        obsWire.ConnectionsOpened.Should().Be(1);
        obsWire.SetConfigStatements.Should().Be(2);
        obsGen2Delta.Should().Be(0, "5,000 observations must not trigger blocking Gen2 collections");
        obsLohMb.Should().BeLessThan(5.0, "5,000 observations must allocate under 5 MB on the LOH");
        obsElapsedMs.Should().BeLessThan(300.0, "5,000 observations must classify in under 300 ms");

        findingsClassified.Should().HaveCount(200);
        findingsClassified.Values.Count(v => v.IsBlinded).Should().Be(perf.ExpectedBlindedFindings);
        findingProtocolReads.Should().Be(1, "the 460.7+ KB protocol is transferred and parsed once across 200 findings");
        findingWire.ConnectionsOpened.Should().Be(1);
        findingWire.SetConfigStatements.Should().Be(2);
        findingLohMb.Should().BeLessThan(5.0, "200 findings must allocate under 5 MB on the LOH");
        findingElapsedMs.Should().BeLessThan(300.0, "200 findings must classify in under 300 ms");
    }

    private InstrumentedHarness CreateInstrumentedStore(IBlindedTreatmentClassifier? blindingClassifier = null)
    {
        var recorder = new SqlWireRecorder();
        var loggerFactory = new LoggerFactory([recorder]);
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(fixture.ConnectionString!);
        dataSourceBuilder.UseLoggerFactory(loggerFactory);
        var dataSource = dataSourceBuilder.Build();

        var optionsBuilder = new DbContextOptionsBuilder<KernelDbContext>();
        optionsBuilder.UseNpgsql(
            dataSource,
            npgsql => npgsql.MigrationsHistoryTable(KernelDatabase.MigrationsHistoryTable, KernelDbContext.Schema));
        optionsBuilder.AddInterceptors(new KernelTenantConnectionInterceptor(), recorder);

        var contextFactory = new SimpleDbContextFactory(optionsBuilder.Options);
        var tenantContexts = new KernelTenantDbContextFactory(contextFactory);
        var authorityStore = new KernelAuthorityStore(tenantContexts, blindingClassifier);
        return new InstrumentedHarness(authorityStore, tenantContexts, recorder, dataSource, loggerFactory);
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task<GrantId> IssueGrantAsync(
        TenantId tenant, Actor grantee, AuthorityScope scope, CancellationToken ct, bool withGrantOption = false)
    {
        var grant = GrantId.New();
        await _scenario.OkAsync(
            new IssueGrant(tenant, grant, grantee, "observation.record", null, scope, fixture.Clock.UtcNow, null, withGrantOption, KernelProtocolScenario.Key(), "Appointed"),
            ct);
        return grant;
    }

    private async Task<AllKindsWorld> SeedAllKindsWorldAsync(CancellationToken ct)
    {
        var signed = await new KernelSignedTargetsScenario(fixture).CreateAsync(ct);
        var tenant = signed.Tenant;
        var studyA = signed.Study;
        var bostonSite = signed.StudySite;
        var enrollmentBoston = signed.Enrollment;
        var protocolVersionA = signed.ProtocolVersion;
        var studyB = await NewStudyInTenantAsync(tenant, ct);

        OrganizationId sponsor;
        ParticipantId participantBoston;
        SiteId hospitalBoston;
        Guid protocolElementAId;
        EffectivityId studyEffectivity;
        await using (var db = fixture.CreateContext(tenant))
        {
            sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == studyA).Select(s => s.SponsorId).SingleAsync(ct);
            participantBoston = await db.Enrollments.AsNoTracking().Where(e => e.Id == enrollmentBoston).Select(e => e.ParticipantId).SingleAsync(ct);
            hospitalBoston = await db.StudySites.AsNoTracking().Where(ss => ss.Id == bostonSite).Select(ss => ss.SiteId).SingleAsync(ct);
            protocolElementAId = await db.LineageNodes.AsNoTracking()
                .Where(n => n.Kind == NodeKinds.ProtocolElement)
                .Select(n => n.NodeId)
                .FirstAsync(ct);
            studyEffectivity = await db.Effectivities.AsNoTracking()
                .Where(e => e.ProtocolVersionId == protocolVersionA)
                .Select(e => e.Id)
                .SingleAsync(ct);
        }

        var (chicagoSite, _) = await AddStudySiteAsync(tenant, studyA, "US", ct);
        var (berlinSite, hospitalBerlin) = await AddStudySiteAsync(tenant, studyB, "DE", ct, externalInstitution: true);
        var (participantChicago, _) = await ScreenAsync(tenant, chicagoSite, "BATCH-002", ct);

        var countryEffectivity = EffectivityId.New();
        await _scenario.OkAsync(
            new DeclareEffectivity(tenant, countryEffectivity, protocolVersionA, EffectivityScope.InCountry("US"), Tick(), KernelProtocolScenario.Key(), "US filing"),
            ct);
        var siteEffectivity = EffectivityId.New();
        await _scenario.OkAsync(
            new DeclareEffectivity(tenant, siteEffectivity, protocolVersionA, EffectivityScope.At(bostonSite), Tick(), KernelProtocolScenario.Key(), "Boston IRB"),
            ct);

        var reader = Actor.Person("kinds-reader@visibility.test");
        var tenantGrant = await IssueGrantAsync(tenant, reader, AuthorityScope.Tenant(), ct);
        var orgGrant = await IssueGrantAsync(tenant, reader, AuthorityScope.Organization(sponsor), ct);
        var studyGrant = await IssueGrantAsync(tenant, reader, AuthorityScope.Study(studyA), ct);
        var countryGrant = await IssueGrantAsync(tenant, reader, AuthorityScope.StudyCountry(studyA, "US"), ct);
        var siteGrant = await IssueGrantAsync(tenant, reader, AuthorityScope.StudySite(bostonSite), ct);

        var orgRequirement = QualificationRequirementId.New();
        await _scenario.OkAsync(
            new DefineQualificationRequirement(tenant, orgRequirement, "observation.record", "gcp", null, AuthorityScope.Organization(sponsor), Tick(), null, KernelProtocolScenario.Key(), "Org GCP"),
            ct);
        var studyRequirement = QualificationRequirementId.New();
        await _scenario.OkAsync(
            new DefineQualificationRequirement(tenant, studyRequirement, "observation.record", "study_training", protocolVersionA, AuthorityScope.Study(studyA), Tick(), null, KernelProtocolScenario.Key(), "Study training"),
            ct);
        var countryRequirement = QualificationRequirementId.New();
        await _scenario.OkAsync(
            new DefineQualificationRequirement(tenant, countryRequirement, "observation.record", "us_reg", null, AuthorityScope.StudyCountry(studyA, "US"), Tick(), null, KernelProtocolScenario.Key(), "US reg"),
            ct);
        var siteRequirement = QualificationRequirementId.New();
        await _scenario.OkAsync(
            new DefineQualificationRequirement(tenant, siteRequirement, "observation.record", "site_sop", null, AuthorityScope.StudySite(bostonSite), Tick(), null, KernelProtocolScenario.Key(), "Site SOP"),
            ct);

        // Site-level and study-level lock assertions:
        var observations = new KernelObservationScenario(fixture);
        var siteLock = await observations.LockAsync(tenant, studyA, NodeRef.For(bostonSite), ct, LockKind.Frozen);
        await _scenario.OkAsync(siteLock, ct);
        var studyLock = await observations.LockAsync(tenant, studyA, NodeRef.For(studyA), ct, LockKind.Frozen);
        await _scenario.OkAsync(studyLock, ct);

        // Open study finding (R2 from KernelSignedTargetsScenario), plus site-level findings on collection_instance and observation:
        var openStudyFinding = (await fixture.Findings.ListAsync(tenant, new FindingQuery(studyA), ct)).Findings
            .Single(f => f.RuleId == "R2").Id;

        var collectionFindingTarget = new FindingTarget(FindingTargetKind.DatasetRecord, "VITALS", signed.CollectionInstance.Value.ToString("D"), KernelCollectionScenario.Systolic);
        var observationFindingTarget = new FindingTarget(FindingTargetKind.Observation, "VITALS", NodeRef.For(signed.StandaloneObservation).ToString());
        var siteRunId = ValidationRunId.New();
        var siteRunAt = Tick();
        await _scenario.OkAsync(
            new RecordValidationRun(
                tenant,
                siteRunId,
                studyA,
                ValidationStage.Collection,
                [signed.LibraryItem],
                new DateOnly(2026, 9, 30),
                null,
                null,
                null,
                [],
                ["VITALS"],
                siteRunAt.AddSeconds(-1),
                siteRunAt,
                [
                    new ValidationDetection(
                        "signed-targets-pack",
                        "R1",
                        1,
                        ValidationSeverity.Error,
                        collectionFindingTarget,
                        "Systolic check on collection instance.",
                        null,
                        [new ObservedVariable("SYS", "90")]),
                    new ValidationDetection(
                        "signed-targets-pack",
                        "R2",
                        1,
                        ValidationSeverity.Warning,
                        observationFindingTarget,
                        "Heart rate check on standalone observation.",
                        null,
                        [new ObservedVariable("HR", "68")]),
                ],
                KernelProtocolScenario.Key(),
                "Site findings run"),
            ct);

        var collectionFindings = (await fixture.Findings.ListAsync(tenant, new FindingQuery(studyA) { Stage = ValidationStage.Collection }, ct)).Findings;
        var siteCollectionFinding = collectionFindings.Single(f => f.Target.Kind == FindingTargetKind.DatasetRecord).Id;
        var siteObservationFinding = collectionFindings.Single(f => f.Target.Kind == FindingTargetKind.Observation).Id;
        const string SiteRationale = "Confirmed reading at Boston site.";
        var siteFindingAttestation = await _scenario.OkAsync(
            _scenario.AttestCommand(
                tenant,
                AttestationMeaning.Responsibility,
                new AttestationTarget(
                    NodeKinds.ValidationFinding,
                    siteCollectionFinding.Value,
                    FindingDecisionContent.Hash(siteCollectionFinding, siteRunId, FindingStatus.Accepted, SiteRationale))),
            ct);
        await _scenario.OkAsync(new AcceptFinding(tenant, siteCollectionFinding, SiteRationale, siteFindingAttestation, KernelProtocolScenario.Key(), "Accept site finding"), ct);

        Tick();

        return new AllKindsWorld(
            tenant,
            sponsor,
            hospitalBoston.Value,
            hospitalBerlin.Value,
            studyA,
            studyB,
            bostonSite,
            berlinSite,
            participantBoston,
            participantChicago,
            enrollmentBoston,
            protocolVersionA,
            protocolElementAId,
            signed.StudyArtifact,
            signed.TenantArtifact,
            signed.StudyArtifactVersion,
            signed.TenantArtifactVersion,
            tenantGrant,
            orgGrant,
            studyGrant,
            countryGrant,
            siteGrant,
            orgRequirement,
            studyRequirement,
            countryRequirement,
            siteRequirement,
            studyEffectivity,
            countryEffectivity,
            siteEffectivity,
            signed.Proposal,
            signed.CollectionInstance,
            signed.FormObservation,
            signed.StandaloneObservation,
            signed.VerificationAssertion,
            signed.RecordedLockAssertion,
            siteLock.AssertionId,
            studyLock.AssertionId,
            openStudyFinding,
            signed.ValidationFinding,
            siteCollectionFinding,
            siteObservationFinding,
            signed.ProtocolVersionAttestation,
            signed.EnrollmentAttestation,
            signed.StudyArtifactAttestation,
            signed.TenantArtifactAttestation,
            signed.CollectionInstanceAttestation,
            signed.RecordedLockAttestation,
            signed.UnrecordedLockAttestation,
            signed.ProposalAttestation,
            signed.ValidationFindingAttestation,
            siteFindingAttestation,
            signed.StandardVersionAttestation,
            signed.LibraryItemAttestation,
            signed.TaskConfigurationAttestation,
            signed.AttestedTaskConfiguration);
    }

    private async Task<WaterfallScenario> SeedTwoThousandNodeScenarioAsync(CancellationToken ct)
    {
        var world = await SeedAllKindsWorldAsync(ct);

        var pi = Actor.Person("waterfall-pi@site.test");
        var coordinator = Actor.Person("waterfall-crc@site.test");
        var stranger = Actor.Person("waterfall-stranger@site.test");
        var agent = Actor.AutomationAgent("waterfall-agent");

        var piGrant = await IssueGrantAsync(world.Tenant, pi, AuthorityScope.Study(world.StudyA), ct, withGrantOption: true);
        Tick();

        var crcDelegation = GrantId.New();
        var previousActor = fixture.CurrentActor.Actor;
        try
        {
            fixture.CurrentActor.Actor = pi;
            var delegated = await fixture.Sender.SendAsync(
                new DelegateGrant(
                    world.Tenant,
                    crcDelegation,
                    piGrant,
                    coordinator,
                    "observation.record",
                    null,
                    AuthorityScope.StudySite(world.BostonSite),
                    fixture.Clock.UtcNow,
                    null,
                    false,
                    KernelProtocolScenario.Key(),
                    "Delegated site visibility"),
                cancellationToken: ct);
            delegated.IsSuccess.Should().BeTrue(delegated.Error?.Message);
        }
        finally
        {
            fixture.CurrentActor.Actor = previousActor;
        }

        await IssueGrantAsync(world.Tenant, agent, AuthorityScope.Study(world.StudyA), ct);
        Tick();

        // Bulk-seed 600 participants and 600 enrollments split across Boston (covered) and Berlin (uncovered) so the
        // 2,000-node input exercises multi-hundred-element ANY(@ids) / IN (...) arrays under RLS.
        var now = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow);
        var seededEntities = new List<object>(1_200);
        var bulkParticipants = new List<ParticipantId>(600);
        var bulkEnrollments = new List<EnrollmentId>(600);
        for (var i = 0; i < 600; i++)
        {
            var participantId = ParticipantId.New();
            var enrollmentId = EnrollmentId.New();
            var inBoston = i % 2 == 0;
            var targetStudy = inBoston ? world.StudyA : world.StudyB;
            var targetSite = inBoston ? world.BostonSite : world.BerlinSite;
            SubjectCode.TryFrom($"WF-{i:D4}", out var code);

            seededEntities.Add(Participant.Register(participantId, world.Tenant));
            seededEntities.Add(Enrollment.Screen(enrollmentId, world.Tenant, participantId, targetStudy, targetSite, code, now, now).Value!);
            bulkParticipants.Add(participantId);
            bulkEnrollments.Add(enrollmentId);
        }

        await fixture.SeedAsync([.. seededEntities]);

        var nodes = new List<NodeRef>(2_000)
        {
            NodeRef.For(world.Sponsor),
            new(NodeKinds.Site, world.HospitalBostonId),
            new(NodeKinds.Site, world.HospitalBerlinId),
            NodeRef.For(world.StudyA, 1),
            NodeRef.For(world.StudyA, 2),
            NodeRef.For(world.StudyB, 1),
            NodeRef.For(world.BostonSite, 1),
            NodeRef.For(world.BerlinSite, 1),
            NodeRef.For(world.ProtocolVersionA, 1),
            NodeRef.For(world.ProtocolVersionA, 2),
            new(NodeKinds.ProtocolElement, world.ProtocolElementAId, 1),
            new(NodeKinds.ProtocolElement, world.ProtocolElementAId, 2),
            NodeRef.For(world.StudyArtifact, 1),
            NodeRef.For(world.StudyArtifactVersion, 1),
            NodeRef.For(world.SiteGrant),
            NodeRef.For(world.SiteRequirement),
            NodeRef.For(world.CountryEffectivity),
            NodeRef.For(world.SiteEffectivity),
            NodeRef.For(world.ProposalA, 1),
            NodeRef.For(world.CollectionInstanceBoston, 1),
            NodeRef.For(world.FormObservationBoston, 1),
            NodeRef.For(world.VerificationAssertionBoston, 1),
            NodeRef.For(world.SiteLockAssertion, 1),
            NodeRef.For(world.StudyLockAssertion, 1),
            NodeRef.For(world.OpenStudyFinding),
            NodeRef.For(world.SiteCollectionFinding),
            NodeRef.For(world.CollectionAttestation, 1),
            NodeRef.For(world.ProtocolAttestation, 1),
            NodeRef.For(world.AttestedTaskConfiguration, 1),
        };

        foreach (var enrollmentId in bulkEnrollments)
        {
            nodes.Add(NodeRef.For(enrollmentId, 1));
            nodes.Add(NodeRef.For(enrollmentId, 2));
        }

        foreach (var participantId in bulkParticipants)
        {
            nodes.Add(NodeRef.For(participantId, 1));
        }

        while (nodes.Count < 2_000)
        {
            nodes.Add(NodeRef.For(ConceptId.New(), 1));
        }

        var runId = Guid.CreateVersion7();
        return new WaterfallScenario(
            world.Tenant,
            coordinator,
            new AgentPrincipal(world.Tenant, agent, coordinator, runId),
            new AgentPrincipal(world.Tenant, agent, stranger, runId),
            nodes,
            nodes.Distinct().Count(),
            world.AttestedTaskConfiguration);
    }

    private async Task<(ParticipantId Participant, EnrollmentId Enrollment)> ScreenAsync(
        TenantId tenant, StudySiteId studySite, string code, CancellationToken ct)
    {
        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Registered"), ct);
        SubjectCode.TryFrom(code, out var subjectCode);
        var enrollment = await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subjectCode, Tick(), KernelProtocolScenario.Key(), "Screening"),
            ct);
        return (participant, enrollment.EnrollmentId);
    }

    private async Task<StudyId> NewStudyInTenantAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{suffix}", "Second study", "Phase 2", KernelPostgresFixture.ActorId, "Seeded");
        await fixture.SeedAsync(study);
        return study.Id;
    }

    private async Task<(StudySiteId StudySite, SiteId Site)> AddStudySiteAsync(
        TenantId tenant, StudyId study, string country, CancellationToken ct, bool externalInstitution = false)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var institutionId = sponsor;
        Organization? externalOrg = null;
        if (externalInstitution)
        {
            var orgKind = await db.Organizations.AsNoTracking().Where(o => o.Id == sponsor).Select(o => o.Kind).SingleAsync(ct);
            institutionId = OrganizationId.New();
            externalOrg = new Organization(institutionId, tenant, $"ORG-{suffix}", "External Medical Center", orgKind, KernelPostgresFixture.ActorId, "Seeded");
        }

        var site = new Site(SiteId.New(), tenant, institutionId, $"SITE-{suffix}", "Hospital", country, KernelPostgresFixture.ActorId, "Seeded");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded");
        if (externalOrg is not null)
        {
            await fixture.SeedAsync(externalOrg, site, studySite);
        }
        else
        {
            await fixture.SeedAsync(site, studySite);
        }

        return (studySite.Id, site.Id);
    }

    private async Task<FourEvidenceKindsWorld> SeedFourEvidenceKindsWorldAsync(CancellationToken ct)
    {
        var forms = new KernelCollectionScenario(fixture);
        var (tenant, study) = await forms.Protocol.NewStudyAsync(ct);

        var sbpConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Arterial systolic pressure.", KernelProtocolScenario.Key(), "Define SBP"),
            ct);
        var armConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Randomization Arm", "Blinded treatment allocation.", KernelProtocolScenario.Key(), "Define Arm"),
            ct);
        var amendedBlindedConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Investigational Product Allocation", "Masked kit assignment.", KernelProtocolScenario.Key(), "Define IP"),
            ct);
        var customTagConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Clinic Visit Note", "Routine clinic note.", KernelProtocolScenario.Key(), "Define Note"),
            ct);

        var designV1 = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep_treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(KernelCollectionScenario.VisitId, "Day 1 visit", "ep_treatment", [ActivityLocation.Site], ["cs_vitals", "cs_dispense", "cs_ancillary"], new ActivityTiming(1, 0, 0)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs_vitals",
                    "Vital signs",
                    [
                        new CollectionItem("sbp", sbpConcept, "mm[Hg]", Required: true),
                        new CollectionItem("code_note", customTagConcept, ValueKind: ObservationValueKind.Text),
                    ]),
                new CollectionSpec(
                    "cs_dispense",
                    "Randomization Dispense",
                    [
                        new CollectionItem("arm", armConcept, ValueKind: ObservationValueKind.Text, Required: true),
                    ]),
                new CollectionSpec(
                    "cs_ancillary",
                    "Ancillary Log",
                    [
                        new CollectionItem("aux_note", customTagConcept, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };
        var v1Json = ProtocolDesignDocument.Write(designV1);
        ProtocolDesignDocument.Read(v1Json).IsValid.Should().BeTrue();

        var version1 = ProtocolVersionId.New();
        (await forms.Protocol.CreateDraftAsync(tenant, study, version1, ct, v1Json)).IsSuccess.Should().BeTrue();
        var v1Review = await forms.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version1, KernelProtocolScenario.Key(), "Submit v1"), ct);
        var v1Sig = await forms.Protocol.OkAsync(forms.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(v1Review)), ct);
        await forms.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version1, v1Sig, KernelProtocolScenario.Key(), "Approve v1"), ct);

        var eff = EffectivityId.New();
        await forms.Protocol.OkAsync(new DeclareEffectivity(tenant, eff, version1, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Declare v1"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.CreateVersion7():N}");
        var ethics = ArtifactVersionId.New();
        await forms.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethics,
                ArtifactKind.EthicsApproval,
                study,
                "IRB Approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://irb/approval"),
                KernelProtocolScenario.Key(),
                "IRB approval"),
            ct);
        await forms.Protocol.OkAsync(new ChangeEffectivityStatus(tenant, eff, EffectivityStatus.Effective, Tick(), [ethics], KernelProtocolScenario.Key(), "Effective"), ct);

        // Fork protocol v2 adding a neutral-named spec/item whose Concept metadata is blinded ("Investigational Product Allocation").
        var designV2 = designV1 with
        {
            PlannedActivities =
            [
                new PlannedActivity(KernelCollectionScenario.VisitId, "Day 1 visit", "ep_treatment", [ActivityLocation.Site], ["cs_vitals", "cs_dispense", "cs_ancillary", "cs_pharmacy_check"], new ActivityTiming(1, 0, 0)),
            ],
            CollectionSpecs =
            [
                .. designV1.CollectionSpecs,
                new CollectionSpec(
                    "cs_pharmacy_check",
                    "Pharmacy Check",
                    [
                        new CollectionItem("code", amendedBlindedConcept, ValueKind: ObservationValueKind.Text),
                    ]),
            ],
        };
        var v2Json = ProtocolDesignDocument.Write(designV2);
        ProtocolDesignDocument.Read(v2Json).IsValid.Should().BeTrue();
        var version2 = ProtocolVersionId.New();
        await forms.Protocol.OkAsync(
            new ForkProtocolVersion(
                tenant,
                version1,
                version2,
                new ProtocolAmendment(
                    "AMD-1",
                    "Add pharmacy check specification",
                    AmendmentClassification.NonSubstantial,
                    "Add pharmacy check collection specification.",
                    ["CollectionSpecs"],
                    TransitionPolicy.Automatic),
                KernelProtocolScenario.Key(),
                "Fork v2"),
            ct);
        await forms.Protocol.OkAsync(
            new ReviseProtocolDraft(tenant, version2, v2Json, null, KernelProtocolScenario.Key(), "Revise v2"),
            ct);

        var (bostonSite, _) = await AddStudySiteAsync(tenant, study, "US", ct);
        var (londonSite, _) = await AddStudySiteAsync(tenant, study, "GB", ct);
        var (_, enrollment1) = await ScreenAsync(tenant, bostonSite, "EV-001", ct);
        var (_, enrollment2) = await ScreenAsync(tenant, bostonSite, "EV-002", ct);
        var (_, londonEnrollment) = await ScreenAsync(tenant, londonSite, "EV-LON-001", ct);
        var londonVitalsObs = (await forms.Protocol.OkAsync(
            new RecordObservation(tenant, ObservationId.New(), londonEnrollment, sbpConcept, KernelCollectionScenario.Mmhg(122), Tick().AddSeconds(-2), KernelProtocolScenario.Key(), "London vitals"),
            ct)).ObservationId;

        var subj1 = new KernelObservationScenario.Subject(tenant, study, bostonSite, enrollment1);
        var subj2 = new KernelObservationScenario.Subject(tenant, study, bostonSite, enrollment2);

        var vitalsForm1 = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subj1, CollectionInstanceId.New(), Tick(), "cs_vitals"), ct)).CollectionInstanceId;
        var dispenseForm1 = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subj1, CollectionInstanceId.New(), Tick(), "cs_dispense"), ct)).CollectionInstanceId;
        var ancillaryForm1 = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subj1, CollectionInstanceId.New(), Tick(), "cs_ancillary"), ct)).CollectionInstanceId;
        var vitalsForm2 = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subj2, CollectionInstanceId.New(), Tick(), "cs_vitals"), ct)).CollectionInstanceId;

        var savedVitals1 = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                new KernelCollectionScenario.Form(subj1, version1, vitalsForm1),
                KernelCollectionScenario.Value("sbp", KernelCollectionScenario.Mmhg(124))),
            ct);
        var vitalsObs1 = savedVitals1.Values.Single().ObservationId;

        var savedDispense1 = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                new KernelCollectionScenario.Form(subj1, version1, dispenseForm1),
                new CollectedValue("arm", new TextValue("Active Arm A"), 0)),
            ct);
        var dispenseObs1 = savedDispense1.Values.Single().ObservationId;

        await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                new KernelCollectionScenario.Form(subj1, version1, ancillaryForm1),
                new CollectedValue("aux_note", new MissingValue(NullFlavor.Masked), 0)),
            ct);

        var savedVitals2 = await forms.Protocol.OkAsync(
            KernelCollectionScenario.Save(
                new KernelCollectionScenario.Form(subj2, version1, vitalsForm2),
                KernelCollectionScenario.Value("sbp", KernelCollectionScenario.Mmhg(118)),
                new CollectedValue("code_note", new TextValue("SENTINEL_CUSTOM_BLIND"), 0)),
            ct);
        var vitalsObs2 = savedVitals2.Values.Single(v => v.ItemId == "sbp").ObservationId;
        var customTagObs2 = savedVitals2.Values.Single(v => v.ItemId == "code_note").ObservationId;

        var standaloneBlindedObs1 = (await forms.Protocol.OkAsync(
            new RecordObservation(tenant, ObservationId.New(), enrollment1, armConcept, new TextValue("Kit 1042"), Tick().AddSeconds(-1), KernelProtocolScenario.Key(), "Standalone blinded"),
            ct)).ObservationId;

        // Assertions: on blinded obs, child assertion targeting blinded assertion, on open obs, and on custom-tag obs.
        var assertBlinded = (await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment1, dispenseObs1, 1, KernelProtocolScenario.Key(), "Verify blinded"),
            ct)).AssertionId;
        var assertOpen = (await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment1, vitalsObs1, 1, KernelProtocolScenario.Key(), "Verify open"),
            ct)).AssertionId;
        var assertCustomTag = (await forms.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment2, customTagObs2, 1, KernelProtocolScenario.Key(), "Verify custom"),
            ct)).AssertionId;

        Assertion childBlindedAssertion;
        await using (var db = fixture.CreateContext(tenant))
        {
            var parentAssertion = await db.Assertions.AsNoTracking().SingleAsync(a => a.Id == assertBlinded, ct);
            childBlindedAssertion = Assertion.Retract(AssertionId.New(), parentAssertion, null, KernelPostgresFixture.ActorId, Tick());
        }

        // Form queries linked via LineageEdges (GovernedBy enrollment + DerivedFrom target observation).
        var blindedQueryId = Guid.CreateVersion7();
        var openQueryId = Guid.CreateVersion7();
        var customTagQueryId = Guid.CreateVersion7();
        var edgeAt = KernelTimestamps.ToStoredPrecision(Tick());
        await fixture.SeedAsync(
            childBlindedAssertion,
            Edge(tenant, NodeRef.For(enrollment1, 1), new NodeRef(NodeKinds.FormQuery, blindedQueryId, 1), LineageEdgeKind.GovernedBy, edgeAt),
            Edge(tenant, NodeRef.For(dispenseObs1, 1), new NodeRef(NodeKinds.FormQuery, blindedQueryId, 1), LineageEdgeKind.DerivedFrom, edgeAt.AddMicroseconds(1)),
            Edge(tenant, NodeRef.For(enrollment1, 1), new NodeRef(NodeKinds.FormQuery, openQueryId, 1), LineageEdgeKind.GovernedBy, edgeAt.AddMicroseconds(2)),
            Edge(tenant, NodeRef.For(vitalsObs1, 1), new NodeRef(NodeKinds.FormQuery, openQueryId, 1), LineageEdgeKind.DerivedFrom, edgeAt.AddMicroseconds(3)),
            Edge(tenant, NodeRef.For(enrollment2, 1), new NodeRef(NodeKinds.FormQuery, customTagQueryId, 1), LineageEdgeKind.GovernedBy, edgeAt.AddMicroseconds(4)),
            Edge(tenant, NodeRef.For(customTagObs2, 1), new NodeRef(NodeKinds.FormQuery, customTagQueryId, 1), LineageEdgeKind.DerivedFrom, edgeAt.AddMicroseconds(5)));

        // Validation findings across all classification branches.
        var pack = await forms.Protocol.OkAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.RulePack, "ev-parity-pack", "Evidence parity pack", RulePackJson.ToJson(SampleRulePacks.Sample()), [], KernelProtocolScenario.Key(), "Draft pack"),
            ct);
        var packSig = await forms.Protocol.OkAsync(
            forms.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, pack.Id.Value, pack.ContentHash)),
            ct);
        await forms.Protocol.OkAsync(new ApproveLibraryItem(tenant, pack.Id, packSig, KernelProtocolScenario.Key(), "Approve pack"), ct);

        var runAt = Tick();
        await forms.Protocol.OkAsync(
            new RecordValidationRun(
                tenant,
                ValidationRunId.New(),
                study,
                ValidationStage.Collection,
                [pack.Id],
                new DateOnly(2026, 10, 1),
                null,
                null,
                null,
                [],
                ["cs_vitals", "cs_dispense", "cs_ancillary", "cs_pharmacy_check", "PROTOCOL"],
                runAt.AddSeconds(-1),
                runAt,
                [
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_DIRECT_BLIND",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_dispense", dispenseForm1.Value.ToString("D"), "arm"),
                        "Direct blinded check.",
                        null,
                        [new ObservedVariable("arm", "Active Arm A")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_AMENDED_CONCEPT",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_pharmacy_check", vitalsForm2.Value.ToString("D"), "code"),
                        "Amended concept blinded check.",
                        null,
                        [new ObservedVariable("code", "K-99")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_TARGET_OBS",
                        1,
                        ValidationSeverity.Error,
                        new FindingTarget(FindingTargetKind.Observation, "cs_vitals", NodeRef.For(standaloneBlindedObs1).ToString(), "sbp"),
                        "Target observation blinded check.",
                        null,
                        [new ObservedVariable("val", "checked")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_SIBLING_BLIND",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", vitalsForm1.Value.ToString("D"), "sbp"),
                        "Cross-form sibling blinded check.",
                        null,
                        [new ObservedVariable("cs_ancillary.aux_note", "masked")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_ENR2_OPEN_A",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", vitalsForm2.Value.ToString("D"), "sbp"),
                        "Open SBP range check A.",
                        null,
                        [new ObservedVariable("sbp", "118")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_ENR2_OPEN_B",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", vitalsForm2.Value.ToString("D"), "sbp"),
                        "Open SBP range check B.",
                        null,
                        [new ObservedVariable("sbp", "118")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_ENR2_OPEN_C",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", vitalsForm2.Value.ToString("D"), "code_note"),
                        "Open note check C.",
                        null,
                        [new ObservedVariable("code_note", "note")]),
                    new ValidationDetection(
                        "ev-parity-pack",
                        "R_DESIGN_OPEN",
                        1,
                        ValidationSeverity.Warning,
                        new FindingTarget(FindingTargetKind.DesignElement, "PROTOCOL", NodeRef.For(version1).ToString()),
                        "Study design check.",
                        null,
                        []),
                ],
                KernelProtocolScenario.Key(),
                "Record parity findings"),
            ct);

        var findingsByRule = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Take = 50 }, ct))
            .Findings
            .ToDictionary(f => f.RuleId, f => f.Id, StringComparer.Ordinal);

        var tenantMonitor = Actor.Person("tenant-monitor@ev-parity.test");
        var unblindedPharm = Actor.Person("boston-pharm@ev-parity.test");
        var blindedBostonCrc = Actor.Person("boston-crc@ev-parity.test");
        await IssueGrantAsync(tenant, tenantMonitor, AuthorityScope.Tenant(), ct);
        await IssueGrantAsync(tenant, unblindedPharm, AuthorityScope.StudySite(bostonSite), ct);
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), unblindedPharm, "enrollment.unblind", null, AuthorityScope.StudySite(bostonSite), fixture.Clock.UtcNow, null, false, KernelProtocolScenario.Key(), "Unblind"),
            ct);
        await IssueGrantAsync(tenant, blindedBostonCrc, AuthorityScope.StudySite(bostonSite), ct);
        Tick();

        return new FourEvidenceKindsWorld(
            tenant,
            study,
            bostonSite,
            londonSite,
            londonVitalsObs,
            vitalsObs1,
            dispenseObs1,
            standaloneBlindedObs1,
            vitalsObs2,
            customTagObs2,
            findingsByRule["R_DIRECT_BLIND"],
            findingsByRule["R_AMENDED_CONCEPT"],
            findingsByRule["R_TARGET_OBS"],
            findingsByRule["R_SIBLING_BLIND"],
            findingsByRule["R_ENR2_OPEN_A"],
            findingsByRule["R_ENR2_OPEN_B"],
            findingsByRule["R_ENR2_OPEN_C"],
            findingsByRule["R_DESIGN_OPEN"],
            assertBlinded,
            childBlindedAssertion.Id,
            assertOpen,
            assertCustomTag,
            blindedQueryId,
            openQueryId,
            customTagQueryId,
            tenantMonitor,
            unblindedPharm,
            blindedBostonCrc);
    }

    private async Task<LargeProtocolBenchmarkWorld> SeedLargeProtocolBenchmarkAsync(CancellationToken ct)
    {
        var forms = new KernelCollectionScenario(fixture);
        var (tenant, study) = await forms.Protocol.NewStudyAsync(ct);

        var sbpConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic Blood Pressure", "Resting arterial systolic pressure.", KernelProtocolScenario.Key(), "Define SBP"),
            ct);
        var hrConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Heart Rate", "Resting ventricular rate.", KernelProtocolScenario.Key(), "Define HR"),
            ct);
        var armConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Randomization Arm", "Blinded treatment arm allocation.", KernelProtocolScenario.Key(), "Define Arm"),
            ct);
        var panelConcept = await forms.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Clinical Laboratory Analyte", "Standardized chemistry and hematology analyte.", KernelProtocolScenario.Key(), "Define Panel Analyte"),
            ct);

        // Build a valid >460.7 KB protocol design (40 collection specs x 25 items + detailed import notes / rules).
        var specs = new List<CollectionSpec>(42)
        {
            new(
                "cs_vitals",
                "Vital Signs Assessment",
                [
                    new CollectionItem("sbp", sbpConcept, "mm[Hg]", Required: true),
                    new CollectionItem("hr", hrConcept, "/min", Required: true),
                ]),
            new(
                "cs_dispense",
                "Investigational Product Dispensation",
                [
                    new CollectionItem("arm", armConcept, ValueKind: ObservationValueKind.Text, Required: true),
                ]),
        };

        var specIds = new List<string>(42) { "cs_vitals", "cs_dispense" };
        for (var s = 1; s <= 40; s++)
        {
            var specId = $"cs_panel_{s:D2}";
            specIds.Add(specId);
            var items = new List<CollectionItem>(25);
            for (var i = 1; i <= 25; i++)
            {
                items.Add(new CollectionItem(
                    $"p{s:D2}_m{i:D2}",
                    panelConcept,
                    i % 2 == 0 ? "/min" : "mmol/L",
                    Required: false));
            }

            specs.Add(new CollectionSpec(
                specId,
                $"Clinical Laboratory and Biomarker Assessment Panel {s:D2} — Standardized Multi-Regional Schedule",
                items,
                Purpose: new DataPurpose
                {
                    OperationalNeed = $"Protocol-mandated safety and pharmacodynamic panel {s:D2} collected at scheduled clinic visits across global investigational sites.",
                }));
        }

        var notes = new List<ProtocolImportNote>(1_250);
        for (var n = 1; n <= 1_250; n++)
        {
            notes.Add(new ProtocolImportNote(
                $"USDM.StudyDesign.ScheduleTimeline.Activity_{n:D4}",
                $"Imported CDISC USDM v3 schedule annotation {n:D4}: mapped site assessment window, specimen handling instruction, central laboratory courier window, and eCRF completion guideline without loss of clinical semantics."));
        }

        var largeDesign = ProtocolDesignSamples.MinimalDesign() with
        {
            Title = "Phase 3 Randomized, Double-Blind, Placebo-Controlled Multi-Regional Trial Evaluating Efficacy and Safety",
            Epochs = [new Epoch("ep.treatment", "Double-Blind Treatment Epoch", 1)],
            PlannedActivities =
            [
                new PlannedActivity(KernelCollectionScenario.VisitId, "Baseline and Randomization Visit", "ep.treatment", [ActivityLocation.Site], specIds, new ActivityTiming(1, 0, 0)),
            ],
            CollectionSpecs = specs,
            ImportNotes = notes,
        };

        var protocolJson = ProtocolDesignDocument.Write(largeDesign);
        ProtocolDesignDocument.Read(protocolJson).IsValid.Should().BeTrue("benchmark protocol must be a valid ProtocolDesignDocument");
        var protocolUtf8Bytes = Encoding.UTF8.GetByteCount(protocolJson);

        var version = ProtocolVersionId.New();
        (await forms.Protocol.CreateDraftAsync(tenant, study, version, ct, protocolJson)).IsSuccess.Should().BeTrue();
        var inReview = await forms.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, version, KernelProtocolScenario.Key(), "Submit large protocol"), ct);
        var sig = await forms.Protocol.OkAsync(forms.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await forms.Protocol.OkAsync(new ApproveProtocolVersion(tenant, version, sig, KernelProtocolScenario.Key(), "Approve large protocol"), ct);

        var eff = EffectivityId.New();
        await forms.Protocol.OkAsync(new DeclareEffectivity(tenant, eff, version, EffectivityScope.Study(), Tick(), KernelProtocolScenario.Key(), "Declare effectivity"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.CreateVersion7():N}");
        var ethics = ArtifactVersionId.New();
        await forms.Protocol.OkAsync(
            new RegisterArtifact(
                tenant,
                ArtifactId.New(),
                ethics,
                ArtifactKind.EthicsApproval,
                study,
                "Central IRB Approval",
                new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://irb/central"),
                KernelProtocolScenario.Key(),
                "Central IRB"),
            ct);
        await forms.Protocol.OkAsync(new ChangeEffectivityStatus(tenant, eff, EffectivityStatus.Effective, Tick(), [ethics], KernelProtocolScenario.Key(), "Effective"), ct);

        var (bostonSite, _) = await AddStudySiteAsync(tenant, study, "US", ct);

        // Seed 20 enrollments with open vitals + dispense forms and sibling observations via command pipeline,
        // then bulk-seed remaining observations to reach 5,000 observations and 200 validation findings.
        const int enrollmentCount = 20;
        var enrollments = new List<(ParticipantId Participant, EnrollmentId Enrollment, CollectionInstanceId VitalsForm, CollectionInstanceId DispenseForm)>(enrollmentCount);
        var observationNodes = new List<NodeRef>(5_000);
        var expectedBlindedObs = 0;

        for (var e = 0; e < enrollmentCount; e++)
        {
            var (participantId, enrollmentId) = await ScreenAsync(tenant, bostonSite, $"PERF-{e + 1:D4}", ct);
            var subject = new KernelObservationScenario.Subject(tenant, study, bostonSite, enrollmentId);
            var vitalsFormId = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subject, CollectionInstanceId.New(), Tick(), "cs_vitals"), ct)).CollectionInstanceId;
            var dispenseFormId = (await forms.Protocol.OkAsync(KernelCollectionScenario.Open(subject, CollectionInstanceId.New(), Tick(), "cs_dispense"), ct)).CollectionInstanceId;

            var savedVitals = await forms.Protocol.OkAsync(
                KernelCollectionScenario.Save(
                    new KernelCollectionScenario.Form(subject, version, vitalsFormId),
                    KernelCollectionScenario.Value("sbp", KernelCollectionScenario.Mmhg(120 + (e % 15))),
                    new CollectedValue("hr", new QuantityValue(65 + (e % 10), "/min"), 0)),
                ct);
            foreach (var v in savedVitals.Values)
            {
                observationNodes.Add(NodeRef.For(v.ObservationId));
            }

            var savedDispense = await forms.Protocol.OkAsync(
                KernelCollectionScenario.Save(
                    new KernelCollectionScenario.Form(subject, version, dispenseFormId),
                    new CollectedValue("arm", new TextValue(e % 2 == 0 ? "Arm A Active" : "Arm B Placebo"), 0)),
                ct);
            observationNodes.Add(NodeRef.For(savedDispense.Values.Single().ObservationId));
            expectedBlindedObs++;

            enrollments.Add((participantId, enrollmentId, vitalsFormId, dispenseFormId));
        }

        var recordedAt = KernelTimestamps.ToStoredPrecision(Tick());
        var observedAt = recordedAt.AddMinutes(-5);
        var remainingCount = 5_000 - observationNodes.Count;
        var bulkObservations = new List<object>(remainingCount);
        for (var i = 0; i < remainingCount; i++)
        {
            var owner = enrollments[i % enrollmentCount];
            var isBlinded = i % 10 == 0;
            var obsId = ObservationId.New();
            var fallbackConcept = i % 2 == 0 ? sbpConcept : hrConcept;
            var concept = isBlinded ? armConcept : fallbackConcept;
            var unit = i % 2 == 0 ? "mm[Hg]" : "/min";
            ObservationValue val = isBlinded
                ? new TextValue($"Kit-{1000 + i}")
                : new QuantityValue(110 + (i % 30), unit);
            var obs = Observation.Record(
                obsId,
                tenant,
                owner.Enrollment,
                owner.Participant,
                study,
                concept,
                val,
                observedAt,
                "Bulk benchmark observation",
                KernelPostgresFixture.ActorId,
                recordedAt).Value!;
            bulkObservations.Add(obs);
            observationNodes.Add(NodeRef.For(obsId));
            if (isBlinded)
            {
                expectedBlindedObs++;
            }
        }

        await fixture.SeedAsync([.. bulkObservations]);

        // Record 200 validation findings across the 20 enrollments (10 per enrollment: 2 blinded, 8 unblinded DatasetRecord checks).
        var pack = await forms.Protocol.OkAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.RulePack, "perf-200-pack", "Perf 200 findings pack", RulePackJson.ToJson(SampleRulePacks.Sample()), [], KernelProtocolScenario.Key(), "Draft perf pack"),
            ct);
        var packSig = await forms.Protocol.OkAsync(
            forms.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, pack.Id.Value, pack.ContentHash)),
            ct);
        await forms.Protocol.OkAsync(new ApproveLibraryItem(tenant, pack.Id, packSig, KernelProtocolScenario.Key(), "Approve perf pack"), ct);

        var detections = new List<ValidationDetection>(200);
        var expectedBlindedFindings = 0;
        for (var i = 0; i < 200; i++)
        {
            var owner = enrollments[i % enrollmentCount];
            if (i % 10 == 0)
            {
                detections.Add(new ValidationDetection(
                    "perf-200-pack",
                    $"R_PERF_{i:D3}",
                    1,
                    ValidationSeverity.Error,
                    new FindingTarget(FindingTargetKind.DatasetRecord, "cs_dispense", owner.DispenseForm.Value.ToString("D"), "arm"),
                    $"Dispense check {i}.",
                    null,
                    [new ObservedVariable("arm", "checked")]));
                expectedBlindedFindings++;
            }
            else if (i % 10 == 1)
            {
                detections.Add(new ValidationDetection(
                    "perf-200-pack",
                    $"R_PERF_{i:D3}",
                    1,
                    ValidationSeverity.Warning,
                    new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", owner.VitalsForm.Value.ToString("D"), "sbp"),
                    $"Cross-form blinded sibling check {i}.",
                    null,
                    [new ObservedVariable("cs_dispense.arm", "present")]));
                expectedBlindedFindings++;
            }
            else
            {
                detections.Add(new ValidationDetection(
                    "perf-200-pack",
                    $"R_PERF_{i:D3}",
                    1,
                    ValidationSeverity.Warning,
                    new FindingTarget(FindingTargetKind.DatasetRecord, "cs_vitals", owner.VitalsForm.Value.ToString("D"), i % 2 == 0 ? "sbp" : "hr"),
                    $"Open vitals check {i}.",
                    null,
                    [new ObservedVariable("sbp", "122")]));
            }
        }

        var runAt = Tick();
        await forms.Protocol.OkAsync(
            new RecordValidationRun(
                tenant,
                ValidationRunId.New(),
                study,
                ValidationStage.Collection,
                [pack.Id],
                new DateOnly(2026, 10, 2),
                null,
                null,
                null,
                [],
                ["cs_vitals", "cs_dispense"],
                runAt.AddSeconds(-1),
                runAt,
                detections,
                KernelProtocolScenario.Key(),
                "Record 200 perf findings"),
            ct);

        var findingNodes = (await fixture.Findings.ListAsync(tenant, new FindingQuery(study) { Take = 200 }, ct))
            .Findings
            .Select(f => NodeRef.For(f.Id))
            .ToList();

        await using (var adminConn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await adminConn.OpenAsync(ct);
            await using var analyzeCmd = new NpgsqlCommand(
                "ANALYZE kernel.observations, kernel.collection_instances, kernel.collection_instance_members, kernel.validation_findings, kernel.validation_detections, kernel.protocol_versions, kernel.concepts;",
                adminConn)
            { CommandTimeout = 0 };
            await analyzeCmd.ExecuteNonQueryAsync(ct);
        }

        return new LargeProtocolBenchmarkWorld(
            tenant,
            protocolUtf8Bytes,
            observationNodes,
            expectedBlindedObs,
            findingNodes,
            expectedBlindedFindings);
    }

    private static LineageEdgeRecord Edge(
        TenantId tenant, NodeRef from, NodeRef to, LineageEdgeKind kind, DateTimeOffset recordedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant,
            FromKind = from.Kind,
            FromId = from.Id,
            FromVersion = from.Version!.Value,
            ToKind = to.Kind,
            ToId = to.Id,
            ToVersion = to.Version!.Value,
            Kind = kind,
            ActivityId = Guid.CreateVersion7(),
            RecordedAt = recordedAt,
        };

    private sealed record AllKindsWorld(
        TenantId Tenant,
        OrganizationId Sponsor,
        Guid HospitalBostonId,
        Guid HospitalBerlinId,
        StudyId StudyA,
        StudyId StudyB,
        StudySiteId BostonSite,
        StudySiteId BerlinSite,
        ParticipantId ParticipantBoston,
        ParticipantId ParticipantChicago,
        EnrollmentId EnrollmentBoston,
        ProtocolVersionId ProtocolVersionA,
        Guid ProtocolElementAId,
        ArtifactId StudyArtifact,
        ArtifactId TenantArtifact,
        ArtifactVersionId StudyArtifactVersion,
        ArtifactVersionId TenantArtifactVersion,
        GrantId TenantGrant,
        GrantId OrgGrant,
        GrantId StudyGrant,
        GrantId CountryGrant,
        GrantId SiteGrant,
        QualificationRequirementId OrgRequirement,
        QualificationRequirementId StudyRequirement,
        QualificationRequirementId CountryRequirement,
        QualificationRequirementId SiteRequirement,
        EffectivityId StudyEffectivity,
        EffectivityId CountryEffectivity,
        EffectivityId SiteEffectivity,
        ProposalId ProposalA,
        CollectionInstanceId CollectionInstanceBoston,
        ObservationId FormObservationBoston,
        ObservationId StandaloneObservationBoston,
        AssertionId VerificationAssertionBoston,
        AssertionId RecordedFormLockAssertion,
        AssertionId SiteLockAssertion,
        AssertionId StudyLockAssertion,
        ValidationFindingId OpenStudyFinding,
        ValidationFindingId DecidedStudyFinding,
        ValidationFindingId SiteCollectionFinding,
        ValidationFindingId SiteObservationFinding,
        AttestationId ProtocolAttestation,
        AttestationId EnrollmentAttestation,
        AttestationId StudyArtifactAttestation,
        AttestationId TenantArtifactAttestation,
        AttestationId CollectionAttestation,
        AttestationId RecordedLockAttestation,
        AttestationId UnrecordedLockAttestation,
        AttestationId ProposalAttestation,
        AttestationId StudyFindingAttestation,
        AttestationId SiteFindingAttestation,
        AttestationId StandardAttestation,
        AttestationId LibraryItemAttestation,
        AttestationId TaskConfigurationAttestation,
        TaskConfigurationId AttestedTaskConfiguration)
    {
        public SiteId HospitalBoston => SiteId.FromGuid(HospitalBostonId);
    }

    private sealed record WaterfallScenario(
        TenantId Tenant,
        Actor Coordinator,
        AgentPrincipal Principal,
        AgentPrincipal UnpermittedPrincipal,
        List<NodeRef> Nodes,
        int DistinctInputCount,
        TaskConfigurationId AttestedTaskConfiguration);

    private sealed record WireSnapshot(
        int ConnectionsOpened,
        int SetConfigStatements,
        int SelectStatements,
        int TotalWireStatements,
        double ActualPostgresElapsedMs,
        double SimulatedAtHalfMsPerStatementMs);

    private sealed record FourEvidenceKindsWorld(
        TenantId Tenant,
        StudyId Study,
        StudySiteId BostonSite,
        StudySiteId LondonSite,
        ObservationId LondonVitalsObs,
        ObservationId VitalsObsEnrollment1,
        ObservationId DispenseBlindedObsEnrollment1,
        ObservationId StandaloneBlindedObsEnrollment1,
        ObservationId VitalsObsEnrollment2,
        ObservationId CustomTagObsEnrollment2,
        ValidationFindingId DirectBlindedFinding,
        ValidationFindingId AmendedConceptBlindedFinding,
        ValidationFindingId TargetObsBlindedFinding,
        ValidationFindingId CrossFormSiblingBlindedFinding,
        ValidationFindingId Enrollment2OpenFindingA,
        ValidationFindingId Enrollment2OpenFindingB,
        ValidationFindingId Enrollment2OpenFindingC,
        ValidationFindingId DesignElementOpenFinding,
        AssertionId AssertionOnBlindedObs,
        AssertionId ChildAssertionOnBlindedAssertion,
        AssertionId AssertionOnOpenObs,
        AssertionId AssertionOnCustomTagObs,
        Guid BlindedFormQueryId,
        Guid OpenFormQueryId,
        Guid CustomTagFormQueryId,
        Actor TenantMonitor,
        Actor UnblindedBostonPharmacist,
        Actor BlindedBostonCoordinator);

    private sealed record LargeProtocolBenchmarkWorld(
        TenantId Tenant,
        int ProtocolUtf8Bytes,
        List<NodeRef> ObservationNodes,
        int ExpectedBlindedObservations,
        List<NodeRef> FindingNodes,
        int ExpectedBlindedFindings);

    private sealed class CountingBlindedTreatmentClassifier(IBlindedTreatmentClassifier inner) : IBlindedTreatmentClassifier
    {
        private readonly ConcurrentDictionary<string, int> _obsCallsByItem = new(StringComparer.Ordinal);

        public int ObservationCallsForItem(string itemId) => _obsCallsByItem.GetValueOrDefault(itemId, 0);

        public void Reset() => _obsCallsByItem.Clear();

        public bool IsBlindedObservation(
            ConceptId conceptId,
            ObservationValue? value,
            BlindedConceptMetadata? concept,
            string? collectionSpecId,
            string? itemId,
            IReadOnlyList<ProtocolDesign> designs)
        {
            if (itemId is not null)
            {
                _obsCallsByItem.AddOrUpdate(itemId, 1, static (_, count) => count + 1);
            }

            return inner.IsBlindedObservation(conceptId, value, concept, collectionSpecId, itemId, designs);
        }

        public bool IsBlindedCollectionSpec(
            string? collectionSpecId,
            string? collectionSpecName = null,
            string? operationalNeed = null,
            IReadOnlyList<ProtocolDesign>? designs = null) =>
            inner.IsBlindedCollectionSpec(collectionSpecId, collectionSpecName, operationalNeed, designs);

        public bool IsBlindedCollectionItem(
            string? collectionSpecId,
            string? collectionTitle,
            CollectionItem? item,
            ObservationValue? value = null,
            BlindedConceptMetadata? concept = null,
            IReadOnlyList<ProtocolDesign>? designs = null) =>
            inner.IsBlindedCollectionItem(collectionSpecId, collectionTitle, item, value, concept, designs);

        public bool IsBlindedVariable(
            string? defaultDataset,
            string? variable,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null) =>
            inner.IsBlindedVariable(defaultDataset, variable, designs, resolveConcept);

        public bool IsBlindedFinding(
            FindingTarget target,
            IReadOnlyList<ObservedVariable> observedValues,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null) =>
            inner.IsBlindedFinding(target, observedValues, designs, resolveConcept);
    }

    private sealed class CustomMarkerBlindedClassifier(string sentinel) : IBlindedTreatmentClassifier
    {
        public bool IsBlindedObservation(
            ConceptId conceptId,
            ObservationValue? value,
            BlindedConceptMetadata? concept,
            string? collectionSpecId,
            string? itemId,
            IReadOnlyList<ProtocolDesign> designs) =>
            (value is TextValue text && text.Value.Contains(sentinel, StringComparison.Ordinal))
            || BlindedTreatmentClassifier.Instance.IsBlindedObservation(conceptId, value, concept, collectionSpecId, itemId, designs);

        public bool IsBlindedCollectionSpec(
            string? collectionSpecId,
            string? collectionSpecName = null,
            string? operationalNeed = null,
            IReadOnlyList<ProtocolDesign>? designs = null) =>
            BlindedTreatmentClassifier.Instance.IsBlindedCollectionSpec(collectionSpecId, collectionSpecName, operationalNeed, designs);

        public bool IsBlindedCollectionItem(
            string? collectionSpecId,
            string? collectionTitle,
            CollectionItem? item,
            ObservationValue? value = null,
            BlindedConceptMetadata? concept = null,
            IReadOnlyList<ProtocolDesign>? designs = null) =>
            BlindedTreatmentClassifier.Instance.IsBlindedCollectionItem(collectionSpecId, collectionTitle, item, value, concept, designs);

        public bool IsBlindedVariable(
            string? defaultDataset,
            string? variable,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null) =>
            BlindedTreatmentClassifier.Instance.IsBlindedVariable(defaultDataset, variable, designs, resolveConcept);

        public bool IsBlindedFinding(
            FindingTarget target,
            IReadOnlyList<ObservedVariable> observedValues,
            IReadOnlyList<ProtocolDesign> designs,
            Func<ConceptId, BlindedConceptMetadata?>? resolveConcept = null) =>
            observedValues.Any(v => v.Value?.Contains(sentinel, StringComparison.Ordinal) == true)
            || BlindedTreatmentClassifier.Instance.IsBlindedFinding(target, observedValues, designs, resolveConcept);
    }

    /// <summary>
    /// Captures exact Large Object Heap (&gt;= 85,000 byte) allocations emitted by the .NET GC runtime via
    /// <c>Microsoft-Windows-DotNETRuntime</c> <c>GCAllocationTick</c> events (<c>AllocationKind == 1</c>).
    /// </summary>
    private sealed class LohAllocationRecorder : EventListener
    {
        private const EventKeywords GcKeyword = (EventKeywords)0x1;
        private const uint LargeObjectHeapKind = 1;
        private long _allocatedLohBytes;

        public long AllocatedLohBytes => Interlocked.Read(ref _allocatedLohBytes);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, "Microsoft-Windows-DotNETRuntime", StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Verbose, GcKeyword);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.Payload is { Count: >= 4 } payload
                && eventData.EventName is { } name
                && name.StartsWith("GCAllocationTick", StringComparison.Ordinal)
                && payload[1] is uint kind
                && kind == LargeObjectHeapKind
                && payload[3] is ulong bytes)
            {
                Interlocked.Add(ref _allocatedLohBytes, (long)bytes);
            }
        }
    }

    private sealed class SimpleDbContextFactory(DbContextOptions<KernelDbContext> options) : IDbContextFactory<KernelDbContext>
    {
        public KernelDbContext CreateDbContext() => new(options);
    }

    private sealed class InstrumentedHarness(
        KernelAuthorityStore authorityStore,
        KernelTenantDbContextFactory tenantContexts,
        SqlWireRecorder recorder,
        NpgsqlDataSource dataSource,
        LoggerFactory loggerFactory) : IAsyncDisposable
    {
        public KernelAuthorityStore AuthorityStore { get; } = authorityStore;

        public KernelTenantDbContextFactory TenantContexts { get; } = tenantContexts;

        public SqlWireRecorder Recorder { get; } = recorder;

        public async ValueTask DisposeAsync()
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
            loggerFactory.Dispose();
        }
    }

    /// <summary>
    /// Captures connection leases, EF Core queries, and wire-level Npgsql <c>pg_catalog.set_config</c> statements executed
    /// by <see cref="KernelTenantConnectionInterceptor"/>.
    /// </summary>
    private sealed class SqlWireRecorder : DbCommandInterceptor, IDbConnectionInterceptor, ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _efCommands = new();
        private readonly ConcurrentQueue<string> _npgsqlCommands = new();
        private int _connectionsOpened;
        private int _connectionsClosed;

        public int ConnectionsOpened => Volatile.Read(ref _connectionsOpened);

        public int SetConfigStatements
        {
            get
            {
                var fromWire = _npgsqlCommands.Count(sql => sql.Contains("pg_catalog.set_config", StringComparison.Ordinal));
                return fromWire > 0 ? fromWire : ConnectionsOpened + Volatile.Read(ref _connectionsClosed);
            }
        }

        public int SelectStatements => _efCommands.Count;

        public IReadOnlyList<string> EfCommands => [.. _efCommands];

        public void Reset()
        {
            _efCommands.Clear();
            _npgsqlCommands.Clear();
            Interlocked.Exchange(ref _connectionsOpened, 0);
            Interlocked.Exchange(ref _connectionsClosed, 0);
        }

        public WireSnapshot Snapshot(double elapsedMs)
        {
            var selects = SelectStatements;
            var setConfigs = SetConfigStatements;
            var totalWire = selects + setConfigs;
            return new WireSnapshot(
                ConnectionsOpened,
                setConfigs,
                selects,
                totalWire,
                Math.Round(elapsedMs, 2),
                Math.Round(totalWire * 0.5, 2));
        }

        public void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            Interlocked.Increment(ref _connectionsOpened);

        public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectionsOpened);
            return Task.CompletedTask;
        }

        public InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (connection.State == ConnectionState.Open)
            {
                Interlocked.Increment(ref _connectionsClosed);
            }

            return result;
        }

        public ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (connection.State == ConnectionState.Open)
            {
                Interlocked.Increment(ref _connectionsClosed);
            }

            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _efCommands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            _efCommands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public ILogger CreateLogger(string categoryName) =>
            string.Equals(categoryName, "Npgsql.Command", StringComparison.Ordinal)
                ? new NpgsqlCommandLogger(_npgsqlCommands)
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
                if (logLevel == LogLevel.Information && state is IReadOnlyList<KeyValuePair<string, object?>> values)
                {
                    var commandText = values.FirstOrDefault(kv => string.Equals(kv.Key, "CommandText", StringComparison.Ordinal)).Value as string;
                    if (commandText is not null)
                    {
                        commands.Enqueue(commandText);
                    }
                }
            }
        }
    }
}
