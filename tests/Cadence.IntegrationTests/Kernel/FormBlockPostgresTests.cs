using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Standards;
using Cadence.Kernel.Trust;
using Cadence.Standards.Blocks;

using FluentAssertions;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Reusable form blocks as governed collection specifications on PostgreSQL (ADR-2503): the shipped vital signs block
/// drafts and approves as a <c>CollectionSpecification</c> library item, instantiates into a protocol design once the
/// tenant holds its Biomedical Concepts, and invalid content is refused when drafted. Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FormBlockPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// The vital signs block is approved as library content, cannot be instantiated while the tenant lacks its concepts,
    /// and once they are defined it becomes a collection specification that a protocol draft accepts.
    /// </summary>
    [Fact]
    public async Task VitalSignsBlock_ApprovedAndInstantiated_IsAcceptedInAProtocolDraft()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var block = FormBlocks.Load("vital-signs.json");

        var item = await _scenario.OkAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.CollectionSpecification, block.Key, block.Title, block.Content, [], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            ct);
        var approval = await _scenario.OkAsync(
            _scenario.AttestCommand(tenant, AttestationMeaning.Approved, new AttestationTarget(NodeKinds.LibraryItem, item.Id.Value, item.ContentHash)), ct);
        await _scenario.OkAsync(new ApproveLibraryItem(tenant, item.Id, approval, KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var stored = await fixture.Standards.FindLibraryItemAsync(tenant, item.Id, ct);
        stored!.Status.Should().Be(GovernanceStatus.Approved);
        var definition = CollectionSpecificationJson.Read(stored.Content).Definition!;

        var missing = await CollectionSpecificationInstantiator.InstantiateAsync(definition, "VS", fixture.ConceptRegistry, tenant, ct);
        missing.IsSuccess.Should().BeFalse();
        missing.Problems.Should().HaveCount(5, "none of the block's Biomedical Concepts is in the tenant yet");

        foreach (string code in definition.Items.Select(i => i.BiomedicalConcept))
        {
            await _scenario.OkAsync(
                new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.CdiscBiomedicalConcept, code, ConceptKind.BiomedicalConcept, $"CDISC {code}", $"CDISC Biomedical Concept {code}.", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
                ct);
        }

        var instantiated = await CollectionSpecificationInstantiator.InstantiateAsync(definition, "VS", fixture.ConceptRegistry, tenant, ct);
        instantiated.Problems.Should().BeEmpty();
        var spec = instantiated.Spec!;
        spec.Items.Select(i => i.Id).Should().Equal("VS.SYSBP", "VS.DIABP", "VS.PULSE", "VS.RESP", "VS.TEMP");
        foreach (var collected in spec.Items)
        {
            (await fixture.ConceptRegistry.ResolveAsync(tenant, collected.Concept, ct)).Should().NotBeNull();
        }

        var design = ProtocolDesignDocument.Read(KernelProtocolScenario.Content).Design! with { CollectionSpecs = [spec] };
        (await _scenario.CreateDraftAsync(tenant, study, ProtocolVersionId.New(), ct, ProtocolDesignDocument.Write(design))).Error.Should().BeNull();
    }

    /// <summary>Drafting runs the collection specification validator, so content that cannot be instantiated is refused.</summary>
    [Fact]
    public async Task CollectionSpecification_InvalidContent_IsRefusedWhenDrafted()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, _) = await _scenario.NewStudyAsync(ct);
        using var invalid = JsonDocument.Parse("""{"name":"Vitals","items":[{"id":"SYSBP","concept":"systolic"}]}""");

        var result = await fixture.Sender.SendAsync(
            new DraftLibraryItem(tenant, LibraryItemId.New(), LibraryItemKind.CollectionSpecification, "vitals", "Vitals", invalid.RootElement, [], KernelProtocolScenario.Key(), KernelProtocolScenario.Reason),
            cancellationToken: ct);

        result.Error!.Code.Should().Be(StandardsErrors.LibraryItemInvalid.Code);
        result.Error.Message.Should().Contain("'concept' must be a CDISC Biomedical Concept C-code");
    }
}
