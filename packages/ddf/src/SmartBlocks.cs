using System;
using System.Collections.Generic;

namespace Cadence.Ddf;

public static class SmartBlocksCatalogService
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> GetCatalog()
    {
        return new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["block_id"] = "BLOCK-VITAL-SIGNS",
                ["name"] = "Standard Vital Signs Panel",
                ["domain"] = "VS",
                ["version"] = "2.0.0",
                ["description"] = "Pre-configured panel with SYSBP, DIABP, HR, TEMP, RR, and MAP calculation.",
                ["variables"] = new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "VS_SYSBP",
                        ["name"] = "Systolic Blood Pressure",
                        ["data_type"] = "integer",
                        ["cdisc_oid"] = "IT.VS.SYSBP",
                        ["ucum_unit"] = "mmHg",
                        ["min"] = 40.0,
                        ["max"] = 250.0
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "VS_DIABP",
                        ["name"] = "Diastolic Blood Pressure",
                        ["data_type"] = "integer",
                        ["cdisc_oid"] = "IT.VS.DIABP",
                        ["ucum_unit"] = "mmHg",
                        ["min"] = 30.0,
                        ["max"] = 150.0
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "VS_PULSE",
                        ["name"] = "Pulse / Heart Rate",
                        ["data_type"] = "integer",
                        ["cdisc_oid"] = "IT.VS.PULSE",
                        ["ucum_unit"] = "beats/min",
                        ["min"] = 30.0,
                        ["max"] = 220.0
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "VS_TEMP",
                        ["name"] = "Body Temperature",
                        ["data_type"] = "float",
                        ["cdisc_oid"] = "IT.VS.TEMP",
                        ["ucum_unit"] = "Cel",
                        ["min"] = 32.0,
                        ["max"] = 43.0
                    }
                }
            },
            new Dictionary<string, object?>
            {
                ["block_id"] = "BLOCK-ADVERSE-EVENT",
                ["name"] = "CDASH Adverse Event Log",
                ["domain"] = "AE",
                ["version"] = "2.1.0",
                ["description"] = "MedDRA-enabled AE log grid with seriousness criteria and CTCAE toxicity grades.",
                ["variables"] = new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>
                    {
                        ["id"] = "AE_TERM",
                        ["name"] = "Adverse Event Verbatim Term",
                        ["data_type"] = "string",
                        ["cdisc_oid"] = "IT.AE.AETERM"
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "AE_START_DATE",
                        ["name"] = "AE Start Date",
                        ["data_type"] = "date",
                        ["cdisc_oid"] = "IT.AE.AESTDAT"
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "AE_SEVERITY",
                        ["name"] = "Toxicity Grade / Severity",
                        ["data_type"] = "codelist",
                        ["cdisc_oid"] = "IT.AE.AESEV"
                    },
                    new Dictionary<string, object?>
                    {
                        ["id"] = "AE_SERIOUS",
                        ["name"] = "Serious Adverse Event (SAE)?",
                        ["data_type"] = "boolean",
                        ["cdisc_oid"] = "IT.AE.AESER"
                    }
                }
            }
        };
    }

    public static TripartiteFormDefinition InstantiateSmartBlock(string blockId, string formId)
    {
        IReadOnlyDictionary<string, object?>? foundBlock = null;
        foreach (var b in GetCatalog())
        {
            if (b.TryGetValue("block_id", out var idObj) && idObj?.ToString() == blockId)
            {
                foundBlock = b;
                break;
            }
        }

        if (foundBlock is null)
        {
            throw new ArgumentException($"Smart block '{blockId}' not found in Global Library.");
        }

        string domain = foundBlock["domain"]?.ToString() ?? "VS";
        string name = foundBlock["name"]?.ToString() ?? "Smart Block";
        string version = foundBlock["version"]?.ToString() ?? "1.0.0";
        var rawVars = (IReadOnlyList<IReadOnlyDictionary<string, object?>>)foundBlock["variables"]!;

        var variables = new List<VariableDefinition>();
        var widgets = new List<FieldWidgetConfig>();

        foreach (var v in rawVars)
        {
            string varId = v["id"]!.ToString()!;
            string varName = v["name"]!.ToString()!;
            string dataType = v["data_type"]!.ToString()!;
            string oid = v["cdisc_oid"]!.ToString()!;
            string? unit = v.TryGetValue("ucum_unit", out var uObj) ? uObj?.ToString() : null;

            double? minVal = v.TryGetValue("min", out var minObj) && minObj != null ? Convert.ToDouble(minObj) : null;
            double? maxVal = v.TryGetValue("max", out var maxObj) && maxObj != null ? Convert.ToDouble(maxObj) : null;

            string varShortName = varId.Contains('_') ? varId[(varId.LastIndexOf('_') + 1)..] : varId;

            var cdisc = new CdiscMetadata(
                Oid: oid,
                Domain: domain,
                VariableName: varShortName,
                UcumUnit: unit
            );

            var constraints = new ConstraintDefinition(
                Required: true,
                MinValue: minVal,
                MaxValue: maxVal
            );

            var varDef = new VariableDefinition(
                Id: varId,
                Name: varName,
                DataType: dataType,
                Cdisc: cdisc,
                Constraints: constraints
            );

            variables.Add(varDef);

            string widgetType = (dataType is "integer" or "float")
                ? WidgetType.NumberInput
                : WidgetType.TextInput;

            widgets.Add(new FieldWidgetConfig(
                VariableId: varId,
                Widget: widgetType,
                GridColumnSpan: 6
            ));
        }

        var dataSchema = new DataSchema(
            FormId: formId,
            Name: name,
            CdiscFormOid: $"FORM.{domain}",
            Variables: variables
        );

        var gridSection = new GridSection(
            Id: $"SEC-{domain}",
            Title: name,
            Widgets: widgets,
            Columns: 12
        );

        var uiSchema = new UiSchema(
            FormId: formId,
            Sections: [gridSection],
            DefaultViewport: ViewportMode.DesktopEdc
        );

        var logicSchema = new LogicSchema(
            FormId: formId,
            EditChecks: [],
            DerivedRules: [],
            CrossVisitDependencies: []
        );

        return new TripartiteFormDefinition(
            FormId: formId,
            ProtocolId: "GLOBAL_LIBRARY",
            VersionTag: version,
            DataSchema: dataSchema,
            UiSchema: uiSchema,
            LogicSchema: logicSchema
        );
    }
}
