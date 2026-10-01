using System;

using Cadence.Ddf;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Ddf;

public class UatSimulationEngineTests
{
    [Fact]
    public void GenerateSyntheticPayloads_ProducesExactly500BoundaryPayloads()
    {
        var formDef = TripartiteCompiler.CompileTripartiteForm(
            design: new Cadence.Kernel.Protocol.Design.ProtocolDesign { Title = "UAT Test Study" },
            formId: "FORM-UAT-001",
            protocolId: "STUDY-UAT"
        );

        var payloads = UatSimulationEngine.GenerateSyntheticPayloads(formDef, count: 500);

        payloads.Should().HaveCount(500);
        payloads[0].PayloadId.Should().Be("payload-001");
        payloads[499].PayloadId.Should().Be("payload-500");
    }

    [Fact]
    public void RunSimulation_ProducesSignedSha256EvidenceReport()
    {
        var formDef = TripartiteCompiler.CompileTripartiteForm(
            design: new Cadence.Kernel.Protocol.Design.ProtocolDesign { Title = "UAT Simulation Study" },
            formId: "FORM-SIM-001",
            protocolId: "STUDY-SIM"
        );

        DateTimeOffset timestamp = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var report = UatSimulationEngine.RunSimulation(formDef, executedAt: timestamp);

        report.Should().NotBeNull();
        report.TotalScenariosEvaluated.Should().Be(500);
        report.ScenariosPassed.Should().Be(500);
        report.ScenariosFailed.Should().Be(0);
        report.Sha256EvidenceDigest.Should().NotBeNullOrWhiteSpace();
        report.Sha256EvidenceDigest.Length.Should().Be(64);

        string expectedDigest = UatSimulationEngine.ComputeSha256EvidenceDigest(
            reportId: report.ReportId,
            formId: report.FormId,
            protocolId: report.ProtocolId,
            totalScenarios: report.TotalScenariosEvaluated,
            passed: report.ScenariosPassed,
            failed: report.ScenariosFailed,
            coverage: report.RuleCoveragePercentage,
            executedAt: timestamp
        );

        report.Sha256EvidenceDigest.Should().Be(expectedDigest);
    }
}
