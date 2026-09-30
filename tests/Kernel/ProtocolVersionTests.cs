using System;
using System.Collections.Generic;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Trust;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// The protocol version lifecycle in the domain (spec §6.2, #4775): <c>Draft → InReview → Approved → Superseded |
/// Withdrawn</c>, frozen content with a content hash from review onwards, and forks that leave the parent approved.
/// The database repeats these rules; <c>KernelProtocolVersionPostgresTests</c> covers that side.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ProtocolVersionTests
{
    private const string Content = """{"study":{"name":"CARDIO-1","arms":["A","B"]}}""";

    private static readonly ProtocolAmendment Amendment = new(
        "Add a cohort",
        "Enrolment is slower than planned",
        AmendmentClassification.Substantial,
        "Adds cohort C and a visit at week 12",
        ["arm:C", "visit:W12"],
        TransitionPolicy.RequiresReconsent);

    public static TheoryData<ProtocolVersionStatus, string, bool> Transitions()
    {
        var allowed = new HashSet<(ProtocolVersionStatus, string)>
        {
            (ProtocolVersionStatus.Draft, "Revise"),
            (ProtocolVersionStatus.Draft, "Submit"),
            (ProtocolVersionStatus.InReview, "ReturnToDraft"),
            (ProtocolVersionStatus.InReview, "Approve"),
            (ProtocolVersionStatus.InReview, "Withdraw"),
            (ProtocolVersionStatus.Approved, "Supersede"),
            (ProtocolVersionStatus.Approved, "Withdraw"),
            (ProtocolVersionStatus.Approved, "Fork"),
        };
        var data = new TheoryData<ProtocolVersionStatus, string, bool>();
        foreach (var status in Enum.GetValues<ProtocolVersionStatus>())
        {
            foreach (var operation in new[] { "Revise", "Submit", "ReturnToDraft", "Approve", "Withdraw", "Supersede", "Fork" })
            {
                data.Add(status, operation, allowed.Contains((status, operation)));
            }
        }

        return data;
    }

    /// <summary>A study's first version starts as draft number 1, with no parent, amendment, hash or attestations.</summary>
    [Fact(DisplayName = "CreateFirst_StartsAsDraftNumberOne")]
    public void CreateFirstStartsAsDraftNumberOne()
    {
        var id = ProtocolVersionId.New();
        var tenant = TenantId.New();
        var study = StudyId.New();

        var version = ProtocolVersion.CreateFirst(id, tenant, study, Content).Value!;

        version.Id.Should().Be(id);
        version.TenantId.Should().Be(tenant);
        version.StudyId.Should().Be(study);
        version.Number.Should().Be(1);
        version.Status.Should().Be(ProtocolVersionStatus.Draft);
        version.Content.Should().Be(Content);
        version.ParentVersionId.Should().BeNull();
        version.Amendment.Should().BeNull();
        version.ContentHash.Should().BeNull();
        version.SignableTarget.Should().BeNull();
        version.ApprovalAttestationId.Should().BeNull();
        version.WithdrawalAttestationId.Should().BeNull();
    }

    /// <summary>Protocol content must be a single JSON object without duplicate keys, so its hash is well defined.</summary>
    [Theory(DisplayName = "Content_MustBeAJsonObject")]
    [InlineData("", "K.Protocol.ContentRequired")]
    [InlineData("   ", "K.Protocol.ContentRequired")]
    [InlineData("[1,2]", "K.Protocol.ContentInvalid")]
    [InlineData("\"text\"", "K.Protocol.ContentInvalid")]
    [InlineData("{\"a\":1", "K.Protocol.ContentInvalid")]
    [InlineData("{\"a\":1,\"a\":2}", "K.Protocol.ContentInvalid")]
    public void ContentMustBeAJsonObject(string content, string code)
    {
        ProtocolVersion.CreateFirst(ProtocolVersionId.New(), TenantId.New(), StudyId.New(), content).Error!.Code.Should().Be(code);

        var draft = NewDraft();
        draft.Revise(content, null)!.Code.Should().Be(code);
        draft.Content.Should().Be(Content);
    }

    /// <summary>
    /// Submitting freezes the content under its canonical hash; returning to draft clears it, and resubmitting the
    /// same content gives the same hash.
    /// </summary>
    [Fact(DisplayName = "SubmitForReview_SetsTheContentHash_ReturnToDraftClearsIt")]
    [GxPRequirement("PRD-CRF-012")]
    public void SubmitForReviewSetsTheContentHashReturnToDraftClearsIt()
    {
        var version = NewDraft();

        version.SubmitForReview().Should().BeNull();

        version.Status.Should().Be(ProtocolVersionStatus.InReview);
        version.ContentHash.Should().Be(ContentHash.Compute(Content));
        version.SignableTarget.Should().Be(new AttestationTarget(AttestationTarget.ProtocolVersionKind, version.Id.Value, version.ContentHash!));
        version.Revise("""{"changed":true}""", null)!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);
        version.Content.Should().Be(Content);

        version.ReturnToDraft().Should().BeNull();
        version.ContentHash.Should().BeNull();
        version.Status.Should().Be(ProtocolVersionStatus.Draft);

        version.SubmitForReview().Should().BeNull();
        version.ContentHash.Should().Be(ContentHash.Compute(Content));
    }

    /// <summary>
    /// Every status against every operation: only the transitions in spec §6.2 succeed, and a refused one returns
    /// <c>K.Protocol.InvalidTransition</c> and changes nothing.
    /// </summary>
    [Theory(DisplayName = "Lifecycle_AllowsOnlyTheSpecifiedTransitions")]
    [MemberData(nameof(Transitions))]
    [GxPRequirement("PRD-CRF-012")]
    public void LifecycleAllowsOnlyTheSpecifiedTransitions(ProtocolVersionStatus status, string operation, bool allowed)
    {
        var version = InStatus(status);
        var hash = version.ContentHash;

        KernelError? error = operation switch
        {
            "Revise" => version.Revise("""{"revised":true}""", null),
            "Submit" => version.SubmitForReview(),
            "ReturnToDraft" => version.ReturnToDraft(),
            "Approve" => version.Approve(AttestationId.New()),
            "Withdraw" => version.Withdraw(AttestationId.New(), "Safety signal"),
            "Supersede" => version.Supersede(),
            "Fork" => ProtocolVersion.Fork(version, ProtocolVersionId.New(), version.Number + 1, Amendment).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        if (allowed)
        {
            error.Should().BeNull();
            return;
        }

        error!.Code.Should().Be(ProtocolErrors.InvalidTransition.Code);
        version.Status.Should().Be(status);
        version.ContentHash.Should().Be(hash);
    }

    /// <summary>Approval and withdrawal record their attestation; withdrawal also needs a reason.</summary>
    [Fact(DisplayName = "ApproveAndWithdraw_RecordTheirAttestations")]
    public void ApproveAndWithdrawRecordTheirAttestations()
    {
        var version = InStatus(ProtocolVersionStatus.InReview);
        var approval = AttestationId.New();
        var withdrawal = AttestationId.New();

        version.Approve(approval).Should().BeNull();
        version.ApprovalAttestationId.Should().Be(approval);
        version.Withdraw(withdrawal, " ").Should().Be(KernelErrors.ReasonForChangeRequired);
        version.Status.Should().Be(ProtocolVersionStatus.Approved);

        version.Withdraw(withdrawal, "Sponsor terminated the study").Should().BeNull();

        version.Status.Should().Be(ProtocolVersionStatus.Withdrawn);
        version.WithdrawalAttestationId.Should().Be(withdrawal);
        version.WithdrawalReason.Should().Be("Sponsor terminated the study");
    }

    /// <summary>
    /// Forking an approved version starts a draft that copies the content and carries the amendment, and the parent
    /// stays approved (#4775: forking must not supersede the parent).
    /// </summary>
    [Fact(DisplayName = "Fork_LeavesTheParentApproved_AndStartsAnAmendedDraft")]
    [GxPRequirement("PRD-CRF-012")]
    public void ForkLeavesTheParentApprovedAndStartsAnAmendedDraft()
    {
        var parent = InStatus(ProtocolVersionStatus.Approved);
        var id = ProtocolVersionId.New();

        var fork = ProtocolVersion.Fork(parent, id, 2, Amendment).Value!;

        parent.Status.Should().Be(ProtocolVersionStatus.Approved);
        fork.Id.Should().Be(id);
        fork.Number.Should().Be(2);
        fork.ParentVersionId.Should().Be(parent.Id);
        fork.TenantId.Should().Be(parent.TenantId);
        fork.StudyId.Should().Be(parent.StudyId);
        fork.Status.Should().Be(ProtocolVersionStatus.Draft);
        fork.Content.Should().Be(parent.Content);
        fork.Amendment.Should().Be(Amendment);
        fork.ContentHash.Should().BeNull();
    }

    /// <summary>An amendment needs its intent, rationale, summary and impacted elements, and only a fork has one.</summary>
    [Fact(DisplayName = "Amendment_IsValidated_AndBelongsToForksOnly")]
    public void AmendmentIsValidatedAndBelongsToForksOnly()
    {
        var parent = InStatus(ProtocolVersionStatus.Approved);

        foreach (var invalid in new[]
        {
            Amendment with { Intent = " " },
            Amendment with { Rationale = "" },
            Amendment with { SummaryOfChanges = "" },
            Amendment with { ImpactedElements = [] },
            Amendment with { ImpactedElements = ["arm:C", " "] },
            Amendment with { Classification = (AmendmentClassification)99 },
            Amendment with { TransitionPolicy = (TransitionPolicy)99 },
        })
        {
            ProtocolVersion.Fork(parent, ProtocolVersionId.New(), 2, invalid).Error.Should().Be(ProtocolErrors.AmendmentInvalid);
        }

        NewDraft().Revise(Content, Amendment).Should().Be(ProtocolErrors.AmendmentNotAllowed);

        var fork = ProtocolVersion.Fork(parent, ProtocolVersionId.New(), 2, Amendment).Value!;
        var revised = Amendment with { SummaryOfChanges = "Adds cohort C only" };
        fork.Revise("""{"study":{"name":"CARDIO-1","arms":["A","B","C"]}}""", revised).Should().BeNull();
        fork.Amendment.Should().Be(revised);
        fork.Revise(Content, null).Should().BeNull();
        fork.Amendment.Should().Be(revised);
    }

    private static ProtocolVersion NewDraft() =>
        ProtocolVersion.CreateFirst(ProtocolVersionId.New(), TenantId.New(), StudyId.New(), Content).Value!;

    private static ProtocolVersion InStatus(ProtocolVersionStatus status)
    {
        var version = NewDraft();
        if (status == ProtocolVersionStatus.Draft)
        {
            return version;
        }

        version.SubmitForReview().Should().BeNull();
        switch (status)
        {
            case ProtocolVersionStatus.Approved:
                version.Approve(AttestationId.New()).Should().BeNull();
                break;
            case ProtocolVersionStatus.Superseded:
                version.Approve(AttestationId.New()).Should().BeNull();
                version.Supersede().Should().BeNull();
                break;
            case ProtocolVersionStatus.Withdrawn:
                version.Withdraw(AttestationId.New(), "Withdrawn in test").Should().BeNull();
                break;
        }

        version.Status.Should().Be(status);
        return version;
    }
}
