using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Standards;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Knowledge;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The Standards Registry and Metadata Registry on PostgreSQL through the command pipeline (spec §6.3, #4809), as the
/// application role under Row-Level Security. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
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
    [KernelRule("K-ID-2")]
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

        await ImportSdtmCtAsync(tenant, await RegisterSdtmCtAsync(tenant, ct), "2024-09-27", ct);

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

        var batched = await fixture.Standards.FindLibraryItemsAsync(tenant, [v1.Id, v2.Id, LibraryItemId.New()], ct);
        batched.Keys.Should().BeEquivalentTo([v1.Id, v2.Id]);
        batched[v1.Id].Status.Should().Be(GovernanceStatus.Deprecated);
        batched[v2.Id].Status.Should().Be(GovernanceStatus.Draft);

        (await DeclareAsync(tenant, graph.Study.Id, profileId, sdtmig, v1.Id, ct)).Error!.Code.Should().Be(StandardsErrors.NotApproved.Code);
        (await DeclareAsync(tenant, graph.Study.Id, StudyStandardsProfileId.New(), sdtmig, v1.Id, ct)).Error!.Code
            .Should().Be(StandardsErrors.DeclarationInvalid.Code, "a study has one profile, revised in place");
    }

    /// <summary>
    /// Approving or deprecating a library item the tenant does not have is refused before any signature is checked, and
    /// a study cannot pin one.
    /// </summary>
    [Fact(DisplayName = "LibraryItemCommands_RefuseUnknownItems")]
    public async Task LibraryItemCommandsRefuseUnknownItems()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;
        var sdtmig = await RegisterAsync(tenant, Seed("SDTMIG"), ct);
        var approval = await SignStandardAsync(tenant, sdtmig, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, approval, Key(), Reason), ct)).IsSuccess.Should().BeTrue();
        await ImportSdtmCtAsync(tenant, await RegisterSdtmCtAsync(tenant, ct), "2024-09-27", ct);
        var unknown = LibraryItemId.New();

        (await SendAsync(new ApproveLibraryItem(tenant, unknown, AttestationId.New(), Key(), Reason), ct)).Error
            .Should().Be(StandardsErrors.LibraryItemUnknown);
        (await SendAsync(new DeprecateLibraryItem(tenant, unknown, AttestationId.New(), Key(), Reason), ct)).Error
            .Should().Be(StandardsErrors.LibraryItemUnknown);
        (await DeclareAsync(tenant, graph.Study.Id, StudyStandardsProfileId.New(), sdtmig, unknown, ct)).Error!.Code
            .Should().Be(StandardsErrors.LibraryItemUnknown.Code);
        (await fixture.Standards.FindStudyStandardsAsync(tenant, graph.Study.Id, ct)).Should().BeNull();
    }

    /// <summary>
    /// Terminology bindings resolve to the tenant's Concept Registry (ADR-2460 §4.2, #4888). A standard version keeps
    /// the code-system version its binding resolved to when it was registered, or its natural key alone when the
    /// package was not imported yet. A study can declare only terminology the tenant imported, and it keeps the version
    /// each declaration resolved to.
    /// </summary>
    [Fact(DisplayName = "TerminologyBindings_ResolveToImportedVersions_AndUnimportedDeclarationsAreRefused")]
    public async Task TerminologyBindingsResolveToImportedVersionsAndUnimportedDeclarationsAreRefused()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Study);
        var tenant = graph.Tenant.Id;
        var sdtmct = new CodeSystemVersionRef("sdtmct", "2024-09-27");

        var adamig = await RegisterAsync(tenant, Seed("ADaMIG"), ct);
        var before = await RegisterAsync(tenant, Seed("SDTMIG") with { Version = "3.4-before-import" }, ct);
        (await fixture.Standards.FindStandardVersionAsync(tenant, before, ct))!.ResolvedTerminology.Should().BeEmpty();

        var codeSystem = await RegisterSdtmCtAsync(tenant, ct);
        var imported = await ImportSdtmCtAsync(tenant, codeSystem, sdtmct.Version, ct);
        var sdtmig = await RegisterAsync(tenant, Seed("SDTMIG"), ct);
        var summary = await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct);
        summary!.ControlledTerminology.Should().Equal(sdtmct);
        summary.ResolvedTerminology.Should().Equal(new ResolvedCodeSystemVersion(sdtmct, imported));
        (await fixture.Standards.FindStandardVersionAsync(tenant, before, ct))!.ResolvedTerminology
            .Should().BeEmpty("a binding keeps what it resolved to when it was registered");

        var approval = await SignStandardAsync(tenant, sdtmig, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, approval, Key(), Reason), ct)).Error.Should().BeNull();
        var adamApproval = await SignStandardAsync(tenant, adamig, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, adamig, adamApproval, Key(), Reason), ct)).Error.Should().BeNull();

        var older = await DeclareTerminologyAsync(tenant, graph.Study.Id, sdtmig, sdtmct with { Version = "2023-12-15" }, ct);
        older.Error!.Code.Should().Be(StandardsErrors.DeclarationInvalid.Code);
        older.Error.Message.Should().Contain("sdtmct-2023-12-15 has not been imported");
        (await DeclareTerminologyAsync(tenant, graph.Study.Id, sdtmig, new CodeSystemVersionRef(KnownCodeSystems.MedDra, "27.0"), ct))
            .Error!.Message.Should().Contain("has not been imported");
        (await fixture.Standards.FindStudyStandardsAsync(tenant, graph.Study.Id, ct)).Should().BeNull("a refused declaration writes nothing");

        (await DeclareTerminologyAsync(tenant, graph.Study.Id, sdtmig, sdtmct, ct)).Error.Should().BeNull();
        var profile = await fixture.Standards.FindStudyStandardsAsync(tenant, graph.Study.Id, ct);
        profile!.ControlledTerminology.Should().Equal(sdtmct);
        profile.ResolvedTerminology.Should().Equal(new ResolvedCodeSystemVersion(sdtmct, imported));

        await using var db = fixture.CreateAdminContext();
        (await db.Set<StandardTerminologyBinding>().AsNoTracking()
            .Where(b => b.TenantId == tenant && b.StandardVersionId == sdtmig).Select(b => b.CodeSystemVersionId).SingleAsync(ct))
            .Should().Be(imported);
    }

    /// <summary>
    /// A tenant can declare only terminology it imported itself: another tenant's import of the same package does not
    /// resolve (K-ID-2).
    /// @req:PRD-CRF-013
    /// </summary>
    [Fact(DisplayName = "TerminologyResolution_IsTenantScoped")]
    [GxPRequirement("PRD-CRF-013")]
    [KernelRule("K-ID-2")]
    public async Task TerminologyResolutionIsTenantScoped()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var sdtmct = new CodeSystemVersionRef("sdtmct", "2024-09-27");
        var importer = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(importer.Tenant, importer.Sponsor, importer.Study);
        await ImportSdtmCtAsync(importer.Tenant.Id, await RegisterSdtmCtAsync(importer.Tenant.Id, ct), sdtmct.Version, ct);

        var other = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(other.Tenant, other.Sponsor, other.Study);
        var tenant = other.Tenant.Id;
        var sdtmig = await RegisterAsync(tenant, Seed("SDTMIG"), ct);
        (await fixture.Standards.FindStandardVersionAsync(tenant, sdtmig, ct))!.ResolvedTerminology.Should().BeEmpty();
        var approval = await SignStandardAsync(tenant, sdtmig, AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, sdtmig, approval, Key(), Reason), ct)).Error.Should().BeNull();

        (await DeclareTerminologyAsync(tenant, other.Study.Id, sdtmig, sdtmct, ct)).Error!.Message.Should().Contain("has not been imported");
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

    private Task<CommandResult<StudyStandardsProfileId>> DeclareTerminologyAsync(
        TenantId tenant, StudyId study, StandardVersionId standard, CodeSystemVersionRef terminology, CancellationToken ct) =>
        SendAsync(new DeclareStudyStandards(tenant, study, StudyStandardsProfileId.New(), [standard], [terminology], [], Key(), Reason), ct);

    private async Task<CodeSystemId> RegisterSdtmCtAsync(TenantId tenant, CancellationToken ct)
    {
        var result = await SendAsync(new RegisterCodeSystem(
            tenant, CodeSystemId.New(), KnownCodeSystems.SdtmCt, "SDTM Controlled Terminology", "CDISC", LicenseClass.Open, Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    /// <summary>Imports the CT fixture as <paramref name="version"/> of the tenant's SDTM CT code system.</summary>
    private async Task<CodeSystemVersionId> ImportSdtmCtAsync(TenantId tenant, CodeSystemId codeSystem, string version, CancellationToken ct)
    {
        var release = new TerminologyRelease(
            tenant, codeSystem, CodeSystemVersionId.New(), version, new DateOnly(2024, 9, 27), "SDTM Terminology.txt",
            "https://evs.nci.nih.gov/ftp1/CDISC/SDTM/", TerminologyFixtures.Sha256(TerminologyFixtures.CdiscCt), Reason);
        var result = await new TerminologyPackageLoader(fixture.Sender)
            .LoadAsync(release, TerminologyFixtures.CdiscCt, new CdiscControlledTerminologyParser("sdtmct"), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

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
