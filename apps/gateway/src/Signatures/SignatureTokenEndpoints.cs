using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Gateway.Signatures;

/// <summary>
/// Body of a signature re-authentication request. <see cref="Username"/> is optional; when sent it must be the
/// caller's own. The password is only forwarded to the identity provider.
/// </summary>
public sealed record SignatureVerificationRequest(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("totp")] string? Totp,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("semantic_action")] string? SemanticAction,
    [property: JsonPropertyName("batch_id")] string? BatchId);

public sealed record SignatureTokenResponse(
    [property: JsonPropertyName("sig_token")] string SigToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

/// <summary>
/// The e-signature step-up endpoint: the signed-in caller re-enters their password, the gateway checks it with
/// Keycloak and returns a single-use, 60-second <c>X-Sig-Token</c> bound to one action (21 CFR 11.200(a)(1)).
/// @req:PRD-SYS-053
/// </summary>
public static partial class SignatureTokenEndpoints
{
    public const string Path = "/api/v1/auth/signature-verification";
    public const string RateLimitPolicy = "signature-verification";

    /// <summary>Attempts one signer may make per minute before the gateway answers 429.</summary>
    public const int AttemptsPerMinute = 5;

    private const string UsernameClaim = "preferred_username";
    private const int MaxActionLength = 512;

    public static IEndpointRouteBuilder MapSignatureTokenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Path, IssueSignatureTokenAsync)
            .WithName("IssueSignatureToken")
            .WithSummary("Re-authenticates the signer and issues a single-use e-signature token for one action.")
            .RequireRateLimiting(RateLimitPolicy)
            .Produces<SignatureTokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    public static async Task<Results<Ok<SignatureTokenResponse>, ValidationProblem, ProblemHttpResult>> IssueSignatureTokenAsync(
        SignatureVerificationRequest request,
        HttpContext httpContext,
        ICredentialVerifier credentials,
        IOptions<GatewaySigningOptions> signing,
        IClock clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(SignatureTokenEndpoints));
        var user = httpContext.User;
        string? subject = user.FindFirst(CadenceClaimTypes.Subject)?.Value;
        string? username = user.FindFirst(UsernameClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username))
        {
            return Problem(StatusCodes.Status403Forbidden, "SIGNER_IDENTITY_INCOMPLETE", "The sign-in token carries no subject or username to re-authenticate.");
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!string.IsNullOrWhiteSpace(request.Username) && !string.Equals(request.Username.Trim(), username, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(StatusCodes.Status403Forbidden, "SIGNER_MISMATCH", "An electronic signature can only be applied by the signed-in user.");
        }

        var roles = user.FindAll(GatewayIdentityForwarding.RoleClaimType).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList();
        if (!SigTokenIssuer.MayObtainSignatureToken(roles))
        {
            return Problem(StatusCodes.Status403Forbidden, "SIGNATURE_NOT_PERMITTED", "Auditors and inspectors cannot apply electronic signatures.");
        }

        var check = await credentials.VerifyAsync(username, request.Password!, request.Totp, cancellationToken).ConfigureAwait(false);
        if (check.Outcome == CredentialCheckOutcome.Unavailable)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "IDENTITY_PROVIDER_UNAVAILABLE", "Credentials could not be checked right now. Try again.");
        }

        if (check.Outcome != CredentialCheckOutcome.Verified || !string.Equals(check.Subject, subject, StringComparison.Ordinal))
        {
            logger.LogWarning("E-signature re-authentication failed for {Subject} ({SemanticAction}).", subject, request.SemanticAction);
            return Problem(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "The credentials entered are not valid for the signed-in user.");
        }

        string token = SigTokenIssuer.Issue(
            new SigTokenGrant(subject, username, roles, request.Action!, request.SemanticAction!, NullIfBlank(request.BatchId)),
            clock.UtcNow,
            signing.Value.GetSecretBytes());
        logger.LogInformation("E-signature token issued to {Subject} for {SemanticAction} on {Action}.", subject, request.SemanticAction, request.Action);

        httpContext.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(new SignatureTokenResponse(token, (int)SigTokenClaims.Lifetime.TotalSeconds));
    }

    private static Dictionary<string, string[]> Validate(SignatureVerificationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(request.Password))
        {
            errors["password"] = ["Re-enter your password to sign."];
        }

        if (request.Action is not { Length: > 0 and <= MaxActionLength } action
            || !action.StartsWith("/api/v1/", StringComparison.Ordinal)
            || action.Any(char.IsWhiteSpace)
            || action.Contains("..", StringComparison.Ordinal))
        {
            errors["action"] = ["Name the API path being signed, for example /api/v1/execution/forms/{id}/approve."];
        }

        if (request.SemanticAction is null || !SemanticActionPattern().IsMatch(request.SemanticAction))
        {
            errors["semantic_action"] = ["Name the signature's meaning as a namespaced action, for example execution.form.approve."];
        }

        return errors;
    }

    private static ProblemHttpResult Problem(int status, string code, string detail) =>
        TypedResults.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["error"] = code });

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*){1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticActionPattern();
}
