using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Commercial packaging tiers defined in <c>docs/strategy/PRODUCT_CATALOG.md</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommercialTier>))]
public enum CommercialTier
{
    Individual,
    Select,
    Complete,
}

/// <summary>
/// Lifecycle state of a tenant's commercial <c>Subscription</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionStatus>))]
public enum SubscriptionStatus
{
    Trialing,
    Active,
    Expired,
}

/// <summary>
/// Commercial product families built over the shared Clinical Kernel (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Baseline kernel capabilities (audit trails, electronic signatures, and authorization checks) are never gated by family.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductFamily>))]
public enum ProductFamily
{
    Blueprint,
    Capture,
    Orbit,
    Engage,
    Allocate,
    Vigil,
    Lens,
    Assure,
}

/// <summary>
/// Structured study scoping parameters and purchase-order / quote confirmation required before a commercial
/// subscription transitions to <see cref="SubscriptionStatus.Active"/> (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// Live payment card processing is intentionally out of scope.
/// </summary>
public sealed record SubscriptionScoping(
    string StudyPhase,
    int StudyDurationMonths,
    IReadOnlyList<string> Countries,
    int SiteCount,
    int ParticipantCount,
    int ActiveStudyCount,
    string BillingConfirmation);

/// <summary>
/// Validates and normalizes <see cref="SubscriptionScoping"/> inputs, returning RFC 9457 field-keyed validation messages.
/// </summary>
public static class SubscriptionScopingValidator
{
    public const int MaxStudyDurationMonths = 240;
    public const int MaxSiteCount = 10_000;
    public const int MaxParticipantCount = 1_000_000;
    public const int MaxActiveStudyCount = 1_000;
    public const int MaxBillingConfirmationLength = 1_024;

    public static IReadOnlyDictionary<string, string[]> Validate(
        string? studyPhase,
        int? studyDurationMonths,
        IReadOnlyList<string>? countries,
        int? siteCount,
        int? participantCount,
        int? activeStudyCount,
        string? billingConfirmation)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        string trimmedPhase = (studyPhase ?? string.Empty).Trim();
        if (trimmedPhase.Length == 0)
        {
            errors["study_phase"] = ["Study phase is required."];
        }
        else if (trimmedPhase.Length > 64)
        {
            errors["study_phase"] = ["Study phase must be 64 characters or fewer."];
        }

        if (studyDurationMonths is not { } duration || duration <= 0 || duration > MaxStudyDurationMonths)
        {
            errors["study_duration_months"] = [$"Study duration in months must be between 1 and {MaxStudyDurationMonths}."];
        }

        var normalizedCountries = NormalizeCountries(countries);
        if (normalizedCountries.Length == 0)
        {
            errors["countries"] = ["At least one participating country is required."];
        }
        else if (normalizedCountries.Length > 250 || normalizedCountries.Any(c => c.Length > 64))
        {
            errors["countries"] = ["Countries must contain at most 250 entries of 64 characters or fewer."];
        }

        if (siteCount is not { } sites || sites <= 0 || sites > MaxSiteCount)
        {
            errors["site_count"] = [$"Planned site count must be between 1 and {MaxSiteCount:N0}."];
        }

        if (participantCount is not { } participants || participants <= 0 || participants > MaxParticipantCount)
        {
            errors["participant_count"] = [$"Planned participant count must be between 1 and {MaxParticipantCount:N0}."];
        }

        if (activeStudyCount is not { } studies || studies <= 0 || studies > MaxActiveStudyCount)
        {
            errors["active_study_count"] = [$"Concurrent active study count must be between 1 and {MaxActiveStudyCount:N0}."];
        }

