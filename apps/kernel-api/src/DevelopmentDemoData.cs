using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Execution;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Host;

/// <summary>
/// The local stack's demo study in the development tenant (ADR-2543, #5452): a sponsor, the study
/// <see cref="StudyCode"/> at two sites with screened participants, matching what the Execution service seeds, and grants
/// that let the local realm's users read it, the coordinator only at their own site. Everything is written through
/// the command pipeline as the development bootstrap's service account, which first grants itself a role for the
/// writes. Ids are derived from the tenant and each record's name, and every value is fixed, so each command has the
/// same payload on every start: a later start replays the stored results instead of writing again, and a start
/// interrupted halfway finishes the rest.
/// </summary>
internal static class DevelopmentDemoData
{
    /// <summary>The demo study's code: the study the Execution service seeds (<c>ExecutionDbSeeder</c>).</summary>
    public const string StudyCode = "CADENCE-101";

    /// <summary>When the demo world's records take effect in valid time; fixed so every start sends the same payload.</summary>
    internal static readonly DateTimeOffset Epoch = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    private const string Reason = "Seed the local development stack's demo study (ADR-2543)";

    /// <summary>The demo sites: code, name and country; the coordinator and the monitor work at the first.</summary>
    internal static readonly (string Code, string Name, string Country)[] Sites =
    [
        ("SITE-101", "Riverside University Hospital", "US"),
        ("SITE-102", "Lakeshore Medical Center", "US"),
    ];

    /// <summary>The demo participants by subject code and site; the first matches the Execution service's seed.</summary>
    internal static readonly (string SubjectCode, string Site)[] Participants =
    [
        ("SUBJ-101-001", "SITE-101"),
        ("SUBJ-101-002", "SITE-101"),
        ("SUBJ-102-001", "SITE-102"),
    ];

    /// <summary>The roles the realm's users are granted: code, name and capabilities.</summary>
    internal static readonly (string Code, string Name, string[] Capabilities)[] Roles =
    [
        ("DEMO_CRC", "Clinical research coordinator", ["participant.register", "participant.screen", "enrollment.change_status", "observation.record", "observation.correct", "collection.open", "collection.record", "execution.query.answer"]),
        ("DEMO_INVESTIGATOR", "Site investigator", ["collection.sign", "execution.query.answer"]),
        ("DEMO_MONITOR", "Clinical research associate", ["validation_finding.investigate", "validation_finding.classify", "validation_finding.explain", "execution.query.raise", "execution.query.manage"]),
        ("DEMO_DATA_MANAGER", "Data manager", ["validation_run.record", "validation_finding.accept", "validation_finding.reopen", "observation.lock", "execution.query.raise", "execution.query.manage"]),
        ("DEMO_DESIGNER", "Study designer", ["protocol.draft", "protocol.revise_draft", "protocol.submit_for_review", "protocol.amend"]),
        ("DEMO_SAFETY", "Safety reviewer", ["validation_finding.investigate", "validation_finding.classify"]),
        ("DEMO_AUDITOR", "Auditor", ["validation_finding.investigate"]),
    ];

    /// <summary>
    /// The local realm's users (<c>docker/cadence-realm.json</c>) by their fixed Keycloak subject id, with the role and
    /// the site they are granted at; a null site grants the role for the whole study.
    /// </summary>
    internal static readonly (string Username, string Subject, string Role, string? Site)[] Users =
    [
        ("crc.site101", "1a4345cc-364c-4b8b-881e-93bbea528b0a", "DEMO_CRC", "SITE-101"),
        ("pi.site101", "cf64a3b4-33cc-4925-8452-75e29ca9526e", "DEMO_INVESTIGATOR", "SITE-101"),
        ("cra.monitor", "ee2cfdb6-d893-4fb4-a10f-d50830b6728a", "DEMO_MONITOR", "SITE-101"),
        ("dm.lead", "47a9d88f-73d7-4aca-9fd6-14b8e6e9284f", "DEMO_DATA_MANAGER", null),
        ("designer_demo", "d0b6257b-57fe-420b-9526-366e83c756aa", "DEMO_DESIGNER", null),
        ("designer.lead", "8808e058-a472-4f72-9970-a027eddf35bc", "DEMO_DESIGNER", null),
        ("safety.officer", "3d290711-4167-4da5-b55a-4c75760bd27c", "DEMO_SAFETY", null), // deid-ignore: a demo realm user's fixed subject id, not a telephone number
        ("auditor.lead", "d43095eb-1912-40dd-a528-fa04335a2342", "DEMO_AUDITOR", null),
    ];

