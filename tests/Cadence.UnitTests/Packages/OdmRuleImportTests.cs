using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Import;
using Cadence.Kernel.Validation;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Unit tests for CDISC ODM v1.3.2 <c>RangeCheck</c> and <c>ConditionDef</c> compilation into validated
/// <see cref="RulePack"/> definitions and in-memory <see cref="CollectionValidationService"/> record projection.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OdmRuleImportTests
{
    private static ProtocolConceptResolver AlwaysSponsorConcept() =>
        (_, _) => ValueTask.FromResult(new ProtocolConceptResolution(new ConceptRef(ConceptId.New(), 1), UsedFallbackSponsorConcept: false));

    [Fact]
    public async Task OdmRangeChecksAndConditionDefs_CompileIntoValidExecutableRulePackWithoutSkippedNotes()
    {
        const string odm = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3"
                 FileOID="ODM.RULES.1"
                 FileType="Snapshot"
                 ODMVersion="1.3.2"
                 CreationDateTime="2026-01-15T00:00:00Z"> <!-- deid-ignore -->
              <Study OID="S.CARDIO">
                <GlobalVariables>
                  <StudyName>Cardio Edit Check Study</StudyName>
                  <StudyDescription>ODM edit checks</StudyDescription>
                  <ProtocolName>CARDIO-202</ProtocolName>
                </GlobalVariables>
                <BasicDefinitions>
                  <MeasurementUnit OID="MU.MMHG" Name="mm[Hg]">
                    <Symbol><TranslatedText xml:lang="en">mm[Hg]</TranslatedText></Symbol>
                  </MeasurementUnit>
                </BasicDefinitions>
                <MetaDataVersion OID="MDV.1" Name="v1">
                  <FormDef OID="F.VS" Name="Vital Signs" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.VS" OrderNumber="1" Mandatory="Yes"/>
                  </FormDef>
                  <FormDef OID="F.AE" Name="Adverse Events" Repeating="No">
                    <ItemGroupRef ItemGroupOID="IG.AE" OrderNumber="1" Mandatory="No"/>
                  </FormDef>
                  <ItemGroupDef OID="IG.VS" Name="Vitals" Repeating="No">
                    <ItemRef ItemOID="IT.SYSBP" OrderNumber="1" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.DIABP" OrderNumber="2" Mandatory="Yes"/>
                    <ItemRef ItemOID="IT.POS" OrderNumber="3" Mandatory="No"/>
                    <ItemRef ItemOID="IT.PREG" OrderNumber="4" Mandatory="No" CollectionExceptionConditionOID="CD.NOT_PREG_ELIGIBLE"/>
                  </ItemGroupDef>
                  <ItemGroupDef OID="IG.AE" Name="AE" Repeating="No">
                    <ItemRef ItemOID="IT.AETERM" OrderNumber="1" Mandatory="No"/>
                  </ItemGroupDef>
                  <ItemDef OID="IT.SYSBP" Name="SYSBP" DataType="integer">
                    <Question><TranslatedText xml:lang="en">Systolic BP</TranslatedText></Question>
                    <MeasurementUnitRef MeasurementUnitOID="MU.MMHG"/>
                    <RangeCheck Comparator="GE" SoftHard="Soft">
                      <CheckValue>70</CheckValue>
                      <ErrorMessage><TranslatedText xml:lang="en">Systolic BP is below 70 mm[Hg].</TranslatedText></ErrorMessage>
                    </RangeCheck>
                    <RangeCheck Comparator="LE" SoftHard="Hard">
                      <CheckValue>220</CheckValue>
                      <ErrorMessage><TranslatedText xml:lang="en">Systolic BP exceeds 220 mm[Hg].</TranslatedText></ErrorMessage>
                    </RangeCheck>
                  </ItemDef>
                  <ItemDef OID="IT.DIABP" Name="DIABP" DataType="integer">
                    <Question><TranslatedText xml:lang="en">Diastolic BP</TranslatedText></Question>
                    <MeasurementUnitRef MeasurementUnitOID="MU.MMHG"/>
                    <RangeCheck Comparator="GT" SoftHard="Soft">
                      <CheckValue>35</CheckValue>
                    </RangeCheck>
                    <RangeCheck Comparator="LT" SoftHard="Soft">
                      <CheckValue>135</CheckValue>
                    </RangeCheck>
                  </ItemDef>
                  <ItemDef OID="IT.POS" Name="VSPOS" DataType="text">
                    <Question><TranslatedText xml:lang="en">Body Position</TranslatedText></Question>
                    <RangeCheck Comparator="IN" SoftHard="Soft">
                      <CheckValue>SITTING</CheckValue>
                      <CheckValue>SUPINE</CheckValue>
                      <ErrorMessage><TranslatedText xml:lang="en">Position must be SITTING or SUPINE.</TranslatedText></ErrorMessage>
                    </RangeCheck>
                  </ItemDef>
                  <ItemDef OID="IT.PREG" Name="PREGTEST" DataType="text">
                    <Question><TranslatedText xml:lang="en">Pregnancy Test Result</TranslatedText></Question>
                  </ItemDef>
                  <ItemDef OID="IT.AETERM" Name="AETERM" DataType="text">
                    <Question><TranslatedText xml:lang="en">Adverse Event Term</TranslatedText></Question>
                  </ItemDef>
                  <ConditionDef OID="CD.NOT_PREG_ELIGIBLE" Name="Supine Skips Pregnancy">
                    <Description><TranslatedText xml:lang="en">Pregnancy test should be omitted when supine.</TranslatedText></Description>
                    <FormalExpression Context="XPath">IT.POS = 'SUPINE'</FormalExpression>
                  </ConditionDef>
                  <ConditionDef OID="CD.PULSE_PRESSURE" Name="Inverted Blood Pressure">
                    <Description><TranslatedText xml:lang="en">Systolic BP must exceed Diastolic BP.</TranslatedText></Description>
                    <FormalExpression Context="XPath">IT.SYSBP &lt;= IT.DIABP</FormalExpression>
                  </ConditionDef>
                  <ConditionDef OID="CD.HYPERTENSIVE_NO_AE" Name="Severe Hypertension Without AE">
                    <Description><TranslatedText xml:lang="en">Severe systolic elevation requires an AE entry.</TranslatedText></Description>
                    <FormalExpression Context="XPath">IT.SYSBP &gt; 200 and F.AE/IG.AE/IT.AETERM = ''</FormalExpression>
                  </ConditionDef>
                </MetaDataVersion>
              </Study>
            </ODM>
            """;

        var service = new ProtocolDesignImporterService();
        var result = await service.ImportAsync(
            new ProtocolDesignImportRequest(RawBytes: Encoding.UTF8.GetBytes(odm), FileName: "cardio.xml"),
            AlwaysSponsorConcept(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Problems));
        result.Notes.Should().NotContain(n => n.Message.Contains("not yet imported as executable", StringComparison.OrdinalIgnoreCase));
        result.RulePack.Should().NotBeNull();
        result.RulePack!.RuleCount.Should().BeGreaterThanOrEqualTo(8);

        var (pack, parseProblems) = RulePackJson.Read(result.RulePack.Content);
        parseProblems.Should().BeEmpty();
        pack.Should().NotBeNull();
        pack!.Rules.Should().Contain(r => r.Severity == ValidationSeverity.Warning);
        pack.Rules.Should().Contain(r => r.Severity == ValidationSeverity.Error);

        var vsSpec = result.Design!.CollectionSpecs.Single(s => s.Name == "Vital Signs");
        var aeSpec = result.Design.CollectionSpecs.Single(s => s.Name == "Adverse Events");
        var sbpItem = vsSpec.Items[0].Id;
        var dbpItem = vsSpec.Items[1].Id;
        var posItem = vsSpec.Items[2].Id;
        var pregItem = vsSpec.Items[3].Id;
        var aeItem = aeSpec.Items[0].Id;

        var evaluator = new DotNetRuleEvaluator();
        var ctx = new RuleEvaluationContext(ValidationStage.Collection, new DateOnly(2026, 3, 1));

        var validRecord = new RuleRecord(
            vsSpec.Id,
            "form-valid",
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                [sbpItem] = RuleValue.FromNumber(120m),
                [dbpItem] = RuleValue.FromNumber(80m),
                [posItem] = RuleValue.FromText("SITTING"),
                [pregItem] = RuleValue.Null,
                [$"{aeSpec.Id}.{aeItem}"] = RuleValue.FromText("None"),
            });
        evaluator.Evaluate(pack, ctx, [validRecord]).Should().BeEmpty();

        var violatingRecord = new RuleRecord(
            vsSpec.Id,
            "form-invalid",
            new Dictionary<string, RuleValue>(StringComparer.Ordinal)
            {
                [sbpItem] = RuleValue.FromNumber(230m),
                [dbpItem] = RuleValue.FromNumber(235m),
                [posItem] = RuleValue.FromText("STANDING"),
                [pregItem] = RuleValue.FromText("NEGATIVE"),
                [$"{aeSpec.Id}.{aeItem}"] = RuleValue.Null,
            });
        var violations = evaluator.Evaluate(pack, ctx, [violatingRecord]);
        violations.Should().Contain(v => v.Explanation.Contains("220", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Explanation.Contains("SITTING or SUPINE", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Explanation.Contains("Diastolic BP", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Explanation.Contains("Severe systolic elevation requires an AE entry", StringComparison.Ordinal));
    }
}
