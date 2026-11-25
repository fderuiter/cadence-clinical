using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

public sealed class SceProgramAndLockService(
    InteropUnitOfWork uow,
    ILockfileValidatorService lockfileValidator,
    SceTokenSigner signer,
    IClock clock)
{
    public static IReadOnlyList<string> UnblindedExecutorRoles { get; } = PermissionMatrix.ROLE_PERMISSIONS
        .Where(r => r.Value.TryGetValue("sce_run", out var actions) && actions.Contains("execute_unblinded"))
        .Select(r => r.Key)
        .Order(StringComparer.Ordinal)
        .ToList();

    public async Task<IResult> ListProgramsAsync(string studyId, HttpContext httpContext, CancellationToken ct)
    {
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return Results.Ok(new ProgramListResponseDto(studyId, 0, []));
        }

        var programs = await uow.Db.StatisticalPrograms
            .AsNoTracking()
            .Where(p => p.StudyId == studyId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var dtos = programs.Select(ToProgramDto).ToList();
        return Results.Ok(new ProgramListResponseDto(studyId, dtos.Count, dtos));
    }

    public async Task<IResult> CreateProgramAsync(CreateProgramRequestDto req, HttpContext httpContext, CancellationToken ct)
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

        uow.Db.StatisticalPrograms.Add(entity);
        await uow.CommitAsync(ct).ConfigureAwait(false);

        var dto = ToProgramDto(entity);
        return Results.Created($"/api/v1/interop/sce/programs/{entity.Id}", new ProgramResponseDto(dto));
    }

    public async Task<IResult> GetProgramByIdAsync(string programId, HttpContext httpContext, CancellationToken ct)
    {
        var program = await uow.Db.StatisticalPrograms
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == programId || p.Name == programId, ct)
            .ConfigureAwait(false);

        if (program == null || !ClinicalScopeGuard.CanAccessStudy(httpContext, program.StudyId))
        {
            return Results.NotFound(new { error = $"Statistical program '{programId}' not found." });
        }

        return Results.Ok(new ProgramResponseDto(ToProgramDto(program)));
    }

    public IResult ValidateLockfile(ValidateLockfileRequestDto req)
    {
        var result = lockfileValidator.ValidateLockfile(req.LockType, req.Content, req.ExpectedDigest, req.StrictQualification);
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
    }

    public async Task<IResult> ListLockTokensAsync(string studyId, HttpContext httpContext, CancellationToken ct)
    {
        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, studyId))
        {
            return Results.Ok(new LockTokenListResponseDto(studyId, 0, []));
        }

        var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
        var now = clock.UtcNow;
        var query = uow.Db.DatabaseLockAuthTokens
            .AsNoTracking()
            .Where(t => t.StudyId == studyId && !t.IsRevoked && t.ExpiresAt > now);

        if (!PermissionMatrix.HasPermission(caller, "sce_lock_token:issue"))
        {
            query = query.Where(t => t.IssuedTo == caller.UserId);
        }

        var tokens = await query.OrderByDescending(t => t.IssuedAt).ToListAsync(ct).ConfigureAwait(false);
        var dtos = tokens.Select(ToLockTokenDto).ToList();
        return Results.Ok(new LockTokenListResponseDto(studyId, dtos.Count, dtos));
    }

    public async Task<IResult> IssueLockTokenAsync(IssueLockTokenRequestDto req, HttpContext httpContext, CancellationToken ct)
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
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(now.AddHours(req.ExpiresInHours <= 0 ? 24 : req.ExpiresInHours).ToUnixTimeSeconds());
        string tokenId = "DBLOCK-AUTH-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
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

        uow.Db.DatabaseLockAuthTokens.Add(tokenEntity);

        uow.Db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
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

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Created("/api/v1/interop/sce/lock-tokens", new LockTokenResponseDto(ToLockTokenDto(tokenEntity)));
    }

    public async Task<IResult> RevokeLockTokenAsync(string tokenId, string? reason, HttpContext httpContext, CancellationToken ct)
    {
        var caller = CallerScope.RequiredPrincipal(httpContext, "SCE routes require a verified gateway principal.");
        string userId = caller.UserId;
        string userRole = CallerScope.Roles(httpContext);
        string revocationReason = string.IsNullOrWhiteSpace(reason) ? "Revocation" : reason;

        var token = await uow.Db.DatabaseLockAuthTokens
            .FirstOrDefaultAsync(t => t.TokenId == tokenId || t.Id == tokenId, ct)
            .ConfigureAwait(false);

        if (token == null)
        {
            return Results.NotFound(new { error = $"Database lock auth token '{tokenId}' not found." });
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

        uow.Db.BiostatExportAuditLogs.Add(new BiostatExportAuditLog
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

        await uow.CommitAsync(ct).ConfigureAwait(false);

        return Results.Ok(new Dictionary<string, object>
        {
            ["status"] = "REVOKED",
            ["token_id"] = token.TokenId,
            ["revoked_at"] = now.ToString("O"),
            ["reason"] = revocationReason
        });
    }

    internal static StatisticalProgramDto ToProgramDto(StatisticalProgram p) =>
        new(
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

    internal static DatabaseLockAuthTokenDto ToLockTokenDto(DatabaseLockAuthToken t)
    {
        var roles = JsonSerializer.Deserialize<List<string>>(t.AuthorizedRolesJson) ?? [];
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

    internal static IResult OutsideStudy() =>
        Results.Json(new { error = "OUTSIDE_STUDY_SCOPE", details = "You are not assigned to this study." }, statusCode: StatusCodes.Status403Forbidden);

    internal static string LockTokenPayload(string tokenId, string studyId, string lockId, string issuedTo, DateTimeOffset expiresAt) =>
        $"{tokenId}|{studyId}|{lockId}|{issuedTo}|{expiresAt.ToUnixTimeSeconds()}";

    internal static string ComputeSha256(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
