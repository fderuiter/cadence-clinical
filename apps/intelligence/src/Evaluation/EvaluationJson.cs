using System.Text.Json;
using System.Text.Json.Serialization;

using Cadence.Modules.Intelligence.Evaluation.Judging;
using Cadence.Modules.Intelligence.Evaluation.Runs;
using Cadence.Modules.Intelligence.Evaluation.Sets;

namespace Cadence.Modules.Intelligence.Evaluation;

/// <summary>
/// The JSON form of evaluation sets, rubrics, calibrations and run records (ADR-2634): camelCase properties, kebab-case
/// enum values, and unknown properties refused so a typo in a reviewed file fails loudly instead of being ignored.
/// </summary>
public static class EvaluationJson
{
    /// <summary>The options every evaluation file and record is read and written with.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = EvaluationJsonContext.Default,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(EvaluationSet))]
[JsonSerializable(typeof(Rubric))]
[JsonSerializable(typeof(JudgeCalibration))]
[JsonSerializable(typeof(EvaluationRunRecord))]
[JsonSerializable(typeof(EvaluationRunSummary))]
internal sealed partial class EvaluationJsonContext : JsonSerializerContext;
