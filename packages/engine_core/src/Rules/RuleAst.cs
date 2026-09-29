using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cadence.Core.Domain.Rules;

public record FieldReference(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("form_id")] string? FormId = null,
    [property: JsonPropertyName("visit_id")] string? VisitId = null,
    [property: JsonPropertyName("visit_relative")] string? VisitRelative = null
);

public class ExpressionNode
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty; // "constant", "variable", "field_ref", "logical", "comparison", "arithmetic", "function"
    [JsonPropertyName("operator")] public string? Operator { get; set; }
    [JsonPropertyName("operands")] public List<ExpressionNode>? Operands { get; set; }
    [JsonPropertyName("value")] public object? Value { get; set; }
    [JsonPropertyName("field_ref")] public FieldReference? FieldRef { get; set; }
    [JsonPropertyName("variable_name")] public string? VariableName { get; set; }
}

public class RuleDefinition
{
    [JsonPropertyName("rule_id")] public string RuleId { get; set; } = string.Empty;
    [JsonPropertyName("study_id")] public string StudyId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("rule_type")] public string RuleType { get; set; } = string.Empty;
    [JsonPropertyName("condition")] public ExpressionNode Condition { get; set; } = new();
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("target_field")] public string? TargetField { get; set; }
    [JsonPropertyName("target_form")] public string? TargetForm { get; set; }
    [JsonPropertyName("target_group")] public string? TargetGroup { get; set; }
    [JsonPropertyName("query_message")] public string? QueryMessage { get; set; }
    [JsonPropertyName("version_index")] public int VersionIndex { get; set; } = 1;
    [JsonPropertyName("sha256_checksum")] public string? Sha256Checksum { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; } = true;
    [JsonPropertyName("is_deleted")] public bool IsDeleted { get; set; } = false;
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonPropertyName("created_by")] public string CreatedBy { get; set; } = string.Empty;
    [JsonPropertyName("reason_for_change")] public string ReasonForChange { get; set; } = "Initial rule authoring";

    public void ComputeChecksum()
    {
        if (string.IsNullOrEmpty(Sha256Checksum))
        {
            var payload = new JsonObject
            {
                ["rule_id"] = RuleId,
                ["study_id"] = StudyId,
                ["rule_type"] = RuleType,
                ["condition"] = JsonSerializer.SerializeToNode(Condition, RuleJsonSerializerContext.Default.ExpressionNode),
                ["action"] = Action,
                ["target_field"] = TargetField,
                ["target_form"] = TargetForm,
                ["query_message"] = QueryMessage,
                ["version_index"] = VersionIndex
            };
            string raw = payload.ToJsonString();
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            Sha256Checksum = Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

/// <summary>
/// Source-generated serialization metadata for rule payloads, so checksums can be computed
/// without reflection in the Native AOT CLI.
/// </summary>
[JsonSerializable(typeof(ExpressionNode))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class RuleJsonSerializerContext : JsonSerializerContext;
