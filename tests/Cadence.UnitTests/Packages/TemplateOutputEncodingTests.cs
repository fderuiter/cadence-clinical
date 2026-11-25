using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Apps.Ctms.Application;
using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Notifications.Domain;
using Cadence.Apps.Notifications.Endpoints;
using Cadence.Apps.Notifications.Infrastructure;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Templating;
using Cadence.Testing.Time;

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
            new IcsrHeader("B-1\" onload=\"x", "SND", "RCV", "20250930120000+0000", "M-1", "SND", "RCV"),
            new IcsrSafetyReport("XX-SPONSOR-1", "20250930110000+0000", "1", "20250929", "20250929", false, "XX-SPONSOR-1", "2"),
            [new IcsrPrimarySource("1", PrimaryForRegulatoryPurposes: true)],
            new IcsrSender("1", "Sponsor & Co"),
            new IcsrPatient(PatientId: "A&B <x/>"),
            [new IcsrReaction(new IcsrMeddraCoding("27.1", "10037844"), "1", ReactionTerm: "Rash</originalText><injected/>", ReactionTermLanguage: "eng")],
            [new IcsrDrug("1", "Drug \"X\"")],
            Hostile);

        var result = await IcsrTemplateEndpoints.RenderIcsrAsync(
            report, FluidTemplateRenderer.Instance, new FakeClock(new DateTimeOffset(2025, 10, 1, 0, 0, 0, TimeSpan.Zero)));

        var document = XDocument.Parse(result.Result.Should().BeOfType<ContentHttpResult>().Subject.ResponseContent!);
        document.Descendants(Hl7 + "investigationEvent").Single().Element(Hl7 + "text")!.Value.Should().Be(Hostile);
        document.Descendants(Hl7 + "originalText").Single().Value.Should().Be("Rash</originalText><injected/>");
        document.Descendants(Hl7 + "kindOfProduct").Single().Element(Hl7 + "name")!.Value.Should().Be("Drug \"X\"");
        document.Descendants(Hl7 + "injected").Should().BeEmpty();
        document.Root!.Element(Hl7 + "id")!.Attribute("extension")!.Value.Should().Be("B-1\" onload=\"x");
        document.Root.Element(Hl7 + "id")!.Attribute("onload").Should().BeNull();
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
    public async Task CtmsVisitLettersHtml_EncodeMarkupAcrossAllFields()
    {
        var confirmation = new SiteVisitLetterModel(
            CraId: "CRA <7>",
            CreatedAt: "2026-09-29 & \"now\"",
            StudyId: "R&D-42 <script>alert(1)</script>",
            SiteId: "SITE-'9'",
            VisitType: "IMV <b>1</b>",
            ScheduledDate: "2026-10-01 <img src=x>",
            Finalize: true,
            SignerName: "Dr. Élodie <PI>",
            SigningPurpose: "Approval & \"Sign-off\"");

        var confirmationPdf = await GxpPdfCompiler.CompileLetterPdfAsync(
            "confirmation_letter",
            confirmation,
            FluidTemplateRenderer.Instance,
            LetterTemplateEndpoints.Catalog,
            new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero)));

        confirmationPdf.RenderedHtml.Should().NotContain("<script>")
            .And.NotContain("<img")
            .And.NotContain("<b>1</b>")
            .And.NotContain("<PI>")
            .And.Contain("CRA (CRA &lt;7&gt;)")
            .And.Contain("R&amp;D-42 &lt;script&gt;alert(1)&lt;/script&gt;")
            .And.Contain("Dr. Élodie &lt;PI&gt;")
            .And.Contain("Approval &amp; &quot;Sign-off&quot;");

        var followUp = new SiteVisitLetterModel(
            CraId: "CRA <7>",
            CreatedAt: "2026-09-29",
            StudyId: "R&D-42",
            SiteId: "SITE-9",
            VisitType: "Close-out & <Review>",
            ActualDate: "2026-09-30 \"UTC\"",
            Findings:
            [
                new SiteVisitFindingModel(
                    Text: "<div data-canary=\"x\">Finding & 'note' é</div>",
                    Severity: "MAJOR <1>",
                    ResolutionStatus: "OPEN & \"PENDING\""),
            ]);

        var followUpResult = await LetterTemplateEndpoints.RenderLetterAsync(
            "follow_up_letter",
            followUp,
            FluidTemplateRenderer.Instance);

        string followUpHtml = followUpResult.Result.Should().BeOfType<ContentHttpResult>().Subject.ResponseContent!;
        followUpHtml.Should().NotContain("<div data-canary=\"x\">")
            .And.NotContain("<Review>")
            .And.NotContain("<1>")
            .And.Contain("&lt;div data-canary=&quot;x&quot;&gt;Finding &amp; ")
            .And.Contain("é&lt;/div&gt;")
            .And.Contain("MAJOR &lt;1&gt;")
            .And.Contain("OPEN &amp; &quot;PENDING&quot;");
    }

    [Fact]
    public async Task PlainTextTemplate_IsNotEncoded()
    {
        var inlineAssembly = new InlineTemplateAssembly(
            "Inline.Templates.plain_note.liquid",
            "To: {{ cra_id }} | Study: {{ study_id }}");
        var catalog = new TemplateCatalog(
            inlineAssembly,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["plain_note"] = "plain_note.liquid" });

        string content = await FluidTemplateRenderer.Instance.RenderAsync(
            catalog,
            "plain_note",
            new { cra_id = "CRA <7>", study_id = "R&D-42" });

        content.Should().Be("To: CRA <7> | Study: R&D-42");
    }

    [Theory]
    [InlineData("e2b_r3_icsr.xml.liquid", true)]
    [InlineData("Module.Templates.odm_template.XML.liquid", true)]
    [InlineData("edc_query_raised.html.liquid", true)]
    [InlineData("confirmation_letter.html.liquid", true)]
    [InlineData("follow_up_letter.html.liquid", true)]
    [InlineData("page.htm.liquid", true)]
    [InlineData("plain_note.liquid", false)]
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

    private sealed class InlineTemplateAssembly(string resourceName, string templateContent) : Assembly
    {
        public override string FullName => $"InlineTemplateAssembly,{resourceName}";

        public override AssemblyName GetName() => new("InlineTemplateAssembly");

        public override string[] GetManifestResourceNames() => [resourceName];

        public override Stream? GetManifestResourceStream(string name) =>
            string.Equals(name, resourceName, StringComparison.Ordinal)
                ? new MemoryStream(Encoding.UTF8.GetBytes(templateContent))
                : null;
    }
}
