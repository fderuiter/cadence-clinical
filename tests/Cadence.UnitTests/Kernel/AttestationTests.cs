using System;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Signing an attestation (spec §9.5, 21 CFR 11.50, 11.70, 11.200): only a person signs, after a fresh
/// authentication, and an attestation satisfies a requirement only for its meaning, its record and its content hash.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AttestationTests
{
    private static readonly DateTimeOffset Now = FakeClock.DefaultStart;

    private static readonly AttestationTarget Target =
        new(AttestationTarget.ProtocolVersionKind, Guid.CreateVersion7(), ContentHash.Compute("""{"v":1}"""));

    /// <summary>An attestation names its target with the same kind a <see cref="NodeRef"/> uses (ADR-2481).</summary>
    [Fact(DisplayName = "TargetKind_IsARegisteredNodeKind")]
    public void TargetKindIsARegisteredNodeKind()
    {
        NodeKinds.IsRegistered(AttestationTarget.ProtocolVersionKind).Should().BeTrue();
        NodeKinds.IdentifierTypeOf(AttestationTarget.ProtocolVersionKind).Should().Be<ProtocolVersionId>();
    }

    /// <summary>A signature records who, what, which content, how and when the signer authenticated, and when it was recorded.</summary>
    [Fact(DisplayName = "Sign_RecordsTheSignatureManifest")]
    public void SignRecordsTheSignatureManifest()
    {
        var id = AttestationId.New();
        var tenant = TenantId.New();
        var authenticated = Now.AddMinutes(-2).ToOffset(TimeSpan.FromHours(2)).AddTicks(7);

        var attestation = Attestation.Sign(
            id, tenant, Actor.Person("00u1signer"), AttestationMeaning.Approved, Target, authenticated, "pwd+otp", Now, "Approve v1").Value!;

        attestation.Id.Should().Be(id);
        attestation.TenantId.Should().Be(tenant);
        attestation.ActorId.Should().Be("00u1signer");
        attestation.Meaning.Should().Be(AttestationMeaning.Approved);
        attestation.Target.Should().Be(Target);
        attestation.AuthenticatedAt.Should().Be(Now.AddMinutes(-2));
        attestation.AuthenticatedAt.Offset.Should().Be(TimeSpan.Zero);
        attestation.Method.Should().Be("pwd+otp");
        attestation.RecordedAt.Should().Be(Now);
        attestation.Reason.Should().Be("Approve v1");
    }

    /// <summary>
    /// Refusals: a service account or agent cannot sign, the method is required, the authentication must be at most
    /// five minutes old and not in the future, and the target needs a well-formed content hash.
    /// </summary>
    [Fact(DisplayName = "Sign_RefusesNonPersons_StaleAuthentication_AndMalformedTargets")]
    public void SignRefusesNonPersonsStaleAuthenticationAndMalformedTargets()
    {
        static KernelError? Sign(Actor actor, DateTimeOffset authenticated, string method = "pwd", AttestationTarget? target = null) =>
            Attestation.Sign(AttestationId.New(), TenantId.New(), actor, AttestationMeaning.Approved, target ?? Target, authenticated, method, Now, "Sign").Error;

        var person = Actor.Person("00u1signer");

        Sign(Actor.ServiceAccount("relay"), Now).Should().Be(TrustErrors.PersonRequired);
        Sign(new Actor(ActorKind.AutomationAgent, "agent:mapper"), Now).Should().Be(TrustErrors.PersonRequired);
        Sign(person, Now, method: " ").Should().Be(TrustErrors.MethodRequired);
        Sign(person, Now - Attestation.MaxAuthenticationAge).Should().BeNull();
        Sign(person, Now - Attestation.MaxAuthenticationAge - TimeSpan.FromMicroseconds(1)).Should().Be(TrustErrors.AuthenticationStale);
        Sign(person, Now.AddSeconds(1)).Should().Be(TrustErrors.AuthenticationStale);
        Sign(person, Now, target: Target with { ContentHash = "abc" }).Should().Be(TrustErrors.TargetNotSignable);
        Sign(person, Now, target: Target with { Kind = " " }).Should().Be(TrustErrors.TargetNotSignable);
    }

    /// <summary>An attestation satisfies only its own signer, meaning, record and content hash (21 CFR 11.50, 11.70).</summary>
    [Fact(DisplayName = "Mismatch_NamesWhatDiffers")]
    public void MismatchNamesWhatDiffers()
    {
        var signer = Actor.Person("00u1signer");
        var attestation = Attestation.Sign(
            AttestationId.New(), TenantId.New(), signer, AttestationMeaning.Approved, Target, Now, "pwd", Now, "Approve").Value!;

        attestation.Mismatch(AttestationMeaning.Approved, Target, signer).Should().BeNull();
        attestation.Mismatch(AttestationMeaning.Approved, Target, Actor.Person("00u2other")).Should().Contain("someone else");
        attestation.Mismatch(AttestationMeaning.Approved, Target, Actor.ServiceAccount("00u1signer")).Should().Contain("someone else");
        attestation.Mismatch(AttestationMeaning.Reviewed, Target, signer).Should().Contain("Reviewed");
        attestation.Mismatch(AttestationMeaning.Approved, Target with { Id = Guid.CreateVersion7() }, signer).Should().Contain("different record");
        attestation.Mismatch(AttestationMeaning.Approved, Target with { Kind = "artifact" }, signer).Should().Contain("different record");
        attestation.Mismatch(AttestationMeaning.Approved, Target with { ContentHash = ContentHash.Compute("""{"v":2}""") }, signer)
            .Should().Contain("changed after it was signed");
    }
}
