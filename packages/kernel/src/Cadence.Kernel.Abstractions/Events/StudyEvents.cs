using Cadence.Kernel.Commands;

namespace Cadence.Kernel.Events;

/// <summary>A study was registered, owned by its sponsor organization (spec §3.1, §5.2, Knowledge family).</summary>
[KernelEvent("StudyRegistered", 1)]
public sealed record StudyRegistered(TenantId TenantId, StudyId StudyId, OrganizationId SponsorId, string Code, string Title, string Phase);

/// <summary>A site was registered: a durable location belonging to an institution (spec §3.1, §5.2, Execution family).</summary>
[KernelEvent("SiteRegistered", 1)]
public sealed record SiteRegistered(TenantId TenantId, SiteId SiteId, OrganizationId InstitutionId, string Code, string Name, string Country);

/// <summary>A site was linked to a study as a study site, in status <c>Selected</c> (spec §3.1, §5.2, Execution family).</summary>
[KernelEvent("SiteLinkedToStudy", 1)]
public sealed record SiteLinkedToStudy(TenantId TenantId, StudySiteId StudySiteId, StudyId StudyId, SiteId SiteId);
