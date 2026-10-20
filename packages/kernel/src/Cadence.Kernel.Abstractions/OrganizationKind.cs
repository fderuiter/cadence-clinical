namespace Cadence.Kernel;

/// <summary>
/// The role an organization plays toward a tenant's studies (spec §7.1). Stored by name.
/// </summary>
public enum OrganizationKind
{
    Sponsor,
    Cro,
    SiteInstitution,
    Vendor,
    Regulator,
    EthicsCommittee,
    Lab,
}
