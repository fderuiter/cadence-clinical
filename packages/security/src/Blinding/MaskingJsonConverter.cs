using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cadence.Security.Rbac;
using Microsoft.AspNetCore.Http;

namespace Cadence.Security.Blinding;

/// <summary>
/// System.Text.Json converter that automatically applies role-aware field masking during JSON serialization.
/// @req:PRD-SYS-053
/// </summary>
public class MaskingJsonConverterFactory : JsonConverterFactory
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MaskingJsonConverterFactory(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return !typeToConvert.IsPrimitive && typeToConvert != typeof(string) && typeToConvert != typeof(DateTime) && typeToConvert != typeof(Guid);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Type converterType = typeof(MaskingJsonConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType, _httpContextAccessor);
    }
}

public class MaskingJsonConverter<T> : JsonConverter<T>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MaskingJsonConverter(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var principal = _httpContextAccessor.HttpContext?.Items["Principal"] as Principal;
        object? masked = FieldMasker.MaskPayload(value, principal);

        JsonSerializer.Serialize(writer, masked, options);
    }
}
