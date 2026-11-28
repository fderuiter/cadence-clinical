import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/knowledge";

type Schemas = components["schemas"];

export type TrainingCurriculum = Schemas["Knowledge_TrainingCurriculumDto"];
export type TrainingModule = Schemas["Knowledge_TrainingModuleDto"];
export type TrainingQuizQuestion = Schemas["Knowledge_TrainingQuizQuestionDto"];
export type TrainingQuizOption = Schemas["Knowledge_TrainingQuizOptionDto"];
export type TrainingAssignment = Schemas["Knowledge_TrainingAssignmentDto"];
export type StudyStaffGrant = Schemas["Knowledge_StudyStaffGrantDto"];
export type CreateTrainingCurriculumPayload =
  Schemas["Knowledge_CreateTrainingCurriculumRequest"];
export type CompleteTrainingAssignmentPayload =
  Schemas["Knowledge_CompleteTrainingAssignmentRequest"];
export type CompleteTrainingAssignmentResponse =
  Schemas["Knowledge_CompleteTrainingAssignmentResponse"];

export interface CurriculumQueryParams {
  study_id?: string;
  protocol_version?: string;
}

export interface StudyStaffQueryParams {
  study_id: string;
  site_id?: string;
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

const client = createOpenApiClient<paths>();

/**
 * Typed client for the .NET Knowledge protocol training & qualification service,
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
   * Author a new protocol training curriculum with modules and verification quiz (POST /api/v1/training/curricula).
   */
  createCurriculum(
    payload: CreateTrainingCurriculumPayload
  ): Promise<TrainingCurriculum> {
    const headers: Record<string, string> = {};
    const reason =
      payload.reason_for_change?.trim() ||
      `Author protocol training curriculum ${payload.title}`;
    headers["X-Change-Reason"] = encodeChangeReason(reason);

    return unwrapResponse(
      client.POST("/api/v1/training/curricula", {
        body: {
          ...payload,
          reason_for_change: reason,
        },
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
   * Synchronous batch assignment of curriculum to site personnel resolved from canonical Kernel grants
   * (POST /api/v1/training/curricula/batch-assign).
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
   * List study staff holding active canonical Kernel site grants (GET /api/v1/training/study-staff).
   */
  listStudyStaff(params: StudyStaffQueryParams): Promise<StudyStaffGrant[]> {
    return unwrapResponse(
      client.GET("/api/v1/training/study-staff", {
        params: {
          query: {
            study_id: params.study_id,
            site_id: params.site_id,
          },
        },
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
   * Complete a personal training assignment with quiz answers and 21 CFR Part 11 step-up signature,
   * filing a signed 05.02.04 PDF certificate in eTMF and recording a bitemporal Kernel Qualification
   * (POST /api/v1/training/assignments/{assignment_id}/complete).
   */
  completeAssignment(
    assignmentId: string,
    payload: CompleteTrainingAssignmentPayload,
    sigToken: string
  ): Promise<CompleteTrainingAssignmentResponse> {
    const headers: Record<string, string> = {
      "X-Sig-Token": sigToken,
    };
    const reason =
      payload.signature_payload.reason_for_change?.trim() ||
      "21 CFR Part 11 protocol training qualification attestation";
    headers["X-Change-Reason"] = encodeChangeReason(reason);

    return unwrapResponse(
      client.POST("/api/v1/training/assignments/{assignment_id}/complete", {
        params: { path: { assignment_id: assignmentId } },
        body: {
          quiz_answers: payload.quiz_answers ?? null,
          time_spent_seconds: payload.time_spent_seconds ?? null,
          signature_payload: {
            meaning: payload.signature_payload.meaning,
            reason_for_change: reason,
          },
        },
        headers,
      })
    );
  },
};
