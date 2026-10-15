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
/// @req:PRD-SYS-051
/// </summary>
public class AiGatewayIntentTests
{
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
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
        aeRec.Category.Should().Be("[AI Copilot]");
        aeRec.Confidence.Should().Be(1.0);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
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
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
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
        ticketRec.Category.Should().Be("[AI Copilot]");
        ticketRec.Confidence.Should().Be(1.0);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
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
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
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

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-051")]
    public void ClassifyIntent_Performance_CompletesUnder300ms()
    {
        var roles = new[] { "site_investigator", "crc", "cra", "sponsor_admin" };
        var query = "log adverse event for subject 101 missing lab cert SOP";

        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            RuleBasedIntentRouter.ClassifyIntent(query, roles);
        }
        stopwatch.Stop();

        // 100 iterations should take well under 300ms
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300);
    }
}
