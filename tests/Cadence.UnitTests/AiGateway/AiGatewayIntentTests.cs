using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Cadence.Apps.AiGateway.Services;
using Cadence.Contracts;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.AiGateway;

/// <summary>
/// Unit tests for AI Gateway Intent Classification and Navigation Endpoint.
/// @req:PRD-SYS-001
/// </summary>
public class AiGatewayIntentTests
{
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void ClassifyIntent_Scenario1_LogAdverseEventForSubject_ReturnsEcrfAction()
    {
        var roles = new[] { "site_investigator", "crc" };
        var query = "log adverse event for subject 101";

        var response = IntentRoutingService.ClassifyIntent(query, roles);

        response.Query.Should().Be(query);
        response.Recommendations.Should().NotBeEmpty();

        var aeRec = response.Recommendations.FirstOrDefault(r => r.Action == "log_ae");
        aeRec.Should().NotBeNull();
        aeRec!.Name.Should().Contain("Log Adverse Event");
        aeRec.Name.Should().Contain("101");
        aeRec.TargetRoute.Should().Contain("/ecrf");
        aeRec.TargetRoute.Should().Contain("subjectId=SUBJ-101");
        aeRec.TargetRoute.Should().Contain("action=log_ae");
        aeRec.Category.Should().Be("[AI Copilot]");
        aeRec.Confidence.Should().BeGreaterThanOrEqualTo(0.95);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void ClassifyIntent_Scenario2_CreateProtocolDeviation_ReturnsTicketAction()
    {
        var roles = new[] { "cra", "monitor" };
        var query = "create protocol deviation for site 102 missing lab cert";

        var response = IntentRoutingService.ClassifyIntent(query, roles);

        response.Recommendations.Should().NotBeEmpty();

        var ticketRec = response.Recommendations.FirstOrDefault(r => r.Action == "create_ticket");
        ticketRec.Should().NotBeNull();
        ticketRec!.Name.Should().Contain("Protocol Deviation");
        ticketRec.Name.Should().Contain("102");
        ticketRec.TargetRoute.Should().Contain("/tickets");
        ticketRec.TargetRoute.Should().Contain("type=protocol_deviation");
        ticketRec.TargetRoute.Should().Contain("siteId=102");
        ticketRec.Category.Should().Be("[AI Copilot]");
        ticketRec.Confidence.Should().BeGreaterThanOrEqualTo(0.90);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void ClassifyIntent_FiltersSuggestions_BasedOnUserRoles()
    {
        var ctmsQuery = "view site monitoring visits ctms";

        // CRA role -> allowed CTMS
        var craResponse = IntentRoutingService.ClassifyIntent(ctmsQuery, new[] { "cra" });
        craResponse.Recommendations.Should().Contain(r => r.TargetRoute.StartsWith("/ctms"));

        // Lay public user -> prohibited CTMS
        var restrictedResponse = IntentRoutingService.ClassifyIntent(ctmsQuery, new[] { "unauthorized_role" });
        restrictedResponse.Recommendations.Should().NotContain(r => r.TargetRoute.StartsWith("/ctms"));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void ClassifyIntent_Performance_CompletesUnder300ms()
    {
        var roles = new[] { "site_investigator", "crc", "cra", "sponsor_admin" };
        var query = "log adverse event for subject 101 missing lab cert SOP";

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            IntentRoutingService.ClassifyIntent(query, roles);
        }
        stopwatch.Stop();

        // 100 iterations should take well under 300ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300);
    }
}
