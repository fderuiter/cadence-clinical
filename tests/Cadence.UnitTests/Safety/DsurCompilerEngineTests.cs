using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Safety;

public class DsurCompilerEngineTests
{
    private static SafetyDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SafetyDbContext>()
            .UseInMemoryDatabase(databaseName: $"SafetyTestDb_{Guid.NewGuid()}")
            .Options;

        return new SafetyDbContext(options);
    }

    /// <summary>
    /// Validates that DSUR compilation accepts compound and period window, derives patient-years exposure,
    /// cross-tabulates MedDRA terms, and completes within 5 seconds.
    /// @req:PRD-PVG-004
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task CompileDsur_CalculatesPatientYearsExposureAndMedDraCrossTabulation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
        var engine = new DsurCompilerEngine(dbContext, fakeClock);

        var periodStart = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var enrollments = new List<SubjectEnrollmentRecord>
        {
            new("SUBJ-101", "STUDY-2025", "Investigational Drug", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), null),
            new("SUBJ-102", "STUDY-2025", "Investigational Drug", new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero), null),
            new("SUBJ-103", "STUDY-2025", "Placebo", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 7, 1, 0, 0, 0, TimeSpan.Zero))
        };

        var safetyCases = new List<SafetyCaseIcsrInput>
        {
            new("CASE-001", "SUBJ-101", "STUDY-2025", new DateTimeOffset(2025, 4, 15, 0, 0, 0, TimeSpan.Zero), "Nausea", "Nausea", "Gastrointestinal disorders", true, false, false, null, "CAUSAL", "EXPECTED", "Investigational Drug", "DRUG_WITHDRAWN", new DateTimeOffset(2025, 4, 14, 0, 0, 0, TimeSpan.Zero), null),
            new("CASE-002", "SUBJ-102", "STUDY-2025", new DateTimeOffset(2025, 6, 20, 0, 0, 0, TimeSpan.Zero), "Hepatotoxicity", "Hepatic failure", "Hepatobiliary disorders", true, true, true, "LIVER_SAFETY", "CAUSAL", "UNEXPECTED", "Investigational Drug", "DRUG_WITHDRAWN", new DateTimeOffset(2025, 6, 19, 0, 0, 0, TimeSpan.Zero), null),
            new("CASE-003", "SUBJ-103", "STUDY-2025", new DateTimeOffset(2025, 5, 10, 0, 0, 0, TimeSpan.Zero), "Headache", "Headache", "Nervous system disorders", true, false, false, null, "CAUSAL", "EXPECTED", "Placebo", "DOSE_NOT_CHANGED", new DateTimeOffset(2025, 5, 9, 0, 0, 0, TimeSpan.Zero), null)
        };

        var request = new DsurCompileRequest(
            CompoundName: "Cadence-Compound-77",
            StudyId: "STUDY-2025",
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            ReasonForChange: "Annual DSUR Report Compilation",
            SubjectEnrollments: enrollments,
            SafetyCases: safetyCases
        );

        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await engine.CompileDsurAsync(request, "safety-officer-user", cancellationToken);

        stopwatch.Stop();

        // Assert: Success metrics
        stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(5.0, "DSUR compilation must complete within 5 seconds.");

        result.Should().NotBeNull();
        result.Record.Should().NotBeNull();
        result.Record.ReportType.Should().Be("DSUR");
        result.Record.CompoundName.Should().Be("Cadence-Compound-77");
        result.Record.Status.Should().Be("DRAFT");
        result.Record.TotalSaes.Should().Be(3);

        result.Payload.Should().NotBeNull();
        result.Payload.ExposureMetrics.TotalSubjectsEnrolled.Should().Be(3);
        result.Payload.ExposureMetrics.TotalPatientYearsExposure.Should().BeGreaterThan(1.5);
        result.Payload.LineListings.Should().HaveCount(3);
        result.Payload.SummaryTabulations.Should().HaveCount(3);

        // De-identification verify
        result.Payload.LineListings.Select(l => l.SubjectId).Should().AllSatisfy(id => id.Should().StartWith("SUBJ-"));

        // Database record check
        var savedRecord = await dbContext.PeriodicSafetyReports.FirstOrDefaultAsync(r => r.Id == result.Record.Id, cancellationToken);
        savedRecord.Should().NotBeNull();
        savedRecord!.ReportPayloadJson.Should().Contain("ExposureMetrics");
        savedRecord.ReportPayloadJson.Should().Contain("Gastrointestinal disorders");
    }

    /// <summary>
    /// Validates that approving a DSUR applies Part 11 cryptographic signatures via GxPSigner,
    /// sets status to LOCKED, and passes ESignatureVerifier validation.
    /// @req:PRD-PVG-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-008")]
    public async Task ApproveDsur_AppliesPart11SignatureBlock_AndLocksRecord()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.Zero));
        var engine = new DsurCompilerEngine(dbContext, fakeClock);

        var compileRequest = new DsurCompileRequest(
            CompoundName: "Cadence-Compound-77",
            StudyId: "STUDY-2025",
            PeriodStart: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero),
            ReasonForChange: "Draft for approval"
        );

        var compileResult = await engine.CompileDsurAsync(compileRequest, "safety-officer", cancellationToken);
        var reportId = compileResult.Record.Id;

        var approveRequest = new DsurApproveRequest(
            SignerUserId: "qppv-physician-01",
            SigningReason: "Qualified Person Responsible for Pharmacovigilance (QPPV) Approval",
            ReasonForChange: "Final QPPV Approval and Lock"
        );

        // Act
        var approvalResult = await engine.ApproveDsurAsync(reportId, approveRequest, "qppv-physician-01", cancellationToken);

        // Assert
        approvalResult.Status.Should().Be(DsurOperationStatus.Success);
        approvalResult.Record.Should().NotBeNull();
        approvalResult.Record!.Status.Should().Be("LOCKED");
        approvalResult.Record.ApprovedByUserId.Should().Be("qppv-physician-01");
        approvalResult.Record.ApprovedAt.Should().Be(fakeClock.UtcNow);
        approvalResult.Record.EsignatureManifestId.Should().NotBeNullOrEmpty();

        // ESignatureVerifier validation check
        var verifier = new ESignatureVerifier();
        var verification = verifier.VerifySignature(Encoding.UTF8.GetBytes(approvalResult.Record.EsignatureManifestId!));
        verification.IsValid.Should().BeTrue();
        verification.Status.Should().Be("VALID");
    }

    /// <summary>
    /// Validates that attempting to edit or update a LOCKED DSUR record returns a 409 Conflict error.
    /// @req:PRD-PVG-004
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task UpdateDsur_GivenLockedRecord_Returns409Conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var engine = new DsurCompilerEngine(dbContext, fakeClock);

        var compileResult = await engine.CompileDsurAsync(new DsurCompileRequest("Compound-X", "STUDY-1", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero), "Draft"), "officer", cancellationToken);
        var reportId = compileResult.Record.Id;

        await engine.ApproveDsurAsync(reportId, new DsurApproveRequest("qppv-1", "Approve", "Locking"), "qppv-1", cancellationToken);

        // Act 1: Attempting to update locked record via engine
        var updateResult = await engine.UpdateDsurAsync(reportId, new DsurUpdateRequest("New-Name", null, null, null, "Edit attempt"), "officer", cancellationToken);

        // Assert 1
        updateResult.Status.Should().Be(DsurOperationStatus.Conflict);
        updateResult.Message.Should().Contain("LOCKED");

        // Act 2: Attempting to re-approve an already locked record
        var reapproveResult = await engine.ApproveDsurAsync(reportId, new DsurApproveRequest("qppv-2", "Re-approve", "Re-approval attempt"), "qppv-2", cancellationToken);

        // Assert 2
        reapproveResult.Status.Should().Be(DsurOperationStatus.Conflict);
    }

    /// <summary>
    /// Validates endpoint handlers POST /api/v1/safety/dsur/compile and POST /api/v1/safety/dsur/{id}/approve.
    /// @req:PRD-PVG-004
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task Endpoints_CompileAndApprove_ReturnCreatedAndOkWithValidSignature()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 14, 0, 0, TimeSpan.Zero));

        var httpContext = new DefaultHttpContext();

        var compileReq = new DsurCompileRequest(
            CompoundName: "Cadence-Compound-88",
            StudyId: "STUDY-88",
            PeriodStart: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            ReasonForChange: "Initial compilation"
        );

        // Act 1: Compile via endpoint handler
        var compileResult = await DsurEndpoints.CompileDsurAsync(compileReq, dbContext, fakeClock, httpContext, cancellationToken);

        // Assert 1
        compileResult.Should().BeOfType<Created<PeriodicSafetyReportRecord>>();
        var createdResult = (Created<PeriodicSafetyReportRecord>)compileResult;
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var createdRecord = createdResult.Value;
        createdRecord.Should().NotBeNull();
        createdRecord!.Status.Should().Be("DRAFT");

        // Act 2: Approve via endpoint handler
        var approveReq = new DsurApproveRequest("physician-user", "Regulatory Approval", "Final Signoff");
        var approveResult = await DsurEndpoints.ApproveDsurAsync(createdRecord.Id, approveReq, dbContext, fakeClock, httpContext, cancellationToken);

        // Assert 2
        approveResult.Should().BeOfType<Ok<PeriodicSafetyReportRecord>>();
        var okResult = (Ok<PeriodicSafetyReportRecord>)approveResult;
        var approvedRecord = okResult.Value;
        approvedRecord.Should().NotBeNull();
        approvedRecord!.Status.Should().Be("LOCKED");
        approvedRecord.EsignatureManifestId.Should().NotBeNullOrEmpty();

        // Act 3: Update locked record via endpoint handler
        var updateReq = new DsurUpdateRequest("Updated-Name", null, null, null, "Unauthorized Edit");
        var updateResult = await DsurEndpoints.UpdateDsurAsync(createdRecord.Id, updateReq, dbContext, fakeClock, httpContext, cancellationToken);

        // Assert 3
        updateResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)updateResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// Validates that MaskSubjectId uses deterministic SHA-256 hashing to generate subject pseudonyms.
    /// @req:PRD-PVG-004
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public void MaskSubjectId_UsesDeterministicSha256_ProducesConsistentPseudonyms()
    {
        // Arrange
        var rawId = "PATIENT-98765";
        var expectedHashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawId)), 0, 4);
        var expectedMaskedId = $"SUBJ-{expectedHashHex}";

        // Act
        var result1 = DsurCompilerEngine.MaskSubjectId(rawId);
        var result2 = DsurCompilerEngine.MaskSubjectId(rawId);

        // Assert
        result1.Should().Be(expectedMaskedId);
        result2.Should().Be(expectedMaskedId);
        result1.Should().Be(result2);

        // Verify edge cases
        DsurCompilerEngine.MaskSubjectId(null!).Should().Be("SUBJ-DEID-ANON");
        DsurCompilerEngine.MaskSubjectId("").Should().Be("SUBJ-DEID-ANON");
        DsurCompilerEngine.MaskSubjectId("   ").Should().Be("SUBJ-DEID-ANON");
        DsurCompilerEngine.MaskSubjectId("SUBJ-001").Should().Be("SUBJ-001");
        DsurCompilerEngine.MaskSubjectId("PT-002").Should().Be("PT-002");
        DsurCompilerEngine.MaskSubjectId("pt-003").Should().Be("pt-003");
    }

    /// <summary>
    /// Validates that DSUR compilation de-identifies safety case subject IDs deterministically across separate engine instances.
    /// @req:PRD-PVG-004
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task CompileDsur_DeidentifiesSubjectIdsDeterministicallyAcrossEngineInstances()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Arrange
        using var dbContext1 = CreateInMemoryDbContext();
        using var dbContext2 = CreateInMemoryDbContext();
        var engine1 = new DsurCompilerEngine(dbContext1);
        var engine2 = new DsurCompilerEngine(dbContext2);

        var request = new DsurCompileRequest(
            CompoundName: "TestCompound",
            StudyId: "STUDY-1",
            PeriodStart: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            ReasonForChange: "Testing deterministic masking",
            SafetyCases:
            [
                new("CASE-001", "RAW-SUBJECT-ABC", "STUDY-1", new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero), "Rash", "Rash", "Skin disorders", false, false, false, null, "CAUSAL", "UNEXPECTED", "Drug", "NONE", new DateTimeOffset(2025, 5, 1, 0, 0, 0, TimeSpan.Zero), null)
            ]
        );

        // Act
        var result1 = await engine1.CompileDsurAsync(request, "user1", cancellationToken);
        var result2 = await engine2.CompileDsurAsync(request, "user2", cancellationToken);

        // Assert
        var subjectId1 = result1.Payload.LineListings[0].SubjectId;
        var subjectId2 = result2.Payload.LineListings[0].SubjectId;

        var expectedHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("RAW-SUBJECT-ABC")), 0, 4);
        var expectedSubjectId = $"SUBJ-{expectedHex}";

        subjectId1.Should().Be(expectedSubjectId);
        subjectId2.Should().Be(expectedSubjectId);
        subjectId1.Should().Be(subjectId2);
    }
}
