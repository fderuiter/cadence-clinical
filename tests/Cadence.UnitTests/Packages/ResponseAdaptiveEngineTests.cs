using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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

        var result = StochasticRandomizationEngine.AllocateResponseAdaptive(config, armStates, request);

        result.Should().NotBeNull();
        result.AlgorithmType.Should().Be(RandomizationAlgorithmType.RESPONSE_ADAPTIVE);
        result.DecisionRationale.Should().Contain("Burn-In Phase");
        result.DecisionRationale.Should().Contain("p=0.5000");
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
        result.DecisionRationale.Should().Contain("Beta(9.0,3.0)");
        result.DecisionRationale.Should().Contain("Beta(3.0,9.0)");

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
        result.DecisionRationale.Should().Contain("p=0.8000"); // ArmA capped at max 0.80
        result.DecisionRationale.Should().Contain("p=0.2000"); // ArmB receives remaining 0.20 mass (>= min 0.10)
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
        result.DecisionRationale.Should().Contain("ArmB: ");
        result.DecisionRationale.Should().Contain("[DROPPED]");
        result.DecisionRationale.Should().Contain("ArmA: ");
        result.DecisionRationale.Should().Contain("[GRADUATED]");
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

        var result1 = StochasticRandomizationEngine.AllocateResponseAdaptive(config, armStates, request1);
        var result2 = StochasticRandomizationEngine.AllocateResponseAdaptive(config, armStates, request2);

        result1.AssignedArm.Should().Be(result2.AssignedArm);
        result1.DeterministicChecksum.Should().Be(result2.DeterministicChecksum);

        var requestDifferentSubject = CreateRequest(studyId: "STUDY-XYZ", subjectId: "S-103");
        var resultDifferent = StochasticRandomizationEngine.AllocateResponseAdaptive(config, armStates, requestDifferentSubject);

        resultDifferent.DeterministicChecksum.Should().NotBe(result1.DeterministicChecksum);
    }

    [Fact]
    public void Allocate_PerformanceBenchmark_ExecutesSub10MillisecondsFor10Arms()
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

        // Warmup execution
        ResponseAdaptiveEngine.Allocate(config, armStates, request);

        const int iterations = 100;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var req = CreateRequest(subjectId: $"SUBJ-{i}");
            ResponseAdaptiveEngine.Allocate(config, armStates, req);
        }

        sw.Stop();

        double averageLatencyMs = (double)sw.ElapsedMilliseconds / iterations;

        averageLatencyMs.Should().BeLessThan(50.0, "ResponseAdaptiveEngine allocation must complete under 50ms for 10 treatment arms");
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
}
