using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Core.Domain.Toxicity;

public class LabToxicityGradingEngine
{
    private readonly Dictionary<string, ToxicityRuleDefinition> _rules;

    public LabToxicityGradingEngine(IEnumerable<ToxicityRuleDefinition>? customRules = null)
    {
        _rules = BuildStandardCtcaeRules();
        if (customRules != null)
        {
            foreach (var rule in customRules)
            {
                _rules[rule.TestCode.Trim().ToUpperInvariant()] = rule;
            }
        }
    }

    private static Dictionary<string, ToxicityRuleDefinition> BuildStandardCtcaeRules()
    {
        var rules = new Dictionary<string, ToxicityRuleDefinition>(StringComparer.OrdinalIgnoreCase);

        rules["ALT"] = new ToxicityRuleDefinition
        {
            TestCode = "ALT", TestName = "Alanine Aminotransferase", Category = ToxicityCategory.HEPATIC, Scale = "CTCAE_V5", MeddraTerm = "Alanine aminotransferase increased",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, ">1.0 to 3.0x ULN", "ULN_MULTIPLIER", 1.0, 3.0),
                new(CTCAEGrade.GRADE_2, ">3.0 to 5.0x ULN", "ULN_MULTIPLIER", 3.0, 5.0),
                new(CTCAEGrade.GRADE_3, ">5.0 to 20.0x ULN - Severe", "ULN_MULTIPLIER", 5.0, 20.0),
                new(CTCAEGrade.GRADE_4, ">20.0x ULN - Life-threatening", "ULN_MULTIPLIER", 20.0, null)
            }
        };

        rules["AST"] = new ToxicityRuleDefinition
        {
            TestCode = "AST", TestName = "Aspartate Aminotransferase", Category = ToxicityCategory.HEPATIC, Scale = "CTCAE_V5", MeddraTerm = "Aspartate aminotransferase increased",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, ">1.0 to 3.0x ULN", "ULN_MULTIPLIER", 1.0, 3.0),
                new(CTCAEGrade.GRADE_2, ">3.0 to 5.0x ULN", "ULN_MULTIPLIER", 3.0, 5.0),
                new(CTCAEGrade.GRADE_3, ">5.0 to 20.0x ULN - Severe", "ULN_MULTIPLIER", 5.0, 20.0),
                new(CTCAEGrade.GRADE_4, ">20.0x ULN - Life-threatening", "ULN_MULTIPLIER", 20.0, null)
            }
        };

        rules["BILIRUBIN"] = new ToxicityRuleDefinition
        {
            TestCode = "BILIRUBIN", TestName = "Total Bilirubin", Category = ToxicityCategory.HEPATIC, Scale = "CTCAE_V5", MeddraTerm = "Blood bilirubin increased",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, ">1.0 to 1.5x ULN", "ULN_MULTIPLIER", 1.0, 1.5),
                new(CTCAEGrade.GRADE_2, ">1.5 to 3.0x ULN", "ULN_MULTIPLIER", 1.5, 3.0),
                new(CTCAEGrade.GRADE_3, ">3.0 to 10.0x ULN - Severe", "ULN_MULTIPLIER", 3.0, 10.0),
                new(CTCAEGrade.GRADE_4, ">10.0x ULN - Life-threatening", "ULN_MULTIPLIER", 10.0, null)
            }
        };

        rules["CREATININE"] = new ToxicityRuleDefinition
        {
            TestCode = "CREATININE", TestName = "Serum Creatinine", Category = ToxicityCategory.RENAL, Scale = "CTCAE_V5", MeddraTerm = "Blood creatinine increased",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, ">1.0 to 1.5x ULN", "ULN_MULTIPLIER", 1.0, 1.5),
                new(CTCAEGrade.GRADE_2, ">1.5 to 3.0x ULN", "ULN_MULTIPLIER", 1.5, 3.0),
                new(CTCAEGrade.GRADE_3, ">3.0 to 6.0x ULN - Severe", "ULN_MULTIPLIER", 3.0, 6.0),
                new(CTCAEGrade.GRADE_4, ">6.0x ULN - Life-threatening / Dialysis", "ULN_MULTIPLIER", 6.0, null)
            }
        };

        rules["PLATELETS"] = new ToxicityRuleDefinition
        {
            TestCode = "PLATELETS", TestName = "Platelet Count", Category = ToxicityCategory.HEMATOLOGY, Scale = "CTCAE_V5", MeddraTerm = "Platelet count decreased",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, "<LLN - 75,000/mm3", "ABSOLUTE_RANGE", MinValue: 75000, MaxValue: 150000),
                new(CTCAEGrade.GRADE_2, "50,000 - 75,000/mm3", "ABSOLUTE_RANGE", MinValue: 50000, MaxValue: 75000),
                new(CTCAEGrade.GRADE_3, "25,000 - 50,000/mm3 - Severe", "ABSOLUTE_RANGE", MinValue: 25000, MaxValue: 50000),
                new(CTCAEGrade.GRADE_4, "<25,000/mm3 - Life-threatening", "ABSOLUTE_RANGE", MinValue: 0, MaxValue: 25000)
            }
        };

        rules["POTASSIUM_LOW"] = new ToxicityRuleDefinition
        {
            TestCode = "POTASSIUM_LOW", TestName = "Potassium (Hypokalemia)", Category = ToxicityCategory.ELECTROLYTES, Scale = "CTCAE_V5", MeddraTerm = "Hypokalaemia",
            Grades = new List<GradeCutoff>
            {
                new(CTCAEGrade.GRADE_1, "<LLN - 3.0 mmol/L", "ABSOLUTE_RANGE", MinValue: 3.0, MaxValue: 3.5),
                new(CTCAEGrade.GRADE_2, "Symptomatic or <3.0 mmol/L", "ABSOLUTE_RANGE", MinValue: 3.0, MaxValue: 3.5),
                new(CTCAEGrade.GRADE_3, "<3.0 - 2.5 mmol/L - Severe", "ABSOLUTE_RANGE", MinValue: 2.5, MaxValue: 3.0),
                new(CTCAEGrade.GRADE_4, "<2.5 mmol/L - Life-threatening / Arrhythmia", "ABSOLUTE_RANGE", MinValue: 0, MaxValue: 2.5)
            }
        };

        return rules;
    }

    public ToxicityAssessmentResult EvaluateObservation(
        string testCode,
        double value,
        string unit,
        double? refLow = null,
        double? refHigh = null,
        double? baselineValue = null,
        ToxicityRuleDefinition? ruleOverride = null)
    {
        string codeU = testCode.Trim().ToUpperInvariant();
        var rule = ruleOverride ?? (_rules.TryGetValue(codeU, out var r) ? r : null);

        if (rule == null)
        {
            if (codeU is "SGPT" or "ALAT") rule = _rules.GetValueOrDefault("ALT");
            else if (codeU is "SGOT" or "ASAT") rule = _rules.GetValueOrDefault("AST");
            else if (codeU is "TBIL" or "TOTAL_BILIRUBIN" or "BILI") rule = _rules.GetValueOrDefault("BILIRUBIN");
            else if (codeU is "CREAT" or "SERUM_CREATININE" or "SCR") rule = _rules.GetValueOrDefault("CREATININE");
            else if (codeU is "PLT" or "THROMBOCYTES") rule = _rules.GetValueOrDefault("PLATELETS");
        }

        string testTitle = rule?.TestName ?? testCode;

        if (rule == null || rule.Grades.Count == 0)
        {
            return new ToxicityAssessmentResult(
                TestCode: testCode,
                TestName: testTitle,
                ObservedValue: value,
                Unit: unit,
                Description: "Within reference limits or unconfigured toxicity rule",
                RefLow: refLow,
                RefHigh: refHigh,
                BaselineValue: baselineValue,
                Grade: CTCAEGrade.GRADE_0,
                EvalTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        CTCAEGrade matchedGrade = CTCAEGrade.GRADE_0;
        string matchedDesc = "Within normal limits";

        var sortedGrades = rule.Grades.OrderByDescending(g => (int)g.Grade).ToList();

        foreach (var cutoff in sortedGrades)
        {
            string ctype = cutoff.CriteriaType;

            if (ctype == "ULN_MULTIPLIER")
            {
                if (refHigh is null or <= 0) continue;
                double ratio = value / refHigh.Value;
                if (ratio > (cutoff.MinMultiplier ?? 1.0) && (cutoff.MaxMultiplier == null || ratio <= cutoff.MaxMultiplier.Value))
                {
                    matchedGrade = cutoff.Grade;
                    matchedDesc = $"{cutoff.Description} ({ratio:F2}x ULN)";
                    break;
                }
            }
            else if (ctype == "LLN_MULTIPLIER")
            {
                if (refLow is null or <= 0) continue;
                double ratio = value / refLow.Value;
                if (ratio < (cutoff.MaxMultiplier ?? 1.0) && (cutoff.MinMultiplier == null || ratio >= cutoff.MinMultiplier.Value))
                {
                    matchedGrade = cutoff.Grade;
                    matchedDesc = $"{cutoff.Description} ({ratio:F2}x LLN)";
                    break;
                }
            }
            else if (ctype == "ABSOLUTE_RANGE")
            {
                double minV = cutoff.MinValue ?? double.NegativeInfinity;
                double maxV = cutoff.MaxValue ?? double.PositiveInfinity;
                if (value >= minV && value <= maxV)
                {
                    matchedGrade = cutoff.Grade;
                    matchedDesc = $"{cutoff.Description} ({value} {unit})";
                    break;
                }
            }
        }

        bool isDeltaAlert = false;
        double? deltaPct = null;
        if (baselineValue is > 0)
        {
            deltaPct = Math.Round(((value - baselineValue.Value) / baselineValue.Value) * 100.0, 2);
            if (Math.Abs(deltaPct.Value) >= 50.0)
            {
                isDeltaAlert = true;
            }
        }

        bool isCritical = matchedGrade is CTCAEGrade.GRADE_3 or CTCAEGrade.GRADE_4 or CTCAEGrade.GRADE_5;
        AdverseEventDraftPayload? aeDraft = null;

        if (isCritical)
        {
            string severity = matchedGrade == CTCAEGrade.GRADE_4 ? "LIFE_THREATENING" : "SEVERE";
            aeDraft = new AdverseEventDraftPayload(
                TestCode: testCode,
                TestName: testTitle,
                ObservedValue: value,
                Unit: unit,
                Grade: matchedGrade,
                Severity: severity,
                MeddraTerm: rule.MeddraTerm ?? $"{testTitle} abnormal",
                IsSerious: true,
                HysLawIndicated: false,
                DraftTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        return new ToxicityAssessmentResult(
            TestCode: testCode,
            TestName: testTitle,
            ObservedValue: value,
            Unit: unit,
            Description: matchedDesc,
            RefLow: refLow,
            RefHigh: refHigh,
            BaselineValue: baselineValue,
            Grade: matchedGrade,
            Scale: rule.Scale,
            IsCriticalAeTrigger: isCritical,
            IsDeltaCheckAlert: isDeltaAlert,
            DeltaPercentage: deltaPct,
            AeDraft: aeDraft,
            EvalTimestamp: DateTime.UtcNow.ToString("o")
        );
    }

    public static HysLawEvaluation EvaluateHysLaw(
        double alt, double ast, double tbil, double alkp,
        double altUln, double astUln, double tbilUln, double alkpUln)
    {
        if (altUln <= 0 || astUln <= 0 || tbilUln <= 0 || alkpUln <= 0)
        {
            return new HysLawEvaluation(
                AltAstRatioUln: 0.0, TbilRatioUln: 0.0, AlkpRatioUln: 0.0,
                AlertLevel: "NORMAL", Rationale: "ULN reference bounds not fully defined for all hepatic analytes",
                EvalTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        double altRatio = alt / altUln;
        double astRatio = ast / astUln;
        double maxTransaminaseRatio = Math.Max(altRatio, astRatio);
        double tbilRatio = tbil / tbilUln;
        double alkpRatio = alkp / alkpUln;

        bool isTransaminaseElevated = maxTransaminaseRatio >= 3.0;
        bool isBiliElevated = tbilRatio >= 2.0;
        bool isNotCholestatic = alkpRatio < 2.0;

        if (isTransaminaseElevated && isBiliElevated && isNotCholestatic)
        {
            return new HysLawEvaluation(
                AltAstRatioUln: Math.Round(maxTransaminaseRatio, 2),
                TbilRatioUln: Math.Round(tbilRatio, 2),
                AlkpRatioUln: Math.Round(alkpRatio, 2),
                AlertLevel: "HYS_LAW_CRITICAL",
                IsHysLawMet: true,
                Rationale: $"Hy's Law DILI Criteria MET: ALT/AST {maxTransaminaseRatio:F2}x ULN (>=3.0x), " +
                           $"Total Bilirubin {tbilRatio:F2}x ULN (>=2.0x), and ALKP {alkpRatio:F2}x ULN (<2.0x).",
                EvalTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        if (isTransaminaseElevated && isBiliElevated)
        {
            return new HysLawEvaluation(
                AltAstRatioUln: Math.Round(maxTransaminaseRatio, 2),
                TbilRatioUln: Math.Round(tbilRatio, 2),
                AlkpRatioUln: Math.Round(alkpRatio, 2),
                AlertLevel: "POTENTIAL_DILI_WARNING",
                Rationale: $"Potential DILI with Cholestasis: ALT/AST {maxTransaminaseRatio:F2}x ULN (>=3.0x) " +
                           $"and TBIL {tbilRatio:F2}x ULN (>=2.0x), but ALKP is elevated ({alkpRatio:F2}x ULN >= 2.0x).",
                EvalTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        if (isTransaminaseElevated || isBiliElevated)
        {
            return new HysLawEvaluation(
                AltAstRatioUln: Math.Round(maxTransaminaseRatio, 2),
                TbilRatioUln: Math.Round(tbilRatio, 2),
                AlkpRatioUln: Math.Round(alkpRatio, 2),
                AlertLevel: "BORDERLINE_HEPATIC",
                Rationale: $"Borderline hepatic elevation: Transaminases {maxTransaminaseRatio:F2}x ULN, TBIL {tbilRatio:F2}x ULN.",
                EvalTimestamp: DateTime.UtcNow.ToString("o")
            );
        }

        return new HysLawEvaluation(
            AltAstRatioUln: Math.Round(maxTransaminaseRatio, 2),
            TbilRatioUln: Math.Round(tbilRatio, 2),
            AlkpRatioUln: Math.Round(alkpRatio, 2),
            AlertLevel: "NORMAL",
            Rationale: "Hepatic biomarkers within acceptable non-DILI boundaries.",
            EvalTimestamp: DateTime.UtcNow.ToString("o")
        );
    }
}
