using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Persistence.Simulation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Simulation;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Simulation against PostgreSQL (ADR-2480): a forked and revised protocol draft is compared with its approved parent
/// through kernel persistence, with the parent's dependents counted exactly as amendment impact counts them, the run adds or changes no row in any kernel table (K-SIM-1), and another tenant's
/// versions cannot be simulated. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelSimulationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private static readonly string Revised = ProtocolDesignDocument.Write(ProtocolDesignSamples.MinimalDesign() with
    {
        Arms = [ProtocolDesignSamples.MinimalDesign().Arms[0], new Arm("arm.C", "High dose", ArmType.Experimental, [])],
    });

    /// <summary>
    /// A draft forked from the approved version and revised to replace an arm is reported element by element, its
    /// dependents are those of the removed arm and of the parent as a whole, and running the simulation leaves every
    /// kernel table of the tenant exactly as it was.
    /// </summary>
    [Fact(DisplayName = "Simulate_ForkedDraftAgainstApprovedParent_WritesNothing")]
    public async Task SimulateForkedDraftAgainstApprovedParentWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (tenant, study) = await scenario.NewStudyAsync(ct);
        var parent = await ApproveAsync(scenario, tenant, study, ProtocolDesignSamples.Minimal, ct);
        var draft = ProtocolVersionId.New();
        await scenario.OkAsync(new ForkProtocolVersion(tenant, parent, draft, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        await scenario.OkAsync(new ReviseProtocolDraft(tenant, draft, Revised, null, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var other = ProtocolVersionId.New();
        await scenario.OkAsync(new ForkProtocolVersion(tenant, parent, other, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var before = await RowCountsAsync(tenant, ct);

        var result = await Engine().SimulateAsync(tenant, new SimulationRequest(SimulationCandidate.StoredProtocolVersion(draft)), ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value!.Scope.Should().Be(NodeRef.For(study));
        result.Value.StructuralDiff.Should().BeEquivalentTo(
            [
                new ElementChange(ProtocolStructuralDiff.RootId, null, ElementChangeKind.Changed, ["/arms"]),
                new ElementChange("arm.B", null, ElementChangeKind.Removed, []),
                new ElementChange("arm.C", null, ElementChangeKind.Added, []),
            ],
            options => options.WithStrictOrdering());
        result.Value.Candidate.BaseVersion!.Unversioned().Should().Be(NodeRef.For(parent));
        result.Value.EngineVersions.Should().Contain(SimulationEngine.DependentsEngine);
        result.Value.Dependents!.Start.Should().Be(NodeRef.For(parent).ToString());
        result.Value.Dependents.Groups.SelectMany(g => g.Objects).Select(o => o.Ref).Should().BeEquivalentTo(
            [NodeRef.For(other, 1), NodeRef.For(ProtocolElementId.For(study, "arm.B"), 1)],
            "the other open amendment depends on the parent, and so does the removed arm (ADR-2474); the simulated draft "
            + "itself and the unchanged arm do not count");
        var impact = await fixture.AmendmentImpact.ComputeAsync(
            tenant, fixture.CurrentActor.Actor!, draft, validAt: result.Value.AsOf.ValidAt, knownAt: result.Value.AsOf.KnownAt, cancellationToken: ct);
        impact!.Dependents.Should().BeEquivalentTo(result.Value.Dependents, "simulation and amendment impact count through the same read (Canvas D2)");
        (await RowCountsAsync(tenant, ct)).Should().Equal(before, "a simulation writes no clinical state (K-SIM-1)");
    }

    /// <summary>A version of another tenant is unknown to the simulation, as to every other read.</summary>
    [Fact(DisplayName = "Simulate_CannotReachAnotherTenantsVersions")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task SimulateCannotReachAnotherTenantsVersions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var scenario = new KernelProtocolScenario(fixture);
        var (owner, study) = await scenario.NewStudyAsync(ct);
        var (stranger, _) = await scenario.NewStudyAsync(ct);
        var version = await ApproveAsync(scenario, owner, study, KernelProtocolScenario.Content, ct);

        var own = await Engine().SimulateAsync(owner, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(version, Revised)), ct);
        var foreign = await Engine().SimulateAsync(stranger, new SimulationRequest(SimulationCandidate.UnsavedProtocolVersion(version, Revised)), ct);

        own.IsSuccess.Should().BeTrue(own.Error?.Message);
        foreign.Error!.Code.Should().Be(SimulationErrors.VersionUnknown.Code);
    }

    private SimulationEngine Engine() =>
        new(new KernelSimulationInputs(fixture.TenantContexts, (KernelLineageStore)fixture.Lineage), new PermitAll(), fixture.Visibility, fixture.CurrentActor, fixture.Clock);

    private static async Task<ProtocolVersionId> ApproveAsync(KernelProtocolScenario scenario, TenantId tenant, StudyId study, string content, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await scenario.CreateDraftAsync(tenant, study, id, ct, content)).IsSuccess.Should().BeTrue();
        var inReview = await scenario.OkAsync(new SubmitProtocolVersionForReview(tenant, id, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var attestation = await scenario.OkAsync(scenario.AttestCommand(tenant, Cadence.Kernel.Trust.AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await scenario.OkAsync(new ApproveProtocolVersion(tenant, id, attestation, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        return id;
    }

    /// <summary>Every kernel table with a tenant column, with its row count and a digest of its rows for the tenant.</summary>
    private async Task<List<string>> RowCountsAsync(TenantId tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.columns WHERE table_schema = 'kernel' AND column_name = 'tenant_id' ORDER BY table_name",
            connection))
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        tables.Should().Contain(["protocol_versions", "audit_entries", "outbox_messages"]);
        var counts = new List<string>();
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand(
                $"SELECT count(*) || ':' || coalesce(md5(string_agg(t::text, '|' ORDER BY t::text)), '') FROM kernel.\"{table}\" t WHERE tenant_id = @tenant",
                connection);
            count.Parameters.AddWithValue("tenant", tenant.Value);
            counts.Add($"{table}={await count.ExecuteScalarAsync(ct)}");
        }

        return counts;
    }

    private sealed class PermitAll : IAuthorizationProbe
    {
        public ValueTask<AuthorizationDecision> CanIAsync(TenantId tenant, Capability capability, NodeRef resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(AuthorizationDecision.Permit());

        public ValueTask<IReadOnlyList<CommandAvailability>> GetCommandsAsync(TenantId tenant, NodeRef resource, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
