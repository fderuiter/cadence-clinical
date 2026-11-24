using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Execution;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Effectivity and study-site activation through the production pipeline against PostgreSQL (spec §7.1, #4777): a
/// version becomes effective only on approval evidence (K-TIME-5), a study site activates only where a version is in
/// force (K-TIME-4), the reader resolves the most specific scope as of any valid and known time, and the status history
/// is append-only whoever connects. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelEffectivityPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = KernelProtocolScenario.Reason;

    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates the site activation gate: a study site cannot be activated while no approved protocol version is
    /// effective there, and can be once an ethics approval makes the version effective. A country-level effectivity
    /// that is still pending keeps a site in that country closed although the version is effective study-wide.
    /// </summary>
    [Fact(DisplayName = "StudySite_Activation_NeedsAnEffectiveVersionOnApprovalEvidence")]
    [GxPRequirement("PRD-CTMS-005")]
    [KernelRule("K-TIME-4")]
    [KernelRule("K-TIME-5")]
    public async Task StudySiteActivationNeedsAnEffectiveVersionOnApprovalEvidence()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var boston = await AddStudySiteAsync(tenant, study, "US", ct);
        var berlin = await AddStudySiteAsync(tenant, study, "DE", ct);

        var studyWide = EffectivityId.New();
        var declared = await _scenario.OkAsync(Declare(tenant, studyWide, version.ProtocolVersionId, EffectivityScope.Study(), Tick()), ct);
        await _scenario.OkAsync(Declare(tenant, EffectivityId.New(), version.ProtocolVersionId, EffectivityScope.InCountry("DE"), Tick()), ct);
        declared.Status.Should().Be(EffectivityStatus.Pending);

        Tick();
        (await ActivateAsync(tenant, boston, ct)).Error.Should().Be(EffectivityErrors.NoEffectiveVersion);
        var beforeEffective = fixture.Clock.UtcNow;

        var approval = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, study, ct);
        var effectiveAt = Tick();
        var effective = await _scenario.OkAsync(ChangeStatus(tenant, studyWide, EffectivityStatus.Effective, effectiveAt, approval), ct);
        effective.Should().BeEquivalentTo(new EffectivityState(studyWide, version.ProtocolVersionId, EffectivityScope.Study(), EffectivityStatus.Effective, effectiveAt, [approval]));

        Tick();
        var activated = await _scenario.OkAsync(new ActivateStudySite(tenant, boston, KernelProtocolScenario.Key(), "Greenlight received"), ct);
        activated.Should().BeEquivalentTo(new StudySiteState(
            boston, ActivateStudySiteHandler.ActiveStatus, [new EffectiveVersion(version.ProtocolVersionId, version.Number, studyWide, EffectivityLevel.Study)]));
        (await ActivateAsync(tenant, boston, ct)).Error.Should().Be(EffectivityErrors.StudySiteAlreadyActive);
        (await ActivateAsync(tenant, berlin, ct)).Error.Should().Be(EffectivityErrors.NoEffectiveVersion, "the German effectivity is still pending");
        (await ActivateAsync(tenant, StudySiteId.New(), ct)).Error.Should().Be(EffectivityErrors.StudySiteUnknown);

        await using var db = fixture.CreateContext(tenant);
        (await db.StudySites.AsNoTracking().SingleAsync(s => s.Id == boston, ct)).Status.Should().Be(ActivateStudySiteHandler.ActiveStatus);
        (await db.StudySites.AsNoTracking().SingleAsync(s => s.Id == berlin, ct)).Status.Should().Be("Selected");
        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant).OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().ContainInOrder("EffectivityDeclared", "EffectivityDeclared", "EffectivityStatusChanged", "StudySiteActivated");

        var reader = fixture.Effectivity;
        var now = fixture.Clock.UtcNow;
        (await reader.EffectiveAtAsync(tenant, boston, now, now, ct)).Should().ContainSingle().Which.EffectivityId.Should().Be(studyWide);
        (await reader.EffectiveAtAsync(tenant, berlin, now, now, ct)).Should().BeEmpty();
        (await reader.EffectiveAtAsync(tenant, boston, now, beforeEffective, ct)).Should().BeEmpty("the approval was not yet known");
        (await reader.EffectiveAtAsync(tenant, boston, effectiveAt.AddTicks(-10), now, ct)).Should().BeEmpty("the version was not yet effective");
        (await reader.EffectiveAtAsync(tenant, StudySiteId.New(), now, now, ct)).Should().BeEmpty();
    }

    /// <summary>
    /// Validates the evidence rule (K-TIME-5): becoming effective needs an approval of the version's study; no evidence,
    /// the wrong kind of artifact, another study's approval or an unknown version are refused, and a move that takes no
    /// evidence refuses any. An ethics approval moves a pending effectivity to EthicsApproved.
    /// </summary>
    [Fact(DisplayName = "Effectivity_ChangeStatus_ChecksEvidence")]
    [KernelRule("K-TIME-5")]
    public async Task EffectivityChangeStatusChecksEvidence()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var otherStudy = await NewStudyInTenantAsync(tenant, ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var effectivity = EffectivityId.New();
        await _scenario.OkAsync(Declare(tenant, effectivity, version.ProtocolVersionId, EffectivityScope.InCountry("US"), Tick()), ct);
        var labReport = await RegisterAsync(tenant, ArtifactKind.LabReport, study, ct);
        var foreignApproval = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, otherStudy, ct);
        var ethics = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, study, ct);
        var tenantWideCta = await RegisterAsync(tenant, ArtifactKind.CtaAcknowledgement, studyId: null, ct);

        (await SendAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Effective, Tick()), ct)).Error.Should().Be(EffectivityErrors.EvidenceRequired);
        foreach (var bad in new[] { labReport, foreignApproval, ArtifactVersionId.New() })
        {
            (await SendAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), bad), ct)).Error!.Code
                .Should().Be(EffectivityErrors.EvidenceInvalid.Code);
        }

        (await SendAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Withdrawn, Tick(), ethics), ct)).Error!.Code
            .Should().Be(EffectivityErrors.EvidenceInvalid.Code);
        (await SendAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.EthicsApproved, Tick(), tenantWideCta), ct)).Error!.Code
            .Should().Be(EffectivityErrors.EvidenceInvalid.Code, "a CTA acknowledgement is regulatory, not ethics, evidence");
        (await SendAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Pending, Tick()), ct)).Error!.Code
            .Should().Be(EffectivityErrors.InvalidTransition.Code);
        (await SendAsync(ChangeStatus(tenant, EffectivityId.New(), EffectivityStatus.Withdrawn, Tick()), ct)).Error.Should().Be(KernelErrors.NotFound);

        var ethicsApproved = await _scenario.OkAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.EthicsApproved, Tick(), ethics), ct);
        ethicsApproved.Evidence.Should().Equal(ethics);
        var regulatory = await _scenario.OkAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.RegulatoryApproved, Tick(), tenantWideCta), ct);
        regulatory.Status.Should().Be(EffectivityStatus.RegulatoryApproved);
        var effective = await _scenario.OkAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), ethics, tenantWideCta), ct);
        effective.Evidence.Should().Equal(ethics, tenantWideCta);
    }

    /// <summary>
    /// Validates the declaration rules: an unknown or withdrawn version, a study site of another study, a malformed
    /// scope and a second effectivity for the same version and scope are refused. A version not yet approved may be
    /// declared but cannot become effective.
    /// </summary>
    [Fact(DisplayName = "Effectivity_Declare_RefusesUnknownForeignDuplicateAndUnapproved")]
    public async Task EffectivityDeclareRefusesUnknownForeignDuplicateAndUnapproved()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var otherStudy = await NewStudyInTenantAsync(tenant, ct);
        var foreignSite = await AddStudySiteAsync(tenant, otherStudy, "US", ct);
        var ownSite = await AddStudySiteAsync(tenant, study, "US", ct);
        var draft = await _scenario.InReviewAsync(tenant, study, ct);
        var versionId = draft.ProtocolVersionId;

        (await SendAsync(Declare(tenant, EffectivityId.New(), ProtocolVersionId.New(), EffectivityScope.Study(), Tick()), ct)).Error
            .Should().Be(EffectivityErrors.ProtocolVersionUnknown);
        (await SendAsync(Declare(tenant, EffectivityId.New(), versionId, EffectivityScope.At(foreignSite), Tick()), ct)).Error
            .Should().Be(EffectivityErrors.StudySiteNotInStudy);
        (await SendAsync(Declare(tenant, EffectivityId.New(), versionId, EffectivityScope.At(StudySiteId.New()), Tick()), ct)).Error
            .Should().Be(EffectivityErrors.StudySiteNotInStudy);
        (await SendAsync(Declare(tenant, EffectivityId.New(), versionId, EffectivityScope.InCountry("usa"), Tick()), ct)).Error!.Code
            .Should().Be(EffectivityErrors.ScopeInvalid.Code);

        var atSite = EffectivityId.New();
        await _scenario.OkAsync(Declare(tenant, atSite, versionId, EffectivityScope.At(ownSite), Tick()), ct);
        (await SendAsync(Declare(tenant, atSite, versionId, EffectivityScope.Study(), Tick()), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
        (await SendAsync(Declare(tenant, EffectivityId.New(), versionId, EffectivityScope.At(ownSite), Tick()), ct)).Error
            .Should().Be(EffectivityErrors.AlreadyDeclared);

        var approval = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, study, ct);
        (await SendAsync(ChangeStatus(tenant, atSite, EffectivityStatus.Effective, Tick(), approval), ct)).Error
            .Should().Be(EffectivityErrors.ProtocolVersionNotApproved);

        Tick();
        var withdrawal = await _scenario.OkAsync(_scenario.AttestCommand(tenant, AttestationMeaning.Withdrawn, KernelProtocolScenario.Target(draft)), ct);
        await _scenario.OkAsync(new WithdrawProtocolVersion(tenant, versionId, withdrawal, KernelProtocolScenario.Key(), "Study cancelled"), ct);

        (await SendAsync(Declare(tenant, EffectivityId.New(), versionId, EffectivityScope.Study(), Tick()), ct)).Error
            .Should().Be(EffectivityErrors.ProtocolVersionWithdrawn);
    }

    /// <summary>
    /// Validates the database side: even the superuser cannot rewrite or delete an effectivity's status rows (SQLSTATE
    /// KT001), and the scope check and the one-effectivity-per-scope index hold for rows written outside the kernel.
    /// </summary>
    [Fact(DisplayName = "EffectivityStatusPeriods_AreAppendOnly_ForEveryDatabaseUser")]
    [KernelRule("K-TIME-1")]
    public async Task EffectivityStatusPeriodsAreAppendOnlyForEveryDatabaseUser()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var effectivity = EffectivityId.New();
        await _scenario.OkAsync(Declare(tenant, effectivity, version.ProtocolVersionId, EffectivityScope.Study(), Tick()), ct);
        var approval = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, study, ct);
        await _scenario.OkAsync(ChangeStatus(tenant, effectivity, EffectivityStatus.Effective, Tick(), approval), ct);

        foreach (var statement in new[]
        {
            $"UPDATE kernel.effectivity_status_periods SET status = 'Pending' WHERE effectivity_id = '{effectivity.Value}'",
            $"UPDATE kernel.effectivity_status_periods SET evidence = '{{}}' WHERE effectivity_id = '{effectivity.Value}'",
            $"DELETE FROM kernel.effectivity_status_periods WHERE effectivity_id = '{effectivity.Value}'",
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement)).Which.SqlState.Should().Be(BitemporalSql.AppendOnlyViolationSqlState);
        }

        foreach (var (level, country, expected) in new[]
        {
            ("Study", "'US'", PostgresErrorCodes.CheckViolation),
            ("Country", "NULL", PostgresErrorCodes.CheckViolation),
            ("Study", "NULL", PostgresErrorCodes.UniqueViolation),
        })
        {
            var act = () => ExecuteAsAdminAsync(
                $"""
                INSERT INTO kernel.effectivities (id, tenant_id, study_id, protocol_version_id, scope_level, scope_country, status, status_since, created_at, created_by, reason_for_change, version_index)
                VALUES ('{Guid.CreateVersion7()}', '{tenant.Value}', '{study.Value}', '{version.ProtocolVersionId.Value}', '{level}', {country}, 'Pending', now(), now(), 'sql', 'sql', 1)
                """,
                ct);
            (await act.Should().ThrowAsync<PostgresException>($"{level} {country}")).Which.SqlState.Should().Be(expected);
        }
    }

    /// <summary>
    /// Covering <see cref="LockKind.Frozen"/> and <see cref="LockKind.Locked"/> locks reject
    /// <see cref="DeclareEffectivity"/>, <see cref="ChangeEffectivityStatus"/>, and <see cref="ActivateStudySite"/>
    /// with <see cref="ObservationErrors.Locked"/>.
    /// </summary>
    [Fact(DisplayName = "EffectivityAndSiteActivation_RejectMutationsUnderCoveringFrozenOrLockedLock")]
    public async Task EffectivityAndSiteActivationRejectMutationsUnderCoveringFrozenOrLockedLock()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var boston = await AddStudySiteAsync(tenant, study, "US", ct);
        var berlin = await AddStudySiteAsync(tenant, study, "DE", ct);
        var approval = await RegisterAsync(tenant, ArtifactKind.EthicsApproval, study, ct);

        var usEffectivity = EffectivityId.New();
        await _scenario.OkAsync(Declare(tenant, usEffectivity, version.ProtocolVersionId, EffectivityScope.InCountry("US"), Tick()), ct);
        await _scenario.OkAsync(ChangeStatus(tenant, usEffectivity, EffectivityStatus.Effective, Tick(), approval), ct);

        var deEffectivity = EffectivityId.New();
        await _scenario.OkAsync(Declare(tenant, deEffectivity, version.ProtocolVersionId, EffectivityScope.InCountry("DE"), Tick()), ct);

        var observations = new KernelObservationScenario(fixture);
        foreach (var lockKind in new[] { LockKind.Frozen, LockKind.Locked })
        {
            var studyScope = NodeRef.For(study);
            var applied = await _scenario.OkAsync(
                await observations.LockAsync(tenant, study, studyScope, ct, lockKind),
                ct);

            (await SendAsync(Declare(tenant, EffectivityId.New(), version.ProtocolVersionId, EffectivityScope.At(berlin), Tick()), ct))
                .Error.Should().Be(ObservationErrors.Locked);
            (await SendAsync(ChangeStatus(tenant, deEffectivity, EffectivityStatus.Effective, Tick(), approval), ct))
                .Error.Should().Be(ObservationErrors.Locked);
            (await ActivateAsync(tenant, boston, ct))
                .Error.Should().Be(ObservationErrors.Locked);

            await _scenario.OkAsync(
                await observations.ReleaseAsync(tenant, study, applied.LockId, ct),
                ct);
        }

        // Study-site-level lock also blocks site-scoped DeclareEffectivity and ActivateStudySite on that site.
        var bostonScope = NodeRef.For(boston);
        var siteFrozen = await _scenario.OkAsync(
            await observations.LockAsync(tenant, study, bostonScope, ct, LockKind.Frozen),
            ct);
        (await SendAsync(Declare(tenant, EffectivityId.New(), version.ProtocolVersionId, EffectivityScope.At(boston), Tick()), ct))
            .Error.Should().Be(ObservationErrors.Locked);
        (await ActivateAsync(tenant, boston, ct)).Error.Should().Be(ObservationErrors.Locked);

        await _scenario.OkAsync(
            await observations.ReleaseAsync(tenant, study, siteFrozen.LockId, ct),
            ct);
        (await ActivateAsync(tenant, boston, ct)).IsSuccess.Should().BeTrue();
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private Task<CommandResult<EffectivityState>> SendAsync(ICommand<EffectivityState> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private Task<CommandResult<StudySiteState>> ActivateAsync(TenantId tenant, StudySiteId studySite, CancellationToken ct) =>
        fixture.Sender.SendAsync(new ActivateStudySite(tenant, studySite, KernelProtocolScenario.Key(), Reason), cancellationToken: ct);

    private static DeclareEffectivity Declare(TenantId tenant, EffectivityId id, ProtocolVersionId version, EffectivityScope scope, DateTimeOffset at) =>
        new(tenant, id, version, scope, at, KernelProtocolScenario.Key(), Reason);

    private static ChangeEffectivityStatus ChangeStatus(
        TenantId tenant, EffectivityId id, EffectivityStatus to, DateTimeOffset at, params ArtifactVersionId[] evidence) =>
        new(tenant, id, to, at, evidence, KernelProtocolScenario.Key(), Reason);

    private async Task<ArtifactVersionId> RegisterAsync(TenantId tenant, ArtifactKind kind, StudyId? studyId, CancellationToken ct)
    {
        var bytes = $"{kind}-{Guid.NewGuid():N}";
        var version = ArtifactVersionId.New();
        var content = new ArtifactContent(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), bytes.Length, "application/pdf", $"blob://evidence/{bytes}");
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), version, kind, studyId, $"{kind} letter", content, KernelProtocolScenario.Key(), "Filed approval"),
            ct);
        return version;
    }

    private async Task<StudyId> NewStudyInTenantAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{suffix}", "Second study", "Phase 2", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(study);
        return study.Id;
    }

    private async Task<StudySiteId> AddStudySiteAsync(TenantId tenant, StudyId study, string country, CancellationToken ct)
    {
        await using var db = fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", country, KernelPostgresFixture.ActorId, "Seeded for test");
        var studySite = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(site, studySite);
        return studySite.Id;
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
