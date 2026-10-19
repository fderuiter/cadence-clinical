using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Application.Execution;

/// <summary>
/// Handles <see cref="RegisterStudy"/>. Under the tenant's lock it checks that the code is unused in the tenant, so two
/// registrations with one code run one after another and the second is refused.
/// </summary>
internal sealed class RegisterStudyHandler : ICommandHandler<RegisterStudy, StudyId>
{
    public async Task<CommandResult<StudyId>> HandleAsync(RegisterStudy command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return StudyErrors.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return StudyErrors.TitleRequired;
        }

        if (await context.Work.ExistsAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!await context.Work.ExistsAsync<Organization>(command.SponsorId, cancellationToken).ConfigureAwait(false))
        {
            return StudyErrors.SponsorUnknown;
        }

        await context.Work.LockAsync<Tenant>(command.TenantId, cancellationToken).ConfigureAwait(false);
        string code = command.Code;
        if ((await context.Work.ReadAllAsync<Study>(s => s.StudyCode == code, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return StudyErrors.StudyCodeTaken;
        }

        context.Work.Add(new Study(
            command.StudyId, command.TenantId, command.SponsorId, code, command.Title, command.Phase, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new StudyRegistered(command.TenantId, command.StudyId, command.SponsorId, code, command.Title, command.Phase));
        return CommandResult<StudyId>.Success(command.StudyId);
    }
}

/// <summary>
/// Handles <see cref="RegisterSite"/>. Under the institution's lock it checks that the code is unused by the
/// institution's other sites.
/// </summary>
internal sealed partial class RegisterSiteHandler : ICommandHandler<RegisterSite, SiteId>
{
    public async Task<CommandResult<SiteId>> HandleAsync(RegisterSite command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return StudyErrors.CodeRequired;
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return StudyErrors.NameRequired;
        }

        if (command.Country is null || !CountryPattern().IsMatch(command.Country))
        {
            return StudyErrors.CountryInvalid;
        }

        if (await context.Work.ExistsAsync<Site>(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!await context.Work.ExistsAsync<Organization>(command.InstitutionId, cancellationToken).ConfigureAwait(false))
        {
            return StudyErrors.InstitutionUnknown;
        }

        var institution = command.InstitutionId;
        await context.Work.LockAsync<Organization>(institution, cancellationToken).ConfigureAwait(false);
        string code = command.Code;
        if ((await context.Work.ReadAllAsync<Site>(s => s.InstitutionId == institution && s.SiteCode == code, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return StudyErrors.SiteCodeTaken;
        }

        context.Work.Add(new Site(
            command.SiteId, command.TenantId, institution, code, command.Name, command.Country, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new SiteRegistered(command.TenantId, command.SiteId, institution, code, command.Name, command.Country));
        return CommandResult<SiteId>.Success(command.SiteId);
    }

    [GeneratedRegex("^[A-Z]{2}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();
}

/// <summary>
/// Handles <see cref="LinkSiteToStudy"/>. Under the study's lock it checks that the site is not already linked, so the
/// study has at most one study site per site.
/// </summary>
internal sealed class LinkSiteToStudyHandler : ICommandHandler<LinkSiteToStudy, StudySiteId>
{
    /// <summary>The status a new study site starts in, before activation (K-TIME-4).</summary>
    public const string InitialStatus = "Selected";

    public async Task<CommandResult<StudySiteId>> HandleAsync(LinkSiteToStudy command, ICommandContext context, CancellationToken cancellationToken)
    {
        if (await context.Work.ExistsAsync<StudySite>(command.StudySiteId, cancellationToken).ConfigureAwait(false))
        {
            return KernelErrors.AlreadyExists;
        }

        if (!await context.Work.ExistsAsync<Study>(command.StudyId, cancellationToken).ConfigureAwait(false))
        {
            return StudyErrors.StudyUnknown;
        }

        if (!await context.Work.ExistsAsync<Site>(command.SiteId, cancellationToken).ConfigureAwait(false))
        {
            return StudyErrors.SiteUnknown;
        }

        var study = command.StudyId;
        var site = command.SiteId;
        await context.Work.LockAsync<Study>(study, cancellationToken).ConfigureAwait(false);
        if ((await context.Work.ReadAllAsync<StudySite>(s => s.StudyId == study && s.SiteId == site, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return StudyErrors.AlreadyLinked;
        }

        context.Work.Add(new StudySite(command.StudySiteId, command.TenantId, study, site, InitialStatus, context.Actor.Id, command.ReasonForChange));
        context.Work.Raise(new SiteLinkedToStudy(command.TenantId, command.StudySiteId, study, site));
        return CommandResult<StudySiteId>.Success(command.StudySiteId);
    }
}

internal static class StudyCommandRegistration
{
    public static IServiceCollection AddKernelStudyHandlers(this IServiceCollection services)
    {
        services.AddTransient<ICommandHandler<RegisterStudy, StudyId>, RegisterStudyHandler>();
        services.AddTransient<ICommandHandler<RegisterSite, SiteId>, RegisterSiteHandler>();
        services.AddTransient<ICommandHandler<LinkSiteToStudy, StudySiteId>, LinkSiteToStudyHandler>();
        return services;
    }
}
