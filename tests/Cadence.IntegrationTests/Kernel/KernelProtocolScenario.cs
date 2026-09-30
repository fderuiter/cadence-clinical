using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Trust;

using FluentAssertions;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Builds protocol scenarios through the production pipeline: a tenant with a sponsor and a study, protocol versions
/// driven to a status, and attestations signed by the fixture's person. The study is seeded directly because no
/// kernel command registers studies yet.
/// </summary>
internal sealed class KernelProtocolScenario(KernelPostgresFixture fixture)
{
    public const string Reason = "Integration test";

    public const string Content = """{"study":{"name":"CARDIO-1","arms":["A","B"]}}""";

    public static readonly ProtocolAmendment Amendment = new(
        "Add a cohort",
        "Enrolment is slower than planned",
        AmendmentClassification.Substantial,
        "Adds cohort C",
        ["arm:C"],
        TransitionPolicy.RequiresReconsent);

    /// <summary>A new tenant with a sponsor organization and one study.</summary>
    public async Task<(TenantId Tenant, StudyId Study)> NewStudyAsync(CancellationToken ct)
    {
        var suffix = Suffix();
        var tenant = TenantId.New();
        var sponsor = OrganizationId.New();
        await OkAsync(new ProvisionTenant(tenant, $"T-{suffix}", $"Tenant {suffix}", $"tenant-{suffix}", Reason), ct);
        await OkAsync(new RegisterOrganization(tenant, sponsor, $"ORG-{suffix}", $"Sponsor {suffix}", OrganizationKind.Sponsor, $"org-{suffix}", Reason), ct);

        var study = new Study(StudyId.New(), tenant, sponsor, $"STUDY-{suffix}", "Cardiovascular outcomes", "Phase 3", KernelPostgresFixture.ActorId, "Seeded for test");
        await fixture.SeedAsync(study);
        return (tenant, study.Id);
    }

    public Task<CommandResult<ProtocolVersionState>> CreateDraftAsync(TenantId tenant, StudyId study, ProtocolVersionId id, CancellationToken ct, string content = Content) =>
        fixture.Sender.SendAsync(new CreateProtocolDraft(tenant, study, id, content, Key(), Reason), cancellationToken: ct);

    public Task<CommandResult<ProtocolVersionState>> SubmitAsync(TenantId tenant, ProtocolVersionId id, CancellationToken ct) =>
        fixture.Sender.SendAsync(new SubmitProtocolVersionForReview(tenant, id, Key(), Reason), cancellationToken: ct);

    public Task<CommandResult<AttestationId>> AttestAsync(TenantId tenant, AttestationMeaning meaning, AttestationTarget target, CancellationToken ct) =>
        fixture.Sender.SendAsync(
            new Attest(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}"),
            cancellationToken: ct);

    public Task<CommandResult<ProtocolVersionState>> ApproveAsync(TenantId tenant, ProtocolVersionId id, AttestationId attestation, CancellationToken ct) =>
        fixture.Sender.SendAsync(new ApproveProtocolVersion(tenant, id, attestation, Key(), Reason), cancellationToken: ct);

    /// <summary>A study's first version, submitted for review; returns its state.</summary>
    public async Task<ProtocolVersionState> InReviewAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var id = ProtocolVersionId.New();
        (await CreateDraftAsync(tenant, study, id, ct)).IsSuccess.Should().BeTrue();
        return await OkAsync(new SubmitProtocolVersionForReview(tenant, id, Key(), Reason), ct);
    }

    /// <summary>A study's first version, approved with an attestation; returns its state and the attestation.</summary>
    public async Task<(ProtocolVersionState Version, AttestationId Attestation)> ApprovedAsync(TenantId tenant, StudyId study, CancellationToken ct)
    {
        var version = await InReviewAsync(tenant, study, ct);
        var attestation = await OkAsync(AttestCommand(tenant, AttestationMeaning.Approved, Target(version)), ct);
        return (await OkAsync(new ApproveProtocolVersion(tenant, version.ProtocolVersionId, attestation, Key(), Reason), ct), attestation);
    }

    public Attest AttestCommand(TenantId tenant, AttestationMeaning meaning, AttestationTarget target) =>
        new(tenant, AttestationId.New(), meaning, target, fixture.Clock.UtcNow.AddMinutes(-1), "pwd+otp", Key(), $"Signing as {meaning}");

    public static AttestationTarget Target(ProtocolVersionState version) =>
        new(AttestationTarget.ProtocolVersionKind, version.ProtocolVersionId.Value, version.ContentHash!);

    public static string Key() => $"key-{Suffix()}";

    public async Task<TResult> OkAsync<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        var result = await fixture.Sender.SendAsync(command, cancellationToken: ct);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        return result.Value!;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..10];
}
