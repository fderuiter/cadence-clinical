using Cadence.Kernel.Authorization;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Registers a study, owned by exactly one sponsor organization in the tenant (spec §3.1). The code is unique in the
/// tenant; protocol versions, study sites, enrollments and grant scopes then hang off the study.
/// </summary>
[KernelCapability("study.register", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(StudyErrors),
    nameof(StudyErrors.CodeRequired),
    nameof(StudyErrors.TitleRequired),
    nameof(StudyErrors.SponsorUnknown),
    nameof(StudyErrors.StudyCodeTaken))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RegisterStudy(
    TenantId TenantId,
    StudyId StudyId,
    OrganizationId SponsorId,
    string Code,
    string Title,
    string Phase,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StudyId>;

/// <summary>
/// Registers a site: a durable location belonging to an institution (spec §3.1), independent of any study. The code is
/// unique for the institution, and the country is an ISO 3166-1 alpha-2 code.
/// </summary>
[KernelCapability("site.register", VerbClass.Execute)]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(StudyErrors),
    nameof(StudyErrors.CodeRequired),
    nameof(StudyErrors.NameRequired),
    nameof(StudyErrors.CountryInvalid),
    nameof(StudyErrors.InstitutionUnknown),
    nameof(StudyErrors.SiteCodeTaken))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record RegisterSite(
    TenantId TenantId,
    SiteId SiteId,
    OrganizationId InstitutionId,
    string Code,
    string Name,
    string Country,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<SiteId>;

/// <summary>
/// Links a site to a study as a study site, in status <c>Selected</c> (spec §3.1, raising <c>SiteLinkedToStudy</c>).
/// Activation follows with <see cref="ActivateStudySite"/> once a protocol version is effective there (K-TIME-4).
/// </summary>
[KernelCapability("study_site.link", VerbClass.Execute, Resource = nameof(StudyId))]
[RaisesKernelErrors(typeof(KernelErrors), nameof(KernelErrors.AlreadyExists))]
[RaisesKernelErrors(
    typeof(StudyErrors),
    nameof(StudyErrors.StudyUnknown),
    nameof(StudyErrors.SiteUnknown),
    nameof(StudyErrors.AlreadyLinked))]
[NoValidator(NoValidatorAttribute.HandlerChecksInputs)]
public sealed record LinkSiteToStudy(
    TenantId TenantId,
    StudySiteId StudySiteId,
    StudyId StudyId,
    SiteId SiteId,
    string IdempotencyKey,
    string ReasonForChange) : ICommand<StudySiteId>;

/// <summary>Errors raised by the study and site commands.</summary>
public static class StudyErrors
{
    public static readonly KernelError CodeRequired =
        new("K.Study.CodeRequired", 422, "A code is required.");

    public static readonly KernelError TitleRequired =
        new("K.Study.TitleRequired", 422, "A study title is required.");

    public static readonly KernelError NameRequired =
        new("K.Study.NameRequired", 422, "A site name is required.");

    public static readonly KernelError CountryInvalid =
        new("K.Study.CountryInvalid", 422, "A country is an ISO 3166-1 alpha-2 code: two upper-case letters.");

    public static readonly KernelError SponsorUnknown =
        new("K.Study.SponsorUnknown", 404, "The sponsor organization does not exist in this tenant.");

    public static readonly KernelError InstitutionUnknown =
        new("K.Study.InstitutionUnknown", 404, "The institution does not exist in this tenant.");

    public static readonly KernelError StudyUnknown =
        new("K.Study.StudyUnknown", 404, "The study does not exist.");

    public static readonly KernelError SiteUnknown =
        new("K.Study.SiteUnknown", 404, "The site does not exist.");

    public static readonly KernelError StudyCodeTaken =
        new("K.Study.StudyCodeTaken", 409, "Another study in this tenant already has the code.");

    public static readonly KernelError SiteCodeTaken =
        new("K.Study.SiteCodeTaken", 409, "Another site of this institution already has the code.");

    public static readonly KernelError AlreadyLinked =
        new("K.Study.AlreadyLinked", 409, "The site is already linked to the study.");
}
