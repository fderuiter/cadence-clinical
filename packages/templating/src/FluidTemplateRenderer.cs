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

    private readonly Lock _optionsLock = new();
    private readonly ConcurrentDictionary<Type, byte> _registeredTypes = new();
    private readonly TemplateOptions? _initialOptions;
    private volatile TemplateOptions _options;

    public FluidTemplateRenderer(TemplateOptions? options = null)
    {
        _initialOptions = options;
        foreach (var standardType in new[] { typeof(DateTime), typeof(DateTimeOffset), typeof(Guid) })
        {
            _registeredTypes.TryAdd(standardType, 0);
        }

        var initialDiscovered = new List<Type>();
        foreach (var preRegistered in PreRegisteredTypes)
        {
            CollectReachableTypes(preRegistered, initialDiscovered);
        }

        foreach (var candidate in initialDiscovered)
        {
            _registeredTypes.TryAdd(candidate, 0);
        }

        _options = options ?? BuildOptions();
    }

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType<T>()
    {
        RegisterType(typeof(T));
    }

    /// <summary>
    /// Registers a DTO type with Fluid's MemberAccessStrategy for safe member access.
    /// </summary>
    public static void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        PreRegisteredTypes.Add(type);
        if (DefaultInstance.IsValueCreated)
        {
            DefaultInstance.Value.EnsureTypeRegistered(type);
        }
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

    private TemplateOptions BuildOptions()
    {
        var builder = _initialOptions is not null
            ? _initialOptions.ToBuilder()
            : new TemplateOptionsBuilder()
                .AddFilter("format_date", FormatDateFilter)
                .AddFilter("xml_escape", XmlEscapeFilter)
                .AddFilter("escape_xml", XmlEscapeFilter);

        return builder
            .ConfigureMemberAccess(strategy =>
            {
                foreach (var type in _registeredTypes.Keys)
                {
                    RegisterTypeMembers(strategy, type);
                }
            })
            .Build();
    }

    private void EnsureTypeRegistered(Type type)
    {
        if (type == null || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return;
        if (_registeredTypes.ContainsKey(type)) return;

        var discovered = new List<Type>();
        CollectReachableTypes(type, discovered);
        if (discovered.Count == 0) return;

        lock (_optionsLock)
        {
            var pending = discovered.Where(candidate => _registeredTypes.TryAdd(candidate, 0)).ToList();
            if (pending.Count == 0) return;

            _options = BuildOptions();
        }
    }

    private void CollectReachableTypes(Type type, List<Type> discovered)
    {
        var visited = new HashSet<Type>();
        void Visit(Type candidate)
        {
            if (candidate.IsPrimitive || candidate == typeof(string) || candidate == typeof(decimal) || !visited.Add(candidate))
            {
                return;
            }

            if (!_registeredTypes.ContainsKey(candidate))
            {
                discovered.Add(candidate);
            }

            var propertyTypes = candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
            foreach (var propertyType in propertyTypes)
            {
                foreach (var nested in new[] { propertyType, ElementType(propertyType) })
                {
                    if (nested is not null && IsModelType(nested))
                    {
                        Visit(nested);
                    }
                }
            }
        }

        Visit(type);
    }

    private static void RegisterTypeMembers(MemberAccessStrategy strategy, Type type)
    {
        if (type == null || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

            var accessor = new PropertyInfoAccessor(prop);
            RegisterMemberAliases(strategy, type, prop.Name, accessor);
        }

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var accessor = new FieldInfoAccessor(field);
            RegisterMemberAliases(strategy, type, field.Name, accessor);
        }
    }

    private static void RegisterMemberAliases(MemberAccessStrategy strategy, Type type, string memberName, MemberAccessor accessor)
    {
        strategy.Register(type, memberName, accessor);

        string snakeName = ToSnakeCase(memberName);
        if (!string.Equals(snakeName, memberName, StringComparison.Ordinal))
        {
            strategy.Register(type, snakeName, accessor);
        }

        string camelName = ToCamelCase(memberName);
        if (!string.Equals(camelName, memberName, StringComparison.Ordinal)
            && !string.Equals(camelName, snakeName, StringComparison.Ordinal))
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
        EnsureModelTypesRegistered(model);

        var options = _options;
        var context = new TemplateContext(options, options.ModelNamesComparer);

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

    private void EnsureModelTypesRegistered(object? model)
    {
        if (model is null or string) return;

        if (model is IDictionary<string, object?> dict)
        {
            foreach (var value in dict.Values)
            {
                EnsureModelTypesRegistered(value);
            }
            return;
        }

        if (model is IDictionary genericDict)
        {
            foreach (DictionaryEntry entry in genericDict)
            {
                EnsureModelTypesRegistered(entry.Value);
            }
            return;
        }

        var type = model.GetType();
        EnsureTypeRegistered(type);

        if (model is IEnumerable enumerable)
        {
            foreach (var item in enumerable.OfType<object>())
            {
                EnsureTypeRegistered(item.GetType());
            }
            return;
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            var val = prop.GetValue(model);
            if (val is IEnumerable childEnumerable and not string and not IDictionary)
            {
                foreach (var item in childEnumerable.OfType<object>())
                {
                    EnsureTypeRegistered(item.GetType());
                }
            }
            else if (val is not null)
            {
                EnsureTypeRegistered(val.GetType());
            }
        }
    }

    private static void SetContextValue(TemplateContext context, string name, object? value)
    {
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
        if (input.IsNil()) return ValueTask.FromResult(StringValue.Create(string.Empty));

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
        if (input.IsNil()) return ValueTask.FromResult(StringValue.Create(string.Empty));
        string val = input.ToStringValue(context);
        if (string.IsNullOrEmpty(val)) return ValueTask.FromResult(StringValue.Create(string.Empty));

        string escaped = val
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

        // Already escaped, so the template's encoder must not escape it a second time.
        return ValueTask.FromResult(StringValue.Create(escaped, false));
    }

    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || !char.IsUpper(str[0])) return str;
        return string.Create(str.Length, str, static (chars, source) =>
        {
            source.AsSpan().CopyTo(chars);
            chars[0] = char.ToLowerInvariant(chars[0]);
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
