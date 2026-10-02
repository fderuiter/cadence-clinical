using System;
using System.Text;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Econsent;

/// <summary>
/// 21 CFR Part 11 step-up electronic signature verification tests for eConsent capture endpoints.
/// @req:PRD-SYS-053
/// </summary>
public sealed class EconsentEndpointsTests : IDisposable
{
    private const string ActionUrl = "/api/v1/econsent/templates/icf-001/versions/1/capture-consent";
    private const string SemanticAction = "econsent.consent.capture";
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("test-econsent-secret-key-32-bytes-long!!");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public EconsentEndpointsTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public void SigTokenVerifier_ValidTokenForEconsent_VerifiesAndConsumesSuccessfully()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], ActionUrl, SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        var verifiedJwt = verifier.VerifyAndConsumeSigToken(token, "subj-001", binding, Secret);

        verifiedJwt.Should().NotBeNull();
        verifiedJwt.Subject.Should().Be("subj-001");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public void SigTokenVerifier_ReusedTokenForEconsent_FailsClosedWithReauthenticationRequired()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], ActionUrl, SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        verifier.VerifyAndConsumeSigToken(token, "subj-001", binding, Secret);

        Action replay = () => verifier.VerifyAndConsumeSigToken(token, "subj-001", binding, Secret);
        replay.Should().Throw<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public void SigTokenVerifier_TokenForDifferentActionUrl_FailsClosedWithReauthenticationRequired()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], "/api/v1/econsent/templates/icf-001/versions/2/capture-consent", SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        Action verify = () => verifier.VerifyAndConsumeSigToken(token, "subj-001", binding, Secret);
        verify.Should().Throw<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
    }

    private EconsentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<EconsentDbContext>().UseSqlite(_connection).Options, TestFieldProtection.CreateEncryptor());
}
