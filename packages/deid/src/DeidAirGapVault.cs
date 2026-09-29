using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Cadence.Deid;

public class DeidAirGapVault : IDisposable
{
    private readonly DeidDetector _detector;
    private readonly Dictionary<string, string> _surrogateToRaw = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rawToSurrogate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _categoryCounters = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public DeidAirGapVault(DeidDetector? detector = null)
    {
        _detector = detector ?? new DeidDetector();
    }

    public bool HasSurrogates => _surrogateToRaw.Count > 0;
    public int SurrogateCount => _surrogateToRaw.Count;

    public string GetOrCreateSurrogate(string rawValue, string category)
    {
        if (_rawToSurrogate.TryGetValue(rawValue, out var existing))
        {
            return existing;
        }

        string catUpper = category.ToUpperInvariant();
        _categoryCounters.TryGetValue(catUpper, out int currentCount);
        int nextIdx = currentCount + 1;
        _categoryCounters[catUpper] = nextIdx;

        string surrogate = $"[SURROGATE_{catUpper}_{nextIdx}]";
        _rawToSurrogate[rawValue] = surrogate;
        _surrogateToRaw[surrogate] = rawValue;
        return surrogate;
    }

    public string DeidentifyText(string text, ComplianceProfile profile = ComplianceProfile.HIPAA, List<string>? customTerms = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var detections = _detector.Detect(text, profile, customTerms);
        var clean = DeidDetector.ResolveOverlaps(detections, text);
        if (clean.Count == 0)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text);
        foreach (var res in clean.OrderByDescending(x => x.Start))
        {
            string surrogate = GetOrCreateSurrogate(res.Value, res.Category);
            sb.Remove(res.Start, res.End - res.Start);
            sb.Insert(res.Start, surrogate);
        }

