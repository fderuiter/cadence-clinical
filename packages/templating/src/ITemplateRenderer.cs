using System.Threading.Tasks;

namespace Cadence.Templating;

/// <summary>
/// Renders a module's own embedded Liquid templates. A template is addressed only by a key from the
/// module's <see cref="TemplateCatalog"/>; free-form resource names are not accepted.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Renders the template <paramref name="key"/> from <paramref name="catalog"/>.
    /// Throws <see cref="TemplateNotFoundException"/> when the key is not in the catalog.
    /// </summary>
    ValueTask<string> RenderAsync(TemplateCatalog catalog, string key, object model);

    /// <summary>Synchronous form of <see cref="RenderAsync"/>.</summary>
    string Render(TemplateCatalog catalog, string key, object model);
}
