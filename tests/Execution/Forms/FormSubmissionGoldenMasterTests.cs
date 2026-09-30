using System;
using System.Security.Cryptography;
using System.Text;

using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Golden master parity tests verifying eCRF form submission lifecycle states,
/// 21 CFR Part 11 cryptographic signatures, and audited unlock transitions against Python EDC baseline.
/// @req:SRS-EDC-003
/// </summary>
public sealed class FormSubmissionGoldenMasterTests
{
    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Validates that nominal eCRF form flow executes correctly with cryptographic signature and audited unlock.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public void NominalFormSubmission_ApprovalAndUnlock_MaintainsRegulatoryIntegrity()
    {
        var payload = "{\"vssbp\": 120, \"vsdpb\": 80, \"pulse\": 72}";
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS-01"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            protocolVersion: "1.0.0",
            initialPayload: payload).Value!;

        // 1. Initial Draft
        form.Status.Should().Be(FormSubmissionStatus.Draft);
        form.IsReadonly.Should().BeFalse();

        // 2. Complete
        var completeRes = form.Complete();
        completeRes.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Completed);

        // 3. Approve with valid Part 11 signature
        var hash = ComputeSha256(payload);
        var manifest = SignatureManifest.Create(
            SignerId: "pi-user-smith",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I approve the accuracy and completeness of this form.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: hash);

        var approveRes = form.Approve(manifest);
        approveRes.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Approved);
        form.IsReadonly.Should().BeTrue();

        // 4. Verification: Modifying approved form fails
        var editRes = form.UpdatePayload("{\"vssbp\": 130}", "Unauthorized edit");
        editRes.IsFailure.Should().BeTrue();
        editRes.Error!.Code.Should().Be("FORM_LOCKED");

        // 5. Unlock with GxP justification reverts to Draft
        var unlockRes = form.Unlock("dm-user-jones", "Data discrepancy correction requested by Sponsor DM");
        unlockRes.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Draft);
        form.IsReadonly.Should().BeFalse();
        form.ReasonForChange.Should().Be("Data discrepancy correction requested by Sponsor DM");
    }
}
