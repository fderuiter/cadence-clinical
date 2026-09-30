using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.References;
using Cadence.Kernel.Trust;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// Evidence artifacts through the production pipeline against PostgreSQL (spec §8.3, #4729): registration with a
/// first version, later versions numbered in order, events, attestations over a version, and the insert-only guard
/// that keeps versions immutable whoever connects. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelArtifactPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>
{
    private readonly KernelProtocolScenario _scenario = new(fixture);

    /// <summary>
    /// Validates the whole path: an ethics approval is registered for a study with version 1, amended as version 2, and
    /// both versions are stored with their hash, size, media type and storage reference. Each step raises its event.
    /// </summary>
    [Fact(DisplayName = "Artifact_RegisterAndAddVersion_EndToEnd")]
    public async Task ArtifactRegisterAndAddVersionEndToEnd()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var artifact = ArtifactId.New();
        var first = ArtifactVersionId.New();
        var second = ArtifactVersionId.New();

        var registered = await _scenario.OkAsync(Register(tenant, artifact, first, study, Content("approval v1")), ct);
        Tick();
        var amended = await _scenario.OkAsync(AddVersion(tenant, artifact, second, Content("approval v2")), ct);

        registered.Should().Be(new ArtifactState(artifact, ArtifactKind.EthicsApproval, study, "IRB approval", first, 1, Content("approval v1").ContentHash));
        amended.Should().Be(registered with { LatestVersionId = second, LatestVersionNumber = 2, LatestContentHash = Content("approval v2").ContentHash });

        await using var db = fixture.CreateContext(tenant);
        var versions = await db.ArtifactVersions.AsNoTracking().Where(v => v.ArtifactId == artifact).OrderBy(v => v.Number).ToListAsync(ct);
        versions.Select(v => (v.Id, v.Number, v.ContentHash, v.SizeBytes, v.MediaType, v.StorageRef)).Should().Equal(
            (first, 1, Content("approval v1").ContentHash, 11L, "application/pdf", "blob://evidence/approval v1"),
            (second, 2, Content("approval v2").ContentHash, 11L, "application/pdf", "blob://evidence/approval v2"));
        var events = await db.DomainEvents.AsNoTracking().Where(e => e.TenantId == tenant).OrderBy(e => e.Recorded).ThenBy(e => e.EventId).Select(e => e.Type).ToListAsync(ct);
        events.Should().ContainInOrder("ArtifactRegistered", "ArtifactVersioned");
    }

    /// <summary>
    /// Validates the command rules: an unknown study or artifact, a reused artifact or version id, unchanged content
    /// and malformed content are refused; tenant-wide evidence (no study) is allowed.
    /// </summary>
    [Fact(DisplayName = "Artifact_Commands_RefuseUnknownDuplicateAndUnchanged")]
    public async Task ArtifactCommandsRefuseUnknownDuplicateAndUnchanged()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var artifact = ArtifactId.New();
        var first = ArtifactVersionId.New();
        await _scenario.OkAsync(Register(tenant, artifact, first, study, Content("v1")), ct);

        (await SendAsync(Register(tenant, ArtifactId.New(), ArtifactVersionId.New(), StudyId.New(), Content("x")), ct)).Error.Should().Be(EvidenceErrors.StudyUnknown);
        (await SendAsync(Register(tenant, artifact, ArtifactVersionId.New(), study, Content("y")), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
        (await SendAsync(AddVersion(tenant, ArtifactId.New(), ArtifactVersionId.New(), Content("z")), ct)).Error.Should().Be(KernelErrors.NotFound);
        (await SendAsync(AddVersion(tenant, artifact, first, Content("w")), ct)).Error.Should().Be(KernelErrors.AlreadyExists);
        (await SendAsync(AddVersion(tenant, artifact, ArtifactVersionId.New(), Content("v1")), ct)).Error.Should().Be(EvidenceErrors.ContentUnchanged);
        (await SendAsync(AddVersion(tenant, artifact, ArtifactVersionId.New(), Content("v2") with { MediaType = "PDF" }), ct)).Error!.Code
            .Should().Be(EvidenceErrors.ContentInvalid.Code);

        var tenantWide = await _scenario.OkAsync(Register(tenant, ArtifactId.New(), ArtifactVersionId.New(), studyId: null, Content("ranges")), ct);
        tenantWide.StudyId.Should().BeNull();
    }

    /// <summary>
    /// Validates signing evidence (spec §8.3, §10.3): an attestation targets an artifact version by its content hash.
    /// A hash that does not match the version, or a version that does not exist, is refused.
    /// </summary>
    [Fact(DisplayName = "Attest_ArtifactVersion_SignsItsContentHash")]
    public async Task AttestArtifactVersionSignsItsContentHash()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var version = ArtifactVersionId.New();
        var state = await _scenario.OkAsync(Register(tenant, ArtifactId.New(), version, study, Content("consent")), ct);

        Tick();
        var signed = await _scenario.AttestAsync(tenant, AttestationMeaning.Reviewed, new AttestationTarget(NodeKinds.ArtifactVersion, version.Value, state.LatestContentHash), ct);
        signed.IsSuccess.Should().BeTrue(signed.Error?.Message);

        Tick();
        var wrongHash = await _scenario.AttestAsync(tenant, AttestationMeaning.Reviewed, new AttestationTarget(NodeKinds.ArtifactVersion, version.Value, Content("other").ContentHash), ct);
        wrongHash.Error!.Code.Should().Be(TrustErrors.TargetContentChanged.Code);

        Tick();
        var unknown = await _scenario.AttestAsync(tenant, AttestationMeaning.Reviewed, new AttestationTarget(NodeKinds.ArtifactVersion, Guid.CreateVersion7(), state.LatestContentHash), ct);
        unknown.Error.Should().Be(TrustErrors.TargetUnknown);
    }

    /// <summary>
    /// Validates immutability in the database: even the superuser cannot update, delete or truncate an artifact
    /// version (SQLSTATE KA001), and the check constraints refuse a malformed hash or an empty file.
    /// </summary>
    [Fact(DisplayName = "ArtifactVersions_AreInsertOnly_ForEveryDatabaseUser")]
    public async Task ArtifactVersionsAreInsertOnlyForEveryDatabaseUser()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, study) = await _scenario.NewStudyAsync(ct);
        var artifact = ArtifactId.New();
        var version = ArtifactVersionId.New();
        await _scenario.OkAsync(Register(tenant, artifact, version, study, Content("lab")), ct);

        foreach (var statement in new[]
        {
            $"UPDATE kernel.artifact_versions SET storage_ref = 'blob://elsewhere' WHERE id = '{version.Value}'",
            $"UPDATE kernel.artifact_versions SET content_hash = '{Content("forged").ContentHash}' WHERE id = '{version.Value}'",
            $"DELETE FROM kernel.artifact_versions WHERE id = '{version.Value}'",

            // CASCADE, because qualifications reference their evidence version and a plain TRUNCATE stops at the
            // foreign key before the guard runs.
            "TRUNCATE kernel.artifact_versions CASCADE",
        })
        {
            var act = () => ExecuteAsAdminAsync(statement, ct);
            (await act.Should().ThrowAsync<PostgresException>(statement)).Which.SqlState.Should().Be(InsertOnlySql.ViolationSqlState);
        }

        foreach (var (hash, size) in new[] { ("sha256:XYZ", 1L), (Content("empty").ContentHash, 0L) })
        {
            var act = () => ExecuteAsAdminAsync(
                $"""
                INSERT INTO kernel.artifact_versions (id, tenant_id, artifact_id, number, content_hash, size_bytes, media_type, storage_ref, recorded_at)
                VALUES ('{Guid.CreateVersion7()}', '{tenant.Value}', '{artifact.Value}', 9, '{hash}', {size}, 'application/pdf', 'blob://x', now())
                """,
                ct);
            (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    private void Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));

    private Task<CommandResult<ArtifactState>> SendAsync(ICommand<ArtifactState> command, CancellationToken ct) =>
        fixture.Sender.SendAsync(command, cancellationToken: ct);

    private static RegisterArtifact Register(TenantId tenant, ArtifactId artifact, ArtifactVersionId version, StudyId? studyId, ArtifactContent content) =>
        new(tenant, artifact, version, ArtifactKind.EthicsApproval, studyId, "IRB approval", content, KernelProtocolScenario.Key(), "Filed approval");

    private static AddArtifactVersion AddVersion(TenantId tenant, ArtifactId artifact, ArtifactVersionId version, ArtifactContent content) =>
        new(tenant, artifact, version, content, KernelProtocolScenario.Key(), "Amended approval");

    private static ArtifactContent Content(string bytes) =>
        new(ContentHash.OfBytes(Encoding.UTF8.GetBytes(bytes)), Encoding.UTF8.GetByteCount(bytes), "application/pdf", $"blob://evidence/{bytes}");

    private async Task ExecuteAsAdminAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}
