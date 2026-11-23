using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Cadence.Database.Tenancy;

public interface ICurrentTenantAccessor
{
    Guid? GetCurrentTenantId();

    ValueTask<Guid?> GetCurrentTenantIdAsync(CancellationToken cancellationToken = default);

    bool IsSystemSuppressed { get; }

    bool HasHttpRequestWithoutTenant { get; }

    bool IsCurrentTenantReadOnly { get; }

    bool IsHttpWriteExemptFromExpiry { get; }

    bool IsHttpProductFamilyExempt { get; }

    IReadOnlyList<string>? CurrentTenantProductFamilies { get; }

    TenantScopingCaps? CurrentTenantScoping { get; }
}

public interface ITenantScope
{
    IDisposable Bind(Guid tenantId);

    IDisposable Bind(string tenantCode);

    IDisposable Suppress();
}

public sealed class CurrentTenantAccessor(
    ITenantCodeResolver tenantCodeResolver,
    IHttpContextAccessor? httpContextAccessor = null)
    : ICurrentTenantAccessor, ITenantScope, ITenantAccessVerifier
{
    private static readonly string RegulatedExportPolicy =
        PermissionPolicyProvider.PolicyName("etmf_document:export");

    private static readonly AsyncLocal<Guid?> BoundTenantLocal = new();
    private static readonly AsyncLocal<string?> BoundTenantCodeLocal = new();
    private static readonly AsyncLocal<bool> SuppressedLocal = new();

    private string? _nonHttpFallbackTenantCode;

    public bool IsSystemSuppressed => SuppressedLocal.Value;

    public bool IsCurrentTenantReadOnly =>
        !SuppressedLocal.Value
        && httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal { TenantReadOnly: true };

    public bool IsHttpWriteExemptFromExpiry
    {
        get
        {
            Endpoint? endpoint = httpContextAccessor?.HttpContext?.GetEndpoint();
            if (endpoint is null)
            {
                return false;
            }

            if (endpoint.Metadata.GetMetadata<AllowExpiredTenantWriteMetadata>() is not null)
            {
                return true;
            }

            return endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Any(authorize => string.Equals(authorize.Policy, RegulatedExportPolicy, StringComparison.Ordinal));
        }
    }

    public bool IsHttpProductFamilyExempt
    {
        get
        {
            if (IsHttpWriteExemptFromExpiry)
            {
                return true;
            }

            Endpoint? endpoint = httpContextAccessor?.HttpContext?.GetEndpoint();
            return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || endpoint?.Metadata.GetMetadata<AllowBaselineProductFamilyMetadata>() is not null;
        }
    }

    public IReadOnlyList<string>? CurrentTenantProductFamilies
    {
        get
        {
            if (SuppressedLocal.Value)
            {
                return null;
            }

            return httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] is Principal
            {
                TenantId: { Length: > 0 },
                ProductFamilies: { } families,
            }
                ? families
                : null;
        }
    }

    public TenantScopingCaps? CurrentTenantScoping =>
        SuppressedLocal.Value
            ? null
            : (httpContextAccessor?.HttpContext?.Items[GatewayAuthenticationHandler.PrincipalItemKey] as Principal)?.TenantScoping;

    public bool HasHttpRequestWithoutTenant
    {
        get
        {
            if (SuppressedLocal.Value || BoundTenantLocal.Value.HasValue || !string.IsNullOrWhiteSpace(BoundTenantCodeLocal.Value))
            {
                return false;
            }

            HttpContext? ctx = httpContextAccessor?.HttpContext;
            return ctx is not null && string.IsNullOrWhiteSpace(CallerScope.TenantId(ctx));
        }
    }

    /// <summary>
    /// Configures a fallback tenant code strictly for non-HTTP test fixture seeding (<c>HttpContext == null</c>).
    /// HTTP requests never use this fallback and always resolve <c>X-Tenant-Id</c> fail-closed (ADR-2514).
    /// </summary>
    public void UseNonHttpSeedingFallbackTenant(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        _nonHttpFallbackTenantCode = tenantCode.Trim();
    }

    public Guid? GetCurrentTenantId()
    {
        if (SuppressedLocal.Value)
        {
            return null;
        }

        if (BoundTenantLocal.Value is { } explicitGuid)
        {
            return explicitGuid;
        }

        if (!string.IsNullOrWhiteSpace(BoundTenantCodeLocal.Value))
        {
            return tenantCodeResolver.ResolveTenantId(BoundTenantCodeLocal.Value);
        }

        HttpContext? httpContext = httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return _nonHttpFallbackTenantCode is not null
                ? tenantCodeResolver.ResolveTenantId(_nonHttpFallbackTenantCode)
                : null;
        }

        string? tenantCode = CallerScope.TenantId(httpContext);
        return string.IsNullOrWhiteSpace(tenantCode)
            ? null
            : tenantCodeResolver.ResolveTenantId(tenantCode);
    }

    public async ValueTask<Guid?> GetCurrentTenantIdAsync(CancellationToken cancellationToken = default)
    {
        if (SuppressedLocal.Value)
        {
            return null;
        }

        if (BoundTenantLocal.Value is { } explicitGuid)
        {
            return explicitGuid;
        }

        if (!string.IsNullOrWhiteSpace(BoundTenantCodeLocal.Value))
        {
            return await tenantCodeResolver.ResolveTenantIdAsync(BoundTenantCodeLocal.Value, cancellationToken).ConfigureAwait(false);
        }

        HttpContext? httpContext = httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return _nonHttpFallbackTenantCode is not null
                ? await tenantCodeResolver.ResolveTenantIdAsync(_nonHttpFallbackTenantCode, cancellationToken).ConfigureAwait(false)
                : null;
        }

        string? tenantCode = CallerScope.TenantId(httpContext);
        return string.IsNullOrWhiteSpace(tenantCode)
            ? null
            : await tenantCodeResolver.ResolveTenantIdAsync(tenantCode, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<bool> IsActiveTenantAsync(string tenantCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantCode))
        {
            return ValueTask.FromResult(false);
        }

        return CheckActiveAsync(tenantCode, cancellationToken);
    }

    private async ValueTask<bool> CheckActiveAsync(string tenantCode, CancellationToken cancellationToken)
    {
        Guid? resolved = await tenantCodeResolver.ResolveTenantIdAsync(tenantCode, cancellationToken).ConfigureAwait(false);
        return resolved.HasValue && resolved.Value != Guid.Empty;
    }

    public IDisposable Bind(Guid tenantId)
    {
        Guid? prevId = BoundTenantLocal.Value;
        string? prevCode = BoundTenantCodeLocal.Value;
        BoundTenantLocal.Value = tenantId;
        BoundTenantCodeLocal.Value = null;
        return new ScopeReset(() =>
        {
            BoundTenantLocal.Value = prevId;
            BoundTenantCodeLocal.Value = prevCode;
        });
    }

    public IDisposable Bind(string tenantCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantCode);
        Guid? prevId = BoundTenantLocal.Value;
        string? prevCode = BoundTenantCodeLocal.Value;
        BoundTenantLocal.Value = null;
        BoundTenantCodeLocal.Value = tenantCode.Trim();
        return new ScopeReset(() =>
        {
            BoundTenantLocal.Value = prevId;
            BoundTenantCodeLocal.Value = prevCode;
        });
    }

    public IDisposable Suppress()
    {
        bool prev = SuppressedLocal.Value;
        SuppressedLocal.Value = true;
        return new ScopeReset(() => SuppressedLocal.Value = prev);
    }

    private sealed class ScopeReset(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose();
            }
        }
    }
}
