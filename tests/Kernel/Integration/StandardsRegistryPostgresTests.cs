using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Standards;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// The Standards Registry and Metadata Registry on PostgreSQL through the command pipeline (spec §6.3, #4809), as the
/// application role under Row-Level Security. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StandardsRegistryPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Standards registry test";

    /// <summary>
    /// Validates that registering and approving a standard version are audited kernel writes: each leaves an audit
    /// entry naming the actor, the aggregate and the reason, and an event, and the registered definitions read back
    /// exactly (the package re-hashes to the stored content hash).
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "RegisterAndApproveStandardVersion_AreAudited_AndReadBackExactly")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RegisterAndApproveStandardVersionAreAuditedAndReadBackExactly()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var package = Seed("SDTMIG");

        var id = await RegisterAsync(tenant, package, ct);
        var approval = await SignStandardAsync(tenant, id, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, id, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        var summary = await fixture.Standards.FindStandardVersionAsync(tenant, id, ct);
        summary!.Status.Should().Be(GovernanceStatus.Approved);
        summary.ControlledTerminology.Should().Equal(new CodeSystemVersionRef("sdtmct", "2024-09-27"));
        var stored = await fixture.Standards.GetStandardPackageAsync(tenant, id, ct);
        StandardPackageContent.Hash(stored!).Should().Be(summary.ContentHash);
        stored!.Datasets.Sum(d => d.Variables.Count).Should().Be(package.Datasets.Sum(d => d.Variables.Count));
        (await fixture.Standards.FindStandardVersionAsync(tenant, "SDTMIG", "3.4", ct))!.Id.Should().Be(id);

        await using var db = fixture.CreateContext(tenant);
        var audit = await db.AuditEntries.AsNoTracking()
            .Where(e => e.AggregateType == nameof(StandardVersion)).OrderBy(e => e.Sequence).ToListAsync(ct);
        audit.Select(e => e.Action).Should().Equal("Created", "Updated");
        audit.Should().OnlyContain(e => e.ActorId == KernelPostgresFixture.ActorId && e.ReasonForChange == Reason && e.AggregateId == id.ToString());
        (await db.DomainEvents.AsNoTracking().Where(e => e.Type.StartsWith("StandardVersion")).Select(e => e.Type).ToListAsync(ct))
            .Should().BeEquivalentTo("StandardVersionRegistered", "StandardVersionApproved");
    }

    /// <summary>
    /// Validates that approval needs an attestation from the kernel's store in which the acting person signed
    /// <c>Approved</c> over this version's content hash: an attestation with another meaning or by another person is
    /// rejected with the reason and leaves the version a draft, and a matching one approves it.
    /// </summary>
    [Fact(DisplayName = "ApproveStandardVersion_RequiresTheActorsApprovalAttestation")]
    public async Task ApproveStandardVersionRequiresTheActorsApprovalAttestation()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var id = await RegisterAsync(tenant, Seed("ADaMIG"), ct);

        var reviewed = await SignStandardAsync(tenant, id, AttestationMeaning.Reviewed, ct);
        var wrongMeaning = await SendAsync(new ApproveStandardVersion(tenant, id, reviewed, Key(), Reason), ct);
        wrongMeaning.Error!.Code.Should().Be(StandardsErrors.AttestationRejected.Code);
        wrongMeaning.Error.Message.Should().Contain("Reviewed");

        var byColleague = await SignStandardAsync(tenant, id, AttestationMeaning.Approved, ct, Actor.Person("00u9colleague"));
        var otherSigner = await SendAsync(new ApproveStandardVersion(tenant, id, byColleague, Key(), Reason), ct);
        otherSigner.Error!.Code.Should().Be(StandardsErrors.AttestationRejected.Code);
        otherSigner.Error.Message.Should().Contain("someone else");
        (await fixture.Standards.FindStandardVersionAsync(tenant, id, ct))!.Status.Should().Be(GovernanceStatus.Draft);

        var approval = await SignStandardAsync(tenant, id, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, id, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        (await fixture.Standards.FindStandardVersionAsync(tenant, id, ct))!.Status.Should().Be(GovernanceStatus.Approved);
    }

    /// <summary>
    /// Validates that a standard version is registered once per tenant: the same code and version under a new id is
    /// refused, and another tenant registers its own copy that the first tenant cannot see (K-ID-2).
    /// </summary>
    [Fact(DisplayName = "RegisterStandardVersion_IsOncePerTenant_AndTenantScoped")]
    public async Task RegisterStandardVersionIsOncePerTenantAndTenantScoped()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenantA = await ProvisionTenantAsync(ct);
        var tenantB = await ProvisionTenantAsync(ct);
        var package = Seed("ADaMIG");

        var inA = await RegisterAsync(tenantA, package, ct);
        var again = await SendAsync(new RegisterStandardVersion(tenantA, StandardVersionId.New(), package, null, Key(), Reason), ct);
        var inB = await RegisterAsync(tenantB, package, ct);

        again.Error!.Code.Should().Be(StandardsErrors.VersionAlreadyRegistered.Code);
        (await fixture.Standards.ListStandardVersionsAsync(tenantA, ct)).Select(s => s.Id).Should().Equal(inA);
        (await fixture.Standards.FindStandardVersionAsync(tenantA, inB, ct)).Should().BeNull();
    }

    /// <summary>
    /// Validates library versioning and study pinning: a study pins approved versions, deprecating what it pinned
    /// (and its standard version) never changes its declaration, and a new declaration cannot pin a deprecated version.
    /// </summary>
    [Fact(DisplayName = "StudyPins_SurviveDeprecation_AndOnlyApprovedCanBePinned")]
    public async Task StudyPinsSurviveDeprecationAndOnlyApprovedCanBePinned()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;
        var sdtmig = await RegisterAsync(tenant, Seed("SDTMIG"), ct);
        var standardApproval = await SignStandardAsync(tenant, sdtmig, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, standardApproval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        using var units = JsonDocument.Parse("""{"codelist":"C66770","terms":["mmHg","beats/min"]}""");
        var v1 = await DraftAsync(tenant, units.RootElement, [sdtmig], ct);
        var v2 = await DraftAsync(tenant, units.RootElement, [sdtmig], ct);
        v1.Version.Should().Be(1);
        v2.Version.Should().Be(2);
        v2.ContentHash.Should().Be(v1.ContentHash);

        var pinDraft = await DeclareAsync(tenant, graph.Study.Id, StudyStandardsProfileId.New(), sdtmig, v1.Id, ct);
        pinDraft.Error!.Code.Should().Be(StandardsErrors.NotApproved.Code);

        var itemApproval = await SignLibraryItemAsync(tenant, v1, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, v1.Id, itemApproval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        var profileId = StudyStandardsProfileId.New();
        (await DeclareAsync(tenant, graph.Study.Id, profileId, sdtmig, v1.Id, ct)).Value.Should().Be(profileId);

        var itemDeprecation = await SignLibraryItemAsync(tenant, v1, AttestationMeaning.Responsibility, ct);
        (await SendAsync(new DeprecateLibraryItem(tenant, v1.Id, itemDeprecation, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        var standardDeprecation = await SignStandardAsync(tenant, sdtmig, AttestationMeaning.Responsibility, ct);
        (await SendAsync(new DeprecateStandardVersion(tenant, sdtmig, standardDeprecation, Key(), Reason), ct)).IsSuccess.Should().BeTrue();

        var profile = await fixture.Standards.FindStudyStandardsAsync(tenant, graph.Study.Id, ct);
        profile!.Revision.Should().Be(1);
        profile.StandardVersions.Should().Equal(sdtmig);
        profile.LibraryPins.Should().Equal(v1.Id);
        profile.ControlledTerminology.Should().Equal(new CodeSystemVersionRef("sdtmct", "2024-09-27"));

        (await DeclareAsync(tenant, graph.Study.Id, profileId, sdtmig, v1.Id, ct)).Error!.Code.Should().Be(StandardsErrors.NotApproved.Code);
        (await DeclareAsync(tenant, graph.Study.Id, StudyStandardsProfileId.New(), sdtmig, v1.Id, ct)).Error!.Code
            .Should().Be(StandardsErrors.DeclarationInvalid.Code, "a study has one profile, revised in place");
    }

    /// <summary>
    /// Validates the database guards: registered definitions are insert-only (SQLSTATE KA001) and the identity and
    /// content columns of a standard version are fixed (SQLSTATE KS001), even for the schema owner.
    /// </summary>
    [Fact(DisplayName = "RegisteredDefinitions_CannotBeChanged_InTheDatabase")]
    public async Task RegisteredDefinitionsCannotBeChangedInTheDatabase()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionTenantAsync(ct);
        var id = await RegisterAsync(tenant, Seed("ADaMIG"), ct);

        (await AdminExecuteAsync($"UPDATE kernel.standard_variables SET label = 'x' WHERE standard_version_id = '{id}'", ct))!
            .SqlState.Should().Be(InsertOnlySql.ViolationSqlState);
        (await AdminExecuteAsync($"UPDATE kernel.standard_versions SET content_hash = 'sha256:0' WHERE id = '{id}'", ct))!
            .SqlState.Should().Be(ImmutableColumnsSql.ViolationSqlState);
        (await AdminExecuteAsync($"UPDATE kernel.standard_versions SET status = 'Approved', version_index = version_index + 1 WHERE id = '{id}'", ct))
            .Should().BeNull("governance columns may change");
    }

    private async Task<TenantId> ProvisionTenantAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var command = new ProvisionTenant(TenantId.New(), $"T-{suffix}", $"Tenant {suffix}", Key(), Reason);
        (await SendAsync(command, ct)).IsSuccess.Should().BeTrue();
        return command.TenantId;
    }

    private async Task<StandardVersionId> RegisterAsync(TenantId tenant, StandardPackage package, CancellationToken ct)
    {
        var result = await SendAsync(new RegisterStandardVersion(tenant, StandardVersionId.New(), package, null, Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private async Task<LibraryItemVersion> DraftAsync(TenantId tenant, JsonElement content, StandardVersionId[] standards, CancellationToken ct)
    {
        var result = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.CodelistSubset, "VS original units", "Vital sign units", content, standards, Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

    private Task<CommandResult<StudyStandardsProfileId>> DeclareAsync(
        TenantId tenant, StudyId study, StudyStandardsProfileId profile, StandardVersionId standard, LibraryItemId pin, CancellationToken ct) =>
        SendAsync(new DeclareStudyStandards(
            tenant, study, profile, [standard], [new CodeSystemVersionRef("sdtmct", "2024-09-27")], [pin], Key(), Reason), ct);

    /// <summary>Signs <paramref name="meaning"/> over a standard version's current content hash, as the fixture's person unless another signer is given.</summary>
    private async Task<AttestationId> SignStandardAsync(
        TenantId tenant, StandardVersionId id, AttestationMeaning meaning, CancellationToken ct, Actor? signer = null)
    {
        var summary = await fixture.Standards.FindStandardVersionAsync(tenant, id, ct);
        return await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, id.Value, summary!.ContentHash), meaning, signer, ct);
    }

    private Task<AttestationId> SignLibraryItemAsync(TenantId tenant, LibraryItemVersion item, AttestationMeaning meaning, CancellationToken ct) =>
        SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, item.Id.Value, item.ContentHash), meaning, signer: null, ct);

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationTarget target, AttestationMeaning meaning, Actor? signer, CancellationToken ct)
    {
        var acting = fixture.CurrentActor.Actor;
        fixture.CurrentActor.Actor = signer ?? acting;
        try
        {
            var result = await SendAsync(
                new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}"), ct);
            result.Error.Should().BeNull();
            return result.Value;
        }
        finally
        {
            fixture.CurrentActor.Actor = acting;
        }
    }

    private Task<CommandResult<T>> SendAsync<T>(ICommand<T> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private async Task<PostgresException?> AdminExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }

    private static StandardPackage Seed(string code) =>
        StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == code).Package;

    private static string Key() => $"key-{Guid.NewGuid():N}";
}
