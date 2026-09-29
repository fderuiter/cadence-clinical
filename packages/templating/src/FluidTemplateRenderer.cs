using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Fluid;
using Fluid.Values;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Templating;

/// <summary>
/// Core implementation of <see cref="ITemplateRenderer"/> powered by Fluid.Core Liquid engine.
/// Renders only templates a module's <see cref="TemplateCatalog"/> allows, loaded from that module's own
/// assembly; parsed templates are cached per assembly and resource so modules can never collide.
/// @req:PRD-SYS-053
/// </summary>
public class FluidTemplateRenderer : ITemplateRenderer
{
    private static readonly FluidParser Parser = new();
    private static readonly ConcurrentDictionary<(string Assembly, string Resource), IFluidTemplate> TemplateCache = new();
    private static readonly ConcurrentBag<Type> PreRegisteredTypes = new();
    private static readonly Lazy<FluidTemplateRenderer> DefaultInstance = new(() => new FluidTemplateRenderer());

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
        var template = GetOrParseTemplate(catalog, key);
        var context = CreateContext(model);
        return await template.RenderAsync(context);
    }

    /// <inheritdoc />
    public string Render(TemplateCatalog catalog, string key, object model)
    {
        return RenderAsync(catalog, key, model).GetAwaiter().GetResult();
    }

    private static TemplateOptions CreateDefaultOptions()
    {
        var options = new TemplateOptions();

        // Register custom Liquid filters
        options.Filters.AddFilter("format_date", FormatDateFilter);
        options.Filters.AddFilter("xml_escape", XmlEscapeFilter);
        options.Filters.AddFilter("escape_xml", XmlEscapeFilter);

        // Configure member access strategy for standard types
        RegisterTypeMembers(options.MemberAccessStrategy, typeof(DateTime));
        RegisterTypeMembers(options.MemberAccessStrategy, typeof(DateTimeOffset));
        RegisterTypeMembers(options.MemberAccessStrategy, typeof(Guid));

        foreach (var type in PreRegisteredTypes)
        {
            RegisterTypeMembers(options.MemberAccessStrategy, type);
        }

        return options;
    }

    private static void RegisterTypeMembers(MemberAccessStrategy strategy, Type type)
    {
        if (type == null || type.IsPrimitive || type == typeof(string) || type == typeof(decimal)) return;

        strategy.MemberNameStrategy = MemberNameStrategies.Default;
        strategy.Register(type);

        strategy.MemberNameStrategy = MemberNameStrategies.SnakeCase;
        strategy.Register(type);

        strategy.MemberNameStrategy = MemberNameStrategies.CamelCase;
        strategy.Register(type);
    }

    private static IFluidTemplate GetOrParseTemplate(TemplateCatalog catalog, string key)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.TryGetResourceName(key, out string resourceName))
        {
            throw new TemplateNotFoundException(key ?? string.Empty);
        }

        string assemblyName = catalog.Assembly.FullName ?? catalog.Assembly.GetName().Name ?? string.Empty;
        return TemplateCache.GetOrAdd((assemblyName, resourceName), _ =>
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

        context.SetValue(name, value);
        string snake = ToSnakeCase(name);
        if (snake != name)
        {
            context.SetValue(snake, value);
        }
    }

    private static ValueTask<FluidValue> FormatDateFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil()) return StringValue.Empty;

        object? value = input.ToObjectValue();
        DateTime dt;

        if (value is DateTime dateTime)
        {
            dt = dateTime;
        }
        else if (value is DateTimeOffset dateTimeOffset)
        {
            dt = dateTimeOffset.LocalDateTime;
        }
        else if (DateTime.TryParse(input.ToStringValue(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            dt = parsed;
        }
        else
        {
            return input;
        }

        string format = "yyyy-MM-dd";
        if (arguments.Count > 0)
        {
            format = arguments.At(0).ToStringValue();
        }

        return new StringValue(dt.ToString(format, CultureInfo.InvariantCulture));
    }

    private static ValueTask<FluidValue> XmlEscapeFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil()) return StringValue.Empty;
        string val = input.ToStringValue();
        if (string.IsNullOrEmpty(val)) return StringValue.Empty;

        string escaped = val
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

        return new StringValue(escaped);
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
