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
    /** Lists registered hospital SMART on FHIR endpoints. */
    get: operations["ListEhrEndpoints"];
    put?: never;
    /** Registers a hospital SMART on FHIR endpoint configuration and server-side signing key reference. */
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
    /** Signs a SMART Backend Services OAuth 2.0 JWT client assertion for a registered EHR endpoint. */
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
    /** Verifies a SMART Backend Services client assertion JWT signature, audience, and lifetime. */
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
    /**
     * Polls a hospital EHR endpoint for eSource observations (returns 501 Not Implemented; ingest FHIR bundles via POST /ingest).
     * @description HTTP 501 Not Implemented stub: live outbound SMART on FHIR EHR polling is not wired, so this route validates study/site scope and returns RFC 7807 ProblemDetails (501) without synthesizing candidate records.
     */
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
    /** Maps an exported HL7 FHIR Observation bundle into side-by-side eSource reconciliation candidates. */
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
    /** Lists eSource reconciliation candidates filtered by study, site, subject, domain, or status. */
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
    /** Retrieves a single eSource reconciliation candidate record by ID. */
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
    /** Accepts and commits a hospital eSource candidate value into the eCRF with Part 11 audit trail. */
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
    /** Batch-accepts up to 100 hospital eSource candidate values atomically into the eCRF. */
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
    /** Rejects an eSource candidate value with mandatory GxP clinical rationale. */
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
    /** Batch-rejects up to 100 eSource candidate values with mandatory GxP clinical rationale. */
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
    /** Raises a clinical discrepancy query from an eSource reconciliation candidate. */
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
    /** Retrieves FHIR resource provenance and reconciliation audit lineage for an eSource candidate. */
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
    /** Lists statistical analysis programs registered for a study. */
    get: operations["ListScePrograms"];
    put?: never;
    /** Registers a versioned R or Python statistical program with SHA-256 code and lockfile digests. */
    post: operations["CreateSceProgram"];
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
    /** Retrieves a registered statistical program by ID or program name. */
    get: operations["GetSceProgram"];
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
    /** Validates an R renv.lock or Python uv.lock package lockfile against qualified GxP package digests. */
    post: operations["ValidateSceLockfile"];
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
    /** Lists active database lock authorization tokens for unblinded statistical runs. */
    get: operations["ListSceLockTokens"];
    put?: never;
    /** Issues a signed database lock authorization token for unblinded statistical execution. */
    post: operations["IssueSceLockToken"];
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
    /** Revokes an active database lock authorization token and logs the revocation. */
    post: operations["RevokeSceLockToken"];
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
    /** Executes a statistical program inside an isolated SCE container and seals the run manifest. */
    post: operations["ExecuteSceProgram"];
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
    /** Lists historical statistical execution runs and manifests for a study. */
    get: operations["ListSceRuns"];
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
    /** Retrieves a single statistical execution run manifest by run ID. */
    get: operations["GetSceRun"];
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
    /** Recomputes and verifies the SHA-256 cryptographic seal of an SCE execution run manifest. */
    post: operations["VerifySceRunManifest"];
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
    /** Retrieves metadata, SHA-256 digest, and preview for an output artifact from an SCE run. */
    get: operations["GetSceRunArtifact"];
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
    Interop_DatabaseLockAuthTokenDto: {
      token_id: string;
      study_id: string;
      lock_id: string;
      milestone: string;
      issued_to: string;
      authorized_roles: string[];
      issued_at: string;
      expires_at: string;
      signature_hash: string;
      is_revoked: boolean;
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
    Interop_ElectronicSignatureBlockDto: {
      signer_id: string;
      signer_role: string;
      signed_at: string;
      signature_meaning: string;
      hmac_signature: string;
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
    Interop_ExecuteProgramResponseDto: {
      manifest: components["schemas"]["Interop_ExecutionRunManifestDto"];
    };
    Interop_ExecutionRunDetailResponseDto: {
      run: components["schemas"]["Interop_ExecutionRunManifestDto"];
    };
    Interop_ExecutionRunListResponseDto: {
      study_id: string;
      /** Format: int32 */
      total: number | string;
      runs: components["schemas"]["Interop_ExecutionRunManifestDto"][];
    };
    Interop_ExecutionRunManifestDto: {
      manifest_id: string;
      run_id: string;
      study_id: string;
      program_id: string;
      program_name: string;
      program_version: string;
      snapshot_id: string;
      snapshot_version_tag: string;
      snapshot_digest: string;
      language: string;
      script_code_digest: string;
      lockfile_digest: string;
      container_image_digest: string;
      execution_start: string;
      execution_end: string;
      /** Format: double */
      duration_ms: number | string;
      /** Format: int32 */
      exit_code: number | string;
      status: string;
      stdout_capture: string;
      stderr_capture: string;
      output_artifacts: components["schemas"]["Interop_OutputArtifactSchemaDto"][];
      output_artifacts_digest: string;
      is_unblinded_run: boolean;
      database_lock_token_id: null | string;
      executed_by: string;
      executed_by_role: string;
      signature_block: components["schemas"]["Interop_ElectronicSignatureBlockDto"];
      manifest_sha256_seal: string;
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
    Interop_LockfileValidationResultSchemaDto: {
      lock_type: string;
      status: string;
      lock_digest: string;
      /** Format: int32 */
      package_count: number | string;
      packages: components["schemas"]["Interop_PackageEntrySchemaDto"][];
      is_qualified: boolean;
      unqualified_packages: string[];
      details: string;
    };
    Interop_LockTokenListResponseDto: {
      study_id: string;
      /** Format: int32 */
      total: number | string;
      tokens: components["schemas"]["Interop_DatabaseLockAuthTokenDto"][];
    };
    Interop_LockTokenResponseDto: {
      token: components["schemas"]["Interop_DatabaseLockAuthTokenDto"];
    };
    Interop_OutputArtifactSchemaDto: {
      artifact_id: string;
      name: string;
      artifact_type: string;
      file_path: string;
      mime_type: string;
      /** Format: int64 */
      size_bytes: number | string;
      sha256_digest: string;
      content_preview?: null | string;
    };
    Interop_PackageEntrySchemaDto: {
      name: string;
      version: string;
      checksum?: null | string;
      repository?: null | string;
      /** @default true */
      is_qualified: boolean;
    };
    Interop_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Interop_ProgramListResponseDto: {
      study_id: string;
      /** Format: int32 */
      total: number | string;
      programs: components["schemas"]["Interop_StatisticalProgramDto"][];
    };
    Interop_ProgramResponseDto: {
      program: components["schemas"]["Interop_StatisticalProgramDto"];
    };
    Interop_RevokeLockTokenResponseDto: {
      status: string;
      token_id: string;
      revoked_at: string;
      reason: string;
    };
    Interop_SceArtifactDetailResponseDto: {
      artifact_id: string;
      run_id: string;
      name: string;
      artifact_type: string;
      file_path: string;
      mime_type: string;
      /** Format: int64 */
      size_bytes: number | string;
      sha256_digest: string;
      content_preview: null | string;
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
    Interop_StatisticalProgramDto: {
      program_id: string;
      study_id: string;
      name: string;
      title: string;
      description: string;
      language: string;
      version: string;
      source_code: string;
      code_hash: string;
      lockfile_content: null | string;
      lock_type: string;
      lock_digest: string;
      target_domain: null | string;
      created_by: string;
      created_at: string;
      reason_for_change: string;
    };
    Interop_ValidateLockfileRequestDto: {
      lock_type: string;
      content: string;
      expected_digest?: null | string;
      /** @default false */
      strict_qualification: boolean;
    };
    Interop_ValidateLockfileResponseDto: {
      result: components["schemas"]["Interop_LockfileValidationResultSchemaDto"];
    };
    Interop_VerifyManifestResponseDto: {
      result: Record<string, never>;
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
      /** @description Conflict */
      409: {
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
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
      /** @description Unauthorized */
      401: {
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
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
      /** @description Unauthorized */
      401: {
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
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
      /** @description Unauthorized */
      401: {
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
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
      /** @description Unauthorized */
      401: {
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
  ListScePrograms: {
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
        content: {
          "application/json": components["schemas"]["Interop_ProgramListResponseDto"];
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
  CreateSceProgram: {
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
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_ProgramResponseDto"];
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
  GetSceProgram: {
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
        content: {
          "application/json": components["schemas"]["Interop_ProgramResponseDto"];
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
  ValidateSceLockfile: {
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
        content: {
          "application/json": components["schemas"]["Interop_ValidateLockfileResponseDto"];
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
  ListSceLockTokens: {
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
        content: {
          "application/json": components["schemas"]["Interop_LockTokenListResponseDto"];
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
  IssueSceLockToken: {
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
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Interop_LockTokenResponseDto"];
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
  RevokeSceLockToken: {
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
        content: {
          "application/json": components["schemas"]["Interop_RevokeLockTokenResponseDto"];
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
  ExecuteSceProgram: {
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
        content: {
          "application/json": components["schemas"]["Interop_ExecuteProgramResponseDto"];
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
  ListSceRuns: {
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
        content: {
          "application/json": components["schemas"]["Interop_ExecutionRunListResponseDto"];
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
  GetSceRun: {
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
        content: {
          "application/json": components["schemas"]["Interop_ExecutionRunDetailResponseDto"];
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
  VerifySceRunManifest: {
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
        content: {
          "application/json": components["schemas"]["Interop_VerifyManifestResponseDto"];
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
  GetSceRunArtifact: {
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
        content: {
          "application/json": components["schemas"]["Interop_SceArtifactDetailResponseDto"];
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
