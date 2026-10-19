using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// A denied command returns the evaluator's explanation and records the conditions it was decided on in the audit
/// chain and the <c>DecisionDenied</c> event (ADR-2482, K-AUTH-2, K-AUTH-6). Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelExplanationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.Authorizer.Policy = null;
        }
    }

    /// <summary>
    /// The caller gets a 403 whose explanation lists met and unmet conditions with their evidence, and the denial's
    /// audit entry and event carry the same conditions, so the audit trail records what the denial rested on.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "Denial_IsExplained_AndAuditedWithItsConditions")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    [KernelRule("K-AUTH-2")]
    [KernelRule("K-AUTH-6")]
    public async Task DenialIsExplainedAndAuditedWithItsConditions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var grant = NodeRef.For(AttestationId.New(), 1);
        var met = EvaluatedCondition.Satisfied("tenant.member", "Member of the platform tenant", grant);
        var unmet = EvaluatedCondition.Unsatisfied("grant.present", "Hold tenant.provision");
        fixture.Authorizer.Policy = (_, _) => AuthorizationDecision.Deny("Missing grant: tenant.provision", met, unmet);
        var command = new ProvisionTenant(TenantId.New(), "EXPL", "Explained Pharma", Guid.NewGuid().ToString(), "Integration test");

        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);

        result.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        var explanation = result.Error.Explanation!;
        explanation.Claim.Should().Be("ProvisionTenant is not permitted.");
        explanation.Producer.Should().Be(AuthorizationDecision.ExplanationProducer);
        explanation.Conditions.Should().Equal(met, unmet);
        explanation.NextActions.Should().Equal(new NextAction("Hold tenant.provision"));

        await using var db = fixture.CreateAdminContext();
        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.TenantId == command.TenantId, ct);
        var eventPayload = (await db.DomainEvents.AsNoTracking().SingleAsync(e => e.TenantId == command.TenantId, ct)).Payload;
        foreach (var recorded in new[] { audit.Changes, eventPayload })
        {
            using var json = JsonDocument.Parse(recorded);
            json.RootElement.GetProperty("reason").GetString().Should().Be("Missing grant: tenant.provision");
            var conditions = json.RootElement.GetProperty("conditions").EnumerateArray().ToList();
            conditions.Select(c => (c.GetProperty("code").GetString(), c.GetProperty("met").GetBoolean()))
                .Should().Equal(("tenant.member", true), ("grant.present", false));
            conditions[0].GetProperty("evidence").EnumerateArray().Select(e => e.GetString()).Should().Equal(grant.ToString());
        }
    }
}
