using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cadence.Templating;

/// <summary>
/// A module's closed allow-list of renderable templates: each public key maps to exactly one Liquid
/// template embedded in the module's own assembly. Callers can only name a key; they can never name a
/// resource, reach another assembly, or traverse paths.
/// </summary>
public sealed class TemplateCatalog
{
    private readonly Dictionary<string, string> _resourceByKey;

    /// <summary>
    /// Builds the catalog and resolves every template now, so a missing or ambiguous template fails at
    /// startup rather than on a request.
    /// </summary>
    /// <param name="assembly">The module assembly that embeds the templates. No other assembly is searched.</param>
    /// <param name="templates">Public key to embedded template file name (e.g. <c>confirmation_letter.liquid</c>).</param>
    public TemplateCatalog(Assembly assembly, IReadOnlyDictionary<string, string> templates)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(templates);

        Assembly = assembly;
        string[] resources = assembly.GetManifestResourceNames();
        _resourceByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, fileName) in templates)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

            string[] matches = resources
                .Where(r => r.Equals(fileName, StringComparison.Ordinal) || r.EndsWith("." + fileName, StringComparison.Ordinal))
                .ToArray();

            _resourceByKey[key] = matches.Length switch
            {
                1 => matches[0],
                0 => throw new InvalidOperationException(
                    $"Template '{fileName}' (key '{key}') is not embedded in assembly '{assembly.GetName().Name}'."),
                _ => throw new InvalidOperationException(
                    $"Template '{fileName}' (key '{key}') matches several resources in assembly '{assembly.GetName().Name}': {string.Join(", ", matches)}."),
            };
        }
    }

    /// <summary>The assembly the templates are loaded from.</summary>
    public Assembly Assembly { get; }

    /// <summary>The allowed keys.</summary>
    public IReadOnlyCollection<string> Keys => _resourceByKey.Keys;

    /// <summary>Returns the manifest resource name for an allowed key. Keys are matched exactly (ordinal).</summary>
    public bool TryGetResourceName(string? key, out string resourceName)
    {
        if (key is not null && _resourceByKey.TryGetValue(key, out var found))
        {
            resourceName = found;
            return true;
        }

        resourceName = string.Empty;
        return false;
    }

    /// <summary>Returns true when <paramref name="key"/> is in the allow-list.</summary>
    public bool Contains(string? key) => key is not null && _resourceByKey.ContainsKey(key);
}

/// <summary>Thrown when a caller asks for a template key the module's catalog does not allow.</summary>
public sealed class TemplateNotFoundException(string key)
    : KeyNotFoundException($"Template '{key}' is not an allowed template for this module.")
{
    public string Key { get; } = key;
}
