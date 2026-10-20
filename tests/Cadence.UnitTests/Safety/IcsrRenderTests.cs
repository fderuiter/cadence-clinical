using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

using Cadence.Apps.Safety.Infrastructure.Standards;
using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// <c>POST /api/v1/safety/icsr/render</c> emits an ICH E2B(R3) ICSR (#5573): the HL7 v3 <c>MCCI_IN200100UV01</c> batch
/// message of the ICH ICSR Implementation Guide. Each rendered message is validated here against the ICH schema files
/// vendored in <c>apps/safety/Schemas/ich-e2b-r3</c>, loaded from disk independently of the service's own validator.
/// A report that breaks an E2B(R3) conformance rule is refused with a 400 validation problem naming the member.
/// </summary>
public sealed class IcsrRenderTests(DsurApprovalAuthorityTests.SafetyFactory factory)
    : IClassFixture<DsurApprovalAuthorityTests.SafetyFactory>
{
    private const string Route = "/api/v1/safety/icsr/render";
    private const string GoldenResource = "Cadence.UnitTests.Safety.Golden.e2b_r3_icsr.golden.xml";

    private static readonly XNamespace Hl7 = "urn:hl7-org:v3";
    private static readonly Lazy<XmlSchemaSet> IchSchema = new(LoadIchSchema);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A complete report renders, element for element, to the golden file.</summary>
    [Fact]
    public async Task Render_CompleteReport_MatchesTheGoldenFile()
    {
        string xml = await RenderOkAsync(IcsrFixture.CompleteReport());

        // Comments are not compared; whitespace between elements is dropped by Parse.
        Strip(XDocument.Parse(xml)).Should().Be(Strip(XDocument.Parse(Golden())));
    }

    /// <summary>The complete and the minimal report each render to a message the ICH E2B(R3) schema accepts.</summary>
    [Theory]
    [InlineData("complete")]
    [InlineData("minimal")]
    public async Task Render_ValidatesAgainstTheIchE2bR3Schema(string which)
    {
        var report = which == "complete" ? IcsrFixture.CompleteReport() : IcsrFixture.MinimalReport();

        string xml = await RenderOkAsync(report);

        SchemaErrors(xml).Should().BeEmpty();
    }

    /// <summary>
    /// The ICH data elements sit where the Implementation Guide puts them, under the ICH OIDs: batch and message
    /// identifiers, the case identifiers, the MedDRA coding of each reaction, each drug's role and the narrative.
    /// </summary>
    [Fact]
    public async Task Render_CompleteReport_PutsEachElementWhereTheIchGuideDoes()
    {
        var root = XDocument.Parse(await RenderOkAsync(IcsrFixture.CompleteReport())).Root!;

        root.Name.Should().Be(Hl7 + "MCCI_IN200100UV01");
        Id(root, "2.16.840.1.113883.3.989.2.1.3.22").Should().Be("BATCH-FIX-0001", "N.1.2 batch number");
        Id(root.Element(Hl7 + "sender")!.Element(Hl7 + "device")!, "2.16.840.1.113883.3.989.2.1.3.13").Should().Be("CADENCE-FIX-SENDER", "N.1.3 batch sender");
        Id(root.Element(Hl7 + "receiver")!.Element(Hl7 + "device")!, "2.16.840.1.113883.3.989.2.1.3.14").Should().Be("FIX-RECEIVER", "N.1.4 batch receiver");
        root.Element(Hl7 + "creationTime")!.Attribute("value")!.Value.Should().Be("20251020091500+0000", "N.1.5");
        var message = root.Element(Hl7 + "PORR_IN049016UV")!;
        message.Element(Hl7 + "creationTime")!.Attribute("value")!.Value.Should().Be("20251020090000+0000", "N.2.r.4 is C.1.2");

        var investigation = message.Descendants(Hl7 + "investigationEvent").Single();
        Id(investigation, "2.16.840.1.113883.3.989.2.1.3.1").Should().Be("XX-CADENCE-FIX-0001", "C.1.1");
        Id(investigation, "2.16.840.1.113883.3.989.2.1.3.2").Should().Be("XX-CADENCE-FIX-0001", "C.1.8.1");
        investigation.Element(Hl7 + "text")!.Value.Should().StartWith("Synthetic fixture case", "H.1 narrative");

        var codings = investigation.Descendants(Hl7 + "value")
            .Where(v => v.Attribute("codeSystem")?.Value == "2.16.840.1.113883.6.163")
            .Select(v => (Code: v.Attribute("code")!.Value, Version: v.Attribute("codeSystemVersion")!.Value, Term: v.Element(Hl7 + "originalText")?.Value));
        codings.Should().Equal(("10019663", "27.1", "Hepatic failure"), ("10028813", "27.1", "Nausea"));

        var roles = investigation.Descendants(Hl7 + "causalityAssessment")
            .Select(c => c.Element(Hl7 + "value")!)
            .Where(v => v.Attribute("codeSystem")!.Value == "2.16.840.1.113883.3.989.2.1.1.13")
            .Select(v => v.Attribute("code")!.Value);
        roles.Should().Equal("1");

        var player = investigation.Descendants(Hl7 + "player1").Single();
        player.Element(Hl7 + "name")!.Attribute("nullFlavor")!.Value.Should().Be("MSK", "D.1 is never transmitted");
        player.Element(Hl7 + "administrativeGenderCode")!.Attribute("code")!.Value.Should().Be("2");
    }

    /// <summary>A report without a sex renders no sex element (D.5 is optional), and the message still validates.</summary>
    [Fact]
    public async Task Render_WithoutSex_OmitsTheSexElement()
    {
        string xml = await RenderOkAsync(IcsrFixture.Without(IcsrFixture.CompleteReport(), "patient.sex", blank: false));

        XDocument.Parse(xml).Descendants(Hl7 + "administrativeGenderCode").Should().BeEmpty();
        SchemaErrors(xml).Should().BeEmpty();
    }

    /// <summary>
    /// The six seriousness criteria (E.i.3.2a-f) are sent for every reaction: true when met, otherwise nullFlavor NI,
    /// never false, as the ICH guide allows only those two.
    /// </summary>
    [Fact]
    public async Task Render_SendsEachSeriousnessCriterionAsTrueOrNi()
    {
        var investigation = XDocument.Parse(await RenderOkAsync(IcsrFixture.CompleteReport()))
            .Descendants(Hl7 + "investigationEvent").Single();
        var reactions = investigation.Descendants(Hl7 + "observation")
            .Where(o => o.Element(Hl7 + "code") is { } code
                && code.Attribute("code")?.Value == "29"
                && code.Attribute("codeSystem")?.Value == "2.16.840.1.113883.3.989.2.1.1.19")
            .ToList();

        string[] criteria = ["34", "21", "33", "35", "12", "26"];
        reactions.Should().HaveCount(2);
        criteria.Select(c => Criterion(reactions[0], c))
            .Should().Equal("true", "NI", "true", "NI", "NI", "NI");
        criteria.Select(c => Criterion(reactions[1], c)).Should().AllBe("NI");
    }

    /// <summary>Each mandatory element, removed or blanked, and the JSON path the refusal must name.</summary>
    public static TheoryData<string, string> MissingElements => new()
    {
        { "header", "header" },
        { "header.batchNumber", "header.batchNumber" },
        { "header.batchSenderIdentifier", "header.batchSenderIdentifier" },
        { "header.batchReceiverIdentifier", "header.batchReceiverIdentifier" },
        { "header.batchTransmissionDate", "header.batchTransmissionDate" },
        { "header.messageIdentifier", "header.messageIdentifier" },
        { "header.messageSenderIdentifier", "header.messageSenderIdentifier" },
        { "header.messageReceiverIdentifier", "header.messageReceiverIdentifier" },
        { "safetyReport", "safetyReport" },
        { "safetyReport.senderSafetyReportId", "safetyReport.senderSafetyReportId" },
        { "safetyReport.creationDate", "safetyReport.creationDate" },
        { "safetyReport.reportType", "safetyReport.reportType" },
        { "safetyReport.firstReceivedDate", "safetyReport.firstReceivedDate" },
        { "safetyReport.mostRecentInformationDate", "safetyReport.mostRecentInformationDate" },
        { "safetyReport.fulfilsLocalCriteriaForExpeditedReport", "safetyReport.fulfilsLocalCriteriaForExpeditedReport" },
        { "safetyReport.worldwideUniqueCaseId", "safetyReport.worldwideUniqueCaseId" },
        { "safetyReport.firstSender", "safetyReport.firstSender" },
        { "primarySources", "primarySources" },
        { "primarySources[0].qualification", "primarySources[0].qualification" },
        { "sender", "sender" },
        { "sender.senderType", "sender.senderType" },
        { "sender.organisation", "sender.organisation" },
        { "study", "study" },
        { "study.studyType", "study.studyType" },
        { "patient", "patient" },
        { "reactions", "reactions" },
        { "reactions[1].meddraCoding", "reactions[1].meddraCoding" },
        { "reactions[0].meddraCoding.version", "reactions[0].meddraCoding.version" },
        { "reactions[0].meddraCoding.lltCode", "reactions[0].meddraCoding.lltCode" },
        { "reactions[0].reactionTermLanguage", "reactions[0].reactionTermLanguage" },
        { "reactions[1].outcome", "reactions[1].outcome" },
        { "drugs", "drugs" },
        { "drugs[0].drugRole", "drugs[0].drugRole" },
        { "drugs[0].drugName", "drugs[0].drugName" },
        { "narrative", "narrative" },
    };

    /// <summary>
    /// A report missing a mandatory element, or giving it blank, is refused with a 400 validation problem whose errors
    /// name that element; no XML is emitted.
    /// </summary>
    [Theory]
    [MemberData(nameof(MissingElements))]
    public async Task Render_MissingAMandatoryElement_Returns400NamingIt(string field, string member)
    {
        foreach (bool blank in new[] { false, true })
        {
            await ShouldBeRefusedAsync(IcsrFixture.Without(IcsrFixture.CompleteReport(), field, blank), member);
        }
    }

    /// <summary>Values that break an E2B(R3) rule on their own or across elements, and the member each refusal names.</summary>
    public static TheoryData<string, string, string> InvalidValues => new()
    {
        // N.1.5 and C.1.2 need the date and time to the second, as an HL7 TS.
        { "header.batchTransmissionDate", "\"20251020\"", "header.batchTransmissionDate" },
        { "safetyReport.creationDate", "\"2025-10-20T09:00:00Z\"", "safetyReport.creationDate" },
        // No date may refer to the future.
        { "safetyReport.firstReceivedDate", "\"29991231\"", "safetyReport.firstReceivedDate" },
        { "reactions[0].startDate", "\"2999\"", "reactions[0].startDate" },
        // An impossible calendar date.
        { "safetyReport.mostRecentInformationDate", "\"20250230\"", "safetyReport.mostRecentInformationDate" },
        // C.1.1: country code-sender-report number.
        { "safetyReport.senderSafetyReportId", "\"CADENCE0001\"", "safetyReport.senderSafetyReportId" },
        { "safetyReport.reportType", "\"9\"", "safetyReport.reportType" },
        // C.2.r.5: exactly one primary source for regulatory purposes.
        { "primarySources[0].primaryForRegulatoryPurposes", "false", "primarySources" },
        { "primarySources[0].countryCode", "\"Netherlands\"", "primarySources[0].countryCode" },
        // E.i.2.1: an eight-digit MedDRA code, an N.N version, one version per ICSR.
        { "reactions[0].meddraCoding.lltCode", "\"1001966\"", "reactions[0].meddraCoding.lltCode" },
        { "reactions[0].meddraCoding.version", "\"27\"", "reactions[0].meddraCoding.version" },
        { "reactions[1].meddraCoding.version", "\"26.0\"", "reactions" },
        { "reactions[0].reactionTermLanguage", "\"en\"", "reactions[0].reactionTermLanguage" },
        { "reactions[0].outcome", "\"6\"", "reactions[0].outcome" },
        { "patient.sex", "\"F\"", "patient.sex" },
        { "patient.ageUnit", "\"years\"", "patient.ageUnit" },
        // G.k.1: at least one suspect, interacting or not-administered drug.
        { "drugs[0].drugRole", "\"2\"", "drugs" },
        { "drugs[0].routeOfAdministration", "\"oral\"", "drugs[0].routeOfAdministration" },
        { "drugs[0].actionTakenWithDrug", "\"7\"", "drugs[0].actionTakenWithDrug" },
        { "header.batchNumber", "\"" + new string('B', 101) + "\"", "header.batchNumber" },
    };

    /// <summary>A value that breaks an E2B(R3) conformance or business rule is refused with 400 naming the member.</summary>
    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task Render_InvalidValue_Returns400NamingIt(string field, string json, string member)
    {
        await ShouldBeRefusedAsync(IcsrFixture.With(IcsrFixture.CompleteReport(), field, JsonNode.Parse(json)), member);
    }

    /// <summary>C.3.2 sender's organisation is needed unless the sender is a patient or consumer (C.3.1 = 7).</summary>
    [Fact]
    public async Task Render_ConsumerSenderWithoutOrganisation_IsAccepted()
    {
        var report = IcsrFixture.With(IcsrFixture.CompleteReport(), "sender", new JsonObject { ["senderType"] = "7" });

        SchemaErrors(await RenderOkAsync(report)).Should().BeEmpty();
    }

    /// <summary>
    /// The service's own validator, which the route runs on every message before returning it, accepts a rendered ICSR
    /// and rejects one whose structure the schema does not allow, as the independent check here does.
    /// </summary>
    [Fact]
    public async Task IcsrSchema_RejectsAMessageTheSchemaDoesNotAllow()
    {
        string xml = await RenderOkAsync(IcsrFixture.CompleteReport());
        IcsrSchema.Validate(xml).Should().BeEmpty();

        var tampered = XDocument.Parse(xml);
        tampered.Root!.Element(Hl7 + "name")!.Remove();
        tampered.Root.Element(Hl7 + "PORR_IN049016UV")!.AddFirst(new XElement(Hl7 + "unexpected"));

        IcsrSchema.Validate(tampered.ToString()).Should().NotBeEmpty();
        SchemaErrors(tampered.ToString()).Should().NotBeEmpty();
    }

    private async Task ShouldBeRefusedAsync(JsonNode report, string member)
    {
        using var response = await RenderAsync(report);

        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var errors = JsonNode.Parse(body)!["errors"]!.AsObject();
        errors.Select(e => e.Key).Should().Contain(member, body);
        body.Should().NotContain("<MCCI_IN200100UV01");
    }

    private async Task<string> RenderOkAsync(JsonNode report)
    {
        using var response = await RenderAsync(report);
        string body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");
        return body;
    }

    private async Task<HttpResponseMessage> RenderAsync(JsonNode report)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("icsr-mm", "Sponsor Medical Monitor"));
        using var content = new StringContent(report.ToJsonString(), Encoding.UTF8, "application/json");
        return await client.PostAsync(Route, content, Ct);
    }

    private static string Id(XElement scope, string root) =>
        scope.Elements(Hl7 + "id").Single(id => id.Attribute("root")!.Value == root).Attribute("extension")!.Value;

    /// <summary>The value of seriousness criterion <paramref name="code"/> of a reaction: "true", or its nullFlavor.</summary>
    private static string Criterion(XElement reaction, string code)
    {
        var value = reaction.Elements(Hl7 + "outboundRelationship2")
            .Select(r => r.Element(Hl7 + "observation")!)
            .Single(o => o.Element(Hl7 + "code")!.Attribute("code")!.Value == code)
            .Element(Hl7 + "value")!;
        return value.Attribute("value")?.Value ?? value.Attribute("nullFlavor")!.Value;
    }

    private static string Strip(XDocument document)
    {
        foreach (var comment in document.DescendantNodes().OfType<XComment>().ToList())
        {
            comment.Remove();
        }

        return document.Root!.ToString();
    }

    /// <summary>The errors of validating <paramref name="xml"/> against the vendored ICH schema files on disk.</summary>
    private static List<string> SchemaErrors(string xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = IchSchema.Value, DtdProcessing = DtdProcessing.Prohibit };
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity} line {e.Exception?.LineNumber}: {e.Message}");
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
            // Each schema error raises a validation event while the document is read.
        }

        return errors;
    }

    private static XmlSchemaSet LoadIchSchema()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Safety", "Schemas", "ich-e2b-r3", "multicacheschemas", "MCCI_IN200100UV01.xsd");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add("urn:hl7-org:v3", path);
        set.Compile();
        return set;
    }

    private static string Golden()
    {
        using var stream = typeof(IcsrRenderTests).Assembly.GetManifestResourceStream(GoldenResource)
            ?? throw new InvalidOperationException($"Embedded golden file {GoldenResource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
