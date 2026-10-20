using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Testing.Security;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// <c>POST /api/v1/safety/icsr/render</c> (#5464). A complete report renders to the golden file
/// <c>Safety/Golden/e2b_r3_icsr.golden.xml</c> with every mandatory element filled; a report that lacks one is refused
/// with a 400 validation problem naming the element, never rendered with an empty element. The repository holds no ICH
/// E2B(R3) XSD and the template does not follow the HL7 v3 ICSR message structure, so the golden file and the
/// structural assertions here stand in for a schema check.
/// </summary>
public sealed class IcsrRenderTests(DsurApprovalAuthorityTests.SafetyFactory factory)
    : IClassFixture<DsurApprovalAuthorityTests.SafetyFactory>
{
    private const string Route = "/api/v1/safety/icsr/render";
    private const string GoldenResource = "Cadence.UnitTests.Safety.Golden.e2b_r3_icsr.golden.xml";

    private static readonly XNamespace Hl7 = "urn:hl7-org:v3";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A complete report renders, element for element, to the golden file.</summary>
    [Fact]
    public async Task Render_CompleteReport_MatchesTheGoldenFile()
    {
        using var response = await RenderAsync(CompleteReport());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/xml");
        var rendered = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        // The roots, so the golden file's leading comment is not compared; whitespace between elements is dropped by Parse.
        rendered.Root!.ToString().Should().Be(XDocument.Parse(Golden()).Root!.ToString());
    }

    /// <summary>Every mandatory element of the rendered ICSR is present once and carries the value it was given.</summary>
    [Fact]
    public async Task Render_CompleteReport_FillsEveryMandatoryElement()
    {
        using var response = await RenderAsync(CompleteReport());
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));

        var root = XDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).Root!;

        root.Name.Should().Be(Hl7 + "ichicsr");
        Single(root, "header", "message_id").Should().Be("MSG-FIX-0001");
        Single(root, "header", "sender_organization").Should().Be("Cadence Sponsor");
        Single(root, "header", "receiver_organization").Should().Be("Regulatory Agency");
        Single(root, "header", "transmission_date").Should().Be("20261020");
        Single(root, "safety_report", "worldwide_unique_case_id").Should().Be("XX-CADENCE-FIX-0001");
        Single(root, "patient", "patient_id").Should().Be("PT-FIX-0001");
        root.Element(Hl7 + "reactions")!.Elements(Hl7 + "reaction").Select(r => r.Element(Hl7 + "reaction_term")!.Value)
            .Should().Equal("Hepatic failure", "Nausea");
        root.Element(Hl7 + "suspect_drugs")!.Elements(Hl7 + "suspect_drug").Select(d => d.Element(Hl7 + "drug_name")!.Value)
            .Should().Equal("Fixture-Compound 10 mg tablet");
        root.Descendants().Where(e => !e.HasElements).Should().OnlyContain(e => e.Value.Trim().Length > 0, "no element is emitted empty");
    }

    /// <summary>Each mandatory element, removed or blanked, and the rendered path the refusal must name.</summary>
    public static TheoryData<string, string> MissingElements => new()
    {
        { "header", "header/message_id" },
        { "header.messageId", "header/message_id" },
        { "header.senderOrganization", "header/sender_organization" },
        { "header.receiverOrganization", "header/receiver_organization" },
        { "header.transmissionDate", "header/transmission_date" },
        { "reportIdentifiers.worldwideUniqueCaseId", "safety_report/worldwide_unique_case_id" },
        { "patient", "patient/patient_id" },
        { "patient.patientId", "patient/patient_id" },
        { "reactions", "reactions/reaction" },
        { "reactions[1].reactionTerm", "reactions/reaction[2]/reaction_term" },
        { "suspectDrugs", "suspect_drugs/suspect_drug" },
        { "suspectDrugs[0].drugName", "suspect_drugs/suspect_drug[1]/drug_name" },
    };

    /// <summary>
    /// A report missing a mandatory element, or giving it blank, is refused with a 400 validation problem whose errors
    /// name that element; no XML is emitted.
    /// </summary>
    [Theory]
    [MemberData(nameof(MissingElements))]
    public async Task Render_MissingAMandatoryElement_Returns400NamingIt(string field, string element)
    {
        foreach (var report in new[] { Without(field, blank: false), Without(field, blank: true) })
        {
            using var response = await RenderAsync(report);

            string body = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var errors = JsonNode.Parse(body)!["errors"]!.AsObject();
            errors.Select(e => e.Key).Should().Contain(element);
            body.Should().NotContain("<ichicsr");
        }
    }

    private async Task<HttpResponseMessage> RenderAsync(JsonNode report)
    {
        using var client = factory.CreateSignedClient(TestSecurityContext.Identity("icsr-mm", "Sponsor Medical Monitor"));
        using var content = new StringContent(report.ToJsonString(), Encoding.UTF8, "application/json");
        return await client.PostAsync(Route, content, Ct);
    }

    private static string Single(XElement root, string parent, string element) =>
        root.Elements(Hl7 + parent).Single().Elements(Hl7 + element).Single().Value;

    /// <summary>
    /// <see cref="CompleteReport"/> with <paramref name="path"/> (dotted, with <c>[i]</c> indexes) removed, or for a leaf
    /// with <paramref name="blank"/>, set to whitespace; a blanked object or array becomes JSON null or empty.
    /// </summary>
    private static JsonNode Without(string path, bool blank)
    {
        var report = CompleteReport();
        string[] segments = path.Split('.');
        JsonNode parent = report;
        foreach (string segment in segments[..^1])
        {
            parent = Step(parent, segment);
        }

        string last = segments[^1];
        var target = parent.AsObject();
        if (!blank)
        {
            target.Remove(last);
        }
        else
        {
            target[last] = target[last] switch
            {
                JsonArray => new JsonArray(),
                JsonObject => null,
                _ => "   ",
            };
        }

        return report;
    }

    private static JsonNode Step(JsonNode node, string segment)
    {
        int bracket = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracket < 0)
        {
            return node[segment]!;
        }

        int index = int.Parse(segment[(bracket + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        return node[segment[..bracket]]![index]!;
    }

    private static JsonNode CompleteReport() => JsonNode.Parse(
        """
        {
          "header": {
            "messageId": "MSG-FIX-0001",
            "senderOrganization": "Cadence Sponsor",
            "receiverOrganization": "Regulatory Agency",
            "transmissionDate": "20261020",
            "messageType": "ichicsr"
          },
          "reportIdentifiers": {
            "worldwideUniqueCaseId": "XX-CADENCE-FIX-0001",
            "localReportId": "LOCAL-FIX-0001",
            "firstSenderType": "1"
          },
          "patient": { "patientId": "PT-FIX-0001", "sex": "2", "age": 54, "ageUnit": "a" },
          "reactions": [
            {
              "reactionTerm": "Hepatic failure",
              "meddraCoding": {
                "lltCode": "10019663", "lltName": "Hepatic failure",
                "ptCode": "10019663", "ptName": "Hepatic failure",
                "hltCode": "10019654", "hltName": "Hepatic failure and associated disorders",
                "hlgtCode": "10019805", "hlgtName": "Hepatic and hepatobiliary disorders",
                "socCode": "10019805", "socName": "Hepatobiliary disorders",
                "primarySocFlag": "Y",
                "score": "1.0"
              },
              "startDate": "20250619",
              "outcome": "5",
              "seriousnessDeath": "1",
              "seriousnessHospitalization": "1"
            },
            { "reactionTerm": "Nausea", "startDate": "20250414", "endDate": "20250420", "outcome": "1" }
          ],
          "suspectDrugs": [
            {
              "drugName": "Fixture-Compound 10 mg tablet",
              "activeSubstanceName": "Fixture-Compound",
              "dosageText": "10 mg once daily",
              "routeOfAdministration": "048",
              "actionTakenWithDrug": "1"
            }
          ],
          "versionIndex": 1,
          "reasonForChange": "Initial ICSR transmission"
        }
        """)!;

    private static string Golden()
    {
        using var stream = typeof(IcsrRenderTests).Assembly.GetManifestResourceStream(GoldenResource)
            ?? throw new InvalidOperationException($"Embedded golden file {GoldenResource} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
