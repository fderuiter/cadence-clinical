using System;
using System.Security.Claims;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Coverage for <see cref="AmbientAuditContextAccessor"/>: the caller's <c>sub</c> claim is the actor, a
/// declared <see cref="ServiceAccount"/> overrides it, and with neither the actor is unresolved and reading
/// it throws instead of defaulting to <c>"system"</c> (#4767). The change reason keeps its default.
/// </summary>
public class CurrentAuditContextAccessorTests
{
    private sealed class FakeHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private static AmbientAuditContextAccessor CreateAccessor(string? subClaim = null, string? changeReasonHeader = null)
    {
        var context = new DefaultHttpContext();

        if (subClaim is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subClaim)]));
        }

        if (changeReasonHeader is not null)
        {
            context.Request.Headers["X-Change-Reason"] = changeReasonHeader;
        }

        return new AmbientAuditContextAccessor(new FakeHttpContextAccessor(context));
    }

    // ---- AmbientAuditContextAccessor.UserId ----------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void UserId_GivenNoHttpContext_Throws()
    {
        var accessor = new AmbientAuditContextAccessor(new FakeHttpContextAccessor(null));

        FluentActions.Invoking(() => accessor.UserId).Should().Throw<AuditActorUnresolvedException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void UserId_GivenNoSubClaim_Throws()
    {
        var accessor = CreateAccessor(subClaim: null);

        FluentActions.Invoking(() => accessor.UserId).Should().Throw<AuditActorUnresolvedException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void UserId_GivenEmptySubClaim_Throws()
    {
        var accessor = CreateAccessor(subClaim: string.Empty);

        FluentActions.Invoking(() => accessor.UserId).Should().Throw<AuditActorUnresolvedException>();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void UserId_GivenWhitespaceOnlySubClaim_Throws()
    {
        var accessor = CreateAccessor(subClaim: "   ");

        FluentActions.Invoking(() => accessor.UserId).Should().Throw<AuditActorUnresolvedException>();
    }

    [Fact]
    public void UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault()
    {
        var accessor = CreateAccessor(subClaim: "user-42");

        accessor.UserId.Should().Be("user-42");
    }

    // ---- AmbientAuditContextAccessor.ChangeReason ----------------------------------------------

    [Fact]
    public void ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault()
    {
        var accessor = new AmbientAuditContextAccessor(new FakeHttpContextAccessor(null));

        accessor.ChangeReason.Should().Be("system_operation");
    }

    [Fact]
    public void ChangeReason_GivenNoHeader_ReturnsSystemOperationDefault()
    {
        var accessor = CreateAccessor(changeReasonHeader: null);

        accessor.ChangeReason.Should().Be("system_operation");
    }

    [Fact]
    public void ChangeReason_GivenExplicitlyEmptyHeader_ReturnsSameDefaultAsAbsentHeader()
    {
        // Boundary check: an explicitly-empty header ("", present but blank) must resolve to the same
        // default as a fully-absent one, not be distinguished from it.
        var accessor = CreateAccessor(changeReasonHeader: string.Empty);

        accessor.ChangeReason.Should().Be("system_operation");
    }

    [Fact]
    public void ChangeReason_GivenWhitespaceOnlyHeader_ReturnsSameDefaultAsAbsentHeader()
    {
        // Boundary check (AC): whitespace-only, not merely absent, must still resolve to the default —
        // one consistent falsy-check, not a null-only check that would treat "" (or "   ") differently.
        var accessor = CreateAccessor(changeReasonHeader: "   ");

        accessor.ChangeReason.Should().Be("system_operation");
    }

    [Fact]
    public void ChangeReason_GivenHeaderPresent_ReturnsActualHeaderValueNotDefault()
    {
        var accessor = CreateAccessor(changeReasonHeader: "Protocol amendment v2");

        accessor.ChangeReason.Should().Be("Protocol amendment v2");
    }

    [Fact]
    public void UserIdAndChangeReason_GivenBothClaimAndHeaderPresent_ReturnActualValuesNotDefaults()
    {
        var accessor = CreateAccessor(subClaim: "user-7", changeReasonHeader: "Manual correction");

        accessor.UserId.Should().Be("user-7");
        accessor.ChangeReason.Should().Be("Manual correction");
    }

    // ---- Service accounts ---------------------------------------------------------------------------

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void RunAs_WithNoHttpContext_AttributesToTheServiceAccount()
    {
        var accessor = new AmbientAuditContextAccessor(new FakeHttpContextAccessor(null));

        accessor.RunAs(ServiceAccounts.EConsentConsumer);

        accessor.UserId.Should().Be("svc:econsent-consumer");
        accessor.ChangeReason.Should().Be("Automated consent-completion enrollment");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public void RunAs_OverridesTheAmbientCaller()
    {
        var accessor = CreateAccessor(subClaim: "user-7", changeReasonHeader: "Manual correction");

        accessor.RunAs(ServiceAccounts.EConsentConsumer);

        accessor.UserId.Should().Be("svc:econsent-consumer");
    }

    [Theory]
    [GxPRequirement("PRD-SYS-001")]
    [InlineData("system")]
    [InlineData("svc:")]
    [InlineData("")]
    public void ServiceAccount_WithoutANamedSvcId_IsRejected(string userId)
    {
        FluentActions.Invoking(() => new ServiceAccount(userId, "reason")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeReason_GivenRfc8187Header_ReturnsTheDecodedText()
    {
        var accessor = CreateAccessor(changeReasonHeader: "UTF-8''IRB%20approval%20%E2%80%94%20%E6%89%BF%E8%AA%8D");

        accessor.ChangeReason.Should().Be("IRB approval — 承認");
    }
}
