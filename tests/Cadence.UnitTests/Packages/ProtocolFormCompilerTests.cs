using System;
using System.Text.Json;
using System.Xml.Linq;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Trust;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public sealed class ProtocolFormCompilerTests
{
    private static readonly ConceptRef DummyConcept = new(
        ConceptId.FromGuid(Guid.Parse("01960000-0000-7000-8000-000000000001")),
        1);

    [Fact(DisplayName = "Compile_ReturnsNull_WhenCollectionSpecsEmptyOrItemless")]
    public void CompileReturnsNullWhenCollectionSpecsEmptyOrItemless()
    {
        var compiler = new ProtocolFormCompiler();
        var emptySpecs = new ProtocolDesign { Title = "Empty Design" };
        compiler.Compile(emptySpecs, "STUDY-101", "1").Should().BeNull();

        var itemlessSpecs = new ProtocolDesign
        {
            Title = "Itemless Design",
            CollectionSpecs = [new CollectionSpec("F_EMPTY", "Empty Form", [])],
        };
        compiler.Compile(itemlessSpecs, "STUDY-101", "1").Should().BeNull();
    }

    [Fact(DisplayName = "Compile_ProducesValidOpenRosaOdmDdfIrAndCanonicalHash_FromCollectionSpecs")]
    public void CompileProducesValidOpenRosaOdmDdfIrAndCanonicalHashFromCollectionSpecs()
    {
        var csVersion = CodeSystemVersionId.FromGuid(Guid.Parse("01960000-0000-7000-8000-000000000002"));
        var design = new ProtocolDesign
        {
            Title = "Vital Signs & Notes Protocol",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "ACT_VS",
                    "Vital Signs",
                    [
                        new CollectionItem(
                            "ACT_VS.BC_SBP",
                            DummyConcept,
                            Unit: "mm[Hg]",
                            Required: true,
                            ValueKind: ObservationValueKind.Quantity,
                            Prompt: "Systolic Blood Pressure"),
                        new CollectionItem(
                            "ACT_VS.BC_POS",
                            DummyConcept,
                            Terminology: new TerminologyBinding(csVersion, "VSPOS"),
                            Required: true,
                            ValueKind: ObservationValueKind.Coded,
                            Prompt: "Body Position"),
                        new CollectionItem(
                            "ACT_VS.BC_FAST",
                            DummyConcept,
                            Required: false,
                            ValueKind: ObservationValueKind.Boolean,
                            Prompt: "Fasting"),
                        new CollectionItem(
                            "ACT_VS.BC_DTC",
                            DummyConcept,
                            Required: false,
                            ValueKind: ObservationValueKind.DateTime,
                            Prompt: "Collection Date/Time"),
                    ]),
            ],
        };

        IProtocolFormCompiler compiler = new ProtocolFormCompiler();
        var compiled = compiler.Compile(design, "STUDY-101", "2");

        compiled.Should().NotBeNull();
        compiled!.FormId.Should().Be("STUDY-101");
        compiled.FormHash.Should().Be(ContentHash.OfCanonical(compiled.OpenRosaXml));
        ContentHash.IsWellFormed(compiled.FormHash).Should().BeTrue();

        var openRosaDoc = XDocument.Parse(compiled.OpenRosaXml);
        openRosaDoc.Root.Should().NotBeNull();
        compiled.OpenRosaXml.Should().Contain("Systolic Blood Pressure").And.Contain("Unit: mm[Hg]");

        var odmDoc = XDocument.Parse(compiled.OdmXml);
        odmDoc.Root!.Name.LocalName.Should().Be("ODM");
        compiled.OdmXml.Should().Contain("IT.ACT_VS.BC_SBP");

        using var irDoc = JsonDocument.Parse(compiled.DdfIrJson);
        irDoc.RootElement.GetProperty("studyId").GetString().Should().Be("STUDY-101");
        irDoc.RootElement.GetProperty("version").GetString().Should().Be("2");
        irDoc.RootElement.GetProperty("items").GetArrayLength().Should().Be(4);
    }
}
