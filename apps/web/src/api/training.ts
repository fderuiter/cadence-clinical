import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/knowledge";

type Schemas = components["schemas"];

export type TrainingCurriculum = Schemas["Knowledge_TrainingCurriculumDto"];
export type TrainingAssignment = Schemas["Knowledge_TrainingAssignmentDto"];
export type DelegationSummary = Schemas["Knowledge_DelegationSummaryDto"];
export type CreateTrainingCurriculumPayload =
  Schemas["Knowledge_CreateTrainingCurriculumRequest"];
export type CreateTrainingModulePayload =
  Schemas["Knowledge_CreateTrainingModuleRequest"];
export type CompleteTrainingAssignmentPayload =
  Schemas["Knowledge_CompleteTrainingAssignmentRequest"];

export interface CurriculumQueryParams {
  study_id?: string;
  protocol_version?: string;
}

export interface AssignmentQueryParams {
  study_id?: string;
  site_id?: string;
  user_id?: string;
  status?: string;
}

export interface BatchAssignCurriculumPayload {
  curriculum_id: string;
  study_id: string;
  site_ids?: string[] | null;
  roles?: string[] | null;
  target_personnel_ids?: string[] | null;
  reason_for_change?: string | null;
}

export interface DelegationQueryParams {
  study_id?: string;
  site_id?: string;
  status?: string;
}

export interface BulkDelegationSignOffPayload {
  delegation_ids: string[];
  study_id?: string | null;
  site_id?: string | null;
  /** The signer is the signed-in user; the credential check is the step-up token (ADR-2515). */
  signature_payload: {
    meaning: string;
    reason_for_change?: string | null;
  };
}

const client = createOpenApiClient<paths>();

/**
 * Typed client for the .NET Knowledge training & delegation service,
 * generated from docs/openapi/knowledge_openapi.json.
 */
export const trainingApi = {
  /**
   * Fetch active training curricula for a study and protocol version (GET /api/v1/training/curricula).
   */
  listCurricula(
    params: CurriculumQueryParams = {}
  ): Promise<TrainingCurriculum[]> {
    return unwrapResponse(
      client.GET("/api/v1/training/curricula", {
        params: {
          query: {
            study_id: params.study_id,
            protocol_version: params.protocol_version,
          },
        },
      })
    );
  },

  /**
   * Publish a new protocol training curriculum with modules (POST /api/v1/training/curricula).
   */
  createCurriculum(
    payload: CreateTrainingCurriculumPayload
  ): Promise<TrainingCurriculum> {
    const headers: Record<string, string> = {};
    if (payload.reason_for_change) {
      headers["X-Change-Reason"] = encodeChangeReason(
        payload.reason_for_change
      );
    }
    return unwrapResponse(
      client.POST("/api/v1/training/curricula", {
        body: payload,
        headers,
      })
    );
  },

  /**
   * Fetch specific curriculum details (GET /api/v1/training/curricula/{curriculum_id}).
   */
  getCurriculum(curriculumId: string): Promise<TrainingCurriculum> {
    return unwrapResponse(
      client.GET("/api/v1/training/curricula/{curriculum_id}", {
        params: { path: { curriculum_id: curriculumId } },
      })
    );
  },

  /**
   * Synchronous batch assignment of curriculum to site personnel (POST /api/v1/training/curricula/batch-assign).
   */
  batchAssignCurriculum(
    payload: BatchAssignCurriculumPayload
  ): Promise<Schemas["Knowledge_BatchAssignmentResponse"]> {
    const headers: Record<string, string> = {};
    if (payload.reason_for_change) {
      headers["X-Change-Reason"] = encodeChangeReason(
        payload.reason_for_change
      );
    }

    return unwrapResponse(
      client.POST("/api/v1/training/curricula/batch-assign", {
        body: {
          curriculum_id: payload.curriculum_id,
          study_id: payload.study_id,
          site_ids: payload.site_ids ?? null,
          roles: payload.roles ?? null,
          target_personnel_ids: payload.target_personnel_ids ?? null,
          reason_for_change: payload.reason_for_change ?? null,
        },
        headers,
      })
    );
  },

  /**
   * List training assignments for a user, site, or study (GET /api/v1/training/assignments).
   */
  listAssignments(
    params: AssignmentQueryParams = {}
  ): Promise<TrainingAssignment[]> {
    return unwrapResponse(
      client.GET("/api/v1/training/assignments", {
        params: {
          query: {
            study_id: params.study_id,
            site_id: params.site_id,
            user_id: params.user_id,
            status: params.status,
          },
        },
      })
    );
  },

  /**
   * Retrieve details of a training assignment (GET /api/v1/training/assignments/{assignment_id}).
   */
  getAssignmentDetail(assignmentId: string): Promise<TrainingAssignment> {
    return unwrapResponse(
      client.GET("/api/v1/training/assignments/{assignment_id}", {
        params: { path: { assignment_id: assignmentId } },
      })
    );
  },

  /**
   * Record staff completion of a training assignment (POST /api/v1/training/assignments/{assignment_id}/complete).
   */
  completeAssignment(
    assignmentId: string,
    payload: CompleteTrainingAssignmentPayload
  ): Promise<TrainingAssignment> {
    const headers: Record<string, string> = {};
    if (payload.reason_for_change) {
      headers["X-Change-Reason"] = encodeChangeReason(
        payload.reason_for_change
      );
    }
    return unwrapResponse(
      client.POST("/api/v1/training/assignments/{assignment_id}/complete", {
        params: { path: { assignment_id: assignmentId } },
        body: payload,
        headers,
      })
    );
  },

  /**
   * List site delegation records (GET /api/v1/training/delegations).
   */
  listDelegations(
    params: DelegationQueryParams = {}
  ): Promise<DelegationSummary[]> {
    return unwrapResponse(
      client.GET("/api/v1/training/delegations", {
        params: {
          query: {
            study_id: params.study_id,
            site_id: params.site_id,
            status: params.status,
          },
        },
      })
    );
  },

  /**
   * Execute 21 CFR Part 11 bulk delegation sign-off (POST /api/v1/training/delegations/bulk-signoff).
   */
  bulkSignOffDelegations(
    payload: BulkDelegationSignOffPayload,
    sigToken: string
  ): Promise<Schemas["Knowledge_BulkDelegationSignOffResponse"]> {
    const headers: Record<string, string> = {
      "X-Sig-Token": sigToken,
    };
    if (payload.signature_payload.reason_for_change) {
      headers["X-Change-Reason"] = encodeChangeReason(
        payload.signature_payload.reason_for_change
      );
    }

    return unwrapResponse(
      client.POST("/api/v1/training/delegations/bulk-signoff", {
        body: {
          delegation_ids: payload.delegation_ids,
          study_id: payload.study_id ?? null,
          site_id: payload.site_id ?? null,
          signature_payload: {
            meaning: payload.signature_payload.meaning,
            reason_for_change:
              payload.signature_payload.reason_for_change ?? null,
          },
        },
        headers,
      })
    );
  },
};
