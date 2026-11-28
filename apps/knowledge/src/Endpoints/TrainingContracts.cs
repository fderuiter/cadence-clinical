using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Kernel.Tenancy;

namespace Cadence.Apps.Knowledge.Endpoints;

public sealed record TrainingModuleDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("section_ref")] string? SectionRef,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("key_changes")] List<string>? KeyChanges,
    [property: JsonPropertyName("estimated_minutes")] int EstimatedMinutes);

public sealed record TrainingQuizOptionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label);

public sealed record TrainingQuizQuestionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("options")] List<TrainingQuizOptionDto> Options,
    [property: JsonPropertyName("correct_option_id")] string CorrectOptionId);

public sealed record CreateTrainingCurriculumRequest(
    [property: JsonPropertyName("study_id")] KernelStudyCode StudyId,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("protocol_version_number")] int? ProtocolVersionNumber,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("qualification_code")] string? QualificationCode,
    [property: JsonPropertyName("passing_score")] double? PassingScore,
    [property: JsonPropertyName("modules")] List<TrainingModuleDto>? Modules,
    [property: JsonPropertyName("quiz_questions")] List<TrainingQuizQuestionDto>? QuizQuestions,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record TrainingCurriculumDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("study_id")] KernelStudyCode StudyId,
    [property: JsonPropertyName("protocol_version")] string ProtocolVersion,
    [property: JsonPropertyName("protocol_version_number")] int ProtocolVersionNumber,
    [property: JsonPropertyName("qualification_code")] string QualificationCode,
    [property: JsonPropertyName("passing_score")] double PassingScore,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("modules")] List<TrainingModuleDto> Modules,
    [property: JsonPropertyName("quiz_questions")] List<TrainingQuizQuestionDto> QuizQuestions,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record StudyStaffGrantDto(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("site_id")] KernelSiteCode SiteId,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("grant_id")] Guid GrantId);

public sealed record BatchAssignCurriculumRequest(
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] KernelStudyCode StudyId,
    [property: JsonPropertyName("site_ids")] List<KernelSiteCode>? SiteIds,
    [property: JsonPropertyName("roles")] List<string>? Roles,
    [property: JsonPropertyName("target_personnel_ids")] List<string>? TargetPersonnelIds,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record TrainingAssignmentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] KernelStudyCode StudyId,
    [property: JsonPropertyName("site_id")] KernelSiteCode SiteId,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("batch_transaction_id")] string? BatchTransactionId,
    [property: JsonPropertyName("quiz_score")] double? QuizScore,
    [property: JsonPropertyName("time_spent_seconds")] int TimeSpentSeconds,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("etmf_document_id")] string? EtmfDocumentId,
    [property: JsonPropertyName("kernel_artifact_version_ref")] string? KernelArtifactVersionRef,
    [property: JsonPropertyName("kernel_qualification_ref")] string? KernelQualificationRef,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record SkippedAssignmentItemDto(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("site_id")] KernelSiteCode SiteId,
    [property: JsonPropertyName("user_role")] string UserRole,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record BatchAssignmentResponse(
    [property: JsonPropertyName("batch_transaction_id")] string BatchTransactionId,
    [property: JsonPropertyName("curriculum_id")] string CurriculumId,
    [property: JsonPropertyName("study_id")] KernelStudyCode StudyId,
    [property: JsonPropertyName("total_eligible")] int TotalEligible,
    [property: JsonPropertyName("total_assigned")] int TotalAssigned,
    [property: JsonPropertyName("total_skipped")] int TotalSkipped,
    [property: JsonPropertyName("assignments")] List<TrainingAssignmentDto> Assignments,
    [property: JsonPropertyName("skipped_items")] List<SkippedAssignmentItemDto> SkippedItems,
    [property: JsonPropertyName("executed_at")] DateTimeOffset ExecutedAt);

public sealed record SignaturePayloadDto(
    [property: JsonPropertyName("meaning")] string Meaning,
    [property: JsonPropertyName("reason_for_change")] string? ReasonForChange);

public sealed record QuizAnswerDto(
    [property: JsonPropertyName("question_id")] string QuestionId,
    [property: JsonPropertyName("selected_option_id")] string SelectedOptionId);

public sealed record CompleteTrainingAssignmentRequest(
    [property: JsonPropertyName("quiz_answers")] List<QuizAnswerDto>? QuizAnswers,
    [property: JsonPropertyName("time_spent_seconds")] int? TimeSpentSeconds,
    [property: JsonPropertyName("signature_payload")] SignaturePayloadDto SignaturePayload);

public sealed record CompleteTrainingAssignmentResponse(
    [property: JsonPropertyName("assignment")] TrainingAssignmentDto Assignment,
    [property: JsonPropertyName("quiz_score")] double QuizScore,
    [property: JsonPropertyName("passing_score")] double PassingScore,
    [property: JsonPropertyName("sha256_seal")] string Sha256Seal,
    [property: JsonPropertyName("etmf_artifact_id")] string EtmfArtifactId,
    [property: JsonPropertyName("etmf_document_id")] string EtmfDocumentId,
    [property: JsonPropertyName("kernel_artifact_version_ref")] string KernelArtifactVersionRef,
    [property: JsonPropertyName("kernel_qualification_ref")] string KernelQualificationRef,
    [property: JsonPropertyName("executed_at")] DateTimeOffset ExecutedAt);
