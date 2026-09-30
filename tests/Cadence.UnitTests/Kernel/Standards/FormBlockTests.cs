using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Standards;
using Cadence.Standards;
using Cadence.Standards.Blocks;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Kernel.Standards;

/// <summary>
/// Reusable form blocks as collection specification library content (ADR-2503): the strict content format, the
/// shipped vital signs block, and instantiation into a protocol design's collection specification.
/// </summary>
public class FormBlockTests
{
    private const string SdtmCtVersion = "2024-09-27";

    private static readonly TenantId Tenant = TenantId.New();

    /// <summary>The shipped vital signs block loads, is valid content, and names the SDTM CT 2024-09-27 vital signs test concepts.</summary>
    [Fact]
    public void VitalSignsBlock_Loads_WithVerifiedConceptsAndUcumUnits()
    {
        FormBlocks.Names.Should().Equal("vital-signs.json");
        var block = FormBlocks.LoadAll().Should().ContainSingle().Subject;

        block.Key.Should().Be("cadence.vital-signs");
        block.Title.Should().Be("Vital Signs");
        new CollectionSpecificationContentValidator().Validate(block.Content).Should().BeEmpty();
        block.Definition.Items.Select(i => (i.Id, i.BiomedicalConcept, i.Unit, i.Required)).Should().Equal(
            ("SYSBP", "C25298", "mm[Hg]", true),
            ("DIABP", "C25299", "mm[Hg]", true),
            ("PULSE", "C49676", "{beats}/min", true),
            ("RESP", "C49678", "{breaths}/min", true),
            ("TEMP", "C174446", "Cel", true));
    }

    /// <summary>An unknown block name is refused with the names that exist.</summary>
    [Fact]
    public void Load_UnknownBlock_NamesTheKnownOnes()
    {
        var load = () => FormBlocks.Load("hematology.json");

        load.Should().Throw<ArgumentException>().WithMessage("*Known: vital-signs.json*");
    }

    /// <summary>Content with a terminology reference reads into a definition with its pinned version and codelist.</summary>
    [Fact]
    public void Read_ItemWithTerminology_KeepsTheReference()
    {
        var (definition, problems) = Read("""
            { "name": "Seriousness", "description": "Whether the event is serious.",
              "items": [ { "id": "SER", "concept": "C25298",
                "terminology": { "codeSystem": "https://library.cdisc.org/api/mdr/ct/packages/sdtmct", "version": "2024-09-27", "codelist": "C66742" } } ] }
            """);

        problems.Should().BeEmpty();
        definition!.Description.Should().Be("Whether the event is serious.");
        var item = definition.Items.Should().ContainSingle().Subject;
        item.Required.Should().BeFalse();
        item.Unit.Should().BeNull();
        item.Terminology.Should().Be(new TerminologyReference(KnownCodeSystems.SdtmCt, SdtmCtVersion, "C66742"));
    }

