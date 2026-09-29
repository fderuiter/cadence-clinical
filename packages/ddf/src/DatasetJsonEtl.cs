using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Ddf;

public static class DatasetJsonEtlProcessor
{
    public static IReadOnlyDictionary<string, object?> ExtractSdtmDomain(
        TripartiteFormDefinition formDef,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rawObservations,
        string domain = "VS")
    {
        string creationTime = DateTimeOffset.UtcNow.ToString("o");

        if (rawObservations == null || rawObservations.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["datasetJSONCreationDateTime"] = creationTime,
                ["records"] = 0,
                ["domain"] = domain,
                ["columns"] = new List<IReadOnlyDictionary<string, string>>(),
                ["rows"] = new List<IReadOnlyList<object?>>()
            };
        }

        var varToSdtm = formDef.DataSchema.Variables
            .Where(v => v.Cdisc.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(v => v.Id, v => v.Cdisc.VariableName);

        var sdtmColumns = new List<IReadOnlyDictionary<string, string>>
        {
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = "STUDYID",
                ["label"] = "Study Identifier",
                ["dataType"] = "string"
            },
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = "USUBJID",
                ["label"] = "Unique Subject Identifier",
                ["dataType"] = "string"
            },
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = "DOMAIN",
                ["label"] = "Domain Abbreviation",
                ["dataType"] = "string"
            },
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = $"{domain}TESTCD",
                ["label"] = $"{domain} Short Name",
                ["dataType"] = "string"
            },
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = $"{domain}ORRES",
                ["label"] = "Original Result",
                ["dataType"] = "string"
            },
            new Dictionary<string, string>
            {
                ["itemGroupOID"] = $"IG.{domain}",
                ["name"] = $"{domain}ORRESU",
                ["label"] = "Original Units",
                ["dataType"] = "string"
            }
        };

        var rows = new List<IReadOnlyList<object?>>();

        foreach (var record in rawObservations)
        {
            string studyId = record.TryGetValue("study_id", out var stObj) && stObj != null ? stObj.ToString()! : formDef.ProtocolId;
            string subjectId = record.TryGetValue("subject_id", out var sbObj) && sbObj != null ? sbObj.ToString()! : "UNKNOWN";
            string usubjid = $"{studyId}-{subjectId}";

            foreach (var (varId, sdtmVar) in varToSdtm)
            {
                if (record.TryGetValue(varId, out var val) && val != null)
                {
                    string unit = formDef.DataSchema.Variables
                        .FirstOrDefault(v => v.Id == varId)?.Cdisc.UcumUnit ?? string.Empty;

                    var row = new List<object?>
                    {
                        studyId,
                        usubjid,
                        domain,
                        sdtmVar,
                        val.ToString(),
                        unit
                    };
                    rows.Add(row);
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["datasetJSONCreationDateTime"] = creationTime,
            ["records"] = rows.Count,
            ["domain"] = domain,
            ["columns"] = sdtmColumns,
            ["rows"] = rows
        };
    }
}
