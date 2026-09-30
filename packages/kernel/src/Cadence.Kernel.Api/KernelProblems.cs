using Cadence.Kernel.References;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Kernel.Api;

/// <summary>The problem responses the kernel read endpoints share (RFC 9457).</summary>
internal static class KernelProblems
{
    internal static ProblemHttpResult BadRequest(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    internal static ProblemHttpResult InvalidNodeRef(string text) =>
        BadRequest(
            "Invalid NodeRef",
            $"'{text}' is not a NodeRef: expected kind:id or kind:id@version with a registered kind and a lower-case UUID.");

    internal static ProblemHttpResult WrongKind(NodeRef node, string expected) =>
        BadRequest("Wrong kind", $"{node} is a {node.Kind}; this endpoint takes a {expected}.");

    internal static ProblemHttpResult NotFound(NodeRef node) =>
        TypedResults.Problem(title: "Not found", detail: $"{node} does not exist in your tenant.", statusCode: StatusCodes.Status404NotFound);
}
