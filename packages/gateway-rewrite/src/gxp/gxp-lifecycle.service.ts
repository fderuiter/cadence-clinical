import { Injectable } from "@nestjs/common";
import { GxpContextService } from "./gxp-context.service.js";
import {
  AuditJustificationError,
  ComplianceError,
  InvalidStateTransitionError,
  LockedFactorMutationError,
} from "./gxp-exceptions.js";

@Injectable()
export class GxpLifecycleService {
  constructor(private readonly gxpContext: GxpContextService) {}

  // Allowed state transitions map
  private readonly permittedTransitions: Record<string, string[]> = {
    SCREENING: ["SCREEN_FAILED", "ENROLLED"],
    ENROLLED: ["RANDOMIZED"],
    RANDOMIZED: ["ACTIVE", "WITHDRAWN", "UNBLINDED"],
    ACTIVE: ["COMPLETED", "WITHDRAWN", "UNBLINDED"],
    UNBLINDED: ["WITHDRAWN", "COMPLETED"],
  };

  // Lock configurations
  private trialLocked: boolean = false;
  private readonly lockedSites = new Set<string>();
  private readonly lockedEntities = new Set<string>();

  setTrialLock(locked: boolean): void {
    this.trialLocked = locked;
  }

  lockSite(siteId: string): void {
    this.lockedSites.add(siteId);
  }

  unlockSite(siteId: string): void {
    this.lockedSites.delete(siteId);
  }

  lockEntity(entityId: string): void {
    this.lockedEntities.add(String(entityId));
  }

  unlockEntity(entityId: string): void {
    this.lockedEntities.delete(String(entityId));
  }

  clearLocks(): void {
    this.trialLocked = false;
    this.lockedSites.clear();
    this.lockedEntities.clear();
  }

  /**
   * Enforces that subjects transition strictly through defined sequential states,
   * beginning at SCREENING. Identical transitions (same state) are always permitted.
   */
  guardSubjectTransition(
    fromState: string | null | undefined,
    toState: string
  ): void {
    const from = fromState || null;
    if (from === null) {
      if (toState !== "SCREENING") {
        throw new InvalidStateTransitionError(
          `Initial state must be SCREENING. Cannot start at "${toState}".`
        );
      }
      return;
    }

    if (from === toState) {
      return; // Permitted
    }

    const allowed = this.permittedTransitions[from];
    if (!allowed || !allowed.includes(toState)) {
      throw new InvalidStateTransitionError(
        `Invalid subject state transition from "${from}" to "${toState}".`
      );
    }
  }

  /**
   * Performs deep equality comparison on stratification factors (Key-Value map).
   */
  private areFactorsEqual(a: any, b: any): boolean {
    if (a === b) return true;
    if (!a || !b) return false;
    const keysA = Object.keys(a).sort();
    const keysB = Object.keys(b).sort();
    if (keysA.length !== keysB.length) return false;
    for (let i = 0; i < keysA.length; i++) {
      if (keysA[i] !== keysB[i]) return false;
      const valA = a[keysA[i]];
      const valB = b[keysB[i]];
      if (typeof valA === "object" && typeof valB === "object") {
        if (!this.areFactorsEqual(valA, valB)) return false;
      } else if (valA !== valB) {
        return false;
      }
    }
    return true;
  }

  /**
   * Intercepts clinical write operations.
   * Performs validations before database/state commits.
   */
  validateWrite(entity: any, originalEntity?: any): void {
    // 1. Trial Lock Check
    if (this.trialLocked) {
      throw new ComplianceError("Trial is currently locked.");
    }

    // 2. Site Lock Check
    const siteId = entity.siteId || entity.site_id;
    if (siteId && this.lockedSites.has(siteId)) {
      throw new ComplianceError(`Site ${siteId} is currently locked.`);
    }

    // 3. Entity Lock Check
    const entityId =
      entity.id ||
      entity.entityId ||
      entity.entity_id ||
      entity.subjectId ||
      entity.subject_id;
    if (entityId && this.lockedEntities.has(String(entityId))) {
      throw new ComplianceError(`Entity ${entityId} is currently locked.`);
    }

    // 4. Change Justification Reason Verification
    const reasonForChange =
      entity.reasonForChange ||
      entity.reason_for_change ||
      this.gxpContext.getChangeReason();
    if (
      !reasonForChange ||
      typeof reasonForChange !== "string" ||
      !reasonForChange.trim()
    ) {
      throw new AuditJustificationError("Reason for change cannot be empty.");
    }

    // 5. Subject Transition & Stratification Factor checks if entity is a subject
    const isSubject =
      entity.status !== undefined ||
      entity.stratFactors !== undefined ||
      entity.strat_factors !== undefined ||
      entity.subjectId !== undefined ||
      entity.subject_id !== undefined;

    if (isSubject) {
      const fromStatus = originalEntity ? originalEntity.status : null;
      const toStatus = entity.status;

      // Validate transitions
      if (toStatus) {
        this.guardSubjectTransition(fromStatus, toStatus);
      }

      // Validate stratification factor immutable post-randomization rules
      const wasPostRandomization =
        fromStatus &&
        [
          "RANDOMIZED",
          "ACTIVE",
          "WITHDRAWN",
          "UNBLINDED",
          "COMPLETED",
        ].includes(fromStatus);

      if (wasPostRandomization) {
        const oldFactors =
          originalEntity.stratFactors || originalEntity.strat_factors;
        const newFactors = entity.stratFactors || entity.strat_factors;

        if (
          newFactors !== undefined &&
          !this.areFactorsEqual(oldFactors, newFactors)
        ) {
          throw new LockedFactorMutationError(
            "Stratification factors are locked post-randomization."
          );
        }
      }
    }
  }
}
