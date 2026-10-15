using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Core.Synthetic;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace Cadence.UnitTests.Knowledge;

public class KnowledgeTestFactory : WebApplicationFactory<Cadence.Apps.Knowledge.Program>
{
    private readonly string _databaseName = $"knowledge-test-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestGatewaySecret().UseTestPostgresConnectionString();
        builder.ConfigureServices(services =>
        {
            var inMemoryServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.RemoveAll<DbContextOptions<KnowledgeDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<KnowledgeDbContext>>();
            services.AddDbContext<KnowledgeDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.UseInternalServiceProvider(inMemoryServices);
            });
        });
    }
}

public class RweEndpointsTests : IClassFixture<KnowledgeTestFactory>
{
    private readonly KnowledgeTestFactory _factory;

    public RweEndpointsTests(KnowledgeTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRweCohorts_ReturnsDefaultOrSeededProfiles()
    {
        var client = _factory.CreateSignedClient();

        var ct = TestContext.Current.CancellationToken;
        var response = await client.GetAsync("/api/v1/knowledge/rwe/cohorts", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cohorts = await response.Content.ReadFromJsonAsync<List<RweCohortProfileResponse>>(cancellationToken: ct);

        cohorts.Should().NotBeNull();
        cohorts.Should().NotBeEmpty();
        cohorts![0].CohortCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateRweCohort_StoresNewProfile()
    {
        var client = _factory.CreateSignedClient();

        var request = new CreateRweCohortRequest(
            CohortCode: "RWE-CARDIO-02",
            Name: "Cardiology Heart Failure Baseline",
            TherapeuticArea: "Cardiology",
            Indication: "Heart Failure with Reduced Ejection Fraction",
            TotalPatientCount: 7500);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/api/v1/knowledge/rwe/cohorts", request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var profile = await response.Content.ReadFromJsonAsync<RweCohortProfileResponse>(cancellationToken: ct);

        profile.Should().NotBeNull();
        profile!.CohortCode.Should().Be("RWE-CARDIO-02");
        profile.TherapeuticArea.Should().Be("Cardiology");
    }

    [Fact]
    public async Task GetRweCohortRecords_ReturnsDeidentifiedRecords()
    {
        var client = _factory.CreateSignedClient();

        var ct = TestContext.Current.CancellationToken;
        var response = await client.GetAsync("/api/v1/knowledge/rwe/cohorts/RWE-NSCLC-01/records?limit=50", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var records = await response.Content.ReadFromJsonAsync<List<SyntheticPatientRecord>>(cancellationToken: ct);

        records.Should().NotBeNull();
        records.Should().HaveCount(50);
        records![0].PatientId.Should().StartWith("SYNTH-PAT-");
    }
}
