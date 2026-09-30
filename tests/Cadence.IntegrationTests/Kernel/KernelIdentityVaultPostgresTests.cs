using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Identity;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.IdentityVault;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The IdentityVault against PostgreSQL (spec §9.2, ADR-2253, ADR-2458): sealed storage outside the kernel schema,
/// audited reveals per layer (K-IDN-1), crypto-shredding, cross-study links (K-IDN-2), residency, and a vault the
/// application role reaches only through its functions. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelIdentityVaultPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Reason = "Identity vault test";

    private static readonly Actor Platform = Actor.ServiceAccount("vault-platform");
    private static readonly Actor Coordinator = Actor.Person("crc@vault.test");

    private readonly KernelProtocolScenario _scenario = new(fixture);

    public void Dispose()
    {
        if (fixture.ConnectionString is not null)
        {
            fixture.Authorizer.Inner = null;
            fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// Validates K-IDN-1 end to end: recorded details are stored sealed in the vault, and no kernel table holds any of
    /// them. They are disclosed only through a reveal that commits an audit entry and an <c>IdentityRevealed</c> event
    /// naming the actor, and a replayed reveal key discloses nothing.
    /// </summary>
    [Fact(DisplayName = "RecordAndReveal_DisclosesOnlyAfterAnAuditedReveal_AndNoKernelTableHoldsIdentifiers")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RecordAndRevealDisclosesOnlyAfterAnAuditedRevealAndNoKernelTableHoldsIdentifiers()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var person = await PersonAsync(tenant);
        var email = $"jane.doe.{Guid.NewGuid():N}@hospital.test";
        var details = new IdentityDetails
        {
            GivenName = "Jane",
            FamilyName = $"Doe{Guid.NewGuid():N}",
            BirthDate = new DateOnly(1970, 3, 14),
            Email = email,
            Phone = "+1 555 0100",
        };

        (await fixture.Sender.SendAsync(new RecordPersonIdentity(tenant, person, details, Key(), Reason), cancellationToken: ct))
            .Value.Should().Be(person);

        var stored = await fixture.IdentityVaultStore.FindAsync(tenant, IdentityLayer.Person, person.Value, ct);
        stored!.Sealed.Should().NotBeNull();
        Contains(stored.Sealed!.Ciphertext, email).Should().BeFalse("the details are encrypted");
        Contains(stored.Sealed.Ciphertext, details.FamilyName!).Should().BeFalse();
        stored.Sealed.BlindIndex.Should().NotBeNull();
        (await KernelRowsContainingAsync(email, ct)).Should().BeEmpty("direct identifiers never reach the kernel schema");
        (await KernelRowsContainingAsync(details.FamilyName!, ct)).Should().BeEmpty();

        var audited = await AuditCountAsync(tenant, ct);
        var reveal = new RevealPersonIdentity(tenant, person, Key(), "Contact the participant about a safety finding");
        var revealed = await fixture.IdentityVault.RevealAsync(reveal, ct);

        revealed.Value.Should().Be(details);
        (await AuditCountAsync(tenant, ct)).Should().Be(audited + 1);
        (await EventActorsAsync(tenant, "IdentityRevealed", ct)).Should().Equal(KernelPostgresFixture.ActorId);
        (await fixture.IdentityVault.RevealAsync(reveal, ct)).Error.Should().Be(IdentityVaultErrors.RevealReplayed);
        (await KernelRowsContainingAsync(email, ct)).Should().BeEmpty("a reveal records that it happened, not what it showed");
    }

    /// <summary>
    /// Validates K-IDN-1 with the production authorization: without a grant of <c>identity.reveal.person</c> the reveal is
    /// denied, the denial is audited and nothing is disclosed. A grant of another layer's reveal does not help; a grant of
    /// the person layer's does.
    /// </summary>
    [Fact(DisplayName = "Reveal_WithoutTheLayersCapability_IsDeniedAuditedAndDisclosesNothing")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RevealWithoutTheLayersCapabilityIsDeniedAuditedAndDisclosesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var person = await PersonAsync(tenant);
        await _scenario.OkAsync(new RecordPersonIdentity(tenant, person, new IdentityDetails { GivenName = "Ada", FamilyName = "Lovelace" }, Key(), Reason), ct);
        UseProductionAuthorization();

        fixture.CurrentActor.Actor = Coordinator;
        var denied = await fixture.IdentityVault.RevealAsync(new RevealPersonIdentity(tenant, person, Key(), "Curious"), ct);
        denied.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        denied.Value.Should().BeNull();
        (await EventActorsAsync(tenant, "DecisionDenied", ct)).Should().Equal(Coordinator.Id);
        (await EventActorsAsync(tenant, "IdentityRevealed", ct)).Should().BeEmpty();

        await IssueAsync(tenant, Coordinator, "identity.reveal.site_local", ct);
        fixture.CurrentActor.Actor = Coordinator;
        (await fixture.IdentityVault.RevealAsync(new RevealPersonIdentity(tenant, person, Key(), "Wrong layer"), ct)).Error!.Code
            .Should().Be(KernelErrors.AuthorizationDenied.Code);

        await IssueAsync(tenant, Coordinator, "identity.reveal.person", ct);
        fixture.CurrentActor.Actor = Coordinator;
        (await fixture.IdentityVault.RevealAsync(new RevealPersonIdentity(tenant, person, Key(), "Safety follow-up"), ct)).Value!.FamilyName
            .Should().Be("Lovelace");
        (await EventActorsAsync(tenant, "IdentityRevealed", ct)).Should().Equal(Coordinator.Id);
    }

    /// <summary>
    /// Validates that a reveal is an act a person performs (spec §11): an automation agent holding every reveal grant is
    /// denied each reveal on <c>actor.agent_verb</c>, the denial is audited, and nothing is disclosed.
    /// </summary>
    [Fact(DisplayName = "Reveal_ByAnAutomationAgent_IsDeniedEvenWithTheGrant")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RevealByAnAutomationAgentIsDeniedEvenWithTheGrant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var person = await PersonAsync(tenant);
        await _scenario.OkAsync(new RecordPersonIdentity(tenant, person, new IdentityDetails { GivenName = "Ada", FamilyName = "Lovelace" }, Key(), Reason), ct);
        UseProductionAuthorization();
        var agent = new Actor(ActorKind.AutomationAgent, "agent:identity-assistant");
        await IssueAsync(tenant, agent, "identity.reveal.person", ct);
        await IssueAsync(tenant, agent, "identity.reveal.links", ct);

        fixture.CurrentActor.Actor = agent;
        var identity = await fixture.IdentityVault.RevealAsync(new RevealPersonIdentity(tenant, person, Key(), "Automated lookup"), ct);
        var links = await fixture.IdentityVault.RevealLinksAsync(new RevealPersonLinks(tenant, person, Key(), "Automated lookup"), ct);

        foreach (var error in new[] { identity.Error, links.Error })
        {
            error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
            error.Explanation!.Conditions.Should().Contain(c => c.Code == AuthorityEvaluator.AgentVerbCondition && !c.Met);
        }

        identity.Value.Should().BeNull();
        links.Value.Should().BeNull();
        (await EventActorsAsync(tenant, "DecisionDenied", ct)).Should().Equal(agent.Id, agent.Id);
        (await EventActorsAsync(tenant, "IdentityRevealed", ct)).Should().BeEmpty();
        (await EventActorsAsync(tenant, "PersonLinksRevealed", ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Validates crypto-shredding (spec §9.2): erasing a site-local identifier deletes its data key, ciphertext and blind
    /// index, a reveal and a new record are refused, and the pseudonymous participant and its history remain.
    /// </summary>
    [Fact(DisplayName = "Erase_CryptoShredsTheRecord_AndKeepsThePseudonymousKernelRecords")]
    public async Task EraseCryptoShredsTheRecordAndKeepsThePseudonymousKernelRecords()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var participant = await ParticipantAsync(tenant, ct);
        await _scenario.OkAsync(new RecordSiteLocalIdentity(tenant, participant, new IdentityDetails { SiteLocalNumber = "scr-0042" }, Key(), Reason), ct);
        (await fixture.IdentityVault.RevealAsync(new RevealSiteLocalIdentity(tenant, participant, Key(), Reason), ct)).Value!.SiteLocalNumber
            .Should().Be("scr-0042");

        await _scenario.OkAsync(new EraseSiteLocalIdentity(tenant, participant, Key(), "Erasure request under GDPR Art. 17"), ct);

        var row = await AdminRecordAsync(tenant, participant.Value, ct);
        row.Should().Be(new VaultRow(null, null, null, null, true));
        (await fixture.IdentityVault.RevealAsync(new RevealSiteLocalIdentity(tenant, participant, Key(), Reason), ct)).Error
            .Should().Be(IdentityVaultErrors.Erased);
        (await fixture.Sender.SendAsync(new RecordSiteLocalIdentity(tenant, participant, new IdentityDetails { SiteLocalNumber = "SCR-9" }, Key(), Reason), cancellationToken: ct)).Error
            .Should().Be(IdentityVaultErrors.Erased);
        (await fixture.Sender.SendAsync(new EraseSiteLocalIdentity(tenant, participant, Key(), Reason), cancellationToken: ct)).Error
            .Should().Be(IdentityVaultErrors.Erased);

        await using var db = fixture.CreateContext(tenant);
        (await db.Participants.AsNoTracking().AnyAsync(p => p.Id == participant, ct)).Should().BeTrue();
        (await EventActorsAsync(tenant, "ParticipantRegistered", ct)).Should().HaveCount(1);
        (await EventActorsAsync(tenant, "IdentityErased", ct)).Should().HaveCount(1);
    }

    /// <summary>
    /// Validates ADR-2253's role separation: the application role holds no privilege on either vault table and cannot
    /// select from them, may execute only the vault's functions, and those functions refuse a connection with no tenant.
    /// </summary>
    [Fact(DisplayName = "ApplicationRole_CannotReadTheVaultTables_AndReachesThemOnlyThroughItsFunctions")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task ApplicationRoleCannotReadTheVaultTablesAndReachesThemOnlyThroughItsFunctions()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);

        foreach (var table in new[] { "identity_records", "participant_links" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                (await AppScalarAsync<bool>($"SELECT has_table_privilege('identity_vault.{table}', '{privilege}')", null, ct))
                    .Should().BeFalse($"the application role must not {privilege} identity_vault.{table}");
            }

            var select = () => AppScalarAsync<long>($"SELECT count(*) FROM identity_vault.{table}", tenant, ct);
            (await select.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        (await AppScalarAsync<bool>("SELECT has_function_privilege('identity_vault.get_record(text, uuid)', 'EXECUTE')", null, ct)).Should().BeTrue();
        (await AppScalarAsync<bool>("SELECT has_function_privilege('identity_vault.require_tenant()', 'EXECUTE')", null, ct)).Should().BeFalse();
        var untenanted = () => AppScalarAsync<object>($"SELECT count(*) FROM identity_vault.get_record('person', '{Guid.NewGuid()}')", null, ct);
        (await untenanted.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    /// <summary>
    /// Validates tenant isolation in the vault: a record of tenant A is invisible to tenant B even by its subject id,
    /// and tenant B's commands cannot erase or reveal it.
    /// </summary>
    [Fact(DisplayName = "VaultRecords_AreIsolatedByTenant")]
    [GxPRequirement("PRD-CRF-013")]
    public async Task VaultRecordsAreIsolatedByTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (a, _) = await _scenario.NewStudyAsync(ct);
        var (b, _) = await _scenario.NewStudyAsync(ct);
        var participant = await ParticipantAsync(a, ct);
        await _scenario.OkAsync(new RecordSiteLocalIdentity(a, participant, new IdentityDetails { SiteLocalNumber = "A-001" }, Key(), Reason), ct);

        (await fixture.IdentityVaultStore.FindAsync(b, IdentityLayer.SiteLocal, participant.Value, ct)).Should().BeNull();
        (await AppScalarAsync<long>($"SELECT count(*) FROM identity_vault.get_record('site_local', '{participant.Value}')", b, ct)).Should().Be(0);
        (await fixture.IdentityVault.RevealAsync(new RevealSiteLocalIdentity(b, participant, Key(), Reason), ct)).Error
            .Should().Be(IdentityVaultErrors.SubjectUnknown);
        (await fixture.Sender.SendAsync(new EraseSiteLocalIdentity(b, participant, Key(), Reason), cancellationToken: ct)).Error
            .Should().Be(IdentityVaultErrors.SubjectUnknown);
        (await fixture.IdentityVaultStore.FindAsync(a, IdentityLayer.SiteLocal, participant.Value, ct))!.IsErased.Should().BeFalse();
    }

    /// <summary>
    /// Validates residency as a tenant property the vault honours (spec §13): this vault serves the EU, so a tenant
    /// resident in the US cannot record identifiers here, while an EU tenant can. A malformed residency is refused.
    /// </summary>
    [Fact(DisplayName = "Residency_AVaultRefusesTenantsOfAnotherRegion")]
    public async Task ResidencyAVaultRefusesTenantsOfAnotherRegion()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var us = await TenantAsync("US", ct);
        var eu = await TenantAsync(KernelPostgresFixture.VaultRegion, ct);
        var usParticipant = await ParticipantAsync(us, ct);
        var euParticipant = await ParticipantAsync(eu, ct);

        (await fixture.Sender.SendAsync(new RecordSiteLocalIdentity(us, usParticipant, new IdentityDetails { SiteLocalNumber = "US-1" }, Key(), Reason), cancellationToken: ct)).Error
            .Should().Be(IdentityVaultErrors.ResidencyMismatch);
        (await fixture.IdentityVaultStore.FindAsync(us, IdentityLayer.SiteLocal, usParticipant.Value, ct)).Should().BeNull();
        await _scenario.OkAsync(new RecordSiteLocalIdentity(eu, euParticipant, new IdentityDetails { SiteLocalNumber = "EU-1" }, Key(), Reason), ct);
        (await fixture.Sender.SendAsync(new ProvisionTenant(TenantId.New(), Code(), "Lower-case", Key(), Reason, "eu"), cancellationToken: ct)).Error
            .Should().Be(IdentityErrors.ResidencyInvalid);
    }

    /// <summary>
    /// Validates K-IDN-2: linking participants of two studies to one person is a vault operation whose event does not
    /// name the person; the links are disclosed through an audited reveal. A participant links to one person only.
    /// </summary>
    [Fact(DisplayName = "Link_JoinsAPersonsParticipantsAcrossStudies_InsideTheVault")]
    public async Task LinkJoinsAPersonsParticipantsAcrossStudiesInsideTheVault()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        var person = await PersonAsync(tenant);
        var otherPerson = await PersonAsync(tenant);
        var first = await ParticipantAsync(tenant, ct);
        var second = await ParticipantAsync(tenant, ct);

        await _scenario.OkAsync(new LinkParticipantToPerson(tenant, first, person, Key(), "Same patient, second study"), ct);
        await _scenario.OkAsync(new LinkParticipantToPerson(tenant, second, person, Key(), "Same patient, second study"), ct);
        await _scenario.OkAsync(new LinkParticipantToPerson(tenant, first, person, Key(), "Linked again"), ct);
        (await fixture.Sender.SendAsync(new LinkParticipantToPerson(tenant, first, otherPerson, Key(), Reason), cancellationToken: ct)).Error
            .Should().Be(IdentityVaultErrors.AlreadyLinked);

        (await EventActorsAsync(tenant, "ParticipantLinked", ct)).Should().HaveCount(2);
        (await KernelRowsContainingAsync(person.Value.ToString(), ct, exceptTables: ["people", "tenant_memberships"]))
            .Should().BeEmpty("the link stays in the vault");

        (await fixture.IdentityVault.RevealLinksAsync(new RevealPersonLinks(tenant, person, Key(), Reason), ct)).Value
            .Should().Equal(new[] { first, second }.OrderBy(p => p.Value));
        (await fixture.IdentityVault.RevealLinksAsync(new RevealPersonLinks(tenant, otherPerson, Key(), Reason), ct)).Value.Should().BeEmpty();
        (await EventActorsAsync(tenant, "PersonLinksRevealed", ct)).Should().HaveCount(2);
    }

    /// <summary>
    /// Validates the schema rule behind K-IDN-1 on the migrated database: no column of the kernel schema is named like a
    /// direct identifier. Names, dates of birth and contact details live only in <c>identity_vault</c>.
    /// </summary>
    [Fact(DisplayName = "KernelSchema_HasNoDirectIdentifierColumn")]
    public async Task KernelSchemaHasNoDirectIdentifierColumn()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();

        var columns = await db.Database.SqlQuery<string>(
                $"SELECT table_name || '.' || column_name AS \"Value\" FROM information_schema.columns WHERE table_schema = {KernelDbContext.Schema}")
            .ToListAsync(ct);

        columns.Where(c => DirectIdentifierColumns.IsIdentifier(c.Split('.')[1])).Should().BeEmpty();
        columns.Should().NotBeEmpty();
    }

    private void UseProductionAuthorization() =>
        fixture.Authorizer.Inner = new KernelAuthorization(
            new AuthorityEvaluator(fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>([Platform.Id]))),
            fixture.CurrentActor,
            KernelCommandCatalog.Kernel,
            fixture.Clock,
            NullLogger<KernelAuthorization>.Instance);

    private async Task IssueAsync(TenantId tenant, Actor grantee, string capability, CancellationToken ct)
    {
        fixture.CurrentActor.Actor = Platform;
        await _scenario.OkAsync(
            new IssueGrant(tenant, GrantId.New(), grantee, capability, null, AuthorityScope.Tenant(), fixture.Clock.UtcNow, null, false, Key(), "Appointed"), ct);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
    }

    private async Task<TenantId> TenantAsync(string residency, CancellationToken ct)
    {
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, Code(), $"Tenant in {residency}", Key(), Reason, residency), ct);
        return tenant;
    }

    private async Task<PersonId> PersonAsync(TenantId tenant)
    {
        var person = new Person(PersonId.New(), KernelPostgresFixture.ActorId, Reason);
        await fixture.SeedAsync(person);
        await fixture.SeedAsync(new TenantMembership(TenantMembershipId.New(), tenant, person.Id, KernelPostgresFixture.ActorId, Reason));
        return person.Id;
    }

    private Task<ParticipantId> ParticipantAsync(TenantId tenant, CancellationToken ct) =>
        _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), Key(), Reason), ct);

    private async Task<long> AuditCountAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return await db.AuditEntries.AsNoTracking().LongCountAsync(e => e.TenantId == tenant, ct);
    }

    private async Task<List<string>> EventActorsAsync(TenantId tenant, string type, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return await db.DomainEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant && e.Type == type)
            .OrderBy(e => e.Recorded)
            .Select(e => e.ActorId)
            .ToListAsync(ct);
    }

    /// <summary>Every kernel table with a row whose text form contains <paramref name="needle"/>, read as the superuser.</summary>
    private async Task<List<string>> KernelRowsContainingAsync(string needle, CancellationToken ct, string[]? exceptTables = null)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'kernel' AND table_type = 'BASE TABLE'", connection))
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var hits = new List<string>();
        foreach (var table in tables.Except(exceptTables ?? []))
        {
            await using var probe = new NpgsqlCommand($"SELECT count(*) FROM kernel.\"{table}\" t WHERE t::text ILIKE '%' || @needle || '%'", connection);
            probe.Parameters.AddWithValue("needle", needle);
            if ((long)(await probe.ExecuteScalarAsync(ct))! > 0)
            {
                hits.Add(table);
            }
        }

        return hits;
    }

    private async Task<VaultRow?> AdminRecordAsync(TenantId tenant, Guid subject, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return await db.Database.SqlQuery<VaultRow>(
                $"""
                SELECT key_id AS "KeyId", wrapped_data_key AS "WrappedDataKey", ciphertext AS "Ciphertext",
                       blind_index AS "BlindIndex", erased_at IS NOT NULL AS "Erased"
                FROM identity_vault.identity_records WHERE tenant_id = {tenant.Value} AND subject_id = {subject}
                """)
            .SingleOrDefaultAsync(ct);
    }

    private async Task<T> AppScalarAsync<T>(string sql, TenantId? tenant, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        if (tenant is { } scoped)
        {
            await using var set = new NpgsqlCommand($"SELECT set_config('{TenantIsolationSql.TenantSetting}', '{scoped.Value}', false)", connection);
            await set.ExecuteNonQueryAsync(ct);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(ct))!;
    }

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.UTF8.GetBytes(needle)) >= 0;

    private static string Key() => KernelProtocolScenario.Key();

    private static string Code() => $"T-{Guid.NewGuid():N}"[..20];

    private sealed record VaultRow(string? KeyId, byte[]? WrappedDataKey, byte[]? Ciphertext, byte[]? BlindIndex, bool Erased);
}
