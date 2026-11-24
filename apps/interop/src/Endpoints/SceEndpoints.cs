using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Application.Services;
using Cadence.Apps.Interop.Domain;
using Cadence.Apps.Interop.Infrastructure.Persistence;
using Cadence.Apps.Interop.Infrastructure.Security;
using Cadence.Kernel.Time;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Endpoints;

#region DTOs

public record CreateProgramRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("language")] string Language = "PYTHON",
    [property: JsonPropertyName("version")] string Version = "1.0.0",
    [property: JsonPropertyName("source_code")] string SourceCode = "",
    [property: JsonPropertyName("lockfile_content")] string? LockfileContent = null,
    [property: JsonPropertyName("lock_type")] string LockType = "UV_LOCK",
    [property: JsonPropertyName("target_domain")] string? TargetDomain = null,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange = "Initial program authoring"
);

public record StatisticalProgramDto(
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("source_code")] string SourceCode,
    [property: JsonPropertyName("code_hash")] string CodeHash,
    [property: JsonPropertyName("lockfile_content")] string? LockfileContent,
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("lock_digest")] string LockDigest,
    [property: JsonPropertyName("target_domain")] string? TargetDomain,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("reason_for_change")] string ReasonForChange
);

public record ProgramResponseDto(
    [property: JsonPropertyName("program")] StatisticalProgramDto Program
);

public record ProgramListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("programs")] List<StatisticalProgramDto> Programs
);

public record ValidateLockfileRequestDto(
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("expected_digest")] string? ExpectedDigest = null,
    [property: JsonPropertyName("strict_qualification")] bool StrictQualification = false
);

public record PackageEntrySchemaDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("checksum")] string? Checksum = null,
    [property: JsonPropertyName("repository")] string? Repository = null,
    [property: JsonPropertyName("is_qualified")] bool IsQualified = true
);

public record LockfileValidationResultSchemaDto(
    [property: JsonPropertyName("lock_type")] string LockType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("lock_digest")] string LockDigest,
    [property: JsonPropertyName("package_count")] int PackageCount,
    [property: JsonPropertyName("packages")] List<PackageEntrySchemaDto> Packages,
    [property: JsonPropertyName("is_qualified")] bool IsQualified,
    [property: JsonPropertyName("unqualified_packages")] List<string> UnqualifiedPackages,
    [property: JsonPropertyName("details")] string Details
);

public record ValidateLockfileResponseDto(
    [property: JsonPropertyName("result")] LockfileValidationResultSchemaDto Result
);

public record IssueLockTokenRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("lock_id")] string LockId,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("issued_to")] string IssuedTo,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("expires_in_hours")] double ExpiresInHours = 24.0
);

public record DatabaseLockAuthTokenDto(
    [property: JsonPropertyName("token_id")] string TokenId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("lock_id")] string LockId,
    [property: JsonPropertyName("milestone")] string Milestone,
    [property: JsonPropertyName("issued_to")] string IssuedTo,
    [property: JsonPropertyName("authorized_roles")] List<string> AuthorizedRoles,
    [property: JsonPropertyName("issued_at")] string IssuedAt,
    [property: JsonPropertyName("expires_at")] string ExpiresAt,
    [property: JsonPropertyName("signature_hash")] string SignatureHash,
    [property: JsonPropertyName("is_revoked")] bool IsRevoked
);

public record LockTokenResponseDto(
    [property: JsonPropertyName("token")] DatabaseLockAuthTokenDto Token
);

public record LockTokenListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("tokens")] List<DatabaseLockAuthTokenDto> Tokens
);

public record ExecutionSandboxConfigDto(
    [property: JsonPropertyName("max_memory_mb")] int MaxMemoryMb = 2048,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 300,
    [property: JsonPropertyName("network_isolated")] bool NetworkIsolated = true,
    [property: JsonPropertyName("filesystem_readonly_root")] bool FilesystemReadonlyRoot = true,
    [property: JsonPropertyName("max_output_bytes")] long MaxOutputBytes = 10485760,
    [property: JsonPropertyName("docker_image_digest")] string DockerImageDigest = "sha256:cadence-sce-r4.4-py3.14-validated-base-2026",
    [property: JsonPropertyName("python_version")] string PythonVersion = "3.14.0",
    [property: JsonPropertyName("r_version")] string RVersion = "4.4.2"
);

