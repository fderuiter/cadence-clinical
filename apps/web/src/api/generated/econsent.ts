/**
 * Generated from docs/openapi/econsent_openapi.json by scripts/generate-api-types.js.
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
    /** eConsent service health probe. */
    get: operations["EconsentHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns eConsent operational status. */
    get: operations["EconsentStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/versions/{version_index}/publish": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["PublishTemplateVersioned"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/publish": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["PublishTemplate"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/approved-content": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetApprovedConsentContent"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/versions/{version_index}/comprehension-checks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetComprehensionChecks"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/versions/{version_index}/submit-answers": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["SubmitComprehensionAnswers"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CaptureConsentVersioned"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/templates/{template_id}/capture-consent": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CaptureConsentLegacy"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/reconsent/pending/{study_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetPendingReconsents"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/econsent/reconsent/complete/{requirement_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["CompleteReconsentRequirement"];
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
    Econsent_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Econsent_ApprovedConsentClauseDto: {
      id: string;
      clause_id: string;
      title: string;
      text: string;
      content: string;
      /** Format: int32 */
      version_index: number | string;
    };
    Econsent_ApprovedConsentContentResponse: {
      template_id: string;
      template_name: string;
      study_id: string;
      site_id: null | string;
      protocol_version: string;
      /** Format: int32 */
      version_index: number | string;
      language_code: string;
      requires_reconsent: boolean;
      digest_sha256: string;
      clauses: components["schemas"]["Econsent_ApprovedConsentClauseDto"][];
      workflow_steps: components["schemas"]["Econsent_JsonElement"];
    };
    Econsent_CaptureConsentRequest: {
      subject_pseudonym: null | string;
      study_id: null | string;
      site_id: null | string;
      signer_name: null | string;
      signer_email: null | string;
      signer_role: null | string;
      meaning: null | string;
      signing_reason: null | string;
      protocol_version: null | string;
      digest_sha256: null | string;
      signature_data: null | string;
      liveness_token: null | string;
      /** Format: date-time */
      device_timestamp: null | string;
    };
    Econsent_CaptureConsentResponse: {
      id: string;
      template_id: string;
      /** Format: int32 */
      version_index: number | string;
      subject_pseudonym: string;
      signer_name: string;
      signer_role: string;
      meaning: string;
      /** Format: date-time */
      server_timestamp: string;
      status: string;
      sha256_hash: string;
    };
    Econsent_CompleteReconsentRequest: {
      subject_pseudonym: null | string;
      study_id: null | string;
      site_id: null | string;
      template_id: null | string;
      /** Format: int32 */
      version_index: null | number | string;
      protocol_version: null | string;
      signer_name: null | string;
      signer_email: null | string;
      signer_role: null | string;
      meaning: null | string;
      signing_reason: null | string;
      comprehension_answers: null | {
        [key: string]: string;
      };
      digest_sha256: null | string;
      signature_data: null | string;
      /** Format: date-time */
      device_timestamp: null | string;
    };
    Econsent_CompleteReconsentResponse: {
      id: string;
      requirement_id: string;
      completed_consent_id: string;
      template_id: string;
      /** Format: int32 */
      version_index: number | string;
      protocol_version: string;
      subject_pseudonym: string;
      status: string;
      /** Format: date-time */
      server_timestamp: string;
      sha256_hash: string;
    };
    Econsent_ComprehensionCheckResponse: {
      template_id: string;
      /** Format: int32 */
      version_index: number | string;
      questions: components["schemas"]["Econsent_JsonElement"];
      threshold_policy: components["schemas"]["Econsent_JsonElement"];
    };
    Econsent_HttpValidationProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
      errors?: {
        [key: string]: string[];
      };
    };
    Econsent_JsonElement: unknown;
    Econsent_PendingReconsentItemDto: {
      id: string;
      requirement_id: string;
      study_id: string;
      site_id: null | string;
      template_id: string;
      /** Format: int32 */
      prior_version_index: number | string;
      /** Format: int32 */
      new_version_index: number | string;
      /** Format: int32 */
      version_index: number | string;
      protocol_version: string;
      subject_pseudonym: string;
      status: string;
      change_summary: string;
      /** Format: date-time */
      deadline_at: null | string;
      clause_diffs: components["schemas"]["Econsent_JsonElement"];
      comprehension_questions: components["schemas"]["Econsent_JsonElement"];
    };
    Econsent_PendingReconsentListResponse: {
      study_id: string;
      subject_pseudonym: string;
      pending_items: components["schemas"]["Econsent_PendingReconsentItemDto"][];
    };
    Econsent_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Econsent_PublishTemplateRequest: {
      study_id: null | string;
      protocol_version: null | string;
      change_summary: null | string;
      change_reason: null | string;
      requires_reconsent: null | boolean;
      channels: null | string[];
    };
    Econsent_PublishTemplateResponse: {
      template_id: string;
      /** Format: int32 */
      version_index: number | string;
      is_published: boolean;
      requires_reconsent: boolean;
      /** Format: date-time */
      published_at: string;
    };
    Econsent_SubmitComprehensionAnswersRequest: {
      subject_pseudonym: null | string;
      submitted_answers: null | {
        [key: string]: string;
      };
      reason_for_change: null | string;
    };
    Econsent_SubmitComprehensionAnswersResponse: {
      id: string;
      passed: boolean;
      /** Format: double */
      score: number | string;
      /** Format: int32 */
      correct_count: number | string;
      /** Format: int32 */
      total_questions: number | string;
      next_step: string;
      message: string;
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
  EconsentHealthCheck: {
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
          "application/json": components["schemas"]["Econsent_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  EconsentStatus: {
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
          "application/json": components["schemas"]["Econsent_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  PublishTemplateVersioned: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
        version_index: number | string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Econsent_PublishTemplateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_PublishTemplateResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  PublishTemplate: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Econsent_PublishTemplateRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_PublishTemplateResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  GetApprovedConsentContent: {
    parameters: {
      query?: {
        language_code?: string;
      };
      header?: never;
      path: {
        template_id: string;
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
          "application/json": components["schemas"]["Econsent_ApprovedConsentContentResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  GetComprehensionChecks: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
        version_index: number | string;
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
          "application/json": components["schemas"]["Econsent_ComprehensionCheckResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  SubmitComprehensionAnswers: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
        version_index: number | string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Econsent_SubmitComprehensionAnswersRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_SubmitComprehensionAnswersResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_HttpValidationProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  CaptureConsentVersioned: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
        version_index: number | string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Econsent_CaptureConsentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_CaptureConsentResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  CaptureConsentLegacy: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        template_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Econsent_CaptureConsentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_CaptureConsentResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  GetPendingReconsents: {
    parameters: {
      query?: {
        subject_pseudonym?: string;
      };
      header?: never;
      path: {
        study_id: string;
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
          "application/json": components["schemas"]["Econsent_PendingReconsentListResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
  CompleteReconsentRequirement: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        requirement_id: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Econsent_CompleteReconsentRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Econsent_CompleteReconsentResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_HttpValidationProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Econsent_ProblemDetails"];
        };
      };
    };
  };
}
