using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Abstractions;
using Cadence.Core.Domain;
using Cadence.Core.Domain.Randomization;
using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Adaptive;

public sealed class AdaptiveDoseEscalationService : IAdaptiveDoseEscalationService
{
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IClock _clock;

    // In-memory persistent state per study
    private static readonly ConcurrentDictionary<string, StudyAdaptiveState> StudyStates = new(StringComparer.OrdinalIgnoreCase);

    public AdaptiveDoseEscalationService(IAuditLogWriter auditLogWriter, IClock clock)
    {
        _auditLogWriter = auditLogWriter;
        _clock = clock;
    }

    public Task<AdaptiveDoseEscalationStateResponse> GetStateAsync(string studyId, CancellationToken ct = default)
    {
        var studyState = GetOrCreateStudyState(studyId);
        var response = EvaluateAndBuildResponse(studyState);
        return Task.FromResult(response);
    }

    public async Task<Result<AdaptiveCohortTransitionResponse>> SubmitCohortTransitionAsync(
        AdaptiveCohortTransitionRequest request,
        AuthenticatedCaller caller,
        string? sigToken,
        string? changeReasonHeader,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return DomainError.Validation("INVALID_STUDY_ID", "Study ID is required.");
        }

        // Validate Electronic Signature (Requirement 3 & 21 CFR Part 11)
        string? eSig = request.ElectronicSignature?.Trim();
        if (string.IsNullOrWhiteSpace(eSig) && string.IsNullOrWhiteSpace(sigToken))
        {
            return DomainError.Unauthorized("ELECTRONIC_SIGNATURE_REQUIRED", "21 CFR Part 11 electronic signature or valid sig_token header is required for cohort transitions.");
        }

        var studyState = GetOrCreateStudyState(request.StudyId);
        int currentDoseIndex = studyState.CurrentDoseLevelIndex;

        // Evaluate BoinEngine recommendation to determine expected decision
        var config = new BOINConfig(TargetDltRate: studyState.TargetToxicityRate);
        var currentCohortState = studyState.CohortStates.FirstOrDefault(c => c.DoseIndex == currentDoseIndex)
            ?? new DoseCohortState(currentDoseIndex, studyState.DoseLevels[currentDoseIndex].DoseAmount, studyState.DoseLevels[currentDoseIndex].DoseUnit, 3, 0);

        var boinRecommendation = BoinEngine.EvaluateDoseLevel(config, studyState.DoseLevels, currentCohortState, studyState.CohortStates);

        string recommendedActionNorm = NormalizeAction(boinRecommendation.Action.ToString());
        int recommendedTargetIndex = boinRecommendation.RecommendedDoseIndex ?? currentDoseIndex;

        string requestedActionNorm = NormalizeAction(request.DecisionAction);

        bool isOverride = requestedActionNorm != recommendedActionNorm || request.TargetDoseLevelIndex != recommendedTargetIndex;

        // Validate Override Rationale (Constraint & Requirement 3)
        if (isOverride && string.IsNullOrWhiteSpace(request.OverrideReason))
        {
            return DomainError.Validation(
                "OVERRIDE_RATIONALE_REQUIRED",
                "Safety Committee override justification is required when overriding BoinEngine algorithmic recommendation.");
        }

        int previousDoseIndex = studyState.CurrentDoseLevelIndex;
        int newDoseIndex = Math.Clamp(request.TargetDoseLevelIndex, 0, studyState.DoseLevels.Count - 1);

        // Update Study State
        studyState.CurrentDoseLevelIndex = newDoseIndex;
        studyState.ActiveCohortNumber += 1;

        // Ensure new dose level cohort state exists
        if (!studyState.CohortStates.Any(c => c.DoseIndex == newDoseIndex))
        {
            var doseDef = studyState.DoseLevels[newDoseIndex];
            studyState.CohortStates.Add(new DoseCohortState(newDoseIndex, doseDef.DoseAmount, doseDef.DoseUnit, 3, 0));
        }

        DateTimeOffset now = _clock.UtcNow;
        string authorizedBy = caller.UserId ?? eSig ?? "clinical_investigator";
        string overrideReasonText = request.OverrideReason?.Trim() ?? string.Empty;
        string effectiveReason;
        if (!string.IsNullOrWhiteSpace(overrideReasonText))
        {
            effectiveReason = overrideReasonText;
        }
        else if (!string.IsNullOrWhiteSpace(changeReasonHeader))
        {
            effectiveReason = changeReasonHeader;
        }
        else
        {
            effectiveReason = $"Authorized cohort transition to dose level #{newDoseIndex}";
        }

        string auditEntryId = $"AUDIT-TRANSITION-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

        // Write 21 CFR Part 11 Audit Log Entry via IAuditLogWriter
        var oldValues = new Dictionary<string, object?>
        {
            ["active_cohort_number"] = studyState.ActiveCohortNumber - 1,
            ["current_dose_level_index"] = previousDoseIndex,
            ["recommendation_action"] = recommendedActionNorm
        };

