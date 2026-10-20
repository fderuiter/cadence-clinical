using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Standards;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Protocol;
using Cadence.TestSupport.Kernel.Rules;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Mapping specifications on PostgreSQL (spec §6.3, #4810, ADR-2463): drafting resolves a specification's standard
/// version, variables, CT versions, codelists and Concepts and links them as its lineage inputs; a study's pins must work
/// together; and K-STD-1 refuses to submit a protocol version whose collected Concepts have no mapping in the study's
/// declared standards unless the gap is an accepted finding. Skipped unless <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MappingSpecificationPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Mapping test";
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// A mapping specification version is pinned to its standard version, CT version and Concept versions: each is a
    /// DefinedBy input of the drafted version, written in the draft's transaction (K-LIN-2).
    /// </summary>
    [Fact(DisplayName = "Draft_ResolvesItsReferences_AndLinksThemAsDefinedBy")]
    [KernelRule("K-LIN-2")]
    public async Task DraftResolvesItsReferencesAndLinksThemAsDefinedBy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);

        var draft = await DraftAsync(world, "sdtm-vitals", Vitals(world), ct);

        draft.Error.Should().BeNull();
        await using var db = fixture.CreateContext(world.Tenant);
        var edges = await db.Set<LineageEdgeRecord>().AsNoTracking()
            .Where(e => e.ToId == draft.Value!.Id.Value && e.Kind == LineageEdgeKind.DefinedBy).ToListAsync(ct);
        edges.Select(e => (e.FromKind, e.FromId, e.FromVersion)).Should().BeEquivalentTo(new[]
        {
            (NodeKinds.StandardVersion, world.Sdtmig.Value, world.SdtmigVersionIndex),
            (NodeKinds.CodeSystemVersion, world.SdtmCt.Value, 1L),
            (NodeKinds.Concept, world.Systolic.ConceptId.Value, (long)world.Systolic.Version),
            (NodeKinds.Concept, world.Position.ConceptId.Value, (long)world.Position.Version),
        });
    }

    /// <summary>
    /// Drafting refuses a specification whose references do not resolve: more than one standard version, a variable the
    /// standard does not define, CT the tenant has not imported, a codelist the CT does not hold, a Concept version that
    /// does not exist, and an override of a key that is not a mapping specification.
    /// </summary>
    [Fact(DisplayName = "Draft_RefusesReferencesThatDoNotResolve")]
    public async Task DraftRefusesReferencesThatDoNotResolve()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var sysbp = SampleMappingSpecifications.SystolicBloodPressure(world.Systolic);
        var cases = new (MappingSpecification Specification, IReadOnlyList<StandardVersionId> Standards, string Expected)[]
        {
            (Vitals(world), [], "exactly one standard version"),
            (Spec(sysbp with { Dataset = "XX" }), [world.Sdtmig], "has no dataset XX"),
            (Spec(sysbp with { Result = sysbp.Result with { StandardCharacter = "VSSTRESX" } }), [world.Sdtmig], "has no variable VS.VSSTRESX"),
            (Spec(sysbp) with { ControlledTerminology = [new CodeSystemVersionRef("sdtmct", "2099-01-01")] }, [world.Sdtmig], "sdtmct-2099-01-01 has not been imported"),
            (Spec(SampleMappingSpecifications.Position(world.Position) with { Codelist = "C99999" }), [world.Sdtmig], "Codelist C99999"),
            (Spec(SampleMappingSpecifications.SystolicBloodPressure(new ConceptRef(world.Systolic.ConceptId, 9))), [world.Sdtmig], "does not exist"),
            (Spec(sysbp) with { Overrides = "no-such-key" }, [world.Sdtmig], "\"no-such-key\" is not a mapping specification key"),
        };

        foreach (var (specification, standards, expected) in cases)
        {
            var result = await SendAsync(new DraftLibraryItem(
                world.Tenant, LibraryItemId.New(), LibraryItemKind.MappingSpecification, "bad-" + Guid.NewGuid().ToString("N")[..8], "Bad",
                MappingSpecificationJson.ToJson(specification), standards, Key(), Reason), ct);
            result.Error.Should().NotBeNull(expected);
            result.Error!.Code.Should().Be(StandardsErrors.LibraryItemInvalid.Code, expected);
            result.Error.Message.Should().Contain(expected);
        }

        (await SendAsync(new DraftLibraryItem(
            world.Tenant, LibraryItemId.New(), LibraryItemKind.MappingSpecification, "no-title", "Bad",
            JsonDocument.Parse("""{"mappings":[]}""").RootElement, [world.Sdtmig], Key(), Reason), ct))
            .Error!.Message.Should().Contain("$.title: required");
    }

    /// <summary>
    /// Validates K-STD-1: a study that declared SDTMIG but pinned no mapping for the Concept its CollectionSpec items
    /// record cannot submit its protocol version; once the gap is recorded as a Design finding and accepted with a
    /// Responsibility attestation, it can.
    /// </summary>
    [Fact(DisplayName = "Submit_UnmappedConcept_IsRefused_UntilTheGapIsAccepted")]
    [KernelRule("K-STD-1")]
    public async Task SubmitUnmappedConceptIsRefusedUntilTheGapIsAccepted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var pack = await ReleasedRulePackAsync(world.Tenant, ct);
        await DeclareAsync(world, [pack], ct);
        var version = await DraftProtocolAsync(world, ct);

        var refused = await _scenario.SubmitAsync(world.Tenant, version, ct);

        refused.Error!.Code.Should().Be(StandardsErrors.MappingMissing.Code);
        refused.Error.Message.Should().Contain("cs.vitals.item.sbp").And.Contain(world.Systolic.ToString());
        await using (var db = fixture.CreateContext(world.Tenant))
        {
            (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == version, ct)).Status.Should().Be(ProtocolVersionStatus.Draft);
        }

        var run = await SendAsync(new RecordValidationRun(
            world.Tenant, ValidationRunId.New(), world.Study, ValidationStage.Design, [pack], new DateOnly(2026, 9, 30), "SDTMIG", "3.4", null, [],
            [MappingGap.Dataset], fixture.Clock.UtcNow.AddMinutes(-1), fixture.Clock.UtcNow,
            [new ValidationDetection("sample-vitals-rules", "R4", 1, ValidationSeverity.Error, MappingGap.Target(world.Systolic.ConceptId),
                "Systolic blood pressure has no SDTM mapping.", "A mapping in the declared standards", [])],
            Key(), Reason), ct);
        run.Error.Should().BeNull();
        var finding = (await fixture.Findings.ListAsync(world.Tenant, new FindingQuery(world.Study) { Dataset = MappingGap.Dataset }, ct)).Findings.Single();
        const string Text = "Collected for eligibility only; not tabulated.";
        var attestation = await SignAsync(world.Tenant, new AttestationTarget(
            NodeKinds.ValidationFinding, finding.Id.Value, FindingDecisionContent.Hash(finding.Id, run.Value!.RunId, FindingStatus.Accepted, Text)), AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(world.Tenant, finding.Id, Text, attestation, Key(), Reason), ct)).Error.Should().BeNull();

        var submitted = await _scenario.SubmitAsync(world.Tenant, version, ct);

        submitted.Error.Should().BeNull();
        submitted.Value!.Status.Should().Be(ProtocolVersionStatus.InReview);
    }

    /// <summary>
    /// Validates K-STD-1 and K-LIN-2: a protocol version whose collected Concept is mapped by the study's pinned
    /// specification is submitted, and the specification version it relies on is a MappedBy input of the protocol
    /// version. A study that declared no standards has nothing to map into and submits as before.
    /// </summary>
    [Fact(DisplayName = "Submit_MappedDesign_GoesIntoReview_AndLinksItsMappingAsMappedBy")]
    [KernelRule("K-STD-1")]
    [KernelRule("K-LIN-2")]
    public async Task SubmitMappedDesignGoesIntoReviewAndLinksItsMappingAsMappedBy()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var mapping = await ApprovedAsync(world, "sdtm-vitals", Vitals(world), ct);
        await DeclareAsync(world, [mapping.Id], ct);
        var version = await DraftProtocolAsync(world, ct);

        var submitted = await _scenario.SubmitAsync(world.Tenant, version, ct);

        submitted.Error.Should().BeNull();
        await using var db = fixture.CreateContext(world.Tenant);
        var mappingIndex = (await fixture.Standards.FindLibraryItemAsync(world.Tenant, mapping.Id, ct))!.VersionIndex;
        (await db.Set<LineageEdgeRecord>().AsNoTracking().Where(e => e.ToId == version.Value && e.Kind == LineageEdgeKind.MappedBy).ToListAsync(ct))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                FromKind = NodeKinds.LibraryItem,
                FromId = mapping.Id.Value,
                FromVersion = mappingIndex,
                ToKind = NodeKinds.ProtocolVersion,
            });

        var (tenantWithoutStandards, study) = await _scenario.NewStudyAsync(world.Tenant, ct);
        tenantWithoutStandards.Should().Be(world.Tenant);
        var unmapped = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(world.Tenant, study, unmapped, ct, Design(world))).Error.Should().BeNull();
        (await _scenario.SubmitAsync(world.Tenant, unmapped, ct)).Error.Should().BeNull();
    }

    /// <summary>
    /// A study-level override replaces the base mapping for the Concepts it maps, so the protocol version relies on the
    /// override alone; a study cannot pin an override without its base, nor a specification for a standard version it
    /// does not declare.
    /// </summary>
    [Fact(DisplayName = "Declare_OverridesNeedTheirBase_AndMappingsNeedADeclaredStandard")]
    public async Task DeclareOverridesNeedTheirBaseAndMappingsNeedADeclaredStandard()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var baseSpec = await ApprovedAsync(world, "sdtm-vitals", Vitals(world), ct);
        var overrideSpec = await ApprovedAsync(
            world, "study-vitals", Spec(SampleMappingSpecifications.SystolicBloodPressure(world.Systolic) with { Decimals = 1 }) with { Overrides = "sdtm-vitals" }, ct);

        var withoutBase = await SendAsync(Declare(world, [overrideSpec.Id]), ct);
        var withoutStandard = await SendAsync(new DeclareStudyStandards(
            world.Tenant, world.Study, StudyStandardsProfileId.New(), [world.Adamig], [SampleMappingSpecifications.SdtmCt], [baseSpec.Id], Key(), Reason), ct);
        var both = await SendAsync(Declare(world, [baseSpec.Id, overrideSpec.Id]), ct);

        withoutBase.Error!.Code.Should().Be(StandardsErrors.DeclarationInvalid.Code);
        withoutBase.Error.Message.Should().Contain("overrides sdtm-vitals, which the study does not pin");
        withoutStandard.Error!.Message.Should().Contain("which the study does not declare");
        both.Error.Should().BeNull();

        var version = await DraftProtocolAsync(world, ct);
        (await _scenario.SubmitAsync(world.Tenant, version, ct)).Error.Should().BeNull();
        await using var db = fixture.CreateContext(world.Tenant);
        (await db.Set<LineageEdgeRecord>().AsNoTracking().Where(e => e.ToId == version.Value && e.Kind == LineageEdgeKind.MappedBy).Select(e => e.FromId).ToListAsync(ct))
            .Should().Equal([overrideSpec.Id.Value], "the override's mapping replaced the base's for the only collected concept");
    }

    /// <summary>A tenant with SDTM CT imported, two Concepts, approved SDTMIG 3.4 and ADaMIG 1.3, and a study.</summary>
    private async Task<World> WorldAsync(CancellationToken ct)
    {
        var seed = await ConceptRegistryPostgresTests.SeedAsync(fixture, ct);
        var tenant = seed.Tenant;
        var systolic = (await SendAsync(new DefineConcept(
            tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic blood pressure",
            "The maximum arterial pressure during contraction of the left ventricle.", Key(), Reason), ct)).Value;
        var sdtmig = await ApprovedStandardAsync(tenant, "SDTMIG", ct);
        var adamig = await ApprovedStandardAsync(tenant, "ADaMIG", ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        await using var db = fixture.CreateContext(tenant);
        var sdtmigIndex = await db.Set<StandardVersion>().AsNoTracking().Where(v => v.Id == sdtmig).Select(v => v.VersionIndex).SingleAsync(ct);
        return new World(tenant, study, seed.CodeSystemVersion, systolic, seed.Concept, sdtmig, sdtmigIndex, adamig);
    }

    private async Task<StandardVersionId> ApprovedStandardAsync(TenantId tenant, string code, CancellationToken ct)
    {
        var id = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == code).Package, null, Key(), Reason), ct)).Value;
        var hash = (await fixture.Standards.FindStandardVersionAsync(tenant, id, ct))!.ContentHash;
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, id.Value, hash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, id, approval, Key(), Reason), ct)).Error.Should().BeNull();
        return id;
    }

    private Task<CommandResult<LibraryItemVersion>> DraftAsync(World world, string key, MappingSpecification specification, CancellationToken ct) =>
        SendAsync(new DraftLibraryItem(
            world.Tenant, LibraryItemId.New(), LibraryItemKind.MappingSpecification, key, specification.Title,
            MappingSpecificationJson.ToJson(specification), [world.Sdtmig], Key(), Reason), ct);

    private async Task<LibraryItemVersion> ApprovedAsync(World world, string key, MappingSpecification specification, CancellationToken ct)
    {
        var draft = await DraftAsync(world, key, specification, ct);
        draft.Error.Should().BeNull();
        var approval = await SignAsync(world.Tenant, new AttestationTarget(NodeKinds.LibraryItem, draft.Value!.Id.Value, draft.Value.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(world.Tenant, draft.Value.Id, approval, Key(), Reason), ct)).Error.Should().BeNull();
        return draft.Value;
    }

    private async Task<LibraryItemId> ReleasedRulePackAsync(TenantId tenant, CancellationToken ct)
    {
        var draft = await SendAsync(new DraftLibraryItem(
            tenant, LibraryItemId.New(), LibraryItemKind.RulePack, "sample-vitals-rules", "Sample vitals rules", RulePackJson.ToJson(SampleRulePacks.Sample()), [], Key(), Reason), ct);
        draft.Error.Should().BeNull();
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, draft.Value!.Id.Value, draft.Value.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, draft.Value.Id, approval, Key(), Reason), ct)).Error.Should().BeNull();
        return draft.Value.Id;
    }

    private async Task DeclareAsync(World world, IReadOnlyList<LibraryItemId> pins, CancellationToken ct) =>
        (await SendAsync(Declare(world, pins), ct)).Error.Should().BeNull();

    private static DeclareStudyStandards Declare(World world, IReadOnlyList<LibraryItemId> pins) =>
        new(world.Tenant, world.Study, StudyStandardsProfileId.New(), [world.Sdtmig], [SampleMappingSpecifications.SdtmCt], pins, Key(), Reason);

    private async Task<ProtocolVersionId> DraftProtocolAsync(World world, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(world.Tenant, world.Study, id, ct, Design(world))).Error.Should().BeNull();
        return id;
    }

    /// <summary>A design whose CollectionSpec items record systolic blood pressure.</summary>
    private static string Design(World world) => ProtocolDesignDocument.Write(ProtocolDesignSamples.Complete(world.Systolic, world.SdtmCt));

    private static MappingSpecification Vitals(World world) => SampleMappingSpecifications.Specification(
        SampleMappingSpecifications.SystolicBloodPressure(world.Systolic), SampleMappingSpecifications.Position(world.Position));

    private static MappingSpecification Spec(params VariableMapping[] mappings) => SampleMappingSpecifications.Specification(mappings);

    private async Task<AttestationId> SignAsync(TenantId tenant, AttestationTarget target, AttestationMeaning meaning, CancellationToken ct)
    {
        var result = await SendAsync(
            new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}"), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private Task<CommandResult<T>> SendAsync<T>(ICommand<T> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private static string Key() => $"key-{Guid.NewGuid():N}";

    private sealed record World(
        TenantId Tenant,
        StudyId Study,
        CodeSystemVersionId SdtmCt,
        ConceptRef Systolic,
        ConceptRef Position,
        StandardVersionId Sdtmig,
        long SdtmigVersionIndex,
        StandardVersionId Adamig);
}
