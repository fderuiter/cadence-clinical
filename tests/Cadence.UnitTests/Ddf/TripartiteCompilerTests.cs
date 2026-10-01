using System;
using System.Linq;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ddf;

public class TripartiteCompilerTests
{
    [Fact]
    public void CompileTripartiteForm_WithValidDesign_GeneratesSchemas()
    {
        var concept1 = ConceptId.New();
        var concept2 = ConceptId.New();

        var design = new ProtocolDesign
        {
            Title = "Cardiovascular Trial Phase II",
            CollectionSpecs =
            [
                new CollectionSpec(
                    Id: "ACT-001",
                    Name: "Vital Signs",
                    Items:
                    [
                        new CollectionItem("ACT-001.BC-001", new ConceptRef(concept1, 1), Required: true),
                        new CollectionItem("ACT-001.BC-002", new ConceptRef(concept2, 1), Required: false)
                    ]
                )
            ]
        };

        var formDef = TripartiteCompiler.CompileTripartiteForm(
            design: design,
            formId: "FORM-VS-001",
            protocolId: "STUDY-CARDIO-01",
            versionIndex: 2,
            versionTag: "2.0.0",
            reasonForChange: "Amendment 1 eCRF sync"
        );

        formDef.Should().NotBeNull();
        formDef.FormId.Should().Be("FORM-VS-001");
        formDef.ProtocolId.Should().Be("STUDY-CARDIO-01");
        formDef.VersionIndex.Should().Be(2);
        formDef.VersionTag.Should().Be("2.0.0");

        formDef.DataSchema.Should().NotBeNull();
        formDef.DataSchema.Variables.Should().NotBeNull();
        formDef.DataSchema.Variables!.Should().HaveCount(2);
        formDef.DataSchema.Variables[0].Id.Should().Be(concept1.ToString());

        formDef.UiSchema.Should().NotBeNull();
        formDef.UiSchema.Sections.Should().NotBeNull();
        formDef.UiSchema.Sections!.Should().HaveCount(1);
        formDef.UiSchema.Sections[0].Widgets.Should().NotBeNull();
        formDef.UiSchema.Sections[0].Widgets!.Should().HaveCount(2);

        formDef.LogicSchema.Should().NotBeNull();
        formDef.LogicSchema.EditChecks.Should().NotBeNull();
        formDef.LogicSchema.EditChecks!.Should().HaveCount(1);
        formDef.LogicSchema.EditChecks[0].TargetVariableIds.Should().Contain(concept1.ToString());
    }

    [Fact]
    public void CompileTripartiteForm_WithEmptyDesign_ProvidesDefaultFallbackVariable()
    {
        var design = new ProtocolDesign { Title = "Empty Trial" };

        var formDef = TripartiteCompiler.CompileTripartiteForm(
            design: design,
            formId: "FORM-EMPTY",
            protocolId: "STUDY-EMPTY",
            versionIndex: 1,
            versionTag: "1.0.0"
        );

        formDef.DataSchema.Variables.Should().NotBeNull();
        formDef.DataSchema.Variables!.Should().ContainSingle();
        formDef.DataSchema.Variables[0].Id.Should().Be("VSORRES");
        formDef.LogicSchema.EditChecks.Should().NotBeNull();
        formDef.LogicSchema.EditChecks!.Should().ContainSingle();
    }
}
