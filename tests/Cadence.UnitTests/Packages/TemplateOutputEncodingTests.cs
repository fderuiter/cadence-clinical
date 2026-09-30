using System;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Templating;

using FluentAssertions;

using Fluid;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// #4959: the template renderer encodes every output value for the template's format, so caller data cannot
/// break or inject into rendered XML and HTML.
/// </summary>
public sealed class TemplateOutputEncodingTests
{
    private const string Hostile = "A&B <x/> \"q\" 'a' é";

    private static readonly XNamespace Hl7 = "urn:hl7-org:v3";

    [Fact]
    public async Task IcsrXml_StaysWellFormedAndKeepsHostileValues()
    {
        var report = new IcsrReport(
            new IcsrHeader("MSG-1", "Sponsor & Co", "EMA <test>", "20260930"),
            new IcsrReportIdentifiers("WW-1"),
            new IcsrPatient(Hostile, "F"),
            [new IcsrReaction("Rash</reaction_term><injected/>")],
            [new IcsrSuspectDrug("Drug \"X\"")],
            ReasonForChange: "fix \" onload=\"x");

        var result = await IcsrTemplateEndpoints.RenderIcsrAsync(report, FluidTemplateRenderer.Instance);

        var document = XDocument.Parse(result.ResponseContent!);
        document.Descendants(Hl7 + "patient_id").Single().Value.Should().Be(Hostile);
        document.Descendants(Hl7 + "reaction_term").Single().Value.Should().Be("Rash</reaction_term><injected/>");
        document.Descendants(Hl7 + "drug_name").Single().Value.Should().Be("Drug \"X\"");
        document.Descendants(Hl7 + "injected").Should().BeEmpty();
        document.Root!.Attribute("reason_for_change")!.Value.Should().Be("fix \" onload=\"x");
        document.Root.Attribute("onload").Should().BeNull();
    }

    [Fact]
    public async Task NotificationHtml_EncodesMarkupInFields()
    {
        var request = new NotificationRenderModel(
            "STUDY-1",
            "EVT-1",
            "2026-09-30T00:00:00Z",
            new NotificationPayload(SubjectId: "<script>alert(1)</script>", QueryMessage: "a & b"));

        var result = await NotificationTemplateEndpoints.RenderNotificationAsync(
            "edc_query_raised", request, FluidTemplateRenderer.Instance, Options.Create(new NotificationBrandingOptions()));

        string content = result.Result.Should().BeOfType<Ok<RenderedNotification>>().Subject.Value!.Content;
        content.Should().NotContain("<script>");
        content.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        content.Should().Contain("a &amp; b");
    }

    [Fact]
    public async Task PlainTextLetter_IsNotEncoded()
    {
        var letter = new SiteVisitLetterModel(
            CraId: "CRA <7>",
            CreatedAt: "2026-09-29",
            StudyId: "R&D-42",
            SiteId: "SITE-9",
            ScheduledDate: "2026-10-01");

        var result = await LetterTemplateEndpoints.RenderLetterAsync("confirmation_letter", letter, FluidTemplateRenderer.Instance);

        string content = result.Result.Should().BeOfType<ContentHttpResult>().Subject.ResponseContent!;
        content.Should().Contain("CRA (CRA <7>)");
        content.Should().Contain("R&D-42");
    }

    [Theory]
    [InlineData("e2b_r3_icsr.xml.liquid", true)]
    [InlineData("Module.Templates.odm_template.XML.liquid", true)]
    [InlineData("edc_query_raised.html.liquid", true)]
    [InlineData("page.htm.liquid", true)]
    [InlineData("confirmation_letter.liquid", false)]
    [InlineData("summary.txt.liquid", false)]
    public void EncoderFor_ChoosesMarkupEncodingByFormat(string fileName, bool encodesMarkup)
    {
        var encoder = FluidTemplateRenderer.EncoderFor(fileName);

        if (encodesMarkup)
        {
            encoder.Should().NotBeSameAs(NullEncoder.Default);
            encoder.Encode("<a href='x'>&\"</a>").Should().NotContainAny("<", ">", "'", "\"");
            encoder.Encode("é").Should().Be("é");
        }
        else
        {
            encoder.Should().BeSameAs(NullEncoder.Default);
        }
    }

    [Fact]
    public void EncoderFor_RejectsNull()
    {
        var act = () => FluidTemplateRenderer.EncoderFor(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
