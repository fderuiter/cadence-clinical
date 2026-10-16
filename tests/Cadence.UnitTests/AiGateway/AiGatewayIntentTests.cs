using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

using Cadence.Apps.AiGateway.Endpoints;
using Cadence.Apps.AiGateway.Services;
using Cadence.Contracts;

using FluentAssertions;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Cadence.UnitTests.AiGateway;

/// <summary>
/// Unit tests for AI Gateway Intent Classification and Navigation Endpoint.
/// </summary>
public class AiGatewayIntentTests
{
    [Fact]
    public void ClassifyIntent_Scenario1_LogAdverseEventForSubject_ReturnsEcrfActionWithSubject()
    {
        var roles = new[] { "site_investigator", "crc" };
        var query = "log adverse event for subject 101";

        var response = RuleBasedIntentRouter.ClassifyIntent(query, roles);

        response.Query.Should().Be(query);
        response.Recommendations.Should().NotBeEmpty();

        var aeRec = response.Recommendations.FirstOrDefault(r => r.Action == "log_ae");
        aeRec.Should().NotBeNull();
        aeRec!.Name.Should().Contain("Log Adverse Event");
        aeRec.Name.Should().Contain("SUBJ-101");
        aeRec.TargetRoute.Should().Be("/ecrf?subjectId=SUBJ-101&action=log_ae");
        aeRec.Category.Should().Be("[Suggested]");
        aeRec.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void ClassifyIntent_LogAdverseEventWithoutSubject_DoesNotFabricateDefaultSubject()
    {
        var roles = new[] { "site_investigator", "crc" };
        var query = "log adverse event";

        var response = RuleBasedIntentRouter.ClassifyIntent(query, roles);

        response.Recommendations.Should().NotBeEmpty();

        var aeRec = response.Recommendations.FirstOrDefault(r => r.Action == "log_ae");
        aeRec.Should().NotBeNull();
        aeRec!.TargetRoute.Should().Be("/ecrf?action=log_ae");
        aeRec.TargetRoute.Should().NotContain("subjectId=");
        aeRec.Name.Should().Be("Log Adverse Event");
    }

    [Fact]
    public void ClassifyIntent_Scenario2_CreateProtocolDeviation_ReturnsTicketActionWithSite()
    {
        var roles = new[] { "cra", "monitor" };
        var query = "create protocol deviation for site 102 missing lab cert";

        var response = RuleBasedIntentRouter.ClassifyIntent(query, roles);

        response.Recommendations.Should().NotBeEmpty();

        var ticketRec = response.Recommendations.FirstOrDefault(r => r.Action == "create_ticket");
        ticketRec.Should().NotBeNull();
        ticketRec!.Name.Should().Contain("Protocol Deviation");
        ticketRec.Name.Should().Contain("102");
        ticketRec.TargetRoute.Should().Contain("siteId=102");
        ticketRec.TargetRoute.Should().Contain("reason=missing_lab_cert");
        ticketRec.Category.Should().Be("[Suggested]");
        ticketRec.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void ClassifyIntent_ProtocolDeviationWithoutSite_DoesNotFabricateDefaultSite()
    {
        var roles = new[] { "cra", "monitor" };
        var query = "raise a protocol deviation";

        var response = RuleBasedIntentRouter.ClassifyIntent(query, roles);

        response.Recommendations.Should().NotBeEmpty();

        var ticketRec = response.Recommendations.FirstOrDefault(r => r.Action == "create_ticket");
        ticketRec.Should().NotBeNull();
        ticketRec!.TargetRoute.Should().NotContain("siteId=");
        ticketRec.Name.Should().Be("Create Protocol Deviation");
    }

    [Fact]
    public void ClassifyIntent_FiltersSuggestions_BasedOnUserRoles()
    {
        var ctmsQuery = "view site monitoring visits ctms";

        // CRA role -> allowed CTMS
        var craResponse = RuleBasedIntentRouter.ClassifyIntent(ctmsQuery, new[] { "cra" });
        craResponse.Recommendations.Should().Contain(r => r.TargetRoute.StartsWith("/ctms"));

        // Lay public user -> prohibited CTMS
        var restrictedResponse = RuleBasedIntentRouter.ClassifyIntent(ctmsQuery, new[] { "unauthorized_role" });
        restrictedResponse.Recommendations.Should().NotContain(r => r.TargetRoute.StartsWith("/ctms"));
    }

    /// <summary>The rule-based router gives the same recommendations every time for the same query and roles (#5415).</summary>
    [Fact]
    public void ClassifyIntent_ForTheSameQueryAndRoles_RecommendsTheSameRoutes()
    {
        var roles = new[] { "site_investigator", "crc", "cra", "sponsor_admin" };
        var query = "log adverse event for subject 101 missing lab cert SOP";
        var first = RuleBasedIntentRouter.ClassifyIntent(query, roles);

        first.Recommendations.Should().NotBeEmpty();
        for (int i = 0; i < 100; i++)
        {
            RuleBasedIntentRouter.ClassifyIntent(query, roles).Recommendations.Select(r => r.TargetRoute)
                .Should().Equal(first.Recommendations.Select(r => r.TargetRoute));
        }
    }

    [Theory]
    [InlineData("investigator")]
    [InlineData("principal_investigator")]
    [InlineData("crc")]
    [InlineData("sponsor_dm")]
    public void ClassifyIntent_CanonicalGatewayRoles_SeeAdverseEventSuggestion(string canonicalRole)
    {
        // The gateway signs roles already passed through RoleNormalization, so these are the names
        // the endpoint receives in practice.
        var response = RuleBasedIntentRouter.ClassifyIntent("log adverse event for subject 101", new[] { canonicalRole });

        response.Recommendations.Should().Contain(r => r.Action == "log_ae");
    }

    [Theory]
    [InlineData("Site Investigator")]
    [InlineData("data_manager")]
    [InlineData("sponsor_admin")]
    public void IsRoleAllowed_LegacyAliases_MatchTheirCanonicalRole(string alias)
    {
        RuleBasedIntentRouter.IsRoleAllowed(new[] { alias }, new[] { "investigator", "sponsor_dm" }).Should().BeTrue();
    }

    [Fact]
    public void ClassifyIntent_SubjectRole_GetsNoRoleRestrictedSuggestions()
    {
        var response = RuleBasedIntentRouter.ClassifyIntent("log adverse event for subject 101", new[] { "subject" });

        response.Recommendations.Should().NotContain(r => r.Action == "log_ae");
    }

    [Fact]
    public void RouteIntent_LogsQueryLengthButNotQueryText()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, "investigator") },
            authenticationType: "test"));
        const string query = "log adverse event for subject 101 born 1970-01-01";

        var result = IntentEndpoints.RouteIntentAsync(new IntentRouteRequest(query), user, loggerFactory);

        result.Should().BeOfType<Ok<IntentRouteResponse>>()
            .Which.Value!.Recommendations.Should().Contain(r => r.Action == "log_ae");
        loggerFactory.Messages.Should().ContainSingle()
            .Which.Should().Contain(query.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .And.NotContain("1970-01-01")
            .And.NotContain("subject 101");
    }

    [Fact]
    public void RouteIntent_UnauthenticatedCaller_IsRejected()
    {
        var result = IntentEndpoints.RouteIntentAsync(
            new IntentRouteRequest("log adverse event"), new ClaimsPrincipal(new ClaimsIdentity()), new CapturingLoggerFactory());

        result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
            // Not needed: every logger writes to Messages.
        }

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
            // Nothing to release.
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
