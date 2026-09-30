using System;
using System.Text.RegularExpressions;

namespace Cadence.Kernel.Authorization;

/// <summary>
/// The level of the scope path a grant applies at (spec §9.3): <c>tenant / organization / study / country /
/// study_site</c>. A grant at one level covers everything below it.
/// </summary>
public enum ScopeLevel
{
    /// <summary>Everything in the tenant.</summary>
    Tenant,

    /// <summary>An organization and the studies it sponsors.</summary>
    Organization,

    /// <summary>A study and all of its study sites.</summary>
    Study,

    /// <summary>A study's study sites in one country.</summary>
    StudyCountry,

    /// <summary>One study site.</summary>
    StudySite,
}

/// <summary>
/// Where a grant applies (spec §9.3). <see cref="TargetId"/> names the organization, study or study site; a
/// <see cref="ScopeLevel.StudyCountry"/> scope names the study and an ISO 3166-1 alpha-2 <see cref="Country"/>.
/// </summary>
public sealed partial record AuthorityScope(ScopeLevel Level, Guid? TargetId = null, string? Country = null)
{
    public static AuthorityScope Tenant() => new(ScopeLevel.Tenant);

    public static AuthorityScope Organization(OrganizationId organization) => new(ScopeLevel.Organization, organization.Value);

    public static AuthorityScope Study(StudyId study) => new(ScopeLevel.Study, study.Value);

    public static AuthorityScope StudyCountry(StudyId study, string country) => new(ScopeLevel.StudyCountry, study.Value, country);

    public static AuthorityScope StudySite(StudySiteId studySite) => new(ScopeLevel.StudySite, studySite.Value);

    /// <summary>Why this scope is malformed, or null when it is well formed.</summary>
    public string? Problem() => Level switch
    {
        ScopeLevel.Tenant when TargetId is null && Country is null => null,
        ScopeLevel.Organization or ScopeLevel.Study or ScopeLevel.StudySite
            when TargetId is { } id && id != Guid.Empty && Country is null => null,
        ScopeLevel.StudyCountry when TargetId is { } id && id != Guid.Empty && Country is not null && CountryCode().IsMatch(Country) => null,
        ScopeLevel.Tenant => "A tenant scope names no object.",
        ScopeLevel.StudyCountry => "A study-country scope names a study and an upper-case ISO 3166-1 alpha-2 country.",
        _ when !Enum.IsDefined(Level) => "The scope level is not known.",
        _ => $"A {Level} scope names its object and no country.",
    };

    /// <summary>True when a grant at this scope covers an object whose scope path is <paramref name="path"/>.</summary>
    public bool Covers(ScopePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Level switch
        {
            ScopeLevel.Tenant => true,
            ScopeLevel.Organization => path.Organization?.Value == TargetId,
            ScopeLevel.Study => path.Study?.Value == TargetId,
            ScopeLevel.StudyCountry => path.Study?.Value == TargetId && string.Equals(path.Country, Country, StringComparison.Ordinal),
            ScopeLevel.StudySite => path.StudySite?.Value == TargetId,
            _ => false,
        };
    }

    public override string ToString() => Level switch
    {
        ScopeLevel.Tenant => "tenant",
        ScopeLevel.StudyCountry => $"study {TargetId} in {Country}",
        _ => $"{Level.ToString().ToLowerInvariant()} {TargetId}",
    };

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryCode();
}

/// <summary>
/// Where an object sits on the scope path (spec §9.3), within its tenant: the sponsoring organization, the study, the
/// study site's country and the study site, as far as they apply. An object that belongs to no study (a concept, a
/// standard, the tenant itself) has an empty path, which only tenant-wide grants cover.
/// </summary>
public sealed record ScopePath(
    OrganizationId? Organization = null,
    StudyId? Study = null,
    string? Country = null,
    StudySiteId? StudySite = null)
{
    /// <summary>The path of an object that belongs to no organization or study.</summary>
    public static ScopePath TenantWide { get; } = new();
}
