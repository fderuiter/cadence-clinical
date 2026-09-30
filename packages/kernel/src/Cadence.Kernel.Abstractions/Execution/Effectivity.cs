using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Evidence;

namespace Cadence.Kernel.Execution;

/// <summary>
/// Where a protocol version stands in one scope (spec §7.1). Approval of a protocol version is not effectiveness: a
/// version is usable only where it is <see cref="Effective"/>.
/// </summary>
public enum EffectivityStatus
{
    /// <summary>Declared for the scope; approvals are outstanding.</summary>
    Pending,

    /// <summary>The competent authority has approved or acknowledged it for the scope.</summary>
    RegulatoryApproved,

    /// <summary>The ethics committee or IRB has approved it for the scope.</summary>
    EthicsApproved,

    /// <summary>In force in the scope, on approval evidence (K-TIME-5).</summary>
    Effective,

    /// <summary>No longer in force in the scope; terminal.</summary>
    Withdrawn,
}

/// <summary>How wide an effectivity is. Resolution takes the most specific one: study site, then country, then study.</summary>
public enum EffectivityLevel
{
    /// <summary>Every study site of the study.</summary>
    Study,

    /// <summary>The study's sites in one country.</summary>
    Country,

    /// <summary>One study site.</summary>
    StudySite,
}

/// <summary>
/// The scope of an effectivity (spec §7.1). A <see cref="EffectivityLevel.Country"/> scope names an ISO 3166-1 alpha-2
/// <see cref="Country"/>; a <see cref="EffectivityLevel.StudySite"/> scope names the <see cref="StudySiteId"/>.
/// </summary>
public sealed partial record EffectivityScope(EffectivityLevel Level, string? Country = null, StudySiteId? StudySiteId = null)
{
    /// <summary>Every study site of the protocol version's study.</summary>
    public static EffectivityScope Study() => new(EffectivityLevel.Study);

    /// <summary>The study's sites in <paramref name="country"/>.</summary>
    public static EffectivityScope InCountry(string country) => new(EffectivityLevel.Country, country);

    /// <summary>One study site.</summary>
    public static EffectivityScope At(StudySiteId studySite) => new(EffectivityLevel.StudySite, StudySiteId: studySite);

    /// <summary>Why the scope is malformed, or null when it is well formed.</summary>
    public string? Problem() => Level switch
    {
        EffectivityLevel.Study when Country is null && StudySiteId is null => null,
        EffectivityLevel.Country when StudySiteId is null && Country is not null && CountryCode().IsMatch(Country) => null,
        EffectivityLevel.StudySite when Country is null && StudySiteId is { IsEmpty: false } => null,
        EffectivityLevel.Study => "A study-wide scope names no country or study site.",
        EffectivityLevel.Country => "A country scope names an ISO 3166-1 alpha-2 country and no study site.",
        EffectivityLevel.StudySite => "A study-site scope names one study site and no country.",
        _ => "Unknown scope level.",
    };

    /// <summary>True when the scope applies to a study site in <paramref name="country"/>.</summary>
    public bool Covers(StudySiteId studySite, string country) => Level switch
    {
        EffectivityLevel.Study => true,
        EffectivityLevel.Country => string.Equals(Country, country, StringComparison.Ordinal),
        EffectivityLevel.StudySite => StudySiteId == studySite,
        _ => false,
    };

    public override string ToString() => Level switch
    {
        EffectivityLevel.Country => $"country {Country}",
        EffectivityLevel.StudySite => $"study site {StudySiteId}",
        _ => "study",
    };

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryCode();
}

/// <summary>The effectivity lifecycle (spec §7.1). Approvals may arrive in either order; Withdrawn is terminal.</summary>
public static class EffectivityTransitions
{
    /// <summary>True when an effectivity in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool IsAllowed(EffectivityStatus from, EffectivityStatus to) => (from, to) switch
    {
        (EffectivityStatus.Withdrawn, _) => false,
        (_, EffectivityStatus.Withdrawn) => true,
        (EffectivityStatus.Effective, _) => false,
        (_, EffectivityStatus.Pending) => false,
        _ => from != to,
    };

    /// <summary>
    /// The artifact kinds that can evidence a move to <paramref name="to"/>. <see cref="EffectivityStatus.Effective"/>
    /// needs at least one (K-TIME-5); the approval statuses accept their own kind; the others take none.
    /// </summary>
    public static IReadOnlySet<ArtifactKind> EvidenceKinds(EffectivityStatus to) => to switch
    {
        EffectivityStatus.Effective => EffectiveEvidence,
        EffectivityStatus.EthicsApproved => EthicsEvidence,
        EffectivityStatus.RegulatoryApproved => RegulatoryEvidence,
        _ => NoEvidence,
    };

    private static readonly HashSet<ArtifactKind> EffectiveEvidence =
        [ArtifactKind.EthicsApproval, ArtifactKind.RegulatoryApproval, ArtifactKind.CtaAcknowledgement];

    private static readonly HashSet<ArtifactKind> EthicsEvidence = [ArtifactKind.EthicsApproval];

    private static readonly HashSet<ArtifactKind> RegulatoryEvidence = [ArtifactKind.RegulatoryApproval, ArtifactKind.CtaAcknowledgement];

    private static readonly HashSet<ArtifactKind> NoEvidence = [];
}

/// <summary>A protocol version in force at a study site, and the effectivity that makes it so.</summary>
/// <param name="ProtocolVersionId">The version.</param>
/// <param name="Number">The version's number in its study.</param>
/// <param name="EffectivityId">The most specific effectivity of the version that applies to the study site.</param>
/// <param name="Level">That effectivity's level.</param>
public sealed record EffectiveVersion(ProtocolVersionId ProtocolVersionId, int Number, EffectivityId EffectivityId, EffectivityLevel Level);

/// <summary>
/// Reads which protocol versions are in force at a study site (spec §7.1), under the tenant's Row-Level Security. For
/// each version of the site's study the most specific effectivity applying to the site decides: study site, then
/// country, then study. A version whose deciding effectivity is not <see cref="EffectivityStatus.Effective"/> at the
/// instant is not in force, even when a wider one is.
/// </summary>
public interface IEffectivityReader
{
    /// <summary>
    /// The versions in force at <paramref name="studySite"/> at <paramref name="validAt"/> as known at
    /// <paramref name="knownAt"/>, highest number first. Empty for an unknown study site.
    /// </summary>
    Task<IReadOnlyList<EffectiveVersion>> EffectiveAtAsync(
        TenantId tenant, StudySiteId studySite, DateTimeOffset validAt, DateTimeOffset knownAt, CancellationToken cancellationToken);
}
