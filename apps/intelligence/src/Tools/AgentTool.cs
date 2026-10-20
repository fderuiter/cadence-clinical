using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>
/// One governed read an agent may call (ADR-2633 §4.3). A tool reads kernel abstractions as its
/// <see cref="AgentToolContext.Principal"/>, masks every object through <see cref="IAgentVisibility"/>, records what
/// it showed in the run's <see cref="RunReadSet"/>, and never sends a command.
/// </summary>
public interface IAgentTool
{
    /// <summary>The name the model calls it by (<see cref="KernelToolNames"/>).</summary>
    string Name { get; }

    /// <summary>What it does, for the model.</summary>
    string Description { get; }

    /// <summary>The JSON schema of its arguments.</summary>
    JsonElement InputSchema { get; }

    /// <summary>Runs the tool with the model's <paramref name="arguments"/>.</summary>
    ValueTask<ToolOutcome> InvokeAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>What a tool call has to work with: the principal it reads as and the run's read set.</summary>
public sealed record AgentToolContext(AgentPrincipal Principal, RunReadSet ReadSet);

/// <summary>
/// A tool's result before it is wrapped as untrusted data (<see cref="ToolResultEnvelope"/>): either data or an error
/// with a stable code the model can act on.
/// </summary>
public sealed record ToolOutcome
{
    private ToolOutcome(JsonNode? data, string? errorCode, string? errorMessage)
    {
        Data = data;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>The result, when the call succeeded.</summary>
    public JsonNode? Data { get; }

    /// <summary>The error code (<see cref="ToolErrors"/>), when it failed.</summary>
    public string? ErrorCode { get; }

    /// <summary>The error, in a sentence.</summary>
    public string? ErrorMessage { get; }

    /// <summary>True when the call succeeded.</summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>A successful call returning <paramref name="data"/>.</summary>
    public static ToolOutcome Success(JsonNode? data) => new(data, null, null);

    /// <summary>A refused or failed call with an error code from <see cref="ToolErrors"/>.</summary>
    public static ToolOutcome Failure(string code, string message) => new(null, code, message);
}

/// <summary>The error codes a tool returns. They never say whether a hidden object exists.</summary>
public static class ToolErrors
{
    /// <summary>The arguments do not match the schema.</summary>
    public const string InvalidArguments = "invalid_arguments";

    /// <summary>The object does not exist or the principal may not see it; the two are deliberately one answer.</summary>
    public const string NotFoundOrNotVisible = "not_found_or_not_visible";

    /// <summary>The tool is not on the run's allow-list (ADR-2633 §4.4).</summary>
    public const string NotAllowed = "tool_not_allowed";

    /// <summary>The host does not provide this read yet (ADR-2633 §4.9).</summary>
    public const string NotAvailable = "not_available";

    /// <summary>The kernel refused the read, for example an invalid simulation candidate.</summary>
    public const string Refused = "refused";

    /// <summary>The object has no explanation producer the tool knows.</summary>
    public const string NotExplainable = "not_explainable";

    /// <summary>The one message for an object that is missing or hidden, so the two cannot be told apart.</summary>
    public const string NotFoundOrNotVisibleMessage = "The object does not exist or you may not see it.";
}

/// <summary>
/// A tool with typed arguments and result, serialized through source-generated metadata
/// (<see cref="IntelligenceToolsJsonContext"/>).
/// </summary>
public abstract class AgentTool<TInput, TOutput> : IAgentTool
    where TInput : class
    where TOutput : class
{
    private readonly JsonTypeInfo<TInput> inputType;
    private readonly JsonTypeInfo<TOutput> outputType;

    /// <summary>Creates the tool with its argument and result metadata and its JSON Schema.</summary>
    /// <param name="inputType">Source-generated metadata for the arguments.</param>
    /// <param name="outputType">Source-generated metadata for the result.</param>
    /// <param name="inputSchema">The JSON Schema of the arguments, shown to the model.</param>
    protected AgentTool(JsonTypeInfo<TInput> inputType, JsonTypeInfo<TOutput> outputType, string inputSchema)
    {
        this.inputType = inputType;
        this.outputType = outputType;
        InputSchema = JsonElement.Parse(inputSchema);
    }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract string Description { get; }

    /// <inheritdoc/>
    public JsonElement InputSchema { get; }

    /// <inheritdoc/>
    public async ValueTask<ToolOutcome> InvokeAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        TInput? input;
        try
        {
            input = arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? JsonSerializer.Deserialize("{}", inputType)
                : arguments.Deserialize(inputType);
        }
        catch (JsonException exception)
        {
            return ToolOutcome.Failure(ToolErrors.InvalidArguments, $"The arguments do not match the schema: {exception.Message}");
        }

        if (input is null)
        {
            return ToolOutcome.Failure(ToolErrors.InvalidArguments, "Arguments are required.");
        }

        if (Validate(input) is { } problem)
        {
            return ToolOutcome.Failure(ToolErrors.InvalidArguments, problem);
        }

        var result = await ExecuteAsync(context, input, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? ToolOutcome.Success(JsonSerializer.SerializeToNode(result.Value!, outputType))
            : ToolOutcome.Failure(result.ErrorCode!, result.ErrorMessage!);
    }

    /// <summary>A sentence saying what is wrong with <paramref name="input"/>, or null when it is valid.</summary>
    protected virtual string? Validate(TInput input) => null;

    /// <summary>Runs the tool on valid arguments as <see cref="AgentToolContext.Principal"/>.</summary>
    /// <param name="context">The principal and the run's read set.</param>
    /// <param name="input">The validated arguments.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    protected abstract ValueTask<ToolResult<TOutput>> ExecuteAsync(AgentToolContext context, TInput input, CancellationToken cancellationToken);
}

/// <summary>A typed tool result.</summary>
public readonly record struct ToolResult<T>(T? Value, string? ErrorCode, string? ErrorMessage)
{
    /// <summary>True when the call succeeded.</summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>A successful result.</summary>
    public static implicit operator ToolResult<T>(T value) => new(value, null, null);

    /// <summary>A refused or failed result with an error code from <see cref="ToolErrors"/>.</summary>
    public static ToolResult<T> Failure(string code, string message) => new(default, code, message);

    /// <summary>The result for an object that is missing or hidden.</summary>
    public static ToolResult<T> NotFoundOrNotVisible() => Failure(ToolErrors.NotFoundOrNotVisible, ToolErrors.NotFoundOrNotVisibleMessage);
}