public record ExecuteProgramRequestDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("snapshot_id")] string SnapshotId,
    [property: JsonPropertyName("is_unblinded")] bool IsUnblinded = false,
    [property: JsonPropertyName("database_lock_token_id")] string? DatabaseLockTokenId = null,
    [property: JsonPropertyName("sandbox_config")] ExecutionSandboxConfigDto? SandboxConfig = null,
    [property: JsonPropertyName("reason_for_execution")] string ReasonForExecution = "Statistical analysis execution run"
);

public record OutputArtifactSchemaDto(
    [property: JsonPropertyName("artifact_id")] string ArtifactId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artifact_type")] string ArtifactType,
    [property: JsonPropertyName("file_path")] string FilePath,
    [property: JsonPropertyName("mime_type")] string MimeType,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("sha256_digest")] string Sha256Digest,
    [property: JsonPropertyName("content_preview")] string? ContentPreview = null
);

public record ElectronicSignatureBlockDto(
    [property: JsonPropertyName("signer_id")] string SignerId,
    [property: JsonPropertyName("signer_role")] string SignerRole,
    [property: JsonPropertyName("signed_at")] string SignedAt,
    [property: JsonPropertyName("signature_meaning")] string SignatureMeaning,
    [property: JsonPropertyName("hmac_signature")] string HmacSignature
);

public record ExecutionRunManifestDto(
    [property: JsonPropertyName("manifest_id")] string ManifestId,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("program_id")] string ProgramId,
    [property: JsonPropertyName("program_name")] string ProgramName,
    [property: JsonPropertyName("program_version")] string ProgramVersion,
    [property: JsonPropertyName("snapshot_id")] string SnapshotId,
    [property: JsonPropertyName("snapshot_version_tag")] string SnapshotVersionTag,
    [property: JsonPropertyName("snapshot_digest")] string SnapshotDigest,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("script_code_digest")] string ScriptCodeDigest,
    [property: JsonPropertyName("lockfile_digest")] string LockfileDigest,
    [property: JsonPropertyName("container_image_digest")] string ContainerImageDigest,
    [property: JsonPropertyName("execution_start")] string ExecutionStart,
    [property: JsonPropertyName("execution_end")] string ExecutionEnd,
    [property: JsonPropertyName("duration_ms")] double DurationMs,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stdout_capture")] string StdoutCapture,
    [property: JsonPropertyName("stderr_capture")] string StderrCapture,
    [property: JsonPropertyName("output_artifacts")] List<OutputArtifactSchemaDto> OutputArtifacts,
    [property: JsonPropertyName("output_artifacts_digest")] string OutputArtifactsDigest,
    [property: JsonPropertyName("is_unblinded_run")] bool IsUnblindedRun,
    [property: JsonPropertyName("database_lock_token_id")] string? DatabaseLockTokenId,
    [property: JsonPropertyName("executed_by")] string ExecutedBy,
    [property: JsonPropertyName("executed_by_role")] string ExecutedByRole,
    [property: JsonPropertyName("signature_block")] ElectronicSignatureBlockDto SignatureBlock,
    [property: JsonPropertyName("manifest_sha256_seal")] string ManifestSha256Seal
);

public record ExecuteProgramResponseDto(
    [property: JsonPropertyName("manifest")] ExecutionRunManifestDto Manifest
);

public record ExecutionRunListResponseDto(
    [property: JsonPropertyName("study_id")] string StudyId,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("runs")] List<ExecutionRunManifestDto> Runs
);

public record ExecutionRunDetailResponseDto(
    [property: JsonPropertyName("run")] ExecutionRunManifestDto Run
);

public record VerifyManifestResponseDto(
    [property: JsonPropertyName("result")] Dictionary<string, object> Result
);

#endregion

/// <summary>
/// The statistical computing environment. Every route needs an <c>sce_*</c> permission from the permission matrix,
/// and identity and roles come only from the verified gateway principal (#5382). An unblinded run needs
/// <c>sce_run:execute_unblinded</c> and a database-lock token issued to the caller and signed with
/// <c>CADENCE_SCE_SIGNING_KEY</c>; unblinded runs are visible only with <c>sce_run:read_unblinded</c>. Every route that
/// names a study also requires the caller to be assigned to it in the signed <c>study_ids</c> claim (ADR-2516, #5402): a
/// list outside the caller's studies is empty, one record outside them is not found, and a write outside them is
/// refused with <c>403</c> and writes nothing.
/// @req:PRD-CDR-006
/// </summary>
public static class SceEndpoints
{
    /// <summary>Permission to run an unblinded analysis with a lock token.</summary>
    public const string ExecuteUnblindedPermission = "sce_run:execute_unblinded";

