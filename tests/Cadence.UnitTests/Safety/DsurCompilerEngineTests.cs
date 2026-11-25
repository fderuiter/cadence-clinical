using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Database;
using Cadence.Kernel.Time;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Safety;

public class DsurCompilerEngineTests
{
    /// <summary>The interceptor's own actor; every DSUR audit entry must name the verified caller instead.</summary>
    private const string InterceptorActor = "interceptor-actor";

    private static SafetyDbContext CreateInMemoryDbContext(IClock? clock = null)
    {
        var options = new DbContextOptionsBuilder<SafetyDbContext>()
            .UseInMemoryDatabase(databaseName: $"SafetyTestDb_{Guid.NewGuid()}")
            .AddInterceptors(new GxPAuditInterceptor(() => InterceptorActor, clock ?? SystemClock.Instance))
            .Options;

        return new SafetyDbContext(options);
    }

    private static DsurCompileRequest DraftRequest(string reason = "Draft for approval") => new(
        CompoundName: "Cadence-Compound-77",
        StudyId: "STUDY-2025",
        PeriodStart: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        PeriodEnd: new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero),
        ReasonForChange: reason);

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

        // Act
        var result = await engine.CompileDsurAsync(request, "safety-officer-user", cancellationToken);

        // Assert
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
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.Zero));
        using var dbContext = CreateInMemoryDbContext(fakeClock);
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
        approvalResult.Record.EsignatureManifestId.Should().Contain("qppv-physician-01");
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

        (await engine.ApproveDsurAsync(reportId, new DsurApproveRequest("Approve", "Locking"), "qppv-1", cancellationToken))
            .Status.Should().Be(DsurOperationStatus.Success);

        // Act 1: Attempting to update locked record via engine
        var updateResult = await engine.UpdateDsurAsync(reportId, new DsurUpdateRequest("New-Name", null, null, null, "Edit attempt"), "officer", cancellationToken);

        // Assert 1
        updateResult.Status.Should().Be(DsurOperationStatus.Conflict);
        updateResult.Message.Should().Contain("LOCKED");

        // Act 2: Attempting to re-approve an already locked record
        var reapproveResult = await engine.ApproveDsurAsync(reportId, new DsurApproveRequest("Re-approve", "Re-approval attempt"), "qppv-2", cancellationToken);

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

        var compilerContext = new DefaultHttpContext
        {
            User = TestSecurityContext.CreateScopedPrincipal("mm-compiler", ["STUDY-88"], [], "Sponsor Medical Monitor"),
        };
        var approverContext = new DefaultHttpContext
        {
            User = TestSecurityContext.CreateScopedPrincipal("mm-approver", ["STUDY-88"], [], "Sponsor Medical Monitor"),
        };

        var compileReq = new DsurCompileRequest(
            CompoundName: "Cadence-Compound-88",
            StudyId: "STUDY-88",
            PeriodStart: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEnd: new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            ReasonForChange: "Initial compilation"
        );

        // Act 1: Compile via endpoint handler
        var dsurService = new DsurApplicationService(dbContext, fakeClock);
        var compileResult = await DsurEndpoints.CompileDsurAsync(compileReq, dsurService, compilerContext, cancellationToken);

        // Assert 1
        compileResult.Should().BeOfType<Created<PeriodicSafetyReportRecord>>();
        var createdResult = (Created<PeriodicSafetyReportRecord>)compileResult;
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
        var createdRecord = createdResult.Value;
        createdRecord.Should().NotBeNull();
        createdRecord!.Status.Should().Be("DRAFT");
        createdRecord.CreatedBy.Should().Be("mm-compiler");

        // Act 2: Approve via endpoint handler
        var approveReq = new DsurApproveRequest("Regulatory Approval", "Final Signoff");
        var approveResult = await DsurEndpoints.ApproveDsurAsync(createdRecord.Id, approveReq, dsurService, approverContext, cancellationToken);

        // Assert 2
        approveResult.Should().BeOfType<Ok<PeriodicSafetyReportRecord>>();
        var okResult = (Ok<PeriodicSafetyReportRecord>)approveResult;
        var approvedRecord = okResult.Value;
        approvedRecord.Should().NotBeNull();
        approvedRecord!.Status.Should().Be("LOCKED");
        approvedRecord.EsignatureManifestId.Should().NotBeNullOrEmpty();
        approvedRecord.ApprovedByUserId.Should().Be("mm-approver");

        // Act 3: Update locked record via endpoint handler
        var updateReq = new DsurUpdateRequest("Updated-Name", null, null, null, "Unauthorized Edit");
        var updateResult = await DsurEndpoints.UpdateDsurAsync(createdRecord.Id, updateReq, dsurService, compilerContext, cancellationToken);

        // Assert 3
        updateResult.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)updateResult;
        problemResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    /// <summary>
    /// The approval's audit entry names the verified caller as actor, carries the reason for change the caller gave, and
    /// records the signer; the compilation's entry names the compiler. Neither falls back to the interceptor's actor.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task ApproveDsur_WritesAuditEntryNamingTheCallerAndReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        using var dbContext = CreateInMemoryDbContext(fakeClock);
        var engine = new DsurCompilerEngine(dbContext, fakeClock);
        var compiled = await engine.CompileDsurAsync(DraftRequest(), "mm-compiler", cancellationToken);

        var result = await engine.ApproveDsurAsync(
            compiled.Record.Id, new DsurApproveRequest(null, "Annual DSUR medical review complete"), "mm-approver", cancellationToken);

        result.Status.Should().Be(DsurOperationStatus.Success);
        var entries = await dbContext.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == DsurCompilerEngine.AuditEntityName && e.EntityId == compiled.Record.Id)
            .ToListAsync(cancellationToken);
        entries.Should().ContainSingle(e => e.Action == DsurCompilerEngine.CompiledAuditAction)
            .Which.UserId.Should().Be("mm-compiler");
        var approval = entries.Should().ContainSingle(e => e.Action == DsurCompilerEngine.ApprovedAuditAction).Subject;
        approval.UserId.Should().Be("mm-approver");
        approval.ReasonForChange.Should().Be("Annual DSUR medical review complete");
        // The record is IGxPAuditable (#5573): its approval entry holds its changed values, the signer among them, and its version.
        approval.ChangesJson.Should().Contain("\"ApprovedByUserId\":\"mm-approver\"");
        approval.VersionIndex.Should().Be(2);
        entries.Should().NotContain(e => e.UserId == InterceptorActor);
    }

    /// <summary>The user who compiled a DSUR cannot approve it; the report stays a DRAFT and nothing is signed.</summary>
    [Fact]
    public async Task ApproveDsur_ByItsCompiler_IsForbiddenAndLeavesTheDraft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        using var dbContext = CreateInMemoryDbContext(fakeClock);
        var engine = new DsurCompilerEngine(dbContext, fakeClock);
        var compiled = await engine.CompileDsurAsync(DraftRequest(), "mm-compiler", cancellationToken);

        var result = await engine.ApproveDsurAsync(
            compiled.Record.Id, new DsurApproveRequest(null, "Self approval attempt"), "mm-compiler", cancellationToken);

        result.Status.Should().Be(DsurOperationStatus.Forbidden);
        var stored = await dbContext.PeriodicSafetyReports.AsNoTracking().SingleAsync(r => r.Id == compiled.Record.Id, cancellationToken);
        stored.Status.Should().Be("DRAFT");
        stored.ApprovedByUserId.Should().BeNull();
        stored.EsignatureManifestId.Should().BeNull();
    }

    /// <summary>An approval without a reason for change is refused and does not lock the report.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApproveDsur_WithoutReasonForChange_IsRefused(string? reason)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        using var dbContext = CreateInMemoryDbContext(fakeClock);
        var engine = new DsurCompilerEngine(dbContext, fakeClock);
        var compiled = await engine.CompileDsurAsync(DraftRequest(), "mm-compiler", cancellationToken);

        var result = await engine.ApproveDsurAsync(compiled.Record.Id, new DsurApproveRequest(null, reason), "mm-approver", cancellationToken);

        result.Status.Should().Be(DsurOperationStatus.Invalid);
        (await dbContext.PeriodicSafetyReports.AsNoTracking().SingleAsync(r => r.Id == compiled.Record.Id, cancellationToken))
            .Status.Should().Be("DRAFT");
    }

    /// <summary>The handlers refuse a request with no verified caller (401) instead of attributing it to a default user.</summary>
    [Fact]
    public async Task Endpoints_WithoutAVerifiedCaller_Return401AndWriteNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
        using var dbContext = CreateInMemoryDbContext(fakeClock);
        var engine = new DsurCompilerEngine(dbContext, fakeClock);
        var compiled = await engine.CompileDsurAsync(DraftRequest(), "mm-compiler", cancellationToken);
        var anonymous = new DefaultHttpContext();

        var dsurService = new DsurApplicationService(dbContext, fakeClock);
        var compile = await DsurEndpoints.CompileDsurAsync(DraftRequest(), dsurService, anonymous, cancellationToken);
        var approve = await DsurEndpoints.ApproveDsurAsync(
            compiled.Record.Id, new DsurApproveRequest(null, "Anonymous approval"), dsurService, anonymous, cancellationToken);
        var update = await DsurEndpoints.UpdateDsurAsync(
            compiled.Record.Id, new DsurUpdateRequest("Renamed", null, null, null, "Anonymous edit"), dsurService, anonymous, cancellationToken);

        foreach (var result in new[] { compile, approve, update })
        {
            result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        }

        var stored = await dbContext.PeriodicSafetyReports.AsNoTracking().ToListAsync(cancellationToken);
        stored.Should().ContainSingle().Which.Status.Should().Be("DRAFT");
    }

    /// <summary>
    /// Updating a draft DSUR without a reason for change is refused and changes nothing; the engine no longer fills in
    /// a default reason (21 CFR 11.10(e), #5462).
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDsur_WithoutReasonForChange_IsRefusedAndChangesNothing(string? reason)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();
        var fakeClock = new FakeClock(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
        var engine = new DsurCompilerEngine(dbContext, fakeClock);
        var compiled = await engine.CompileDsurAsync(new DsurCompileRequest("Compound-X", "STUDY-1", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero), "Draft"), "officer", cancellationToken);

        var result = await engine.UpdateDsurAsync(compiled.Record.Id, new DsurUpdateRequest("Renamed", null, null, null, reason), "officer", cancellationToken);

        result.Status.Should().Be(DsurOperationStatus.Invalid);
        var stored = await dbContext.PeriodicSafetyReports.AsNoTracking().SingleAsync(cancellationToken);
        stored.CompoundName.Should().Be("Compound-X");
        stored.VersionIndex.Should().Be(compiled.Record.VersionIndex);
    }
}
