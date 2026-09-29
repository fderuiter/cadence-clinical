using System;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Domain.Entities;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

public class ProtocolVersionTests
{
    /// <summary>
    /// Validates ProtocolVersion initialization in Draft state with immutable identity and audit fields.
    /// </summary>
    [Fact(DisplayName = "ProtocolVersion_Create_InitializesInDraftStatus")]
    [Trait("Category", "GxP_OQ")]
    public void ProtocolVersionCreateInitializesInDraftStatus()
    {
        // Arrange
        var versionId = ProtocolVersionId.New();
        var tenantId = TenantId.New();
        var studyId = StudyId.New();
        var now = DateTimeOffset.UtcNow;
        var period = TemporalPeriod.CreateCurrent(now, now);

        // Act
        var protocolVersion = new ProtocolVersion(
            versionId,
            tenantId,
            studyId,
            "v1.0-draft",
            1,
            predecessorVersionId: null,
            "{\"study\": {\"name\": \"Protocol Alpha\"}}",
            period,
            "protocol_author_1",
            "Initial protocol definition");

        // Assert
        protocolVersion.Id.Should().Be(versionId);
        protocolVersion.TenantId.Should().Be(tenantId);
        protocolVersion.StudyId.Should().Be(studyId);
        protocolVersion.VersionTag.Should().Be("v1.0-draft");
        protocolVersion.VersionIndex.Should().Be(1);
        protocolVersion.PredecessorVersionId.Should().BeNull();
        protocolVersion.Status.Should().Be(ProtocolApprovalStatus.Draft);
        protocolVersion.CreatedBy.Should().Be("protocol_author_1");
        protocolVersion.ReasonForChange.Should().Be("Initial protocol definition");
    }

    /// <summary>
    /// Validates draft payload updates and transition to InReview status.
    /// </summary>
    [Fact(DisplayName = "ProtocolVersion_DraftWorkflow_AllowsUpdatesAndSubmission")]
    [Trait("Category", "GxP_OQ")]
    public void ProtocolVersionDraftWorkflowAllowsUpdatesAndSubmission()
    {
        // Arrange
        var protocolVersion = CreateTestProtocolVersion();
        var updatedJson = "{\"study\": {\"name\": \"Protocol Alpha Revised\"}}";

        // Act 1: Update payload while in Draft
        protocolVersion.UpdateDraftPayload(updatedJson, "author_2", "Added secondary endpoints");

        // Assert 1
        protocolVersion.UsdmPayloadJson.Should().Be(updatedJson);
        protocolVersion.CreatedBy.Should().Be("author_2");
        protocolVersion.ReasonForChange.Should().Be("Added secondary endpoints");

        // Act 2: Submit for review
        protocolVersion.SubmitForReview("author_2", "Ready for medical monitor review");

        // Assert 2
        protocolVersion.Status.Should().Be(ProtocolApprovalStatus.InReview);
        protocolVersion.ReasonForChange.Should().Be("Ready for medical monitor review");

        // Assert 3: Submitting again while InReview throws InvalidOperationException
        var actInvalidSubmit = () => protocolVersion.SubmitForReview("author_2", "Duplicate submission");
        actInvalidSubmit.Should().Throw<InvalidOperationException>()
            .WithMessage("*Only Draft protocol versions can be submitted for review*");
    }

    /// <summary>
    /// Validates formal approval and enforces strict 21 CFR Part 11 immutability on approved versions.
    /// </summary>
    [Fact(DisplayName = "ProtocolVersion_Approval_EnforcesPermanentImmutability")]
    [Trait("Category", "GxP_OQ")]
    public void ProtocolVersionApprovalEnforcesPermanentImmutability()
    {
        // Arrange
        var protocolVersion = CreateTestProtocolVersion();
        protocolVersion.SubmitForReview("author_1", "Ready for IRB submission");

        var approvedAt = DateTimeOffset.UtcNow;

        // Act: Formally approve
        protocolVersion.Approve(approvedAt, "medical_monitor_1", "IRB approval granted");

        // Assert status
        protocolVersion.Status.Should().Be(ProtocolApprovalStatus.Approved);
        protocolVersion.CreatedBy.Should().Be("medical_monitor_1");
        protocolVersion.ReasonForChange.Should().Be("IRB approval granted");

        // Assert Immutability: Mutating approved version is rejected under 21 CFR Part 11
        var actMutate = () => protocolVersion.UpdateDraftPayload("{}", "hacker", "Unauthorized edit");
        actMutate.Should().Throw<InvalidOperationException>()
            .WithMessage("*permanently immutable under 21 CFR Part 11*");

        var actReApprove = () => protocolVersion.Approve(approvedAt.AddMinutes(5), "monitor_2", "Re-approval");
        actReApprove.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be approved from status: Approved*");
    }

    /// <summary>
    /// Validates forking an approved protocol into a new amendment draft version with bitemporal supersession.
    /// </summary>
    [Fact(DisplayName = "ProtocolVersion_ForkAmendment_SupersedesPriorAndCreatesNewDraft")]
    [Trait("Category", "GxP_OQ")]
    public void ProtocolVersionForkAmendmentSupersedesPriorAndCreatesNewDraft()
    {
        // Arrange
        var protocolVersion = CreateTestProtocolVersion();
        var approvalTime = DateTimeOffset.UtcNow.AddDays(-10);
        protocolVersion.Approve(approvalTime, "sponsor_qa", "Initial Protocol Approval");

        var amendmentTime = DateTimeOffset.UtcNow;
        var amendmentId = ProtocolVersionId.New();

        // Act: Fork protocol amendment
        var amendment = protocolVersion.ForkAmendment(
            amendmentId,
            "v2.0-amendment-1",
            amendmentTime,
            "pi_lead",
            "Protocol Amendment 1: Modified inclusion criteria");

        // Assert Prior Version
        protocolVersion.Status.Should().Be(ProtocolApprovalStatus.Superseded);
        protocolVersion.Period.RecordedTo.Should().Be(amendmentTime);
        protocolVersion.Period.IsCurrent(amendmentTime.AddMinutes(1)).Should().BeFalse();

        // Assert Amendment Version
        amendment.Id.Should().Be(amendmentId);
        amendment.PredecessorVersionId.Should().Be(protocolVersion.Id);
        amendment.VersionTag.Should().Be("v2.0-amendment-1");
        amendment.VersionIndex.Should().Be(protocolVersion.VersionIndex + 1);
        amendment.Status.Should().Be(ProtocolApprovalStatus.Draft);
        amendment.Period.RecordedFrom.Should().Be(amendmentTime);
        amendment.Period.RecordedTo.Should().BeNull();
        amendment.Period.IsCurrent(amendmentTime).Should().BeTrue();
        amendment.CreatedBy.Should().Be("pi_lead");
        amendment.ReasonForChange.Should().Be("Protocol Amendment 1: Modified inclusion criteria");
    }

    private static ProtocolVersion CreateTestProtocolVersion()
    {
        var now = DateTimeOffset.UtcNow;
        return new ProtocolVersion(
            ProtocolVersionId.New(),
            TenantId.New(),
            StudyId.New(),
            "v1.0",
            1,
            predecessorVersionId: null,
            "{\"study\": {\"id\": \"ST-001\", \"name\": \"Phase III Oncology Trial\"}}",
            TemporalPeriod.CreateCurrent(now, now),
            "data_manager_1",
            "Initial setup");
    }
}
