using System.Collections.Generic;

using Cadence.Core.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// The primary <see cref="DomainError"/> -&gt; HTTP mechanism (`02_technical_design.md` §3.4). Application
/// service/endpoint call sites (MIG-SUBJ-018/019 onward) call <see cref="ToProblemResult"/> directly on a
/// failed `Result&lt;T&gt;.Error` rather than hand-rolling a status code or scattering literals across
/// handlers. Always resolves its status via <see cref="DomainErrorHttpMapping.GetStatusCode"/> and stamps the
/// stable, machine-readable <see cref="DomainError.Code"/> onto the RFC 9457 <c>code</c> extension member so
/// API consumers can branch on it without parsing <c>detail</c> free text.
/// @req:PRD-SUB-001
/// </summary>
public static class DomainErrorExtensions
{
    public static ProblemHttpResult ToProblemResult(this DomainError error) =>
        TypedResults.Problem(
            detail: error.Description,
            statusCode: DomainErrorHttpMapping.GetStatusCode(error),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
