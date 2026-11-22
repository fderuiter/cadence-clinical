using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Subjects;
using Cadence.Core.Domain;
using Cadence.Security.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Execution.Infrastructure.Security;

/// <summary>
/// Verifies and consumes the caller's single-use <c>X-Sig-Token</c> against
/// <see cref="IWithdrawalSignatureVerifier.Action"/> and <see cref="IWithdrawalSignatureVerifier.SemanticAction"/>
/// when Execution handles a withdrawal over HTTP without delegating consumption to the Clinical Kernel.
/// </summary>
public sealed class HttpWithdrawalSignatureVerifier(
    IHttpContextAccessor httpContextAccessor,
    SigTokenVerifier sigTokenVerifier,
    IOptionsMonitor<GatewayAuthenticationOptions> gatewayOptions) : IWithdrawalSignatureVerifier
{
    /// <inheritdoc />
    public async Task<Result<bool>> VerifyAndConsumeIfHttpAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is not { } httpContext)
        {
            return true;
        }

        string? userId = httpContext.User.FindFirst(CadenceClaimTypes.Subject)?.Value;
        string? gatewaySecret = gatewayOptions.Get(GatewayAuthenticationHandler.SchemeName).GatewaySecret
            ?? gatewayOptions.CurrentValue.GatewaySecret;

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(gatewaySecret))
        {
            return DomainError.Unauthorized(SigTokenVerifier.ReauthenticationRequired, SigTokenVerifier.ReauthenticationRequired);
        }

        byte[] secretBytes = GatewayIdentity.EncodeSecret(gatewaySecret);

        try
        {
            var token = await sigTokenVerifier.VerifyAndConsumeAsync(
                httpContext.Request.Headers[GatewayHeaders.SigToken].ToString(),
                userId,
                new SigTokenBinding(IWithdrawalSignatureVerifier.Action, IWithdrawalSignatureVerifier.SemanticAction),
                secretBytes,
                cancellationToken).ConfigureAwait(false);
            httpContext.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] = token;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return DomainError.Unauthorized(SigTokenVerifier.ReauthenticationRequired, SigTokenVerifier.ReauthenticationRequired);
        }
    }
}
