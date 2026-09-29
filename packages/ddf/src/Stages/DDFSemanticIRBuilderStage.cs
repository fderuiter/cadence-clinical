using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.Ddf.Rules;

namespace Cadence.Ddf.Stages;

[SuppressMessage("SonarAnalyzer.CSharp", "S101:Types should name in PascalCase", Justification = "DDF acronym naming convention")]
public class DDFSemanticIRBuilderStage
{
    [SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Pipeline stage instance method")]
    public DDFStudyIR BuildIr(string studyId, JsonNode payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var obj = payload.AsObject();

        var docs = new List<JsonObject>();
        CollectDocs(obj, docs);

        var rawItems = new List<JsonObject>();
        CollectRawItems(obj, rawItems);

        string studyName = GetStudyName(obj);
        string version = GetStudyVersion(obj);

        var processedItems = new List<DDFItemIR>();

        foreach (var itemObj in rawItems)
        {
            string origId = itemObj.TryGetPropertyValue("id", out var idProp) ? idProp?.ToString() ?? "" : "";
            string itemId = XPathCompiler.SanitizeIdentifier(origId);
            string itemName = itemObj.TryGetPropertyValue("name", out var nameProp) ? nameProp?.ToString() ?? "Unknown Field" : "Unknown Field";
            string itemType = itemObj.TryGetPropertyValue("type", out var typeProp) ? typeProp?.ToString() ?? "string" : "string";

            var itemDict = JsonNodeToDictionary(itemObj);
            string? appearance = XPathCompiler.ExtractAppearance(itemDict);

            var relevants = new List<string>();
            var constraints = new List<string>();
            var constraintMessages = new List<string>();

            if (itemObj.TryGetPropertyValue("rules", out var rulesNode) && rulesNode is JsonArray rulesArr)
            {
                foreach (var rNode in rulesArr)
                {
                    if (rNode is not JsonObject rObj) continue;
                    string rType = rObj.TryGetPropertyValue("type", out var rt) ? rt?.ToString() ?? "" : "";
                    if (!rObj.TryGetPropertyValue("condition", out var condNode) || condNode is null) continue;

                    string compiledXpath = XPathCompiler.CompileConditionToXPath(condNode);
                    if (string.IsNullOrEmpty(compiledXpath)) continue;

                    if (rType == "skip_logic")
                    {
                        string action = rObj.TryGetPropertyValue("action", out var act) ? act?.ToString() ?? "show" : "show";
                        string expr = action == "hide" ? $"not({compiledXpath})" : compiledXpath;
                        relevants.Add(expr);
                    }
                    else if (rType == "constraint")
                    {
                        constraints.Add(compiledXpath);
                        if (rObj.TryGetPropertyValue("query_message", out var msgNode) && msgNode != null)
                        {
                            constraintMessages.Add(msgNode.ToString());
                        }
                    }
                }
            }

            string? relevantExpr = relevants.Count > 0
                ? string.Join(" and ", relevants.Select(r => relevants.Count > 1 ? $"({r})" : r))
                : null;

            string? constraintExpr = constraints.Count > 0
                ? string.Join(" and ", constraints.Select(c => constraints.Count > 1 ? $"({c})" : c))
                : null;

            string? constraintMsg = constraintMessages.Count > 0 ? string.Join("; ", constraintMessages) : null;

            var itemLocalizations = BuildLocalizations(docs, obj, itemId, itemName, origId);

            processedItems.Add(new DDFItemIR(
                Id: itemId,
                Name: itemName,
                Type: itemType,
                Appearance: appearance,
                Relevant: relevantExpr,
                Constraint: constraintExpr,
                ConstraintMessage: constraintMsg,
                Localizations: itemLocalizations
            ));
        }

        return new DDFStudyIR(
            StudyId: studyId,
            Name: studyName,
            Items: processedItems,
            Version: version
        );
    }

    private static string GetStudyName(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("name", out var n) && n != null)
        {
            return n.ToString();
        }

        if (obj.TryGetPropertyValue("study", out var st) && st is JsonObject stObj && stObj.TryGetPropertyValue("name", out var sn) && sn != null)
        {
            return sn.ToString();
        }

        return "Clinical Trial Protocol";
    }

