using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Trust;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;
using Cadence.Testing;

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
    public async Task All36BundledCdiscTemplates_WithTerminologyResolver_MapItemSchemasAndCompileSnapshot()
    {
        var catalog = new CdiscLibraryTemplateCatalog();
        var service = new ProtocolDesignImporterService(catalog);
        var allIds = catalog.ListTemplates().Select(t => t.Id).ToList();
        var csVersion = CodeSystemVersionId.New();
        var resolvedCodelists = new HashSet<string>(StringComparer.Ordinal);

        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(TemplateIds: allIds, FallbackTitle: "CDISC Full Suite"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken,
            resolveTerminology: (candidate, _) =>
            {
                if (candidate.CodelistCode is { } cl)
                {
                    resolvedCodelists.Add(cl);
                }

                return ValueTask.FromResult(
                    new ProtocolTerminologyResolution(new TerminologyBinding(csVersion, candidate.CodelistCode)));
            });

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        resolvedCodelists.Should().NotBeEmpty("CDISC Library templates include CodeList-backed items");

        var allItems = result.Design!.CollectionSpecs.SelectMany(s => s.Items).ToList();
        allItems.Should().Contain(i => i.Terminology != null && i.ExpectedKind == ObservationValueKind.Coded);
        allItems.Should().Contain(i => i.ExpectedKind == ObservationValueKind.Quantity);
        allItems.Should().Contain(i => i.ExpectedKind == ObservationValueKind.DateTime);
        allItems.Should().OnlyContain(i =>
            (i.Terminology == null || i.ExpectedKind == ObservationValueKind.Coded)
            && (i.Unit == null || i.ExpectedKind == ObservationValueKind.Quantity));

        var compiler = new ProtocolFormCompiler();
        compiler.Compile(new ProtocolDesign { Title = "Empty" }, "STUDY-EMPTY", "1").Should().BeNull();

        var compiled = compiler.Compile(result.Design, "STUDY-CDISC-36", "1");
        compiled.Should().NotBeNull();
        compiled!.FormId.Should().Be("STUDY-CDISC-36");
        ContentHash.IsWellFormed(compiled.FormHash).Should().BeTrue();
        compiled.OpenRosaXml.Should().NotBeNullOrWhiteSpace().And.Contain("<h:html");
        compiled.DdfIrJson.Should().NotBeNullOrWhiteSpace().And.Contain("STUDY-CDISC-36");
        compiled.OdmXml.Should().NotBeNullOrWhiteSpace().And.Contain("<ODM");
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

    [Fact]
    [KernelRule("K-PROT-1")]
    public async Task ReplaceForms_OnCadence101Design_RetainsAll18NonFormElements_PrunesRemovedSpecs_AndPassesRead()
    {
        var baseDesign = BuildCadence101LikeDesign();
        var catalog = new CdiscLibraryTemplateCatalog();
        var service = new ProtocolDesignImporterService(catalog);

        const string replacementOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.REPLACE" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-03-01T00:00:00Z">
              <Study OID="S.CADENCE.101">
                <GlobalVariables>
                  <StudyName>CADENCE-101 Updated Forms</StudyName>
                  <StudyDescription>Replacement eCRFs</StudyDescription>
                  <ProtocolName>CADENCE-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.2" Name="v2">
                  <FormDef OID="cs.vitals" Name="Vital Signs Seated" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="cs.ecg" Name="12-Lead ECG" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.ECG" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                    <ItemRef ItemOID="vs.sbp" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.ECG" Name="ECG" Repeating="No">
                    <ItemRef ItemOID="ecg.hr" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="vs.sbp" Name="SYSBP" DataType="integer">
                    <Alias Context="prompt" Name="Seated Systolic BP"/>
                  </ItemDef>
                  <ItemDef OID="ecg.hr" Name="ECGHR" DataType="integer">
                    <Alias Context="prompt" Name="Ventricular Rate"/>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var outcome = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: Encoding.UTF8.GetBytes(replacementOdm),
                FileName: "cadence101-replace.xml",
                Format: ProtocolImportFormat.OdmXml,
                BaseDesign: baseDesign,
                ReplaceForms: true),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        outcome.IsSuccess.Should().BeTrue(string.Join("; ", outcome.Problems));
        var design = outcome.Design!;

        var roundTrip = ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(design));
        roundTrip.IsValid.Should().BeTrue(string.Join("; ", roundTrip.Problems));

        int nonFormCount =
            design.Objectives.Count
            + design.Endpoints.Count
            + design.Estimands.Count
            + design.Populations.Count
            + design.EligibilityCriteria.Count
            + design.Interventions.Count
            + design.Arms.Count
            + design.SafetyRules.Count
            + design.Epochs.Count
            + design.PlannedActivities.Count;
        nonFormCount.Should().Be(18);

        design.CollectionSpecs.Select(s => s.Id).Should().Equal("cs.vitals", "cs.ecg");
        design.PlannedActivities.Single(a => a.Id == "act.screening").CollectionSpecIds.Should().Equal("cs.vitals");
        design.PlannedActivities.Single(a => a.Id == "act.eos").CollectionSpecIds.Should().Equal("cs.vitals");
        design.ImportNotes.Should().Contain(n => n.Source == "act.screening" && n.Message.Contains("cs.physical_exam", StringComparison.Ordinal));
    }

    [Fact]
    [KernelRule("K-PROT-1")]
    public async Task UsdmImport_WithReplaceFormsFalse_MergesFormsAndSectionsOntoBaseDesign()
    {
        var baseConcept = new ConceptRef(ConceptId.New(), 1);
        var baseDesign = new ProtocolDesign
        {
            Title = "CADENCE-101",
            Objectives = [new Objective("obj.primary", ObjectiveLevel.Primary, "Primary objective")],
            Epochs = [new Epoch("ep.screening", "Screening", 1)],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.demographics",
                    "Demographics",
                    [new CollectionItem("dm.age", baseConcept, "yr", Required: true, ValueKind: ObservationValueKind.Quantity, Prompt: "Age")]),
            ],
        };

        const string usdmJson = """
            {
              "usdmVersion": "4.0.0",
              "study": {
                "id": "Study_CADENCE_101",
                "name": "CADENCE-101",
                "versions": [
                  {
                    "id": "SV_1",
                    "titles": [
                      { "id": "T_1", "text": "CADENCE-101", "type": { "code": "C207616", "decode": "Official Study Title" } }
                    ],
                    "biomedicalConcepts": [
                      {
                        "id": "BC_SBP",
                        "name": "Systolic Blood Pressure",
                        "label": "Systolic Blood Pressure",
                        "code": { "id": "AC_1", "standardCode": { "code": "C25298", "decode": "Systolic Blood Pressure" } }
                      }
                    ],
                    "studyDesigns": [
                      {
                        "id": "SD_1",
                        "epochs": [
                          { "id": "ep.treatment", "name": "Treatment" }
                        ],
                        "activities": [
                          {
                            "id": "ACT.VITALS",
                            "name": "Vital Signs",
                            "biomedicalConceptIds": [ "BC_SBP" ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;

        var service = new ProtocolDesignImporterService(new CdiscLibraryTemplateCatalog());
        var outcome = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: Encoding.UTF8.GetBytes(usdmJson),
                FileName: "usdm-v4.json",
                Format: ProtocolImportFormat.UsdmJson,
                BaseDesign: baseDesign,
                ReplaceForms: false),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        outcome.IsSuccess.Should().BeTrue(string.Join("; ", outcome.Problems));
        outcome.Design!.Objectives.Select(o => o.Id).Should().Equal("obj.primary");
        outcome.Design.Epochs.Select(e => e.Id).Should().Equal("ep.screening", "ep.treatment");
        outcome.Design.CollectionSpecs.Select(s => s.Id).Should().Equal("cs.demographics", "ACT.VITALS");
    }

    [Fact]
    [KernelRule("K-PROT-1")]
    public async Task CombinedRawBytesAndTemplates_ImportsFormsFromBothSourcesInDeterministicOrder()
    {
        var catalog = new CdiscLibraryTemplateCatalog();
        var templates = catalog.ListTemplates().Take(2).ToList();
        var service = new ProtocolDesignImporterService(catalog);

        const string customOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.COMBINED" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-03-01T00:00:00Z">
              <Study OID="S.COMBINED">
                <GlobalVariables>
                  <StudyName>Combined Import Study</StudyName>
                  <StudyDescription>Combined</StudyDescription>
                  <ProtocolName>COMB-01</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <FormDef OID="F.CUSTOM_PK" Name="Pharmacokinetics Sampling" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.PK" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.PK" Name="PK" Repeating="No">
                    <ItemRef ItemOID="IT.PKTIME" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.PKTIME" Name="PKTIME" DataType="text">
                    <Alias Context="prompt" Name="Sample Collection Time"/>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var outcome = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: Encoding.UTF8.GetBytes(customOdm),
                FileName: "custom-pk.xml",
                Format: ProtocolImportFormat.OdmXml,
                TemplateIds: [templates[0].Id, templates[1].Id],
                ReplaceForms: false),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        outcome.IsSuccess.Should().BeTrue(string.Join("; ", outcome.Problems));
        outcome.Design!.CollectionSpecs.Select(s => s.Id).Should().Equal(
            "F.CUSTOM_PK",
            templates[0].FormOid,
            templates[1].FormOid);
    }

    [Fact]
    [KernelRule("K-PROT-1")]
    public async Task OdmScheduleAndRenamedForms_PopulateEpochsAndPlannedActivities_AndUpdateInPlaceWithoutSuffixes()
    {
        const string initialOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.SOA.1" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-03-01T00:00:00Z">
              <Study OID="S.SOA">
                <GlobalVariables>
                  <StudyName>SOA ODM Study</StudyName>
                  <StudyDescription>SOA</StudyDescription>
                  <ProtocolName>SOA-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <Protocol>
                    <StudyEventRef StudyEventOID="SE.WK4" OrderNumber="2" Mandatory="Yes"/>
                    <StudyEventRef StudyEventOID="SE.SCR" OrderNumber="1" Mandatory="Yes"/>
                  </Protocol>
                  <StudyEventDef OID="SE.SCR" Name="Screening Visit" Repeating="No" Type="Scheduled">
                    <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="Yes"/>
                  </StudyEventDef>
                  <StudyEventDef OID="SE.WK4" Name="Week 4 Visit" Repeating="No" Type="Scheduled" Category="Treatment">
                    <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="No"/>
                  </StudyEventDef>
                  <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                    <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.DIA" OrderNumber="2" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                    <Alias Context="prompt" Name="Systolic Blood Pressure"/>
                  </ItemDef>
                  <ItemDef OID="IT.DIA" Name="DIABP" DataType="integer">
                    <Alias Context="prompt" Name="Diastolic Blood Pressure"/>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        const string renamedFormOdm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.SOA.2" FileType="Snapshot" ODMVersion="1.3.2" CreationDateTime="2026-03-02T00:00:00Z">
              <Study OID="S.SOA">
                <GlobalVariables>
                  <StudyName>SOA ODM Study</StudyName>
                  <StudyDescription>SOA</StudyDescription>
                  <ProtocolName>SOA-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.2" Name="v2">
                  <FormDef OID="F.VS" Name="Vital Signs (Seated)" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                    <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.DIA" OrderNumber="2" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.PULSE" OrderNumber="3" Mandatory="No"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                    <Alias Context="prompt" Name="Seated Systolic Blood Pressure"/>
                  </ItemDef>
                  <ItemDef OID="IT.DIA" Name="DIABP" DataType="integer">
                    <Alias Context="prompt" Name="Seated Diastolic Blood Pressure"/>
                  </ItemDef>
                  <ItemDef OID="IT.PULSE" Name="PULSE" DataType="integer">
                    <Alias Context="prompt" Name="Pulse Rate"/>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var service = new ProtocolDesignImporterService(new CdiscLibraryTemplateCatalog());
        var first = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: Encoding.UTF8.GetBytes(initialOdm),
                FileName: "soa-v1.xml",
                Format: ProtocolImportFormat.OdmXml),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        first.IsSuccess.Should().BeTrue(string.Join("; ", first.Problems));
        first.Design!.Epochs.Select(e => (e.Id, e.Name, e.Order)).Should().Equal(
            ("Epoch.scheduled", "Scheduled", 1),
            ("Epoch.Treatment", "Treatment", 2));
        first.Design.PlannedActivities.Select(a => (a.Id, a.EpochId)).Should().Equal(
            ("SE.SCR", "Epoch.scheduled"),
            ("SE.WK4", "Epoch.Treatment"));
        first.Design.PlannedActivities.Should().OnlyContain(a => a.CollectionSpecIds.SequenceEqual(new[] { "F.VS" }));

        var merged = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                RawBytes: Encoding.UTF8.GetBytes(renamedFormOdm),
                FileName: "soa-v2.xml",
                Format: ProtocolImportFormat.OdmXml,
                BaseDesign: first.Design,
                ReplaceForms: false),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        merged.IsSuccess.Should().BeTrue(string.Join("; ", merged.Problems));
        merged.Design!.CollectionSpecs.Should().ContainSingle(s => s.Id == "F.VS" && s.Name == "Vital Signs (Seated)");
        merged.Design.CollectionSpecs[0].Items.Select(i => i.Id).Should().Equal("IT.SYS", "IT.DIA", "IT.PULSE");
        merged.Design.CollectionSpecs[0].Items.Select(i => i.Id).Should().NotContain(id => id.EndsWith("_2", StringComparison.Ordinal));
        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(merged.Design)).IsValid.Should().BeTrue();
    }

    private static ProtocolDesign BuildCadence101LikeDesign()
    {
        var systolic = new ConceptRef(ConceptId.New(), 1);
        var diastolic = new ConceptRef(ConceptId.New(), 1);
        var heartRate = new ConceptRef(ConceptId.New(), 1);
        var examFinding = new ConceptRef(ConceptId.New(), 1);
        var adverseEventTerm = new ConceptRef(ConceptId.New(), 1);
        var adverseEventSeverity = new ConceptRef(ConceptId.New(), 1);
        var medicationName = new ConceptRef(ConceptId.New(), 1);
        var medicationIndication = new ConceptRef(ConceptId.New(), 1);

        return new ProtocolDesign
        {
            Title = "Cardiovascular outcomes clinical protocol",
            Objectives =
            [
                new Objective("obj.primary", ObjectiveLevel.Primary, "Evaluate blood pressure control over 4 weeks of treatment."),
            ],
            Endpoints =
            [
                new Endpoint("ep.sbp_change", "obj.primary", ObjectiveLevel.Primary, "Change from baseline in systolic blood pressure at Week 4.", systolic),
            ],
            Estimands =
            [
                new Estimand(
                    "est.primary",
                    "ep.sbp_change",
                    "pop.itt",
                    ["arm.active", "arm.placebo"],
                    "Change in seated systolic blood pressure from Day 1 to Week 4",
                    [new IntercurrentEvent("Treatment discontinuation", IntercurrentEventStrategy.TreatmentPolicy)],
                    "Difference in mean change between arms"),
            ],
            Populations =
            [
                new Population("pop.itt", "Intent-to-treat", ["crit.htn"]),
            ],
            EligibilityCriteria =
            [
                new EligibilityCriterion("crit.htn", CriterionCategory.Inclusion, "Seated systolic blood pressure >= 140 mm[Hg] at screening.", [systolic]),
            ],
            Interventions =
            [
                new Intervention("int.investigational", "Investigational antihypertensive", Dose: new Dose(10m, "mg"), Route: "Oral", Regimen: "Once daily"),
                new Intervention("int.placebo", "Matching placebo", Route: "Oral", Regimen: "Once daily"),
            ],
            Arms =
            [
                new Arm("arm.active", "Active treatment", ArmType.Experimental, ["int.investigational"]),
                new Arm("arm.placebo", "Placebo", ArmType.PlaceboComparator, ["int.placebo"]),
            ],
            SafetyRules =
            [
                new ProtocolRule("rule.safety_monitoring", "Monitor treatment-emergent adverse events and concomitant medications.", [adverseEventTerm, medicationName]),
            ],
            Epochs =
            [
                new Epoch("ep.screening", "Screening", 1),
                new Epoch("ep.treatment", "Treatment", 2),
                new Epoch("ep.followup", "Follow-up", 3),
            ],
            PlannedActivities =
            [
                new PlannedActivity("act.screening", "Screening Visit", "ep.screening", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(-7, 7, 0)),
                new PlannedActivity("act.day1", "Day 1 Baseline", "ep.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(1, 0, 1)),
                new PlannedActivity("act.week2", "Week 2 Visit", "ep.treatment", [ActivityLocation.Site], ["cs.vitals"], new ActivityTiming(14, 2, 2)),
                new PlannedActivity("act.week4", "Week 4 Primary Endpoint", "ep.treatment", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(28, 3, 3)),
                new PlannedActivity("act.eos", "End of Study", "ep.followup", [ActivityLocation.Site], ["cs.vitals", "cs.physical_exam"], new ActivityTiming(42, 3, 5)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [
                        new CollectionItem("item.sbp", systolic, "mm[Hg]", Required: true),
                        new CollectionItem("item.dbp", diastolic, "mm[Hg]", Required: true),
                        new CollectionItem("item.hr", heartRate, "/min", Required: true),
                        new CollectionItem("item.vs_notes", systolic, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { EndpointIds = ["ep.sbp_change"], EstimandIds = ["est.primary"] }),
                new CollectionSpec(
                    "cs.physical_exam",
                    "Physical Examination",
                    [
                        new CollectionItem("item.pe_summary", examFinding, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.pe_clinically_significant", examFinding, Required: true, ValueKind: ObservationValueKind.Boolean),
                    ],
                    new DataPurpose { OperationalNeed = "Baseline and follow-up clinical assessment" }),
                new CollectionSpec(
                    "cs.adverse_event",
                    "Adverse Event Log",
                    [
                        new CollectionItem("item.ae_term", adverseEventTerm, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.ae_severity", adverseEventSeverity, Required: true, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { SafetyRuleIds = ["rule.safety_monitoring"] }),
                new CollectionSpec(
                    "cs.concomitant_meds",
                    "Concomitant Medications",
                    [
                        new CollectionItem("item.cm_name", medicationName, Required: true, ValueKind: ObservationValueKind.Text),
                        new CollectionItem("item.cm_indication", medicationIndication, ValueKind: ObservationValueKind.Text),
                    ],
                    new DataPurpose { SafetyRuleIds = ["rule.safety_monitoring"] }),
            ],
        };
    }

    [Fact]
    public async Task OdmFormalExpressionParser_AcceptsMaxDepthBoundary_AndWrappedRulesPassRulePackRead()
    {
        var maxDepthParens = new string('(', OdmFormalExpressionParser.MaxDepth)
            + "IT.SYS == 120"
            + new string(')', OdmFormalExpressionParser.MaxDepth);
        OdmFormalExpressionParser.TryParse(maxDepthParens, null, null, out var parsedParens, out var parensErr).Should().BeTrue(parensErr);
        parsedParens.Should().NotBeNull();

        // 27 unary/function ops (54 JSON levels) + comparison (2 JSON levels) + variable (1 JSON level) = 57 JSON levels,
        // which hits System.Text.Json's exact 64-level limit after RangeCheck (+4) and RulePack (+3) wrapping.
        var maxWrappableUnary = new string('!', 27) + "IT.SYS == 120";
        var maxWrappableFunctions = string.Concat(Enumerable.Repeat("length(", 27))
            + "IT.SYS"
            + new string(')', 27)
            + " > 0";

        foreach (var (rangeExpr, condExpr) in new[]
        {
            (maxDepthParens, maxDepthParens),
            (maxWrappableUnary, maxWrappableFunctions),
        })
        {
            var odm = BuildSingleFormOdmWithExpressions(
                rangeCheckExpression: rangeExpr,
                conditionExpression: condExpr);

            var service = new ProtocolDesignImporterService();
            var result = await service.ImportAsync(
                new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(odm), FileName: "boundary-rules.xml"),
                AlwaysSponsorConcept(),
                TestContext.Current.CancellationToken);

            result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
            result.RulePack.Should().NotBeNull();
            result.RulePack!.RuleCount.Should().Be(2);

            var (pack, packProblems) = RulePackJson.Read(result.RulePack.Content);
            packProblems.Should().BeEmpty();
            pack.Should().NotBeNull();
            pack!.Rules.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task OdmFormalExpressionParser_RejectsDeeplyNestedAndWideExpressions_WithoutStackOverflow()
    {
        int overDepth = OdmFormalExpressionParser.MaxDepth + 1;
        string[] oversizedExpressions =
        [
            new string('(', overDepth) + "IT.SYS == 120" + new string(')', overDepth),
            new string('(', 4000) + "IT.SYS == 120" + new string(')', 4000),
            new string('!', 28) + "IT.SYS == 120",
            new string('!', overDepth) + "true",
            new string('!', 4000) + "true",
            string.Concat(Enumerable.Repeat("not ", overDepth)) + "true",
            string.Concat(Enumerable.Repeat("not(", overDepth)) + "true" + new string(')', overDepth),
            new string('-', overDepth) + "IT.SYS",
            string.Concat(Enumerable.Repeat("length(", 28)) + "IT.SYS" + new string(')', 28) + " > 0",
            string.Concat(Enumerable.Repeat("length(", overDepth)) + "IT.SYS" + new string(')', overDepth),
            string.Concat(Enumerable.Repeat("length(", 4000)) + "IT.SYS" + new string(')', 4000),
            new string('(', overDepth - 1) + "IT.SYS in (1, 2)" + new string(')', overDepth - 1),
            string.Join(" + ", Enumerable.Repeat("1", overDepth + 1)) + " > 0",
            string.Join(" and ", Enumerable.Repeat("IT.SYS == 120", 1400)),
            string.Join(" or ", Enumerable.Repeat("true", RuleExpressionJson.MaxNodes)),
            string.Join(" + ", Enumerable.Repeat("1", (OdmFormalExpressionParser.MaxTokens / 2) + 2)),
            new string(' ', OdmFormalExpressionParser.MaxExpressionLength + 10) + "IT.SYS == 120",
        ];

        foreach (var expr in oversizedExpressions)
        {
            OdmFormalExpressionParser.TryParse(expr, null, null, out var parsed, out var error).Should().BeFalse();
            parsed.Should().BeNull();
            error.Should().NotBeNullOrWhiteSpace();
        }

        // Unsupported vendor syntax within bounds still skips with an import note rather than failing the ODM import.
        var vendorOdm = BuildSingleFormOdmWithExpressions(
            rangeCheckExpression: "VendorCustomCheck(IT.SYS)",
            conditionExpression: null);
        var service = new ProtocolDesignImporterService();
        var vendorResult = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(vendorOdm), FileName: "vendor-fn.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);
        vendorResult.IsSuccess.Should().BeTrue(string.Join("; ", vendorResult.Problems));
        vendorResult.Notes.Should().Contain(n => n.Message.Contains("VendorCustomCheck", StringComparison.Ordinal));
    }

    [Fact]
    public void OdmFormalExpressionParser_RewriteVariables_EnforcesMaxDepthAndMaxNodes()
    {
        RuleExpression atMaxDepth = new VariableExpression(".");
        for (int i = 0; i < RuleExpressionJson.MaxDepth; i++)
        {
            atMaxDepth = new NotExpression(atMaxDepth);
        }

        var rewritten = OdmFormalExpressionParser.RewriteVariables(atMaxDepth, name => $"mapped.{name}", "IT.SYS");
        RuleExpressionJson.Variables(rewritten).Should().Equal("mapped.IT.SYS");

        var overMaxDepth = new NotExpression(atMaxDepth);
        Action actDeep = () => OdmFormalExpressionParser.RewriteVariables(overMaxDepth, static name => name);
        actDeep.Should().Throw<FormatException>()
            .WithMessage($"*nested more than {RuleExpressionJson.MaxDepth} levels*");

        var maxNodeOperands = Enumerable.Range(0, RuleExpressionJson.MaxNodes - 1)
            .Select(_ => (RuleExpression)new LiteralExpression(RuleValue.True))
            .ToList();
        var atMaxNodes = new LogicalExpression(LogicalOperator.Or, maxNodeOperands);
        OdmFormalExpressionParser.RewriteVariables(atMaxNodes, static name => name).Should().NotBeNull();

        var overMaxNodes = new LogicalExpression(
            LogicalOperator.Or,
            [.. maxNodeOperands, new LiteralExpression(RuleValue.True)]);
        Action actWide = () => OdmFormalExpressionParser.RewriteVariables(overMaxNodes, static name => name);
        actWide.Should().Throw<FormatException>()
            .WithMessage($"*more than {RuleExpressionJson.MaxNodes} nodes*");
    }

    private static string BuildSingleFormOdmWithExpressions(string rangeCheckExpression, string? conditionExpression)
    {
        var escapedRange = new XText(rangeCheckExpression).ToString();
        var conditionItemAttr = conditionExpression is not null
            ? " CollectionExceptionConditionOID=\"CD.TEST\""
            : string.Empty;
        var conditionBlock = conditionExpression is not null
            ? $"""
                  <ConditionDef OID="CD.TEST" Name="Test Condition">
                    <Description><TranslatedText xml:lang="en">Conditional check</TranslatedText></Description>
                    <FormalExpression Context="XPath">{new XText(conditionExpression)}</FormalExpression>
                  </ConditionDef>
              """
            : string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" FileOID="ODM.EXPR" FileType="Snapshot" ODMVersion="1.3.2">
              <Study OID="S.EXPR">
                <GlobalVariables>
                  <StudyName>Expression Bounds Study</StudyName>
                  <StudyDescription>Bounds</StudyDescription>
                  <ProtocolName>EXPR-101</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                    <ItemRef ItemOID="IT.SYS" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.DIA" OrderNumber="2" Mandatory="No"{conditionItemAttr}/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYS" Name="SYSBP" DataType="integer">
                    <Question><TranslatedText xml:lang="en">Systolic BP</TranslatedText></Question>
                    <RangeCheck SoftHard="Hard">
                      <FormalExpression Context="XPath">{escapedRange}</FormalExpression>
                      <ErrorMessage><TranslatedText xml:lang="en">Invalid systolic BP.</TranslatedText></ErrorMessage>
                    </RangeCheck>
                  </ItemDef>
                  <ItemDef OID="IT.DIA" Name="DIABP" DataType="integer">
                    <Question><TranslatedText xml:lang="en">Diastolic BP</TranslatedText></Question>
                  </ItemDef>
            {conditionBlock}
                </MetaDataVersion>
              </Study>
            </ODM>
            """;
    }
}
