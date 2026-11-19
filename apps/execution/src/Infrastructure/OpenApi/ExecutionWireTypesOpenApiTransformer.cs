using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Kernel;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Cadence.Apps.Execution.Infrastructure.OpenApi;

/// <summary>
/// Describes the Execution value objects in the OpenAPI document the way
/// <c>ExecutionJsonSerialization</c> puts them on the wire: IDs and codes as strings.
/// Their custom JSON converters hide that shape from the schema generator, which otherwise emits an empty
/// schema and leaves typed clients (the web app) with <c>unknown</c>.
/// </summary>
public sealed class ExecutionWireTypesOpenApiTransformer : IOpenApiSchemaTransformer
{
    private static readonly HashSet<Type> StringValueObjects =
    [
        typeof(SubjectId),
        typeof(SubjectConsentId),
        typeof(SubjectCode),
        typeof(StudyCode),
        typeof(SiteCode),
        typeof(ObservationId),
        typeof(TestCode),
    ];

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        Type type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;

        if (StringValueObjects.Contains(type))
        {
            SetString(schema);
        }

        return Task.CompletedTask;
    }

    private static void SetString(OpenApiSchema schema)
    {
        schema.Type = JsonSchemaType.String;
        schema.Format = null;
        schema.Properties?.Clear();
        schema.Required?.Clear();
    }
}
