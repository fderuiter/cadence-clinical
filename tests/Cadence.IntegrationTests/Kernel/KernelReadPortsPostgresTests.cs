using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol;

using FluentAssertions;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The read ports a kernel host uses before and while serving the Canvas, read as the application role: resolving the
/// gateway's tenant code (#4780) and listing a study's protocol versions (spec §6.2). Skipped unless
/// <c>CADENCE_TEST_POSTGRES</c> or <c>CADENCE_TEST_DOCKER=1</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelReadPortsPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates the tenant code directory: an active tenant's exact code resolves to its id; an unknown code, a code
    /// in another case, an over-long code and a deactivated tenant's code resolve to nothing. The application role
    /// still cannot read the tenants table.
    /// </summary>
    [Fact(DisplayName = "TenantDirectory_ResolvesActiveTenantCodesOnly")]
    public async Task TenantDirectoryResolvesActiveTenantCodesOnly()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var active = await ProvisionAsync(ct);
        var inactive = await ProvisionAsync(ct);
        await ExecuteAsAdminAsync($"UPDATE kernel.tenants SET is_active = false WHERE id = '{inactive.Id.Value}'", ct);
        var directory = fixture.TenantDirectory;

        (await directory.FindActiveByCodeAsync(active.Code, ct)).Should().Be(active.Id);
        (await directory.FindActiveByCodeAsync(active.Code.ToLowerInvariant(), ct)).Should().BeNull();
        (await directory.FindActiveByCodeAsync(inactive.Code, ct)).Should().BeNull();
        (await directory.FindActiveByCodeAsync("T-UNKNOWN", ct)).Should().BeNull();
        (await directory.FindActiveByCodeAsync("", ct)).Should().BeNull();
        (await directory.FindActiveByCodeAsync(new string('T', 65), ct)).Should().BeNull();
        (await directory.FindActiveByCodeAsync("x' OR '1'='1", ct)).Should().BeNull();

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);
        await using var read = new NpgsqlCommand("SELECT count(*) FROM kernel.tenants", connection);
        ((long)(await read.ExecuteScalarAsync(ct))!).Should().Be(0);
    }

    /// <summary>
    /// Validates the protocol version reader: a study's versions come newest first with their lineage, status, hash,
    /// amendment intent and approval; one version reads by id; another tenant sees neither.
    /// </summary>
    [Fact(DisplayName = "ProtocolVersionReader_ListsNewestFirst_WithinTheTenant")]
    public async Task ProtocolVersionReaderListsNewestFirstWithinTheTenant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var (first, attestation) = await _scenario.ApprovedAsync(tenant, study, ct);
        var forkId = ProtocolVersionId.New();
        await _scenario.OkAsync(
            new ForkProtocolVersion(tenant, first.ProtocolVersionId, forkId, KernelProtocolScenario.Amendment, KernelProtocolScenario.Key(), "Amendment 1"), ct);
        var reader = fixture.ProtocolVersions;

        var versions = await reader.ListForStudyAsync(tenant, study, ct);

        versions.Should().Equal(
            new ProtocolVersionSummary(forkId, study, 2, first.ProtocolVersionId, ProtocolVersionStatus.Draft, null, KernelProtocolScenario.Amendment.Intent, null, null),
            new ProtocolVersionSummary(first.ProtocolVersionId, study, 1, null, ProtocolVersionStatus.Approved, first.ContentHash, null, attestation, null));
        (await reader.GetAsync(tenant, forkId, ct)).Should().Be(versions[0]);
        (await reader.GetAsync(tenant, ProtocolVersionId.New(), ct)).Should().BeNull();
        (await reader.ListForStudyAsync(tenant, StudyId.New(), ct)).Should().BeEmpty();

        var (other, _) = await _scenario.NewStudyAsync(ct);
        (await reader.GetAsync(other, forkId, ct)).Should().BeNull();
        (await reader.ListForStudyAsync(other, study, ct)).Should().BeEmpty();
    }

    private async Task<(TenantId Id, string Code)> ProvisionAsync(CancellationToken ct)
    {
        var id = TenantId.New();
        var code = $"T-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await _scenario.OkAsync(new ProvisionTenant(id, code, "Tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        return (id, code);
    }

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