    /// <summary>Each malformed part of the content is reported, and nothing is read from content with problems.</summary>
    [Theory]
    [InlineData("[]", "A collection specification is a JSON object.")]
    [InlineData("""{"items":[{"id":"A","concept":"C1"}]}""", "The collection specification: 'name' is required.")]
    [InlineData("""{"name":"  ","items":[{"id":"A","concept":"C1"}]}""", "The collection specification: 'name' must be non-blank text.")]
    [InlineData("""{"name":" Vitals","items":[{"id":"A","concept":"C1"}]}""", "The collection specification: 'name' has leading or trailing spaces.")]
    [InlineData("""{"name":"V","items":[]}""", "'items' must be a non-empty array.")]
    [InlineData("""{"name":"V","layout":"grid","items":[{"id":"A","concept":"C1"}]}""", "The collection specification: unknown property 'layout'.")]
    [InlineData("""{"name":"V","name":"W","items":[{"id":"A","concept":"C1"}]}""", "The collection specification: property 'name' appears more than once.")]
    [InlineData("""{"name":"V","items":["A"]}""", "Item 0: an item is a JSON object.")]
    [InlineData("""{"name":"V","items":[{"concept":"C1"}]}""", "Item 0: 'id' is required.")]
    [InlineData("""{"name":"V","items":[{"id":"1A","concept":"C1"}]}""", "Item '1A': the id must start with a letter and hold up to 63 characters: letters, digits, '_', '-', ':' or '.'.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1"},{"id":"A","concept":"C2"}]}""", "Item id 'A' is used more than once.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"systolic"}]}""", "Item 'A': 'concept' must be a CDISC Biomedical Concept C-code such as 'C25298'.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","unit":"beats per minute"}]}""", "Item 'A': 'unit' must be a UCUM code, which has no spaces.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","required":"yes"}]}""", "Item 'A': 'required' must be true or false.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","min":40}]}""", "Item 'A': unknown property 'min'.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","terminology":"sdtmct"}]}""", "Item 'A': 'terminology' is an object with 'codeSystem', 'version' and an optional 'codelist'.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","terminology":{"codeSystem":"sdtmct","version":"1"}}]}""", "Item 'A' terminology: 'codeSystem' must be an absolute URI.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","terminology":{"codeSystem":"http://x.org/cs"}}]}""", "Item 'A' terminology: 'version' is required.")]
    [InlineData("""{"name":"V","items":[{"id":"A","concept":"C1","terminology":{"codeSystem":"http://x.org/cs","version":"1","codelist":"NY"}}]}""", "Item 'A' terminology: 'codelist' must be a codelist C-code such as 'C66742'.")]
    public void Read_MalformedContent_ReportsTheProblem(string json, string problem)
    {
        var (definition, problems) = Read(json);

        definition.Should().BeNull();
        problems.Should().Contain(problem);
    }

    /// <summary>More items than the limit are refused.</summary>
    [Fact]
    public void Read_TooManyItems_IsRefused()
    {
        var items = string.Join(",", Enumerable.Range(1, CollectionSpecificationJson.MaxItems + 1).Select(i => $$"""{"id":"I{{i}}","concept":"C{{i}}"}"""));

        Read($$"""{"name":"V","items":[{{items}}]}""").Problems.Should().Equal($"'items' holds {CollectionSpecificationJson.MaxItems + 1} items; at most {CollectionSpecificationJson.MaxItems} are allowed.");
    }

    /// <summary>The validator is registered for collection specifications, next to any other kind's validators.</summary>
    [Fact]
    public void AddCadenceStandards_RegistersTheCollectionSpecificationValidatorOnce()
    {
        using var provider = new ServiceCollection().AddCadenceStandards().AddCadenceStandards().BuildServiceProvider();

        provider.GetServices<ILibraryContentValidator>().Should().ContainSingle()
            .Which.Kind.Should().Be(LibraryItemKind.CollectionSpecification);
    }

    /// <summary>
    /// Instantiating resolves every concept and terminology reference in the tenant and prefixes item ids with the
    /// specification id, so one block can be used twice in a design.
    /// </summary>
    [Fact]
    public async Task Instantiate_ResolvesConceptsAndTerminology_AndPrefixesItemIds()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Substitute.For<IConceptRegistry>();
        var systolic = Concept(registry, "C25298");
        var version = CodeSystemVersion(registry, KnownCodeSystems.SdtmCt, SdtmCtVersion);
        registry.GetGroupAsync(Tenant, version, "C66742", Arg.Any<CancellationToken>()).Returns([Entry(version, "C49488")]);
        var definition = Read("""
            { "name": "Mixed", "items": [
              { "id": "SYSBP", "concept": "C25298", "unit": "mm[Hg]", "required": true },
              { "id": "SER", "concept": "C25298", "terminology": { "codeSystem": "https://library.cdisc.org/api/mdr/ct/packages/sdtmct", "version": "2024-09-27", "codelist": "C66742" } } ] }
            """).Definition!;

        var result = await CollectionSpecificationInstantiator.InstantiateAsync(definition, "SCREENING_VS", registry, Tenant, ct);

        result.Problems.Should().BeEmpty();
        var spec = result.Spec!;
        spec.Id.Should().Be("SCREENING_VS");
        spec.Name.Should().Be("Mixed");
        spec.Items.Select(i => i.Id).Should().Equal("SCREENING_VS.SYSBP", "SCREENING_VS.SER");
        spec.Items[0].Concept.Should().Be(systolic);
        spec.Items[0].Unit.Should().Be("mm[Hg]");
        spec.Items[0].Required.Should().BeTrue();
        spec.Items[0].Terminology.Should().BeNull();
        spec.Items[1].Terminology.Should().Be(new Cadence.Kernel.Protocol.Design.TerminologyBinding(version, "C66742"));
    }

    /// <summary>A concept, code-system version or codelist the tenant does not hold is reported, one problem each, and nothing is built.</summary>
    [Fact]
    public async Task Instantiate_UnresolvedReferences_ReportsEachAndBuildsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = Substitute.For<IConceptRegistry>();
        Concept(registry, "C25298");
        var version = CodeSystemVersion(registry, KnownCodeSystems.SdtmCt, SdtmCtVersion);
        registry.GetGroupAsync(Tenant, version, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        var definition = Read("""
            { "name": "Broken", "items": [
              { "id": "A", "concept": "C99999" },
              { "id": "B", "concept": "C25298", "terminology": { "codeSystem": "https://library.cdisc.org/api/mdr/ct/packages/sdtmct", "version": "2023-12-15" } },
              { "id": "C", "concept": "C25298", "terminology": { "codeSystem": "https://library.cdisc.org/api/mdr/ct/packages/sdtmct", "version": "2024-09-27", "codelist": "C00001" } } ] }
            """).Definition!;

        var result = await CollectionSpecificationInstantiator.InstantiateAsync(definition, "VS", registry, Tenant, ct);

        result.IsSuccess.Should().BeFalse();
        result.Problems.Should().Equal(
            "Item 'A': the Concept Registry has no Biomedical Concept C99999.",
            $"Item 'B': the Concept Registry has no version 2023-12-15 of {KnownCodeSystems.SdtmCt}.",
            $"Item 'C': {KnownCodeSystems.SdtmCt} version 2024-09-27 has no codelist C00001.");
    }

    /// <summary>A specification id that is not a valid element id is refused before anything is resolved.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("1VS")]
    [InlineData("VS SCREENING")]
    public async Task Instantiate_InvalidSpecId_IsRefused(string specId)
    {
        var registry = Substitute.For<IConceptRegistry>();

        var result = await CollectionSpecificationInstantiator.InstantiateAsync(
            FormBlocks.Load("vital-signs.json").Definition, specId, registry, Tenant, TestContext.Current.CancellationToken);

        result.Problems.Should().ContainSingle().Which.Should().StartWith($"'{specId}' is not a valid specification id");
        registry.ReceivedCalls().Should().BeEmpty();
    }

    private static (CollectionSpecificationDefinition? Definition, IReadOnlyList<string> Problems) Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CollectionSpecificationJson.Read(document.RootElement);
    }

    private static ConceptRef Concept(IConceptRegistry registry, string code)
    {
        var concept = new ConceptRef(ConceptId.New(), 1);
        registry.FindBiomedicalConceptAsync(Tenant, code, Arg.Any<CancellationToken>()).Returns(new ConceptVersionView(
            concept, ConceptOrigin.CdiscBiomedicalConcept, code, ConceptKind.BiomedicalConcept, code, code, ConceptStatus.Active, 1,
            ConceptMappingState.Unmapped, DateTimeOffset.UnixEpoch));
        return concept;
    }

    private static CodeSystemVersionId CodeSystemVersion(IConceptRegistry registry, string uri, string version)
    {
        var id = CodeSystemVersionId.New();
        registry.FindCodeSystemVersionAsync(Tenant, uri, version, Arg.Any<CancellationToken>()).Returns(new CodeSystemVersionView(
            id, CodeSystemId.New(), uri, version, new DateOnly(2024, 9, 27), LicenseClass.Open, new string('0', 64), 1));
        return id;
    }

    private static CodeSystemEntryView Entry(CodeSystemVersionId version, string code) =>
        new(version, code, "Yes", "C66742", "Y", null, []);
}
