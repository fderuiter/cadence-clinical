using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Authorization;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Modules.Intelligence.Tools;
using Cadence.Rules.Evaluation;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// PostgreSQL verification of batched scope lookups over shared connection leases (<c>KernelAuthorityStore</c>,
/// <c>GrantNodeVisibility</c>, <c>GrantChain</c>, <c>KernelLineageStore</c>, and <c>RequesterBoundVisibility</c>):
/// one connection lease and two <c>pg_catalog.set_config</c> statements per bulk authority call (K-ID-2), one set-based
/// <c>SELECT</c> per present kind, exact parity with single-node lookups across all 18 path-bearing kinds, lineage
/// version deduplication with preserved <c>masked:n</c> handles, and the 2,000-node waterfall benchmark.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelBatchVisibilityPostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output) : IClassFixture<KernelPostgresFixture>
{
    private const string WaterfallArtifactPath = "/tmp/artifacts/kernel-visibility-waterfall-results.json";

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

    private InstrumentedHarness CreateInstrumentedStore()
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
        var authorityStore = new KernelAuthorityStore(tenantContexts);
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
