using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Endpoints;
using Cadence.Apps.Knowledge.Infrastructure;
using Cadence.Apps.Knowledge.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Database.Tenancy;
using Cadence.Kernel;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Knowledge.Application;

public interface ITrainingCurriculumService
{
    Task<IResult> ListCurriculaAsync(
        string? studyId,
        string? protocolVersion,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> CreateCurriculumAsync(
        CreateTrainingCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetCurriculumAsync(
        string curriculumId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> ListStudyStaffAsync(
        string studyId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> ListAssignmentsAsync(
        string? studyId,
        string? siteId,
        string? userId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> GetAssignmentDetailAsync(
        string assignmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken);

    Task<IResult> CompleteAssignmentAsync(
        string assignmentId,
        CompleteTrainingAssignmentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken);
}

public sealed class TrainingCurriculumService(
    KnowledgeDbContext dbContext,
    IKnowledgeUnitOfWork unitOfWork,
    IKnowledgeKernelTrustClient kernelTrustClient,
    IEtmfTrainingIngestClient etmfClient,
    IClock clock) : ITrainingCurriculumService
{
    public const int MaxBatchLimit = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IResult> ListCurriculaAsync(
        string? studyId,
        string? protocolVersion,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var studies = CallerScope.StudyFilter(httpContext);
        var query = dbContext.TrainingCurricula.AsNoTracking().Where(c => c.IsActive);
        if (studies != null)
        {
            var allowed = studies.Select(s => new KernelStudyCode(s)).ToList();
            query = query.Where(c => allowed.Contains(c.StudyId));
        }

        if (!string.IsNullOrEmpty(studyId))
        {
            var targetStudy = new KernelStudyCode(studyId);
            query = query.Where(c => c.StudyId == targetStudy);
        }

        if (!string.IsNullOrEmpty(protocolVersion))
        {
            query = query.Where(c => c.ProtocolVersion == protocolVersion);
        }

        var list = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var dtos = list.Select(ToCurriculumDto).ToList();
        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> CreateCurriculumAsync(
        CreateTrainingCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        string studyCode = request.StudyId.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return TypedResults.Problem("Study ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.Problem("Curriculum title is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!CallerScope.CanAccessStudy(httpContext, studyCode))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        double passingScore = request.PassingScore ?? 0.8;
        if (passingScore is <= 0 or > 1.0)
        {
            return TypedResults.Problem("Passing score must be between 0 (exclusive) and 1.0 (inclusive).", statusCode: StatusCodes.Status400BadRequest);
        }

        string actorId = CallerScope.RequiredUserId(httpContext, "An admitted curriculum request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);
        var (tenantId, _) = await ResolveTenantContextAsync(httpContext, cancellationToken);
        var now = clock.UtcNow;
        string reason = string.IsNullOrWhiteSpace(request.ReasonForChange)
            ? "Author protocol training curriculum"
            : request.ReasonForChange.Trim();

        var modules = request.Modules ?? [];
        var questions = request.QuizQuestions ?? [];

        var curriculum = new TrainingCurriculum
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            StudyId = new KernelStudyCode(studyCode),
            ProtocolVersion = string.IsNullOrWhiteSpace(request.ProtocolVersion) ? "1.0" : request.ProtocolVersion.Trim(),
            ProtocolVersionNumber = request.ProtocolVersionNumber is > 0 ? request.ProtocolVersionNumber.Value : ParseVersionNumber(request.ProtocolVersion),
            QualificationCode = string.IsNullOrWhiteSpace(request.QualificationCode) ? "protocol_training" : request.QualificationCode.Trim(),
            PassingScore = passingScore,
            Title = request.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ModulesJson = modules.Count > 0 ? JsonSerializer.Serialize(modules, JsonOptions) : null,
            QuizQuestionsJson = questions.Count > 0 ? JsonSerializer.Serialize(questions, JsonOptions) : null,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = actorId,
            ReasonForChange = reason,
        };

        dbContext.TrainingCurricula.Add(curriculum);
        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.curriculum.create",
            RecordId = curriculum.Id,
            Details = $"Created curriculum '{curriculum.Title}' ({curriculum.ProtocolVersion}) for study {studyCode}.",
            ReasonForChange = reason,
        });

        await unitOfWork.CommitAsync(cancellationToken);

        var dto = ToCurriculumDto(curriculum);
        return TypedResults.Created($"/api/v1/training/curricula/{curriculum.Id}", dto);
    }

    public async Task<IResult> GetCurriculumAsync(
        string curriculumId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var c = await dbContext.TrainingCurricula.AsNoTracking().FirstOrDefaultAsync(x => x.Id == curriculumId, cancellationToken);
        if (c == null || !CallerScope.CanAccessStudy(httpContext, c.StudyId.Value))
        {
            return TypedResults.Problem($"Curriculum '{curriculumId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToCurriculumDto(c));
    }

    public async Task<IResult> ListStudyStaffAsync(
        string studyId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(studyId))
        {
            return TypedResults.Problem("study_id query parameter is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        string trimmedStudy = studyId.Trim();
        if (!CallerScope.CanAccessStudy(httpContext, trimmedStudy))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var (tenantId, tenantCode) = await ResolveTenantContextAsync(httpContext, cancellationToken);
        var grants = await kernelTrustClient.ListStudyStaffGrantsAsync(tenantId, tenantCode, trimmedStudy, cancellationToken);
        var principal = CallerScope.Principal(httpContext);

        var visible = grants
            .Where(g => SiteAccessEvaluator.CanAccessSite(principal, g.SiteId.Value))
            .GroupBy(g => (g.UserId, g.SiteId.Value))
            .Select(grp => grp.First())
            .OrderBy(g => g.SiteId.Value, StringComparer.Ordinal)
            .ThenBy(g => g.UserId, StringComparer.Ordinal)
            .Select(g => new StudyStaffGrantDto(g.UserId, g.UserName, g.SiteId, g.Role, g.GrantId))
            .ToList();

        return TypedResults.Ok(visible);
    }

    public async Task<IResult> BatchAssignCurriculumAsync(
        BatchAssignCurriculumRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CurriculumId))
        {
            return TypedResults.Problem("Curriculum ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        string studyCode = request.StudyId.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(studyCode))
        {
            return TypedResults.Problem("Study ID is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!CallerScope.CanAccessStudy(httpContext, studyCode))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        string actorId = CallerScope.RequiredUserId(httpContext, "An admitted training request has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);

        var requestedSites = (request.SiteIds ?? [])
            .Select(s => s.Value?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();
        bool namesSites = requestedSites.Count > 0;
        bool namesRoles = request.Roles is { Count: > 0 };
        bool namesPeople = request.TargetPersonnelIds is { Count: > 0 };
        if (!namesSites && !namesRoles && !namesPeople)
        {
            return TypedResults.Problem(
                "Name the site_ids, roles or target_personnel_ids to assign; there is no default audience.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var curriculum = await dbContext.TrainingCurricula.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CurriculumId, cancellationToken);
        if (curriculum == null || !string.Equals(curriculum.StudyId.Value, studyCode, StringComparison.Ordinal))
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' not found in study '{studyCode}'.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!curriculum.IsActive)
        {
            return TypedResults.Problem($"Curriculum '{request.CurriculumId}' is not active.", statusCode: StatusCodes.Status409Conflict);
        }

        var principal = CallerScope.Principal(httpContext);
        if (namesSites && requestedSites.Exists(site => !SiteAccessEvaluator.CanAccessSite(principal, site)))
        {
            return ClinicalScopeGuard.OutsideStudy();
        }

        var (tenantId, tenantCode) = await ResolveTenantContextAsync(httpContext, cancellationToken);
        var staff = (await kernelTrustClient.ListStudyStaffGrantsAsync(tenantId, tenantCode, studyCode, cancellationToken)).ToList();
        var studySites = staff.Select(d => d.SiteId.Value).ToHashSet(StringComparer.Ordinal);

        var unknownSites = namesSites ? requestedSites.Where(site => !studySites.Contains(site)).ToList() : [];
        if (unknownSites.Count > 0)
        {
            return TypedResults.Problem(
                $"Study '{studyCode}' has no active Kernel grants at: {string.Join(", ", unknownSites)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var unknownPeople = namesPeople
            ? request.TargetPersonnelIds!.Where(id => !staff.Exists(d => d.UserId == id)).ToList()
            : [];
        if (unknownPeople.Count > 0)
        {
            return TypedResults.Problem(
                $"No active Kernel grant in study '{studyCode}' for: {string.Join(", ", unknownPeople)}. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var candidateList = staff
            .Where(d => !namesSites || requestedSites.Contains(d.SiteId.Value, StringComparer.Ordinal))
            .Where(d => !namesRoles || request.Roles!.Contains(d.Role, StringComparer.OrdinalIgnoreCase))
            .Where(d => !namesPeople || request.TargetPersonnelIds!.Contains(d.UserId, StringComparer.Ordinal))
            .Where(d => SiteAccessEvaluator.CanAccessSite(principal, d.SiteId.Value))
            .GroupBy(d => (d.UserId, d.SiteId.Value))
            .Select(g => g.OrderBy(d => d.GrantId).First())
            .OrderBy(d => d.SiteId.Value, StringComparer.Ordinal).ThenBy(d => d.UserId, StringComparer.Ordinal)
            .ToList();

        if (candidateList.Count == 0)
        {
            return TypedResults.Problem(
                "No staff with active Kernel site grants match these sites, roles and people. Nothing was assigned.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        if (candidateList.Count > MaxBatchLimit)
        {
            return TypedResults.Problem(
                $"Synchronous batch API endpoints enforce a limit of {MaxBatchLimit} target personnel per request to prevent HTTP timeouts. Requested: {candidateList.Count}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var now = clock.UtcNow;
        int siteCount = candidateList.Select(c => c.SiteId.Value).Distinct(StringComparer.Ordinal).Count();
        string batchTxId = Guid.NewGuid().ToString();
        var createdAssignments = new List<TrainingAssignment>();
        var skippedItems = new List<SkippedAssignmentItemDto>();

        var existing = await dbContext.TrainingAssignments
            .Where(a => a.CurriculumId == request.CurriculumId && a.Status != "COMPLETED")
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidateList)
        {
            bool hasActive = existing.Any(a =>
                a.UserId == candidate.UserId &&
                string.Equals(a.SiteId.Value, candidate.SiteId.Value, StringComparison.Ordinal));

            if (hasActive)
            {
                skippedItems.Add(new SkippedAssignmentItemDto(
                    candidate.UserId,
                    candidate.SiteId,
                    candidate.Role,
                    "Duplicate active training assignment already exists for this curriculum"));
                continue;
            }

            var asn = new TrainingAssignment
            {
                Id = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                CurriculumId = request.CurriculumId,
                StudyId = new KernelStudyCode(studyCode),
                SiteId = candidate.SiteId,
                UserId = candidate.UserId,
                UserName = candidate.UserName,
                UserRole = candidate.Role,
                Status = "PENDING",
                BatchTransactionId = batchTxId,
                TimeSpentSeconds = 0,
                CreatedAt = now,
                CreatedBy = actorId,
                ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment from Kernel site grants",
            };

            dbContext.TrainingAssignments.Add(asn);
            createdAssignments.Add(asn);
        }

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.curriculum.batch_assign",
            BatchTransactionId = batchTxId,
            Details = $"Batch curriculum assignment for {request.CurriculumId} created {createdAssignments.Count} assignments across {siteCount} sites. {skippedItems.Count} items skipped.",
            ReasonForChange = request.ReasonForChange ?? "Batch curriculum assignment from Kernel site grants",
        });

        await unitOfWork.CommitAsync(cancellationToken);

        var assignmentDtos = createdAssignments.Select(ToAssignmentDto).ToList();
        var response = new BatchAssignmentResponse(
            batchTxId,
            request.CurriculumId,
            new KernelStudyCode(studyCode),
            candidateList.Count,
            createdAssignments.Count,
            skippedItems.Count,
            assignmentDtos,
            skippedItems,
            now);

        return TypedResults.Created($"/api/v1/training/assignments?batch_transaction_id={batchTxId}", response);
    }

    public async Task<IResult> ListAssignmentsAsync(
        string? studyId,
        string? siteId,
        string? userId,
        string? status,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ClinicalScopeGuard.ResolveStudyAndSiteFilters(httpContext, studyId, siteId, out var studies, out _, out var denied))
        {
            return denied!;
        }

        var query = dbContext.TrainingAssignments.AsNoTracking();
        if (studies != null)
        {
            var allowedStudies = studies.Select(s => new KernelStudyCode(s)).ToList();
            query = query.Where(a => allowedStudies.Contains(a.StudyId));
        }

        if (!string.IsNullOrEmpty(studyId))
        {
            var targetStudy = new KernelStudyCode(studyId);
            query = query.Where(a => a.StudyId == targetStudy);
        }

        if (!string.IsNullOrEmpty(siteId))
        {
            var targetSite = new KernelSiteCode(siteId);
            query = query.Where(a => a.SiteId == targetSite);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            query = query.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(a => a.Status == status);
        }

        var list = (await query.ToListAsync(cancellationToken))
            .Where(a => ClinicalScopeGuard.CanAccessSite(httpContext, a.SiteId.Value))
            .ToList();
        var dtos = list.Select(ToAssignmentDto).ToList();

        return TypedResults.Ok(dtos);
    }

    public async Task<IResult> GetAssignmentDetailAsync(
        string assignmentId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var asn = await dbContext.TrainingAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (asn == null
            || !CallerScope.CanAccessStudy(httpContext, asn.StudyId.Value)
            || !ClinicalScopeGuard.CanAccessSite(httpContext, asn.SiteId.Value))
        {
            return TypedResults.Problem($"Assignment '{assignmentId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(ToAssignmentDto(asn));
    }

    public async Task<IResult> CompleteAssignmentAsync(
        string assignmentId,
        CompleteTrainingAssignmentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.VerifiedSigToken() is null)
        {
            return TypedResults.Problem(
                "Electronic signature step-up token (X-Sig-Token) is required to attest protocol training completion.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (request?.SignaturePayload is null || string.IsNullOrWhiteSpace(request.SignaturePayload.Meaning))
        {
            return TypedResults.Problem(
                "21 CFR Part 11 signature meaning is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        string actorId = CallerScope.RequiredUserId(httpContext, "An admitted training attestation has no signed caller.");
        string actorRole = CallerScope.Roles(httpContext);

        var assignment = await dbContext.TrainingAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment == null
            || !CallerScope.CanAccessStudy(httpContext, assignment.StudyId.Value)
            || !ClinicalScopeGuard.CanAccessSite(httpContext, assignment.SiteId.Value))
        {
            return TypedResults.Problem($"Assignment '{assignmentId}' not found.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!string.Equals(actorId, assignment.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                "21 CFR Part 11 personal training attestation requires the assigned staff member to sign their own completion record.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.Equals(assignment.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Problem(
                $"Assignment '{assignmentId}' has already been completed and sealed.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var curriculum = await dbContext.TrainingCurricula.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == assignment.CurriculumId, cancellationToken);
        if (curriculum == null)
        {
            return TypedResults.Problem(
                $"Curriculum '{assignment.CurriculumId}' for assignment '{assignmentId}' was not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var quizQuestions = DeserializeList<TrainingQuizQuestionDto>(curriculum.QuizQuestionsJson);
        double passingScore = curriculum.PassingScore > 0 ? curriculum.PassingScore : 0.8;
        double score = EvaluateQuizScore(quizQuestions, request.QuizAnswers);
        if (quizQuestions.Count > 0 && score < passingScore)
        {
            return TypedResults.Problem(
                $"Quiz score {score:P0} is below the required passing threshold ({passingScore:P0}) for '{curriculum.Title}'.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var now = clock.UtcNow;
        string studyCode = assignment.StudyId.Value;
        string siteCode = assignment.SiteId.Value;
        string meaning = request.SignaturePayload.Meaning.Trim();
        string reason = string.IsNullOrWhiteSpace(request.SignaturePayload.ReasonForChange)
            ? $"Completed protocol training curriculum '{curriculum.Title}' ({curriculum.ProtocolVersion})"
            : request.SignaturePayload.ReasonForChange.Trim();

        byte[] signedPdfBytes = BuildSignedCertificatePdf(
            assignment,
            curriculum,
            actorId,
            actorRole,
            meaning,
            score,
            now);
        string sha256Seal = Convert.ToHexStringLower(SHA256.HashData(signedPdfBytes));

        var (tenantId, tenantCode) = await ResolveTenantContextAsync(httpContext, cancellationToken);
        string etmfDocId = await etmfClient.IngestTrainingCertificateAsync(
            tenantCode,
            studyCode,
            siteCode,
            assignment.Id,
            signedPdfBytes,
            reason,
            cancellationToken);

        var kernelRegistration = await kernelTrustClient.RegisterTrainingQualificationAsync(
            tenantId,
            tenantCode,
            studyCode,
            curriculum.ProtocolVersionNumber > 0 ? curriculum.ProtocolVersionNumber : ParseVersionNumber(curriculum.ProtocolVersion),
            string.IsNullOrWhiteSpace(curriculum.QualificationCode) ? "protocol_training" : curriculum.QualificationCode,
            assignment.UserId,
            $"TMF 05.02.04 Certificate - {curriculum.Title} ({assignment.UserId})",
            sha256Seal,
            signedPdfBytes.LongLength,
            etmfDocId,
            now,
            reason,
            $"training-complete:{assignment.Id}",
            cancellationToken);

        assignment.Status = "COMPLETED";
        assignment.QuizScore = Math.Round(score, 4);
        if (request.TimeSpentSeconds is > 0)
        {
            assignment.TimeSpentSeconds = request.TimeSpentSeconds.Value;
        }

        assignment.CompletedAt = now;
        assignment.EtmfDocumentId = etmfDocId;
        assignment.KernelArtifactVersionRef = kernelRegistration.ArtifactVersionRef;
        assignment.KernelQualificationRef = kernelRegistration.QualificationRef;
        assignment.ReasonForChange = reason;

        var cert = new TrainingCertificate
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            AssignmentId = assignment.Id,
            BatchTransactionId = assignment.BatchTransactionId,
            SignerName = actorId,
            SignerRole = actorRole,
            Meaning = meaning,
            Sha256Seal = sha256Seal,
            EtmfArtifactId = "05.02.04",
            EtmfDocumentId = etmfDocId,
            KernelArtifactVersionRef = kernelRegistration.ArtifactVersionRef,
            KernelQualificationRef = kernelRegistration.QualificationRef,
            CreatedAt = now,
        };
        dbContext.TrainingCertificates.Add(cert);

        dbContext.OrgAuditLogs.Add(new OrgAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = now,
            ActorId = actorId,
            ActorRole = actorRole,
            Action = "training.assignment.complete",
            RecordId = assignment.Id,
            BatchTransactionId = assignment.BatchTransactionId,
            Details = $"Completed assignment {assignment.Id} (score={score:P0}), filed eTMF 05.02.04 {etmfDocId}, recorded Kernel {kernelRegistration.QualificationRef}.",
            ReasonForChange = reason,
        });

        await unitOfWork.CommitAsync(cancellationToken);

        return TypedResults.Ok(new CompleteTrainingAssignmentResponse(
            ToAssignmentDto(assignment),
            assignment.QuizScore ?? score,
            passingScore,
            sha256Seal,
            "05.02.04",
            etmfDocId,
            kernelRegistration.ArtifactVersionRef,
            kernelRegistration.QualificationRef,
            now));
    }

    private byte[] BuildSignedCertificatePdf(
        TrainingAssignment assignment,
        TrainingCurriculum curriculum,
        string signerId,
        string signerRole,
        string meaning,
        double score,
        DateTimeOffset completedAt)
    {
        var pdf = new StringBuilder();
        pdf.AppendLine("%PDF-1.7");
        pdf.AppendLine("% Cadence Clinical 21 CFR Part 11 Site Staff Qualification and Training Record (TMF 05.02.04)");
        pdf.AppendLine("================================================================================");
        pdf.AppendLine("          SITE STAFF QUALIFICATION AND TRAINING CERTIFICATE (05.02.04)          ");
        pdf.AppendLine("================================================================================");
        pdf.AppendLine($"Assignment ID     : {assignment.Id}");
        pdf.AppendLine($"Study Code        : {assignment.StudyId.Value}");
        pdf.AppendLine($"Site Code         : {assignment.SiteId.Value}");
        pdf.AppendLine($"Curriculum        : {curriculum.Title} (Protocol {curriculum.ProtocolVersion})");
        pdf.AppendLine($"Qualification Code: {curriculum.QualificationCode}");
        pdf.AppendLine($"Trainee           : {assignment.UserName} ({assignment.UserId})");
        pdf.AppendLine($"Signer            : {signerId} ({signerRole})");
        pdf.AppendLine($"Signature Meaning : {meaning}");
        pdf.AppendLine($"Quiz Score        : {score:P0}");
        pdf.AppendLine($"Completed At (UTC): {completedAt:O}");
        pdf.AppendLine("================================================================================");
        pdf.AppendLine("%%EOF");

        byte[] rawPdf = Encoding.UTF8.GetBytes(pdf.ToString().TrimEnd());
        var (cert, key) = Pkcs7Signer.GenerateSigningCredentials(signerId, clock);
        var signer = new Pkcs7Signer(cert, key);
        return signer.SignPdf(rawPdf);
    }

    private static double EvaluateQuizScore(
        IReadOnlyList<TrainingQuizQuestionDto> questions,
        IReadOnlyList<QuizAnswerDto>? answers)
    {
        if (questions.Count == 0)
        {
            return 1.0;
        }

        if (answers is null || answers.Count == 0)
        {
            return 0.0;
        }

        var byQuestion = answers
            .Where(a => !string.IsNullOrWhiteSpace(a.QuestionId))
            .GroupBy(a => a.QuestionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().SelectedOptionId, StringComparer.Ordinal);

        int correct = 0;
        foreach (var question in questions)
        {
            if (byQuestion.TryGetValue(question.Id, out string? selected)
                && string.Equals(selected, question.CorrectOptionId, StringComparison.Ordinal))
            {
                correct++;
            }
        }

        return (double)correct / questions.Count;
    }

    private static int ParseVersionNumber(string? protocolVersion)
    {
        if (string.IsNullOrWhiteSpace(protocolVersion))
        {
            return 1;
        }

        string digits = new(protocolVersion.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int parsed) && parsed > 0 ? parsed : 1;
    }

    private static async Task<(TenantId TenantId, string? TenantCode)> ResolveTenantContextAsync(
        HttpContext httpContext,
        CancellationToken ct)
    {
        string? rawHeader = httpContext.Request.Headers[GatewayHeaders.TenantId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            rawHeader = httpContext.User.FindFirst("tenant_id")?.Value;
        }

        if (httpContext.RequestServices?.GetService<ICurrentTenantAccessor>() is { } accessor)
        {
            Guid? guid = await accessor.GetCurrentTenantIdAsync(ct);
            if (guid is { } g && g != Guid.Empty)
            {
                return (TenantId.FromGuid(g), rawHeader);
            }
        }

        if (!string.IsNullOrWhiteSpace(rawHeader))
        {
            if (Guid.TryParse(rawHeader, out Guid parsed) && parsed != Guid.Empty)
            {
                return (TenantId.FromGuid(parsed), rawHeader);
            }

            return (TenantId.FromGuid(DeterministicTenantCodeResolver.ComputeDeterministicGuid(rawHeader.Trim())), rawHeader.Trim());
        }

        return (TenantId.FromGuid(DeterministicTenantCodeResolver.ComputeDeterministicGuid("tenant-test")), "tenant-test");
    }

    private static TrainingCurriculumDto ToCurriculumDto(TrainingCurriculum c) =>
        new(
            c.Id,
            c.StudyId,
            c.ProtocolVersion,
            c.ProtocolVersionNumber > 0 ? c.ProtocolVersionNumber : 1,
            string.IsNullOrWhiteSpace(c.QualificationCode) ? "protocol_training" : c.QualificationCode,
            c.PassingScore > 0 ? c.PassingScore : 0.8,
            c.Title,
            c.Description,
            DeserializeList<TrainingModuleDto>(c.ModulesJson),
            DeserializeList<TrainingQuizQuestionDto>(c.QuizQuestionsJson),
            c.IsActive,
            c.CreatedAt);

    private static TrainingAssignmentDto ToAssignmentDto(TrainingAssignment a) =>
        new(
            a.Id,
            a.CurriculumId,
            a.StudyId,
            a.SiteId,
            a.UserId,
            a.UserName,
            a.UserRole,
            a.Status,
            a.BatchTransactionId,
            a.QuizScore,
            a.TimeSpentSeconds,
            a.CompletedAt,
            a.EtmfDocumentId,
            a.KernelArtifactVersionRef,
            a.KernelQualificationRef,
            a.CreatedAt);

    private static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    }
}
