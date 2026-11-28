using System;
using System.Security.Cryptography;

using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Contracts;
using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.References;
using Cadence.Security;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Security;

public sealed class DatabaseLockCertificateAndScopeHierarchyTests
{
    private static KernelLockCertificateVerifier CreateVerifier(string? pem = null) =>
        new(Options.Create(new KernelLockCertificateVerifierOptions
        {
            PublicKeyPem = pem ?? TestSecurityContext.KernelLockCertPublicKeyPem,
        }));

    private static DatabaseLockCertificateContract ToContract(DatabaseLockCertificateRecord signed) =>
        new(
            signed.CertificateId,
            signed.TenantId,
            signed.StudyId,
            signed.StudyCode,
            signed.LockId,
            signed.Milestone.ToString(),
            signed.AttestationId,
            signed.SignedBy,
            signed.IssuedAt,
            signed.KeyId,
            signed.Algorithm,
            signed.PayloadSha256,
            signed.FingerprintSha256,
            signed.SignatureBase64);

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public void SignedCertificate_VerifiesOfflineWithPublicKeyOnly()
    {
        var issuedAt = new DateTimeOffset(2026, 5, 14, 16, 32, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var studyId = StudyId.New();
        var lockId = AssertionId.New();
        var attestationId = AttestationId.New();

        var signed = TestSecurityContext.TestLockCertificateSigner.Sign(
            tenantId,
            studyId,
            "CADENCE-101",
            lockId,
            LockMilestone.FinalDatabaseLock,
            attestationId,
            "dm.alice",
            issuedAt);

        var contract = ToContract(signed);
        var verifier = CreateVerifier();
        bool valid = verifier.TryVerifyForStudyLock(
            contract,
            "CADENCE-101",
            lockId.ToString(),
            callerTenantCode: tenantId.ToString(),
            out string? failureReason);

        valid.Should().BeTrue(failureReason);
        failureReason.Should().BeNull();
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public void TamperedCertificateOrWrongStudyOrWrongTenantOrRevoked_FailsOfflineVerification()
    {
        var signed = TestSecurityContext.SignTestLockCertificate(
            studyId: "CADENCE-101",
            studyCode: "CADENCE-101",
            lockId: "LOCK-CADENCE-101",
            milestone: LockMilestone.FinalDatabaseLock,
            tenantId: "tenant-alpha",
            signedBy: "dm.alice");

        var contract = ToContract(signed);
        var verifier = CreateVerifier();

        verifier.TryVerifyForStudyLock(contract with { SignedBy = "mallory" }, "CADENCE-101", "LOCK-CADENCE-101", "tenant-alpha", out string? tamperReason)
            .Should().BeFalse();
        tamperReason.Should().Contain("digest mismatch");

        verifier.TryVerifyForStudyLock(contract, "OTHER-STUDY", "LOCK-CADENCE-101", "tenant-alpha", out string? studyReason)
            .Should().BeFalse();
        studyReason.Should().Contain("does not match requested study");

        verifier.TryVerifyForStudyLock(contract, "CADENCE-101", "OTHER-LOCK", "tenant-alpha", out string? lockReason)
            .Should().BeFalse();
        lockReason.Should().Contain("does not match requested lock");

        verifier.TryVerifyForStudyLock(contract, "CADENCE-101", "LOCK-CADENCE-101", "tenant-beta", out string? tenantReason)
            .Should().BeFalse();
        tenantReason.Should().Contain("tenant does not match");

        DatabaseLockCertificateCrypto.VerifyCertificate(
                signed with { Revoked = true },
                TestSecurityContext.KernelLockCertPublicKeyPem,
                out string? revokedReason)
            .Should().BeFalse();
        revokedReason.Should().Contain("revoked");
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public void PrivateKeyPem_IsRejectedInInteropVerificationConfig()
    {
        using var keyPair = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string privatePem = keyPair.ExportPkcs8PrivateKeyPem();

        KernelLockCertificateVerifier.IsAcceptableConfiguredPem(privatePem).Should().BeFalse();
        KernelLockCertificateVerifier.IsAcceptableConfiguredPem(TestSecurityContext.KernelLockCertPublicKeyPem).Should().BeTrue();

        var signed = TestSecurityContext.SignTestLockCertificate("CADENCE-101");
        var verifier = CreateVerifier(privatePem);
        verifier.TryVerifyForStudyLock(ToContract(signed), "CADENCE-101", signed.LockId, TestSecurityContext.TenantCode, out string? reason)
            .Should().BeFalse();
        reason.Should().Contain("private key");
    }

    [Fact]
    public void LockScopesCovering_IncludesProperHierarchyInOrder()
    {
        var study = StudyId.New();
        var site = StudySiteId.New();
        var enrollment = EnrollmentId.New();
        var instance = CollectionInstanceId.New();
        var observation = ObservationId.New();

        LockScopes.Covering(study).Should().Equal(NodeRef.For(study));
        LockScopes.Covering(study, site).Should().Equal(NodeRef.For(study), NodeRef.For(site));
        LockScopes.Covering(study, site, enrollment).Should().Equal(
            NodeRef.For(study),
            NodeRef.For(site),
            NodeRef.For(enrollment));
        LockScopes.Covering(study, site, enrollment, observation, instance).Should().Equal(
            NodeRef.For(study),
            NodeRef.For(site),
            NodeRef.For(enrollment),
            NodeRef.For(instance),
            NodeRef.For(observation));
    }
}
