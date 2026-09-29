using System.Security.Claims;

using Cadence.Apps.Execution.Infrastructure.Security;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace Cadence.UnitTests.Execution.Security;

/// <summary>
/// Coverage for `MIG-SUBJ-017`'s `ICurrentAuditContextAccessor` implementations: the
/// present/absent/whitespace-only matrix `HttpCurrentAuditContextAccessor` resolves through its single,
/// shared fallback path (closing Rule 9's two-idiom split), and `SystemAuditContextAccessor`'s fixed,
/// non-configurable pubsub identity.
/// </summary>
public class CurrentAuditContextAccessorTests
{
    private sealed class FakeHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }

    private static HttpCurrentAuditContextAccessor CreateAccessor(string? subClaim = null, string? changeReasonHeader = null)
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

        return new HttpCurrentAuditContextAccessor(new FakeHttpContextAccessor(context));
    }

    // ---- HttpCurrentAuditContextAccessor.UserId ----------------------------------------------------

    [Fact]
    public void UserId_GivenNoHttpContext_ReturnsSystemDefault()
    {
        var accessor = new HttpCurrentAuditContextAccessor(new FakeHttpContextAccessor(null));

        accessor.UserId.Should().Be("system");
    }

    [Fact]
    public void UserId_GivenNoSubClaim_ReturnsSystemDefault()
    {
        var accessor = CreateAccessor(subClaim: null);

        accessor.UserId.Should().Be("system");
    }

    [Fact]
    public void UserId_GivenEmptySubClaim_ReturnsSystemDefault()
    {
        var accessor = CreateAccessor(subClaim: string.Empty);

        accessor.UserId.Should().Be("system");
    }

    [Fact]
    public void UserId_GivenWhitespaceOnlySubClaim_ReturnsSystemDefault()
    {
        var accessor = CreateAccessor(subClaim: "   ");

        accessor.UserId.Should().Be("system");
    }

    [Fact]
    public void UserId_GivenSubClaimPresent_ReturnsActualClaimValueNotDefault()
    {
        var accessor = CreateAccessor(subClaim: "user-42");

        accessor.UserId.Should().Be("user-42");
    }

    // ---- HttpCurrentAuditContextAccessor.ChangeReason ----------------------------------------------

    [Fact]
    public void ChangeReason_GivenNoHttpContext_ReturnsSystemOperationDefault()
    {
        var accessor = new HttpCurrentAuditContextAccessor(new FakeHttpContextAccessor(null));

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

    // ---- SystemAuditContextAccessor -----------------------------------------------------------------

    [Fact]
    public void SystemAuditContextAccessor_UserId_AlwaysReturnsFixedPubsubIdentity()
    {
        var accessor = new SystemAuditContextAccessor();

        accessor.UserId.Should().Be("system-pubsub");
    }

    [Fact]
    public void SystemAuditContextAccessor_ChangeReason_AlwaysReturnsFixedAutomatedEnrollmentReason()
    {
        var accessor = new SystemAuditContextAccessor();

        accessor.ChangeReason.Should().Be("Automated consent-completion enrollment");
    }

    [Fact]
    public void SystemAuditContextAccessor_ValuesAreCompileTimeConstantsNotConfigurable()
    {
        // FieldInfo.IsLiteral is true only for `const` fields (never `static readonly` or
        // configuration-sourced values) — matching the legacy subscriber's hardcoded pubsub identity.
        typeof(SystemAuditContextAccessor).GetField(nameof(SystemAuditContextAccessor.SystemUserId))!
            .IsLiteral.Should().BeTrue("the pubsub identity must be a hardcoded compile-time constant, never sourced from configuration");
        typeof(SystemAuditContextAccessor).GetField(nameof(SystemAuditContextAccessor.SystemChangeReason))!
            .IsLiteral.Should().BeTrue("the pubsub change reason must be a hardcoded compile-time constant, never sourced from configuration");
    }
}
