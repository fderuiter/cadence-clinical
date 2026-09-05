using System.Linq;
using System.Text.Json;

namespace Cadence.Apps.Execution.Application.Serialization;

/// <summary>
/// Registers this service's shared System.Text.Json wire conventions on a given
/// <see cref="JsonSerializerOptions"/> instance: snake_case property names (`01_spec.md` §1.2, e.g.
/// <c>subject_id</c>) via <see cref="JsonNamingPolicy.SnakeCaseLower"/>, plus plain-scalar
/// (de)serialization for the strongly-typed ID/code value objects via the converters in
/// `ValueObjectJsonConverters.cs`. Called once from `Program.cs`'s
/// <c>ConfigureHttpJsonOptions</c> (MIG-SUBJ-008, `02_technical_design.md` §2.6) — deliberately
/// idempotent (does not overwrite an already-set naming policy, and bails out before adding a second
/// copy of the converters) so a later ticket wiring another <c>ConfigureHttpJsonOptions</c> call in the
/// same `Program.cs` cannot cause this to double-register on the shared options instance.
/// @req:PRD-SUB-001
/// </summary>
public static class ExecutionJsonSerialization
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy ??= JsonNamingPolicy.SnakeCaseLower;

        if (options.Converters.Any(c => c is SubjectIdJsonConverter))
        {
            return; // already configured on this options instance.
        }

        options.Converters.Add(new SubjectIdJsonConverter());
        options.Converters.Add(new SubjectConsentIdJsonConverter());
        options.Converters.Add(new SubjectCodeJsonConverter());
        options.Converters.Add(new StudyCodeJsonConverter());
        options.Converters.Add(new SiteCodeJsonConverter());
    }
}
