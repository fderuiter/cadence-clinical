import { describe, it, expect } from "vitest";
import {
  normalizeApprovedConsent,
  shapeComprehensionAnswers,
  interpretComprehensionResult,
} from "../index.js";

describe("eConsent Presentation and Gating Utilities", () => {
  describe("normalizeApprovedConsent", () => {
    it("should return empty array if content is null or undefined", () => {
      expect(normalizeApprovedConsent(null)).toEqual([]);
      expect(normalizeApprovedConsent(undefined)).toEqual([]);
    });

    it("should correctly normalize approved composed template content", () => {
      const mockContent = {
        template_id: "tpl-123",
        template_name: "Informed Consent Form",
        study_id: "study-456",
        protocol_version: "v2.1",
        version_index: 2,
        language_code: "es",
        requires_reconsent: true,
        clauses: [
          {
            clause_id: "clause-risk",
            title: "Riesgos",
            text: "Estos son los riesgos...",
            version_index: 1,
          },
          {
            clause_id: "clause-benefit",
            title: "Beneficios",
            text: "Estos son los beneficios...",
            version_index: 2,
          },
        ],
        workflow_steps: [
          { type: "comprehension_check", question: "Understood?" },
          { type: "signature_placeholder", role: "subject" },
        ],
      };

      const result = normalizeApprovedConsent(mockContent);

      expect(result).toHaveLength(5); // 1 metadata + 2 clauses + 2 workflow steps

      // 1. Metadata Check
      expect(result[0]).toEqual({
        id: "metadata",
        type: "metadata",
        title: "Informed Consent Form",
        metadata: {
          template_id: "tpl-123",
          study_id: "study-456",
          protocol_version: "v2.1",
          version_index: 2,
          language_code: "es",
          requires_reconsent: true,
        },
      });

      // 2. Clauses Check
      expect(result[1]).toEqual({
        id: "clause-risk",
        type: "clause",
        title: "Riesgos",
        content: "Estos son los riesgos...",
        version_index: 1,
      });

      expect(result[2]).toEqual({
        id: "clause-benefit",
        type: "clause",
        title: "Beneficios",
        content: "Estos son los beneficios...",
        version_index: 2,
      });

      // 3. Workflow Steps Check
      expect(result[3]).toEqual({
        id: "workflow-step-0",
        type: "workflow_step",
        title: "Comprehension Check",
        step: { type: "comprehension_check", question: "Understood?" },
      });

      expect(result[4]).toEqual({
        id: "workflow-step-1",
        type: "workflow_step",
        title: "Signature Requirement",
        step: { type: "signature_placeholder", role: "subject" },
      });
    });
  });

  describe("shapeComprehensionAnswers", () => {
    it("should correctly structure payload for submission", () => {
      const answers = { q1: "A", q2: "Yes" };
      const payload = shapeComprehensionAnswers(
        "SUB-101",
        answers,
        "Testing submission"
      );

      expect(payload).toEqual({
        subject_pseudonym: "SUB-101",
        submitted_answers: { q1: "A", q2: "Yes" },
        reason_for_change: "Testing submission",
      });
    });
  });

  describe("interpretComprehensionResult", () => {
    it("should handle null response gracefully and deny gating", () => {
      const res = interpretComprehensionResult(null);
      expect(res).toEqual({
        canSign: false,
        nextStep: "retry_checks",
        message: "No submission response received.",
      });
    });

    it("should deny gating if passed is false", () => {
      const mockResponse = {
        passed: false,
        next_step: "retry_checks",
        message: "Failed comprehension check.",
      };
      const res = interpretComprehensionResult(mockResponse);
      expect(res).toEqual({
        canSign: false,
        nextStep: "retry_checks",
        message: "Failed comprehension check.",
      });
    });

    it("should deny gating if passed is true but next_step is not sign_consent", () => {
      const mockResponse = {
        passed: true,
        next_step: "another_step",
        message: "Step passed, but sign not yet allowed.",
      };
      const res = interpretComprehensionResult(mockResponse);
      expect(res).toEqual({
        canSign: false,
        nextStep: "another_step",
        message: "Step passed, but sign not yet allowed.",
      });
    });

    it("should allow gating if passed is true and next_step is sign_consent", () => {
      const mockResponse = {
        passed: true,
        next_step: "sign_consent",
        message: "Passed comprehension! Please sign.",
      };
      const res = interpretComprehensionResult(mockResponse);
      expect(res).toEqual({
        canSign: true,
        nextStep: "sign_consent",
        message: "Passed comprehension! Please sign.",
      });
    });
  });

  describe("normalizeApprovedConsent - Defensive Guarding & Step Types Parsing", () => {
    it("provides safe fallbacks for missing optional metadata and clause fields without throwing", () => {
      const minimalContent = {
        clauses: [
          null, // skip null clause safely
          {
            // missing optional title and text and version_index
            clause_id: "clause-opt",
          },
        ],
        workflow_steps: [
          null, // skip null step safely
          {
            // missing step_id/type
          },
        ],
      };

      const result = normalizeApprovedConsent(minimalContent);
      expect(result).toHaveLength(3); // 1 metadata + 1 clause + 1 workflow step

      // Metadata fallback check
      expect(result[0]).toEqual({
        id: "metadata",
        type: "metadata",
        title: "Informed Consent Form",
        metadata: {
          template_id: "",
          study_id: "",
          protocol_version: "",
          version_index: null,
          language_code: "",
          requires_reconsent: false,
        },
      });

      // Clause fallback check
      expect(result[1]).toEqual({
        id: "clause-opt",
        type: "clause",
        title: "Untitled Clause",
        content: "",
        version_index: null,
      });

      // Workflow step fallback check
      expect(result[2]).toEqual({
        id: "workflow-step-1", // step 0 is skipped because it's null, index of iteration is 1
        type: "workflow_step",
        title: "Signature Requirement",
        step: {},
      });
    });

    it("correctly handles step_type field when step.type is not specified", () => {
      const contentWithStepTypes = {
        template_name: "Consent Form V3",
        workflow_steps: [
          { step_type: "comprehension_check", question: "All good?" },
          { step_type: "signature_requirement", role: "investigator" },
        ],
      };

      const result = normalizeApprovedConsent(contentWithStepTypes);
      expect(result).toHaveLength(3); // 1 metadata + 2 steps

      expect(result[1]).toEqual({
        id: "workflow-step-0",
        type: "workflow_step",
        title: "Comprehension Check",
        step: { step_type: "comprehension_check", question: "All good?" },
      });

      expect(result[2]).toEqual({
        id: "workflow-step-1",
        type: "workflow_step",
        title: "Signature Requirement",
        step: { step_type: "signature_requirement", role: "investigator" },
      });
    });
  });
});
