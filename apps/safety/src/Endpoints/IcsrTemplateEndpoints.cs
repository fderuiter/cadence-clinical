using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Infrastructure.Standards;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;
using Cadence.Templating;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Safety.Endpoints;

/// <summary>ICH E2B(R3) ICSR rendering from this service's own template, checked against the ICH schema.</summary>
public static class IcsrTemplateEndpoints
{
    public const string IcsrTemplateKey = "e2b_r3_icsr";

    /// <summary>The permission that rendering an ICSR requires.</summary>
    public const string RenderPermission = "safety_report:create";

    public static TemplateCatalog Catalog { get; } = new(
        typeof(IcsrTemplateEndpoints).Assembly,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [IcsrTemplateKey] = "e2b_r3_icsr.xml.liquid",
        });

    public static RouteGroupBuilder MapIcsrTemplateEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/icsr/render", RenderIcsrAsync)
            .WithName("RenderIcsrReport")
            .WithSummary("Renders an ICH E2B(R3) ICSR as an HL7 v3 MCCI_IN200100UV01 message validated against the ICH schema; a report that breaks an E2B(R3) conformance rule is refused with 400 naming each member.")
            .Produces<string>(StatusCodes.Status200OK, "application/xml")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            // A regulatory safety submission: medical monitors and system administrators (#4960, ADR-2511).
            .RequirePermission(RenderPermission).RequireCallerStudyScope();

        return group;
    }

    /// <summary>
    /// Renders <paramref name="request"/> as an ICH E2B(R3) ICSR, or refuses it with a validation problem keyed by the JSON
    /// path of each member that breaks an E2B(R3) conformance rule (<see cref="IcsrConformance"/>). The rendered message is
    /// validated against the vendored ICH schema before it is returned, so a message that does not conform is never
    /// emitted: that is a defect of the template, answered with 500 (#5573, ADR-2464).
    /// </summary>
    public static async Task<Results<ContentHttpResult, ValidationProblem, ProblemHttpResult>> RenderIcsrAsync(
        IcsrReport request, ITemplateRenderer renderer, IClock clock, HttpContext? httpContext = null)
    {
        if (request?.Study?.SponsorStudyNumber is { Length: > 0 } sponsorStudy
            && !string.IsNullOrWhiteSpace(sponsorStudy)
            && ClinicalScopeGuard.ResolvePrincipal(httpContext) is not null
            && !ClinicalScopeGuard.CanAccessStudy(httpContext, sponsorStudy))
        {
            return ClinicalScopeGuard.OutsideStudy(ClinicalScopeGuard.StudyAccessDeniedDetail);
        }

        var errors = IcsrConformance.Check(request, clock.UtcNow);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(
                errors,
                detail: $"The ICSR does not meet the ICH E2B(R3) conformance rules: {string.Join(", ", errors.Keys)}.",
                title: "The ICSR breaks E2B(R3) conformance rules");
        }

        // The template reads the report as `icsr` (#4959).
        string rendered = await renderer.RenderAsync(
            Catalog, IcsrTemplateKey, new Dictionary<string, object?> { ["icsr"] = request });

        var schemaErrors = IcsrSchema.Validate(rendered);
        if (schemaErrors.Count > 0)
        {
            return TypedResults.Problem(
                detail: "The rendered ICSR does not validate against the ICH E2B(R3) schema, so it was not returned.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "The rendered ICSR is not schema-valid",
                extensions: new Dictionary<string, object?> { ["schemaErrors"] = schemaErrors });
        }

        return TypedResults.Content(rendered, "application/xml");
    }
}