    /// <summary>
    /// The Execution module's kernel event consumers (ADR-2622, #5461), each a service account with the role it needs
    /// across the tenant: opening its side of every form the kernel opens, and stating each query step as a kernel
    /// assertion. Production grants for module service accounts are #5566.
    /// </summary>
    private static readonly (string Consumer, string Role, string Name, string[] Capabilities)[] ModuleServiceAccounts =
    [
        ("execution-forms", "DEMO_EXECUTION_FORMS", "Execution forms consumer", ["execution.form.open"]),
        ("execution-queries", "DEMO_EXECUTION_QUERIES", "Execution queries consumer", ["observation.query", "observation.query.answer", "assertion.retract"]),
    ];

    /// <summary>The capabilities the bootstrap grants itself, tenant-wide, to write the demo study.</summary>
    private static readonly string[] SeederCapabilities =
        ["organization.register", "study.register", "site.register", "study_site.link", "participant.register", "participant.screen"];

    /// <summary>Writes the demo study into <paramref name="tenant"/>, or replays what an earlier start wrote.</summary>
    public static async Task SeedAsync(TenantId tenant, IServiceScopeFactory scopes, CancellationToken cancellationToken)
    {
        var ids = new DemoIds(tenant);
        var seeder = new Sender(tenant, scopes);

        var seederRole = await seeder.SendAsync(
            new DefineRole(tenant, ids.Role("DEMO_SEEDER"), "DEMO_SEEDER", "Development demo seeder", SeederCapabilities, Key("role:DEMO_SEEDER"), Reason),
            cancellationToken).ConfigureAwait(false);
        await seeder.SendAsync(
            new IssueGrant(tenant, ids.Grant("seeder"), DevelopmentTenantBootstrap.Operator, null, seederRole.RoleId, AuthorityScope.Tenant(), Epoch, null, false, Key("grant:seeder"), Reason),
            cancellationToken).ConfigureAwait(false);

        var sponsor = await seeder.SendAsync(
            new RegisterOrganization(tenant, ids.Organization("sponsor"), "CADENCE-PHARMA", "Cadence Pharma (demo sponsor)", OrganizationKind.Sponsor, Key("organization:sponsor"), Reason),
            cancellationToken).ConfigureAwait(false);
        var study = await seeder.SendAsync(
            new RegisterStudy(tenant, ids.Study(StudyCode), sponsor, StudyCode, "Cadence demo study: cardiovascular outcomes", "Phase 3", Key($"study:{StudyCode}"), Reason),
            cancellationToken).ConfigureAwait(false);

        var studySites = new Dictionary<string, StudySiteId>(StringComparer.Ordinal);
        foreach (var (code, name, country) in Sites)
        {
            var institution = await seeder.SendAsync(
                new RegisterOrganization(tenant, ids.Organization(code), $"INST-{code}", name, OrganizationKind.SiteInstitution, Key($"organization:{code}"), Reason),
                cancellationToken).ConfigureAwait(false);
            var site = await seeder.SendAsync(
                new RegisterSite(tenant, ids.Site(code), institution, code, name, country, Key($"site:{code}"), Reason), cancellationToken).ConfigureAwait(false);
            studySites[code] = await seeder.SendAsync(
                new LinkSiteToStudy(tenant, ids.StudySite(code), study, site, Key($"study-site:{code}"), Reason), cancellationToken).ConfigureAwait(false);
        }

        for (int i = 0; i < Participants.Length; i++)
        {
            var (subjectCode, site) = Participants[i];
            var participant = await seeder.SendAsync(
                new RegisterParticipant(tenant, ids.Participant(subjectCode), Key($"participant:{subjectCode}"), Reason), cancellationToken).ConfigureAwait(false);
            SubjectCode.TryFrom(subjectCode, out var code);
            await seeder.SendAsync(
                new ScreenParticipant(tenant, ids.Enrollment(subjectCode), participant, studySites[site], code, Epoch.AddDays(7 + i), Key($"enrollment:{subjectCode}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        var roles = new Dictionary<string, RoleId>(StringComparer.Ordinal);
        foreach (var (code, name, capabilities) in Roles)
        {
            roles[code] = (await seeder.SendAsync(
                new DefineRole(tenant, ids.Role(code), code, name, capabilities, Key($"role:{code}"), Reason), cancellationToken).ConfigureAwait(false)).RoleId;
        }

        foreach (var (consumer, code, name, capabilities) in ModuleServiceAccounts)
        {
            var role = await seeder.SendAsync(
                new DefineRole(tenant, ids.Role(code), code, name, capabilities, Key($"role:{code}"), Reason), cancellationToken).ConfigureAwait(false);
            await seeder.SendAsync(
                new IssueGrant(tenant, ids.Grant($"svc:{consumer}"), Actor.ServiceAccount(consumer), null, role.RoleId, AuthorityScope.Tenant(), Epoch, null, false, Key($"grant:svc:{consumer}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var (username, subject, role, site) in Users)
        {
            var scope = site is null ? AuthorityScope.Study(study) : AuthorityScope.StudySite(studySites[site]);
            await seeder.SendAsync(
                new IssueGrant(tenant, ids.Grant(username), Actor.Person(subject), null, roles[role], scope, Epoch, null, false, Key($"grant:{username}"), Reason),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Key(string record) => $"development-demo:{record}";

    /// <summary>Sends each command in its own scope as the bootstrap's service account; a refusal stops the host.</summary>
    private sealed class Sender(TenantId tenant, IServiceScopeFactory scopes)
    {
        public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ServiceActorScope>().Actor = DevelopmentTenantBootstrap.Operator;
            var result = await services.GetRequiredService<IKernelCommandSender>().SendAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? result.Value!
                : throw new InvalidOperationException(
                    $"Could not seed the demo study in tenant {tenant}: {command.GetType().Name} was refused with {result.Error?.Code} ({result.Error?.Message}).");
        }
    }

    /// <summary>
    /// Ids derived from the tenant and a record's name (an RFC 9562 version 8 UUID over SHA-256), so a later start names
    /// the same records.
    /// </summary>
    private sealed class DemoIds(TenantId tenant)
    {
        public OrganizationId Organization(string name) => OrganizationId.FromGuid(Derive("organization", name));

        public StudyId Study(string name) => StudyId.FromGuid(Derive("study", name));

        public SiteId Site(string name) => SiteId.FromGuid(Derive("site", name));

        public StudySiteId StudySite(string name) => StudySiteId.FromGuid(Derive("study-site", name));

        public ParticipantId Participant(string name) => ParticipantId.FromGuid(Derive("participant", name));

        public EnrollmentId Enrollment(string name) => EnrollmentId.FromGuid(Derive("enrollment", name));

        public RoleId Role(string name) => RoleId.FromGuid(Derive("role", name));

        public GrantId Grant(string name) => GrantId.FromGuid(Derive("grant", name));

        private Guid Derive(string kind, string name)
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-development-demo:{tenant}:{kind}:{name}"), hash);
            var bytes = hash[..16];
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes, bigEndian: true);
        }
    }
}