        var newValues = new Dictionary<string, object?>
        {
            ["active_cohort_number"] = studyState.ActiveCohortNumber,
            ["current_dose_level_index"] = newDoseIndex,
            ["decision_action"] = request.DecisionAction,
            ["is_override"] = isOverride,
            ["override_reason"] = overrideReasonText,
            ["electronic_signature"] = eSig
        };

        await _auditLogWriter.WriteAsync(
            entityName: "AdaptiveCohortTransition",
            recordId: $"{request.StudyId}:Cohort#{studyState.ActiveCohortNumber}",
            action: $"COHORT_TRANSITION_{request.DecisionAction}",
            userId: authorizedBy,
            changeReason: effectiveReason,
            oldValues: oldValues,
            newValues: newValues,
            ct: ct);

        var updatedState = EvaluateAndBuildResponse(studyState);

        var response = new AdaptiveCohortTransitionResponse(
            StudyId: request.StudyId,
            CohortNumber: studyState.ActiveCohortNumber,
            PreviousDoseLevelIndex: previousDoseIndex,
            NewDoseLevelIndex: newDoseIndex,
            ActionExecuted: request.DecisionAction,
            IsOverride: isOverride,
            OverrideReason: isOverride ? overrideReasonText : null,
            AuthorizedBy: authorizedBy,
            SignatureTimestamp: now,
            AuditEntryId: auditEntryId,
            UpdatedState: updatedState
        );