        string trimmedBilling = (billingConfirmation ?? string.Empty).Trim();
        if (trimmedBilling.Length < 3)
        {
            errors["billing_confirmation"] = ["A signed commercial quote token or operator billing confirmation is required."];
        }
        else if (trimmedBilling.Length > MaxBillingConfirmationLength)
        {
            errors["billing_confirmation"] = [$"Billing confirmation reference must be {MaxBillingConfirmationLength:N0} characters or fewer."];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(SubscriptionScoping? scoping) =>
        scoping is null
            ? Validate(null, null, null, null, null, null, null)
            : Validate(
                scoping.StudyPhase,
                scoping.StudyDurationMonths,
                scoping.Countries,
                scoping.SiteCount,
                scoping.ParticipantCount,
                scoping.ActiveStudyCount,
                scoping.BillingConfirmation);

    public static SubscriptionScoping Normalize(SubscriptionScoping scoping)
    {
        ArgumentNullException.ThrowIfNull(scoping);
        return new SubscriptionScoping(
            scoping.StudyPhase.Trim(),
            scoping.StudyDurationMonths,
            NormalizeCountries(scoping.Countries),
            scoping.SiteCount,
            scoping.ParticipantCount,
            scoping.ActiveStudyCount,
            scoping.BillingConfirmation.Trim());
    }

    public static string[] NormalizeCountries(IReadOnlyList<string>? countries) =>
        countries is null
            ? []
            : countries
                .SelectMany(entry => (entry ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(c => c.Length > 0)
                .Select(c => c.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

/// <summary>Named commercial platform service accounts.</summary>
public static class CommercialOperators
{
    /// <summary>Dedicated service account permitted to activate or expire subscriptions without a self-service quote token.</summary>
    public static readonly Actor BillingOperator = Actor.ServiceAccount("kernel-commercial-billing");

    /// <summary>Dedicated gateway service account permitted to query the minimal internal posture endpoint.</summary>
    public static readonly Actor GatewayPostureProbe = Actor.ServiceAccount("gateway-entitlements");
}

/// <summary>Named clinical trust service accounts for verified cross-service delegation reads and training qualification recording.</summary>
public static class TrustOperators
{
    /// <summary>Dedicated Knowledge service account permitted to read study grants, register signed 05.02.04 training artifacts, and record verified qualifications.</summary>
    public static readonly Actor KnowledgeTrainingOperator = Actor.ServiceAccount("knowledge-training");

    /// <summary>Dedicated CTMS service account permitted to read canonical site grants for RBAC-scoped Delegation of Authority logs.</summary>
    public static readonly Actor CtmsDelegationReader = Actor.ServiceAccount("ctms-delegation-reader");
}

/// <summary>Signing configuration for cryptographic commercial quote tokens.</summary>
/// <param name="SigningSecret">Shared HMAC secret used to verify issued <see cref="SubscriptionQuoteToken"/> values.</param>
public sealed record CommercialQuoteOptions(string? SigningSecret);

/// <summary>Verified claims extracted from a valid <see cref="SubscriptionQuoteToken"/>.</summary>
public sealed record VerifiedSubscriptionQuote(
    TenantId TenantId,
    CommercialTier Tier,
    string QuoteReference,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Issues and verifies HMAC-SHA256 signed commercial quote tokens (<c>qtk1.&lt;payload&gt;.&lt;sig&gt;</c>) binding a
/// specific <see cref="TenantId"/>, <see cref="CommercialTier"/>, <see cref="SubscriptionScoping"/>, quote reference,
/// and expiration timestamp so non-operator callers can never self-activate a paid tier with an arbitrary string.
/// </summary>
public static class SubscriptionQuoteToken
{
    /// <summary>Version prefix for signed commercial quote tokens.</summary>
    public const string Prefix = "qtk1";

    private const int MinSecretLength = 16;
    private const string KeyDomainPrefix = "cadence-commercial-quote-v1:";

    /// <summary>Issues a signed commercial quote token bound to <paramref name="tenantId"/>, <paramref name="tier"/>, and <paramref name="scoping"/>.</summary>
    public static string Issue(
        string signingSecret,
        TenantId tenantId,
        CommercialTier tier,
        SubscriptionScoping scoping,
        string quoteReference,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(signingSecret) || signingSecret.Trim().Length < MinSecretLength)
        {
            throw new ArgumentException($"Quote signing secret must be at least {MinSecretLength} characters.", nameof(signingSecret));
        }

        if (tenantId.IsEmpty)
        {
            throw new ArgumentException("Quote token requires a non-empty tenant id.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(scoping);
        if (string.IsNullOrWhiteSpace(quoteReference))
        {
            throw new ArgumentException("Quote reference is required.", nameof(quoteReference));
        }

        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("Quote expiration must be after its issuance timestamp.", nameof(expiresAt));
        }

        var countries = CanonicalizeCountries(scoping.Countries);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("active_study_count", scoping.ActiveStudyCount);
            writer.WritePropertyName("countries");
            writer.WriteStartArray();
            foreach (string country in countries)
            {
                writer.WriteStringValue(country);
            }

            writer.WriteEndArray();
            writer.WriteNumber("expires_at", expiresAt.ToUnixTimeSeconds());
            writer.WriteNumber("issued_at", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("participant_count", scoping.ParticipantCount);
            writer.WriteString("quote_ref", quoteReference.Trim());
            writer.WriteNumber("site_count", scoping.SiteCount);
            writer.WriteNumber("study_duration_months", scoping.StudyDurationMonths);
            writer.WriteString("study_phase", scoping.StudyPhase.Trim());
            writer.WriteString("tenant_id", tenantId.Value.ToString("D"));
            writer.WriteString("tier", tier.ToString());
            writer.WriteEndObject();
        }

        string encodedPayload = Base64Url.EncodeToString(buffer.WrittenSpan);
        string signingInput = $"{Prefix}.{encodedPayload}";
        Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputeMac(signingSecret.Trim(), signingInput, mac);
        string encodedSig = Base64Url.EncodeToString(mac);
        return $"{signingInput}.{encodedSig}";
    }

    /// <summary>
    /// Verifies that <paramref name="token"/> carries a valid HMAC-SHA256 signature under <paramref name="signingSecret"/>,
    /// has not expired at <paramref name="asOf"/>, and matches <paramref name="expectedTenantId"/>,
    /// <paramref name="expectedTier"/>, and all quantitative/phase/country parameters of <paramref name="expectedScoping"/>.
    /// </summary>
    public static bool TryVerify(
        string? token,
        string? signingSecret,
        TenantId expectedTenantId,
        CommercialTier expectedTier,
        SubscriptionScoping? expectedScoping,
        DateTimeOffset asOf,
        out VerifiedSubscriptionQuote? verified)
    {
        verified = null;
        if (string.IsNullOrWhiteSpace(token)
            || string.IsNullOrWhiteSpace(signingSecret)
            || signingSecret.Trim().Length < MinSecretLength
            || expectedTenantId.IsEmpty
            || expectedScoping is null)
        {
            return false;
        }

        string trimmedToken = token.Trim();
        string[] parts = trimmedToken.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] providedSig;
        byte[] payloadBytes;
        try
        {
            providedSig = Base64Url.DecodeFromChars(parts[2]);
            payloadBytes = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (providedSig.Length != HMACSHA256.HashSizeInBytes)
        {
            return false;
        }

        string signingInput = $"{parts[0]}.{parts[1]}";
        Span<byte> expectedSig = stackalloc byte[HMACSHA256.HashSizeInBytes];
        ComputeMac(signingSecret.Trim(), signingInput, expectedSig);
        if (!CryptographicOperations.FixedTimeEquals(providedSig, expectedSig))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("tenant_id", out var tenantProp)
                || !Guid.TryParse(tenantProp.GetString(), out var parsedTenant)
                || TenantId.FromGuid(parsedTenant) != expectedTenantId)
            {
                return false;
            }

            if (!root.TryGetProperty("tier", out var tierProp)
                || !Enum.TryParse<CommercialTier>(tierProp.GetString(), ignoreCase: false, out var parsedTier)
                || parsedTier != expectedTier)
            {
                return false;
            }

            if (!root.TryGetProperty("study_phase", out var phaseProp)
                || !string.Equals(phaseProp.GetString(), expectedScoping.StudyPhase.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("study_duration_months", out var durationProp)
                || !durationProp.TryGetInt32(out int duration)
                || duration != expectedScoping.StudyDurationMonths)
            {
                return false;
            }

            if (!root.TryGetProperty("site_count", out var siteProp)
                || !siteProp.TryGetInt32(out int sites)
                || sites != expectedScoping.SiteCount)
            {
                return false;
            }

            if (!root.TryGetProperty("participant_count", out var participantProp)
                || !participantProp.TryGetInt32(out int participants)
                || participants != expectedScoping.ParticipantCount)
            {
                return false;
            }

            if (!root.TryGetProperty("active_study_count", out var studyCountProp)
                || !studyCountProp.TryGetInt32(out int studies)
                || studies != expectedScoping.ActiveStudyCount)
            {
                return false;
            }

            if (!root.TryGetProperty("countries", out var countriesProp) || countriesProp.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var quoteCountries = countriesProp.EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .ToArray();
            var expectedCountries = CanonicalizeCountries(expectedScoping.Countries);
            if (!CanonicalizeCountries(quoteCountries).SequenceEqual(expectedCountries, StringComparer.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("issued_at", out var issuedProp)
                || !issuedProp.TryGetInt64(out long issuedUnix)
                || !root.TryGetProperty("expires_at", out var expiresProp)
                || !expiresProp.TryGetInt64(out long expiresUnix))
            {
                return false;
            }

            long nowUnix = asOf.ToUnixTimeSeconds();
            if (expiresUnix <= issuedUnix || expiresUnix <= nowUnix || issuedUnix > nowUnix + 60)
            {
                return false;
            }

            if (!root.TryGetProperty("quote_ref", out var refProp)
                || refProp.GetString() is not { Length: > 0 } quoteRef)
            {
                return false;
            }

            verified = new VerifiedSubscriptionQuote(
                expectedTenantId,
                expectedTier,
                quoteRef,
                DateTimeOffset.FromUnixTimeSeconds(issuedUnix),
                DateTimeOffset.FromUnixTimeSeconds(expiresUnix));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] CanonicalizeCountries(IReadOnlyList<string>? countries) =>
        SubscriptionScopingValidator.NormalizeCountries(countries)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

    private static void ComputeMac(string secret, string signingInput, Span<byte> destination)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(KeyDomainPrefix + secret);
        byte[] messageBytes = Encoding.UTF8.GetBytes(signingInput);
        HMACSHA256.HashData(keyBytes, messageBytes, destination);
    }
}

/// <summary>
/// Read-only commercial entitlement snapshot for a resolved kernel tenant.
/// </summary>
public sealed record TenantEntitlementSnapshot(
    TenantId TenantId,
    SubscriptionId? SubscriptionId,
    CommercialTier Tier,
    SubscriptionStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? TrialEndsAt,
    bool IsReadOnly,
    IReadOnlyList<ProductFamily> ProductFamilies,
    SubscriptionScoping? Scoping = null);

/// <summary>
/// Reads commercial subscription and product-family entitlements for a tenant.
/// Never consulted by <c>AuthorityEvaluator</c> or <c>GrantNodeVisibility</c> to permit clinical data access.
/// </summary>
public interface ITenantEntitlementReader
{
    Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
        TenantId tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken,
        DateTimeOffset? knownAt = null);
}

/// <summary>
/// Canonical product-family bundles per commercial tier (<c>docs/strategy/PRODUCT_CATALOG.md</c>).
/// </summary>
public static class CommercialCatalog
{
    private static readonly ProductFamily[] IndividualFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
    ];

    private static readonly ProductFamily[] SelectDefaultFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
    ];

    private static readonly ProductFamily[] CompleteFamilies =
    [
        ProductFamily.Blueprint,
        ProductFamily.Capture,
        ProductFamily.Orbit,
        ProductFamily.Engage,
        ProductFamily.Allocate,
        ProductFamily.Vigil,
        ProductFamily.Lens,
        ProductFamily.Assure,
    ];

    public static IReadOnlyList<ProductFamily> FamiliesForTier(CommercialTier tier) =>
        tier switch
        {
            CommercialTier.Individual => IndividualFamilies,
            CommercialTier.Select => SelectDefaultFamilies,
            CommercialTier.Complete => CompleteFamilies,
            _ => IndividualFamilies,
        };
}
