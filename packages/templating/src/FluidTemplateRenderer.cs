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
    /// Retained for host compatibility; Fluid 3 resolves public DTO members via <see cref="TemplateOptions.ModelNamesComparer"/>.
    /// </summary>
    public static void RegisterType<T>() => RegisterType(typeof(T));

    /// <summary>
    /// Retained for host compatibility; Fluid 3 resolves public DTO members via <see cref="TemplateOptions.ModelNamesComparer"/>.
    /// </summary>
    public static void RegisterType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
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

    private static TemplateOptions CreateDefaultOptions() =>
        new TemplateOptionsBuilder()
            .WithModelNamesComparer(FlexibleModelMemberComparer.Comparer)
            .AddFilter("format_date", FormatDateFilter)
            .AddFilter("xml_escape", XmlEscapeFilter)
            .AddFilter("escape_xml", XmlEscapeFilter)
            .Build();

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
        var context = new TemplateContext(_options, _options.ModelNamesComparer);

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
                }
            }
        }

        return context;
    }

    private static void SetContextValue(TemplateContext context, string name, object? value)
    {
        SetContextEntry(context, name, value);
        string snake = ToSnakeCase(name);
        if (snake != name)
        {
            SetContextEntry(context, snake, value);
        }
    }

    private static void SetContextEntry(TemplateContext context, string name, object? value)
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

    private static ValueTask<FluidValue> FormatDateFilter(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        if (input.IsNil())
        {
            return ValueTask.FromResult<FluidValue>(StringValue.Empty);
        }

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
        if (input.IsNil())
        {
            return ValueTask.FromResult<FluidValue>(StringValue.Empty);
        }

        string val = input.ToStringValue(context);
        if (string.IsNullOrEmpty(val))
        {
            return ValueTask.FromResult<FluidValue>(StringValue.Empty);
        }

        string escaped = val
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

        // Already escaped, so the template's encoder must not escape it a second time.
        return ValueTask.FromResult(StringValue.Create(escaped, encode: false));
    }

    private sealed class FlexibleModelMemberComparer : StringComparer
    {
        public static readonly FlexibleModelMemberComparer Comparer = new();

        public override int Compare(string? x, string? y) =>
            Ordinal.Compare(Normalize(x), Normalize(y));

        public override bool Equals(string? x, string? y) =>
            Ordinal.Equals(Normalize(x), Normalize(y));

        public override int GetHashCode(string obj) =>
            Ordinal.GetHashCode(Normalize(obj));

        private static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c != '_')
                {
                    sb.Append(char.ToUpperInvariant(c));
                }
            }

            return sb.ToString();
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