        return sb.ToString();
    }

    public object? DeidentifyStructuredData(object? data, ComplianceProfile profile = ComplianceProfile.HIPAA, List<string>? customTerms = null)
    {
        if (data == null)
        {
            return null;
        }

        if (data is string strData)
        {
            string trimmed = strData.Trim();
            if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
            {
                try
                {
                    var node = JsonNode.Parse(strData);
                    if (node != null)
                    {
                        var deidentifiedNode = DeidentifyJsonNode(node, profile, customTerms);
                        return deidentifiedNode?.ToJsonString() ?? strData;
                    }
                }
                catch
                {
                    // Fall back to text deidentification
                }
            }
            return DeidentifyText(strData, profile, customTerms);
        }

        if (data is JsonNode jsonNode)
        {
            return DeidentifyJsonNode(jsonNode, profile, customTerms);
        }

        if (data is IDictionary<string, object?> dict)
        {
            var resultDict = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);
            foreach (var kvp in dict)
            {
                resultDict[kvp.Key] = DeidentifyStructuredData(kvp.Value, profile, customTerms);
            }
            return resultDict;
        }

        if (data is IDictionary nonGenericDict)
        {
            var resultDict = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                string key = entry.Key.ToString() ?? string.Empty;
                resultDict[key] = DeidentifyStructuredData(entry.Value, profile, customTerms);
            }
            return resultDict;
        }

        if (data is IEnumerable list && data is not string)
        {
            var resultList = new List<object?>();
            foreach (var item in list)
            {
                resultList.Add(DeidentifyStructuredData(item, profile, customTerms));
            }
            return resultList;
        }

        return data;
    }

    private JsonNode? DeidentifyJsonNode(JsonNode node, ComplianceProfile profile, List<string>? customTerms)
    {
        if (node is JsonObject obj)
        {
            var newObj = new JsonObject();
            foreach (var prop in obj)
            {
                var val = prop.Value;
                newObj.Add(prop.Key, DeidentifyJsonNode(val?.DeepClone()!, profile, customTerms));
            }
            return newObj;
        }

        if (node is JsonArray arr)
        {
            var newArr = new JsonArray();
            foreach (var item in arr)
            {
                newArr.Add(DeidentifyJsonNode(item?.DeepClone()!, profile, customTerms));
            }
            return newArr;
        }

        if (node is JsonValue valNode && valNode.TryGetValue<string>(out var strVal))
        {
            return JsonValue.Create(DeidentifyText(strVal, profile, customTerms));
        }

        return node?.DeepClone();
    }

    public List<T> DeidentifyMessages<T>(IEnumerable<T> messages, ComplianceProfile profile = ComplianceProfile.HIPAA, List<string>? customTerms = null) where T : class
    {
        var result = new List<T>();
        if (messages == null)
        {
            return result;
        }

        foreach (var msg in messages)
        {
            if (msg is IDictionary<string, object?> dictMsg)
            {
                var copy = new Dictionary<string, object?>(dictMsg, StringComparer.Ordinal);
                if (copy.TryGetValue("content", out var content) && content != null)
                {
                    copy["content"] = DeidentifyStructuredData(content, profile, customTerms);
                }
                if (copy.TryGetValue("tool_calls", out var toolCalls) && toolCalls is IEnumerable tcs)
                {
                    var newTcs = new List<object?>();
                    foreach (var tc in tcs)
                    {
                        if (tc is IDictionary<string, object?> tcDict && tcDict.TryGetValue("function", out var fnObj) && fnObj is IDictionary<string, object?> fnDict)
                        {
                            var tcCopy = new Dictionary<string, object?>(tcDict, StringComparer.Ordinal);
                            var fnCopy = new Dictionary<string, object?>(fnDict, StringComparer.Ordinal);
                            if (fnCopy.TryGetValue("arguments", out var args))
                            {
                                fnCopy["arguments"] = DeidentifyStructuredData(args, profile, customTerms);
                            }
                            tcCopy["function"] = fnCopy;
                            newTcs.Add(tcCopy);
                        }
                        else
                        {
                            newTcs.Add(DeidentifyStructuredData(tc, profile, customTerms));
                        }
                    }
                    copy["tool_calls"] = newTcs;
                }
                result.Add((T)(object)copy);
            }
            else if (msg != null)
            {
                var deidentifiedObj = DeidentifyStructuredData(msg, profile, customTerms);
                result.Add((T)deidentifiedObj!);
            }
        }

        return result;
    }

    public string RehydrateText(string text)
    {
        if (string.IsNullOrEmpty(text) || _surrogateToRaw.Count == 0)
        {
            return text;
        }

        string result = text;
        var sortedPairs = _surrogateToRaw.OrderByDescending(x => x.Key.Length).ToList();
        foreach (var pair in sortedPairs)
        {
            result = result.Replace(pair.Key, pair.Value);
        }

        return result;
    }

    public object? RehydrateStructuredData(object? data)
    {
        if (data == null || _surrogateToRaw.Count == 0)
        {
            return data;
        }

        if (data is string strData)
        {
            return RehydrateText(strData);
        }

        if (data is JsonNode jsonNode)
        {
            return RehydrateJsonNode(jsonNode);
        }

        if (data is IDictionary<string, object?> dict)
        {
            var resultDict = new Dictionary<string, object?>(dict.Count, StringComparer.Ordinal);
            foreach (var kvp in dict)
            {
                resultDict[kvp.Key] = RehydrateStructuredData(kvp.Value);
            }
            return resultDict;
        }

        if (data is IDictionary nonGenericDict)
        {
            var resultDict = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                string key = entry.Key.ToString() ?? string.Empty;
                resultDict[key] = RehydrateStructuredData(entry.Value);
            }
            return resultDict;
        }

        if (data is IEnumerable list && data is not string)
        {
            var resultList = new List<object?>();
            foreach (var item in list)
            {
                resultList.Add(RehydrateStructuredData(item));
            }
            return resultList;
        }

        return data;
    }

    private JsonNode? RehydrateJsonNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var newObj = new JsonObject();
            foreach (var prop in obj)
            {
                newObj.Add(prop.Key, RehydrateJsonNode(prop.Value?.DeepClone()!));
            }
            return newObj;
        }

        if (node is JsonArray arr)
        {
            var newArr = new JsonArray();
            foreach (var item in arr)
            {
                newArr.Add(RehydrateJsonNode(item?.DeepClone()!));
            }
            return newArr;
        }

        if (node is JsonValue valNode && valNode.TryGetValue<string>(out var strVal))
        {
            return JsonValue.Create(RehydrateText(strVal));
        }

        return node?.DeepClone();
    }

    public void Clear()
    {
        _surrogateToRaw.Clear();
        _rawToSurrogate.Clear();
        _categoryCounters.Clear();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Clear();
            }
            _disposed = true;
        }
    }
}
