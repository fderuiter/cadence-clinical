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

export interface BatchAssignCurriculumPayload {
  curriculum_id: string;
  study_id: string;
  site_ids?: string[];
  roles?: string[];
  target_personnel_ids?: string[];
  reason_for_change?: string;
}

export interface BatchAssignmentResponse {
  batch_transaction_id: string;
  curriculum_id: string;
  study_id: string;
  total_eligible: number;
  total_assigned: number;
  total_skipped: number;
  assignments: Array<any>;
  skipped_items: Array<{
    user_id: string;
    site_id: string;
    user_role: string;
    reason: string;
  }>;
  executed_at: string;
}

export interface DelegationQueryParams {
  study_id?: string;
  site_id?: string;
  status?: string;
}

export interface BulkDelegationSignOffPayload {
  delegation_ids: string[];
  study_id?: string;
  site_id?: string;
  signature_payload: {
    signer_name: string;
    signer_role: string;
    meaning: string;
    password: string;
    reason_for_change?: string;
  };
}

export interface BulkDelegationSignOffResponse {
  batch_transaction_id: string;
  total_requested: number;
  total_signed: number;
  total_failed: number;
  signed_delegations: Array<any>;
  failed_items: Array<{ delegation_id: string; reason: string }>;
  sha256_seal: string;
  etmf_artifact_id: string;
  executed_at: string;
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
   * Synchronous batch assignment of curriculum to site personnel.
   */
  batchAssignCurriculum(payload: BatchAssignCurriculumPayload) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/curricula/batch-assign" as any, {
        body: payload as any,
      })
    );
  },

  /**
   * List site delegation records.
   */
  listDelegations(params: DelegationQueryParams = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/training/delegations" as any, {
        params: { query: params },
      })
    );
  },

  /**
   * Execute 21 CFR Part 11 bulk delegation sign-off.
   */
  bulkSignOffDelegations(payload: BulkDelegationSignOffPayload) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/training/delegations/bulk-signoff" as any, {
        body: payload as any,
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
      openApiClient.POST(
        "/api/v1/training/assignments/{assignment_id}/progress",
        {
          params: { path: { assignment_id: assignmentId } },
          body: payload as paths["/api/v1/training/assignments/{assignment_id}/progress"]["post"]["requestBody"]["content"]["application/json"],
        }
      )
    );
  },

  /**
   * Submit comprehension quiz answers and receive score evaluation (>= 80% passing).
   */
  submitQuiz(assignmentId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/training/assignments/{assignment_id}/submit-quiz",
        {
          params: { path: { assignment_id: assignmentId } },
          body: payload as paths["/api/v1/training/assignments/{assignment_id}/submit-quiz"]["post"]["requestBody"]["content"]["application/json"],
        }
      )
    );
  },

  /**
   * Capture 21 CFR Part 11 electronic signature, generate sealed PDF certificate, and push to eTMF & eISF.
   */
  signOffTraining(assignmentId: string, payload: unknown) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/training/assignments/{assignment_id}/sign-off",
        {
          params: { path: { assignment_id: assignmentId } },
          body: payload as paths["/api/v1/training/assignments/{assignment_id}/sign-off"]["post"]["requestBody"]["content"]["application/json"],
        }
      )
    );
  },

  /**
   * Retrieve generated completion certificate and SHA-256 seal for an assignment.
   */
  getCertificate(assignmentId: string) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/training/assignments/{assignment_id}/certificate",
        {
          params: { path: { assignment_id: assignmentId } },
        }
      )
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
            protocol_version:
              params.protocol_version || params.protocol_version_id || "",
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
