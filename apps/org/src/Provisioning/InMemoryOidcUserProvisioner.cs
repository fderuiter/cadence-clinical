using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Time;

namespace Cadence.Modules.Org.Provisioning;

/// <summary>
/// Deterministic in-memory <see cref="IOidcUserProvisioner"/> used in local <c>Development</c> and <c>Testing</c>
/// environments when a live Keycloak Admin endpoint is not configured. Enforces the same tenant-binding, account reuse,
/// and welcome-email semantics as <see cref="KeycloakOidcUserProvisioner"/> without storing plaintext PII in logs.
/// </summary>
public sealed class InMemoryOidcUserProvisioner(IClock clock) : IOidcUserProvisioner
{
    private readonly ConcurrentDictionary<string, ProvisionedEntry> _byEmailHash = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<WelcomeEmailReceipt> _welcomeEmails = new();
    private readonly Lock _gate = new();

    /// <summary>Pseudonymous record of a dispatched welcome password-setup email.</summary>
    /// <param name="PersonId">The provisioned person identifier.</param>
    /// <param name="TenantCode">The tenant workspace code.</param>
    /// <param name="SentAt">UTC timestamp when the welcome email action was dispatched.</param>
    public sealed record WelcomeEmailReceipt(PersonId PersonId, string TenantCode, DateTimeOffset SentAt);

    /// <summary>Pseudonymous snapshot of a provisioned OIDC account for test verification.</summary>
    /// <param name="PersonId">The OIDC subject / kernel person identifier.</param>
    /// <param name="TenantCode">Bound tenant workspace code.</param>
    /// <param name="RealmRoles">Assigned Keycloak realm roles.</param>
    /// <param name="StudyCodes">Assigned study codes.</param>
    /// <param name="SiteCode">Assigned site code, if site-scoped.</param>
    public sealed record ProvisionedAccountSnapshot(
        PersonId PersonId,
        string TenantCode,
        IReadOnlyList<string> RealmRoles,
        IReadOnlyList<string> StudyCodes,
        string? SiteCode);

    /// <summary>Welcome email actions dispatched by this provisioner instance.</summary>
    public IReadOnlyList<WelcomeEmailReceipt> SentWelcomeEmails => [.. _welcomeEmails];

    /// <summary>Current pseudonymous account snapshots keyed by <see cref="PersonId"/>.</summary>
    public IReadOnlyDictionary<PersonId, ProvisionedAccountSnapshot> Accounts =>
        _byEmailHash.Values.ToDictionary(
            e => e.PersonId,
            e => new ProvisionedAccountSnapshot(e.PersonId, e.TenantCode, [.. e.RealmRoles], [.. e.StudyCodes], e.SiteCode));

    /// <inheritdoc />
    public Task<OidcProvisioningResult> ProvisionOrReuseAsync(OidcProvisioningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var emailKey = HashEmail(request.Email);
        lock (_gate)
        {
            if (_byEmailHash.TryGetValue(emailKey, out var existing))
            {
                if (!string.Equals(existing.TenantCode, request.TenantCode, StringComparison.Ordinal))
                {
                    throw new OidcProvisioningException(OrgErrors.CrossTenantIdentityConflict);
                }

                existing.Merge(request.RealmRoles, request.StudyCodes, request.SiteCode);
                return Task.FromResult(new OidcProvisioningResult(existing.PersonId, OidcProvisioningOutcome.ReusedExisting, WelcomeEmailSent: false));
            }

            if (request.KnownTenantPersonId is { } knownPersonId)
            {
                var reused = new ProvisionedEntry(knownPersonId, request.TenantCode, request.RealmRoles, request.StudyCodes, request.SiteCode);
                _byEmailHash[emailKey] = reused;
                return Task.FromResult(new OidcProvisioningResult(knownPersonId, OidcProvisioningOutcome.ReusedExisting, WelcomeEmailSent: false));
            }

            var personId = DerivePersonId(request.TenantCode, request.Email);
            var created = new ProvisionedEntry(personId, request.TenantCode, request.RealmRoles, request.StudyCodes, request.SiteCode);
            _byEmailHash[emailKey] = created;
            _welcomeEmails.Enqueue(new WelcomeEmailReceipt(personId, request.TenantCode, clock.UtcNow));
            return Task.FromResult(new OidcProvisioningResult(personId, OidcProvisioningOutcome.Created, WelcomeEmailSent: true));
        }
    }

    private static string HashEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-oidc-email:{normalized}")));
    }

    private static PersonId DerivePersonId(string tenantCode, string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-oidc-subject:{tenantCode}:{normalized}"));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return PersonId.FromGuid(new Guid(guidBytes));
    }

    private sealed class ProvisionedEntry
    {
        private readonly SortedSet<string> _realmRoles = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _studyCodes = new(StringComparer.Ordinal);

        public ProvisionedEntry(
            PersonId personId,
            string tenantCode,
            IEnumerable<string> realmRoles,
            IEnumerable<string> studyCodes,
            string? siteCode)
        {
            PersonId = personId;
            TenantCode = tenantCode;
            Merge(realmRoles, studyCodes, siteCode);
        }

        public PersonId PersonId { get; }

        public string TenantCode { get; }

        public IReadOnlyCollection<string> RealmRoles => _realmRoles;

        public IReadOnlyCollection<string> StudyCodes => _studyCodes;

        public string? SiteCode { get; private set; }

        public void Merge(IEnumerable<string> realmRoles, IEnumerable<string> studyCodes, string? siteCode)
        {
            foreach (var role in realmRoles.Where(r => !string.IsNullOrWhiteSpace(r)))
            {
                _realmRoles.Add(role);
            }

            foreach (var study in studyCodes.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                _studyCodes.Add(study);
            }

            if (!string.IsNullOrWhiteSpace(siteCode))
            {
                SiteCode = siteCode;
            }
        }
    }
}
