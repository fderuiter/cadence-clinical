using System;
using System.Text.Json;

using Cadence.Kernel.Standards;
using Cadence.Rules.Expressions;
using Cadence.Rules.Packs;

namespace Cadence.TestSupport.Kernel.Rules;

/// <summary>A small rule pack covering each stage, severity and effective period, shared by the unit and integration tests.</summary>
internal static class SampleRulePacks
{
    // Built rather than written as a literal: S1075 treats this library as production code.
    private static readonly string GuidanceUrl = new UriBuilder(Uri.UriSchemeHttps, "example.org") { Path = "guidance" }.Uri.AbsoluteUri;

    public static RulePack Sample()
    {
        var citation = new SourceCitation("Sample public guidance", "4.1", GuidanceUrl);
        var sdtmig = new RuleApplicability(RuleStage.Standardization, "SDTMIG", "3.4", null, "VITALS");
        var anyDataset = new RuleApplicability(RuleStage.Standardization, null, null, null, null);
        return new RulePack(RuleAuthority.Cdisc, "Sample pack",
        [
            new Rule("R1", 3, RuleAuthority.Cdisc, citation, sdtmig, RuleSeverity.Error,
                Condition("""{"op":"<","args":[{"var":"SYS"},{"var":"DIA"}]}"""), "Systolic below diastolic.", "Check the values.", EffectivePeriod.Always),
            new Rule("R2", 1, RuleAuthority.Cdisc, citation, anyDataset, RuleSeverity.Warning,
                Condition("""{"op":"<","args":[{"var":"SYS"},100]}"""), "Low systolic.", null, EffectivePeriod.Always),
            new Rule("R3", 1, RuleAuthority.Cdisc, citation, anyDataset, RuleSeverity.Notice,
                Condition("""{"op":"is_not_empty","args":[{"var":"SYS"}]}"""), "Future rule.", null, new EffectivePeriod(new DateOnly(2027, 1, 1), null)),
            new Rule("R4", 1, RuleAuthority.Cdisc, citation, new RuleApplicability(RuleStage.Design, null, null, null, null), RuleSeverity.Error,
                Condition("true"), "Design-stage rule.", null, EffectivePeriod.Always),
        ]);
    }

    private static RuleExpression Condition(string json)
    {
        using var document = JsonDocument.Parse(json);
        return RuleExpressionJson.Parse(document.RootElement);
    }
}
