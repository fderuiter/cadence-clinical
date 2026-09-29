using System;
using System.Collections.Generic;

using Cadence.Core.Domain.DerivedMetrics;
using Cadence.Core.Domain.Financial;
using Cadence.Core.Domain.Fsm;
using Cadence.Core.Domain.Operations;
using Cadence.Core.Domain.Randomization;
using Cadence.Core.Domain.Rules;
using Cadence.Core.Domain.Sla;
using Cadence.Core.Domain.Supply;
using Cadence.Core.Domain.Timeline;
using Cadence.Core.Domain.Toxicity;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DomainEngineUnitTests
{
    [Fact]
    [GxPRequirement("PRD-RND-001")]
    public void BoinEngine_CalculateBoinBoundaries_ReturnsExpectedBoundaries()
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(0.25);
        lambdaE.Should().BeInRange(0.19, 0.21);
        lambdaD.Should().BeInRange(0.29, 0.31);
    }

    [Fact]
    [GxPRequirement("PRD-RND-001")]
    public void BoinEngine_EvaluateDoseLevel_EscalatesWhenLowDltRate()
    {
        var config = new BOINConfig(TargetDltRate: 0.25);
        var doseLevels = new List<DoseLevelDefinition>
        {
            new(0, 10.0, "mg"),
            new(1, 20.0, "mg"),
            new(2, 40.0, "mg")
        };
        var currentState = new DoseCohortState(DoseIndex: 0, DoseAmount: 10.0, DoseUnit: "mg", EvaluatedSubjects: 3, ObservedDlts: 0);

        var rec = BoinEngine.EvaluateDoseLevel(config, doseLevels, currentState);
        rec.Action.Should().Be(DoseEscalationAction.ESCALATE);
        rec.RecommendedDoseIndex.Should().Be(1);
        rec.RecommendedDoseAmount.Should().Be(20.0);
    }

    [Fact]
    [GxPRequirement("PRD-RND-001")]
    public void BoinEngine_EvaluateDoseLevel_StopsTrialWhenExcessiveToxicityAtLowestDose()
    {
        var config = new BOINConfig(TargetDltRate: 0.25, StoppingCutoff: 0.95);
        var doseLevels = new List<DoseLevelDefinition>
        {
            new(0, 10.0, "mg"),
            new(1, 20.0, "mg")
        };
        var currentState = new DoseCohortState(DoseIndex: 0, DoseAmount: 10.0, DoseUnit: "mg", EvaluatedSubjects: 3, ObservedDlts: 3);

        var rec = BoinEngine.EvaluateDoseLevel(config, doseLevels, currentState);
        rec.Action.Should().Be(DoseEscalationAction.STOP_TRIAL);
        rec.IsTrialStopped.Should().BeTrue();
    }

    [Fact]
    [GxPRequirement("PRD-RND-002")]
    public void BlockRandomizationEngine_GeneratesCanonicalStratumKey()
    {
        var factors = new Dictionary<string, object>
        {
            { "GENDER", "F" },
            { "AGE_GROUP", "18-40" }
        };
        var active = new List<string> { "GENDER", "AGE_GROUP" };

        string key = BlockRandomizationEngine.GenerateCanonicalStratumKey(factors, active);
        key.Should().Be("AGE_GROUP=18-40|GENDER=F");
    }

    [Fact]
    [GxPRequirement("PRD-RND-002")]
    public void BlockRandomizationEngine_Allocate_AssignsArmWithChecksum()
    {
        var config = new StratifiedBlockConfig(
            ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
            StratificationFactors: new List<string> { "AGE" },
            BlockSizes: new List<int> { 2, 4 },
            Seed: 42
        );

        var request = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "AGE", "ADULT" } }
        );

        var result = BlockRandomizationEngine.Allocate(config, request);
        result.AssignedArm.Should().Match(arm => arm == "ARM_A" || arm == "ARM_B");
        result.StratumKey.Should().Be("AGE=ADULT");
        result.DeterministicChecksum.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [GxPRequirement("PRD-RND-003")]
    public void MinimizationEngine_Allocate_CalculatesImbalanceAndAllocates()
    {
        var config = new MinimizationConfig(
            ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
            StratificationFactors: new List<string> { "CENTER", "STAGE" },
            DistanceMetric: ImbalanceDistanceMetric.RANGE,
            PPreferred: 0.8,
            Seed: 101
        );

        var request = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-202",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "CENTER", "NY" }, { "STAGE", "III" } }
        );

        var result = MinimizationEngine.Allocate(config, request);
        result.AssignedArm.Should().Match(a => a == "ARM_A" || a == "ARM_B");
        result.ImbalanceScore.Should().NotBeNull();
        result.DeterministicChecksum.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [GxPRequirement("PRD-SUP-001")]
    public void DTPLogisticsEngine_CalculateMeanKineticTemperature_ComputesHaynesMkt()
    {
        var readings = new List<TemperatureReading>
        {
            new(4.0), new(5.0), new(6.0), new(5.5), new(4.5)
        };

        double mkt = DTPLogisticsEngine.CalculateMeanKineticTemperature(readings);
        mkt.Should().BeInRange(4.8, 5.3);
    }

    [Fact]
    [GxPRequirement("PRD-SUP-001")]
    public void DTPLogisticsEngine_EvaluateDtpDispatch_GeneratesTamperSealWhenEligible()
    {
        var readings = new List<TemperatureReading> { new(5.0), new(5.2) };
        var auth = DTPLogisticsEngine.EvaluateDtpDispatch(
            studyId: "STUDY-01", siteId: "SITE-01", subjectId: "SUBJ-01",
            kitNumber: "KIT-999", lotNumber: "LOT-100", daysUntilExpiry: 60,
            temperatureReadings: readings
        );

        auth.IsEligibleForDispatch.Should().BeTrue();
        auth.TamperSealId.Should().StartWith("SEAL-");
    }

    [Fact]
    [GxPRequirement("PRD-TOX-001")]
    public void LabToxicityGradingEngine_EvaluatesGrade3Alt_AndGeneratesAeDraft()
    {
        var engine = new LabToxicityGradingEngine();
        var result = engine.EvaluateObservation(
            testCode: "ALT",
            value: 300.0,
            unit: "U/L",
            refHigh: 50.0
        );

        result.Grade.Should().Be(CTCAEGrade.GRADE_3);
        result.IsCriticalAeTrigger.Should().BeTrue();
        result.AeDraft.Should().NotBeNull();
        result.AeDraft!.Severity.Should().Be("SEVERE");
    }

    [Fact]
    [GxPRequirement("PRD-TOX-001")]
    public void LabToxicityGradingEngine_EvaluateHysLaw_DetectsCriticalHysLawTriad()
    {
        var eval = LabToxicityGradingEngine.EvaluateHysLaw(
            alt: 200, ast: 180, tbil: 4.5, alkp: 100,
            altUln: 50, astUln: 50, tbilUln: 1.2, alkpUln: 120
        );

        eval.IsHysLawMet.Should().BeTrue();
        eval.AlertLevel.Should().Be("HYS_LAW_CRITICAL");
    }

    [Fact]
    [GxPRequirement("PRD-RUL-001")]
    public void RuleEvaluator_EvaluatesAstAndJsonLogic()
    {
        var evaluator = new RuleEvaluator();

        var astNode = new ExpressionNode
        {
            Type = "logical",
            Operator = "and",
            Operands = new List<ExpressionNode>
            {
                new() { Type = "comparison", Operator = ">=", Operands = new List<ExpressionNode> { new() { Type = "variable", VariableName = "age" }, new() { Type = "constant", Value = 18 } } },
                new() { Type = "comparison", Operator = "==", Operands = new List<ExpressionNode> { new() { Type = "variable", VariableName = "consent" }, new() { Type = "constant", Value = true } } }
            }
        };

        var context = new Dictionary<string, object?>
        {
            { "age", 25 },
            { "consent", true }
        };

        var result = evaluator.Evaluate(astNode, context);
        result.Should().Be(true);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void FinancialLedgerEngine_ProcessEvent_CalculatesTieredOverheadAndPayeeSplits()
    {
        var contract = new GrantContract(
            ContractId: "CTR-101",
            StudyId: "STUDY-001",
            SiteId: "SITE-01",
            EffectiveDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Currency: "USD",
            DefaultOverheadRate: 0.20m,
            DefaultWithholdingRate: 0.05m,
            OverheadTiers: new List<OverheadTier>
            {
                new("T1", 0.30m, 0.0m, 1000.0m),
                new("T2", 0.15m, 1000.0m, null)
            },
            HoldbackRule: new HoldbackReserveRule(HoldbackRate: 0.10m, CapAmount: 500.0m),
            PayeeSplits: new List<PayeeSplit>
            {
                new("PAYEE-1", "Primary Investigator Fund", 0.60m, IsPrimary: true),
                new("PAYEE-2", "Institutional Overhead Fund", 0.40m, IsPrimary: false)
            },
            FeeMatrix: new List<FeeMatrixItem>
            {
                new("VISIT_1", "Screening Visit", TriggerType.VISIT_COMPLETED, 1000.0m)
            }
        );

        contract.ValidatePayeeSplits().Value.Should().BeTrue();

        var engine = new FinancialLedgerEngine(contract);
        var clinicalEvent = new ClinicalEvent(
            EventId: "EVT-001",
            StudyId: "STUDY-001",
            SiteId: "SITE-01",
            TriggerType: TriggerType.VISIT_COMPLETED,
            OccurredAt: new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            SubjectId: "SUBJ-001",
            VisitId: "VISIT_1"
        );

        var lines = engine.ProcessEvent(clinicalEvent);
        lines.Should().HaveCount(2);

        var primaryLine = lines.Single(l => l.PayeeId == "PAYEE-1");
        primaryLine.Breakdown.BaseAmount.Should().Be(600.0m); // 60% of 1000
        primaryLine.Breakdown.OverheadAmount.Should().Be(180.0m); // 60% of 300
        primaryLine.AuditHash.Should().NotBeNullOrWhiteSpace();

        var batchSummary = engine.ProcessBatch([clinicalEvent]);
        batchSummary.TotalEventsProcessed.Should().Be(1);
        batchSummary.TotalBaseAmount.Should().Be(1000.0m);
        batchSummary.CryptographicDigest.Should().NotBeNullOrWhiteSpace();

        var releaseLine = engine.ReleaseHoldback(50.0m, "Midterm Release");
        releaseLine.Breakdown.NetPayable.Should().Be(50.0m);
        engine.HoldbackAccount.CurrentBalance.Should().Be(engine.HoldbackAccount.AccumulatedHoldback - 50.0m);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SubjectFsmEngine_Transition_ExecutesValidTransitionsWithPart11AuditEnvelope()
    {
        var states = new List<StateNode>
        {
            new("SCREENING", StateType.Initial),
            new("ENROLLED", StateType.Normal),
            new("COMPLETED", StateType.Terminal)
        };

        var transitions = new List<TransitionEdge>
        {
            new("ENROLL", "SCREENING", "ENROLLED",
                Guard: new ExpressionNode
                {
                    Type = "comparison",
                    Operator = "==",
                    Operands = new List<ExpressionNode>
                    {
                        new() { Type = "variable", VariableName = "eligible" },
                        new() { Type = "constant", Value = true }
                    }
                },
                AllowedRoles: new List<string> { "INVESTIGATOR" },
                RequiresReasonForChange: true,
                MinReasonLength: 10),
            new("COMPLETE", "ENROLLED", "COMPLETED", AllowedRoles: new List<string> { "INVESTIGATOR" })
        };

        var def = new StateMachineDefinition("FSM-SUBJ-01", "Subject Lifecycle", states, transitions, "SCREENING");
        var graphErrors = StateMachineValidator.ValidateStateMachineGraph(def);
        graphErrors.Should().BeEmpty();

        var engine = new SubjectFsmEngine(def);
        engine.IsValidState("SCREENING").Should().BeTrue();
        engine.IsTerminalState("COMPLETED").Should().BeTrue();

        var ctx = new TransitionContext(
            CurrentState: "SCREENING",
            Trigger: "ENROLL",
            UserId: "user-pi-01",
            UserRoles: new List<string> { "INVESTIGATOR" },
            ReasonForChange: "Subject passed all inclusion and exclusion criteria",
            Payload: new Dictionary<string, object?> { { "eligible", true } }
        );

        var result = engine.Transition(ctx);
        result.Success.Should().BeTrue();
        result.ToState.Should().Be("ENROLLED");
        result.AuditEnvelope.Should().NotBeNull();
        result.AuditEnvelope!.Sha256Digest.Should().NotBeNullOrWhiteSpace();
        result.AuditEnvelope.FromState.Should().Be("SCREENING");
        result.AuditEnvelope.ToState.Should().Be("ENROLLED");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SiteGreenlightEngine_EvaluateReadiness_ProducesCorrectCapabilitiesAndDeficiencies()
    {
        var criteria = new SiteStartupCriteria(
            StudyId: "STUDY-001",
            SiteId: "SITE-101",
            TargetProtocolVersion: "1.0",
            IrbApproval: new IrbApprovalRecord(
                IrbName: "Central IRB",
                ApprovalNumber: "IRB-2026-888",
                ProtocolVersion: "1.0",
                ApprovalDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpirationDate: new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            ),
            Contracts: new List<ContractRecord>
            {
                new("CTA-101", IsFullyExecuted: true, ContractType: "CTA", SponsorSigned: true, InstitutionSigned: true, PiSigned: true)
            },
            StaffQualifications: new List<StaffQualificationRecord>
            {
                new("STAFF-PI", "Dr. Jane Smith", StaffRole.PRINCIPAL_INVESTIGATOR, IsCvCurrent: true, IsMedicalLicenseActive: true, IsGcpCertified: true, FinancialDisclosureSigned: true, ProtocolTrainingCompleted: true, TrainedProtocolVersion: "1.0", DoaSigned: true)
            },
            Fda1572Signed: true,
            SiteInitiationVisitCompleted: true
        );

        var decision = SiteGreenlightEngine.EvaluateReadiness(criteria);
        decision.Status.Should().Be(GreenlightStatus.GREENLIGHT_CLEARED);
        decision.CanShipIp.Should().BeTrue();
        decision.CanScreenSubjects.Should().BeTrue();
        decision.CanEnrollSubjects.Should().BeTrue();
        decision.Deficiencies.Should().BeEmpty();
        decision.DigitalSignatureHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void SlaTrackingEngine_ComputeSlaDueDateAndCountdown_HandlesBusinessHoursAndEscalations()
    {
        var policy = new SlaPolicy(
            policyId: "SLA-SAFETY-01",
            name: "SAE Reporting Policy",
            baseHoursByPriority: new Dictionary<string, double> { { "CRITICAL", 24.0 }, { "HIGH", 48.0 } },
            warningThresholdPct: 75.0,
            criticalThresholdPct: 90.0,
            escalationTiers: new List<EscalationTier>
            {
                new("TIER-1", "Warning Notification", new EscalationAction(NotifyRoles: new List<string> { "SAFETY_LEAD" }), BreachThresholdPct: 75.0)
            }
        );

        var engine = new SlaTrackingEngine(policy);
        DateTime startDate = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        DateTime dueDate = engine.ComputeSlaDueDate(startDate, "CRITICAL");
        dueDate.Should().Be(startDate.AddHours(24.0));

        DateTime asOf = startDate.AddHours(20.0); // 20 / 24 = 83.3% elapsed -> WARNING
        var clock = engine.ComputeCountdownClock(startDate, dueDate, totalPausedSeconds: 0, asOf: asOf);

        clock.ElapsedPercentage.Should().Be(83.33);
        clock.AlertLevel.Should().Be(AlertLevel.WARNING);
        clock.IsOverdue.Should().BeFalse();

        var escalationResult = engine.EvaluateEscalations(clock, currentPriority: "CRITICAL");
        escalationResult.TriggeredTiers.Should().ContainSingle();
        escalationResult.NotificationRoles.Should().Contain("SAFETY_LEAD");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void DerivedMetricsEngine_Calculate_EvaluatesClinicalFormulasAndPlausibility()
    {
        var engine = new DerivedMetricsEngine();

        // 1. BMI Formula
        var bmiFormula = new DerivedFormulaDefinition(
            formulaId: "FORMULA-BMI",
            name: "Body Mass Index",
            outputVariable: "calculated_bmi",
            formulaType: DerivedFormulaType.BMI,
            inputs: new List<FormulaInputRequirement>
            {
                new("weight_kg", Unit: "kg", Plausibility: new PlausibilityRange(MinValue: 20.0, MaxValue: 300.0)),
                new("height_cm", Unit: "cm", Plausibility: new PlausibilityRange(MinValue: 50.0, MaxValue: 250.0))
            }
        );

        var bmiCtx = new Dictionary<string, object?> { { "weight_lbs", 154.32358 }, { "height_in", 70.86614 } }; // ~70 kg, ~180 cm
        var bmiResult = engine.Calculate(bmiFormula, bmiCtx);

        bmiResult.IsValid.Should().BeTrue();
        bmiResult.CalculatedValue.Should().NotBeNull();
        bmiResult.CalculatedValue!.Value.Should().BeInRange(21.0, 22.0);

        // 2. eGFR CKD-EPI 2021 Formula
        var egfrFormula = new DerivedFormulaDefinition(
            formulaId: "FORMULA-EGFR-2021",
            name: "eGFR CKD-EPI 2021",
            outputVariable: "egfr",
            formulaType: DerivedFormulaType.EGFR_CKD_EPI_2021,
            inputs: new List<FormulaInputRequirement>
            {
                new("scr", Unit: "mg/dL"),
                new("age", Unit: "years"),
                new("sex")
            }
        );

        var egfrCtx = new Dictionary<string, object?> { { "scr", 0.9 }, { "age", 45 }, { "sex", "F" } };
        var egfrResult = engine.Calculate(egfrFormula, egfrCtx);

        egfrResult.IsValid.Should().BeTrue();
        egfrResult.CalculatedValue.Should().NotBeNull();
        egfrResult.CalculatedValue!.Value.Should().BeGreaterThan(80.0);

        // 3. MAP Formula
        var mapFormula = new DerivedFormulaDefinition(
            formulaId: "FORMULA-MAP",
            name: "Mean Arterial Pressure",
            outputVariable: "map",
            formulaType: DerivedFormulaType.MAP
        );

        var mapCtx = new Dictionary<string, object?> { { "sbp", 120 }, { "dbp", 80 } };
        var mapResult = engine.Calculate(mapFormula, mapCtx);

        mapResult.IsValid.Should().BeTrue();
        mapResult.CalculatedValue!.Value.Should().BeApproximately(93.3333, 0.1);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TimelineScheduleEngine_ComputeSubjectSchedule_HandlesCycleTemplatesAndDeviations()
    {
        var template = new CycleTemplateDefinition(
            TemplateId: "Q3W",
            Name: "Every 3 Weeks Regimen",
            CycleCount: 2,
            CycleLengthDays: 21,
            EncountersPerCycle: new List<EncounterScheduleItem>
            {
                new("C_DAY1", "Cycle Day 1", TargetDayOffset: 0, Window: new VisitWindowEnvelope(WindowBeforeDays: 2, WindowAfterDays: 2, MajorDeviationToleranceDays: 7))
            }
        );

        var def = new TimelineScheduleDefinition(
            studyId: "STUDY-ONC-01",
            cycleTemplates: new List<CycleTemplateDefinition> { template }
        );

        var engine = new TimelineScheduleEngine(def);
        var encounters = engine.GetAllEncounters();
        encounters.Should().HaveCount(2);

        var anchorDates = new Dictionary<string, object> { { "CYCLE_START", "2026-09-01" } };
        var schedules = engine.ComputeSubjectSchedule(anchorDates, encounters: encounters);

        schedules.Should().HaveCount(2);
        schedules[0].TargetDate.Should().Be("2026-09-01");
        schedules[1].TargetDate.Should().Be("2026-09-22"); // 01 + 21 days

        // Assess visit deviation (actual date = 2026-09-01 => WITHIN_WINDOW)
        var devOnTime = engine.EvaluateVisitDeviation("C_DAY1_C1", "2026-09-01", anchorDates, encounters: encounters);
        devOnTime.Classification.Should().Be(VisitDeviationClassification.WITHIN_WINDOW);
        devOnTime.IsDeviation.Should().BeFalse();

        // Assess late visit deviation (actual date = 2026-09-28 => LATE_VISIT / MAJOR)
        var devLate = engine.EvaluateVisitDeviation("C_DAY1_C1", "2026-09-28", anchorDates, encounters: encounters);
        devLate.IsDeviation.Should().BeTrue();
    }
}

