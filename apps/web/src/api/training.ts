import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export interface CurriculumQueryParams {
  study_id?: string;
  protocol_version_id?: string;
}

export interface AssignmentQueryParams {
  user_id?: string;
  site_id?: string;
  study_id?: string;
}

export interface GatingStatusQueryParams {
  study_id?: string;
  protocol_version?: string;
  protocol_version_id?: string;
  user_id?: string;
  site_id?: string | null;
  user_role?: string | null;
}

export const trainingApi = {
  /**
   * Fetch active training curricula for a study and protocol version.
   */
  listCurricula(params: CurriculumQueryParams = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/curricula", {
        params: { query: params },
      })
    );
  },

  /**
   * Fetch specific curriculum details including modules and quiz questions.
   */
  getCurriculum(curriculumId: string) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/curricula/{curriculum_id}", {
        params: { path: { curriculum_id: curriculumId } },
      })
    );
  },

  /**
   * Generate delta-training curriculum upon protocol amendment publication.
   */
  generateAmendmentCurriculum(payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/curricula/generate-amendment", {
        body: payload as paths["/api/v1/training/curricula/generate-amendment"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },

  /**
   * Assign a curriculum to a clinical site user.
   */
  assignCurriculum(curriculumId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/curricula/{curriculum_id}/assign", {
        params: { path: { curriculum_id: curriculumId } },
        body: payload as paths["/api/v1/training/curricula/{curriculum_id}/assign"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },

  /**
   * List training assignments for a user, site, or study.
   */
  listAssignments(params: AssignmentQueryParams = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/assignments", {
        params: { query: params },
      })
    );
  },

  /**
   * Retrieve full details of an assignment including modules and quiz questions.
   */
  getAssignmentDetail(assignmentId: string) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/assignments/{assignment_id}", {
        params: { path: { assignment_id: assignmentId } },
      })
    );
  },

  /**
   * Record time-on-page reading duration progress.
   */
  recordProgress(assignmentId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/assignments/{assignment_id}/progress", {
        params: { path: { assignment_id: assignmentId } },
        body: payload as paths["/api/v1/training/assignments/{assignment_id}/progress"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },

  /**
   * Submit comprehension quiz answers and receive score evaluation (>= 80% passing).
   */
  submitQuiz(assignmentId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/assignments/{assignment_id}/submit-quiz", {
        params: { path: { assignment_id: assignmentId } },
        body: payload as paths["/api/v1/training/assignments/{assignment_id}/submit-quiz"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },

  /**
   * Capture 21 CFR Part 11 electronic signature, generate sealed PDF certificate, and push to eTMF & eISF.
   */
  signOffTraining(assignmentId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/assignments/{assignment_id}/sign-off", {
        params: { path: { assignment_id: assignmentId } },
        body: payload as paths["/api/v1/training/assignments/{assignment_id}/sign-off"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },

  /**
   * Retrieve generated completion certificate and SHA-256 seal for an assignment.
   */
  getCertificate(assignmentId: string) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/assignments/{assignment_id}/certificate", {
        params: { path: { assignment_id: assignmentId } },
      })
    );
  },

  /**
   * Check whether a user is qualified for EDC write permissions on a protocol version.
   */
  checkGatingStatus(params: GatingStatusQueryParams = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/gating-status", {
        params: {
          query: {
            user_id: params.user_id || "",
            study_id: params.study_id || "",
            protocol_version: params.protocol_version || params.protocol_version_id || "",
            site_id: params.site_id,
            user_role: params.user_role,
          },
        },
      })
    );
  },

  /**
   * Programmatically verify qualification gate.
   */
  verifyGate(payload: unknown) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/verify-gate", {
        body: payload as paths["/api/v1/training/verify-gate"]["post"]["requestBody"]["content"]["application/json"],
      })
    );
  },
};
