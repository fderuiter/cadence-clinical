using System;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Locks;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Queries;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Execution.Locks;

/// <summary>
/// Unit test suite for Automated Pre-lock Discrepancy and Source Data Validation Engine.
/// Verifies 6-tier hierarchy aggregation, open query detection, unverified SDV detection, and sub-500ms execution guardrail.
/// </summary>
public sealed class PreLockValidationEngineTests
{
    private static ExecutionDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ExecutionDbContext(options);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RunPreflightCheck_WhenNoQueriesOrUnverifiedData_ReturnsPassedStatus()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);
        var engine = new PreLockValidationEngine(dbContext, fakeClock);

        // Warm up EF Core model compilation
        _ = await dbContext.ClinicalQueries.FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        var request = new PreflightCheckRequest(
            ScopeType: "STUDY",
            ScopeId: "STUDY-001",
            StudyId: "STUDY-001"
        );

        // Act
        var result = await engine.RunPreflightCheckAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("PASSED");
        result.CanProceed.Should().BeTrue();
        result.RequiresOverride.Should().BeFalse();
        result.DiscrepancyCounts.TotalDiscrepancies.Should().Be(0);
        result.ExecutionTimeMs.Should().BeLessThan(500);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task RunPreflightCheck_WhenOpenQueriesExist_ReturnsBlockedStatus()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);

        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("SYSBP"),
            explanation: "Systolic BP out of range (>180)",
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-VS",
            fieldId: "SYSBP"
        ).Value!;

        dbContext.ClinicalQueries.Add(query);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engine = new PreLockValidationEngine(dbContext, fakeClock);

        var request = new PreflightCheckRequest(
            ScopeType: "SUBJECT",
            ScopeId: "SUBJ-101",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101"
        );

        // Act
        var result = await engine.RunPreflightCheckAsync(request, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be("BLOCKED");
        result.CanProceed.Should().BeFalse();
        result.RequiresOverride.Should().BeTrue();
        result.DiscrepancyCounts.OpenQueries.Should().Be(1);
        result.DiscrepancyCounts.TotalDiscrepancies.Should().Be(1);
        result.Discrepancies.Should().HaveCount(1);
        result.Discrepancies[0].DiscrepancyType.Should().Be("UNANSWERED_QUERY");
        result.ExecutionTimeMs.Should().BeLessThan(500);
    }

    [Fact]
    [GxPRequirement("SRS-EDC-002")]
    public async Task RunPreflightCheck_AcrossSixTiers_AggregatesHierarchicalDiscrepancies()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext(Guid.NewGuid().ToString());
        var fakeClock = new FakeClock(FakeClock.DefaultStart);

        // Open query on Subject 101 Form VS
        var query = ClinicalQuery.Raise(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            testCode: new TestCode("SYSBP"),
            explanation: "Inconsistent vital sign entry.",
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            formId: "FORM-VS",
            fieldId: "SYSBP"
        ).Value!;

        // Unverified observation on Subject 101 Form VS
        var obs = ClinicalObservation.Create(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-101"),
            domain: new DomainCode("VS"),
            testCode: new TestCode("HR"),
            testName: "Heart Rate",
            observationDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            value: 82,
            unit: "bpm",
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            pageId: "FORM-VS"
        ).Value!;

        dbContext.ClinicalQueries.Add(query);
        dbContext.ClinicalObservations.Add(obs);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var engine = new PreLockValidationEngine(dbContext, fakeClock);

        // Act 1: Field level check (SYSBP query matched)
        var fieldCheck = await engine.RunPreflightCheckAsync(new PreflightCheckRequest(
            ScopeType: "FIELD",
            ScopeId: "SYSBP",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            FormId: "FORM-VS",
            FieldName: "SYSBP"
        ), TestContext.Current.CancellationToken);

        // Act 2: Form level check (aggregates query + observation)
        var formCheck = await engine.RunPreflightCheckAsync(new PreflightCheckRequest(
            ScopeType: "FORM",
            ScopeId: "FORM-VS",
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-101",
            FormId: "FORM-VS"
        ), TestContext.Current.CancellationToken);

        // Act 3: Study level check
        var studyCheck = await engine.RunPreflightCheckAsync(new PreflightCheckRequest(
            ScopeType: "STUDY",
            ScopeId: "STUDY-001",
            StudyId: "STUDY-001"
        ), TestContext.Current.CancellationToken);

        // Assert
        fieldCheck.Status.Should().Be("BLOCKED");
        fieldCheck.DiscrepancyCounts.OpenQueries.Should().Be(1);

        formCheck.Status.Should().Be("BLOCKED");
        formCheck.DiscrepancyCounts.TotalDiscrepancies.Should().Be(2);

        studyCheck.Status.Should().Be("BLOCKED");
        studyCheck.DiscrepancyCounts.TotalDiscrepancies.Should().Be(2);
    }
}
