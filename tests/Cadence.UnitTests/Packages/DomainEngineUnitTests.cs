using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Cadence.Core.Domain.Randomization;
using Cadence.Core.Domain.Toxicity;
using Cadence.Testing;
using Cadence.Testing.Time;
using Cadence.ToolingCore.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class DomainEngineUnitTests
{
    [Fact]
    public void BoinEngine_CalculateBoinBoundaries_ReturnsExpectedBoundaries()
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(0.25);
        lambdaE.Should().BeInRange(0.19, 0.21);
        lambdaD.Should().BeInRange(0.29, 0.31);
    }

    [Fact]
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
    public void BlockAndMinimizationEngines_PreserveDefaultSeedsStrataAndSha256Checksums()
    {
        var blockRequest = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "AGE", "ADULT" } }
        );
        var blockDefault = BlockRandomizationEngine.Allocate(
            new StratifiedBlockConfig(
                ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
                StratificationFactors: new List<string> { "AGE" },
                BlockSizes: new List<int> { 2, 4 },
                Seed: null),
            blockRequest);
        var blockSeed42 = BlockRandomizationEngine.Allocate(
            new StratifiedBlockConfig(
                ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
                StratificationFactors: new List<string> { "AGE" },
                BlockSizes: new List<int> { 2, 4 },
                Seed: 42),
            blockRequest);

        blockDefault.AssignedArm.Should().Be(blockSeed42.AssignedArm);
        blockDefault.BlockIndex.Should().Be(blockSeed42.BlockIndex);
        string expectedBlockPayload = $"{blockRequest.StudyId}:{blockRequest.SubjectId}:{blockDefault.StratumKey}:{blockDefault.AllocationIndex}:{blockDefault.BlockIndex}:{blockDefault.AssignedArm}";
        blockDefault.DeterministicChecksum.Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedBlockPayload))));

        var minRequest = new SubjectAllocationRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-202",
            SiteId: "SITE-1",
            StratificationFactors: new Dictionary<string, object> { { "STAGE", "III" }, { "CENTER", "NY" } }
        );
        var minDefault = MinimizationEngine.Allocate(
            new MinimizationConfig(
                ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
                StratificationFactors: new List<string> { "STAGE", "CENTER" },
                DistanceMetric: ImbalanceDistanceMetric.RANGE,
                PPreferred: 0.8,
                Seed: null),
            minRequest);
        var minSeed101 = MinimizationEngine.Allocate(
            new MinimizationConfig(
                ArmsRatios: new Dictionary<string, int> { { "ARM_A", 1 }, { "ARM_B", 1 } },
                StratificationFactors: new List<string> { "STAGE", "CENTER" },
                DistanceMetric: ImbalanceDistanceMetric.RANGE,
                PPreferred: 0.8,
                Seed: 101),
            minRequest);

        minDefault.AssignedArm.Should().Be(minSeed101.AssignedArm);
        minDefault.StratumKey.Should().Be("CENTER=NY|STAGE=III");
        string expectedMinPayload = $"{minRequest.StudyId}:{minRequest.SubjectId}:{minDefault.AssignedArm}:{minDefault.AllocationIndex}:{minDefault.ImbalanceScore}";
        minDefault.DeterministicChecksum.Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expectedMinPayload))));
    }

    [Fact]
    public void DuplicationDetectorService_RandomizationFilesPassCleanly_WithoutExemption()
    {
        string repoRoot = FindRepoRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), $"cadence-dup-rand-{Guid.NewGuid():N}");
        try
        {
            string sourceDir = Path.Combine(repoRoot, "packages", "engine_core", "src", "Randomization");
            string destDir = Path.Combine(tempRoot, "packages", "engine_core", "src", "Randomization");
            Directory.CreateDirectory(destDir);

            var copiedFiles = new List<string>();
            foreach (string srcFile in Directory.EnumerateFiles(sourceDir, "*.cs"))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(srcFile));
                File.Copy(srcFile, destFile);
                copiedFiles.Add(destFile);
            }

            DuplicationDetectorService.DetectDuplication(copiedFiles.ToArray(), tempRoot).Should().Be(0);

            // Verify BoinEngine.cs and ResponseAdaptiveEngine.cs are no longer exempted when >=15 lines match.
            string dupLines = string.Join(
                "\n",
                Enumerable.Range(1, 18).Select(i => $"        double checkVal{i} = System.Math.Sin({i} * 0.125);"));
            string dupHelper = $"\ninternal static class DupProbe\n{{\n    internal static void Probe()\n    {{\n{dupLines}\n    }}\n}}\n";

            File.AppendAllText(Path.Combine(destDir, "BoinEngine.cs"), dupHelper);
            File.AppendAllText(Path.Combine(destDir, "ResponseAdaptiveEngine.cs"), dupHelper);

            DuplicationDetectorService.DetectDuplication(copiedFiles.ToArray(), tempRoot).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DuplicationDetectorService_RejectsJavaScriptDuplicates_OverFifteenNormalizedLines()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"cadence-dup-js-{Guid.NewGuid():N}");
        try
        {
            string webUtilsDir = Path.Combine(tempRoot, "apps", "web", "src", "utils");
            string portalUtilsDir = Path.Combine(tempRoot, "apps", "subject-portal", "src", "utils");
            Directory.CreateDirectory(webUtilsDir);
            Directory.CreateDirectory(portalUtilsDir);

            string webFile = Path.Combine(webUtilsDir, "demoProbe.js");
            string portalFile = Path.Combine(portalUtilsDir, "demoProbe.js");

            File.WriteAllText(webFile, "export function webOnly() {\n  return 'web';\n}\n");
            File.WriteAllText(portalFile, "export function portalOnly() {\n  return 'portal';\n}\n");

            DuplicationDetectorService.DetectDuplication([webFile, portalFile], tempRoot).Should().Be(0);

            string dupJsBody = string.Join(
                "\n",
                Enumerable.Range(1, 18).Select(i => $"  const stepValue{i} = (seed + {i}) * 2654435761;"));
            string dupJsFn = $"\nexport function computeDeterministicProbe(seed) {{\n{dupJsBody}\n  return stepValue18;\n}}\n";

            File.AppendAllText(webFile, dupJsFn);
            File.AppendAllText(portalFile, dupJsFn);

            DuplicationDetectorService.DetectDuplication([webFile, portalFile], tempRoot).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string FindRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);

    [Fact]
    public void LabToxicityGradingEngine_EvaluatesGrade3Alt_AndGeneratesAeDraft()
    {
        var engine = new LabToxicityGradingEngine();
        var result = engine.EvaluateObservation(
            testCode: "ALT",
            value: 300.0,
            unit: "U/L",
            evaluatedAt: FakeClock.DefaultStart,
            refHigh: 50.0
        );

        result.Grade.Should().Be(CTCAEGrade.GRADE_3);
        result.IsCriticalAeTrigger.Should().BeTrue();
        result.AeDraft.Should().NotBeNull();
        result.AeDraft!.Severity.Should().Be("SEVERE");
        result.EvalTimestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
        result.AeDraft.DraftTimestamp.Should().Be(result.EvalTimestamp);
    }

    [Fact]
    public void LabToxicityGradingEngine_EvaluateHysLaw_DetectsCriticalHysLawTriad()
    {
        var eval = LabToxicityGradingEngine.EvaluateHysLaw(
            alt: 200, ast: 180, tbil: 4.5, alkp: 100,
            altUln: 50, astUln: 50, tbilUln: 1.2, alkpUln: 120,
            evaluatedAt: FakeClock.DefaultStart
        );

        eval.IsHysLawMet.Should().BeTrue();
        eval.AlertLevel.Should().Be("HYS_LAW_CRITICAL");
        eval.EvalTimestamp.Should().Be("2026-01-01T00:00:00.0000000Z");
    }
}
