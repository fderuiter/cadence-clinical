using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// Form signatures (spec §9.5, 21 CFR 11.50, 11.70, 11.200, #5463): an investigator signs the exact revisions of a form's
/// values. The route needs a fresh, single-use step-up token from the gateway (password re-entry, <c>X-Sig-Token</c>)
/// bound to this form and to <see cref="SignSemanticAction"/>; it then sends the kernel's
/// <see cref="SignCollectionInstance"/>, which records an attestation only when the hash the signer read is still the
/// form's content. Signatures are read with the form (<c>GET /api/v1/execution/forms/{formId}</c>).
/// </summary>
internal static class FormSignatureEndpoints
{
    /// <summary>The semantic action a form signature's step-up token must carry.</summary>
    public const string SignSemanticAction = "execution.form.sign";

    /// <summary>
    /// How the signer re-authenticated, recorded on the attestation: the gateway's step-up token (contract v3), issued
    /// only after the signer re-entered their password (and one-time code where the realm requires one).
    /// </summary>
    public const string StepUpMethod = "gateway-step-up:v3";

    public static IEndpointRouteBuilder MapFormSignatureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Signing is a kernel command authorized against the caller's grants at the form's enrollment, after a step-up re-authentication.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution forms");

        group.MapPost("/{formId:guid}/signatures", SignAsync)
            .AllowExpiredTenantWrite()
            .WithName("ExecutionSignForm")
            .WithSummary("Signs the form's current values with an X-Sig-Token step-up token (collection.sign); 409 when the form changed since it was read.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesKernelCommand<SignCollectionInstance>()
            .RequireElectronicSignature(SignSemanticAction, SignActionFor);

        return endpoints;
    }

    /// <summary>The concrete action a form's signature token is bound to, so a token for one form cannot sign another.</summary>
    public static string SignAction(Guid formId) => $"{FormEndpoints.RoutePrefix}/{formId:D}/signatures";

    internal static async Task<Results<Created<FormSignatureResponse>, ProblemHttpResult>> SignAsync(
        Guid formId,
        SignFormRequest request,
        HttpContext context,
        ICollectionInstanceRecords forms,
        IKernelCommandSender sender,
        IKernelChangeReason changeReason,
        CancellationToken cancellationToken,
        [FromServices] IModuleTenantContextFactory<ExecutionModuleDbContext>? contexts = null,
        [FromServices] IKernelReadAccess? access = null)
    {
        if (request is null
            || !Enum.TryParse<AttestationMeaning>(request.Meaning, ignoreCase: true, out var meaning)
            || !Enum.IsDefined(meaning)
            || string.IsNullOrWhiteSpace(request.ContentHash))
        {
            return BadRequest("Name the signature's meaning (approved or responsibility) and the form's content_hash as you read it.");
        }

        // The filter verified and spent the token; its issue time is when the signer re-authenticated.
        if (context.VerifiedSigToken() is not { } token)
        {
            return ElectronicSignatureRequiredProblem();
        }

        var tenant = KernelRequestTenant.Get(context);
        var id = CollectionInstanceId.FromGuid(formId);

        // The signature is authorized against the form's enrollment, which the client does not send.
        if (await forms.FindAsync(tenant, id, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return TypedResults.Problem(title: "Not found", detail: $"Form {formId:D} does not exist.", statusCode: StatusCodes.Status404NotFound);
        }

        if (record.Locks.Any(l => l.Kind == LockKind.Locked))
        {
            return KernelErrorEndpoints.Problem(AssertionErrors.Locked);
        }

        var trimmedHash = request.ContentHash.Trim();
        if (contexts is not null
            && access is not null
            && record.ContentHash is not null
            && string.Equals(record.ContentHash, trimmedHash, StringComparison.Ordinal)
            && await access.CanExerciseAsync(
                tenant,
                new Capability("collection.sign", VerbClass.Execute),
                NodeRef.For(record.EnrollmentId),
                cancellationToken).ConfigureAwait(false))
        {
            System.Collections.Generic.List<(Guid KernelQueryId, FormQueryStatus Status)> execQueries;
            await using (var db = contexts.Create(tenant))
            {
                var rawQueries = await db.FormQueries.AsNoTracking()
                    .Where(q => q.FormId == id)
                    .Select(q => new { q.KernelQueryId, q.Status })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                execQueries = [.. rawQueries.Select(q => (q.KernelQueryId.Value, q.Status))];
            }

            var (openQueryCount, answeredQueryCount) = ExecutionQueryMerger.Merge(record.Assertions, execQueries);
            if (openQueryCount + answeredQueryCount > 0)
            {
                var missing = record.MissingRequired ?? [];
                var blockers = CollectionReadiness.BuildBlockers(
                    missing,
                    missing.Count,
                    openQueryCount,
                    answeredQueryCount,
                    record.OpenErrorFindingCount);
                var errors = new System.Collections.Generic.Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["active_queries"] = [$"open={openQueryCount}, answered={answeredQueryCount}"],
                };
                if (missing.Count > 0)
                {
                    errors["missing_required"] = [.. missing];
                }

                return KernelErrorEndpoints.Problem(
                    CollectionErrors.NotReadyToSign.WithMessage(string.Join(" ", blockers)) with { Errors = errors });
            }
        }

        var signature = request.SignatureId is { } chosen && chosen != Guid.Empty ? AttestationId.FromGuid(chosen) : AttestationId.New();
        var result = await sender.SendAsync(
            new SignCollectionInstance(
                tenant,
                signature,
                id,
                record.EnrollmentId,
                meaning,
                trimmedHash,
                new DateTimeOffset(token.IssuedAt, TimeSpan.Zero),
                StepUpMethod,
                FormEndpoints.IdempotencyKey(context),
                changeReason.Read(context) ?? string.Empty),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return KernelErrorEndpoints.Problem(error);
        }

        var signed = await forms.FindAsync(tenant, id, cancellationToken).ConfigureAwait(false);
        var view = signed?.Attestations.SingleOrDefault(a => a.AttestationId == result.Value)
            ?? throw new InvalidOperationException($"Signature {result.Value} on form {formId:D} was recorded but cannot be read back.");
        return TypedResults.Created($"{FormEndpoints.RoutePrefix}/{formId:D}", FormSignatureResponse.From(view));
    }

    private static string SignActionFor(HttpContext context) =>
        Guid.TryParse(context.Request.RouteValues["formId"]?.ToString(), CultureInfo.InvariantCulture, out var formId)
            ? SignAction(formId)
            : string.Empty;

    private static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(title: "Invalid signature", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult ElectronicSignatureRequiredProblem() =>
        TypedResults.Problem(
            title: "Re-authentication required",
            detail: SigTokenVerifier.ReauthenticationRequired,
            statusCode: StatusCodes.Status401Unauthorized);
}