    private static string GetStudyVersion(JsonObject obj)
    {
        if (obj.TryGetPropertyValue("version", out var v) && v != null)
        {
            return v.ToString();
        }

        if (obj.TryGetPropertyValue("study", out var st) && st is JsonObject stObj && stObj.TryGetPropertyValue("version", out var sv) && sv != null)
        {
            return sv.ToString();
        }

        return "1.0";
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> BuildLocalizations(
        List<JsonObject> docs, JsonObject root, string itemId, string itemName, string origId)
    {
        var itemLocalizations = new Dictionary<string, IReadOnlyDictionary<string, object?>>();

        foreach (var doc in docs)
        {
            string langCode = ExtractLanguageCode(doc);

            var narrativeContents = new List<JsonObject>();
            var narrativeItems = new Dictionary<string, JsonObject>();

            ExtractDocumentNarratives(doc, narrativeContents, narrativeItems);
            CollectGlobalNarrativeItems(root, narrativeItems);

            var associatedNcs = new List<JsonObject>();
            foreach (var nc in narrativeContents)
            {
                string ncId = nc.TryGetPropertyValue("id", out var idP) ? idP?.ToString() ?? "" : "";
                string ncName = nc.TryGetPropertyValue("name", out var nameP) ? nameP?.ToString() ?? "" : "";

                if (ncName.Equals(origId, StringComparison.OrdinalIgnoreCase) ||
                    ncName.Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
                    ncId.Equals(origId, StringComparison.OrdinalIgnoreCase) ||
                    ncId.Equals(itemId, StringComparison.OrdinalIgnoreCase) ||
                    ncId.Equals($"nc_{origId}", StringComparison.OrdinalIgnoreCase) ||
                    ncName.Equals($"nc_{origId}", StringComparison.OrdinalIgnoreCase))
                {
                    associatedNcs.Add(nc);
                }
            }

            string? lbl = null, desc = null, hint = null;
            var hintNcs = new List<JsonObject>();
            var descNcs = new List<JsonObject>();

            foreach (var nc in narrativeContents)
            {
                string ncId = nc.TryGetPropertyValue("id", out var idP) ? idP?.ToString() ?? "" : "";
                string ncName = nc.TryGetPropertyValue("name", out var nameP) ? nameP?.ToString() ?? "" : "";

                if ((ncId.Contains("hint", StringComparison.OrdinalIgnoreCase) || ncName.Contains("hint", StringComparison.OrdinalIgnoreCase)) &&
                    (!string.IsNullOrEmpty(origId) && (ncId.Contains(origId, StringComparison.OrdinalIgnoreCase) || ncName.Contains(origId, StringComparison.OrdinalIgnoreCase))))
                {
                    hintNcs.Add(nc);
                }

                if ((ncId.Contains("desc", StringComparison.OrdinalIgnoreCase) || ncName.Contains("desc", StringComparison.OrdinalIgnoreCase)) &&
                    (!string.IsNullOrEmpty(origId) && (ncId.Contains(origId, StringComparison.OrdinalIgnoreCase) || ncName.Contains(origId, StringComparison.OrdinalIgnoreCase))))
                {
                    descNcs.Add(nc);
                }
            }

            foreach (var nc in associatedNcs)
            {
                if (nc.TryGetPropertyValue("sectionTitle", out var stVal) && stVal != null)
                {
                    lbl = stVal.ToString();
                }

                if (nc.TryGetPropertyValue("contentItemId", out var ciVal) && ciVal != null && narrativeItems.TryGetValue(ciVal.ToString(), out var itemObj2) &&
                    itemObj2.TryGetPropertyValue("text", out var tVal) && tVal != null)
                {
                    desc = tVal.ToString();
                }

                if (nc.TryGetPropertyValue("childIds", out var childNode) && childNode is JsonArray childArr)
                {
                    foreach (var cIdNode in childArr)
                    {
                        if (cIdNode is null) continue;
                        string childId = cIdNode.ToString();
                        var childNc = narrativeContents.FirstOrDefault(x => x.TryGetPropertyValue("id", out var xId) && xId?.ToString() == childId);
                        if (childNc != null)
                        {
                            string cName = childNc.TryGetPropertyValue("name", out var cn) ? cn?.ToString() ?? "" : "";
                            string cIdStr = childNc.TryGetPropertyValue("id", out var ci) ? ci?.ToString() ?? "" : "";
                            string? cText = null;
                            if (childNc.TryGetPropertyValue("contentItemId", out var ci2) && ci2 != null && narrativeItems.TryGetValue(ci2.ToString(), out var niObj))
                            {
                                cText = niObj.TryGetPropertyValue("text", out var txt) ? txt?.ToString() : null;
                            }
                            string? val = cText ?? (childNc.TryGetPropertyValue("sectionTitle", out var st3) ? st3?.ToString() : null);

                            if (cName.Contains("hint", StringComparison.OrdinalIgnoreCase) || cIdStr.Contains("hint", StringComparison.OrdinalIgnoreCase))
                            {
                                hint = val;
                            }
                            else if (cName.Contains("desc", StringComparison.OrdinalIgnoreCase) || cIdStr.Contains("desc", StringComparison.OrdinalIgnoreCase) || desc == null)
                            {
                                desc = val;
                            }
                        }
                        else if (narrativeItems.TryGetValue(childId, out var cItem))
                        {
                            string cName = cItem.TryGetPropertyValue("name", out var cn2) ? cn2?.ToString() ?? "" : "";
                            string? cText = cItem.TryGetPropertyValue("text", out var txt2) ? txt2?.ToString() : null;

                            if (cName.Contains("hint", StringComparison.OrdinalIgnoreCase) || childId.Contains("hint", StringComparison.OrdinalIgnoreCase))
                            {
                                hint = cText;
                            }
                            else if (cName.Contains("desc", StringComparison.OrdinalIgnoreCase) || childId.Contains("desc", StringComparison.OrdinalIgnoreCase) || desc == null)
                            {
                                desc = cText;
                            }
                        }
                    }
                }
            }

            if (hint == null && hintNcs.Count > 0)
            {
                var firstHintNc = hintNcs[0];
                string? ciId = firstHintNc.TryGetPropertyValue("contentItemId", out var ci3) ? ci3?.ToString() : null;
                if (ciId != null && narrativeItems.TryGetValue(ciId, out var niObj2) && niObj2.TryGetPropertyValue("text", out var t4))
                {
                    hint = t4?.ToString();
                }
                else if (firstHintNc.TryGetPropertyValue("sectionTitle", out var st4))
                {
                    hint = st4?.ToString();
                }
            }

            if (desc == null && descNcs.Count > 0)
            {
                var firstDescNc = descNcs[0];
                string? ciId = firstDescNc.TryGetPropertyValue("contentItemId", out var ci4) ? ci4?.ToString() : null;
                if (ciId != null && narrativeItems.TryGetValue(ciId, out var niObj3) && niObj3.TryGetPropertyValue("text", out var t5))
                {
                    desc = t5?.ToString();
                }
                else if (firstDescNc.TryGetPropertyValue("sectionTitle", out var st5))
                {
                    desc = st5?.ToString();
                }
            }

            lbl ??= itemName;

            if (lbl != null || desc != null || hint != null)
            {
                itemLocalizations[langCode] = new Dictionary<string, object?>
                {
                    ["label"] = lbl,
                    ["description"] = desc,
                    ["hint"] = hint
                };
            }
        }

        if (itemLocalizations.Count == 0)
        {
            itemLocalizations["en"] = new Dictionary<string, object?>
            {
                ["label"] = itemName,
                ["description"] = itemName,
                ["hint"] = null
            };
        }

        return itemLocalizations;
    }

    private static string ExtractLanguageCode(JsonObject doc)
    {
        string langCode = "en";
        if (doc.TryGetPropertyValue("language", out var langNode) && langNode != null)
        {
            if (langNode is JsonObject langObj)
            {
                if (langObj.TryGetPropertyValue("code", out var cNode) && cNode != null)
                {
                    langCode = cNode.ToString();
                }
                else if (langObj.TryGetPropertyValue("decode", out var dNode) && dNode != null)
                {
                    langCode = dNode.ToString();
                }
            }
            else if (langNode is JsonValue)
            {
                langCode = langNode.ToString();
            }
        }
        return langCode.ToLowerInvariant().Trim();
    }

    private static void ExtractDocumentNarratives(JsonObject doc, List<JsonObject> narrativeContents, Dictionary<string, JsonObject> narrativeItems)
    {
        if (doc.TryGetPropertyValue("versions", out var verNode) && verNode is JsonArray verArr)
        {
            foreach (var ver in verArr)
            {
                if (ver is not JsonObject verObj) continue;
                if (verObj.TryGetPropertyValue("contents", out var cntNode) && cntNode is JsonArray cntArr)
                {
                    foreach (var nc in cntArr) if (nc is JsonObject ncObj) narrativeContents.Add(ncObj);
                }
                if (verObj.TryGetPropertyValue("narrativeContentItems", out var nciNode) && nciNode is JsonArray nciArr)
                {
                    foreach (var nci in nciArr)
                    {
                        if (nci is JsonObject nciObj && nciObj.TryGetPropertyValue("id", out var nciId) && nciId != null)
                        {
                            narrativeItems[nciId.ToString()] = nciObj;
                        }
                    }
                }
            }
        }
    }

    private static void CollectDocs(JsonObject root, List<JsonObject> docs)
    {
        if (root.TryGetPropertyValue("documentedBy", out var docNode) && docNode is JsonArray docArr)
        {
            foreach (var d in docArr) if (d is JsonObject dObj) docs.Add(dObj);
        }
        if (root.TryGetPropertyValue("study", out var stNode) && stNode is JsonObject stObj && stObj.TryGetPropertyValue("documentedBy", out var docNode2) && docNode2 is JsonArray docArr2)
        {
            foreach (var d in docArr2) if (d is JsonObject dObj) docs.Add(dObj);
        }
        if (root.TryGetPropertyValue("protocol", out var prNode) && prNode is JsonObject prObj && prObj.TryGetPropertyValue("documentedBy", out var docNode3) && docNode3 is JsonArray docArr3)
        {
            foreach (var d in docArr3) if (d is JsonObject dObj) docs.Add(dObj);
        }
    }

    private static void CollectRawItems(JsonObject root, List<JsonObject> rawItems)
    {
        if (root.TryGetPropertyValue("protocol", out var prNode) && prNode is JsonObject prObj && prObj.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonArray itemsArr)
        {
            foreach (var it in itemsArr) if (it is JsonObject itObj) rawItems.Add(itObj);
        }

        if (rawItems.Count == 0 && root.TryGetPropertyValue("items", out var topItemsNode) && topItemsNode is JsonArray topItemsArr)
        {
            foreach (var it in topItemsArr) if (it is JsonObject itObj) rawItems.Add(itObj);
        }

        if (rawItems.Count == 0 && root.TryGetPropertyValue("activities", out var actNode) && actNode is JsonArray actArr)
        {
            foreach (var act in actArr)
            {
                if (act is JsonObject actObj && actObj.TryGetPropertyValue("items", out var actItemsNode) && actItemsNode is JsonArray actItemsArr)
                {
                    foreach (var it in actItemsArr) if (it is JsonObject itObj) rawItems.Add(itObj);
                }
            }
        }
    }

    private static void CollectGlobalNarrativeItems(JsonObject root, Dictionary<string, JsonObject> narrativeItems)
    {
        if (root.TryGetPropertyValue("versions", out var verNode) && verNode is JsonArray verArr)
        {
            foreach (var ver in verArr)
            {
                if (ver is JsonObject verObj && verObj.TryGetPropertyValue("narrativeContentItems", out var nciNode) && nciNode is JsonArray nciArr)
                {
                    foreach (var nci in nciArr)
                    {
                        if (nci is JsonObject nciObj && nciObj.TryGetPropertyValue("id", out var nciId) && nciId != null)
                        {
                            narrativeItems[nciId.ToString()] = nciObj;
                        }
                    }
                }
            }
        }
    }

    private static Dictionary<string, object?> JsonNodeToDictionary(JsonObject obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kv in obj)
        {
            dict[kv.Key] = kv.Value?.ToString();
        }
        return dict;
    }
}
