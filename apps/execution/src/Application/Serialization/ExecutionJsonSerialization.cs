using System.Linq;
using System.Text.Json;

namespace Cadence.Apps.Execution.Application.Serialization;

/// <summary>
/// Registers this service's shared System.Text.Json wire conventions on a given
/// <see cref="JsonSerializerOptions"/> instance: snake_case property names via <see cref="JsonNamingPolicy.SnakeCaseLower"/>,
/// plus plain-scalar (de)serialization for the strongly-typed ID/code value objects and status enums via the converters in
/// `ValueObjectJsonConverters.cs`.
/// @req:PRD-SUB-001
/// @req:PRD-SYS-001
/// @req:PRD-SYS-007
/// @req:SRS-EDC-001
/// @req:SRS-EDC-002
/// @req:SRS-EDC-004
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

        options.Converters.Add(new ObservationIdJsonConverter());
        options.Converters.Add(new DomainCodeJsonConverter());
        options.Converters.Add(new TestCodeJsonConverter());
        options.Converters.Add(new FormSubmissionIdJsonConverter());
        options.Converters.Add(new FormIdJsonConverter());
        options.Converters.Add(new QueryIdJsonConverter());
        options.Converters.Add(new FormSubmissionStatusJsonConverter());
        options.Converters.Add(new QueryStatusJsonConverter());
    }
}
