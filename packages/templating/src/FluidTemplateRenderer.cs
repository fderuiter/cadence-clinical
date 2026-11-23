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

    private readonly TemplateOptions _options;

    public FluidTemplateRenderer(TemplateOptions? options = null)
    {
        _options = options ?? CreateDefaultOptions();
    }

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType<T>()
    {
        PreRegisteredTypes.Add(typeof(T));
        RegisterTypeMembers(Instance._options.MemberAccessStrategy, typeof(T));
    }

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        PreRegisteredTypes.Add(type);
        RegisterTypeMembers(Instance._options.MemberAccessStrategy, type);
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
        return RenderAsync(catalog, key, model).GetAwaiter().GetResult();
    }

    private static TemplateOptions CreateDefaultOptions()
    {
        var strategy = new DynamicMemberAccessStrategy();

        // Configure member access strategy for standard types
        RegisterTypeMembers(strategy, typeof(DateTime));
        RegisterTypeMembers(strategy, typeof(DateTimeOffset));
        RegisterTypeMembers(strategy, typeof(Guid));

        foreach (var type in PreRegisteredTypes)
        {
            RegisterTypeMembers(strategy, type);
        }

        return new TemplateOptionsBuilder()
            .WithMemberAccessStrategy(() => strategy)
            .AddFilter("format_date", FormatDateFilter)
            .AddFilter("xml_escape", XmlEscapeFilter)
            .AddFilter("escape_xml", XmlEscapeFilter)
            .Build();
    }

    private static void RegisterTypeMembers(MemberAccessStrategy strategy, Type type)
    {
        if (type == null || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return;

        if (strategy is DynamicMemberAccessStrategy dynamicStrategy)
        {
            dynamicStrategy.EnsureRegistered(type);
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            RegisterMemberAliases(strategy, type, prop.Name, new PropertyInfoAccessor(prop));
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            RegisterMemberAliases(strategy, type, field.Name, new FieldInfoAccessor(field));
        }
    }

    private static void RegisterMemberAliases(MemberAccessStrategy strategy, Type type, string memberName, MemberAccessor accessor)
    {
        strategy.Register(type, memberName, accessor);

        string snake = ToSnakeCase(memberName);
        if (!string.Equals(snake, memberName, StringComparison.Ordinal))
        {
            strategy.Register(type, snake, accessor);
        }

        string camel = ToCamelCase(memberName);
        if (!string.Equals(camel, memberName, StringComparison.Ordinal)
            && !string.Equals(camel, snake, StringComparison.Ordinal))
        {
            strategy.Register(type, camel, accessor);
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
            RegisterTypeMembers(_options.MemberAccessStrategy, type);

            string typeName = type.Name.ToLowerInvariant();
            context.SetValue(typeName, model);
            context.SetValue("model", model);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanRead)
                {
                    var val = prop.GetValue(model);
                    SetContextValue(context, prop.Name, val);
                    string snakeName = ToSnakeCase(prop.Name);
                    if (snakeName != prop.Name)
                    {
                        SetContextValue(context, snakeName, val);
                    }
                }
            }
        }

        return context;
    }

    private void SetContextValue(TemplateContext context, string name, object? value)
    {
        if (value != null)
        {
            var valueType = value.GetType();
            RegisterTypeMembers(_options.MemberAccessStrategy, valueType);
            RegisterPropertyTypes(valueType, [valueType]);

            if (value is IEnumerable enumerable && !(value is string) && !(value is IDictionary))
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        RegisterTypeMembers(_options.MemberAccessStrategy, item.GetType());
                    }
                }
            }
        }

        if (value is null)
        {
            context.SetValue(name, NilValue.Instance);
        }
        else
        {
            context.SetValue(name, value);
        }

        string snake = ToSnakeCase(name);
        if (snake != name)
        {
            if (value is null)
            {
                context.SetValue(snake, NilValue.Instance);
            }
            else
            {
                context.SetValue(snake, value);
            }
        }
    }

    // Registers the model types a value reaches through its public properties (nested records and the element
    // types of their lists), so a template can read `icsr.patient.patient_id` without each host registering them.
    private void RegisterPropertyTypes(Type type, HashSet<Type> visited)
    {
        var propertyTypes = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        foreach (var propertyType in propertyTypes)
        {
            foreach (var candidate in new[] { propertyType, ElementType(propertyType) })
            {
                if (candidate is null || !IsModelType(candidate) || !visited.Add(candidate))
                {
                    continue;
                }

                RegisterTypeMembers(_options.MemberAccessStrategy, candidate);
                RegisterPropertyTypes(candidate, visited);
            }
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
        if (input.IsNil()) return ValueTask.FromResult<FluidValue>(StringValue.Empty);

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
            return ValueTask.FromResult(input);
        }

        string format = "yyyy-MM-dd";
        if (arguments.Count > 0)
        {
            format = arguments.At(0).ToStringValue(context);
        }

        return ValueTask.FromResult(StringValue.Create(dt.ToString(format, CultureInfo.InvariantCulture)));
    }

    private static ValueTask<FluidValue> XmlEscapeFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil()) return ValueTask.FromResult<FluidValue>(StringValue.Empty);
        string val = input.ToStringValue(context);
        if (string.IsNullOrEmpty(val)) return ValueTask.FromResult<FluidValue>(StringValue.Empty);

        string escaped = val
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

        // Already escaped, so the template's encoder must not escape it a second time.
        return ValueTask.FromResult(StringValue.Create(escaped, encode: false));
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
            span[0] = char.ToLowerInvariant(source[0]);
        });
    }

    private sealed class DynamicMemberAccessStrategy : MemberAccessStrategy
    {
        private readonly ConcurrentDictionary<Type, byte> _registeredTypes = new();
        private readonly ConcurrentDictionary<(Type Type, string Name), MemberAccessor> _accessors = new();

        public override MemberAccessor GetAccessor(Type type, string name, StringComparer stringComparer)
        {
            if (_accessors.TryGetValue((type, name), out var accessor))
            {
                return accessor;
            }

            foreach (var candidateType in EnumerateHierarchy(type))
            {
                if (_accessors.TryGetValue((candidateType, name), out accessor))
                {
                    return accessor;
                }
            }

            if (!Equals(stringComparer, StringComparer.Ordinal))
            {
                foreach (var ((registeredType, registeredName), registeredAccessor) in _accessors)
                {
                    if (registeredType.IsAssignableFrom(type) && stringComparer.Equals(registeredName, name))
                    {
                        return registeredAccessor;
                    }
                }
            }

            return null!;
        }

        public override void Register(Type type, string name, MemberAccessor accessor)
        {
            _accessors[(type, name)] = accessor;
        }

        public void EnsureRegistered(Type type)
        {
            if (!_registeredTypes.TryAdd(type, 0))
            {
                return;
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                RegisterMemberAliases(this, type, prop.Name, new PropertyInfoAccessor(prop));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                RegisterMemberAliases(this, type, field.Name, new FieldInfoAccessor(field));
            }
        }

        private static IEnumerable<Type> EnumerateHierarchy(Type type)
        {
            for (Type? current = type.BaseType; current is not null && current != typeof(object); current = current.BaseType)
            {
                yield return current;
            }

            foreach (var iface in type.GetInterfaces())
            {
                yield return iface;
            }
        }
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
