using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Ddf;
using Cadence.Templating;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// #4763: template endpoints render only allow-listed templates from their own module, and parsed
/// templates are cached per owning assembly.
/// @req:PRD-SYS-053
/// </summary>
public sealed class TemplateCatalogTests
{
    private static readonly SiteVisitLetterModel Letter = new(
        CraId: "CRA-7",
        CreatedAt: "2026-09-29",
        StudyId: "STUDY-42",
        SiteId: "SITE-9",
        ScheduledDate: "2026-10-01");

    [Theory]
    [InlineData("odm_template.xml.liquid")]
    [InlineData("openrosa_template.xml.liquid")]
    [InlineData("e2b_r3_icsr.xml.liquid")]
    [InlineData("confirmation_letter.liquid")]
    [InlineData("Templates.confirmation_letter.liquid")]
    [InlineData("../../packages/ddf/Templates/odm_template.xml.liquid")]
    [InlineData("..\\odm")]
    [InlineData("CONFIRMATION_LETTER")]
    [InlineData("")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task CtmsLetterEndpoint_RejectsAnythingOutsideItsAllowList(string templateName)
    {
        var result = await LetterTemplateEndpoints.RenderLetterAsync(templateName, Letter, FluidTemplateRenderer.Instance);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["code"].Should().Be("TEMPLATE_NOT_ALLOWED");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task CtmsLetterEndpoint_RendersAnAllowedLetter()
    {
        var result = await LetterTemplateEndpoints.RenderLetterAsync("confirmation_letter", Letter, FluidTemplateRenderer.Instance);

        var content = result.Result.Should().BeOfType<ContentHttpResult>().Subject;
        content.ResponseContent.Should().Contain("CONFIRMATION OF CLINICAL MONITORING VISIT")
            .And.Contain("STUDY-42")
            .And.Contain("SITE-9");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task Renderer_RejectsKeysOutsideTheCatalog()
    {
        var act = async () => await FluidTemplateRenderer.Instance.RenderAsync(UsdmCompiler.Templates, "confirmation_letter", Letter);

        await act.Should().ThrowAsync<TemplateNotFoundException>();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public void Catalog_OnlyResolvesTemplatesEmbeddedInItsOwnAssembly()
    {
        var act = () => new TemplateCatalog(
            typeof(LetterTemplateEndpoints).Assembly,
            new Dictionary<string, string> { ["odm"] = "odm_template.xml.liquid" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*odm_template.xml.liquid*not embedded*");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-053")]
    public async Task SameKeyInTwoModules_RendersEachModulesOwnTemplate()
    {
        var ctms = new TemplateCatalog(
            typeof(LetterTemplateEndpoints).Assembly,
            new Dictionary<string, string> { ["document"] = "confirmation_letter.liquid" });
        var ddf = new TemplateCatalog(
            typeof(UsdmCompiler).Assembly,
            new Dictionary<string, string> { ["document"] = "odm_template.xml.liquid" });

        string fromCtms = await FluidTemplateRenderer.Instance.RenderAsync(ctms, "document", Letter);
        string fromDdf = await FluidTemplateRenderer.Instance.RenderAsync(ddf, "document", new Dictionary<string, object?>());
        string fromCtmsAgain = await FluidTemplateRenderer.Instance.RenderAsync(ctms, "document", Letter);

        fromCtms.Should().Contain("CONFIRMATION OF CLINICAL MONITORING VISIT");
        fromDdf.Should().NotContain("CONFIRMATION OF CLINICAL MONITORING VISIT");
        fromCtmsAgain.Should().Be(fromCtms);
    }
}
