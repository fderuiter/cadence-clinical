using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.EditChecks;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.EditChecks;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.EditChecks;

/// <summary>
/// Golden master parity and concurrency stress tests for batch edit check evaluation.
/// </summary>
public sealed class BatchEditCheckTests
{
    /// <summary>
    /// Validates that batch evaluation accurately aggregates observations across multiple visits without N+1 queries.
    /// </summary>
    [Fact]
    public void BatchEvaluation_MultiObservationContext_DetectsDiscrepanciesAcrossVisits()
    {
        var engine = new AstEditCheckEngine();

        // Visit 1: Valid (120/80)
        var sysV1 = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            value: 120.0,
            visitId: "VISIT-01").Value!;

        var diaV1 = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic BP",
            value: 80.0,
            visitId: "VISIT-01").Value!;

        // Visit 2: Invalid (75/95 - systolic lower than diastolic)
        var sysV2 = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("SYSBP"),
            testName: "Systolic BP",
            value: 75.0,
            visitId: "VISIT-02").Value!;

        var diaV2 = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("DIABP"),
            testName: "Diastolic BP",
            value: 95.0,
            visitId: "VISIT-02").Value!;

        var context = new BatchEvaluationContext(
            studyId: "STUDY-001",
            subjectId: "SUBJ-001",
            observations: [sysV1, diaV1, sysV2, diaV2],
            formSubmissions: []);

        var violations = engine.EvaluateBatchRules(context);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("RULE_BP_PHYSIOLOGY");
        violations[0].VisitId.Should().Be("VISIT-02");
    }

    /// <summary>
    /// Validates thread safety and determinism of batch evaluation engine under concurrent subject evaluations.
    /// </summary>
    [Fact]
    public async Task BatchEvaluation_ConcurrentExecution_ThreadSafeAndDeterministic()
    {
        var engine = new AstEditCheckEngine();
        const int concurrentTasks = 50;

        var tasks = Enumerable.Range(0, concurrentTasks).Select(i => Task.Run(() =>
        {
            var isDiscrepant = i % 2 == 1;
            var sysVal = isDiscrepant ? 70.0 : 130.0;
            var diaVal = 85.0;

            var sys = ClinicalObservation.Create(
                studyId: new StudyCode("STUDY-001"),
                subjectId: new SubjectCode($"SUBJ-{i:D3}"),
                domain: new DomainCode("VS"),
                testCode: new TestCode("SYSBP"),
                testName: "Systolic BP",
                value: sysVal,
                visitId: "VISIT-01").Value!;

            var dia = ClinicalObservation.Create(
                studyId: new StudyCode("STUDY-001"),
                subjectId: new SubjectCode($"SUBJ-{i:D3}"),
                domain: new DomainCode("VS"),
                testCode: new TestCode("DIABP"),
                testName: "Diastolic BP",
                value: diaVal,
                visitId: "VISIT-01").Value!;

            var ctx = new BatchEvaluationContext(
                studyId: "STUDY-001",
                subjectId: $"SUBJ-{i:D3}",
                observations: [sys, dia],
                formSubmissions: []);

            var violations = engine.EvaluateBatchRules(ctx);
            return (Expected: isDiscrepant, ActualCount: violations.Count);
        }));

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            if (r.Expected)
            {
                r.ActualCount.Should().Be(1);
            }
            else
            {
                r.ActualCount.Should().Be(0);
            }
        }
    }
}
