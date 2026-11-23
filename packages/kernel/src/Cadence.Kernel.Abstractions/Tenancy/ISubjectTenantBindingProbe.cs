using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Tenancy;

/// <summary>
/// Checks whether an OIDC subject already holds a person grant or tenant membership in any foreign kernel tenant,
/// preventing bound users from escalating into <c>Sponsor Admin</c> through self-service trial onboarding.
/// </summary>
public interface ISubjectTenantBindingProbe
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="subjectId"/> (or <paramref name="derivedPersonId"/>) holds
    /// any grant or tenant membership in a kernel tenant other than <paramref name="excludedTenantId"/>.
    /// </summary>
    Task<bool> HasForeignBindingAsync(
        string subjectId,
        PersonId derivedPersonId,
        TenantId excludedTenantId,
        CancellationToken cancellationToken);
}
