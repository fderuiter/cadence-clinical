using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Cadence.Kernel.Commands;
using Cadence.Kernel.Explanations;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi;

namespace Cadence.Kernel.Modules;

/// <summary>
/// Endpoint metadata naming the registered kernel errors an endpoint answers with (K-ERR-1, #5366). Added by
/// <see cref="KernelErrorEndpoints.ProducesKernelErrors"/>; the OpenAPI document lists the same codes.
/// </summary>
public sealed class KernelErrorsMetadata
{
    internal KernelErrorsMetadata(IReadOnlyList<KernelError> errors) => Errors = errors;

    /// <summary>The declared errors, ordered by status and then code.</summary>
    public IReadOnlyList<KernelError> Errors { get; }
}

/// <summary>
/// How kernel and module endpoints declare and return kernel errors, so every error a client can see is a registered
/// code in the endpoint's OpenAPI contract (K-ERR-1, #5366).
/// </summary>
public static class KernelErrorEndpoints
{
    /// <summary>The OpenAPI extension on an operation that lists its kernel errors.</summary>
    public const string OpenApiExtension = "x-kernel-errors";

    /// <summary>
    /// Declares an endpoint that sends <typeparamref name="TCommand"/>: the errors the pipeline returns for any command
    /// (<see cref="KernelErrors.CommandPipeline"/>) plus those the command declares with
    /// <see cref="RaisesKernelErrorsAttribute"/>. Throws when the command declares none, so a command endpoint can't
    /// publish an incomplete contract.
    /// </summary>
    public static RouteHandlerBuilder ProducesKernelCommand<TCommand>(this RouteHandlerBuilder builder)
        where TCommand : ICommand
    {
        var declared = RaisesKernelErrorsAttribute.Of(typeof(TCommand))
            ?? throw new InvalidOperationException(
                $"{typeof(TCommand).Name} declares no errors; add [RaisesKernelErrors] before mapping it to an endpoint.");
        return builder.ProducesKernelErrors([.. KernelErrors.CommandPipeline, .. declared]);
    }

    /// <summary>
    /// Declares the registered kernel errors an endpoint answers with: a problem response for each of their statuses,
    /// <see cref="KernelErrorsMetadata"/>, and the codes in the OpenAPI operation (the <see cref="OpenApiExtension"/>
    /// extension and each response's description).
    /// </summary>
    public static RouteHandlerBuilder ProducesKernelErrors(this RouteHandlerBuilder builder, params KernelError[] errors)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(errors);

        var declared = Declare(errors);
        builder.WithMetadata(new KernelErrorsMetadata(declared));
        foreach (var status in declared.Select(e => e.HttpStatus).Distinct())
        {
            builder.ProducesProblem(status);
        }

        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            Describe(operation, declared);
            return Task.CompletedTask;
        });
        return builder;
    }

    /// <summary>
    /// A kernel error as RFC 9457 problem details: its status, its stable code as the title and as the <c>code</c>
    /// member, its message as the detail, and the invalid fields as the <c>errors</c> member when validation failed.
    /// </summary>
    public static ProblemHttpResult Problem(KernelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error.Errors is { } fields)
        {
            extensions["errors"] = fields;
        }

        if (error.Explanation is { } explanation)
        {
            extensions["explanation"] = ProblemExplanation.From(explanation);
        }

        return TypedResults.Problem(title: error.Code, detail: error.Message, statusCode: error.HttpStatus, extensions: extensions);
    }

    private sealed record ProblemExplanation(
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("producer")] string Producer,
        [property: JsonPropertyName("steps")] IReadOnlyList<ProblemExplanationStep> Steps,
        [property: JsonPropertyName("conditions")] IReadOnlyList<ProblemCondition> Conditions,
        [property: JsonPropertyName("next_actions")] IReadOnlyList<ProblemNextAction> NextActions)
    {
        public static ProblemExplanation From(Explanation explanation) =>
            new(
                explanation.Subject?.ToString(),
                explanation.Claim,
                explanation.Producer,
                [.. explanation.Steps.Select(s => new ProblemExplanationStep(s.Claim, [.. s.Evidence.Select(r => r.ToString())]))],
                [.. explanation.Conditions.Select(c => new ProblemCondition(c.Code, c.Description, c.Met, [.. c.Evidence.Select(r => r.ToString())]))],
                [.. explanation.NextActions.Select(a => new ProblemNextAction(a.Description, a.Capability, a.Target?.ToString()))]);
    }

    private sealed record ProblemExplanationStep(
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);

    private sealed record ProblemCondition(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("met")] bool Met,
        [property: JsonPropertyName("evidence")] IReadOnlyList<string> Evidence);

    private sealed record ProblemNextAction(
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("capability")] string? Capability,
        [property: JsonPropertyName("target")] string? Target);

    private static KernelError[] Declare(IEnumerable<KernelError> errors)
    {
        var declared = new Dictionary<string, KernelError>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            ArgumentNullException.ThrowIfNull(error);
            if (declared.TryGetValue(error.Code, out var existing) && existing.HttpStatus != error.HttpStatus)
            {
                throw new ArgumentException($"{error.Code} is declared with two statuses, {existing.HttpStatus} and {error.HttpStatus}.", nameof(errors));
            }

            declared[error.Code] = error;
        }

        return [.. declared.Values.OrderBy(e => e.HttpStatus).ThenBy(e => e.Code, StringComparer.Ordinal)];
    }

    private static void Describe(OpenApiOperation operation, IReadOnlyList<KernelError> errors)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions[OpenApiExtension] = new JsonNodeExtension(new JsonArray(
            [.. errors.Select(e => (JsonNode)new JsonObject { ["code"] = e.Code, ["status"] = e.HttpStatus })]));

        if (operation.Responses is not { } responses)
        {
            return;
        }

        foreach (var status in errors.GroupBy(e => e.HttpStatus))
        {
            var key = status.Key.ToString(CultureInfo.InvariantCulture);
            if (responses.TryGetValue(key, out var response) && response is OpenApiResponse concrete)
            {
                var codes = string.Join(", ", status.Select(e => $"`{e.Code}`"));
                concrete.Description = string.IsNullOrWhiteSpace(concrete.Description)
                    ? $"Kernel errors: {codes}."
                    : $"{concrete.Description.TrimEnd('.')}. Kernel errors: {codes}.";
            }
        }
    }
}
