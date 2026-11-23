using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

using Fluid;
using Fluid.Accessors;
using Fluid.Values;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Templating;

/// <summary>
/// Core implementation of <see cref="ITemplateRenderer"/> powered by Fluid.Core Liquid engine.
/// Renders only templates a module's <see cref="TemplateCatalog"/> allows, loaded from that module's own
/// assembly; parsed templates are cached per assembly and resource so modules can never collide.
/// Every value a template outputs is encoded for the template's format (#4959): <c>.xml.liquid</c> and
/// <c>.html.liquid</c> templates escape markup characters, other templates (plain text) are written as is.
/// </summary>
public class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private static readonly ConcurrentDictionary<(string Assembly, string Resource), IFluidTemplate> TemplateCache = new();
    private static readonly ConcurrentBag<Type> PreRegisteredTypes = new();
    private static readonly Lazy<FluidTemplateRenderer> DefaultInstance = new(() => new FluidTemplateRenderer());

    // Escapes & < > " ' (and characters the encoder never lets through) as entities or numeric character
    // references, which are valid in both HTML and XML; other Unicode text is written unchanged.
    private static readonly TextEncoder MarkupEncoder = HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Shared thread-safe default instance of <see cref="FluidTemplateRenderer"/>.
    /// </summary>
    public static FluidTemplateRenderer Instance => DefaultInstance.Value;

    private readonly ConcurrentDictionary<Type, byte> _registeredTypes = new();
    private readonly Lock _registrationLock = new();
    private volatile TemplateOptions _options;

    public FluidTemplateRenderer(TemplateOptions? options = null)
    {
        _options = options ?? CreateDefaultOptions(_registeredTypes);
    }

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType<T>() => RegisterType(typeof(T));

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        PreRegisteredTypes.Add(type);
        Instance.EnsureTypesRegistered([type]);
    }

    /// <inheritdoc />
    public async ValueTask<string> RenderAsync(TemplateCatalog catalog, string key, object model)
    {
        var (template, resourceName) = GetOrParseTemplate(catalog, key);
        var context = CreateContext(model);
        return await template.RenderAsync(context, EncoderFor(resourceName));
    }

    /// <summary>
    /// The encoder for a template's output format, chosen from its file name: markup for <c>.xml.liquid</c> and
    /// <c>.html.liquid</c>, none for anything else.
    /// </summary>
    public static TextEncoder EncoderFor(string templateFileName)
    {
        ArgumentNullException.ThrowIfNull(templateFileName);
        return templateFileName.EndsWith(".xml.liquid", StringComparison.OrdinalIgnoreCase)
            || templateFileName.EndsWith(".html.liquid", StringComparison.OrdinalIgnoreCase)
            || templateFileName.EndsWith(".htm.liquid", StringComparison.OrdinalIgnoreCase)
            ? MarkupEncoder
            : NullEncoder.Default;
    }

    /// <inheritdoc />
    public string Render(TemplateCatalog catalog, string key, object model)
    {
        var (template, resourceName) = GetOrParseTemplate(catalog, key);
        var context = CreateContext(model);
        return template.Render(context, EncoderFor(resourceName));
    }

    private static TemplateOptions CreateDefaultOptions(ConcurrentDictionary<Type, byte> registeredTypes)
    {
        Type[] initialTypes = [typeof(DateTime), typeof(DateTimeOffset), typeof(Guid), .. PreRegisteredTypes];
        var typesToRegister = ExpandTypeClosure(initialTypes);

        var builder = new TemplateOptionsBuilder()
            .AddFilter("format_date", FormatDateFilter)
            .AddFilter("xml_escape", XmlEscapeFilter)
            .AddFilter("escape_xml", XmlEscapeFilter)
            .ConfigureMemberAccess(strategy =>
            {
                foreach (var type in typesToRegister)
                {
                    RegisterTypeMembers(strategy, type);
                }
            });

        var options = builder.Build();
        foreach (var type in typesToRegister)
        {
            registeredTypes.TryAdd(type, 0);
        }

        return options;
    }

    private void EnsureTypesRegistered(IEnumerable<Type> rootTypes)
    {
        var candidates = ExpandTypeClosure(rootTypes);
        if (candidates.All(_registeredTypes.ContainsKey))
        {
            return;
        }

        lock (_registrationLock)
        {
            var missing = candidates.Where(t => !_registeredTypes.ContainsKey(t)).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            _options = _options
                .ToBuilder()
                .ConfigureMemberAccess(strategy =>
                {
                    foreach (var type in missing)
                    {
                        RegisterTypeMembers(strategy, type);
                    }
                })
                .Build();

            foreach (var type in missing)
            {
                _registeredTypes.TryAdd(type, 0);
            }
        }
    }

    private static HashSet<Type> ExpandTypeClosure(IEnumerable<Type> roots)
    {
        var visited = new HashSet<Type>();
        foreach (var root in roots)
        {
            if (IsRegistrableType(root) && visited.Add(root))
            {
                CollectPropertyTypes(root, visited);
            }
        }

        return visited;
    }

    // Collects the model types a value reaches through its public properties (nested records and the element
    // types of their lists), so a template can read `icsr.patient.patient_id` without each host registering them.
    private static void CollectPropertyTypes(Type type, HashSet<Type> visited)
    {
        var propertyTypes = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);

        foreach (var propertyType in propertyTypes)
        {
            foreach (var candidate in new[] { propertyType, ElementType(propertyType) })
            {
                if (candidate is null || !IsModelType(candidate) || !visited.Add(candidate))
                {
                    continue;
                }

                CollectPropertyTypes(candidate, visited);
            }
        }
    }

    private static bool IsRegistrableType(Type? type) =>
        type is not null && !type.IsPrimitive && type != typeof(string) && type != typeof(decimal) && type != typeof(object);

    private static void RegisterTypeMembers(MemberAccessStrategy strategy, Type type)
    {
        if (!IsRegistrableType(type))
        {
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var accessor = new PropertyInfoAccessor(prop);
            RegisterMemberNames(strategy, type, prop.Name, accessor);
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var accessor = new FieldInfoAccessor(field);
            RegisterMemberNames(strategy, type, field.Name, accessor);
        }
    }

    private static void RegisterMemberNames(MemberAccessStrategy strategy, Type type, string memberName, MemberAccessor accessor)
    {
        strategy.Register(type, memberName, accessor);

        string snakeName = ToSnakeCase(memberName);
        if (!string.Equals(snakeName, memberName, StringComparison.Ordinal))
        {
            strategy.Register(type, snakeName, accessor);
        }

        string camelName = ToCamelCase(memberName);
        if (!string.Equals(camelName, memberName, StringComparison.Ordinal) &&
            !string.Equals(camelName, snakeName, StringComparison.Ordinal))
        {
            strategy.Register(type, camelName, accessor);
        }
    }

    private static (IFluidTemplate Template, string ResourceName) GetOrParseTemplate(TemplateCatalog catalog, string key)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGetResourceName(key, out string resourceName))
        {
            throw new TemplateNotFoundException(key ?? string.Empty);
        }

        string assemblyName = catalog.Assembly.FullName ?? catalog.Assembly.GetName().Name ?? string.Empty;
        var parsed = TemplateCache.GetOrAdd((assemblyName, resourceName), _ =>
        {
            using var stream = catalog.Assembly.GetManifestResourceStream(resourceName)
                ?? throw new TemplateNotFoundException(key);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string content = reader.ReadToEnd();

            if (!Parser.TryParse(content, out var template, out var error))
            {
                throw new InvalidOperationException($"Failed to parse Liquid template '{key}': {error}");
            }
            return template;
        });
        return (parsed, resourceName);
    }

    private TemplateContext CreateContext(object model)
    {
        RegisterModelGraph(model);
        var context = new TemplateContext(_options);

        if (model is IDictionary<string, object?> dict)
        {
            foreach (var (key, value) in dict)
            {
                SetContextValue(context, key, value);
            }
        }
        else if (model is IDictionary genericDict)
        {
            foreach (DictionaryEntry entry in genericDict)
            {
                if (entry.Key != null)
                {
                    SetContextValue(context, entry.Key.ToString()!, entry.Value);
                }
            }
        }
        else if (model != null)
        {
            var type = model.GetType();
            string typeName = type.Name.ToLowerInvariant();
            context.SetValue(typeName, model);
            context.SetValue("model", model);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                {
                    var val = prop.GetValue(model);
                    SetContextValue(context, prop.Name, val);
                }
            }
        }

        return context;
    }

    private void RegisterModelGraph(object? model)
    {
        if (model is null)
        {
            return;
        }

        var rootTypes = new HashSet<Type>();
        CollectRuntimeTypes(model, rootTypes);
        if (rootTypes.Count > 0)
        {
            EnsureTypesRegistered(rootTypes);
        }
    }

    private static void CollectRuntimeTypes(object? value, HashSet<Type> types)
    {
        if (value is null || value is string)
        {
            return;
        }

        if (value is IDictionary<string, object?> typedDict)
        {
            foreach (var item in typedDict.Values)
            {
                CollectRuntimeTypes(item, types);
            }
            return;
        }

        if (value is IDictionary genericDict)
        {
            foreach (DictionaryEntry entry in genericDict)
            {
                CollectRuntimeTypes(entry.Value, types);
            }
            return;
        }

        var valueType = value.GetType();
        if (IsRegistrableType(valueType))
        {
            types.Add(valueType);
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is not null && item is not string)
                {
                    var itemType = item.GetType();
                    if (IsRegistrableType(itemType))
                    {
                        types.Add(itemType);
                    }
                }
            }
        }
    }

    private static void SetContextValue(TemplateContext context, string name, object? value)
    {
        SetSingleValue(context, name, value);
        string snake = ToSnakeCase(name);
        if (snake != name)
        {
            SetSingleValue(context, snake, value);
        }
    }

    private static void SetSingleValue(TemplateContext context, string name, object? value)
    {
        if (value is null)
        {
            context.SetValue(name, NilValue.Instance);
        }
        else
        {
            context.SetValue(name, value);
        }
    }

    private static Type? ElementType(Type type) =>
        type == typeof(string)
            ? null
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

    private static bool IsModelType(Type type) =>
        type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;

    private static ValueTask<FluidValue> FormatDateFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil()) return new ValueTask<FluidValue>(StringValue.Empty);

        object? value = input.ToObjectValue(context);
        DateTime dt;

        if (value is DateTime dateTime)
        {
            dt = dateTime;
        }
        else if (value is DateTimeOffset dateTimeOffset)
        {
            dt = dateTimeOffset.LocalDateTime;
        }
        else if (DateTime.TryParse(input.ToStringValue(context), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            dt = parsed;
        }
        else
        {
            return new ValueTask<FluidValue>(input);
        }

        string format = "yyyy-MM-dd";
        if (arguments.Count > 0)
        {
            format = arguments.At(0).ToStringValue(context);
        }

        return new ValueTask<FluidValue>(StringValue.Create(dt.ToString(format, CultureInfo.InvariantCulture)));
    }

    private static ValueTask<FluidValue> XmlEscapeFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil()) return new ValueTask<FluidValue>(StringValue.Empty);
        string val = input.ToStringValue(context);
        if (string.IsNullOrEmpty(val)) return new ValueTask<FluidValue>(StringValue.Empty);

        string escaped = val
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

        // Already escaped, so the template's encoder must not escape it a second time.
        return new ValueTask<FluidValue>(StringValue.Create(escaped, encode: false));
    }

    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
        {
            return str;
        }

        return string.Create(str.Length, str, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
    }

    private static string ToSnakeCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var sb = new StringBuilder();
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (char.IsUpper(c))
            {
                if (i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Service collection extension methods for registering Liquid template rendering in ASP.NET Core DI.
/// </summary>
public static class TemplatingServiceCollectionExtensions
{
    public static IServiceCollection AddFluidTemplating(this IServiceCollection services)
    {
        services.AddSingleton<ITemplateRenderer>(FluidTemplateRenderer.Instance);
        return services;
    }
}
