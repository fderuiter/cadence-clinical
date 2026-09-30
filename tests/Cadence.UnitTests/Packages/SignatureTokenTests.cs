using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The e-signature step-up token: what the gateway issues and what a signature-gated endpoint accepts.
/// @req:SRS-SYS-005
/// </summary>
public class SignatureTokenTests
{
    private const string Action = "/api/v1/execution/forms/F-1/approve";
    private const string SemanticAction = "execution.form.approve";

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("signature-token-test-secret-0123456789abcdef");
    private static readonly SigTokenBinding Binding = new(Action, SemanticAction);

    /// <summary>
    /// The issued token carries sub, username, roles, action, semantic_action, sig_ver v3, iat, exp = iat + 60
    /// and a unique jti.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public void Issue_WritesTheV3ContractClaims_WithASixtySecondLifetime()
    {
        var issuedAt = SystemClock.Instance.UtcNow;

        var first = Read(Issue("pi-01", ["Site Investigator"], issuedAt));
        var second = Read(Issue("pi-01", ["Site Investigator"], issuedAt));

        first.Subject.Should().Be("pi-01");
        Claim(first, SigTokenClaims.Username).Should().Be("pi-01");
        Claim(first, SigTokenClaims.Roles).Should().Be("Site Investigator");
        Claim(first, SigTokenClaims.Action).Should().Be(Action);
        Claim(first, SigTokenClaims.SemanticAction).Should().Be(SemanticAction);
        Claim(first, SigTokenClaims.Version).Should().Be("v3");
        (first.ValidTo - first.IssuedAt).Should().Be(TimeSpan.FromSeconds(60));
        first.Header.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
        first.Id.Should().NotBeNullOrEmpty().And.NotBe(second.Id);
    }

    /// <summary>
    /// Auditors and inspectors are read-only and never receive a signature token.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData("Auditor")]
    [InlineData("inspector")]
    [InlineData("Regulatory Inspector")]
    public void Issue_ForAnAuditor_IsRefused(string role)
    {
        Action issue = () => Issue("aud-01", ["CRA", role]);

        issue.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// A token bound to this signer and action verifies once; the replay is rejected.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public void Verify_AcceptsABoundTokenOnce()
    {
        var verifier = new SigTokenVerifier();
        string token = Issue("pi-01", ["Site Investigator"]);

        verifier.VerifyAndConsumeSigToken(token, "pi-01", Binding, Secret).Subject.Should().Be("pi-01");
        Action replay = () => verifier.VerifyAndConsumeSigToken(token, "pi-01", Binding, Secret);

        replay.Should().Throw<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
    }

    public static TheoryData<string, SigTokenBinding> Mismatches => new()
    {
        { "another signer", Binding },
        { "pi-01", new SigTokenBinding("/api/v1/execution/forms/F-2/approve", SemanticAction) },
        { "pi-01", new SigTokenBinding(Action, "quality.capa.close") },
        { "pi-01", new SigTokenBinding(Action, SemanticAction, BatchId: "batch-hash") },
    };

    /// <summary>
    /// A token for form A cannot sign form B, another action, another batch, or for another user, and a
    /// rejected attempt leaves the token usable for what it was issued for.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [MemberData(nameof(Mismatches))]
    public void Verify_WithAnotherSignerActionOrBatch_IsRejectedWithoutConsumingTheToken(string expectedUser, SigTokenBinding binding)
    {
        var verifier = new SigTokenVerifier();
        string token = Issue("pi-01", ["Site Investigator"]);

        Action misuse = () => verifier.VerifyAndConsumeSigToken(token, expectedUser, binding, Secret);

        misuse.Should().Throw<UnauthorizedAccessException>();
        verifier.VerifyAndConsumeSigToken(token, "pi-01", Binding, Secret).Should().NotBeNull();
    }

    /// <summary>
    /// A token older than its 60 seconds (plus 5 seconds of clock skew) is rejected.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public void Verify_ExpiredToken_IsRejected()
    {
        string token = Issue("pi-01", ["Site Investigator"], SystemClock.Instance.UtcNow.AddSeconds(-70));

        Action verify = () => new SigTokenVerifier().VerifyAndConsumeSigToken(token, "pi-01", Binding, Secret);

        verify.Should().Throw<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Only tokens signed with the gateway secret are accepted.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public void Verify_TokenSignedWithAnotherSecret_IsRejected()
    {
        string token = Issue("pi-01", ["Site Investigator"]);

        Action verify = () => new SigTokenVerifier().VerifyAndConsumeSigToken(
            token, "pi-01", Binding, Encoding.UTF8.GetBytes("a-different-secret-that-is-long-enough-0123"));

        verify.Should().Throw<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Hand-built tokens are refused when they lack sig_ver v3, live longer than 60 seconds, or carry an
    /// auditor role, even when correctly signed and bound.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public void Verify_TokensOutsideTheV3Contract_AreRejected()
    {
        var now = SystemClock.Instance.UtcNow.UtcDateTime;
        var verifier = new SigTokenVerifier();

        verifier.Invoking(v => v.VerifyAndConsumeSigToken(Build(now, now.AddSeconds(60), version: null), "pi-01", Binding, Secret))
            .Should().Throw<UnauthorizedAccessException>("a token without sig_ver predates v3");
        verifier.Invoking(v => v.VerifyAndConsumeSigToken(Build(now, now.AddMinutes(10)), "pi-01", Binding, Secret))
            .Should().Throw<UnauthorizedAccessException>("the lifetime is capped at 60 seconds");
        verifier.Invoking(v => v.VerifyAndConsumeSigToken(Build(now, now.AddSeconds(60), role: "auditor"), "pi-01", Binding, Secret))
            .Should().Throw<UnauthorizedAccessException>("auditors cannot sign");
        verifier.VerifyAndConsumeSigToken(Build(now, now.AddSeconds(60)), "pi-01", Binding, Secret).Should().NotBeNull();
    }

    private static string Issue(string subject, IReadOnlyList<string> roles, DateTimeOffset? issuedAt = null) =>
        SigTokenIssuer.Issue(new SigTokenGrant(subject, subject, roles, Action, SemanticAction), issuedAt ?? SystemClock.Instance.UtcNow, Secret);

    private static string Build(DateTime issuedAt, DateTime expires, string? version = "v3", string role = "investigator")
    {
        var claims = new Dictionary<string, object>
        {
            [SigTokenClaims.Action] = Action,
            [SigTokenClaims.SemanticAction] = SemanticAction,
            [SigTokenClaims.Roles] = new[] { role },
            ["jti"] = Guid.NewGuid().ToString(),
        };
        if (version is not null)
        {
            claims[SigTokenClaims.Version] = version;
        }

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", "pi-01")]),
            Claims = claims,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Secret), SecurityAlgorithms.HmacSha256),
        }));
    }

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static string? Claim(JwtSecurityToken token, string type) => token.Claims.FirstOrDefault(c => c.Type == type)?.Value;
}
