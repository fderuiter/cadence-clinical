using System;
using System.Net.Http;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Coding;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Coding;

public sealed class CodingAutosuggestTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task GetAutosuggestionsAsync_FallsBackToDatabaseWhenAiGatewayUnavailable()
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new ExecutionDbContext(options);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        var config = new ConfigurationBuilder().Build();
        var clock = new FakeClock();

        var service = new CodingAutosuggestService(
            factory,
            dbContext,
            config,
            clock,
            NullLogger<CodingAutosuggestService>.Instance);

        var results = await service.GetAutosuggestionsAsync("Headache", "MedDRA", null, 5, TestContext.Current.CancellationToken);

        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results[0].Name.Should().Be("Headache");
        results[0].Code.Should().Be("10019211");
        results[0].Confidence.Should().Be(1.0);
        results[0].MatchType.Should().Be("EXACT_SYNONYM");
    }
}
