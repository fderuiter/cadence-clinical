using System;
using System.Security.Claims;

using Cadence.Apps.Gateway;
using Cadence.Security.Authentication;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace Cadence.UnitTests.Gateway;

/// <summary>
/// The gateway forwards the caller's study assignments from the sign-in token, signed with the rest of the identity, so
/// study-scoped routes can decide from them (ADR-2516). Nothing the browser sends can add one.
/// </summary>
public sealed class GatewayStudyAssignmentTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-192")]
    public void TheStudyAssignmentsInTheToken_AreForwardedAndSigned()
    {
        var context = ContextFor(new Claim(CadenceClaimTypes.Subject, "user-1"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-A"),
            new Claim(CadenceClaimTypes.StudyIds, "STUDY-B"),
            new Claim(CadenceClaimTypes.StudyIds, "BAD,LIST"));
        context.Request.Headers[GatewayHeaders.StudyIds] = "STUDY-C";

        var identity = GatewayIdentityForwarding.CreateIdentity(context);

        identity!.StudyIds.Should().Equal("STUDY-A", "STUDY-B");
        var headers = identity.ToSignedHeaders(IssuedAt, GatewayIdentity.EncodeSecret(new string('k', 32)));
        headers[GatewayHeaders.StudyIds].Should().Be("STUDY-A,STUDY-B");
    }

    [Fact]
    public void ATokenWithoutStudyAssignments_ForwardsNone()
    {
        var identity = GatewayIdentityForwarding.CreateIdentity(ContextFor(new Claim(CadenceClaimTypes.Subject, "user-1")));

        identity!.StudyIds.Should().BeEmpty();
        identity.ToSignedHeaders(IssuedAt, GatewayIdentity.EncodeSecret(new string('k', 32)))
            .Should().NotContainKey(GatewayHeaders.StudyIds);
    }

    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static DefaultHttpContext ContextFor(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
}
