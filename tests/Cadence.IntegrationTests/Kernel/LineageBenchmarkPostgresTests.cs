using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.References;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The lineage benchmark #4815 asks for: trace latency on a 10-million-edge graph. Opt in with
/// <c>CADENCE_LINEAGE_BENCHMARK=1</c> as well as <c>CADENCE_TEST_POSTGRES</c>; it needs about 4 GB of disk and a few
/// minutes to load, so it never runs by default. The graph is 51 layers of 100,000 objects, each object derived from
/// two objects in the layer above, which makes a backward trace fan out like a derived dataset value does.
/// </summary>
[Collection(KernelPerformanceQuotaCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Category", "Benchmark")]
public sealed class LineageBenchmarkPostgresTests(KernelPostgresFixture fixture, ITestOutputHelper output) : IClassFixture<KernelPostgresFixture>
{
    private const int Layers = 51;
    private const int Width = 100_000;

    /// <summary>
    /// Loads 10,000,000 edges and reports the median and worst latency of backward traces at depths 5 and 10 and of
    /// forward traces at depth 5, as the application role under Row-Level Security.
    /// </summary>
    [Fact(DisplayName = "Benchmark_TenMillionEdges_TraceLatency", Timeout = 3_600_000)]
    public async Task BenchmarkTenMillionEdgesTraceLatency()
    {
        fixture.SkipIfUnavailable();
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CADENCE_LINEAGE_BENCHMARK") != "1", "Set CADENCE_LINEAGE_BENCHMARK=1 to run the 10M-edge lineage benchmark.");
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        (await fixture.Sender.SendAsync(new ProvisionTenant(tenant, "BENCH", "Benchmark tenant", Guid.CreateVersion7().ToString(), "Lineage benchmark"), cancellationToken: ct))
            .IsSuccess.Should().BeTrue();

        var load = Stopwatch.StartNew();
        await using (var connection = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO kernel.lineage_edges
                    (id, tenant_id, from_kind, from_id, from_version, to_kind, to_id, to_version, kind, activity_id, recorded_at, valid_from)
                SELECT gen_random_uuid(), @tenant,
                       'observation', md5('n' || (l - 1) || ':' || ((i * 7919 + k * 104729) % @width))::uuid, 1,
                       'observation', md5('n' || l || ':' || i)::uuid, 1,
                       'DerivedFrom', gen_random_uuid(), @recorded, NULL
                FROM generate_series(1, @layers - 1) AS l, generate_series(0, @width - 1) AS i, generate_series(0, 1) AS k
                """,
                connection)
            { CommandTimeout = 0 };
            insert.Parameters.AddWithValue("tenant", tenant.Value);
            insert.Parameters.AddWithValue("width", Width);
            insert.Parameters.AddWithValue("layers", Layers);
            insert.Parameters.AddWithValue("recorded", fixture.Clock.UtcNow.AddDays(-1));
            var rows = await insert.ExecuteNonQueryAsync(ct);
            rows.Should().Be((Layers - 1) * Width * 2);
            await using var analyze = new NpgsqlCommand("ANALYZE kernel.lineage_edges", connection) { CommandTimeout = 0 };
            await analyze.ExecuteNonQueryAsync(ct);
        }

        output.WriteLine($"Loaded {(Layers - 1) * Width * 2:N0} edges in {load.Elapsed.TotalSeconds:F0} s.");

        var reader = Actor.Person("benchmark@cadence.test");
        var random = new Random(4815);
        async Task Measure(string label, LineageDirection direction, int layer, int depth)
        {
            var timings = new List<double>();
            var edges = new List<int>();
            for (var run = 0; run < 21; run++)
            {
                var node = await NodeAsync(layer, random.Next(Width));
                var watch = Stopwatch.StartNew();
                var graph = await fixture.Lineage.TraceAsync(tenant, reader, new LineageQuery(node, direction, depth), ct);
                timings.Add(watch.Elapsed.TotalMilliseconds);
                edges.Add(graph.Edges.Count);
                graph.Edges.Should().NotBeEmpty("every benchmark object has inputs and dependents");
            }

            timings.Sort();
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}: median {timings[timings.Count / 2]:F1} ms, max {timings[^1]:F1} ms, median edges returned {edges.Order().ElementAt(edges.Count / 2):N0}"));
        }

        await Measure("Backward, depth 5", LineageDirection.Backward, Layers - 1, 5);
        await Measure("Backward, depth 10", LineageDirection.Backward, Layers - 1, 10);
        await Measure("Forward, depth 5", LineageDirection.Forward, 0, 5);
    }

    /// <summary>The object the load script generated for this layer and index, computed by the same expression.</summary>
    private async Task<NodeRef> NodeAsync(int layer, int index)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT md5('n' || @layer || ':' || @index)::uuid", connection);
        command.Parameters.AddWithValue("layer", layer);
        command.Parameters.AddWithValue("index", index);
        var id = (Guid)(await command.ExecuteScalarAsync())!;
        return new NodeRef(NodeKinds.Observation, id, 1);
    }
}
