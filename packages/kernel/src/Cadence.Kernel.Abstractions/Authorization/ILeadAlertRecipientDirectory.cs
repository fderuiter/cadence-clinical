using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Authorization;

/// <summary>
/// Resolves pseudonymous person actor identifiers (<c>Actor.Id</c>, never <c>IdentityVault</c> PII) who hold active lead
/// authority for a study or study site so de-identified operational digests can notify accountable leads without crossing
/// the K-IDN-1 identity boundary or K-XS-2 persistence boundary.
/// </summary>
public interface ILeadAlertRecipientDirectory
{
    /// <summary>
    /// Lists distinct active person actor IDs holding study-wide query lead authority (<c>execution.query.manage</c> or a
    /// study-lead role covering <paramref name="studyId"/>) in <paramref name="tenant"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListStudyLeadsAsync(
        TenantId tenant,
        StudyId studyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists distinct active person actor IDs holding site-level investigator or site-lead authority
    /// (<c>collection.sign</c> or an investigator/site-lead role covering <paramref name="studySiteId"/>) in
    /// <paramref name="tenant"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ListSiteLeadsAsync(
        TenantId tenant,
        StudyId studyId,
        StudySiteId studySiteId,
        CancellationToken cancellationToken = default);
}
