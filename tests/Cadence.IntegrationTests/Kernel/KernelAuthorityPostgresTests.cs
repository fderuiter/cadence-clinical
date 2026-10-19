using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Grants, roles and delegation through the command pipeline with the production authorization (spec §9.3,
/// ADR-2455, #4781) and the qualifications that gate them (K-AUTH-5, ADR-2459), against PostgreSQL. Includes spec §14
/// acceptance scenarios 3 and 12. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelAuthorityPostgresTests : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private static readonly Actor Platform = Actor.ServiceAccount("platform");
    private static readonly Actor Pi = Actor.Person("pi@site.test");
    private static readonly Actor Crc = Actor.Person("crc@site.test");
    private static readonly Actor Admin = Actor.Person("training@sponsor.test");

    private readonly KernelPostgresFixture _fixture;
    private readonly KernelProtocolScenario _scenario;
    private readonly AuthorityEvaluator _evaluator;

    public KernelAuthorityPostgresTests(KernelPostgresFixture fixture)
    {
        _fixture = fixture;
        _scenario = new KernelProtocolScenario(fixture);
        if (fixture.ConnectionString is null)
        {
            _evaluator = null!;
            return;
        }

        _evaluator = new AuthorityEvaluator(fixture.AuthorityStore, new KernelAuthorityOptions(new HashSet<string>([Platform.Id])));
    }

    public void Dispose()
    {
        if (_fixture.ConnectionString is not null)
        {
            _fixture.Authorizer.Inner = null;
            _fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    /// <summary>
    /// Spec §14 scenario 3 (K-AUTH-2, K-AUTH-3, K-AUTH-6): a coordinator acting without a delegation is denied, and the
    /// denial is audited naming the missing grant. A DOA signed afterwards and backdated before the attempt does not
    /// make the attempt valid: evaluated as known then, it is still denied, and the audit keeps the denial. Once the
    /// delegation is known the coordinator is permitted, and the permit is recorded with the delegation as evidence.
    /// Revoking the delegation denies again.
    /// </summary>
    [Fact(DisplayName = "Scenario3_ActOutsideDelegation_DeniedAudited_RetroactiveDoaDoesNotValidate")]
    [GxPRequirement("PRD-SYS-001")]
    [KernelRule("K-AUTH-2")]
    [KernelRule("K-AUTH-3")]
    [KernelRule("K-AUTH-6")]
    public async Task Scenario3ActOutsideDelegationDeniedAuditedRetroactiveDoaDoesNotValidate()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, artifact) = await NewStudyWithArtifactAsync(ct);
        UseProductionAuthorization();

        // The platform bootstraps a role and grants it to the PI for the study, with the right to delegate.
        var role = await As(Platform, new DefineRole(tenant, RoleId.New(), "DOC", "Document owner", ["artifact.add_version", "artifact.register"], Key(), "Set up roles"), ct);
        Tick();
        var piGrant = await As(Platform, new IssueGrant(tenant, GrantId.New(), Pi, null, role.RoleId, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, true, Key(), "PI appointed"), ct);
        Tick();
        var beforeAttempt = _fixture.Clock.UtcNow;

        // The coordinator acts with no delegation.
        Tick();
        var attemptedAt = _fixture.Clock.UtcNow;
        var denied = await SendAs(Crc, AddVersion(tenant, artifact, "v2"), ct);

        denied.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        denied.Error.Explanation!.Conditions.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Code = AuthorityEvaluator.GrantCondition,
            Met = false,
            Evidence = new[] { NodeRef.For(artifact) },
        });

        // The PI signs a DOA afterwards, backdated to before the attempt.
        Tick();
        var delegation = await As(Pi, new DelegateGrant(tenant, GrantId.New(), piGrant.GrantId, Crc, "artifact.add_version", null, AuthorityScope.Study(study), beforeAttempt, null, false, Key(), "DOA log entry"), ct);
        Tick();

        var asKnownThen = await EvaluateAsync(tenant, Crc, "artifact.add_version", NodeRef.For(artifact), attemptedAt, attemptedAt, ct);
        var asKnownNow = await EvaluateAsync(tenant, Crc, "artifact.add_version", NodeRef.For(artifact), attemptedAt, _fixture.Clock.UtcNow, ct);
        asKnownThen.Permitted.Should().BeFalse("a retroactive DOA cannot validate an earlier action (K-AUTH-3)");
        asKnownNow.Permitted.Should().BeTrue("the DOA's valid period reaches back, but only from when it was recorded");

        var permitted = await SendAs(Crc, AddVersion(tenant, artifact, "v2"), ct);
        permitted.IsSuccess.Should().BeTrue(permitted.Error?.Message);

        await using (var db = _fixture.CreateContext(tenant))
        {
            var activities = await db.ProvenanceActivities.AsNoTracking()
                .Where(a => a.CommandType == nameof(AddArtifactVersion))
                .OrderBy(a => a.RecordedAt)
                .Select(a => new { a.Outcome, a.ActorId, a.Authorization })
                .ToListAsync(ct);
            activities.Select(a => a.Outcome).Should().Equal("Denied", "Committed");
            activities.Should().OnlyContain(a => a.ActorId == Crc.Id && a.Authorization != null);
            activities[0].Authorization.Should().Contain("\"permitted\": false").And.Contain(AuthorityEvaluator.GrantCondition);
            activities[1].Authorization.Should().Contain("\"permitted\": true").And.Contain($"grant:{delegation.GrantId}");
            (await db.DomainEvents.AsNoTracking().CountAsync(e => e.Type == "DecisionDenied", ct)).Should().Be(1, "the earlier denial stays on record");
            (await db.DomainEvents.AsNoTracking().CountAsync(e => e.Type == "DelegationSigned", ct)).Should().Be(1);
        }

        // The PI, as delegator, revokes the DOA; the coordinator is denied again.
        Tick();
        var revoked = await As(Pi, new RevokeGrant(tenant, delegation.GrantId, _fixture.Clock.UtcNow, Key(), "Left the site"), ct);
        revoked.ValidTo.Should().NotBeNull();
        Tick();
        (await SendAs(Crc, AddVersion(tenant, artifact, "v3"), ct)).Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
    }

    /// <summary>
    /// Validates the delegation rules in the handler: only the holder of a grant with the grant option, in effect, may
    /// delegate; a delegation cannot exceed the parent's capabilities, scope or period, or go to the delegator itself.
    /// </summary>
    [Fact(DisplayName = "Delegation_IsBoundedByTheParentGrant")]
    public async Task DelegationIsBoundedByTheParentGrant()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _) = await NewStudyWithArtifactAsync(ct);
        var (otherStudy, _) = await SecondStudyAsync(tenant, ct);
        UseProductionAuthorization();
        var from = _fixture.Clock.UtcNow;
        var piGrant = await As(Platform, new IssueGrant(tenant, GrantId.New(), Pi, "artifact.add_version", null, AuthorityScope.Study(study), from, from.AddDays(90), true, Key(), "PI appointed"), ct);
        var noOption = await As(Platform, new IssueGrant(tenant, GrantId.New(), Pi, "artifact.register", null, AuthorityScope.Study(study), from, null, false, Key(), "PI appointed"), ct);
        Tick();

        DelegateGrant Delegation(GrantId parent, Actor grantee, string capability, AuthorityScope scope, DateTimeOffset? to) =>
            new(tenant, GrantId.New(), parent, grantee, capability, null, scope, from, to, false, Key(), "DOA");

        (await SendAs(Pi, Delegation(piGrant.GrantId, Crc, "artifact.register", AuthorityScope.Study(study), from.AddDays(30)), ct)).Error!.Code
            .Should().Be(AuthorityErrors.DelegationExceedsParent.Code);
        (await SendAs(Pi, Delegation(piGrant.GrantId, Crc, "artifact.add_version", AuthorityScope.Study(otherStudy), from.AddDays(30)), ct)).Error!.Code
            .Should().Be(AuthorityErrors.DelegationExceedsParent.Code);
        (await SendAs(Pi, Delegation(piGrant.GrantId, Crc, "artifact.add_version", AuthorityScope.Study(study), null), ct)).Error!.Code
            .Should().Be(AuthorityErrors.DelegationExceedsParent.Code, "an open-ended delegation outlasts a bounded parent");
        (await SendAs(Pi, Delegation(piGrant.GrantId, Pi, "artifact.add_version", AuthorityScope.Study(study), from.AddDays(30)), ct)).Error
            .Should().Be(AuthorityErrors.SelfDelegation);
        (await SendAs(Pi, Delegation(noOption.GrantId, Crc, "artifact.register", AuthorityScope.Study(study), null), ct)).Error!.Code
            .Should().Be(KernelErrors.AuthorizationDenied.Code, "the grant carries no grant option");
        (await SendAs(Crc, Delegation(piGrant.GrantId, Crc, "artifact.add_version", AuthorityScope.Study(study), from.AddDays(30)), ct)).Error!.Code
            .Should().Be(KernelErrors.AuthorizationDenied.Code, "only the holder may delegate");
        (await SendAs(Platform, new IssueGrant(tenant, GrantId.New(), Crc, "artifact.register", null, AuthorityScope.Study(StudyId.New()), from, null, false, Key(), "Unknown study"), ct)).Error
            .Should().Be(AuthorityErrors.ScopeUnknown);
        (await SendAs(Platform, new IssueGrant(tenant, GrantId.New(), Crc, null, RoleId.New(), AuthorityScope.Study(study), from, null, false, Key(), "Unknown role"), ct)).Error
            .Should().Be(AuthorityErrors.RoleUnknown);

        var siteDelegation = await As(Pi, Delegation(piGrant.GrantId, Crc, "artifact.add_version", AuthorityScope.StudyCountry(study, "US"), from.AddDays(30)), ct);
        siteDelegation.ParentGrantId.Should().Be(piGrant.GrantId);
    }

    /// <summary>
    /// Validates the database guards (K-TIME-1, K-AUTH-4 by construction): grant validity rows are append-only
    /// (SQLSTATE KT001), roles cannot change (KA001), a grant must name exactly one capability or role, and a
    /// study-country grant must name its country.
    /// </summary>
    [Fact(DisplayName = "Authority_DatabaseGuards_KeepHistoryAndRoles")]
    [KernelRule("K-TIME-1")]
    public async Task AuthorityDatabaseGuardsKeepHistoryAndRoles()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _) = await NewStudyWithArtifactAsync(ct);
        var role = await As(Platform, new DefineRole(tenant, RoleId.New(), "CRC", "Coordinator", ["observation.record"], Key(), "Roles"), ct);
        var grant = await As(Platform, new IssueGrant(tenant, GrantId.New(), Crc, null, role.RoleId, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, false, Key(), "Grant"), ct);

        foreach (var (statement, sqlState) in new[]
        {
            ($"UPDATE kernel.grant_periods SET valid_period = tstzrange(now() - interval '1 year', NULL) WHERE grant_id = '{grant.GrantId.Value}'", BitemporalSql.AppendOnlyViolationSqlState),
            ($"DELETE FROM kernel.grant_periods WHERE grant_id = '{grant.GrantId.Value}'", BitemporalSql.AppendOnlyViolationSqlState),
            ($"UPDATE kernel.roles SET capabilities = ARRAY['enrollment.unblind'] WHERE id = '{role.RoleId.Value}'", "KA001"),
            ($"UPDATE kernel.grants SET capability = 'observation.record' WHERE id = '{grant.GrantId.Value}'", PostgresErrorCodes.CheckViolation),
            ($"UPDATE kernel.grants SET scope_level = 'StudyCountry', scope_country = NULL WHERE id = '{grant.GrantId.Value}'", PostgresErrorCodes.CheckViolation),
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement)).Which.SqlState.Should().Be(sqlState);
        }
    }

    /// <summary>
    /// Spec §14 scenario 12 (K-AUTH-5, K-AUTH-6): a coordinator holds the grant to sign eligibility, but a requirement
    /// names training on the current protocol version. While the training is current the signature is permitted with
    /// the training record as evidence; once it has expired the signature is denied and the denial names the expired
    /// qualification and the day it expired.
    /// </summary>
    [Fact(DisplayName = "Scenario12_ExpiredProtocolTraining_DeniesEligibilitySignatureNamingIt")]
    [KernelRule("K-AUTH-5")]
    [KernelRule("K-AUTH-6")]
    public async Task Scenario12ExpiredProtocolTrainingDeniesEligibilitySignatureNamingIt()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _) = await NewStudyWithArtifactAsync(ct);
        var (version, _) = await _scenario.ApprovedAsync(tenant, study, ct);
        var trainingRecord = await TrainingRecordAsync(tenant, study, ct);
        UseProductionAuthorization();
        await AppointQualificationAdminAsync(tenant, ct);
        await As(Platform, new IssueGrant(tenant, GrantId.New(), Crc, "eligibility.sign", null, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, false, Key(), "Coordinator appointed"), ct);
        var requirement = await As(Admin, new DefineQualificationRequirement(
            tenant, QualificationRequirementId.New(), "eligibility.sign", "protocol_training", version.ProtocolVersionId, AuthorityScope.Study(study),
            _fixture.Clock.UtcNow, null, Key(), "Protocol training before eligibility sign-off"), ct);
        Tick();

        var trainedAt = _fixture.Clock.UtcNow;
        var expiresAt = trainedAt.AddDays(1);
        var training = await As(Admin, new RecordQualification(
            tenant, QualificationId.New(), Crc, QualificationKind.ProtocolTraining, "protocol_training", version.ProtocolVersionId,
            trainingRecord, trainedAt, expiresAt, Key(), "Training completed"), ct);
        Tick();

        var current = await EvaluateAsync(tenant, Crc, "eligibility.sign", NodeRef.For(study), _fixture.Clock.UtcNow, _fixture.Clock.UtcNow, ct);
        current.Permitted.Should().BeTrue(current.Reason);
        current.Conditions.Should().ContainSingle(c => c.Code == AuthorityEvaluator.QualificationCondition)
            .Which.Evidence.Should().Equal(NodeRef.For(requirement.RequirementId), NodeRef.For(training.QualificationId), NodeRef.For(trainingRecord));

        _fixture.Clock.Advance(TimeSpan.FromDays(2));
        var expired = await EvaluateAsync(tenant, Crc, "eligibility.sign", NodeRef.For(study), _fixture.Clock.UtcNow, _fixture.Clock.UtcNow, ct);

        expired.Permitted.Should().BeFalse();
        expired.Reason.Should().Be(
            $"{Crc.Id} is not qualified to eligibility.sign: Renew protocol_training for protocol version {version.ProtocolVersionId}; it expired on {expiresAt:yyyy-MM-dd}.");
        expired.Conditions.Should().ContainSingle(c => !c.Met)
            .Which.Evidence.Should().Equal(NodeRef.For(requirement.RequirementId), NodeRef.For(training.QualificationId));
    }

    /// <summary>
    /// A requirement gates a kernel command through the pipeline: the coordinator's add-version is denied and audited
    /// naming the qualification to obtain, and permitted with the qualification as evidence once it is recorded.
    /// Retiring the requirement lifts the gate for later decisions.
    /// </summary>
    [Fact(DisplayName = "QualificationRequirement_GatesCommandsThroughThePipeline")]
    public async Task QualificationRequirementGatesCommandsThroughThePipeline()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, artifact) = await NewStudyWithArtifactAsync(ct);
        var certificate = await TrainingRecordAsync(tenant, study, ct);
        UseProductionAuthorization();
        await AppointQualificationAdminAsync(tenant, ct);
        await As(Platform, new IssueGrant(tenant, GrantId.New(), Crc, "artifact.add_version", null, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, false, Key(), "Coordinator appointed"), ct);
        var requirement = await As(Admin, new DefineQualificationRequirement(
            tenant, QualificationRequirementId.New(), "artifact.add_version", "gcp", null, AuthorityScope.Study(study), _fixture.Clock.UtcNow, null, Key(), "GCP first"), ct);
        Tick();

        var denied = await SendAs(Crc, AddVersion(tenant, artifact, "v2"), ct);
        denied.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        denied.Error.Explanation!.Conditions.Should().ContainSingle(c => !c.Met).Which.Should().BeEquivalentTo(new
        {
            Code = AuthorityEvaluator.QualificationCondition,
            Description = "Obtain gcp",
            Evidence = new[] { NodeRef.For(requirement.RequirementId) },
        });

        Tick();
        var gcp = await As(Admin, new RecordQualification(
            tenant, QualificationId.New(), Crc, QualificationKind.Certificate, "gcp", null, certificate, _fixture.Clock.UtcNow, null, Key(), "GCP certificate filed"), ct);
        Tick();
        (await SendAs(Crc, AddVersion(tenant, artifact, "v2"), ct)).IsSuccess.Should().BeTrue();

        await using (var db = _fixture.CreateContext(tenant))
        {
            var decisions = await db.ProvenanceActivities.AsNoTracking()
                .Where(a => a.CommandType == nameof(AddArtifactVersion))
                .OrderBy(a => a.RecordedAt)
                .Select(a => a.Authorization!)
                .ToListAsync(ct);
            decisions.Should().HaveCount(2);
            decisions[0].Should().Contain("\"permitted\": false").And.Contain("Obtain gcp");
            decisions[1].Should().Contain("\"permitted\": true").And.Contain($"qualification:{gcp.QualificationId}");
            (await db.DomainEvents.AsNoTracking().CountAsync(e => e.Type == "QualificationRecorded", ct)).Should().Be(1);
            (await db.DomainEvents.AsNoTracking().CountAsync(e => e.Type == "QualificationRequirementDefined", ct)).Should().Be(1);
        }

        // Revoking the certificate denies again; retiring the requirement lifts the gate.
        Tick();
        (await As(Admin, new RevokeQualification(tenant, gcp.QualificationId, _fixture.Clock.UtcNow, Key(), "Filed in error"), ct)).ValidTo.Should().NotBeNull();
        Tick();
        (await SendAs(Crc, AddVersion(tenant, artifact, "v3"), ct)).Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        (await As(Admin, new RetireQualificationRequirement(tenant, requirement.RequirementId, _fixture.Clock.UtcNow, Key(), "No longer required"), ct)).ValidTo.Should().NotBeNull();
        Tick();
        (await SendAs(Crc, AddVersion(tenant, artifact, "v3"), ct)).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Validates the qualification handlers and database guards: evidence must be a training record or credential,
    /// a protocol version and a requirement's scope must exist, validity rows are append-only (KT001), and only
    /// protocol training names a protocol version.
    /// </summary>
    [Fact(DisplayName = "Qualification_HandlersAndDatabaseGuards_RefuseBadRecords")]
    public async Task QualificationHandlersAndDatabaseGuardsRefuseBadRecords()
    {
        _fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study, _) = await NewStudyWithArtifactAsync(ct);
        var ethicsApproval = ArtifactVersionId.New();
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethicsApproval, ArtifactKind.EthicsApproval, study, "IRB approval", Content("irb"), Key(), "Filed"), ct);
        var record = await TrainingRecordAsync(tenant, study, ct);
        var from = _fixture.Clock.UtcNow;

        RecordQualification Qualification(ArtifactVersionId evidence, QualificationKind kind = QualificationKind.Training, ProtocolVersionId? version = null) =>
            new(tenant, QualificationId.New(), Crc, kind, "gcp", version, evidence, from, null, Key(), "Filed");

        (await _fixture.Sender.SendAsync(Qualification(ethicsApproval), cancellationToken: ct)).Error.Should().Be(QualificationErrors.EvidenceInvalid);
        (await _fixture.Sender.SendAsync(Qualification(ArtifactVersionId.New()), cancellationToken: ct)).Error.Should().Be(QualificationErrors.EvidenceInvalid);
        (await _fixture.Sender.SendAsync(Qualification(record, QualificationKind.ProtocolTraining, ProtocolVersionId.New()), cancellationToken: ct)).Error
            .Should().Be(QualificationErrors.ProtocolVersionUnknown);
        (await _fixture.Sender.SendAsync(new DefineQualificationRequirement(
            tenant, QualificationRequirementId.New(), "eligibility.sign", "gcp", null, AuthorityScope.Study(StudyId.New()), from, null, Key(), "Unknown study"), cancellationToken: ct)).Error
            .Should().Be(AuthorityErrors.ScopeUnknown);
        var gcp = await _scenario.OkAsync(Qualification(record), ct);

        foreach (var (statement, sqlState) in new[]
        {
            ($"UPDATE kernel.qualification_periods SET valid_period = tstzrange(now() - interval '1 year', NULL) WHERE qualification_id = '{gcp.QualificationId.Value}'", BitemporalSql.AppendOnlyViolationSqlState),
            ($"DELETE FROM kernel.qualification_periods WHERE qualification_id = '{gcp.QualificationId.Value}'", BitemporalSql.AppendOnlyViolationSqlState),
            ($"UPDATE kernel.qualifications SET kind = 'ProtocolTraining' WHERE id = '{gcp.QualificationId.Value}'", PostgresErrorCodes.CheckViolation),
            ($"UPDATE kernel.qualifications SET code = 'GCP' WHERE id = '{gcp.QualificationId.Value}'", PostgresErrorCodes.CheckViolation),
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement)).Which.SqlState.Should().Be(sqlState);
        }
    }

    private async Task<ArtifactVersionId> TrainingRecordAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var version = ArtifactVersionId.New();
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), version, ArtifactKind.TrainingRecord, study, "Training record", Content($"training-{version}"), Key(), "Filed"), ct);
        return version;
    }

    /// <summary>The platform grants <see cref="Admin"/> the tenant-wide qualification capabilities.</summary>
    private async Task AppointQualificationAdminAsync(TenantId tenant, CancellationToken ct)
    {
        var role = await As(Platform, new DefineRole(
            tenant, RoleId.New(), "QUAL_ADMIN", "Training administrator",
            ["qualification.record", "qualification.revoke", "qualification_requirement.define", "qualification_requirement.retire"], Key(), "Set up roles"), ct);
        await As(Platform, new IssueGrant(tenant, GrantId.New(), Admin, null, role.RoleId, AuthorityScope.Tenant(), _fixture.Clock.UtcNow, null, false, Key(), "Administrator appointed"), ct);
        Tick();
    }

    private void UseProductionAuthorization() =>
        _fixture.Authorizer.Inner = new KernelAuthorization(
            _evaluator, _fixture.CurrentActor, KernelCommandCatalog.Kernel, _fixture.Clock, NullLogger<KernelAuthorization>.Instance);

    private async Task<(TenantId Tenant, StudyId Study, ArtifactId Artifact)> NewStudyWithArtifactAsync(CancellationToken ct)
    {
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var artifact = ArtifactId.New();
        await _scenario.OkAsync(
            new RegisterArtifact(tenant, artifact, ArtifactVersionId.New(), ArtifactKind.EthicsApproval, study, "IRB approval", Content("v1"), Key(), "Filed"), ct);
        return (tenant, study, artifact);
    }

    private async Task<(StudyId Study, OrganizationId Sponsor)> SecondStudyAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = _fixture.CreateContext(tenant);
        var sponsor = await db.Studies.AsNoTracking().Select(s => s.SponsorId).FirstAsync(ct);
        var study = new Cadence.Kernel.Domain.Entities.Study(StudyId.New(), tenant, sponsor, $"STUDY-{Guid.NewGuid():N}"[..16], "Second", "Phase 2", KernelPostgresFixture.ActorId, "Seeded for test");
        await _fixture.SeedAsync(study);
        return (study.Id, sponsor);
    }

    private async Task<TResult> As<TResult>(Actor actor, ICommand<TResult> command, CancellationToken ct)
    {
        var result = await SendAs(actor, command, ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private async Task<CommandResult<TResult>> SendAs<TResult>(Actor actor, ICommand<TResult> command, CancellationToken ct)
    {
        _fixture.CurrentActor.Actor = actor;
        try
        {
            return await _fixture.Sender.SendAsync(command, cancellationToken: ct);
        }
        finally
        {
            _fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
        }
    }

    private ValueTask<AuthorizationDecision> EvaluateAsync(
        TenantId tenant, Actor actor, string capability, NodeRef resource, DateTimeOffset at, DateTimeOffset knownAt, CancellationToken ct) =>
        _evaluator.EvaluateAsync(new AuthorizationRequest(actor, tenant, new Capability(capability, VerbClass.Execute), resource, at, knownAt), ct);

    private static AddArtifactVersion AddVersion(TenantId tenant, ArtifactId artifact, string content) =>
        new(tenant, artifact, ArtifactVersionId.New(), Content(content), Key(), "Amended approval");

    private static ArtifactContent Content(string bytes) =>
        new(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), Encoding.UTF8.GetByteCount(bytes), "application/pdf", $"blob://evidence/{bytes}");

    private static string Key() => KernelProtocolScenario.Key();

    private void Tick() => _fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
