using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Cadence.Telemetry;

/// <summary>
/// Emits OpenAPI <c>string</c> schemas for Kernel strongly-typed identifier and code value structs
/// (<c>TenantId</c>, <c>StudyId</c>, <c>GrantId</c>, <c>RoleId</c>, <c>QualificationId</c>, <c>NodeRef</c>,
/// <c>KernelStudyCode</c>, <c>KernelSiteCode</c>, etc.) whose custom <see cref="JsonConverterAttribute"/> converters
/// serialize as JSON strings on the wire.
/// </summary>
public sealed class KernelWireTypesOpenApiTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        Type? nullableUnderlying = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type);
        Type type = nullableUnderlying ?? context.JsonTypeInfo.Type;

        if (!type.IsEnum
            && type.IsValueType
            && type.Namespace is not null
            && type.Namespace.StartsWith("Cadence.Kernel", StringComparison.Ordinal)
            && Attribute.IsDefined(type, typeof(JsonConverterAttribute), inherit: false))
        {
            bool isNullable = nullableUnderlying is not null
                || (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null));
            schema.Type = isNullable
                ? JsonSchemaType.String | JsonSchemaType.Null
                : JsonSchemaType.String;
            schema.Format = null;
            schema.Properties?.Clear();
            schema.Required?.Clear();
        }

        return Task.CompletedTask;
    }
}
