using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Domain.Forms;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Kernel.Trust;
using Cadence.Security;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

using Xunit;

namespace Cadence.UnitTests.Execution.Forms;

/// <summary>
/// Unit test coverage for eCRF form submission lifecycle states, 21 CFR Part 11 electronic signature approval,
/// SHA-256 payload integrity validation, immutable read-only locking, and audited unlock workflows.
/// @req:SRS-EDC-003
/// </summary>
public sealed class FormSubmissionStateTests
{
    private static readonly AuthenticatedCaller PrincipalInvestigator = new("pi-user-01", ["PRINCIPAL_INVESTIGATOR"]);

    /// <summary>The payload hash a signer computes: SHA-256 of the RFC 8785 canonical payload (ADR-2513).</summary>
    private static string ComputeHash(string payload) => JsonCanonicalization.Sha256Hex(payload);

    /// <summary>
    /// Validates that initializing a form submission places it in Draft state with editable properties.
    /// </summary>
    [Fact]
    public void InitializeForm_CreatesFormInDraftState()
    {
        var result = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            siteId: new SiteCode("SITE-01"),
            visitId: "VISIT-01",
            protocolVersion: "1.0.0",
            initialPayload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}");

        result.IsSuccess.Should().BeTrue();
        var form = result.Value!;
        form.Status.Should().Be(FormSubmissionStatus.Draft);
        form.IsReadonly.Should().BeFalse();
        form.VersionIndex.Should().Be(1);
    }

    /// <summary>
    /// Validates that form payload can be updated while in Draft status.
    /// </summary>
    [Fact]
    public void UpdatePayload_InDraftState_UpdatesPayloadSuccessfully()
    {
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}").Value!;

        var updateResult = form.UpdatePayload("{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 125}", "Corrected entry");
        updateResult.IsSuccess.Should().BeTrue();
        form.Payload.Should().Be("{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 125}");
        form.ReasonForChange.Should().Be("Corrected entry");
    }

    /// <summary>
    /// Validates that completing and approving a form locks it as read-only and captures the signature manifest.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public void CompleteAndApprove_LocksFormInReadOnlyState()
    {
        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120, \"vsdpb\": 80}";
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;

        form.Complete().IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Completed);

        var manifest = SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: ComputeHash(payload));

        var approveResult = form.Approve(manifest);
        approveResult.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Approved);
        form.IsReadonly.Should().BeTrue();
        form.SignatureManifest.Should().NotBeNull();
        form.SignatureManifest!.SignerId.Should().Be("pi-user-01");
    }

    /// <summary>
    /// Validates that modifying an approved form is strictly rejected under 21 CFR Part 11 integrity rules.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public void UpdatePayload_WhenFormIsApproved_ReturnsForbiddenLockedError()
    {
        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;

        form.Complete();
        form.Approve(SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: ComputeHash(payload)));

        var updateResult = form.UpdatePayload("{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 130}", "Post-lock edit attempt");
        updateResult.IsFailure.Should().BeTrue();
        updateResult.Error!.Code.Should().Be("FORM_LOCKED");
    }

    /// <summary>
    /// Validates that unlocking an approved form requires an explicit reason and reverts state to Draft.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public void UnlockForm_WithReason_RevertsToDraftAndUnlocks()
    {
        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;

        form.Complete();
        form.Approve(SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: ComputeHash(payload)));

        var unlockResult = form.Unlock("dm-user-01", "Lab result correction required by DM query");
        unlockResult.IsSuccess.Should().BeTrue();
        form.Status.Should().Be(FormSubmissionStatus.Draft);
        form.IsReadonly.Should().BeFalse();
        form.ReasonForChange.Should().Be("Lab result correction required by DM query");

        // Can now be updated again
        var reEditResult = form.UpdatePayload("{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 122}", "Updated after unlock");
        reEditResult.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Validates that the application service strictly enforces SHA-256 payload verification before signing.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task FormApplicationService_Approve_ValidatesSha256PayloadHash()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi_investigator_01", "PI approval");

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120, \"vsdpb\": 80}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        var formId = createResult.Value!.Id;

        await service.CompleteAsync(formId, CancellationToken.None);

        // Attempt approve with mismatched hash
        var badApproveResult = await service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: "0000000000000000000000000000000000000000000000000000000000000000"),
            PrincipalInvestigator,
            CancellationToken.None);

        badApproveResult.IsFailure.Should().BeTrue();
        badApproveResult.Error!.Code.Should().Be("INVALID_PAYLOAD_HASH");

        // Attempt approve with correct hash and valid HMAC
        Environment.SetEnvironmentVariable("GATEWAY_SECRET", TestSecurityContext.GatewaySecret);
        var validHash = ComputeHash(payload);
        var validHmac = HmacSigner.ComputeSignature(validHash, TestSecurityContext.GatewaySecret);
        var goodApproveResult = await service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: validHash,
            HmacSignature: validHmac),
            PrincipalInvestigator,
            CancellationToken.None);

        goodApproveResult.IsSuccess.Should().BeTrue();
        goodApproveResult.Value!.Status.Should().Be(FormSubmissionStatus.Approved);
        goodApproveResult.Value!.IsReadonly.Should().BeTrue();
    }

    [Fact]
    public async Task FormApplicationService_Approve_WithInvalidHmac_ReturnsInvalidHmacSignatureError()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi_investigator_01", "PI approval");

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        var formId = createResult.Value!.Id;
        await service.CompleteAsync(formId, CancellationToken.None);

        Environment.SetEnvironmentVariable("GATEWAY_SECRET", TestSecurityContext.GatewaySecret);
        var validHash = ComputeHash(payload);

        // Attempt approve with missing/invalid HMAC
        var badApproveResult = await service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: validHash,
            HmacSignature: "invalid-hmac-signature"),
            PrincipalInvestigator,
            CancellationToken.None);

        badApproveResult.IsFailure.Should().BeTrue();
        badApproveResult.Error!.Code.Should().Be("INVALID_HMAC_SIGNATURE");
    }

    /// <summary>
    /// Validates that approving an eCRF form submission with an unapproved signing reason is strictly rejected.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public void ApproveForm_WithInvalidSigningReason_ReturnsInvalidSigningReasonError()
    {
        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var form = FormSubmission.Initialize(
            studyId: new StudyCode("STUDY-001"),
            subjectId: new SubjectCode("SUBJ-001"),
            formId: new FormId("FORM-VS"),
            initialPayload: payload).Value!;

        form.Complete();
        var manifest = SignatureManifest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "Because I felt like it.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: ComputeHash(payload));

        var approveResult = form.Approve(manifest);
        approveResult.IsFailure.Should().BeTrue();
        approveResult.Error!.Code.Should().Be("INVALID_SIGNING_REASON");
    }

    /// <summary>
    /// Validates that attempting to sign an eCRF with an unauthorized role or null manifest is rejected.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task FormApplicationService_Approve_WithUnauthorizedSignerRole_ReturnsUnauthorizedError()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi_investigator_01", "PI approval");

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        var formId = createResult.Value!.Id;
        await service.CompleteAsync(formId, CancellationToken.None);

        // Attempt approve with unauthorized role (e.g., patient, data_entry_clerk)
        var unauthorizedResult = await service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
            SignerId: "clerk-01",
            SignerRole: "data_entry_clerk",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: ComputeHash(payload)),
            new AuthenticatedCaller("clerk-01", ["data_entry_clerk"]),
            CancellationToken.None);

        unauthorizedResult.IsFailure.Should().BeTrue();
        unauthorizedResult.Error!.Code.Should().Be("UNAUTHORIZED_SIGNER_ROLE");

        // Attempt approve with null manifest
        var nullManifestRequest = new FormSubmissionApproveRequest("PI approval and sign-off.", null!);
        var nullManifestResult = await service.ApproveAsync(formId, nullManifestRequest, PrincipalInvestigator, CancellationToken.None);
        nullManifestResult.IsFailure.Should().BeTrue();
        nullManifestResult.Error!.Code.Should().Be("SIGNATURE_MANIFEST_REQUIRED");
    }

    /// <summary>
    /// Validates that approving an eCRF form submission throws an InvalidOperationException when GATEWAY_SECRET environment variable is unconfigured, empty, or whitespace.
    /// @req:SRS-EDC-003
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FormApplicationService_Approve_WithUnconfiguredSecret_ThrowsInvalidOperationException(string? secret)
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi_investigator_01", "PI approval");

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        var formId = createResult.Value!.Id;
        await service.CompleteAsync(formId, CancellationToken.None);

        string? previousSecret = Environment.GetEnvironmentVariable("GATEWAY_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("GATEWAY_SECRET", secret);
            var validHash = ComputeHash(payload);

            var action = () => service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
                SignerId: "pi-user-01",
                SignerRole: "PRINCIPAL_INVESTIGATOR",
                SigningReason: "I attest that I have reviewed and approved these clinical data.",
                SignedAt: FakeClock.DefaultStart.UtcDateTime,
                PayloadHash: validHash,
                HmacSignature: "any-hmac"),
                PrincipalInvestigator,
                CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*GATEWAY_SECRET is not configured*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_SECRET", previousSecret);
        }
    }

    /// <summary>
    /// Validates that approving an eCRF form submission throws an InvalidOperationException when GATEWAY_SECRET in IConfiguration is unconfigured.
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    public async Task FormApplicationService_Approve_WithUnconfiguredConfigurationSecret_ThrowsInvalidOperationException()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi_investigator_01", "PI approval");

        var inMemoryConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GATEWAY_SECRET"] = "   "
            })
            .Build();

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit,
            configuration: inMemoryConfig);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        var formId = createResult.Value!.Id;
        await service.CompleteAsync(formId, CancellationToken.None);

        string? previousSecret = Environment.GetEnvironmentVariable("GATEWAY_SECRET");
        try
        {
            Environment.SetEnvironmentVariable("GATEWAY_SECRET", null);
            var validHash = ComputeHash(payload);

            var action = () => service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
                SignerId: "pi-user-01",
                SignerRole: "PRINCIPAL_INVESTIGATOR",
                SigningReason: "I attest that I have reviewed and approved these clinical data.",
                SignedAt: FakeClock.DefaultStart.UtcDateTime,
                PayloadHash: validHash,
                HmacSignature: "any-hmac"),
                PrincipalInvestigator,
                CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*GATEWAY_SECRET is not configured*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GATEWAY_SECRET", previousSecret);
        }
    }

    /// <summary>
    /// Validates that unlocking an approved form stages a REVOKE_SIGNATURE audit event with complete SignatureManifest snapshot.
    /// @req:PRD-SYS-053
    /// @req:SRS-EDC-003
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task FormApplicationService_UnlockAsync_StagesRevokeSignatureAuditAction_WithSignatureManifestSnapshot()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi-investigator-01", "Unlock request");
        var recordingWriter = new RecordingAuditLogWriter();

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit,
            auditLogWriter: recordingWriter);

        var payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: payload), CancellationToken.None);

        var formId = createResult.Value!.Id;
        await service.CompleteAsync(formId, CancellationToken.None);

        Environment.SetEnvironmentVariable("GATEWAY_SECRET", TestSecurityContext.GatewaySecret);
        var validHash = ComputeHash(payload);
        var validHmac = HmacSigner.ComputeSignature(validHash, TestSecurityContext.GatewaySecret);

        var approveResult = await service.ApproveAsync(formId, FormSubmissionApproveRequest.Create(
            SignerId: "pi-user-01",
            SignerRole: "PRINCIPAL_INVESTIGATOR",
            SigningReason: "I attest that I have reviewed and approved these clinical data.",
            SignedAt: FakeClock.DefaultStart.UtcDateTime,
            PayloadHash: validHash,
            HmacSignature: validHmac),
            PrincipalInvestigator,
            CancellationToken.None);

        approveResult.IsSuccess.Should().BeTrue();

        const string unlockReason = "Lab value discrepancy correction requested by Sponsor";
        var unlockResult = await service.UnlockAsync(formId, unlockReason, CancellationToken.None);

        unlockResult.IsSuccess.Should().BeTrue();
        unlockResult.Value!.Status.Should().Be(FormSubmissionStatus.Draft);

        recordingWriter.Calls.Should().HaveCount(1);
        var call = recordingWriter.Calls[0];
        call.EntityName.Should().Be(nameof(FormSubmission));
        call.RecordId.Should().Be(formId);
        call.Action.Should().Be("REVOKE_SIGNATURE");
        call.UserId.Should().Be("pi-investigator-01");
        call.ChangeReason.Should().Be(unlockReason);

        call.OldValues.Should().NotBeNull();
        call.OldValues!["status"].Should().Be("Approved");
        call.OldValues!["signature_manifest"].Should().BeOfType<Dictionary<string, object?>>();

        var manifestSnap = (Dictionary<string, object?>)call.OldValues!["signature_manifest"]!;
        manifestSnap["signer_id"].Should().Be("pi-user-01");
        manifestSnap["signer_role"].Should().Be("PRINCIPAL_INVESTIGATOR");
        manifestSnap["signing_reason"].Should().Be("I attest that I have reviewed and approved these clinical data.");
        manifestSnap["payload_hash"].Should().Be(validHash);
        manifestSnap["hmac_signature"].Should().Be(validHmac);

        call.NewValues.Should().NotBeNull();
        call.NewValues!["status"].Should().Be("Draft");
        call.NewValues!["signature_manifest"].Should().BeNull();
    }

    /// <summary>
    /// Validates that attempting to unlock a form that is not approved fails and does not stage any audit action.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public async Task FormApplicationService_UnlockAsync_WhenUnlockFails_DoesNotStageAuditAction()
    {
        var formRepo = new InMemoryFormSubmissionRepository();
        var obsRepo = new InMemoryObservationRepository();
        var ucum = new UcumNormalizationService();
        var audit = new FakeCurrentAuditContextAccessor("pi-investigator-01", "Unlock request");
        var recordingWriter = new RecordingAuditLogWriter();

        var service = new FormSubmissionApplicationService(
            formRepo,
            obsRepo,
            ucum,
            audit,
            auditLogWriter: recordingWriter);

        var createResult = await service.CreateAsync(new FormSubmissionCreateRequest(
            StudyId: "STUDY-001",
            SubjectId: "SUBJ-001",
            FormId: "FORM-VS",
            Payload: "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}"), CancellationToken.None);

        var formId = createResult.Value!.Id;

        // Form is Draft, not Approved -> unlock must fail
        var unlockResult = await service.UnlockAsync(formId, "Correction needed", CancellationToken.None);

        unlockResult.IsFailure.Should().BeTrue();
        unlockResult.Error!.Code.Should().Be("FORM_NOT_APPROVED");

        recordingWriter.Calls.Should().BeEmpty();
    }
}
