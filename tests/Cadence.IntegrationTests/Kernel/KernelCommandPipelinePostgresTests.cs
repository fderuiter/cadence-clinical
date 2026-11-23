using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Standards;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Runs kernel commands through the production pipeline against a real PostgreSQL server (spec §5.1-5.4): the
/// single transaction for state, provenance, events, audit and outbox (K-TX-2), idempotency (K-TX-3), one aggregate
/// per command (K-TX-1), the resolved actor (K-TX-5), audited denials (K-AUTH-2), the per-tenant hash chain and the
/// insert-only tables. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelCommandPipelinePostgresTests(KernelPostgresFixture fixture)
    : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Reason = "Integration test";

    public static TheoryData<string> CommitStages => new(Enum.GetNames<CommitStage>());

    public static TheoryData<string> InsertOnlyTables => new(InsertOnlySql.Tables);

    public void Dispose()
    {
        if (fixture.ConnectionString is null)
        {
            return;
        }

        fixture.UnitOfWork.FaultInjector = null;
        fixture.Authorizer.Policy = null;
        fixture.CurrentActor.Actor = Actor.Person(KernelPostgresFixture.ActorId);
    }

    /// <summary>
    /// Validates that one command writes the aggregate with audit fields from the kernel clock and actor, plus exactly
    /// one provenance activity, one event with its outbox row, one audit link at sequence 1 of a fresh tenant chain,
    /// and the idempotency record, all naming the same activity.
    /// </summary>
    [Fact(DisplayName = "ProvisionTenant_Commits_StateProvenanceEventAuditOutboxAndIdempotency_AsOneActivity")]
    [KernelRule("K-PROV-1")]
    [KernelRule("K-TX-2")]
    public async Task ProvisionTenantCommitsStateProvenanceEventAuditOutboxAndIdempotencyAsOneActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();

        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        result.Value.Should().Be(command.TenantId);
        result.Replayed.Should().BeFalse();

        await using var db = fixture.CreateAdminContext();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == command.TenantId, ct);
        tenant.CreatedBy.Should().Be(KernelPostgresFixture.ActorId);
        tenant.CreatedAt.Should().Be(KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow).UtcDateTime);
        tenant.ReasonForChange.Should().Be(Reason);
        tenant.VersionIndex.Should().Be(1);

        var activity = await db.ProvenanceActivities.AsNoTracking().SingleAsync(a => a.TenantId == command.TenantId, ct);
        activity.Outcome.Should().Be("Committed");
        activity.CommandType.Should().Be(nameof(ProvisionTenant));
        activity.ActorId.Should().Be(KernelPostgresFixture.ActorId);
        activity.AggregateType.Should().Be(nameof(Tenant));
        activity.AggregateId.Should().Be(command.TenantId.Value.ToString("D"));

        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == command.TenantId).ToListAsync(ct);
        events.Should().ContainSingle().Which.Should().Match<DomainEventRecord>(e =>
            e.Type == "TenantProvisioned" && e.SchemaVersion == 1 && e.ActivityId == activity.Id);
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(o => o.EventId == events[0].EventId, ct);
        outbox.DispatchedAt.Should().BeNull();

        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.TenantId == command.TenantId, ct);
        audit.Sequence.Should().Be(1);
        audit.Action.Should().Be("Created");
        audit.PredecessorHash.Should().Be(KernelAuditHash.Genesis);
        audit.ActivityId.Should().Be(activity.Id);
        audit.Changes.Should().Contain(command.Code);

        (await db.IdempotencyRecords.AsNoTracking().CountAsync(r => r.TenantId == command.TenantId, ct)).Should().Be(1);
        (await fixture.AuditChainVerifier.VerifyAsync(command.TenantId, ct)).IsIntact.Should().BeTrue();
    }

    /// <summary>
    /// Validates K-TX-3: resending a command with the same key and payload returns the original result without writing
    /// anything, and the same key with a different payload is an idempotency conflict (409).
    /// </summary>
    [Fact(DisplayName = "Command_SameIdempotencyKey_ReplaysOriginal_AndDifferentPayloadConflicts")]
    [KernelRule("K-TX-3")]
    public async Task CommandSameIdempotencyKeyReplaysOriginalAndDifferentPayloadConflicts()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();

        (await fixture.Sender.SendAsync(command, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var replay = await fixture.Sender.SendAsync(command, cancellationToken: ct);
        var conflict = await fixture.Sender.SendAsync(command with { Name = "Changed" }, cancellationToken: ct);

        replay.IsSuccess.Should().BeTrue();
        replay.Replayed.Should().BeTrue();
        replay.Value.Should().Be(command.TenantId);
        conflict.Error.Should().Be(KernelErrors.IdempotencyConflict);
        (await CountWritesAsync(command.TenantId, ct)).Should().Be(new WriteCounts(1, 1, 1, 1));
    }

    /// <summary>
    /// Validates that two concurrent sends of the same command both succeed with the same result and write once,
    /// even when the aggregate's own unique key is what rejects the second transaction.
    /// </summary>
    [Fact(DisplayName = "Command_SameKeyConcurrently_WritesOnce_AndBothCallersGetTheResult")]
    [KernelRule("K-TX-3")]
    public async Task CommandSameKeyConcurrentlyWritesOnceAndBothCallersGetTheResult()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => fixture.Sender.SendAsync(command, cancellationToken: ct)));

        results.Should().OnlyContain(r => r.IsSuccess && r.Value == command.TenantId);
        (await CountWritesAsync(command.TenantId, ct)).Should().Be(new WriteCounts(1, 1, 1, 1));
    }

    /// <summary>
    /// Validates K-TX-5: with no resolved actor the command is refused (401) before anything is written.
    /// </summary>
    [Fact(DisplayName = "Command_WithoutActor_IsRefused_AndWritesNothing")]
    [KernelRule("K-TX-5")]
    public async Task CommandWithoutActorIsRefusedAndWritesNothing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        fixture.CurrentActor.Actor = null;
        var command = NewTenant();

        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.ActorUnresolved);
        result.Error!.HttpStatus.Should().Be(401);
        (await CountWritesAsync(command.TenantId, ct)).Should().Be(new WriteCounts(0, 0, 0, 0));
    }

    /// <summary>
    /// Validates that a command without a reason for change or idempotency key is rejected (422) before anything is
    /// written.
    /// </summary>
    [Fact(DisplayName = "Command_MissingReasonOrKey_IsRejected")]
    [KernelRule("K-TX-3")]
    public async Task CommandMissingReasonOrKeyIsRejected()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var command = NewTenant();

        (await fixture.Sender.SendAsync(command with { ReasonForChange = " " }, cancellationToken: ct)).Error
            .Should().Be(KernelErrors.ReasonForChangeRequired);
        (await fixture.Sender.SendAsync(command with { IdempotencyKey = "" }, cancellationToken: ct)).Error
            .Should().Be(KernelErrors.IdempotencyKeyRequired);
        (await CountWritesAsync(command.TenantId, ct)).Should().Be(new WriteCounts(0, 0, 0, 0));
    }

    /// <summary>
    /// Validates K-AUTH-2: a denied command changes no state but is itself recorded as a provenance activity, a
    /// <c>DecisionDenied</c> event and a "Denied" audit link naming the reason, and returns 403.
    /// </summary>
    [Fact(DisplayName = "Command_Denied_IsAudited_AndChangesNoState")]
    [KernelRule("K-AUTH-2")]
    public async Task CommandDeniedIsAuditedAndChangesNoState()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        fixture.Authorizer.Policy = (_, _) => AuthorizationDecision.Deny("Missing grant: tenant.provision");
        var command = NewTenant();

        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);

        result.Error!.Code.Should().Be(KernelErrors.AuthorizationDenied.Code);
        result.Error.HttpStatus.Should().Be(403);
        result.Error.Message.Should().Contain("tenant.provision");

        await using var db = fixture.CreateAdminContext();
        (await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct)).Should().BeFalse();
        (await db.ProvenanceActivities.SingleAsync(a => a.TenantId == command.TenantId, ct)).Outcome.Should().Be("Denied");
        (await db.DomainEvents.SingleAsync(e => e.TenantId == command.TenantId, ct)).Type.Should().Be("DecisionDenied");
        var audit = await db.AuditEntries.SingleAsync(a => a.TenantId == command.TenantId, ct);
        audit.Action.Should().Be("Denied");
        audit.Changes.Should().Contain("tenant.provision");
        (await db.IdempotencyRecords.AnyAsync(r => r.TenantId == command.TenantId, ct)).Should().BeFalse(
            "a denied command can be retried once the actor is granted access");
    }

    /// <summary>
    /// Validates K-TX-2: a failure at any point of the commit leaves no state, provenance, event, audit link, outbox
    /// row or idempotency record behind, and the tenant's chain head is not advanced.
    /// </summary>
    [Theory(DisplayName = "Commit_FailingAtAnyStage_PersistsNothing")]
    [KernelRule("K-TX-2")]
    [MemberData(nameof(CommitStages))]
    public async Task CommitFailingAtAnyStagePersistsNothing(string stageName)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var stage = Enum.Parse<CommitStage>(stageName);
        fixture.UnitOfWork.FaultInjector = s =>
        {
            if (s == stage)
            {
                throw new InvalidOperationException($"Injected fault at {stage}.");
            }
        };
        var command = NewTenant();

        var act = () => fixture.Sender.SendAsync(command, cancellationToken: ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"Injected fault at {stage}.");
        (await CountWritesAsync(command.TenantId, ct)).Should().Be(new WriteCounts(0, 0, 0, 0));
        await using var db = fixture.CreateAdminContext();
        (await db.Tenants.AnyAsync(t => t.Id == command.TenantId, ct)).Should().BeFalse();
        (await db.AuditChainHeads.AnyAsync(h => h.TenantId == command.TenantId, ct)).Should().BeFalse();
        (await db.OutboxMessages.AnyAsync(o => o.TenantId == command.TenantId, ct)).Should().BeFalse();
    }

    /// <summary>
    /// Validates that a duplicate tenant code under a new key is reported as <c>K.Aggregate.AlreadyExists</c> (409)
    /// rather than an unhandled database error, and that the rejected command leaves nothing behind.
    /// </summary>
    [Fact(DisplayName = "ProvisionTenant_DuplicateCode_ReturnsAlreadyExists")]
    public async Task ProvisionTenantDuplicateCodeReturnsAlreadyExists()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var first = NewTenant();
        var second = NewTenant() with { Code = first.Code };

        (await fixture.Sender.SendAsync(first, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var result = await fixture.Sender.SendAsync(second, cancellationToken: ct);

        result.Error.Should().Be(KernelErrors.AlreadyExists);
        (await CountWritesAsync(second.TenantId, ct)).Should().Be(new WriteCounts(0, 0, 0, 0));
    }

    /// <summary>
    /// Validates the organization command end to end: an unknown tenant is refused (404), and a registration in a
    /// provisioned tenant extends that tenant's chain to sequence 2.
    /// </summary>
    [Fact(DisplayName = "RegisterOrganization_RequiresTenant_AndExtendsTenantChain")]
    public async Task RegisterOrganizationRequiresTenantAndExtendsTenantChain()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();

        (await fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)).Error
            .Should().Be(IdentityErrors.TenantUnknown);

        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var register = NewOrganization(tenant.TenantId);
        var result = await fixture.Sender.SendAsync(register, cancellationToken: ct);

        result.Value.Should().Be(register.OrganizationId);
        await using var db = fixture.CreateAdminContext();
        var organization = await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == register.OrganizationId, ct);
        organization.Kind.Should().Be(OrganizationKind.Sponsor);
        var chain = await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == tenant.TenantId).OrderBy(a => a.Sequence).ToListAsync(ct);
        chain.Select(a => a.Sequence).Should().Equal(1, 2);
        chain[1].PredecessorHash.Should().Be(chain[0].Hash);
        chain[1].AggregateType.Should().Be(nameof(Organization));
    }

    /// <summary>
    /// Validates the per-tenant chain under contention: many commands for one tenant at once still produce a gap-free,
    /// unforked chain that the verifier accepts.
    /// </summary>
    [Fact(DisplayName = "AuditChain_ConcurrentCommandsForOneTenant_StayLinear")]
    public async Task AuditChainConcurrentCommandsForOneTenantStayLinear()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)));

        results.Should().OnlyContain(r => r.IsSuccess);
        var verification = await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct);
        verification.IsIntact.Should().BeTrue(verification.Problem);
        verification.EntriesChecked.Should().Be(13);
    }

    /// <summary>
    /// Validates tamper evidence: when a privileged user bypasses the insert-only trigger and edits an audit link, the
    /// verifier reports the chain broken at that link.
    /// </summary>
    [Fact(DisplayName = "AuditChainVerifier_DetectsEditedLink")]
    public async Task AuditChainVerifierDetectsEditedLink()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        (await fixture.Sender.SendAsync(NewOrganization(tenant.TenantId), cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var trigger = InsertOnlySql.RowTriggerName("audit_entries");
        await ExecuteAsync(
            $"""
            ALTER TABLE kernel.audit_entries DISABLE TRIGGER {trigger};
            UPDATE kernel.audit_entries SET reason_for_change = 'rewritten' WHERE tenant_id = '{tenant.TenantId.Value}' AND sequence = 1;
            ALTER TABLE kernel.audit_entries ENABLE TRIGGER {trigger};
            """,
            ct);

        var verification = await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct);

        verification.IsIntact.Should().BeFalse();
        verification.BrokenAtSequence.Should().Be(1);
    }

    /// <summary>
    /// Validates spec §5.3 item 2: the audit, event, provenance, idempotency, seal, attestation, terminology, lineage,
    /// artifact version, role, work-queue definition, validation run, observation revision and assertion tables reject UPDATE, DELETE and TRUNCATE in the database itself (SQLSTATE KA001).
    /// </summary>
    [Theory(DisplayName = "InsertOnlyTables_RejectUpdateDeleteAndTruncate")]
    [MemberData(nameof(InsertOnlyTables))]
    public async Task InsertOnlyTablesRejectUpdateDeleteAndTruncate(string table)
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        await fixture.AuditChainSealer.SealAllAsync(ct);
        if (table == "attestations")
        {
            var scenario = new KernelProtocolScenario(fixture);
            var (studyTenant, study) = await scenario.NewStudyAsync(ct);
            await scenario.ApprovedAsync(studyTenant, study, ct);
        }

        if (table == "roles")
        {
            var role = new DefineRole(tenant.TenantId, RoleId.New(), "CRC", "Coordinator", ["observation.record"], $"key-{Guid.NewGuid():N}", Reason);
            (await fixture.Sender.SendAsync(role, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        }

        if (table == "work_queue_definitions")
        {
            var queue = new DefineWorkQueue(
                tenant.TenantId, WorkQueueDefinitionId.New(), "protocol_approvals", "Protocol approvals", "protocol.approve",
                VerbClass.Approve, KernelWorkItemSources.ProtocolVersionsInReview, $"key-{Guid.NewGuid():N}", Reason);
            (await fixture.Sender.SendAsync(queue, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        }

        if (table == "artifact_versions")
        {
            var content = new ArtifactContent(ContentHash.OfBytes([1, 2, 3]), 3, "application/pdf", "blob://evidence/1");
            var artifact = new RegisterArtifact(
                tenant.TenantId, ArtifactId.New(), ArtifactVersionId.New(), ArtifactKind.Other, null, "Evidence", content, $"key-{Guid.NewGuid():N}", Reason);
            (await fixture.Sender.SendAsync(artifact, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        }

        if (table == "observation_revisions")
        {
            await KernelObservationPostgresTests.SeedAsync(fixture, ct);
        }

        if (table == "assertions")
        {
            await KernelAssertionPostgresTests.SeedAsync(fixture, ct);
        }

        if (table == "collection_instance_members")
        {
            await KernelCollectionPostgresTests.SeedAsync(fixture, ct);
        }

        if (table.StartsWith("validation_", StringComparison.Ordinal))
        {
            await ValidationFindingPostgresTests.SeedRunAsync(fixture, ct);
        }

        // The Standards Registry definition tables fill only when a standard version is registered.
        var package = StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package with
        {
            ValueLevelDefinitions =
            [
                new ValueLevelDefinitionData(
                    "VS", "VSORRES", [new WhereCondition("VSTESTCD", ["SYSBP"])], StandardDataType.Num, 8, [], [], null),
            ],
        };
        var register = new RegisterStandardVersion(
            tenant.TenantId, StandardVersionId.New(), package, null, $"key-{Guid.NewGuid():N}", Reason);
        (await fixture.Sender.SendAsync(register, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        // The activity stream fills only when the projector runs (ADR-2485).
        if (table == "activity_entries")
        {
            await new KernelActivityProjector(
                fixture.TenantContexts, new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts)), new KernelBackgroundOptions())
                .ProjectAsync(tenant.TenantId, ct);
        }

        if (table == "biostat_export_audit_logs")
        {
            var scenario = new KernelProtocolScenario(fixture);
            var (studyTenant, study) = await scenario.NewStudyAsync(ct);
            var cut = KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow);
            var artifactId = ArtifactId.New(cut);
            var versionId = ArtifactVersionId.New(cut);
            var exportContent = new ArtifactContent(ContentHash.OfBytes([1, 2, 3]), 3, "text/csv", $"export://studies/{study}/{versionId}");
            var export = new ExportStudySnapshot(
                studyTenant,
                study,
                artifactId,
                versionId,
                "Study snapshot export",
                exportContent,
                "csv",
                "locked_only",
                cut,
                cut,
                false,
                $"key-{Guid.CreateVersion7():N}",
                Reason);
            (await fixture.Sender.SendAsync(export, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        }

        foreach (var statement in new[]
        {
            $"UPDATE kernel.{table} SET tenant_id = tenant_id",
            $"DELETE FROM kernel.{table}",
            // CASCADE, so a table other rows reference reaches its trigger instead of stopping at the foreign key.
            $"TRUNCATE kernel.{table} CASCADE",
        })
        {
            var act = () => ExecuteAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement))
                .Which.SqlState.Should().Be(InsertOnlySql.ViolationSqlState);
        }
    }

    /// <summary>
    /// Validates K-TX-1 at the unit of work: a command that loads one aggregate for change cannot load a second.
    /// </summary>
    [Fact(DisplayName = "UnitOfWork_SecondAggregate_Throws")]
    [KernelRule("K-TX-1")]
    public async Task UnitOfWorkSecondAggregateThrows()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Institution);

        await using var transaction = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id));
        (await transaction.Work.FindAsync<Organization>(graph.Sponsor.Id, ct)).Should().NotBeNull();
        (await transaction.Work.FindAsync<Organization>(graph.Sponsor.Id, ct)).Should().NotBeNull("the same aggregate may be loaded twice");

        var act = () => transaction.Work.FindAsync<Organization>(graph.Institution.Id, ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-TX-1*");
    }

    /// <summary>
    /// Validates the K-TX-1 homogeneous Concept-creation exception end to end: <see cref="DefineConcepts"/> commits N
    /// concepts at version 1 in one transaction with one provenance activity, N <c>ConceptDefined</c> events (each
    /// naming its own concept subject), N linearly chained audit entries under one <c>audit_chain_heads</c> update,
    /// clean Merkle sealing, idempotent replay under one idempotency key, and atomic rollback when any item is invalid.
    /// </summary>
    [Fact(DisplayName = "DefineConcepts_CommitsHomogeneousBatchAtomically_ChainsAuditAndEvents_AndSealsMerkleRoot")]
    [KernelRule("K-TX-1")]
    [KernelRule("K-TX-2")]
    [KernelRule("K-TX-3")]
    public async Task DefineConceptsCommitsHomogeneousBatchAtomicallyChainsAuditAndEventsAndSealsMerkleRoot()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();
        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        var invalidBatch = new DefineConcepts(
            tenant.TenantId,
            [
                new ConceptDefinition(ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Valid 1", "Def 1"),
                new ConceptDefinition(ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "   ", "Blank label refuses whole batch"),
            ],
            $"key-invalid-{Guid.NewGuid():N}",
            Reason);
        var refused = await fixture.Sender.SendAsync(invalidBatch, cancellationToken: ct);
        refused.Error.Should().Be(KnowledgeErrors.ConceptLabelRequired);

        await using (var checkDb = fixture.CreateAdminContext())
        {
            (await checkDb.Concepts.AsNoTracking().CountAsync(c => c.TenantId == tenant.TenantId, ct)).Should().Be(0);
        }

        var definitions = Enumerable.Range(1, 5)
            .Select(i => new ConceptDefinition(
                ConceptId.New(),
                i == 1 ? ConceptOrigin.CdiscBiomedicalConcept : ConceptOrigin.Sponsor,
                i == 1 ? "C25298" : null,
                ConceptKind.BiomedicalConcept,
                $"Batch Concept {i}",
                $"Definition {i}"))
            .ToArray();
        var defineKey = $"key-batch-{Guid.NewGuid():N}";
        var batchCommand = new DefineConcepts(tenant.TenantId, definitions, defineKey, Reason);

        var committed = await fixture.Sender.SendAsync(batchCommand, cancellationToken: ct);
        committed.IsSuccess.Should().BeTrue(committed.Error?.Message);
        committed.Replayed.Should().BeFalse();
        committed.Value.Should().HaveCount(5);
        committed.Value!.Select(r => r.ConceptId).Should().Equal(definitions.Select(d => d.ConceptId));
        committed.Value!.Should().OnlyContain(r => r.Version == 1);

        // Idempotent replay with the same key returns the same ConceptRefs without writing duplicate rows or events.
        var replayed = await fixture.Sender.SendAsync(batchCommand, cancellationToken: ct);
        replayed.IsSuccess.Should().BeTrue();
        replayed.Replayed.Should().BeTrue();
        replayed.Value.Should().Equal(committed.Value);

        await using var db = fixture.CreateAdminContext();
        var concepts = await db.Concepts.AsNoTracking().Where(c => c.TenantId == tenant.TenantId).ToListAsync(ct);
        concepts.Should().HaveCount(5);
        concepts.Should().OnlyContain(c => c.VersionIndex == 1 && c.CreatedBy == KernelPostgresFixture.ActorId && c.ReasonForChange == Reason);

        // 1 provenance activity for ProvisionTenant + 1 provenance activity for DefineConcepts = 2 total.
        var activities = await db.ProvenanceActivities.AsNoTracking().Where(a => a.TenantId == tenant.TenantId).ToListAsync(ct);
        activities.Should().HaveCount(2);

        var conceptEvents = await db.DomainEvents.AsNoTracking()
            .Where(e => e.TenantId == tenant.TenantId && e.Type == "ConceptDefined")
            .OrderBy(e => e.Recorded)
            .ToListAsync(ct);
        conceptEvents.Should().HaveCount(5);

        var eventIds = conceptEvents.Select(e => e.EventId).ToList();
        var outboxEnvelopes = (await db.OutboxMessages.AsNoTracking()
                .Where(o => o.TenantId == tenant.TenantId && eventIds.Contains(o.EventId))
                .Select(o => o.Envelope)
                .ToListAsync(ct))
            .Select(json => JsonSerializer.Deserialize<EventEnvelope>(json, KernelCommandPipelineJson.Options)!)
            .ToList();
        outboxEnvelopes.Should().HaveCount(5);
        outboxEnvelopes.Select(e => e.Subject).Should().BeEquivalentTo(definitions.Select(d => NodeRef.For(d.ConceptId, 1)));

        var lineageNodes = await db.LineageNodes.AsNoTracking()
            .Where(n => n.TenantId == tenant.TenantId && n.Kind == NodeKinds.Concept)
            .ToListAsync(ct);
        lineageNodes.Should().HaveCount(5);
        lineageNodes.Select(n => n.NodeId).Should().BeEquivalentTo(definitions.Select(d => d.ConceptId.Value));
        lineageNodes.Should().OnlyContain(n => n.Version == 1 && !n.Derived);

        var chain = await db.AuditEntries.AsNoTracking()
            .Where(a => a.TenantId == tenant.TenantId)
            .OrderBy(a => a.Sequence)
            .ToListAsync(ct);
        // Sequence 1 = ProvisionTenant, Sequences 2..6 = 5 Concept creations.
        chain.Select(a => a.Sequence).Should().Equal(1, 2, 3, 4, 5, 6);
        chain.Skip(1).Select(a => a.AggregateId).Should().BeEquivalentTo(definitions.Select(d => d.ConceptId.Value.ToString("D")));
        chain.Skip(1).Should().OnlyContain(a => a.AggregateType == nameof(Concept) && a.Action == "Created" && a.AggregateVersion == 1);

        var verification = await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct);
        verification.IsIntact.Should().BeTrue(verification.Problem);
        verification.EntriesChecked.Should().Be(6);

        await fixture.AuditChainSealer.SealAllAsync(ct);
        var postSealVerification = await fixture.AuditChainVerifier.VerifyAsync(tenant.TenantId, ct);
        postSealVerification.IsIntact.Should().BeTrue(postSealVerification.Problem);
        (await db.AuditChainSeals.AsNoTracking().CountAsync(s => s.TenantId == tenant.TenantId, ct)).Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// Validates that the K-TX-1 homogeneous Concept-creation exception strictly rejects mixing <see cref="Concept"/>
    /// creation with another aggregate type and rejects loading or mutating multiple existing <see cref="Concept"/>
    /// aggregates in one transaction.
    /// </summary>
    [Fact(DisplayName = "UnitOfWork_ConceptBatchRejectsMixedAggregatesAndMultiUpdates")]
    [KernelRule("K-TX-1")]
    public async Task UnitOfWorkConceptBatchRejectsMixedAggregatesAndMultiUpdates()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor);

        var c1 = Concept.Define(
            ConceptId.New(), graph.Tenant.Id, ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Concept 1", "Def 1", fixture.Clock.UtcNow).Value!;
        var c2 = Concept.Define(
            ConceptId.New(), graph.Tenant.Id, ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Concept 2", "Def 2", fixture.Clock.UtcNow).Value!;

        // 1. Adding two Concepts is allowed, but then loading or adding an Organization throws K-TX-1.
        await using (var mixedTx = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id)))
        {
            mixedTx.Work.Add(c1);
            mixedTx.Work.Add(c2);
            var mixedAct = () => mixedTx.Work.FindAsync<Organization>(graph.Sponsor.Id, ct);
            (await mixedAct.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-TX-1*");
        }

        // 2. Loading an Organization first and then adding a Concept throws K-TX-1.
        await using (var reverseMixedTx = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id)))
        {
            (await reverseMixedTx.Work.FindAsync<Organization>(graph.Sponsor.Id, ct)).Should().NotBeNull();
            var reverseAct = () => reverseMixedTx.Work.Add(c1);
            reverseAct.Should().Throw<InvalidOperationException>().WithMessage("K-TX-1*");
        }

        // 3. Updating two existing Concepts in one transaction throws K-TX-1 (only creation at version 1 may batch).
        var seedBatch = new DefineConcepts(
            graph.Tenant.Id,
            [
                new ConceptDefinition(c1.Id, ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Concept 1", "Def 1"),
                new ConceptDefinition(c2.Id, ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Concept 2", "Def 2"),
            ],
            $"seed-{Guid.NewGuid():N}",
            Reason);
        (await fixture.Sender.SendAsync(seedBatch, cancellationToken: ct)).IsSuccess.Should().BeTrue();

        await using var multiUpdateTx = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id));
        (await multiUpdateTx.Work.FindAsync<Concept>(c1.Id, ct)).Should().NotBeNull();
        var secondUpdateAct = () => multiUpdateTx.Work.FindAsync<Concept>(c2.Id, ct);
        (await secondUpdateAct.Should().ThrowAsync<InvalidOperationException>()).WithMessage("K-TX-1*");
    }

    /// <summary>
    /// Validates optimistic concurrency through the unit of work: an update advances <c>VersionIndex</c> by one and is
    /// audited with before and after values, and of two commands holding the same version the second is a
    /// concurrency conflict that writes nothing.
    /// </summary>
    [Fact(DisplayName = "UnitOfWork_ConcurrentUpdates_SecondIsConflict")]
    public async Task UnitOfWorkConcurrentUpdatesSecondIsConflict()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var graph = KernelSchemaPostgresTests.NewGraph();
        await fixture.SeedAsync(graph.Tenant, graph.Sponsor, graph.Institution, graph.Site, graph.Study, graph.StudySite);

        await using var first = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id));
        await using var second = fixture.UnitOfWork.Begin(Execution(graph.Tenant.Id));
        var firstCopy = (await first.Work.FindAsync<StudySite>(graph.StudySite.Id, ct))!;
        var secondCopy = (await second.Work.FindAsync<StudySite>(graph.StudySite.Id, ct))!;

        firstCopy.Activate(Reason);
        (await first.CommitAsync("true", ct)).Should().Be(CommitOutcome.Committed);
        secondCopy.Activate(Reason);
        (await second.CommitAsync("true", ct)).Should().Be(CommitOutcome.ConcurrencyConflict);

        await using var db = fixture.CreateAdminContext();
        var stored = await db.StudySites.AsNoTracking().SingleAsync(s => s.Id == graph.StudySite.Id, ct);
        stored.VersionIndex.Should().Be(2, "one change is one version");
        stored.CreatedBy.Should().Be(KernelPostgresFixture.ActorId, "an update never rewrites the creator");
        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.TenantId == graph.Tenant.Id, ct);
        audit.Action.Should().Be("Updated");
        audit.AggregateVersion.Should().Be(2);
        audit.Changes.Should().Contain("before").And.Contain("after").And.Contain("Active");
    }

    /// <summary>
    /// A tenant and an organization each need a code and a name; a blank one is refused before anything is written.
    /// </summary>
    [Fact(DisplayName = "IdentityCommands_RefuseBlankCodesAndNames")]
    public async Task IdentityCommandsRefuseBlankCodesAndNames()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = NewTenant();

        (await fixture.Sender.SendAsync(tenant with { Code = " " }, cancellationToken: ct)).Error.Should().Be(IdentityErrors.CodeRequired);
        (await fixture.Sender.SendAsync(tenant with { Name = "" }, cancellationToken: ct)).Error.Should().Be(IdentityErrors.NameRequired);
        (await CountWritesAsync(tenant.TenantId, ct)).Should().Be(new WriteCounts(0, 0, 0, 0));

        (await fixture.Sender.SendAsync(tenant, cancellationToken: ct)).IsSuccess.Should().BeTrue();
        var organization = NewOrganization(tenant.TenantId);
        (await fixture.Sender.SendAsync(organization with { Code = "", IdempotencyKey = $"{organization.IdempotencyKey}-a" }, cancellationToken: ct))
            .Error.Should().Be(IdentityErrors.CodeRequired);
        (await fixture.Sender.SendAsync(organization with { Name = "  ", IdempotencyKey = $"{organization.IdempotencyKey}-b" }, cancellationToken: ct))
            .Error.Should().Be(IdentityErrors.NameRequired);

        await using var db = fixture.CreateAdminContext();
        (await db.Organizations.AsNoTracking().CountAsync(o => o.TenantId == tenant.TenantId, ct)).Should().Be(0);
    }

    /// <summary>
    /// K-TX-4: a command takes at most one aggregate lock. Asking again for the same aggregate is a no-op; asking for a
    /// second one throws, so two commands can never wait on each other's locks.
    /// </summary>
    [Fact(DisplayName = "Lock_SameAggregateAgain_IsANoOp_ButASecondAggregateThrows")]
    [KernelRule("K-TX-4")]
    public async Task LockSameAggregateAgainIsANoOpButASecondAggregateThrows()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var study = StudyId.New();

        await using var transaction = fixture.UnitOfWork.Begin(Execution(tenant));
        await transaction.Work.LockAsync<Study>(study, ct);
        await transaction.Work.LockAsync<Study>(study, ct);

        await FluentActions.Awaiting(() => transaction.Work.LockAsync<Study>(StudyId.New(), ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("K-TX-4:*");
        await FluentActions.Awaiting(() => transaction.Work.LockAsync<Site>(SiteId.New(), ct))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("K-TX-4:*");
        (await IsLockHeldAsync(tenant, typeof(Study), study, ct)).Should().BeTrue("the refused requests leave the first lock held");
    }

    /// <summary>
    /// A second command asking for a locked aggregate waits until the holder is disposed, whether it committed or not;
    /// the same id in another tenant, or under another aggregate type, is a different lock and does not wait.
    /// </summary>
    [Fact(DisplayName = "Lock_WaitsForTheHolder_UntilDisposed_AndIsScopedToTenantAndType")]
    [KernelRule("K-TX-4")]
    public async Task LockWaitsForTheHolderUntilDisposedAndIsScopedToTenantAndType()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var id = Guid.CreateVersion7();

        var holder = fixture.UnitOfWork.Begin(Execution(tenant));
        await holder.Work.LockAsync<Study>(new StudyId(id), ct);

        await using (var otherTenant = fixture.UnitOfWork.Begin(Execution(TenantId.New())))
        await using (var otherType = fixture.UnitOfWork.Begin(Execution(tenant)))
        {
            await otherTenant.Work.LockAsync<Study>(new StudyId(id), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            await otherType.Work.LockAsync<Site>(new SiteId(id), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }

        var waiter = fixture.UnitOfWork.Begin(Execution(tenant));
        var waiting = waiter.Work.LockAsync<Study>(new StudyId(id), ct);
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        waiting.IsCompleted.Should().BeFalse("the holder has not been disposed");

        await holder.DisposeAsync();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct);
        (await IsLockHeldAsync(tenant, typeof(Study), new StudyId(id), ct)).Should().BeTrue("the waiter now holds it");
        await waiter.DisposeAsync();
        (await IsLockHeldAsync(tenant, typeof(Study), new StudyId(id), ct)).Should().BeFalse("disposing releases the lock");
    }

    /// <summary>The advisory lock key depends on the tenant, the aggregate type and the id, and on nothing else.</summary>
    [Fact(DisplayName = "AdvisoryLockKey_DiffersByTenantTypeAndId")]
    public void AdvisoryLockKeyDiffersByTenantTypeAndId()
    {
        var tenant = TenantId.New();
        var id = Guid.CreateVersion7().ToString("D");
        var key = KernelCommandTransaction.AdvisoryLockKey(tenant, typeof(Study), id);

        KernelCommandTransaction.AdvisoryLockKey(tenant, typeof(Study), id).Should().Be(key);
        new[]
        {
            KernelCommandTransaction.AdvisoryLockKey(TenantId.New(), typeof(Study), id),
            KernelCommandTransaction.AdvisoryLockKey(tenant, typeof(Site), id),
            KernelCommandTransaction.AdvisoryLockKey(tenant, typeof(Study), Guid.CreateVersion7().ToString("D")),
        }.Should().NotContain(key).And.OnlyHaveUniqueItems();
    }

    /// <summary>Asks, on a separate superuser connection, whether the aggregate's advisory lock is taken.</summary>
    private async Task<bool> IsLockHeldAsync(TenantId tenant, Type aggregate, IEntityId id, CancellationToken ct)
    {
        var key = KernelCommandTransaction.AdvisoryLockKey(tenant, aggregate, id.Value.ToString("D"));
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection) { Parameters = { new() { Value = key } } };
        var acquired = (bool)(await command.ExecuteScalarAsync(ct))!;
        if (acquired)
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection) { Parameters = { new() { Value = key } } };
            await unlock.ExecuteScalarAsync(ct);
        }

        return !acquired;
    }

    private static ProvisionTenant NewTenant()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new ProvisionTenant(TenantId.New(), $"T-{suffix}", $"Tenant {suffix}", $"key-{suffix}", Reason);
    }

    private static RegisterOrganization NewOrganization(TenantId tenant)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new RegisterOrganization(
            tenant, OrganizationId.New(), $"ORG-{suffix}", $"Sponsor {suffix}", OrganizationKind.Sponsor, $"key-{suffix}", Reason);
    }

    private CommandExecution Execution(TenantId tenant)
    {
        var command = NewTenant() with { TenantId = tenant };
        return new CommandExecution(
            command,
            nameof(ProvisionTenant),
            Actor.Person(KernelPostgresFixture.ActorId),
            KernelTimestamps.ToStoredPrecision(fixture.Clock.UtcNow),
            Guid.CreateVersion7(),
            new CommandMetadata(Guid.CreateVersion7()),
            new string('0', 64));
    }

    private async Task<WriteCounts> CountWritesAsync(TenantId tenant, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        return new WriteCounts(
            await db.ProvenanceActivities.CountAsync(a => a.TenantId == tenant, ct),
            await db.DomainEvents.CountAsync(e => e.TenantId == tenant, ct),
            await db.AuditEntries.CountAsync(a => a.TenantId == tenant, ct),
            await db.IdempotencyRecords.CountAsync(r => r.TenantId == tenant, ct));
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record WriteCounts(int Provenance, int Events, int AuditLinks, int IdempotencyRecords);
}
