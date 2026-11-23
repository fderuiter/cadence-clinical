using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Cadence.Core.Domain.Randomization;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class ResponseAdaptiveEngineTests
{
    private static SubjectAllocationRequest CreateRequest(
        string studyId = "STUDY-RAR-01",
        string subjectId = "SUBJ-101",
        List<string>? historicalAllocations = null)
    {
        return new SubjectAllocationRequest(
            StudyId: studyId,
            SubjectId: subjectId,
            SiteId: "SITE-01",
            StratificationFactors: new Dictionary<string, object> { { "BIOMARKER", "POSITIVE" } },
            CurrentStratumAllocations: historicalAllocations ?? new List<string>()
        );
    }

    [Fact]
    public void Allocate_BurnInPhase_EnforcesEqualAllocationProbabilities()
    {
        var config = new ResponseAdaptiveConfig(
            BurnInThreshold: 20,
            PriorAlpha: 1.0,
            PriorBeta: 1.0
        );

        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 5, ObservedResponses: 5), // 100% response rate
            new("ArmB", EvaluatedSubjects: 5, ObservedResponses: 0)  // 0% response rate
        };

        var request = CreateRequest();

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        result.Should().NotBeNull();
        result.AlgorithmType.Should().Be(RandomizationAlgorithmType.RESPONSE_ADAPTIVE);
        result.DecisionRationale.Should().Contain("Burn-In Phase");
        result.DecisionRationale.Should().NotContain("p=");
        result.UnblindedAuditRationale.Should().NotBeNull();
        result.UnblindedAuditRationale.Should().Contain("Burn-In Phase");
        result.UnblindedAuditRationale.Should().Contain("p=0.5000");
    }

    [Fact]
    public void Allocate_AdaptivePhase_UpdatesBetaPosteriorAndSuperiorityProbabilities()
    {
        var config = new ResponseAdaptiveConfig(
            BurnInThreshold: 10,
            PriorAlpha: 1.0,
            PriorBeta: 1.0,
            PowerTuning: 0.5
        );

        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 10, ObservedResponses: 8), // 80% response
            new("ArmB", EvaluatedSubjects: 10, ObservedResponses: 2)  // 20% response
        };

        var request = CreateRequest(historicalAllocations: Enumerable.Repeat("ArmA", 20).ToList());

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        result.Should().NotBeNull();
        result.DecisionRationale.Should().Contain("Adaptive Phase");
        result.DecisionRationale.Should().NotContain("Beta(");
        result.UnblindedAuditRationale.Should().NotBeNull();
        result.UnblindedAuditRationale.Should().Contain("Adaptive Phase");
        result.UnblindedAuditRationale.Should().Contain("Beta(9.0,3.0)");
        result.UnblindedAuditRationale.Should().Contain("Beta(3.0,9.0)");

        var posterior = ResponseAdaptiveEngine.CalculateSuperiorityProbabilities(
            config,
            armStates,
            new Dictionary<string, (double Alpha, double Beta)>
            {
                { "ArmA", (9.0, 3.0) },
                { "ArmB", (3.0, 9.0) }
            }
        );

        posterior["ArmA"].Should().BeGreaterThan(0.90);
        posterior["ArmB"].Should().BeLessThan(0.10);
    }

    [Fact]
    public void Allocate_PowerTuningAndProbabilityCaps_EnforcesBoundsAndRenormalization()
    {
        var config = new ResponseAdaptiveConfig(
            BurnInThreshold: 0,
            PowerTuning: 0.5,
            MinAllocationCap: 0.10,
            MaxAllocationCap: 0.80,
            FutilityCutoff: 0.0
        );

        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 20, ObservedResponses: 18), // High responder
            new("ArmB", EvaluatedSubjects: 20, ObservedResponses: 2)   // Low responder
        };

        var request = CreateRequest();

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        result.Should().NotBeNull();
        result.DecisionRationale.Should().NotContain("p=");
        result.UnblindedAuditRationale.Should().NotBeNull();
        result.UnblindedAuditRationale.Should().Contain("p=0.8000"); // ArmA capped at max 0.80
        result.UnblindedAuditRationale.Should().Contain("p=0.2000"); // ArmB receives remaining 0.20 mass (>= min 0.10)
    }

    [Fact]
    public void Allocate_FutilityArmDropping_DropsArmFallingBelowCutoff()
    {
        var config = new ResponseAdaptiveConfig(
            BurnInThreshold: 0,
            FutilityCutoff: 0.05,
            SuperiorityCutoff: 0.95,
            ControlArm: "Control"
        );

        var armStates = new List<ResponseArmState>
        {
            new("Control", EvaluatedSubjects: 15, ObservedResponses: 6, IsControl: true), // ~40% response
            new("ArmA", EvaluatedSubjects: 15, ObservedResponses: 14),                     // ~93% response
            new("ArmB", EvaluatedSubjects: 15, ObservedResponses: 1)                       // ~7% response
        };

        var request = CreateRequest();

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        result.Should().NotBeNull();
        result.DecisionRationale.Should().NotContain("[DROPPED]");
        result.UnblindedAuditRationale.Should().NotBeNull();
        result.UnblindedAuditRationale.Should().Contain("ArmB: ");
        result.UnblindedAuditRationale.Should().Contain("[DROPPED]");
        result.UnblindedAuditRationale.Should().Contain("ArmA: ");
        result.UnblindedAuditRationale.Should().Contain("[GRADUATED]");
    }

    [Fact]
    public void Allocate_DeterministicSeedHashing_Is100PercentReproducible()
    {
        var config = new ResponseAdaptiveConfig(
            Seed: 42,
            BurnInThreshold: 10
        );

        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 10, ObservedResponses: 7),
            new("ArmB", EvaluatedSubjects: 10, ObservedResponses: 5)
        };

        var request1 = CreateRequest(studyId: "STUDY-XYZ", subjectId: "S-102");
        var request2 = CreateRequest(studyId: "STUDY-XYZ", subjectId: "S-102");

        var result1 = ResponseAdaptiveEngine.Allocate(config, armStates, request1);
        var result2 = ResponseAdaptiveEngine.Allocate(config, armStates, request2);

        result1.AssignedArm.Should().Be(result2.AssignedArm);
        result1.DeterministicChecksum.Should().Be(result2.DeterministicChecksum);

        var requestDifferentSubject = CreateRequest(studyId: "STUDY-XYZ", subjectId: "S-103");
        var resultDifferent = ResponseAdaptiveEngine.Allocate(config, armStates, requestDifferentSubject);

        resultDifferent.DeterministicChecksum.Should().NotBe(result1.DeterministicChecksum);
    }

    [Fact]
    public void Allocate_DefaultSeed2026AndChecksumPayload_PreservesExactSha256Digest()
    {
        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 5, ObservedResponses: 3),
            new("ArmB", EvaluatedSubjects: 5, ObservedResponses: 2)
        };
        var request = CreateRequest(studyId: "STUDY-RAR-SEED", subjectId: "SUBJ-2026");

        var implicitSeedResult = ResponseAdaptiveEngine.Allocate(
            new ResponseAdaptiveConfig(Seed: null, BurnInThreshold: 20),
            armStates,
            request);
        var explicitSeedResult = ResponseAdaptiveEngine.Allocate(
            new ResponseAdaptiveConfig(Seed: 2026, BurnInThreshold: 20),
            armStates,
            request);

        implicitSeedResult.AssignedArm.Should().Be(explicitSeedResult.AssignedArm);
        implicitSeedResult.DeterministicChecksum.Should().Be(explicitSeedResult.DeterministicChecksum);

        string expectedPayload = $"STUDY-RAR-SEED:SUBJ-2026:BIOMARKER=POSITIVE:0:{implicitSeedResult.AssignedArm}:0.500000";
        string expectedHex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedPayload)));
        implicitSeedResult.DeterministicChecksum.Should().Be(expectedHex);
    }

    /// <summary>
    /// Ten arms allocate every subject to one of the arms, under the configured caps. The test asserts behaviour, not
    /// how long the machine took (#5415).
    /// </summary>
    [Fact]
    public void Allocate_ForTenArms_AssignsEverySubjectToAKnownArm()
    {
        var config = new ResponseAdaptiveConfig(
            BurnInThreshold: 0,
            PowerTuning: 0.5,
            MinAllocationCap: 0.05,
            MaxAllocationCap: 0.50
        );

        var armStates = Enumerable.Range(1, 10).Select(i => new ResponseArmState(
            ArmId: $"Arm-{i}",
            EvaluatedSubjects: 20,
            ObservedResponses: (i * 2) % 18 + 1
        )).ToList();

        var request = CreateRequest();

        var armIds = armStates.Select(a => a.ArmId).ToHashSet();
        var first = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        ResponseAdaptiveEngine.Allocate(config, armStates, request).AssignedArm.Should().Be(first.AssignedArm);
        for (int i = 0; i < 100; i++)
        {
            var result = ResponseAdaptiveEngine.Allocate(config, armStates, CreateRequest(subjectId: $"SUBJ-{i}"));
            armIds.Should().Contain(result.AssignedArm);
        }
    }

    [Fact]
    public void CalculateSuperiorityProbabilities_WithExplicitControlArm_CalculatesControlAndTreatmentMetrics()
    {
        var config = new ResponseAdaptiveConfig(ControlArm: "Control");

        var armStates = new List<ResponseArmState>
        {
            new("Control", EvaluatedSubjects: 10, ObservedResponses: 5, IsControl: true),
            new("ArmA", EvaluatedSubjects: 10, ObservedResponses: 8)
        };

        var posteriorParams = new Dictionary<string, (double Alpha, double Beta)>
        {
            { "Control", (6.0, 6.0) },
            { "ArmA", (9.0, 3.0) }
        };

        var superiority = ResponseAdaptiveEngine.CalculateSuperiorityProbabilities(config, armStates, posteriorParams);

        superiority.Should().ContainKey("Control");
        superiority.Should().ContainKey("ArmA");
        superiority["ArmA"].Should().BeGreaterThan(0.80);
    }

    [Fact]
    public void Allocate_TwoTierRationale_SeparatesPublicAndUnblindedAuditRationale()
    {
        var config = new ResponseAdaptiveConfig(BurnInThreshold: 10);
        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 12, ObservedResponses: 9),
            new("ArmB", EvaluatedSubjects: 12, ObservedResponses: 3)
        };
        var request = CreateRequest();

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        // DecisionRationale must be a clean public operational summary with zero numerical interim metrics
        result.DecisionRationale.Should().Contain("Assigned to arm");
        result.DecisionRationale.Should().Contain("Adaptive Phase");
        result.DecisionRationale.Should().NotContain("n=");
        result.DecisionRationale.Should().NotContain("y=");
        result.DecisionRationale.Should().NotContain("Beta(");
        result.DecisionRationale.Should().NotContain("V=");
        result.DecisionRationale.Should().NotContain("p=");

        // UnblindedAuditRationale must contain full posterior calculations
        result.UnblindedAuditRationale.Should().NotBeNull();
        result.UnblindedAuditRationale.Should().Contain("Beta(10.0,4.0)");
        result.UnblindedAuditRationale.Should().Contain("Beta(4.0,10.0)");
        result.UnblindedAuditRationale.Should().Contain("n=12");
        result.UnblindedAuditRationale.Should().Contain("y=9");
        result.UnblindedAuditRationale.Should().Contain("V=");
        result.UnblindedAuditRationale.Should().Contain("p=");
    }

    [Fact]
    public void FastBetaCdf_LargeSample_N1030_DoesNotOverflowOrReturnNaN()
    {
        // n = a + b - 1 = 550 + 550 - 1 = 1099 >= 1030
        double cdf = ResponseAdaptiveEngine.FastBetaCdf(550, 550, 0.50);

        double.IsNaN(cdf).Should().BeFalse();
        double.IsInfinity(cdf).Should().BeFalse();
        cdf.Should().BeApproximately(0.50, 1e-4);
    }

    [Fact]
    public void Allocate_LargeSample_N1030_ProducesValidProbabilitiesAndNoNaN()
    {
        var config = new ResponseAdaptiveConfig(BurnInThreshold: 0, PowerTuning: 0.5);
        var armStates = new List<ResponseArmState>
        {
            new("ArmA", EvaluatedSubjects: 1100, ObservedResponses: 700),
            new("ArmB", EvaluatedSubjects: 1100, ObservedResponses: 500)
        };
        var request = CreateRequest();

        var result = ResponseAdaptiveEngine.Allocate(config, armStates, request);

        result.Should().NotBeNull();
        result.ImbalanceScore.HasValue.Should().BeTrue();
        double.IsNaN(result.ImbalanceScore!.Value).Should().BeFalse();
        result.ImbalanceScore.Value.Should().BeGreaterThan(0.0);
        result.UnblindedAuditRationale.Should().NotContain("NaN");
        result.UnblindedAuditRationale.Should().NotContain("Infinity");
    }

    [Fact]
    public void FastBetaCdf_AndBetaPdf_PreserveUnroundedPrecisionAndBoundaries()
    {
        // Exact I_0.33(1, 4) = 1 - 0.67^4 = 0.79848879 (unrounded, unlike BoinEngine.BetaInc's 0.798489).
        ResponseAdaptiveEngine.FastBetaCdf(1.0, 4.0, 0.33).Should().BeApproximately(0.79848879, 1e-12);
        ResponseAdaptiveEngine.FastBetaCdf(2.0, 3.0, 0.0).Should().Be(0.0);
        ResponseAdaptiveEngine.FastBetaCdf(2.0, 3.0, 1.0).Should().Be(1.0);
        ResponseAdaptiveEngine.FastBetaCdf(2.5, 1.5, 0.4).Should().Be(BoinEngine.BetaInc(2.5, 1.5, 0.4));

        // Beta(2, 3) PDF at x = 0.5 is 12 * 0.5 * 0.25 = 1.5, and 0 outside (0, 1).
        ResponseAdaptiveEngine.BetaPdf(2.0, 3.0, 0.0).Should().Be(0.0);
        ResponseAdaptiveEngine.BetaPdf(2.0, 3.0, 1.0).Should().Be(0.0);
        ResponseAdaptiveEngine.BetaPdf(2.0, 3.0, 0.5).Should().BeApproximately(1.5, 1e-12);
    }
}
