using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Kernel.Protocol.Design;

namespace Cadence.Ddf;

public static class TripartiteCompiler
{
    public static TripartiteFormDefinition CompileTripartiteForm(
        ProtocolDesign design,
        string formId,
        string protocolId,
        int versionIndex = 1,
        string versionTag = "1.0.0",
        string reasonForChange = "Compiled from USDM protocol design",
        DateTimeOffset createdTime = default)
    {
        ArgumentNullException.ThrowIfNull(design);

        var variables = new List<VariableDefinition>();
        var sections = new List<GridSection>();
        var editChecks = new List<AstEditCheck>();

        foreach (var spec in design.CollectionSpecs)
        {
            var widgets = new List<FieldWidgetConfig>();
            foreach (var item in spec.Items)
            {
                string conceptCode = item.Concept.ConceptId.ToString();
                string varId = !string.IsNullOrWhiteSpace(conceptCode) ? conceptCode : $"{spec.Id}_{variables.Count + 1}";
                string varName = varId;

                var variable = new VariableDefinition(
                    Id: varId,
                    Name: varName,
                    DataType: ClinicalDataType.String,
                    Cdisc: new CdiscMetadata(
                        Domain: "VS",
                        VariableName: varId,
                        Oid: varId
                    ),
                    Constraints: new ConstraintDefinition(
                        Required: item.Required,
                        MinValue: 0,
                        MaxValue: 500
                    )
                );
                variables.Add(variable);

                widgets.Add(new FieldWidgetConfig(
                    VariableId: varId,
                    Widget: WidgetType.TextInput,
                    GridColumnSpan: 12,
                    Placeholder: $"Enter {varName}"
                ));

                if (item.Required)
                {
                    var astExpr = new JsonObject
                    {
                        ["operator"] = "is_empty",
                        ["target"] = varId
                    };
                    editChecks.Add(new AstEditCheck(
                        Id: $"CHK_REQ_{varId}",
                        Name: $"Required Field Check for {varName}",
                        AstExpression: astExpr,
                        Severity: "HARD",
                        QueryMessage: $"Field {varName} ({varId}) is required.",
                        TargetVariableIds: [varId]
                    ));
                }
            }

            sections.Add(new GridSection(
                Id: $"SEC_{spec.Id}",
                Title: spec.Name,
                Columns: 12,
                Widgets: widgets
            ));
        }

        if (variables.Count == 0)
        {
            var defaultVar = new VariableDefinition(
                Id: "VSORRES",
                Name: "Vital Signs Result",
                DataType: ClinicalDataType.Float,
                Cdisc: new CdiscMetadata(Domain: "VS", VariableName: "VSORRES", Oid: "VSORRES"),
                Constraints: new ConstraintDefinition(Required: true, MinValue: 40, MaxValue: 250)
            );
            variables.Add(defaultVar);

            sections.Add(new GridSection(
                Id: "SEC_DEFAULT",
                Title: "Vital Signs",
                Columns: 12,
                Widgets: [new FieldWidgetConfig(VariableId: "VSORRES", Widget: WidgetType.NumberInput, GridColumnSpan: 12)]
            ));

            var astExpr = new JsonObject
            {
                ["operator"] = "<",
                ["target"] = "VSORRES",
                ["operands"] = new JsonArray
                {
                    new JsonObject { ["target"] = "VSORRES" },
                    new JsonObject { ["value"] = 40.0 }
                }
            };
            editChecks.Add(new AstEditCheck(
                Id: "CHK_MIN_VSORRES",
                Name: "Minimum Vital Signs Result Check",
                AstExpression: astExpr,
                Severity: "HARD",
                QueryMessage: "VSORRES is below minimum allowed value 40.0",
                TargetVariableIds: ["VSORRES"]
            ));
        }

        var dataSchema = new DataSchema(
            FormId: formId,
            Name: design.Title,
            CdiscFormOid: $"FORM_{formId}",
            Variables: variables
        );

        var uiSchema = new UiSchema(
            FormId: formId,
            Sections: sections,
            DefaultViewport: ViewportMode.DesktopEdc
        );

        var logicSchema = new LogicSchema(
            FormId: formId,
            EditChecks: editChecks
        );

        return new TripartiteFormDefinition(
            FormId: formId,
            ProtocolId: protocolId,
            VersionIndex: versionIndex,
            VersionTag: versionTag,
            DataSchema: dataSchema,
            UiSchema: uiSchema,
            LogicSchema: logicSchema,
            CreatedAt: createdTime != default ? createdTime : new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedBy: "system",
            ReasonForChange: reasonForChange
        );
    }
}
