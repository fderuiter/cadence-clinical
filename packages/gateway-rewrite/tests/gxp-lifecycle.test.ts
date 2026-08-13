import { describe, it, expect, beforeEach } from "vitest";
import {
  GxpContextService,
  GxpLifecycleService,
  ClinicalSubject,
  AuditJustificationError,
  ComplianceError,
  InvalidStateTransitionError,
  LockedFactorMutationError,
} from "../src/index.js";

describe("Native NestJS GxP Lifecycle Layer", () => {
  let gxpContext: GxpContextService;
  let gxpLifecycle: GxpLifecycleService;

  beforeEach(() => {
    gxpContext = new GxpContextService();
    gxpLifecycle = new GxpLifecycleService(gxpContext);
  });

  describe("Requirement 1: Secure Asynchronous Context Propagation", () => {
    it("should capture and propagate change justification reasons securely across execution contexts", () => {
      const reason = "Updating Subject Demographics";
      gxpContext.runWithReason(reason, () => {
        expect(gxpContext.getChangeReason()).toBe(reason);
      });
    });

    it("should not leak context across concurrent asynchronous execution paths", async () => {
      const runConcurrentTest = async (
        id: number,
        reason: string,
        delay: number
      ) => {
        return gxpContext.runWithReason(reason, async () => {
          await new Promise((resolve) => setTimeout(resolve, delay));
          // Verify that the context for this path has not been overwritten by other concurrent paths
          expect(gxpContext.getChangeReason()).toBe(reason);
          return id;
        });
      };

      // Run multiple concurrent tasks with unique reasons and different delays to ensure isolation
      const results = await Promise.all([
        runConcurrentTest(1, "Reason for task 1", 50),
        runConcurrentTest(2, "Reason for task 2", 10),
        runConcurrentTest(3, "Reason for task 3", 30),
      ]);

      expect(results).toEqual([1, 2, 3]);
    });
  });

  describe("Requirement 2: Database Pre-commit Interception & Entity Lock Enforcement", () => {
    it("should reject clinical writes lacking a change justification prior to SQL execution", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        siteId: "SITE-101",
        status: "SCREENING",
        stratFactors: { age: "GE_65" },
      });

      // No reason in context, and no reason on model -> Should throw AuditJustificationError
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        AuditJustificationError
      );

      // Whitespace reason should also be rejected
      subject.reasonForChange = "   ";
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        AuditJustificationError
      );
    });

    it("should permit clinical writes when a valid justification is provided via context or on the entity", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        siteId: "SITE-101",
        status: "SCREENING",
        stratFactors: { age: "GE_65" },
        reasonForChange: "Subject enrolled successfully",
      });

      // Valid on model
      expect(() => gxpLifecycle.validateWrite(subject)).not.toThrow();

      // Valid via context
      subject.reasonForChange = undefined;
      gxpContext.runWithReason("Subject enrolled from context", () => {
        expect(() => gxpLifecycle.validateWrite(subject)).not.toThrow();
      });
    });

    it("should raise an explicit transaction-level exception under active Trial Lock", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        siteId: "SITE-101",
        status: "SCREENING",
        reasonForChange: "A valid reason",
      });

      gxpLifecycle.setTrialLock(true);

      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        ComplianceError
      );
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        "Trial is currently locked."
      );

      gxpLifecycle.setTrialLock(false);
      expect(() => gxpLifecycle.validateWrite(subject)).not.toThrow();
    });

    it("should raise an explicit transaction-level exception under active Site Lock", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        siteId: "SITE-101",
        status: "SCREENING",
        reasonForChange: "A valid reason",
      });

      gxpLifecycle.lockSite("SITE-101");

      // Attempting write on SITE-101 must fail
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        ComplianceError
      );
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        "Site SITE-101 is currently locked."
      );

      // Attempting write on SITE-202 must succeed
      const subjectSite2 = new ClinicalSubject({
        subjectId: "SUBJ-002",
        siteId: "SITE-202",
        status: "SCREENING",
        reasonForChange: "A valid reason",
      });
      expect(() => gxpLifecycle.validateWrite(subjectSite2)).not.toThrow();

      // Unlocking SITE-101 allows subsequent writes
      gxpLifecycle.unlockSite("SITE-101");
      expect(() => gxpLifecycle.validateWrite(subject)).not.toThrow();
    });

    it("should raise an explicit transaction-level exception under active Entity Lock", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        siteId: "SITE-101",
        status: "SCREENING",
        reasonForChange: "A valid reason",
      });

      gxpLifecycle.lockEntity("SUBJ-001");

      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        ComplianceError
      );
      expect(() => gxpLifecycle.validateWrite(subject)).toThrow(
        "Entity SUBJ-001 is currently locked."
      );

      gxpLifecycle.unlockEntity("SUBJ-001");
      expect(() => gxpLifecycle.validateWrite(subject)).not.toThrow();
    });
  });

  describe("Requirement 3: Subject State Sequential Transitions", () => {
    it("should strictly enforce beginning at the screening stage", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        status: "SCREENING",
      });

      // Starting at SCREENING is valid
      expect(() =>
        gxpLifecycle.guardSubjectTransition(null, "SCREENING")
      ).not.toThrow();

      // Starting at any other state (e.g. ENROLLED, ACTIVE) is blocked
      expect(() =>
        gxpLifecycle.guardSubjectTransition(null, "ENROLLED")
      ).toThrow(InvalidStateTransitionError);
      expect(() => gxpLifecycle.guardSubjectTransition(null, "ACTIVE")).toThrow(
        InvalidStateTransitionError
      );
    });

    it("should allow sequential transitions through defined sequential states", () => {
      // SCREENING -> SCREEN_FAILED is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("SCREENING", "SCREEN_FAILED")
      ).not.toThrow();

      // SCREENING -> ENROLLED is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("SCREENING", "ENROLLED")
      ).not.toThrow();

      // ENROLLED -> RANDOMIZED is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("ENROLLED", "RANDOMIZED")
      ).not.toThrow();

      // RANDOMIZED -> ACTIVE is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("RANDOMIZED", "ACTIVE")
      ).not.toThrow();

      // RANDOMIZED -> WITHDRAWN is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("RANDOMIZED", "WITHDRAWN")
      ).not.toThrow();

      // RANDOMIZED -> UNBLINDED is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("RANDOMIZED", "UNBLINDED")
      ).not.toThrow();

      // ACTIVE -> COMPLETED is allowed
      expect(() =>
        gxpLifecycle.guardSubjectTransition("ACTIVE", "COMPLETED")
      ).not.toThrow();

      // Same state is always allowed (idempotency check)
      expect(() =>
        gxpLifecycle.guardSubjectTransition("SCREENING", "SCREENING")
      ).not.toThrow();
    });

    it("should block invalid state transitions that violate the permitted map", () => {
      // SCREENING -> ACTIVE directly is invalid (must go through ENROLLED and RANDOMIZED first)
      expect(() =>
        gxpLifecycle.guardSubjectTransition("SCREENING", "ACTIVE")
      ).toThrow(InvalidStateTransitionError);

      // SCREEN_FAILED -> ENROLLED is invalid
      expect(() =>
        gxpLifecycle.guardSubjectTransition("SCREEN_FAILED", "ENROLLED")
      ).toThrow(InvalidStateTransitionError);

      // WITHDRAWN -> ACTIVE is invalid
      expect(() =>
        gxpLifecycle.guardSubjectTransition("WITHDRAWN", "ACTIVE")
      ).toThrow(InvalidStateTransitionError);
    });
  });

  describe("Requirement 4: Stratification Factor Immutability Post-Randomization", () => {
    it("should permit modifying stratification factors in pre-randomization states (SCREENING/ENROLLED)", () => {
      const originalSubject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        status: "SCREENING",
        stratFactors: { cohort: "COHORT-A" },
      });

      const updatedSubject = originalSubject.clone();
      updatedSubject.stratFactors = { cohort: "COHORT-B" };
      updatedSubject.reasonForChange = "Updating screening stratification";

      // Mutating factors in SCREENING state is fully allowed
      expect(() =>
        gxpLifecycle.validateWrite(updatedSubject, originalSubject)
      ).not.toThrow();
    });

    it("should permanently lock stratification factors once subject reaches RANDOMIZED or later", () => {
      const originalSubject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        status: "RANDOMIZED",
        stratFactors: { age: "GE_65", gender: "F" },
      });

      const updatedSubject = originalSubject.clone();
      updatedSubject.stratFactors = { age: "LT_65", gender: "F" };
      updatedSubject.reasonForChange = "Attempting post-randomization update";

      // Mutating factors in post-randomized state is blocked
      expect(() =>
        gxpLifecycle.validateWrite(updatedSubject, originalSubject)
      ).toThrow(LockedFactorMutationError);
    });

    it("should permit setting identical stratification factors post-randomization (idempotency check)", () => {
      const originalSubject = new ClinicalSubject({
        subjectId: "SUBJ-001",
        status: "RANDOMIZED",
        stratFactors: { age: "GE_65", gender: "F" },
      });

      const updatedSubject = originalSubject.clone();
      // Re-setting the exact same factors must succeed
      updatedSubject.stratFactors = { age: "GE_65", gender: "F" };
      updatedSubject.reasonForChange = "Idempotent write check";

      expect(() =>
        gxpLifecycle.validateWrite(updatedSubject, originalSubject)
      ).not.toThrow();
    });
  });

  describe("Workflow: Subject Emergency Unblinding & Withdrawal Behavior", () => {
    it("should successfully capture details and execute unblinding and withdrawal transitions", () => {
      const subject = new ClinicalSubject({
        subjectId: "SUBJ-999",
        status: "SCREENING",
      });

      // Step 1: Transition to ENROLLED then RANDOMIZED
      subject.status = "ENROLLED";
      subject.randomize("RAND-101", "KIT-XYZ", { cohort: "A" });
      expect(subject.status).toBe("RANDOMIZED");
      expect(subject.randomizationId).toBe("RAND-101");
      expect(subject.kitReference).toBe("KIT-XYZ");

      // Step 2: Emergency Unblinding transition
      subject.unblind("doctor_smith", "Severe Adverse Event");
      expect(subject.status).toBe("UNBLINDED");
      expect(subject.isUnblinded).toBe(true);
      expect(subject.unblindedBy).toBe("doctor_smith");
      expect(subject.unblindedReason).toBe("Severe Adverse Event");
      expect(subject.unblindedAt).toBeInstanceOf(Date);

      // Step 3: Subject withdrawal transition
      subject.withdraw("Subject withdrew consent");
      expect(subject.status).toBe("WITHDRAWN");
      expect(subject.withdrawalReason).toBe("Subject withdrew consent");
      expect(subject.withdrawnAt).toBeInstanceOf(Date);
    });
  });
});
