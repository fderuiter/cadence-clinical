/**
 * Generated from docs/openapi/interop_openapi.json by scripts/generate-api-types.js.
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
    /** Interoperability FHIR & CDISC connector health probe. */
    get: operations["InteropHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Interoperability operational status. */
    get: operations["InteropStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/endpoints": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListEhrEndpoints"];
    put?: never;
    post: operations["RegisterEhrEndpoint"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/smart/assertion": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["GenerateClientAssertion"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/smart/verify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["VerifyClientAssertion"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/poll": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["PollHospitalEhr"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/ingest": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["IngestFhirBundle"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/candidates": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["ListCandidates"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/candidates/{candidate_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetCandidate"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/reconcile/accept": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["AcceptCandidate"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/reconcile/batch-accept": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchAcceptCandidates"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/reconcile/reject": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["RejectCandidate"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/reconcile/batch-reject": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchRejectCandidates"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/reconcile/query": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["RaiseClinicalQuery"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/esource/provenance/{candidate_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetProvenanceAudit"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/programs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query: {
          study_id: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody: {
        content: {
          "application/json": components["schemas"]["Interop_CreateProgramRequestDto"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/programs/{program_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          program_id: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/validate-lockfile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody: {
        content: {
          "application/json": components["schemas"]["Interop_ValidateLockfileRequestDto"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/lock-tokens": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query: {
          study_id: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody: {
        content: {
          "application/json": components["schemas"]["Interop_IssueLockTokenRequestDto"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/lock-tokens/{token_id}/revoke": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: {
          reason?: string;
        };
        header?: never;
        path: {
          token_id: string;
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
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/execute": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path?: never;
        cookie?: never;
      };
      requestBody: {
        content: {
          "application/json": components["schemas"]["Interop_ExecuteProgramRequestDto"];
        };
      };
      responses: {
        /** @description OK */
        200: {
          headers: {
            [name: string]: unknown;
          };
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query: {
          study_id: string;
          program_id?: string;
          snapshot_id?: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/runs/{run_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          run_id: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/runs/{run_id}/verify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          run_id: string;
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
          content?: never;
        };
      };
    };
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/interop/sce/runs/{run_id}/artifacts/{artifact_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: {
      parameters: {
        query?: never;
        header?: never;
        path: {
          run_id: string;
          artifact_id: string;
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
          content?: never;
        };
      };
    };
    put?: never;
    post?: never;
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
    Interop_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Interop_CreateProgramRequestDto: {
      study_id: string;
      name: string;
      title: string;
      description?: null | string;
      /** @default PYTHON */
      language: string;
      /** @default 1.0.0 */
      version: string;
      /** @default  */
      source_code: string;
      lockfile_content?: null | string;
      /** @default UV_LOCK */
      lock_type: string;
      target_domain?: null | string;
      /** @default Initial program authoring */
      reason_for_change: string;
    };
    Interop_EhrCandidateItemDto: {
      id: string;
      studyId: string;
      siteId: string;
      subjectId: string;
      subjectPseudonym: string;
      ehrSystemId: string;
      fhirResourceType: string;
      fhirResourceId: string;
      /** Format: date-time */
      sourceTimestamp: string;
      targetDomain: string;
      targetVariable: string;
      cdashTestcd: null | string;
      cdashTest: null | string;
      candidateValue: unknown;
      candidateUnit: null | string;
      currentEcrfValue: unknown;
      /** Format: date-time */
      currentEcrfUpdatedAt: null | string;
      discrepancyType: string;
      /** Format: double */
      confidenceScore: number | string;
      mappingProvenance: string;
      rawFhirResource: unknown;
      status: string;
      resolutionAction: null | string;
      resolutionReason: null | string;
      resolvedBy: null | string;
      /** Format: date-time */
      resolvedAt: null | string;
      electronicSignature: unknown;
      clinicalQueryId: null | string;
      /** Format: date-time */
      createdAt: string;
      /** Format: int32 */
      versionIndex: number | string;
    };
    Interop_EhrCandidateListResponse: {
      candidates: components["schemas"]["Interop_EhrCandidateItemDto"][];
      /** Format: int32 */
      totalCount: number | string;
    };
    Interop_EhrEndpointCreateRequest: {
      name: string;
      systemType: null | string;
      fhirBaseUrl: string;
      tokenUrl: null | string;
      clientId: null | string;
      publicKeyJwksUrl: null | string;
      scopes: null | string[];
      /** Format: int32 */
      pollingIntervalSec: number | string;
      reasonForChange: null | string;
      signingKeyRef?: null | string;
      signingKeyId?: null | string;
    };
    Interop_EhrEndpointListResponse: {
      endpoints: components["schemas"]["Interop_EhrEndpointRecord"][];
    };
    Interop_EhrEndpointRecord: {
      id?: string;
      name?: string;
      systemType?: string;
      fhirBaseUrl?: string;
      tokenUrl?: string;
      clientId?: string;
      publicKeyJwksUrl?: null | string;
      signingKeyRef?: null | string;
      signingKeyId?: null | string;
      scopesJson?: string;
      /** Format: int32 */
      pollingIntervalSec?: number | string;
      isActive?: boolean;
      /** Format: date-time */
      lastPolledAt?: null | string;
      /** Format: date-time */
      createdAt: string;
      createdBy: string;
      reasonForChange?: string;
      /** Format: int32 */
      versionIndex?: number | string;
    };
    Interop_EhrPollRequest: {
      endpointId: null | string;
      studyId: string;
      siteId: null | string;
      subjectId: string;
    };
    Interop_EhrProvenanceResponse: {
      candidateId: string;
      studyId: string;
      siteId: string;
      subjectId: string;
      subjectPseudonym: string;
      ehrSystemId: string;
      fhirResourceType: string;
      fhirResourceId: string;
      /** Format: date-time */
      sourceTimestamp: null | string;
      targetDomain: string;
      targetVariable: string;
      cdashTestcd: null | string;
      cdashTest: null | string;
      candidateValue: unknown;
      candidateUnit: null | string;
      currentEcrfValue: unknown;
      /** Format: date-time */
      currentEcrfUpdatedAt: null | string;
      discrepancyType: string;
      /** Format: double */
      confidenceScore: number | string;
      mappingProvenance: string;
      rawFhirResource: unknown;
      status: string;
      resolutionAction: null | string;
      resolutionReason: null | string;
      resolvedBy: null | string;
      /** Format: date-time */
      resolvedAt: null | string;
      electronicSignature: unknown;
      clinicalQueryId: null | string;
      /** Format: date-time */
      createdAt: string;
      /** Format: int32 */
      versionIndex: number | string;
      auditTrail: components["schemas"]["Interop_InteropAuditLog"][];
    };
    Interop_EhrReconcileAcceptRequest: {
      candidateId: string;
      commitReason: null | string;
      changeReason: null | string;
    };
    Interop_EhrReconcileActionResponse: {
      success: boolean;
      candidate: components["schemas"]["Interop_EhrCandidateItemDto"];
      clinicalQueryId: null | string;
      message: string;
    };
    Interop_EhrReconcileBatchAcceptRequest: {
      candidateIds: string[];
      commitReason: null | string;
      changeReason: null | string;
    };
    Interop_EhrReconcileBatchActionResponse: {
      success: boolean;
      /** Format: int32 */
      actionCount: number | string;
      candidates: components["schemas"]["Interop_EhrCandidateItemDto"][];
      message: string;
    };
    Interop_EhrReconcileBatchRejectRequest: {
      candidateIds: string[];
      rejectionReason: string;
      changeReason: null | string;
    };
    Interop_EhrReconcileQueryRequest: {
      candidateId: string;
      queryExplanation: string;
      discrepancyType: null | string;
      changeReason: null | string;
    };
    Interop_EhrReconcileRejectRequest: {
      candidateId: string;
      rejectionReason: string;
      changeReason: null | string;
    };
    Interop_ExecuteProgramRequestDto: {
      study_id: string;
      program_id: string;
      snapshot_id: string;
      /** @default false */
      is_unblinded: boolean;
      database_lock_token_id?: null | string;
      sandbox_config?:
        null | components["schemas"]["Interop_ExecutionSandboxConfigDto"];
      /** @default Statistical analysis execution run */
      reason_for_execution: string;
    };
    Interop_ExecutionSandboxConfigDto: {
      /**
       * Format: int32
       * @default 2048
       */
      max_memory_mb: number | string;
      /**
       * Format: int32
       * @default 300
       */
      timeout_seconds: number | string;
      /** @default true */
      network_isolated: boolean;
      /** @default true */
      filesystem_readonly_root: boolean;
      /**
       * Format: int64
       * @default 10485760
       */
      max_output_bytes: number | string;
      /** @default sha256:cadence-sce-r4.4-py3.14-validated-base-2026 */
      docker_image_digest: string;
      /** @default 3.14.0 */
      python_version: string;
      /** @default 4.4.2 */
      r_version: string;
    };
    Interop_FhirBundleIngestRequest: {
      studyId: string;
      siteId: null | string;
      subjectId: string;
      subjectPseudonym: null | string;
      ehrSystemId: null | string;
      rawFhirBundleJson: string;
    };
    Interop_FhirBundleIngestResponse: {
      success: boolean;
      /** Format: int32 */
      candidateCount: number | string;
      candidates: components["schemas"]["Interop_EhrCandidateItemDto"][];
      message: string;
    };
    Interop_InteropAuditLog: {
      id?: string;
      /** Format: date-time */
      timestamp: string;
      userId?: string;
      userRole?: string;
      action?: string;
      details?: string;
      changeReason?: null | string;
    };
    Interop_IssueLockTokenRequestDto: {
      study_id: string;
      lock_id: string;
      milestone: string;
      issued_to: string;
      reason: string;
      /**
       * Format: double
       * @default 24
       */
      expires_in_hours: number | string;
    };
    Interop_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Interop_SmartAssertionRequest: {
      endpointId: string;
      /**
       * Format: int32
       * @default 300
       */
      expirationSeconds: number | string;
    };
    Interop_SmartAssertionResponse: {
      clientAssertionToken: string;
      clientAssertionType: string;
      /** Format: date-time */
      expiresAt: string;
    };
    Interop_SmartVerifyRequest: {
      clientAssertionToken: string;
      endpointId: string;
    };
    Interop_SmartVerifyResponse: {
      isValid: boolean;
      subject: string;
      issuer: string;
      audience: string;
      /** Format: date-time */
      expiresAt: string;
      errorMessage: null | string;
    };
    Interop_ValidateLockfileRequestDto: {
      lock_type: string;
      content: string;
      expected_digest?: null | string;
      /** @default false */
      strict_qualification: boolean;
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
  InteropHealthCheck: {
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
          "application/json": components["schemas"]["Interop_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  InteropStatus: {
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
          "application/json": components["schemas"]["Interop_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  ListEhrEndpoints: {
    parameters: {
      query: {
        active_only: boolean;
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
          "application/json": components["schemas"]["Interop_EhrEndpointListResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  RegisterEhrEndpoint: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrEndpointCreateRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrEndpointRecord"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  GenerateClientAssertion: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_SmartAssertionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_SmartAssertionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  VerifyClientAssertion: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_SmartVerifyRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_SmartVerifyResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  PollHospitalEhr: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrPollRequest"];
      };
    };
    responses: {
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Implemented */
      501: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  IngestFhirBundle: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_FhirBundleIngestRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_FhirBundleIngestResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  ListCandidates: {
    parameters: {
      query: {
        study_id?: string;
        subject_id?: string;
        site_id?: string;
        domain?: string;
        status?: string;
        skip: number | string;
        limit: number | string;
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
          "application/json": components["schemas"]["Interop_EhrCandidateListResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  GetCandidate: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        candidate_id: string;
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
          "application/json": components["schemas"]["Interop_EhrCandidateItemDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  AcceptCandidate: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrReconcileAcceptRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrReconcileActionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  BatchAcceptCandidates: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrReconcileBatchAcceptRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrReconcileBatchActionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  RejectCandidate: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrReconcileRejectRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrReconcileActionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  BatchRejectCandidates: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrReconcileBatchRejectRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrReconcileBatchActionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  RaiseClinicalQuery: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Interop_EhrReconcileQueryRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_EhrReconcileActionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
  GetProvenanceAudit: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        candidate_id: string;
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
          "application/json": components["schemas"]["Interop_EhrProvenanceResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Interop_ProblemDetails"];
        };
      };
    };
  };
}