    /// <summary>Permission to see unblinded runs and their artifacts.</summary>
    public const string ReadUnblindedPermission = "sce_run:read_unblinded";

    /// <summary>The roles a lock token authorizes: exactly those the matrix lets run unblinded analyses.</summary>
    public static IReadOnlyList<string> UnblindedExecutorRoles { get; } = PermissionMatrix.ROLE_PERMISSIONS
        .Where(r => r.Value.TryGetValue("sce_run", out var actions) && actions.Contains("execute_unblinded"))
        .Select(r => r.Key)
        .Order(StringComparer.Ordinal)
        .ToList();

    public static IEndpointRouteBuilder MapSceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/interop/sce");

        // 1. List statistical programs
        group.MapGet("/programs", async (string study_id, InteropDbContext db, HttpContext httpContext) =>
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, study_id))
            {
                return Results.Ok(new ProgramListResponseDto(study_id, 0, []));
            }

            var programs = await db.StatisticalPrograms
                .AsNoTracking()
                .Where(p => p.StudyId == study_id)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var dtos = programs.Select(ToProgramDto).ToList();
            return Results.Ok(new ProgramListResponseDto(study_id, dtos.Count, dtos));
        }).RequirePermission("sce_program:read").RequireCallerStudyScope().Produces<ProgramListResponseDto>(StatusCodes.Status200OK);

        // 2. Create / register statistical program
        group.MapPost("/programs", async (CreateProgramRequestDto req, InteropDbContext db, IClock clock, HttpContext httpContext) =>
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, req.StudyId))
            {
                return OutsideStudy();
            }

            string userId = CallerScope.RequiredUserId(httpContext, "SCE routes require a verified gateway principal.");
            string codeHash = ComputeSha256(req.SourceCode ?? "");
            string lockDigest = ComputeSha256(req.LockfileContent ?? "");

            var entity = new StatisticalProgram
            {
                StudyId = req.StudyId,
                Name = req.Name,
                Title = req.Title,
                Description = req.Description ?? "",
                Language = req.Language.ToUpperInvariant(),
                Version = req.Version ?? "1.0.0",
                SourceCode = req.SourceCode ?? "",
                CodeHash = codeHash,
                LockfileContent = req.LockfileContent,
                LockType = req.LockType.ToUpperInvariant(),
                LockDigest = lockDigest,
                TargetDomain = req.TargetDomain,
                CreatedAt = clock.UtcNow,
                CreatedBy = userId,
                ReasonForChange = req.ReasonForChange ?? "Initial program authoring"
            };

            db.StatisticalPrograms.Add(entity);
            await db.SaveChangesAsync();

            var dto = ToProgramDto(entity);
            return Results.Created($"/api/v1/interop/sce/programs/{entity.Id}", new ProgramResponseDto(dto));
        }).RequirePermission("sce_program:create").RequireCallerStudyScope().Produces<ProgramResponseDto>(StatusCodes.Status201Created);

        // 3. Get statistical program by ID
        group.MapGet("/programs/{program_id}", async (string program_id, InteropDbContext db, HttpContext httpContext) =>
        {
            var program = await db.StatisticalPrograms
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == program_id || p.Name == program_id);

            if (program == null || !ClinicalScopeGuard.CanAccessStudy(httpContext, program.StudyId))
            {
                return Results.NotFound(new { error = $"Statistical program '{program_id}' not found." });
            }

            return Results.Ok(new ProgramResponseDto(ToProgramDto(program)));
        }).RequirePermission("sce_program:read").RequireCallerStudyScope().Produces<ProgramResponseDto>(StatusCodes.Status200OK);

        // 4. Validate package lockfile
        group.MapPost("/validate-lockfile", (ValidateLockfileRequestDto req, ILockfileValidatorService validator) =>
        {
            var result = validator.ValidateLockfile(req.LockType, req.Content, req.ExpectedDigest, req.StrictQualification);
            var pkgs = result.Packages.Select(p => new PackageEntrySchemaDto(p.Name, p.Version, p.Checksum, p.Repository, p.IsQualified)).ToList();

            var schemaResult = new LockfileValidationResultSchemaDto(
                LockType: result.LockType,
                Status: result.Status,
                LockDigest: result.LockDigest,
                PackageCount: result.PackageCount,
                Packages: pkgs,
                IsQualified: result.IsQualified,
                UnqualifiedPackages: result.UnqualifiedPackages,
                Details: result.Details
            );

            return Results.Ok(new ValidateLockfileResponseDto(schemaResult));
        }).RequirePermission("sce_program:read").RequireTenantScope().Produces<ValidateLockfileResponseDto>(StatusCodes.Status200OK);

        // 5. List active database lock tokens
        group.MapGet("/lock-tokens", async (string study_id, InteropDbContext db, IClock clock, HttpContext httpContext) =>
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, study_id))
            {
                return Results.Ok(new LockTokenListResponseDto(study_id, 0, []));
            }

            var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
            var now = clock.UtcNow;
            var query = db.DatabaseLockAuthTokens
                .AsNoTracking()
                .Where(t => t.StudyId == study_id && !t.IsRevoked && t.ExpiresAt > now);
            // Only an issuer sees every token; a recipient sees the tokens issued to them.
            if (!PermissionMatrix.HasPermission(caller, "sce_lock_token:issue"))
            {
                query = query.Where(t => t.IssuedTo == caller.UserId);
            }

            var tokens = await query.OrderByDescending(t => t.IssuedAt).ToListAsync();

            var dtos = tokens.Select(ToLockTokenDto).ToList();
            return Results.Ok(new LockTokenListResponseDto(study_id, dtos.Count, dtos));
        }).RequirePermission("sce_lock_token:read").RequireCallerStudyScope().Produces<LockTokenListResponseDto>(StatusCodes.Status200OK);

        // 6. Issue database lock token
        group.MapPost("/lock-tokens", async (IssueLockTokenRequestDto req, InteropDbContext db, IClock clock, SceTokenSigner signer, HttpContext httpContext) =>
        {
            var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
            string userId = caller.UserId;
            string userRole = CallerScope.Roles(httpContext);
            if (string.IsNullOrWhiteSpace(req.StudyId) || string.IsNullOrWhiteSpace(req.LockId) || string.IsNullOrWhiteSpace(req.IssuedTo))
            {
                return Results.BadRequest(new { error = "study_id, lock_id and issued_to are required." });
            }

            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, req.StudyId))
            {
                return OutsideStudy();
            }

            var now = clock.UtcNow;
            // Whole seconds, so the signed expiry survives the database's microsecond precision unchanged.
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(now.AddHours(req.ExpiresInHours <= 0 ? 24 : req.ExpiresInHours).ToUnixTimeSeconds());
            string tokenId = "DBLOCK-AUTH-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            // The authorized roles are fixed by the permission matrix, never chosen by the request.
            var roles = UnblindedExecutorRoles.ToList();

            string sigHash = signer.Sign(LockTokenPayload(tokenId, req.StudyId, req.LockId, req.IssuedTo, expiresAt));

            var tokenEntity = new DatabaseLockAuthToken
            {
                TokenId = tokenId,
                StudyId = req.StudyId,
                LockId = req.LockId,
                Milestone = req.Milestone,
                IssuedTo = req.IssuedTo,
                AuthorizedRolesJson = JsonSerializer.Serialize(roles),
                IssuedAt = now,
                ExpiresAt = expiresAt,
                SignatureHash = sigHash,
                IsRevoked = false,
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = req.Reason
            };

            db.DatabaseLockAuthTokens.Add(tokenEntity);

            // Immutable audit log
            db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
            {
                Timestamp = now,
                UserId = userId,
                UserRole = userRole,
                Action = "ISSUE_LOCK_TOKEN",
                StudyId = req.StudyId,
                IsUnblinded = true,
                LockTokenId = tokenId,
                DetailsJson = JsonSerializer.Serialize(new { lock_id = req.LockId, milestone = req.Milestone, reason = req.Reason }),
                Sha256Hash = ComputeSha256($"{tokenId}|ISSUE_LOCK_TOKEN|{now:O}"),
                CreatedAt = now
            });

            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/interop/sce/lock-tokens", new LockTokenResponseDto(ToLockTokenDto(tokenEntity)));
        }).RequirePermission("sce_lock_token:issue").RequireCallerStudyScope().Produces<LockTokenResponseDto>(StatusCodes.Status201Created);

        // 7. Revoke database lock token
        group.MapPost("/lock-tokens/{token_id}/revoke", async (string token_id, string? reason, InteropDbContext db, IClock clock, HttpContext httpContext) =>
        {
            var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
            string userId = caller.UserId;
            string userRole = CallerScope.Roles(httpContext);
            string revocationReason = string.IsNullOrWhiteSpace(reason) ? "Revocation" : reason;

            var token = await db.DatabaseLockAuthTokens
                .FirstOrDefaultAsync(t => t.TokenId == token_id || t.Id == token_id);

            if (token == null)
            {
                return Results.NotFound(new { error = $"Database lock auth token '{token_id}' not found." });
            }

            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, token.StudyId))
            {
                return OutsideStudy();
            }

            var now = clock.UtcNow;
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedBy = userId;
            token.RevocationReason = revocationReason;

            db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
            {
                Timestamp = now,
                UserId = userId,
                UserRole = userRole,
                Action = "REVOKE_LOCK_TOKEN",
                StudyId = token.StudyId,
                IsUnblinded = true,
                LockTokenId = token.TokenId,
                DetailsJson = JsonSerializer.Serialize(new { revocation_reason = revocationReason }),
                Sha256Hash = ComputeSha256($"{token.TokenId}|REVOKE_LOCK_TOKEN|{now:O}"),
                CreatedAt = now
            });

            await db.SaveChangesAsync();

            return Results.Ok(new Dictionary<string, object>
            {
                ["status"] = "REVOKED",
                ["token_id"] = token.TokenId,
                ["revoked_at"] = now.ToString("O"),
                ["reason"] = revocationReason
            });
        }).RequirePermission("sce_lock_token:revoke").RequireCallerStudyScope().Produces<Dictionary<string, object>>(StatusCodes.Status200OK);

        // 8. Execute statistical program in sandbox
        group.MapPost("/execute", async (ExecuteProgramRequestDto req, InteropDbContext db, ISceDockerEngineDriver driver, ILockfileValidatorService lockfileValidator, IClock clock, SceTokenSigner signer, HttpContext httpContext) =>
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, req.StudyId))
            {
                return OutsideStudy();
            }

            var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
            string userId = caller.UserId;
            string userRole = CallerScope.Roles(httpContext);
            var now = clock.UtcNow;

            // The program must belong to the study the run is recorded under, so a caller cannot run another study's
            // program under a study they are assigned to.
            var program = await db.StatisticalPrograms
                .AsNoTracking()
                .FirstOrDefaultAsync(p => (p.Id == req.ProgramId || p.Name == req.ProgramId) && p.StudyId == req.StudyId);

            if (program == null)
            {
                return Results.NotFound(new { error = $"Statistical program '{req.ProgramId}' not found." });
            }

            // Role-gated database lock token validation if unblinded access requested
            DatabaseLockAuthToken? lockToken = null;
            if (req.IsUnblinded)
            {
                if (!PermissionMatrix.HasPermission(caller, ExecuteUnblindedPermission))
                {
                    return Results.Json(new { error = "UNBLINDED_ACCESS_DENIED", details = "The caller may not run unblinded analyses." }, statusCode: 403);
                }

                if (string.IsNullOrWhiteSpace(req.DatabaseLockTokenId))
                {
                    return Results.Json(new { error = "UNBLINDED_ACCESS_DENIED", details = "Unblinded analysis requires an active database lock authorization token ID." }, statusCode: 403);
                }

                lockToken = await db.DatabaseLockAuthTokens
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => (t.TokenId == req.DatabaseLockTokenId || t.Id == req.DatabaseLockTokenId) && t.StudyId == req.StudyId);

                if (lockToken == null || lockToken.IsRevoked || lockToken.ExpiresAt <= now)
                {
                    return Results.Json(new { error = "UNBLINDED_ACCESS_DENIED", details = "Provided database lock token is invalid, expired, or revoked." }, statusCode: 403);
                }

                // The token must carry this key's signature over its claims, be issued to this caller, and authorize
                // one of the caller's verified roles.
                var authorizedRoles = JsonSerializer.Deserialize<List<string>>(lockToken.AuthorizedRolesJson) ?? [];
                bool genuine = signer.Verify(
                    LockTokenPayload(lockToken.TokenId, lockToken.StudyId, lockToken.LockId, lockToken.IssuedTo, lockToken.ExpiresAt),
                    lockToken.SignatureHash);
                if (!genuine
                    || !string.Equals(lockToken.IssuedTo, userId, StringComparison.Ordinal)
                    || !caller.Roles.Intersect(authorizedRoles, StringComparer.OrdinalIgnoreCase).Any())
                {
                    return Results.Json(new { error = "UNBLINDED_ACCESS_DENIED", details = "The database lock token was not issued to this caller or is not genuine." }, statusCode: 403);
                }
            }

            // Lockfile validation check
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
            string snapshotDigest = ComputeSha256(req.SnapshotId);
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
                    HmacSignature: hmacSig
                );

                string sealPayload = $"{runId}|{req.StudyId}|{program.Id}|{req.SnapshotId}|{scriptCodeDigest}|{lockfileDigest}|{execResult.ContainerImageDigest}|{execResult.OutputArtifactsDigest}|{userId}";
                manifestSeal = ComputeSha256(sealPayload);
            }
            else
            {
                sigBlockDto = new ElectronicSignatureBlockDto(
                    SignerId: "",
                    SignerRole: "",
                    SignedAt: "",
                    SignatureMeaning: "NONE - Execution failed; electronic signature refused",
                    HmacSignature: ""
                );
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
                ManifestSha256Seal: manifestSeal
            );

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
                Sha256Hash = ComputeSha256($"{runId}|EXECUTE_PROGRAM|{now:O}"),
                CreatedAt = now
            });

            await db.SaveChangesAsync();

            return Results.Ok(new ExecuteProgramResponseDto(manifestDto));
        }).RequirePermission("sce_run:execute").RequireCallerStudyScope().Produces<ExecuteProgramResponseDto>(StatusCodes.Status200OK);

        // 9. List historical execution runs for a study
        group.MapGet("/runs", async (string study_id, string? program_id, string? snapshot_id, InteropDbContext db, HttpContext httpContext) =>
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, study_id))
            {
                return Results.Ok(new ExecutionRunListResponseDto(study_id, 0, []));
            }

            var query = db.ExecutionRunRecords
                .AsNoTracking()
                .Where(r => r.StudyId == study_id);
            if (!PermissionMatrix.HasPermission(CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal."), ReadUnblindedPermission))
            {
                query = query.Where(r => !r.IsUnblindedRun);
            }

            if (!string.IsNullOrWhiteSpace(program_id))
            {
                query = query.Where(r => r.ProgramId == program_id || r.ProgramName == program_id);
            }
            if (!string.IsNullOrWhiteSpace(snapshot_id))
            {
                query = query.Where(r => r.SnapshotId == snapshot_id);
            }

            var runs = await query.OrderByDescending(r => r.ExecutionStart).ToListAsync();
            var manifests = runs.Select(ToManifestDto).ToList();

            return Results.Ok(new ExecutionRunListResponseDto(study_id, manifests.Count, manifests));
        }).RequirePermission("sce_run:read").RequireCallerStudyScope().Produces<ExecutionRunListResponseDto>(StatusCodes.Status200OK);

        // 10. Get execution run by ID
        group.MapGet("/runs/{run_id}", async (string run_id, InteropDbContext db, HttpContext httpContext) =>
        {
            var run = await db.ExecutionRunRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RunId == run_id || r.Id == run_id);

            // A run outside the caller's studies, or an unblinded run they may not read, does not exist for them.
            if (!IsVisible(run, httpContext))
            {
                return Results.NotFound(new { error = $"Execution run '{run_id}' not found." });
            }

            return Results.Ok(new ExecutionRunDetailResponseDto(ToManifestDto(run)));
        }).RequirePermission("sce_run:read").RequireCallerStudyScope().Produces<ExecutionRunDetailResponseDto>(StatusCodes.Status200OK);

        // 11. Verify execution run manifest seal
        group.MapPost("/runs/{run_id}/verify", async (string run_id, InteropDbContext db, IClock clock, HttpContext httpContext) =>
        {
            var run = await db.ExecutionRunRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RunId == run_id || r.Id == run_id);

            // A run outside the caller's studies, or an unblinded run they may not read, does not exist for them.
            if (!IsVisible(run, httpContext))
            {
                return Results.NotFound(new { error = $"Execution run '{run_id}' not found." });
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
            string expectedSeal = ComputeSha256(sealPayload);
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
        }).RequirePermission("sce_run:read").RequireCallerStudyScope().Produces<VerifyManifestResponseDto>(StatusCodes.Status200OK);

        // 12. Get generated artifact content & preview
        group.MapGet("/runs/{run_id}/artifacts/{artifact_id}", async (string run_id, string artifact_id, InteropDbContext db, HttpContext httpContext) =>
        {
            var run = await db.ExecutionRunRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RunId == run_id || r.Id == run_id);

            // A run outside the caller's studies, or an unblinded run they may not read, does not exist for them.
            if (!IsVisible(run, httpContext))
            {
                return Results.NotFound(new { error = $"Execution run '{run_id}' not found." });
            }

            var artifacts = JsonSerializer.Deserialize<List<OutputArtifactSchemaDto>>(run.OutputArtifactsJson) ?? new();
            var artifact = artifacts.FirstOrDefault(a => a.ArtifactId == artifact_id || a.Name == artifact_id);

            if (artifact == null)
            {
                return Results.NotFound(new { error = $"Artifact '{artifact_id}' not found in run '{run_id}'." });
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
        }).RequirePermission("sce_run:read").RequireCallerStudyScope().Produces<Dictionary<string, object?>>(StatusCodes.Status200OK);

        return routes;
    }

    #region Helpers

    private static StatisticalProgramDto ToProgramDto(StatisticalProgram p) =>
        new StatisticalProgramDto(
            ProgramId: p.Id,
            StudyId: p.StudyId,
            Name: p.Name,
            Title: p.Title,
            Description: p.Description,
            Language: p.Language,
            Version: p.Version,
            SourceCode: p.SourceCode,
            CodeHash: p.CodeHash,
            LockfileContent: p.LockfileContent,
            LockType: p.LockType,
            LockDigest: p.LockDigest,
            TargetDomain: p.TargetDomain,
            CreatedBy: p.CreatedBy,
            CreatedAt: p.CreatedAt.ToString("O"),
            ReasonForChange: p.ReasonForChange
        );

    private static DatabaseLockAuthTokenDto ToLockTokenDto(DatabaseLockAuthToken t)
    {
        var roles = JsonSerializer.Deserialize<List<string>>(t.AuthorizedRolesJson) ?? new();
        return new DatabaseLockAuthTokenDto(
            TokenId: t.TokenId,
            StudyId: t.StudyId,
            LockId: t.LockId,
            Milestone: t.Milestone,
            IssuedTo: t.IssuedTo,
            AuthorizedRoles: roles,
            IssuedAt: t.IssuedAt.ToString("O"),
            ExpiresAt: t.ExpiresAt.ToString("O"),
            SignatureHash: t.SignatureHash,
            IsRevoked: t.IsRevoked
        );
    }

    private static ExecutionRunManifestDto ToManifestDto(ExecutionRunRecord r)
    {
        var artifacts = JsonSerializer.Deserialize<List<OutputArtifactSchemaDto>>(r.OutputArtifactsJson) ?? new();
        var sigBlock = JsonSerializer.Deserialize<ElectronicSignatureBlockDto>(r.SignatureBlockJson) ?? new ElectronicSignatureBlockDto(
            SignerId: r.ExecutedBy,
            SignerRole: r.ExecutedByRole,
            SignedAt: r.ExecutionEnd.ToString("O"),
            SignatureMeaning: "Electronic signature confirming authoring, validation, and execution of statistical run",
            HmacSignature: ""
        );

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
            ManifestSha256Seal: r.ManifestSha256Seal
        );
    }

    private static bool IsVisible([NotNullWhen(true)] ExecutionRunRecord? run, HttpContext httpContext) =>
        run != null
        && ClinicalScopeGuard.CanAccessStudy(httpContext, run.StudyId)
        && (!run.IsUnblindedRun
            || PermissionMatrix.HasPermission(
                CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal."),
                ReadUnblindedPermission));

    private static IResult OutsideStudy() =>
        Results.Json(new { error = "OUTSIDE_STUDY_SCOPE", details = "You are not assigned to this study." }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>The signed claims of a lock token. The expiry is in whole Unix seconds.</summary>
    private static string LockTokenPayload(string tokenId, string studyId, string lockId, string issuedTo, DateTimeOffset expiresAt) =>
        $"{tokenId}|{studyId}|{lockId}|{issuedTo}|{expiresAt.ToUnixTimeSeconds()}";

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
            MaxOutputBytes: SceDockerEngineDriver.ClampMaxOutputBytes(config.MaxOutputBytes)
        );
        errorResult = null;
        return true;
    }

    private static string ComputeSha256(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    #endregion
}
