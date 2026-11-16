using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public class EtmfDocumentLifecycleTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));

    private static DefaultHttpContext CreateHttpContext(string userId = "cra.alice", string roles = "CRA,QualityReviewer", string studyId = "STUDY-001")
    {
        var context = new DefaultHttpContext();
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = userId,
            Roles = roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            AssignedStudies = [studyId],
        };
        return context;
    }

    [Fact(DisplayName = "Metric1_ZeroUnrestrictedPublicStatusSettersExistOnTmfDocument")]
    public void Metric1_ZeroUnrestrictedPublicStatusSettersExistOnTmfDocument()
    {
        var statusProperties = typeof(TmfDocument)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name == nameof(TmfDocument.Status) || p.Name == nameof(TmfDocument.ApprovalStatus))
            .ToList();

        statusProperties.Should().NotBeEmpty();
        foreach (var prop in statusProperties)
        {
            prop.SetMethod.Should().NotBeNull($"Property {prop.Name} must have a setter");
            prop.SetMethod!.IsPublic.Should().BeFalse($"Property {prop.Name} must have a private or non-public setter");
        }
    }

    [Theory(DisplayName = "Requirement1_2_ValidLifecycleStateTransitions")]
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.TechnicalQc, true)]
    [InlineData(TmfDocumentLifecycleState.TechnicalQc, TmfDocumentLifecycleState.Approved, true)]
    [InlineData(TmfDocumentLifecycleState.TechnicalQc, TmfDocumentLifecycleState.Rejected, true)]
    [InlineData(TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.Filed, true)]
    [InlineData(TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.Archived, true)]
    [InlineData(TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.Archived, true)]
    // Invalid transitions (matrix validation)
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.Approved, false)]
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.Rejected, false)]
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.Filed, false)]
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.Archived, false)]
    [InlineData(TmfDocumentLifecycleState.Draft, TmfDocumentLifecycleState.Draft, false)]
    [InlineData(TmfDocumentLifecycleState.TechnicalQc, TmfDocumentLifecycleState.Draft, false)]
    [InlineData(TmfDocumentLifecycleState.TechnicalQc, TmfDocumentLifecycleState.Filed, false)]
    [InlineData(TmfDocumentLifecycleState.TechnicalQc, TmfDocumentLifecycleState.Archived, false)]
    [InlineData(TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.Draft, false)]
    [InlineData(TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.TechnicalQc, false)]
    [InlineData(TmfDocumentLifecycleState.Approved, TmfDocumentLifecycleState.Rejected, false)]
    [InlineData(TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.Draft, false)]
    [InlineData(TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.TechnicalQc, false)]
    [InlineData(TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.Approved, false)]
    [InlineData(TmfDocumentLifecycleState.Filed, TmfDocumentLifecycleState.Rejected, false)]
    [InlineData(TmfDocumentLifecycleState.Rejected, TmfDocumentLifecycleState.Approved, false)]
    [InlineData(TmfDocumentLifecycleState.Rejected, TmfDocumentLifecycleState.Draft, false)]
    [InlineData(TmfDocumentLifecycleState.Archived, TmfDocumentLifecycleState.Filed, false)]
    [InlineData(TmfDocumentLifecycleState.Archived, TmfDocumentLifecycleState.Draft, false)]
    public void Requirement1_2_EnforceDeterministicStateTransitionMatrix(string from, string to, bool expectedValid)
    {
        var isValid = TmfDocument.IsValidTransition(from, to);
        isValid.Should().Be(expectedValid);
    }

    [Fact(DisplayName = "Requirement2_DirectTransitionFromDraftToApproved_IsRejected")]
    public void Requirement2_DirectTransitionFromDraftToApproved_IsRejected()
    {
        var doc = TmfDocument.CreateDraft("DOC-DRAFT-1", "STUDY-001", "protocol.pdf", "author1", _clock.UtcNow.UtcDateTime, "Initial draft creation");

        doc.Status.Should().Be(TmfDocumentLifecycleState.Draft);
        doc.ApprovalStatus.Should().Be(TmfDocumentLifecycleState.Draft);

        var act = () => doc.TransitionTo(TmfDocumentLifecycleState.Approved, "author1", "Author", "Attempting direct approval without QC", _clock.UtcNow);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*Allowed transitions from 'DRAFT': TECHNICAL_QC*");
    }

    [Fact(DisplayName = "Requirement3_DocumentTransitionSequenceNumbers_IncrementMonotonically")]
    public void Requirement3_DocumentTransitionSequenceNumbers_IncrementMonotonically()
    {
        var doc = TmfDocument.CreateDraft("DOC-LIFECYCLE-1", "STUDY-001", "protocol.pdf", "uploader", _clock.UtcNow.UtcDateTime, "Draft protocol document");

        var tr1 = doc.StageForQc("cra.alice", "CRA", "Staging protocol for technical quality review", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.TechnicalQc);
        tr1.TransitionSequence.Should().Be(1);
        tr1.FromStatus.Should().Be(TmfDocumentLifecycleState.Draft);
        tr1.ToStatus.Should().Be(TmfDocumentLifecycleState.TechnicalQc);

        var tr2 = doc.Approve("cra.bob", "SeniorCRA", "Protocol technical quality control approved", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Approved);
        tr2.TransitionSequence.Should().Be(2);
        tr2.FromStatus.Should().Be(TmfDocumentLifecycleState.TechnicalQc);
        tr2.ToStatus.Should().Be(TmfDocumentLifecycleState.Approved);

        var tr3 = doc.File("tmf.lead", "TMFLead", "Filing approved protocol in study eTMF repository", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Filed);
        tr3.TransitionSequence.Should().Be(3);
        tr3.FromStatus.Should().Be(TmfDocumentLifecycleState.Approved);
        tr3.ToStatus.Should().Be(TmfDocumentLifecycleState.Filed);

        var tr4 = doc.Archive("archivist", "RecordsManager", "Study closed; archiving filed trial document", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Archived);
        tr4.TransitionSequence.Should().Be(4);
        tr4.FromStatus.Should().Be(TmfDocumentLifecycleState.Filed);
        tr4.ToStatus.Should().Be(TmfDocumentLifecycleState.Archived);

        doc.Transitions.Should().HaveCount(4);
        doc.Transitions.Select(t => t.TransitionSequence).Should().ContainInOrder(1, 2, 3, 4);
    }

    [Fact(DisplayName = "Requirement4_5_CallerAttributionAndJustificationReasonEnforced")]
    public void Requirement4_5_CallerAttributionAndJustificationReasonEnforced()
    {
        var doc = TmfDocument.CreateDraft("DOC-VALIDATION-1", "STUDY-001", "protocol.pdf", "uploader", _clock.UtcNow.UtcDateTime, "Initial draft upload");

        // Missing actor ID
        var actNoActor = () => doc.StageForQc("", "CRA", "Valid reason with more than 10 characters", _clock.UtcNow);
        actNoActor.Should().Throw<ArgumentException>().WithParameterName("actorId");

        // Missing actor Role
        var actNoRole = () => doc.StageForQc("cra.alice", "", "Valid reason with more than 10 characters", _clock.UtcNow);
        actNoRole.Should().Throw<ArgumentException>().WithParameterName("actorRole");

        // Reason shorter than 10 chars
        var actShortReason = () => doc.StageForQc("cra.alice", "CRA", "Too short", _clock.UtcNow);
        actShortReason.Should().Throw<ArgumentException>().WithMessage("*21 CFR Part 11 requires a reason_for_change of at least 10 characters.*");
    }

    [Fact(DisplayName = "Requirement6_RejectedAndArchivedDocuments_RequireExplicitReactivation")]
    public void Requirement6_RejectedAndArchivedDocuments_RequireExplicitReactivation()
    {
        // Document rejected during QC
        var doc = TmfDocument.CreateForTechnicalQc(
            id: "DOC-REJECTED-1",
            studyId: "STUDY-001",
            filename: "consent.pdf",
            createdBy: "uploader",
            actorRole: "CRC",
            timestamp: _clock.UtcNow,
            reasonForChange: "Uploading informed consent form for review"
        );

        doc.Reject("cra.reviewer", "CRA", "Signature missing on page 3 of consent form", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Rejected);

        // Standard transition attempts must fail
        var actNormalTransition = () => doc.Approve("cra.reviewer", "CRA", "Trying to approve directly without reactivation", _clock.UtcNow);
        actNormalTransition.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*Archived or rejected documents cannot transition without an explicit reactivation workflow.*");

        // Explicit reactivation workflow succeeds
        var trReactivate = doc.Reactivate(TmfDocumentLifecycleState.Draft, "qa.manager", "QAManager", "Reactivating rejected document for investigator re-execution", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Draft);
        trReactivate.TransitionSequence.Should().Be(3);
        trReactivate.FromStatus.Should().Be(TmfDocumentLifecycleState.Rejected);
        trReactivate.ToStatus.Should().Be(TmfDocumentLifecycleState.Draft);
    }

    [Fact(DisplayName = "Requirement6_ArchivedDocument_ReactivationWorkflow")]
    public void Requirement6_ArchivedDocument_ReactivationWorkflow()
    {
        var doc = TmfDocument.CreateFiled(
            id: "DOC-ARCHIVED-1",
            studyId: "STUDY-001",
            filename: "audit_certificate.pdf",
            createdBy: "system",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Initial filing of audit certificate"
        );

        doc.Archive("archivist", "Archivist", "Routine trial archive procedure after 5 years", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Archived);

        // Standard transition attempt fails
        var actNormalTransition = () => doc.TransitionTo(TmfDocumentLifecycleState.Filed, "archivist", "Archivist", "Attempting direct restore without reactivation", _clock.UtcNow);
        actNormalTransition.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*Archived or rejected documents cannot transition without an explicit reactivation workflow.*");

        // Reactivation workflow to FILED succeeds
        var tr = doc.Reactivate(TmfDocumentLifecycleState.Filed, "qa.officer", "QAOfficer", "Regulatory audit inspection reactivation request", _clock.UtcNow);
        doc.Status.Should().Be(TmfDocumentLifecycleState.Filed);
        tr.FromStatus.Should().Be(TmfDocumentLifecycleState.Archived);
        tr.ToStatus.Should().Be(TmfDocumentLifecycleState.Filed);
    }

    [Fact(DisplayName = "AcceptanceCriteria_ReviewQcEndpoint_InvalidPreviousStateReturns400")]
    public async Task AcceptanceCriteria_ReviewQcEndpoint_InvalidPreviousStateReturns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        // Document in DRAFT status
        var draftDoc = TmfDocument.CreateDraft("DOC-DRAFT-100", "STUDY-001", "protocol.pdf", "uploader", _clock.UtcNow.UtcDateTime, "Draft protocol");
        dbContext.TmfDocuments.Add(draftDoc);
        await dbContext.SaveChangesAsync(ct);

        var httpContext = CreateHttpContext("cra.alice", "CRA", "STUDY-001");
        var request = new CraqcReviewRequest("ACCEPT", "Valid reason for approving protocol document");

        // Act
        var result = await EtmfIntelligenceEndpoints.ReviewQcDocument("DOC-DRAFT-100", request, dbContext, _clock, httpContext, ct);

        // Assert
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var badRequestResult = result as IValueHttpResult;
        badRequestResult?.Value?.ToString().Should().Contain("Only documents in 'TECHNICAL_QC' status can be reviewed");
    }

    [Fact(DisplayName = "AcceptanceCriteria_ReviewQcEndpoint_ValidStateTransitionSucceedsWithAudit")]
    public async Task AcceptanceCriteria_ReviewQcEndpoint_ValidStateTransitionSucceedsWithAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        // Staged document in TECHNICAL_QC
        var stagedDoc = TmfDocument.CreateForTechnicalQc(
            id: "DOC-QC-200",
            studyId: "STUDY-001",
            filename: "investigator_cv.pdf",
            createdBy: "uploader",
            actorRole: "CRC",
            timestamp: _clock.UtcNow,
            reasonForChange: "Staging investigator CV for technical review"
        );
        dbContext.TmfDocuments.Add(stagedDoc);
        foreach (var tr in stagedDoc.Transitions)
        {
            dbContext.TmfDocumentQcTransitions.Add(tr);
        }
        await dbContext.SaveChangesAsync(ct);

        var httpContext = CreateHttpContext("cra.reviewer", "CRA", "STUDY-001");
        var request = new CraqcReviewRequest("ACCEPT", "Approved after technical and credential verification");

        // Act
        var result = await EtmfIntelligenceEndpoints.ReviewQcDocument("DOC-QC-200", request, dbContext, _clock, httpContext, ct);

        // Assert
        result.Should().BeOfType<Ok<TmfDocument>>();
        var okResult = (Ok<TmfDocument>)result;
        okResult.Value!.Status.Should().Be(TmfDocumentLifecycleState.Approved);
        okResult.Value.ApprovalStatus.Should().Be(TmfDocumentLifecycleState.Approved);

        // Transitions in DB must have sequential sequence numbers
        var transitions = await dbContext.TmfDocumentQcTransitions
            .Where(t => t.DocumentId == "DOC-QC-200")
            .OrderBy(t => t.TransitionSequence)
            .ToListAsync(ct);

        transitions.Should().HaveCount(2);
        transitions[0].TransitionSequence.Should().Be(1);
        transitions[0].FromStatus.Should().Be(TmfDocumentLifecycleState.Draft);
        transitions[0].ToStatus.Should().Be(TmfDocumentLifecycleState.TechnicalQc);

        transitions[1].TransitionSequence.Should().Be(2);
        transitions[1].FromStatus.Should().Be(TmfDocumentLifecycleState.TechnicalQc);
        transitions[1].ToStatus.Should().Be(TmfDocumentLifecycleState.Approved);
        transitions[1].ActorId.Should().Be("cra.reviewer");

        // Audit log in DB
        var audit = await dbContext.TmfAuditLogs.FirstOrDefaultAsync(a => a.DocumentId == "DOC-QC-200" && a.Action == "QC_REVIEW_ACCEPT", ct);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("Approved after technical and credential verification");
    }

    [Fact(DisplayName = "AcceptanceCriteria_ReviewQcEndpoint_OverrideUpdatesTaxonomyAndTransitions")]
    public async Task AcceptanceCriteria_ReviewQcEndpoint_OverrideUpdatesTaxonomyAndTransitions()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var stagedDoc = TmfDocument.CreateForTechnicalQc(
            id: "DOC-QC-300",
            studyId: "STUDY-001",
            filename: "protocol_v1_draft.pdf",
            createdBy: "uploader",
            actorRole: "CRC",
            timestamp: _clock.UtcNow,
            reasonForChange: "Staging trial document for review"
        );
        dbContext.TmfDocuments.Add(stagedDoc);
        await dbContext.SaveChangesAsync(ct);

        var httpContext = CreateHttpContext("cra.lead", "CRA,QualityManager", "STUDY-001");
        // Override taxonomy to FDA 1572
        var request = new CraqcReviewRequest("OVERRIDE", "Reclassifying document as FDA Form 1572", OverrideArtifactCode: "05.02.01");

        // Act
        var result = await EtmfIntelligenceEndpoints.ReviewQcDocument("DOC-QC-300", request, dbContext, _clock, httpContext, ct);

        // Assert
        result.Should().BeOfType<Ok<TmfDocument>>();
        var doc = ((Ok<TmfDocument>)result).Value!;
        doc.Status.Should().Be(TmfDocumentLifecycleState.Approved);
        doc.ArtifactCode.Should().Be("05.02.01");
        doc.Zone.Should().Be(5);
        doc.Section.Should().Be("05.02");
        doc.ArtifactType.Should().Be("FDA Form 1572");
    }

    [Fact(DisplayName = "AcceptanceCriteria_ReviewQcEndpoint_RejectTransitionsToRejected")]
    public async Task AcceptanceCriteria_ReviewQcEndpoint_RejectTransitionsToRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var stagedDoc = TmfDocument.CreateForTechnicalQc(
            id: "DOC-QC-400",
            studyId: "STUDY-001",
            filename: "lab_certificate.pdf",
            createdBy: "uploader",
            actorRole: "CRC",
            timestamp: _clock.UtcNow,
            reasonForChange: "Staging lab certificate for review"
        );
        dbContext.TmfDocuments.Add(stagedDoc);
        await dbContext.SaveChangesAsync(ct);

        var httpContext = CreateHttpContext("cra.reviewer", "CRA", "STUDY-001");
        var request = new CraqcReviewRequest("REJECT", "Laboratory accreditation certificate has expired");

        // Act
        var result = await EtmfIntelligenceEndpoints.ReviewQcDocument("DOC-QC-400", request, dbContext, _clock, httpContext, ct);

        // Assert
        result.Should().BeOfType<Ok<TmfDocument>>();
        var doc = ((Ok<TmfDocument>)result).Value!;
        doc.Status.Should().Be(TmfDocumentLifecycleState.Rejected);
        doc.ApprovalStatus.Should().Be(TmfDocumentLifecycleState.Rejected);
    }
}