        return response;
    }

    private static StudyAdaptiveState GetOrCreateStudyState(string studyId)
    {
        return StudyStates.GetOrAdd(studyId, id =>
        {
            var doseLevels = new List<DoseLevelDefinition>
            {
                new(0, 10.0, "mg"),
                new(1, 20.0, "mg"),
                new(2, 40.0, "mg"),
                new(3, 80.0, "mg"),
                new(4, 160.0, "mg")
            };

            var cohortStates = new List<DoseCohortState>
            {
                new(0, 10.0, "mg", EvaluatedSubjects: 6, ObservedDlts: 0),
                new(1, 20.0, "mg", EvaluatedSubjects: 3, ObservedDlts: 0),
                new(2, 40.0, "mg", EvaluatedSubjects: 0, ObservedDlts: 0),
                new(3, 80.0, "mg", EvaluatedSubjects: 0, ObservedDlts: 0),
                new(4, 160.0, "mg", EvaluatedSubjects: 0, ObservedDlts: 0)
            };

            var rarArms = new List<ResponseArmState>
            {
                new("ARM-CTRL", EvaluatedSubjects: 10, ObservedResponses: 3, IsControl: true),
                new("ARM-DOSE1", EvaluatedSubjects: 12, ObservedResponses: 6),
                new("ARM-DOSE2", EvaluatedSubjects: 8, ObservedResponses: 5)
            };

            return new StudyAdaptiveState
            {
                StudyId = id,
                DesignType = "BOIN",
                TargetToxicityRate = 0.25,
                ActiveCohortNumber = 1,
                CurrentDoseLevelIndex = 1,
                DoseLevels = doseLevels,
                CohortStates = cohortStates,
                RarArms = rarArms,
                IsActive = true
            };
        });
    }

    private static AdaptiveDoseEscalationStateResponse EvaluateAndBuildResponse(StudyAdaptiveState studyState)
    {
        var (lambdaE, lambdaD) = BoinEngine.CalculateBoinBoundaries(studyState.TargetToxicityRate);

        int currentDoseIndex = studyState.CurrentDoseLevelIndex;
        var currentCohortState = studyState.CohortStates.FirstOrDefault(c => c.DoseIndex == currentDoseIndex)
            ?? new DoseCohortState(currentDoseIndex, studyState.DoseLevels[currentDoseIndex].DoseAmount, studyState.DoseLevels[currentDoseIndex].DoseUnit, 0, 0);

        var config = new BOINConfig(TargetDltRate: studyState.TargetToxicityRate);
        var boinRec = BoinEngine.EvaluateDoseLevel(config, studyState.DoseLevels, currentCohortState, studyState.CohortStates);

        string currentDoseName = $"Dose {currentDoseIndex + 1} ({studyState.DoseLevels[currentDoseIndex].DoseAmount} {studyState.DoseLevels[currentDoseIndex].DoseUnit})";

        int? recIndex = boinRec.RecommendedDoseIndex;
        string? recName = recIndex.HasValue && recIndex.Value >= 0 && recIndex.Value < studyState.DoseLevels.Count
            ? $"Dose {recIndex.Value + 1} ({studyState.DoseLevels[recIndex.Value].DoseAmount} {studyState.DoseLevels[recIndex.Value].DoseUnit})"
            : currentDoseName;

        var doseSummaries = new List<DoseLevelSummaryResponse>();
        foreach (var level in studyState.DoseLevels)
        {
            var cohort = studyState.CohortStates.FirstOrDefault(c => c.DoseIndex == level.DoseIndex)
                ?? new DoseCohortState(level.DoseIndex, level.DoseAmount, level.DoseUnit, 0, 0);

            double? posteriorDlt = cohort.EvaluatedSubjects >= 3
                ? (double?)cohort.EmpiricalDltRate
                : null;

            double? overdoseProb = cohort.EvaluatedSubjects >= 3
                ? (double?)BoinEngine.CalculatePosteriorProbToxic(cohort.ObservedDlts, cohort.EvaluatedSubjects, studyState.TargetToxicityRate)
                : null;

            doseSummaries.Add(new DoseLevelSummaryResponse(
                DoseLevelIndex: level.DoseIndex,
                DoseName: $"Level {level.DoseIndex + 1} - {level.DoseAmount}{level.DoseUnit}",
                DoseValue: level.DoseAmount,
                TotalEnrolled: cohort.EvaluatedSubjects,
                TotalDlts: cohort.ObservedDlts,
                ObservedDltRate: cohort.EmpiricalDltRate,
                PosteriorDltRate: posteriorDlt,
                IsEliminatedForSafety: cohort.IsEliminated,
                OverdoseProbability: overdoseProb
            ));
        }

        // Calculate RAR superiority probabilities using ResponseAdaptiveEngine
        var rarConfig = new ResponseAdaptiveConfig();
        var posteriorParams = studyState.RarArms.ToDictionary(
            a => a.ArmId,
            a => (
                Alpha: Math.Max(1e-6, rarConfig.PriorAlpha + a.ObservedResponses),
                Beta: Math.Max(1e-6, rarConfig.PriorBeta + a.EvaluatedSubjects - a.ObservedResponses)
            )
        );

        var superiorityProbs = ResponseAdaptiveEngine.CalculateSuperiorityProbabilities(rarConfig, studyState.RarArms, posteriorParams);

        var rarArmResponses = new List<RarArmResponse>();
        double totalSup = superiorityProbs.Values.Sum();

        foreach (var arm in studyState.RarArms)
        {
            double supProb = superiorityProbs.GetValueOrDefault(arm.ArmId, 0.0);
            double allocWeight = totalSup > 0 ? supProb / totalSup : 1.0 / studyState.RarArms.Count;

            string status = "ACTIVE";
            if (arm.IsDropped)
            {
                status = "DROPPED";
            }
            else if (arm.IsGraduated)
            {
                status = "GRADUATED";
            }

            rarArmResponses.Add(new RarArmResponse(
                ArmId: arm.ArmId,
                ArmName: arm.IsControl ? "Control Arm" : $"Treatment {arm.ArmId}",
                IsControl: arm.IsControl,
                Status: status,
                EnrolledCount: arm.EvaluatedSubjects,
                ResponseCount: arm.ObservedResponses,
                PosteriorMean: arm.EmpiricalResponseRate,
                AllocationProbability: Math.Round(allocWeight, 4),
                SuperiorityVsControlProb: Math.Round(supProb, 4)
            ));
        }

        return new AdaptiveDoseEscalationStateResponse(
            StudyId: studyState.StudyId,
            DesignType: studyState.DesignType,
            TargetToxicityRate: studyState.TargetToxicityRate,
            LambdaE: lambdaE,
            LambdaD: lambdaD,
            ActiveCohortNumber: studyState.ActiveCohortNumber,
            CurrentDoseLevelIndex: currentDoseIndex,
            CurrentDoseName: currentDoseName,
            RecommendationAction: boinRec.Action.ToString(),
            RecommendedDoseLevelIndex: recIndex,
            RecommendedDoseName: recName,
            Rationale: boinRec.Rationale,
            IsActive: studyState.IsActive,
            DoseSummaries: doseSummaries,
            RarArms: rarArmResponses
        );
    }

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "STAY";
        string upper = action.Trim().ToUpperInvariant();
        if (upper is "DE_ESCALATE" or "DEESCALATE") return "DE_ESCALATE";
        if (upper is "STOP_TRIAL" or "STOP_SAFETY") return "STOP_SAFETY";
        if (upper is "STOP_SUCCESS" or "MTD_FOUND") return "STOP_SUCCESS";
        return upper;
    }

    private sealed class StudyAdaptiveState
    {
        public required string StudyId { get; set; }
        public required string DesignType { get; set; }
        public double TargetToxicityRate { get; set; }
        public int ActiveCohortNumber { get; set; }
        public int CurrentDoseLevelIndex { get; set; }
        public required List<DoseLevelDefinition> DoseLevels { get; set; }
        public required List<DoseCohortState> CohortStates { get; set; }
        public required List<ResponseArmState> RarArms { get; set; }
        public bool IsActive { get; set; }
    }
}
