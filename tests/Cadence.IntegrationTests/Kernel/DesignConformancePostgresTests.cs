using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Application.Knowledge.Terminology;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Persistence.Lineage;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Standards.Conformance;
using Cadence.Kernel.Standards.Mapping;
using Cadence.Kernel.Trust;
using Cadence.Kernel.Validation;
using Cadence.Rules.Design;
using Cadence.Rules.Packs;
using Cadence.Standards;
using Cadence.Testing;
using Cadence.TestSupport.Kernel.Knowledge;
using Cadence.TestSupport.Kernel.Standards;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Design-time conformance on PostgreSQL (K-STD-2, #4813, ADR-2464): the shipped design-stage rule pack runs over a
/// protocol draft's design facts, the run names the draft as it is stored, and submitting the draft for review needs such
/// a run and no open design-stage Error finding. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesignConformancePostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private const string Reason = "Design conformance test";
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates spec §14 test 8: a CollectionSpec item whose unit has no conformant SDTM representation raises a
    /// design-stage finding on the item, and the draft cannot be submitted for review until the finding is accepted with
    /// a Responsibility attestation (K-STD-2, K-FIND-2).
    /// </summary>
    [Fact(DisplayName = "Submit_UnitWithNoConformantRepresentation_IsBlocked_UntilTheFindingIsAccepted")]
    [GxPRequirement("PRD-CDR-002")]
    public async Task SubmitUnitWithNoConformantRepresentationIsBlockedUntilTheFindingIsAccepted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var version = await DraftAsync(world, SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt, unit: "cm[H2O]"), ct);

        var run = await RunAsync(world, version, ct);
        var refused = await _scenario.SubmitAsync(world.Tenant, version, ct);

        run.Detected.Should().Be(1);
        var finding = (await fixture.Findings.ListAsync(world.Tenant, new FindingQuery(world.Study) { Stage = ValidationStage.Design }, ct)).Findings.Should().ContainSingle().Subject;
        finding.RuleId.Should().Be("DC-002");
        finding.PackKey.Should().Be(DesignConformancePack.Key);
        finding.Severity.Should().Be(ValidationSeverity.Error);
        finding.Target.Should().Be(new FindingTarget(FindingTargetKind.DesignElement, DesignConformance.ItemDataset, NodeRef.For(ProtocolElementId.For(world.Study, "item.sbp")).ToString()));
        refused.Error!.Code.Should().Be(StandardsErrors.DesignNotConformant.Code);
        refused.Error.Message.Should().Contain("DC-002").And.Contain(finding.Target.Key);
        await using (var db = fixture.CreateContext(world.Tenant))
        {
            (await db.ProtocolVersions.AsNoTracking().SingleAsync(v => v.Id == version, ct)).Status.Should().Be(ProtocolVersionStatus.Draft);
        }

        const string Text = "Recorded for the site's own records only; not tabulated.";
        var attestation = await SignAsync(world.Tenant, new AttestationTarget(
            NodeKinds.ValidationFinding, finding.Id.Value, FindingDecisionContent.Hash(finding.Id, run.RunId, FindingStatus.Accepted, Text)), AttestationMeaning.Responsibility, ct);
        (await SendAsync(new AcceptFinding(world.Tenant, finding.Id, Text, attestation, Key(), Reason), ct)).Error.Should().BeNull();

        var submitted = await _scenario.SubmitAsync(world.Tenant, version, ct);

        submitted.Error.Should().BeNull();
        submitted.Value!.Status.Should().Be(ProtocolVersionStatus.InReview);
        (await fixture.DesignConformance.RunAsync(new DesignConformanceRequest(world.Tenant, version, Key(), Reason), ct))
            .Error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code, "conformance runs on drafts only");
    }

    /// <summary>
    /// Findings follow design elements across protocol versions, so the block reads only the run of this draft: two
    /// amendments forked from one approved version share their items, and a run of the conformant one that resolves the
    /// shared finding does not let the other through.
    /// </summary>
    [Fact(DisplayName = "Submit_IsBlockedByItsOwnRun_WhateverAnotherVersionsRunFound")]
    public async Task SubmitIsBlockedByItsOwnRunWhateverAnotherVersionsRunFound()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var parent = await DraftAsync(world, SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt), ct);
        await RunAsync(world, parent, ct);
        var inReview = await _scenario.SubmitAsync(world.Tenant, parent, ct);
        inReview.Error.Should().BeNull();
        var approval = await SignAsync(world.Tenant, KernelProtocolScenario.Target(inReview.Value!), AttestationMeaning.Approved, ct);
        (await _scenario.ApproveAsync(world.Tenant, parent, approval, ct)).Error.Should().BeNull();
        var (bad, good) = (ProtocolVersionId.New(), ProtocolVersionId.New());
        foreach (var fork in new[] { bad, good })
        {
            (await SendAsync(new ForkProtocolVersion(world.Tenant, parent, fork, KernelProtocolScenario.Amendment, Key(), Reason), ct)).Error.Should().BeNull();
        }

        (await SendAsync(new ReviseProtocolDraft(
            world.Tenant, bad, ProtocolDesignDocument.Write(SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt, unit: "cm[H2O]")), null, Key(), Reason), ct))
            .Error.Should().BeNull();

        (await RunAsync(world, bad, ct)).Detected.Should().Be(1);
        (await RunAsync(world, good, ct)).Resolved.Should().Be(1, "the other draft's run no longer detects the shared item's finding");
        var refused = await _scenario.SubmitAsync(world.Tenant, bad, ct);

        refused.Error!.Code.Should().Be(StandardsErrors.DesignNotConformant.Code);
        refused.Error.Message.Should().Contain("DC-002");
        (await _scenario.SubmitAsync(world.Tenant, good, ct)).Error.Should().BeNull();
    }

    /// <summary>
    /// A run counts only while it covers what the study pins now and both fact datasets: a run that predates a new mapping
    /// pin, or that did not evaluate the item facts, is refused as not run.
    /// </summary>
    [Fact(DisplayName = "Submit_NeedsARunOfWhatTheStudyPinsNow_OverBothDatasets")]
    public async Task SubmitNeedsARunOfWhatTheStudyPinsNowOverBothDatasets()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var version = await DraftAsync(world, SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt), ct);
        var node = (await fixture.ProtocolDesigns.GetDesignAsync(world.Tenant, version, ct))!.Node;

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        (await SendAsync(new RecordValidationRun(
            world.Tenant, ValidationRunId.New(), world.Study, ValidationStage.Design, [world.Pack], new DateOnly(2026, 9, 30), null, null, null,
            [node, world.MappingNode], [DesignConformance.ConceptDataset], fixture.Clock.UtcNow, fixture.Clock.UtcNow, [], Key(), Reason), ct)).Error.Should().BeNull();
        var partial = await _scenario.SubmitAsync(world.Tenant, version, ct);

        (await RunAsync(world, version, ct)).Detected.Should().Be(0);
        var revised = await ApprovedItemAsync(
            world.Tenant,
            LibraryItemKind.MappingSpecification,
            "sdtm-vitals-2",
            MappingSpecificationJson.ToJson(SampleMappingSpecifications.Specification(
                SampleMappingSpecifications.SystolicBloodPressure(world.Systolic), SampleMappingSpecifications.Position(world.Position))),
            [world.Sdtmig],
            ct);
        (await SendAsync(new DeclareStudyStandards(
            world.Tenant, world.Study, world.Profile, [world.Sdtmig], [SampleMappingSpecifications.SdtmCt], [revised, world.Pack], Key(), Reason), ct))
            .Error.Should().BeNull();
        var repinned = await _scenario.SubmitAsync(world.Tenant, version, ct);

        partial.Error!.Code.Should().Be(StandardsErrors.ConformanceNotRun.Code);
        partial.Error.Message.Should().Contain("did not cover the design fact dataset(s) CollectionItem");
        repinned.Error!.Code.Should().Be(StandardsErrors.ConformanceNotRun.Code);
        repinned.Error.Message.Should().Contain("did not use the pinned mapping specification(s) sdtm-vitals-2 v1");

        (await RunAsync(world, version, ct)).Detected.Should().Be(0);
        (await _scenario.SubmitAsync(world.Tenant, version, ct)).Error.Should().BeNull();
    }

    /// <summary>
    /// A run names the draft at the content it read, its pack as DefinedBy and its inputs as ValidatedBy; a draft revised
    /// after its run cannot be submitted until it is run again, and the new run resolves the finding the fix removed.
    /// </summary>
    [Fact(DisplayName = "Submit_NeedsARunOfTheDraftAsItIs")]
    public async Task SubmitNeedsARunOfTheDraftAsItIs()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var version = await DraftAsync(world, SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt, unit: "cm[H2O]"), ct);

        var notRun = await _scenario.SubmitAsync(world.Tenant, version, ct);
        var first = await RunAsync(world, version, ct);
        var revised = await SendAsync(new ReviseProtocolDraft(
            world.Tenant, version, ProtocolDesignDocument.Write(SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt, unit: "kPa")), null, Key(), Reason), ct);
        var stale = await _scenario.SubmitAsync(world.Tenant, version, ct);

        notRun.Error!.Code.Should().Be(StandardsErrors.ConformanceNotRun.Code);
        first.Detected.Should().Be(1);
        revised.Error.Should().BeNull();
        stale.Error!.Code.Should().Be(StandardsErrors.ConformanceNotRun.Code, "the run read the draft before its revision");

        var second = await RunAsync(world, version, ct);
        second.Should().BeEquivalentTo(new { Detected = 0, New = 0, Resolved = 1 });
        var snapshot = (await fixture.ProtocolDesigns.GetDesignAsync(world.Tenant, version, ct))!;
        await using (var db = fixture.CreateContext(world.Tenant))
        {
            var edges = await db.Set<LineageEdgeRecord>().AsNoTracking().Where(e => e.ToId == second.RunId.Value).ToListAsync(ct);
            edges.Should().Contain(e => e.Kind == LineageEdgeKind.ValidatedBy && e.FromKind == NodeKinds.ProtocolVersion
                && e.FromId == version.Value && e.FromVersion == snapshot.Node.Version);
            edges.Should().Contain(e => e.Kind == LineageEdgeKind.ValidatedBy && e.FromKind == NodeKinds.LibraryItem && e.FromId == world.Mapping.Value);
            edges.Should().Contain(e => e.Kind == LineageEdgeKind.DefinedBy && e.FromId == world.Pack.Value);
        }

        (await _scenario.SubmitAsync(world.Tenant, version, ct)).Error.Should().BeNull();
    }

    /// <summary>
    /// Validates K-PROT-3 at design time: an item with no path to an endpoint, estimand, safety rule or need raises a
    /// Warning that is reported but does not block submission; a conformant design raises nothing.
    /// </summary>
    [Fact(DisplayName = "Submit_WarningsDoNotBlock_AndAConformantDesignHasNoFindings")]
    public async Task SubmitWarningsDoNotBlockAndAConformantDesignHasNoFindings()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var withoutPurpose = await DraftAsync(world, SampleDesignConformance.Vitals(world.Systolic, world.Position, world.SdtmCt, withPurpose: false), ct);

        var run = await RunAsync(world, withoutPurpose, ct);

        run.Detected.Should().Be(1);
        (await fixture.Findings.ListAsync(world.Tenant, new FindingQuery(world.Study) { Stage = ValidationStage.Design }, ct)).Findings
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new { RuleId = "DC-007", Severity = ValidationSeverity.Warning, Status = FindingStatus.Open });
        (await _scenario.SubmitAsync(world.Tenant, withoutPurpose, ct)).Error.Should().BeNull();

        var other = await WorldAsync(ct);
        var conformant = await DraftAsync(other, SampleDesignConformance.Vitals(other.Systolic, other.Position, other.SdtmCt), ct);
        (await RunAsync(other, conformant, ct)).Detected.Should().Be(0);
        (await _scenario.SubmitAsync(other.Tenant, conformant, ct)).Error.Should().BeNull();
    }

    /// <summary>A run of a protocol version the tenant does not hold, or of a study with no standards profile, is refused.</summary>
    [Fact(DisplayName = "Run_RefusesUnknownVersionsAndStudiesWithoutStandards")]
    public async Task RunRefusesUnknownVersionsAndStudiesWithoutStandards()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var world = await WorldAsync(ct);
        var (_, study) = await _scenario.NewStudyAsync(world.Tenant, ct);
        var undeclared = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(world.Tenant, study, undeclared, ct)).Error.Should().BeNull();

        (await fixture.DesignConformance.RunAsync(new DesignConformanceRequest(world.Tenant, ProtocolVersionId.New(), Key(), Reason), ct))
            .Error.Should().Be(KernelErrors.NotFound);
        (await fixture.DesignConformance.RunAsync(new DesignConformanceRequest(world.Tenant, undeclared, Key(), Reason), ct))
            .Error.Should().Be(RuleErrors.NoRulePacks);
        (await _scenario.SubmitAsync(world.Tenant, undeclared, ct)).Error.Should().BeNull("a study with no standards profile submits as before");
    }

    /// <summary>
    /// A tenant with the sample SDTM CT imported, two Concepts, approved SDTMIG 3.4, an approved mapping specification,
    /// the approved design conformance pack, and a study that declares SDTMIG and pins both.
    /// </summary>
    private async Task<World> WorldAsync(CancellationToken ct)
    {
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var codeSystem = (await SendAsync(new RegisterCodeSystem(
            tenant, CodeSystemId.New(), KnownCodeSystems.SdtmCt, "CDISC SDTM Controlled Terminology", "CDISC", LicenseClass.Open, Key(), Reason), ct)).Value;
        var content = SampleDesignConformance.ControlledTerminology;
        var loaded = await new TerminologyPackageLoader(fixture.Sender).LoadAsync(
            new TerminologyRelease(
                tenant, codeSystem, CodeSystemVersionId.New(), SampleMappingSpecifications.SdtmCt.Version, new DateOnly(2024, 9, 27), "terminology.txt",
                "https://evs.nci.nih.gov/ftp1/CDISC/", TerminologyFixtures.Sha256(content), Reason),
            content,
            new CdiscControlledTerminologyParser("sdtmct"),
            ct);
        loaded.Error.Should().BeNull();

        var systolic = await DefineAsync(tenant, "Systolic blood pressure", ct);
        var position = await DefineAsync(tenant, "Vital signs position of subject", ct);
        var sdtmig = await ApprovedStandardAsync(tenant, ct);
        var mapping = await ApprovedItemAsync(
            tenant,
            LibraryItemKind.MappingSpecification,
            "sdtm-vitals",
            MappingSpecificationJson.ToJson(SampleMappingSpecifications.Specification(
                SampleMappingSpecifications.SystolicBloodPressure(systolic), SampleMappingSpecifications.Position(position))),
            [sdtmig],
            ct);
        var pack = await ApprovedItemAsync(tenant, LibraryItemKind.RulePack, DesignConformancePack.Key, DesignConformancePack.Content, [], ct);
        var profile = StudyStandardsProfileId.New();
        (await SendAsync(new DeclareStudyStandards(
            tenant, study, profile, [sdtmig], [SampleMappingSpecifications.SdtmCt], [mapping, pack], Key(), Reason), ct)).Error.Should().BeNull();
        var mappingNode = NodeRef.For(mapping, (await fixture.Standards.FindLibraryItemAsync(tenant, mapping, ct))!.VersionIndex);
        return new World(tenant, study, profile, loaded.Value, systolic, position, sdtmig, mapping, mappingNode, pack);
    }

    private async Task<ConceptRef> DefineAsync(TenantId tenant, string label, CancellationToken ct)
    {
        var result = await SendAsync(new DefineConcept(
            tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, label, $"{label} (definition).", Key(), Reason), ct);
        result.Error.Should().BeNull();
        return result.Value;
    }

    private async Task<StandardVersionId> ApprovedStandardAsync(TenantId tenant, CancellationToken ct)
    {
        var id = (await SendAsync(new RegisterStandardVersion(
            tenant, StandardVersionId.New(), StandardSeeds.LoadAll().Single(s => s.Package.StandardCode == "SDTMIG").Package, null, Key(), Reason), ct)).Value;
        var hash = (await fixture.Standards.FindStandardVersionAsync(tenant, id, ct))!.ContentHash;
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.StandardVersion, id.Value, hash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveStandardVersion(tenant, id, approval, Key(), Reason), ct)).Error.Should().BeNull();
        return id;
    }

    private async Task<LibraryItemId> ApprovedItemAsync(
        TenantId tenant, LibraryItemKind kind, string key, System.Text.Json.JsonElement content, IReadOnlyList<StandardVersionId> standards, CancellationToken ct)
    {
        var draft = await SendAsync(new DraftLibraryItem(tenant, LibraryItemId.New(), kind, key, key, content, standards, Key(), Reason), ct);
        draft.Error.Should().BeNull();
        var approval = await SignAsync(tenant, new AttestationTarget(NodeKinds.LibraryItem, draft.Value!.Id.Value, draft.Value.ContentHash), AttestationMeaning.Approved, ct);
        (await SendAsync(new ApproveLibraryItem(tenant, draft.Value.Id, approval, Key(), Reason), ct)).Error.Should().BeNull();
        return draft.Value.Id;
    }

    private async Task<ProtocolVersionId> DraftAsync(World world, ProtocolDesign design, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await _scenario.CreateDraftAsync(world.Tenant, world.Study, id, ct, ProtocolDesignDocument.Write(design))).Error.Should().BeNull();
        return id;
    }

    private async Task<ValidationRunOutcome> RunAsync(World world, ProtocolVersionId version, CancellationToken ct)
    {
        // The ledger orders runs by when they were recorded; a later run must be recorded later on the fake clock.
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var result = await fixture.DesignConformance.RunAsync(new DesignConformanceRequest(world.Tenant, version, Key(), Reason) { AsOf = new DateOnly(2026, 9, 30) }, ct);
        result.Error.Should().BeNull();
        return result.Value!;
    }

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
        StudyStandardsProfileId Profile,
        CodeSystemVersionId SdtmCt,
        ConceptRef Systolic,
        ConceptRef Position,
        StandardVersionId Sdtmig,
        LibraryItemId Mapping,
        NodeRef MappingNode,
        LibraryItemId Pack);
}
