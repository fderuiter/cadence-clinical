/**
 * Generated from docs/openapi/knowledge_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/healthz": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Knowledge base and study training health probe. */
    get: operations["KnowledgeHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/knowledge/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Knowledge base operational status. */
    get: operations["KnowledgeStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/curricula": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListCurricula"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/curricula/{curriculum_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetCurriculum"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/curricula/batch-assign": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchAssignCurriculum"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/assignments": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListAssignments"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/assignments/{assignment_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetAssignmentDetail"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/delegations": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListDelegations"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/training/delegations/bulk-signoff": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BulkSignOffDelegations"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    Knowledge_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Knowledge_BatchAssignCurriculumRequest: {
      curriculum_id: string;
      study_id: string;
      site_ids: null | string[];
      roles: null | string[];
      target_personnel_ids: null | string[];
      reason_for_change: null | string;
    };
    Knowledge_BatchAssignmentResponse: {
      batch_transaction_id: string;
      curriculum_id: string;
      study_id: string;
      /** Format: int32 */
      total_eligible: number | string;
      /** Format: int32 */
      total_assigned: number | string;
      /** Format: int32 */
      total_skipped: number | string;
      assignments: components["schemas"]["Knowledge_TrainingAssignmentDto"][];
      skipped_items: components["schemas"]["Knowledge_SkippedAssignmentItemDto"][];
      /** Format: date-time */
      executed_at: string;
    };
    Knowledge_BulkDelegationSignOffRequest: {
      delegation_ids: string[];
      study_id: null | string;
      site_id: null | string;
      signature_payload: components["schemas"]["Knowledge_SignaturePayloadDto"];
    };
    Knowledge_BulkDelegationSignOffResponse: {
      batch_transaction_id: string;
      /** Format: int32 */
      total_requested: number | string;
      /** Format: int32 */
      total_signed: number | string;
      /** Format: int32 */
      total_failed: number | string;
      signed_delegations: components["schemas"]["Knowledge_DelegationSummaryDto"][];
      failed_items: components["schemas"]["Knowledge_FailedDelegationItemDto"][];
      sha256_seal: string;
      etmf_artifact_id: string;
      /** Format: date-time */
      executed_at: string;
    };
    Knowledge_DelegationSummaryDto: {
      id: string;
      study_id: string;
      site_id: string;
      staff_user_id: string;
      staff_name: string;
      role: string;
      task_code: string;
      duties: string;
      status: string;
      signed_off: boolean;
      pi_signature_hash: null | string;
      /** Format: date-time */
      pi_approved_at: null | string;
      batch_transaction_id: null | string;
    };
    Knowledge_FailedDelegationItemDto: {
      delegation_id: string;
      reason: string;
    };
    Knowledge_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Knowledge_SignaturePayloadDto: {
      meaning: string;
      reason_for_change: null | string;
    };
    Knowledge_SkippedAssignmentItemDto: {
      user_id: string;
      site_id: string;
      user_role: string;
      reason: string;
    };
    Knowledge_TrainingAssignmentDto: {
      id: string;
      curriculum_id: string;
      study_id: string;
      site_id: string;
      user_id: string;
      user_name: string;
      user_role: string;
      status: string;
      batch_transaction_id: null | string;
      /** Format: double */
      quiz_score: null | number | string;
      /** Format: int32 */
      time_spent_seconds: number | string;
      /** Format: date-time */
      completed_at: null | string;
      /** Format: date-time */
      created_at: string;
    };
    Knowledge_TrainingCurriculumDto: {
      id: string;
      study_id: string;
      protocol_version: string;
      title: string;
      description: null | string;
      is_active: boolean;
      /** Format: date-time */
      created_at: string;
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
  KnowledgeHealthCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  KnowledgeStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  ListCurricula: {
    parameters: {
      query?: {
        study_id?: string;
        protocol_version?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_TrainingCurriculumDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  GetCurriculum: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        curriculum_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_TrainingCurriculumDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  BatchAssignCurriculum: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Knowledge_BatchAssignCurriculumRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_BatchAssignmentResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  ListAssignments: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        user_id?: string;
        status?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_TrainingAssignmentDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  GetAssignmentDetail: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        assignment_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_TrainingAssignmentDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  ListDelegations: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        status?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_DelegationSummaryDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
  BulkSignOffDelegations: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Knowledge_BulkDelegationSignOffRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Knowledge_BulkDelegationSignOffResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Knowledge_ProblemDetails"];
        };
      };
    };
  };
}
