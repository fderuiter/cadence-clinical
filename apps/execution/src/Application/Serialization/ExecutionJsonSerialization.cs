using System.Linq;
using System.Text.Json;

namespace Cadence.Apps.Execution.Application.Serialization;

/// <summary>
/// Registers this service's shared System.Text.Json wire conventions on a given
/// <see cref="JsonSerializerOptions"/> instance: snake_case property names (`01_spec.md` §1.2, e.g.
/// <c>subject_id</c>) via <see cref="JsonNamingPolicy.SnakeCaseLower"/>, plus plain-scalar
/// (de)serialization for the strongly-typed ID/code value objects via the converters in
/// `ValueObjectJsonConverters.cs`. Called once from `Program.cs`'s
/// <c>ConfigureHttpJsonOptions</c> (MIG-SUBJ-008, `02_technical_design.md` §2.6). The naming policy is
/// assigned unconditionally, not via <c>??=</c>: <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/>
/// constructs its <see cref="JsonSerializerOptions"/> with <see cref="JsonSerializerDefaults.Web"/>, which
/// already sets a non-null <see cref="JsonNamingPolicy.CamelCase"/> policy — a <c>??=</c> guard here would
/// silently never fire on the one real caller it exists for, leaving every response camelCase
/// (<c>subjectId</c>) instead of the spec-mandated snake_case (found and fixed while implementing
/// MIG-SUBJ-023, the first ticket to exercise a genuine end-to-end HTTP round trip against this options
/// instance). Idempotent only where it needs to be: bails out before adding a second copy of the
/// converters, so a later <c>ConfigureHttpJsonOptions</c> call on the same shared options instance cannot
/// double-register them.
/// @req:PRD-SUB-001
/// </summary>
public static class ExecutionJsonSerialization
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;

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
