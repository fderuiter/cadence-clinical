using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cadence.Ddf.Stages;

[SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "USDM acronym naming convention")]
public class USDMNormalizerStage
{
    private readonly bool _strictValidation;

    public USDMNormalizerStage(bool strictValidation = true)
    {
        _strictValidation = strictValidation;
    }

    public (JsonNode NormalizedPayload, List<CompilerWarning> Warnings) Normalize(string studyId, string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            throw new ArgumentException("Payload must be a non-empty JSON string.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Payload must be a valid JSON string.", ex);
        }

        if (root is not JsonObject obj)
        {
            throw new ArgumentException("Payload must be a dictionary.");
        }

        return Normalize(studyId, obj);
    }

    public (JsonNode NormalizedPayload, List<CompilerWarning> Warnings) Normalize(string studyId, JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.ContainsKey("protocol"))
        {
            throw new ArgumentException("Validation Failed: 'protocol' missing from study definition.");
        }

        var warnings = new List<CompilerWarning>();
        var vPayload = payload.DeepClone().AsObject();

        // Enforce study id fallback
        string? existingId = vPayload["id"]?.ToString();
        if (string.IsNullOrWhiteSpace(existingId) && vPayload.TryGetPropertyValue("study", out var stNode) && stNode is JsonObject stObj)
        {
            existingId = stObj["id"]?.ToString();
        }
        if (string.IsNullOrWhiteSpace(existingId) && vPayload.TryGetPropertyValue("protocol", out var prNode) && prNode is JsonObject prObj)
        {
            existingId = prObj["id"]?.ToString();
        }

        if (string.IsNullOrWhiteSpace(existingId))
        {
            string fallbackVal = Guid.TryParse(studyId, out var g) ? g.ToString() : "00000000-0000-0000-0000-000000000001";
            vPayload["id"] = fallbackVal;
            warnings.Add(new CompilerWarning(
                Field: "study_id",
                GeneratedValue: fallbackVal,
                WarningReason: "Missing required field 'study_id'; auto-populated fallback UUID."
            ));
        }
        else
        {
            vPayload["id"] = existingId;
        }

        // Enforce study name fallback
        string? existingName = vPayload["name"]?.ToString();
        if (string.IsNullOrWhiteSpace(existingName) && vPayload.TryGetPropertyValue("study", out var stNode2) && stNode2 is JsonObject stObj2)
        {
            existingName = stObj2["name"]?.ToString();
        }
        if (string.IsNullOrWhiteSpace(existingName) && vPayload.TryGetPropertyValue("protocol", out var prNode2) && prNode2 is JsonObject prObj2)
        {
            existingName = prObj2["name"]?.ToString();
        }

        if (string.IsNullOrWhiteSpace(existingName))
        {
            vPayload["name"] = "Default Study Name";
            warnings.Add(new CompilerWarning(
                Field: "name",
                GeneratedValue: "Default Study Name",
                WarningReason: "Missing required field 'name'; auto-populated fallback name."
            ));
        }
        else
        {
            vPayload["name"] = existingName;
        }

        // Auto-populate documentedBy
        if (vPayload.TryGetPropertyValue("documentedBy", out var docNode) && docNode is JsonArray docArr)
        {
            for (int docIdx = 0; docIdx < docArr.Count; docIdx++)
            {
                if (docArr[docIdx] is not JsonObject doc) continue;

                if (!doc.ContainsKey("id") || string.IsNullOrWhiteSpace(doc["id"]?.ToString()))
                {
                    string fallbackDocId = $"00000000-0000-0000-0000-00000000100{docIdx}";
                    doc["id"] = fallbackDocId;
                    warnings.Add(new CompilerWarning(
                        Field: "documentedBy.id",
                        GeneratedValue: fallbackDocId,
                        WarningReason: "Missing required field 'documentedBy.id'; auto-populated fallback UUID."
                    ));
                }

                if (!doc.ContainsKey("type") || string.IsNullOrWhiteSpace(doc["type"]?.ToString()))
                {
                    doc["type"] = "Protocol";
                    warnings.Add(new CompilerWarning(
                        Field: "documentedBy.type",
                        GeneratedValue: "Protocol",
                        WarningReason: "Missing required field 'documentedBy.type'; auto-populated fallback type."
                    ));
                }

                if (!doc.ContainsKey("templateName") || string.IsNullOrWhiteSpace(doc["templateName"]?.ToString()))
                {
                    doc["templateName"] = "Standard Template";
                    warnings.Add(new CompilerWarning(
                        Field: "documentedBy.templateName",
                        GeneratedValue: "Standard Template",
                        WarningReason: "Missing required field 'documentedBy.templateName'; auto-populated fallback template name."
                    ));
                }

                if (!doc.ContainsKey("instanceType") || string.IsNullOrWhiteSpace(doc["instanceType"]?.ToString()))
                {
                    doc["instanceType"] = "StudyDefinitionDocument";
                    warnings.Add(new CompilerWarning(
                        Field: "documentedBy.instanceType",
                        GeneratedValue: "StudyDefinitionDocument",
                        WarningReason: "Missing required field 'documentedBy.instanceType'; auto-populated fallback instance type."
                    ));
                }

                if (!doc.ContainsKey("language") || doc["language"] is not JsonObject langObj)
                {
                    string fallbackLangId = $"00000000-0000-0000-0000-00000000200{docIdx}";
                    var newLang = new JsonObject
                    {
                        ["id"] = fallbackLangId,
                        ["codeSystem"] = "ISO 639-1",
                        ["codeSystemVersion"] = "2002",
                        ["code"] = "en"
                    };
                    doc["language"] = newLang;
                    warnings.Add(new CompilerWarning(
                        Field: "documentedBy.language",
                        GeneratedValue: newLang,
                        WarningReason: "Missing required field 'documentedBy.language'; auto-populated fallback language object."
                    ));
                }
                else
                {
                    if (!langObj.ContainsKey("id") || string.IsNullOrWhiteSpace(langObj["id"]?.ToString()))
                    {
                        string fallbackLangId = $"00000000-0000-0000-0000-00000000200{docIdx}";
                        langObj["id"] = fallbackLangId;
                        warnings.Add(new CompilerWarning(
                            Field: "documentedBy.language.id",
                            GeneratedValue: fallbackLangId,
                            WarningReason: "Missing required field 'documentedBy.language.id'; auto-populated fallback UUID."
                        ));
                    }

                    if (!langObj.ContainsKey("codeSystem") || string.IsNullOrWhiteSpace(langObj["codeSystem"]?.ToString()))
                    {
                        langObj["codeSystem"] = "ISO 639-1";
                        warnings.Add(new CompilerWarning(
                            Field: "documentedBy.language.codeSystem",
                            GeneratedValue: "ISO 639-1",
                            WarningReason: "Missing required field 'documentedBy.language.codeSystem'; auto-populated fallback code system."
                        ));
                    }

                    if (!langObj.ContainsKey("codeSystemVersion") || string.IsNullOrWhiteSpace(langObj["codeSystemVersion"]?.ToString()))
                    {
                        langObj["codeSystemVersion"] = "2002";
                        warnings.Add(new CompilerWarning(
                            Field: "documentedBy.language.codeSystemVersion",
                            GeneratedValue: "2002",
                            WarningReason: "Missing required field 'documentedBy.language.codeSystemVersion'; auto-populated fallback code system version."
                        ));
                    }

                    if (!langObj.ContainsKey("code") || string.IsNullOrWhiteSpace(langObj["code"]?.ToString()))
                    {
                        if (langObj.ContainsKey("decode") && !string.IsNullOrWhiteSpace(langObj["decode"]?.ToString()))
                        {
                            string fallbackCode = langObj["decode"]!.ToString().ToLowerInvariant();
                            langObj["code"] = fallbackCode;
                            warnings.Add(new CompilerWarning(
                                Field: "documentedBy.language.code",
                                GeneratedValue: fallbackCode,
                                WarningReason: $"Missing required field 'documentedBy.language.code'; derived fallback from decode '{langObj["decode"]}'."
                            ));
                        }
                        else
                        {
                            langObj["code"] = "en";
                            warnings.Add(new CompilerWarning(
                                Field: "documentedBy.language.code",
                                GeneratedValue: "en",
                                WarningReason: "Missing required field 'documentedBy.language.code'; auto-populated fallback language code."
                            ));
                        }
                    }
                }

                if (doc.TryGetPropertyValue("versions", out var verNode) && verNode is JsonArray verArray)
                {
                    for (int verIdx = 0; verIdx < verArray.Count; verIdx++)
                    {
                        if (verArray[verIdx] is not JsonObject ver) continue;

                        if (!ver.ContainsKey("id") || string.IsNullOrWhiteSpace(ver["id"]?.ToString()))
                        {
                            string fallbackVerId = $"00000000-0000-0000-0000-00000000300{docIdx}{verIdx}";
                            ver["id"] = fallbackVerId;
                            warnings.Add(new CompilerWarning(
                                Field: "documentedBy.versions.id",
                                GeneratedValue: fallbackVerId,
                                WarningReason: "Missing required field 'documentedBy.versions.id'; auto-populated fallback UUID."
                            ));
                        }

                        if (!ver.ContainsKey("status") || string.IsNullOrWhiteSpace(ver["status"]?.ToString()))
                        {
                            ver["status"] = "Final";
                            warnings.Add(new CompilerWarning(
                                Field: "documentedBy.versions.status",
                                GeneratedValue: "Final",
                                WarningReason: "Missing required field 'documentedBy.versions.status'; auto-populated fallback status."
                            ));
                        }

                        if (!ver.ContainsKey("instanceType") || string.IsNullOrWhiteSpace(ver["instanceType"]?.ToString()))
                        {
                            ver["instanceType"] = "StudyDefinitionDocumentVersion";
                            warnings.Add(new CompilerWarning(
                                Field: "documentedBy.versions.instanceType",
                                GeneratedValue: "StudyDefinitionDocumentVersion",
                                WarningReason: "Missing required field 'documentedBy.versions.instanceType'; auto-populated fallback instance type."
                            ));
                        }

                        if (ver.TryGetPropertyValue("contents", out var cntNode) && cntNode is JsonArray cntArray)
                        {
                            for (int cntIdx = 0; cntIdx < cntArray.Count; cntIdx++)
                            {
                                if (cntArray[cntIdx] is not JsonObject content) continue;

                                if (!content.ContainsKey("id") || string.IsNullOrWhiteSpace(content["id"]?.ToString()))
                                {
                                    string fallbackCntId = $"00000000-0000-0000-0000-00000000400{docIdx}{verIdx}{cntIdx}";
                                    content["id"] = fallbackCntId;
                                    warnings.Add(new CompilerWarning(
                                        Field: "documentedBy.versions.contents.id",
                                        GeneratedValue: fallbackCntId,
                                        WarningReason: "Missing required field 'documentedBy.versions.contents.id'; auto-populated fallback UUID."
                                    ));
                                }

                                if (!content.ContainsKey("displaySectionNumber") || string.IsNullOrWhiteSpace(content["displaySectionNumber"]?.ToString()))
                                {
                                    string fallbackSecNum = $"{cntIdx + 1}";
                                    content["displaySectionNumber"] = fallbackSecNum;
                                    warnings.Add(new CompilerWarning(
                                        Field: "documentedBy.versions.contents.displaySectionNumber",
                                        GeneratedValue: fallbackSecNum,
                                        WarningReason: "Missing required field 'documentedBy.versions.contents.displaySectionNumber'; auto-populated fallback section number."
                                    ));
                                }

                                if (!content.ContainsKey("displaySectionTitle") || string.IsNullOrWhiteSpace(content["displaySectionTitle"]?.ToString()))
                                {
                                    content["displaySectionTitle"] = "Section Title";
                                    warnings.Add(new CompilerWarning(
                                        Field: "documentedBy.versions.contents.displaySectionTitle",
                                        GeneratedValue: "Section Title",
                                        WarningReason: "Missing required field 'documentedBy.versions.contents.displaySectionTitle'; auto-populated fallback section title."
                                    ));
                                }

                                if (!content.ContainsKey("instanceType") || string.IsNullOrWhiteSpace(content["instanceType"]?.ToString()))
                                {
                                    content["instanceType"] = "NarrativeContent";
                                    warnings.Add(new CompilerWarning(
                                        Field: "documentedBy.versions.contents.instanceType",
                                        GeneratedValue: "NarrativeContent",
                                        WarningReason: "Missing required field 'documentedBy.versions.contents.instanceType'; auto-populated fallback instance type."
                                    ));
                                }
                            }
                        }
                    }
                }
            }
        }

        // Check duplicate element IDs
        var idMap = new Dictionary<string, List<string>>();
        CollectIds(vPayload, "", idMap);

        var criticalErrors = new List<string>();
        foreach (var (eid, paths) in idMap)
        {
            if (paths.Count > 1)
            {
                criticalErrors.Add($"multiple_elements: Duplicate physical ID '{eid}' detected across: {string.Join(", ", paths)}.");
            }
        }

        if (string.IsNullOrWhiteSpace(vPayload["id"]?.ToString()))
        {
            criticalErrors.Add("id: Study must contain a non-empty physical ID.");
        }

        if (string.IsNullOrWhiteSpace(vPayload["name"]?.ToString()))
        {
            criticalErrors.Add("name: Study must contain a non-empty physical name/title.");
        }

        if (criticalErrors.Count > 0 && _strictValidation)
        {
            throw new ArgumentException($"Validation Failed: {string.Join("; ", criticalErrors)}");
        }

        return (vPayload, warnings);
    }

    private static void CollectIds(JsonNode? node, string currentPath, Dictionary<string, List<string>> idMap)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("id", out var idProp) && idProp != null && !string.IsNullOrWhiteSpace(idProp.ToString()))
            {
                string eid = idProp.ToString();
                if (!idMap.TryGetValue(eid, out var list))
                {
                    list = new List<string>();
                    idMap[eid] = list;
                }
                list.Add(string.IsNullOrEmpty(currentPath) ? "root" : currentPath);
            }

            foreach (var kv in obj)
            {
                if (kv.Key != "id")
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? kv.Key : $"{currentPath}.{kv.Key}";
                    CollectIds(kv.Value, newPath, idMap);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                string newPath = $"{currentPath}[{i}]";
                CollectIds(arr[i], newPath, idMap);
            }
        }
    }
}
