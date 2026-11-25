using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.IdentityModel.Tokens;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The e-signature step-up token: what the gateway issues and what a signature-gated endpoint accepts.
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
    public async Task Verify_AcceptsABoundTokenOnce()
    {
        var verifier = new SigTokenVerifier();
        string token = Issue("pi-01", ["Site Investigator"]);

        (await verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken)).Subject.Should().Be("pi-01");
        Func<Task> replay = () => verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);

        await replay.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
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
    public async Task Verify_WithAnotherSignerActionOrBatch_IsRejectedWithoutConsumingTheToken(string expectedUser, SigTokenBinding binding)
    {
        var verifier = new SigTokenVerifier();
        string token = Issue("pi-01", ["Site Investigator"]);

        Func<Task> misuse = () => verifier.VerifyAndConsumeAsync(token, expectedUser, binding, Secret, TestContext.Current.CancellationToken);

        await misuse.Should().ThrowAsync<UnauthorizedAccessException>();
        (await verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// A token older than its 60 seconds (plus 5 seconds of clock skew) is rejected.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Verify_ExpiredToken_IsRejected()
    {
        string token = Issue("pi-01", ["Site Investigator"], SystemClock.Instance.UtcNow.AddSeconds(-70));

        Func<Task> verify = () => new SigTokenVerifier().VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);

        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Only tokens signed with the gateway secret are accepted.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Verify_TokenSignedWithAnotherSecret_IsRejected()
    {
        string token = Issue("pi-01", ["Site Investigator"]);

        Func<Task> verify = () => new SigTokenVerifier().VerifyAndConsumeAsync(
            token, "pi-01", Binding, Encoding.UTF8.GetBytes("a-different-secret-that-is-long-enough-0123"), TestContext.Current.CancellationToken);

        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Hand-built tokens are refused when they lack sig_ver v3, live longer than 60 seconds, or carry an
    /// auditor role, even when correctly signed and bound.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Verify_TokensOutsideTheV3Contract_AreRejected()
    {
        var now = SystemClock.Instance.UtcNow.UtcDateTime;
        var verifier = new SigTokenVerifier();

        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(Build(now, now.AddSeconds(60), version: null), "pi-01", Binding, Secret, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>("a token without sig_ver predates v3");
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(Build(now, now.AddMinutes(10)), "pi-01", Binding, Secret, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>("the lifetime is capped at 60 seconds");
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(Build(now, now.AddSeconds(60), role: "auditor"), "pi-01", Binding, Secret, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<UnauthorizedAccessException>("auditors cannot sign");
        (await verifier.VerifyAndConsumeAsync(Build(now, now.AddSeconds(60)), "pi-01", Binding, Secret, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    /// <summary>
    /// The acceptance window, on the verifier's clock: from <c>iat - 5 s</c> until just before <c>exp + 5 s</c>.
    /// A token issued slightly ahead of the verifier's clock is accepted; one further ahead, or past
    /// <c>exp + 5 s</c>, is not.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData(-5, true)]
    [InlineData(-6, false)]
    [InlineData(0, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public async Task Verify_AcceptsATokenOnlyWithinItsWindow(int secondsAfterIssue, bool accepted)
    {
        var clock = new FakeClock();
        var verifier = new SigTokenVerifier(clock);
        string token = Issue("pi-01", ["Site Investigator"], clock.UtcNow.AddSeconds(-secondsAfterIssue));

        Func<Task> verify = () => verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);

        if (accepted)
        {
            await verify.Should().NotThrowAsync();
        }
        else
        {
            await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }

    /// <summary>
    /// A token used in the skew after its expiry cannot be used again in that skew. Before #5311 the replay cache
    /// forgot a use at <c>exp</c> while the verifier still accepted the token until <c>exp + 5 s</c>, so a second
    /// use in those five seconds succeeded.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Verify_ReplayInTheSkewAfterExpiry_IsRejected()
    {
        var clock = new FakeClock();
        var verifier = new SigTokenVerifier(clock);
        string token = Issue("pi-01", ["Site Investigator"], clock.UtcNow);
        clock.Advance(TimeSpan.FromSeconds(61));

        await verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        Func<Task> replay = () => verifier.VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);

        await replay.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    /// <summary>
    /// Single use is as wide as the store: two verifiers sharing one store accept a token once between them, while a
    /// verifier with its own process-local store (a second instance, or a restarted one) would accept it again. That
    /// is why a multi-instance host registers a durable store (ADR-2515).
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task Verify_SingleUseSpansExactlyTheSharedStore()
    {
        var clock = new FakeClock();
        var shared = new ReplayCache(clock);
        string token = Issue("pi-01", ["Site Investigator"], clock.UtcNow);

        await new SigTokenVerifier(shared, clock).VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);
        Func<Task> sameStore = () => new SigTokenVerifier(shared, clock).VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);
        Func<Task> ownStore = () => new SigTokenVerifier(clock).VerifyAndConsumeAsync(token, "pi-01", Binding, Secret, TestContext.Current.CancellationToken);

        await sameStore.Should().ThrowAsync<UnauthorizedAccessException>();
        await ownStore.Should().NotThrowAsync("a process-local store knows nothing of another instance's uses");
    }

    /// <summary>
    /// Dual-signer verification validates both tokens and requires distinct <c>jti</c> values before consuming either,
    /// so a rejected secondary token never burns a valid primary token.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task VerifyDualTokens_ValidatesBothBeforeConsumingEither()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock();
        var shared = new ReplayCache(clock);
        var verifier = new SigTokenVerifier(shared, clock);
        var batchBinding = new SigTokenBinding(Action, SemanticAction, "STUDY-01:SUBJ-01:DIN-01:BATCH-01");

        string primary = SigTokenIssuer.Issue(
            new SigTokenGrant("nurse-01", "nurse-01", ["Site CRC"], Action, SemanticAction, batchBinding.BatchId),
            clock.UtcNow,
            Secret);
        string wrongSignerSecondary = SigTokenIssuer.Issue(
            new SigTokenGrant("other-user", "other-user", ["Site Investigator"], Action, SemanticAction, batchBinding.BatchId),
            clock.UtcNow,
            Secret);

        // Same token passed twice must fail without burning it.
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(primary, "nurse-01", primary, "nurse-01", batchBinding, Secret, ct))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);

        // Wrong secondary signer must fail without burning primary.
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(primary, "nurse-01", wrongSignerSecondary, "pi-02", batchBinding, Secret, ct))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);

        // Valid secondary succeeds with the unburned primary token.
        string validSecondary = SigTokenIssuer.Issue(
            new SigTokenGrant("pi-02", "pi-02", ["Site Investigator"], Action, SemanticAction, batchBinding.BatchId),
            clock.UtcNow,
            Secret);
        var (verifiedPrimary, verifiedSecondary) = await verifier.VerifyAndConsumeAsync(
            primary, "nurse-01", validSecondary, "pi-02", batchBinding, Secret, ct);
        verifiedPrimary.Subject.Should().Be("nurse-01");
        verifiedSecondary.Subject.Should().Be("pi-02");

        // Replaying secondary paired with a fresh primary must fail AND leave the fresh primary unburned.
        string freshPrimary = SigTokenIssuer.Issue(
            new SigTokenGrant("nurse-01", "nurse-01", ["Site CRC"], Action, SemanticAction, batchBinding.BatchId),
            clock.UtcNow,
            Secret);
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(freshPrimary, "nurse-01", validSecondary, "pi-02", batchBinding, Secret, ct))
            .Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);

        string freshSecondary = SigTokenIssuer.Issue(
            new SigTokenGrant("pi-02", "pi-02", ["Site Investigator"], Action, SemanticAction, batchBinding.BatchId),
            clock.UtcNow,
            Secret);
        await verifier.Awaiting(v => v.VerifyAndConsumeAsync(freshPrimary, "nurse-01", freshSecondary, "pi-02", batchBinding, Secret, ct))
            .Should().NotThrowAsync();
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
