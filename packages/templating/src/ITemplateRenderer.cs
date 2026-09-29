using System.Reflection;
using System.Threading.Tasks;

namespace Cadence.Templating;

/// <summary>
/// Defines the contract for Liquid template rendering across Cadence microservices.
/// Provides sub-millisecond document rendering with GxP member access sandboxing.
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Renders an embedded Liquid template by name asynchronously.
    /// </summary>
    ValueTask<string> RenderAsync(string templateName, object model, Assembly? assembly = null);

    /// <summary>
    /// Renders an embedded Liquid template by name synchronously.
    /// </summary>
    string Render(string templateName, object model, Assembly? assembly = null);
}
