using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// Exposes one session tool to Microsoft.Extensions.AI. Invocation goes through <see cref="AgentToolSession"/>, so the
/// allow-list, the read set, the call log and the untrusted-data envelope apply to every model call.
/// </summary>
internal sealed class SessionAIFunction(AgentToolSession session, IAgentTool tool) : AIFunction
{
    public override string Name => tool.Name;

    public override string Description => tool.Description;

    public override JsonElement JsonSchema => tool.InputSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            values[name] = value is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value?.GetType() ?? typeof(object)));
        }

        var json = JsonSerializer.SerializeToElement(values, IntelligenceToolsJsonContext.Default.DictionaryStringJsonElement);
        return await session.InvokeAsync(tool.Name, json, cancellationToken).ConfigureAwait(false);
    }
}
