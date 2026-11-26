using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Endpoints;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Application.Services;

public interface ISceExecutionService
{
    Task<IResult> ExecuteProgramAsync(ExecuteProgramRequestDto req, HttpContext httpContext, CancellationToken ct);
    Task<IResult> ListRunsAsync(string studyId, string? programId, string? snapshotId, HttpContext httpContext, CancellationToken ct);
    Task<IResult> GetRunAsync(string runId, HttpContext httpContext, CancellationToken ct);
    Task<IResult> VerifyRunManifestAsync(string runId, HttpContext httpContext, CancellationToken ct);
    Task<IResult> GetRunArtifactAsync(string runId, string artifactId, HttpContext httpContext, CancellationToken ct);
}

public sealed class SceExecutionService(
    InteropDbContext db,
    IInteropUnitOfWork unitOfWork,
    ISceDockerEngineDriver driver,
    ILockfileValidatorService lockfileValidator,
    SceTokenSigner signer,
    IClock clock) : ISceExecutionService
{
    public async Task<IResult> ExecuteProgramAsync(ExecuteProgramRequestDto req, HttpContext httpContext, CancellationToken ct)
    {
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, req.StudyId))
        {
            return SceProgramLockService.OutsideStudy();
        }

        var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
        string userId = caller.UserId;
        string userRole = CallerScope.Roles(httpContext);
        var now = clock.UtcNow;

        var program = await db.StatisticalPrograms
            .AsNoTracking()
            .FirstOrDefaultAsync(p => (p.Id == req.ProgramId || p.Name == req.ProgramId) && p.StudyId == req.StudyId, ct);

        if (program == null)
        {
            return Results.NotFound(new { error = $"Statistical program '{req.ProgramId}' not found." });
        }

        DatabaseLockAuthToken? lockToken = null;
        if (req.IsUnblinded)
        {
            if (!PermissionMatrix.HasPermission(caller, SceEndpoints.ExecuteUnblindedPermission))
            {
                return Results.Json(
                    new { error = "UNBLINDED_ACCESS_DENIED", details = "The caller may not run unblinded analyses." },
                    statusCode: 403);
            }

            if (string.IsNullOrWhiteSpace(req.DatabaseLockTokenId))
            {
                return Results.Json(
                    new { error = "UNBLINDED_ACCESS_DENIED", details = "Unblinded analysis requires an active database lock authorization token ID." },
                    statusCode: 403);
            }

            lockToken = await db.DatabaseLockAuthTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => (t.TokenId == req.DatabaseLockTokenId || t.Id == req.DatabaseLockTokenId) && t.StudyId == req.StudyId,
                    ct);

            if (lockToken == null || lockToken.IsRevoked || lockToken.ExpiresAt <= now)
            {
                return Results.Json(
                    new { error = "UNBLINDED_ACCESS_DENIED", details = "Provided database lock token is invalid, expired, or revoked." },
                    statusCode: 403);
            }

            var authorizedRoles = JsonSerializer.Deserialize<List<string>>(lockToken.AuthorizedRolesJson) ?? [];
            bool genuine = signer.Verify(
                SceProgramLockService.LockTokenPayload(lockToken.TokenId, lockToken.StudyId, lockToken.LockId, lockToken.IssuedTo, lockToken.ExpiresAt),
                lockToken.SignatureHash);
            if (!genuine
                || !string.Equals(lockToken.IssuedTo, userId, StringComparison.Ordinal)
                || !caller.Roles.Intersect(authorizedRoles, StringComparer.OrdinalIgnoreCase).Any())
            {
                return Results.Json(
                    new { error = "UNBLINDED_ACCESS_DENIED", details = "The database lock token was not issued to this caller or is not genuine." },
                    statusCode: 403);
            }
        }

        string lockfileContent = program.LockfileContent ?? "";
        var lockValidation = lockfileValidator.ValidateLockfile(program.LockType, lockfileContent);
        if (lockValidation.Status == "TAMPERED")
        {
            return Results.UnprocessableEntity(new { error = "LOCKFILE_TAMPERED", details = lockValidation.Details });
        }

        if (!TryBuildSandboxExecutionRequest(program, lockfileContent, req.SandboxConfig, out var sandboxReq, out var policyError))
        {
            return policyError;
        }

        var execResult = await driver.ExecuteInSandboxAsync(sandboxReq);
        var execEnd = clock.UtcNow;

        string runId = "run-" + Guid.NewGuid().ToString("N")[..12];
        string manifestId = "man-" + Guid.NewGuid().ToString("N")[..12];
        string snapshotVersionTag = "v" + req.SnapshotId;
        string snapshotDigest = SceProgramLockService.ComputeSha256(req.SnapshotId);
        string scriptCodeDigest = program.CodeHash;
        string lockfileDigest = program.LockDigest;

        var artifactDtos = execResult.Artifacts.Select(a => new OutputArtifactSchemaDto(
            ArtifactId: a.ArtifactId,
            Name: a.Name,
            ArtifactType: a.ArtifactType,
            FilePath: a.FilePath,
            MimeType: a.MimeType,
            SizeBytes: a.SizeBytes,
            Sha256Digest: a.Sha256Digest,
            ContentPreview: a.ContentPreview
        )).ToList();

        bool isSuccess = execResult.ExitCode == 0;
        string status = isSuccess ? "COMPLETED" : "FAILED";

        ElectronicSignatureBlockDto sigBlockDto;
        string manifestSeal;

        if (isSuccess)
        {
            string hmacSig = signer.Sign($"{userId}|{runId}|{execEnd:O}");
            sigBlockDto = new ElectronicSignatureBlockDto(
                SignerId: userId,
                SignerRole: userRole,
                SignedAt: execEnd.ToString("O"),
                SignatureMeaning: "Electronic signature confirming authoring, validation, and execution of statistical run",
                HmacSignature: hmacSig);

            string sealPayload = $"{runId}|{req.StudyId}|{program.Id}|{req.SnapshotId}|{scriptCodeDigest}|{lockfileDigest}|{execResult.ContainerImageDigest}|{execResult.OutputArtifactsDigest}|{userId}";
            manifestSeal = SceProgramLockService.ComputeSha256(sealPayload);
        }
        else
        {
            sigBlockDto = new ElectronicSignatureBlockDto(
                SignerId: "",
                SignerRole: "",
                SignedAt: "",
                SignatureMeaning: "NONE - Execution failed; electronic signature refused",
                HmacSignature: "");
            manifestSeal = "";
        }

        var manifestDto = new ExecutionRunManifestDto(
            ManifestId: manifestId,
            RunId: runId,
            StudyId: req.StudyId,
            ProgramId: program.Id,
            ProgramName: program.Name,
            ProgramVersion: program.Version,
            SnapshotId: req.SnapshotId,
            SnapshotVersionTag: snapshotVersionTag,
            SnapshotDigest: snapshotDigest,
            Language: program.Language,
            ScriptCodeDigest: scriptCodeDigest,
            LockfileDigest: lockfileDigest,
            ContainerImageDigest: execResult.ContainerImageDigest,
            ExecutionStart: now.ToString("O"),
            ExecutionEnd: execEnd.ToString("O"),
            DurationMs: execResult.DurationMs,
            ExitCode: execResult.ExitCode,
            Status: status,
            StdoutCapture: execResult.Stdout,
            StderrCapture: execResult.Stderr,
            OutputArtifacts: artifactDtos,
            OutputArtifactsDigest: execResult.OutputArtifactsDigest,
            IsUnblindedRun: req.IsUnblinded,
            DatabaseLockTokenId: lockToken?.TokenId ?? req.DatabaseLockTokenId,
            ExecutedBy: userId,
            ExecutedByRole: userRole,
            SignatureBlock: sigBlockDto,
            ManifestSha256Seal: manifestSeal);

        var record = new ExecutionRunRecord
        {
            RunId = runId,
            ManifestId = manifestId,
            StudyId = req.StudyId,
            ProgramId = program.Id,
            ProgramName = program.Name,
            ProgramVersion = program.Version,
            SnapshotId = req.SnapshotId,
            SnapshotVersionTag = snapshotVersionTag,
            SnapshotDigest = snapshotDigest,
            Language = program.Language,
            ScriptCodeDigest = scriptCodeDigest,
            LockfileDigest = lockfileDigest,
            ContainerImageDigest = execResult.ContainerImageDigest,
            ExecutionStart = now,
            ExecutionEnd = execEnd,
            DurationMs = execResult.DurationMs,
            ExitCode = execResult.ExitCode,
            Status = status,
            StdoutCapture = execResult.Stdout,
            StderrCapture = execResult.Stderr,
            OutputArtifactsJson = JsonSerializer.Serialize(artifactDtos),
            OutputArtifactsDigest = execResult.OutputArtifactsDigest,
            IsUnblindedRun = req.IsUnblinded,
            DatabaseLockTokenId = lockToken?.TokenId ?? req.DatabaseLockTokenId,
            ExecutedBy = userId,
            ExecutedByRole = userRole,
            SignatureBlockJson = JsonSerializer.Serialize(sigBlockDto),
            ManifestSha256Seal = manifestSeal,
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = req.ReasonForExecution
        };

        db.ExecutionRunRecords.Add(record);

        db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
        {
            Timestamp = now,
            UserId = userId,
            UserRole = userRole,
            Action = "EXECUTE_PROGRAM",
            StudyId = req.StudyId,
            ProgramId = program.Id,
            SnapshotId = req.SnapshotId,
            IsUnblinded = req.IsUnblinded,
            LockTokenId = lockToken?.TokenId,
            DetailsJson = JsonSerializer.Serialize(new { run_id = runId, exit_code = execResult.ExitCode, seal = manifestSeal }),
            Sha256Hash = SceProgramLockService.ComputeSha256($"{runId}|EXECUTE_PROGRAM|{now:O}"),
            CreatedAt = now
        });

        await unitOfWork.CommitAsync(ct);

        return Results.Ok(new ExecuteProgramResponseDto(manifestDto));
    }

    public async Task<IResult> ListRunsAsync(
        string studyId,
        string? programId,
        string? snapshotId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return Results.Ok(new ExecutionRunListResponseDto(studyId, 0, []));
        }

        var query = db.ExecutionRunRecords
            .AsNoTracking()
            .Where(r => r.StudyId == studyId);
        if (!PermissionMatrix.HasPermission(
            CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal."),
            SceEndpoints.ReadUnblindedPermission))
        {
            query = query.Where(r => !r.IsUnblindedRun);
        }

        if (!string.IsNullOrWhiteSpace(programId))
        {
            query = query.Where(r => r.ProgramId == programId || r.ProgramName == programId);
        }

        if (!string.IsNullOrWhiteSpace(snapshotId))
        {
            query = query.Where(r => r.SnapshotId == snapshotId);
        }

        var runs = await query.OrderByDescending(r => r.ExecutionStart).ToListAsync(ct);
        var manifests = runs.Select(ToManifestDto).ToList();

        return Results.Ok(new ExecutionRunListResponseDto(studyId, manifests.Count, manifests));
    }

    public async Task<IResult> GetRunAsync(string runId, HttpContext httpContext, CancellationToken ct)
    {
        var run = await db.ExecutionRunRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId || r.Id == runId, ct);

        if (!IsVisible(run, httpContext))
        {
            return Results.NotFound(new { error = $"Execution run '{runId}' not found." });
        }

        return Results.Ok(new ExecutionRunDetailResponseDto(ToManifestDto(run)));
    }

    public async Task<IResult> VerifyRunManifestAsync(string runId, HttpContext httpContext, CancellationToken ct)
    {
        var run = await db.ExecutionRunRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId || r.Id == runId, ct);

        if (!IsVisible(run, httpContext))
        {
            return Results.NotFound(new { error = $"Execution run '{runId}' not found." });
        }

        if (run.ExitCode != 0 || run.Status == "FAILED" || string.IsNullOrEmpty(run.ManifestSha256Seal))
        {
            return Results.Ok(new VerifyManifestResponseDto(new Dictionary<string, object>
            {
                ["is_valid"] = false,
                ["run_id"] = run.RunId,
                ["manifest_id"] = run.ManifestId ?? "",
                ["manifest_sha256_seal"] = run.ManifestSha256Seal,
                ["computed_sha256_seal"] = "",
                ["verification_timestamp"] = clock.UtcNow.ToString("O"),
                ["status"] = "SEAL_REFUSED_FAILED_RUN",
                ["reason"] = "Cryptographic seal was refused because the execution run failed."
            }));
        }

        string sealPayload = $"{run.RunId}|{run.StudyId}|{run.ProgramId}|{run.SnapshotId}|{run.ScriptCodeDigest}|{run.LockfileDigest}|{run.ContainerImageDigest}|{run.OutputArtifactsDigest}|{run.ExecutedBy}";
        string expectedSeal = SceProgramLockService.ComputeSha256(sealPayload);
        bool isValid = string.Equals(expectedSeal, run.ManifestSha256Seal, StringComparison.OrdinalIgnoreCase);

        var verificationResult = new Dictionary<string, object>
        {
            ["is_valid"] = isValid,
            ["run_id"] = run.RunId,
            ["manifest_id"] = run.ManifestId ?? "",
            ["manifest_sha256_seal"] = run.ManifestSha256Seal,
            ["computed_sha256_seal"] = expectedSeal,
            ["verification_timestamp"] = clock.UtcNow.ToString("O"),
            ["status"] = isValid ? "VERIFIED_SEAL_INTACT" : "SEAL_CORRUPTED"
        };

        return Results.Ok(new VerifyManifestResponseDto(verificationResult));
    }

    public async Task<IResult> GetRunArtifactAsync(string runId, string artifactId, HttpContext httpContext, CancellationToken ct)
    {
        var run = await db.ExecutionRunRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId || r.Id == runId, ct);

        if (!IsVisible(run, httpContext))
        {
            return Results.NotFound(new { error = $"Execution run '{runId}' not found." });
        }

        var artifacts = JsonSerializer.Deserialize<List<OutputArtifactSchemaDto>>(run.OutputArtifactsJson) ?? [];
        var artifact = artifacts.FirstOrDefault(a => a.ArtifactId == artifactId || a.Name == artifactId);

        if (artifact == null)
        {
            return Results.NotFound(new { error = $"Artifact '{artifactId}' not found in run '{runId}'." });
        }

        return Results.Ok(new Dictionary<string, object?>
        {
            ["artifact_id"] = artifact.ArtifactId,
            ["run_id"] = run.RunId,
            ["name"] = artifact.Name,
            ["artifact_type"] = artifact.ArtifactType,
            ["file_path"] = artifact.FilePath,
            ["mime_type"] = artifact.MimeType,
            ["size_bytes"] = artifact.SizeBytes,
            ["sha256_digest"] = artifact.Sha256Digest,
            ["content_preview"] = artifact.ContentPreview
        });
    }

    internal static bool TryBuildSandboxExecutionRequest(
        StatisticalProgram program,
        string lockfileContent,
        ExecutionSandboxConfigDto? sandboxConfig,
        out SandboxExecutionRequest sandboxReq,
        [NotNullWhen(false)] out IResult? errorResult)
    {
        var config = sandboxConfig ?? new ExecutionSandboxConfigDto();
        if (!config.NetworkIsolated || !config.FilesystemReadonlyRoot)
        {
            sandboxReq = null!;
            errorResult = Results.BadRequest(new
            {
                error = "SANDBOX_POLICY_VIOLATION",
                details = "Network isolation and a read-only root filesystem are mandatory for SCE sandbox execution."
            });
            return false;
        }

        if (!SceDockerEngineDriver.TryResolveApprovedImage(
            program.Language,
            config.DockerImageDigest,
            out string resolvedImage,
            out string imageError))
        {
            sandboxReq = null!;
            errorResult = Results.BadRequest(new
            {
                error = "UNAPPROVED_CONTAINER_IMAGE",
                details = imageError
            });
            return false;
        }

        sandboxReq = new SandboxExecutionRequest(
            Language: program.Language,
            SourceCode: program.SourceCode,
            LockfileContent: lockfileContent,
            LockType: program.LockType,
            MaxMemoryMb: SceDockerEngineDriver.ClampMemoryMb(config.MaxMemoryMb),
            TimeoutSeconds: SceDockerEngineDriver.ClampTimeoutSeconds(config.TimeoutSeconds),
            NetworkIsolated: true,
            FilesystemReadonlyRoot: true,
            ContainerImageOverride: resolvedImage,
            MaxOutputBytes: SceDockerEngineDriver.ClampMaxOutputBytes(config.MaxOutputBytes));
        errorResult = null;
        return true;
    }

    private static bool IsVisible([NotNullWhen(true)] ExecutionRunRecord? run, HttpContext httpContext) =>
        run != null
        && ClinicalScopeGuard.CanAccessStudy(httpContext, run.StudyId)
        && (!run.IsUnblindedRun
            || PermissionMatrix.HasPermission(
                CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal."),
                SceEndpoints.ReadUnblindedPermission));

    private static ExecutionRunManifestDto ToManifestDto(ExecutionRunRecord r)
    {
        var artifacts = JsonSerializer.Deserialize<List<OutputArtifactSchemaDto>>(r.OutputArtifactsJson) ?? [];
        var sigBlock = JsonSerializer.Deserialize<ElectronicSignatureBlockDto>(r.SignatureBlockJson)
            ?? new ElectronicSignatureBlockDto(
                SignerId: r.ExecutedBy,
                SignerRole: r.ExecutedByRole,
                SignedAt: r.ExecutionEnd.ToString("O"),
                SignatureMeaning: "Electronic signature confirming authoring, validation, and execution of statistical run",
                HmacSignature: "");

        return new ExecutionRunManifestDto(
            ManifestId: r.ManifestId ?? r.Id,
            RunId: r.RunId,
            StudyId: r.StudyId,
            ProgramId: r.ProgramId,
            ProgramName: r.ProgramName,
            ProgramVersion: r.ProgramVersion,
            SnapshotId: r.SnapshotId,
            SnapshotVersionTag: r.SnapshotVersionTag,
            SnapshotDigest: r.SnapshotDigest,
            Language: r.Language,
            ScriptCodeDigest: r.ScriptCodeDigest,
            LockfileDigest: r.LockfileDigest,
            ContainerImageDigest: r.ContainerImageDigest,
            ExecutionStart: r.ExecutionStart.ToString("O"),
            ExecutionEnd: r.ExecutionEnd.ToString("O"),
            DurationMs: r.DurationMs,
            ExitCode: r.ExitCode,
            Status: r.Status,
            StdoutCapture: r.StdoutCapture,
            StderrCapture: r.StderrCapture,
            OutputArtifacts: artifacts,
            OutputArtifactsDigest: r.OutputArtifactsDigest,
            IsUnblindedRun: r.IsUnblindedRun,
            DatabaseLockTokenId: r.DatabaseLockTokenId,
            ExecutedBy: r.ExecutedBy,
            ExecutedByRole: r.ExecutedByRole,
            SignatureBlock: sigBlock,
            ManifestSha256Seal: r.ManifestSha256Seal);
    }
}
