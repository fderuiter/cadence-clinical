using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Modules.Execution.Endpoints;

/// <summary>
/// CRA source data verification modification alerts (<c>PRD-QRY-006</c>): lists observations whose previously verified
/// revision was superseded by a correction and whose latest revision has not yet been re-verified against source.
/// </summary>
internal static class FormSdvAlertEndpoints
{
    private const int MaxStudyCodeLength = 128;

    public static IEndpointRouteBuilder MapFormSdvAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(FormEndpoints.RoutePrefix)
            .RequireSignedInCaller("Answers only when the caller may see the requested study or enrollment and filters out blinded observations without unblinding authority.")
            .AddEndpointFilter<KernelTenantFilter>()
            .WithTags("Execution queries");

        group.MapGet("/sdv-alerts", ListSdvAlertsAsync)
            .WithName("ExecutionListFormSdvAlerts")
            .WithSummary("Lists active CRA SDV modification alerts for a study or enrollment, omitting observations once monitors verify the latest revision.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static async Task<Results<Ok<IReadOnlyList<FormSdvAlertResponse>>, ProblemHttpResult>> ListSdvAlertsAsync(
        [FromQuery(Name = "study_id")] Guid? studyId,
        [FromQuery(Name = "study_code")] string? studyCode,
        [FromQuery(Name = "enrollment_id")] Guid? enrollmentId,
        HttpContext context,
        IStudyDirectory studies,
        IKernelReadAccess access,
        IKernelNavigator navigator,
        IFormSdvAlertQueryService queryService,
        CancellationToken cancellationToken)
    {
        bool hasStudyId = studyId is { } sGuid && sGuid != Guid.Empty;
        bool hasEnrollment = enrollmentId is { } eGuid && eGuid != Guid.Empty;
        string? normalizedStudyCode = studyCode?.Trim();
        bool hasStudyCode = normalizedStudyCode is not null;

        if ((studyId.HasValue && !hasStudyId)
            || (enrollmentId.HasValue && !hasEnrollment)
            || (hasStudyCode && (normalizedStudyCode!.Length == 0
                || normalizedStudyCode.Length > MaxStudyCodeLength
                || normalizedStudyCode.Any(char.IsControl))))
        {
            return TypedResults.Problem(
                title: "Invalid query",
                detail: "Specify study_id, study_code, or enrollment_id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!hasStudyId && !hasStudyCode && !hasEnrollment)
        {
            return TypedResults.Problem(
                title: "Invalid query",
                detail: "Specify study_id, study_code, or enrollment_id.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var tenant = KernelRequestTenant.Get(context);
        StudyId? targetStudy = hasStudyId ? StudyId.FromGuid(studyId!.Value) : null;
        string? knownStudyCode = null;

        if (hasStudyCode)
        {
            var resolvedByCode = await studies.FindStudyByCodeAsync(tenant, normalizedStudyCode!, cancellationToken).ConfigureAwait(false);
            if (resolvedByCode is null)
            {
                return TypedResults.Problem(
                    title: "Not found",
                    detail: $"Study '{normalizedStudyCode}' does not exist.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (targetStudy is { } explicitStudy && explicitStudy != resolvedByCode.StudyId)
            {
                return TypedResults.Problem(
                    title: "Invalid query",
                    detail: "study_id and study_code refer to different studies.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            targetStudy = resolvedByCode.StudyId;
            knownStudyCode = resolvedByCode.StudyCode;
        }

        Dictionary<EnrollmentId, SdvAlertEnrollmentContext>? studyEnrollmentHeaders = null;
        EnrollmentId? targetEnrollment = hasEnrollment ? EnrollmentId.FromGuid(enrollmentId!.Value) : null;

        if (targetStudy is { } study)
        {
            var studyEnrollments = await navigator.GetStudyEnrollmentsAsync(tenant, study, cancellationToken).ConfigureAwait(false);
            if (studyEnrollments is null)
            {
                return TypedResults.Problem(
                    title: "Not found",
                    detail: $"Study {study.Value:D} does not exist.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var studyNode = NodeRef.For(study);
            List<NodeRef> nodesToCheck = [studyNode, .. studyEnrollments.Select(e => NodeRef.For(e.EnrollmentId))];
            var visibleNodes = await access.FilterVisibleAsync(tenant, nodesToCheck, cancellationToken).ConfigureAwait(false);
            if (!visibleNodes.Contains(studyNode))
            {
                return NotVisible($"study {study.Value:D}");
            }

            studyEnrollmentHeaders = new Dictionary<EnrollmentId, SdvAlertEnrollmentContext>();
            foreach (var member in studyEnrollments)
            {
                if (!visibleNodes.Contains(NodeRef.For(member.EnrollmentId)))
                {
                    continue;
                }

                if (targetEnrollment is { } onlyEnrollment && member.EnrollmentId != onlyEnrollment)
                {
                    continue;
                }

                studyEnrollmentHeaders[member.EnrollmentId] = new SdvAlertEnrollmentContext(
                    member.StudySiteId,
                    member.SiteCode,
                    member.SubjectCode);
            }
        }

        if (targetEnrollment is { } enrollment)
        {
            var enrollmentNode = NodeRef.For(enrollment);
            var visibleEnrollmentNodes = await access.FilterVisibleAsync(tenant, [enrollmentNode], cancellationToken).ConfigureAwait(false);
            if (!visibleEnrollmentNodes.Contains(enrollmentNode))
            {
                return NotVisible($"enrollment {enrollment.Value:D}");
            }

            if (studyEnrollmentHeaders is not null && !studyEnrollmentHeaders.ContainsKey(enrollment))
            {
                return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([]);
            }
        }

        var views = await queryService.QueryActiveAlertsAsync(
            tenant,
            targetStudy,
            targetEnrollment,
            knownStudyCode,
            studyEnrollmentHeaders,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok<IReadOnlyList<FormSdvAlertResponse>>([.. views.Select(FormSdvAlertResponse.From)]);
    }

    private static ProblemHttpResult NotVisible(string what) =>
        TypedResults.Problem(
            title: "Not visible",
            detail: $"You may not see {what}.",
            statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>
/// HTTP response DTO for an active CRA source data verification modification alert (<c>PRD-QRY-006</c>, <c>K-XS-1</c>).
/// </summary>
public sealed record FormSdvAlertResponse : StudySdvAlertPayload
{
    /// <summary>Initializes an empty response instance for JSON deserialization.</summary>
    public FormSdvAlertResponse()
    {
    }

    [SetsRequiredMembers]
    private FormSdvAlertResponse(StudySdvAlertView view)
        : base(view)
    {
    }

    internal static FormSdvAlertResponse From(StudySdvAlertView view) => new(view);
}
