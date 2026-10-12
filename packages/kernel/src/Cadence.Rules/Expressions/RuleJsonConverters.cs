using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Rules.Packs;

namespace Cadence.Rules.Expressions;

/// <summary>
/// System.Text.Json converter for <see cref="RuleExpression"/>.
/// </summary>
public sealed class RuleExpressionJsonConverter : JsonConverter<RuleExpression>
{
    /// <inheritdoc/>
    public override RuleExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        return RuleExpressionJson.Parse(doc.RootElement);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuleExpression value, JsonSerializerOptions options)
    {
        RuleExpressionJson.Write(writer, value);
    }
}

/// <summary>
/// System.Text.Json converter for <see cref="RulePack"/>.
/// </summary>
public sealed class RulePackJsonConverter : JsonConverter<RulePack>
{
    /// <inheritdoc/>
    public override RulePack? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        return RulePackJson.Parse(doc.RootElement);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RulePack value, JsonSerializerOptions options)
    {
        var element = RulePackJson.ToJson(value);
        element.WriteTo(writer);
    }
}

/// <summary>
/// System.Text.Json converter for <see cref="RuleValue"/>.
/// </summary>
public sealed class RuleValueJsonConverter : JsonConverter<RuleValue>
{
    /// <inheritdoc/>
    public override RuleValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => RuleValue.True,
            JsonTokenType.False => RuleValue.False,
            JsonTokenType.Number => RuleValue.FromNumber(reader.GetDecimal()),
            JsonTokenType.String => RuleValue.FromText(reader.GetString()),
            _ => RuleValue.Null,
        };
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuleValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case RuleValueKind.Null:
                writer.WriteNullValue();
                break;
            case RuleValueKind.Boolean:
                writer.WriteBooleanValue(value.IsTrue);
                break;
            case RuleValueKind.Number:
                value.TryGetNumber(out var num);
                writer.WriteNumberValue(num);
                break;
            case RuleValueKind.Text:
                value.TryGetText(out var txt);
                writer.WriteStringValue(txt);
                break;
        }
    }
}
