/**
 * Generated from docs/openapi/intelligence_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/api/v1/ai/intent/route": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Match a command-palette query against fixed patterns and return the navigation targets the caller's roles allow. */
    post: operations["RouteAiIntent"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The caller's latest agent runs from the interaction ledger, newest first. */
    get: operations["AiListMyRuns"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/runs/{runId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One of the caller's agent runs with its request, answer, usage and integrity hash. */
    get: operations["AiGetMyRun"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The agent task configuration versions in this deployment, with their content hashes and the tenant's qualification of each. */
    get: operations["AiListTasks"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/evaluation-runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Stores an evaluation run record after checking it against its task configuration version, set, criteria and case results (evaluation_run.record, ADR-2634). */
    post: operations["AiRecordEvaluationRun"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/evaluation-runs/{runId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One stored evaluation run with its record, exactly as hashed. */
    get: operations["AiGetEvaluationRun"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/evaluation-runs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The stored evaluation runs of a task, newest first, optionally of one version. */
    get: operations["AiListEvaluationRuns"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/versions/{version}/qualification": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Qualifies a task configuration version on a stored, accepted evaluation run and the caller's signed approval (task_configuration.qualify, ADR-2631). */
    post: operations["AiQualifyTask"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/tasks/{key}/versions/{version}/suspension": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Takes a qualified task configuration version out of use until it is qualified again (task_configuration.suspend). */
    post: operations["AiSuspendTask"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/controls": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The tenant's AI kill switches and monthly token budgets. */
    get: operations["AiListControls"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/controls/{scope}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Switches agents on or off for every task ('*') or one task, and sets the monthly token budget (agent_control.set). */
    put: operations["AiSetControl"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ai/biometrics/verify-liveness": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Tests video frame payloads for anti-spoofing in temporary memory and returns signed liveness tokens. */
    post: operations["AiVerifyLiveness"];
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
    Intelligence_AcceptanceCriteria: {
      /** Format: double */
      minimum_pass_rate: number | string;
      /** Format: double */
      minimum_reviewed_pass_rate: number | string;
      maximum_mean_latency?: null | string;
      /** Format: double */
      maximum_mean_cost?: null | number | string;
    };
    Intelligence_AgentControlRequest: {
      enabled: boolean;
      /** Format: int64 */
      monthly_token_limit: null | number | string;
    };
    Intelligence_AgentControlResponse: {
      scope: string;
      enabled: boolean;
      /** Format: int64 */
      monthly_token_limit: null | number | string;
      /** Format: int64 */
      version_index: number | string;
    };
    Intelligence_AgentRunResponse: {
      summary: components["schemas"]["Intelligence_AgentRunSummaryResponse"];
      provider: string;
      configuration_hash: string;
      request_json: string;
      response_text: null | string;
      failure_reason: null | string;
      content_hash: null | string;
      content_hash_verified: boolean;
    };
    Intelligence_AgentRunSummaryResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      model: string;
      status: string;
      /** Format: date-time */
      started_at: string;
      /** Format: date-time */
      completed_at: null | string;
      /** Format: int64 */
      input_tokens: number | string;
      /** Format: int64 */
      output_tokens: number | string;
      /** Format: double */
      cost_usd: null | number | string;
    };
    Intelligence_AgentTaskResponse: {
      key: string;
      /** Format: int32 */
      version: number | string;
      provider: string;
      model: string;
      autonomy_level: string;
      content_hash: string;
      tools: string[];
      retrieval_sources: string[];
      evaluation_set:
        null | components["schemas"]["Intelligence_EvaluationSetRef"];
      acceptance_criteria:
        null | components["schemas"]["Intelligence_AcceptanceCriteria"];
      risk_class: null | string;
      qualification_gaps: string[];
      qualification:
        null | components["schemas"]["Intelligence_TaskQualificationResponse"];
    };
    Intelligence_EvaluationRunResponse: {
      run: components["schemas"]["Intelligence_EvaluationRunSummaryResponse"];
      record_json: string;
    };
    Intelligence_EvaluationRunStateResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      evaluation_set_hash: string;
      record_hash: string;
      accepted: boolean;
      reasons: string[];
    };
    Intelligence_EvaluationRunSummaryResponse: {
      /** Format: uuid */
      run_id: string;
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      evaluation_set_id: string;
      /** Format: int32 */
      evaluation_set_version: number | string;
      evaluation_set_hash: string;
      provider: string;
      model: string;
      accepted: boolean;
      /** Format: date-time */
      started_at: string;
      /** Format: date-time */
      completed_at: string;
      record_hash: string;
    };
    Intelligence_EvaluationSetRef: {
      set_id: string;
      /** Format: int32 */
      version: number | string;
      content_hash: string;
    };
    Intelligence_IntentRecommendation: {
      id: string;
      name: string;
      description: string;
      category: string;
      icon: string;
      target_route: string;
      action: null | string;
      /** Format: double */
      confidence: number | string;
      roles: string[];
    };
    Intelligence_IntentRouteRequest: {
      query: string;
      context?: null | {
        [key: string]: string;
      };
    };
    Intelligence_IntentRouteResponse: {
      query: string;
      recommendations: components["schemas"]["Intelligence_IntentRecommendation"][];
    };
    Intelligence_JsonElement: unknown;
    Intelligence_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Intelligence_QualificationRequest: {
      configuration_hash: string;
      /** Format: uuid */
      evaluation_run_id: string;
      /** Format: uuid */
      attestation_id: string;
    };
    Intelligence_QualificationResponse: {
      task_key: string;
      /** Format: int32 */
      task_version: number | string;
      configuration_hash: string;
      status: string;
      risk_class: string;
      /** Format: int64 */
      version_index: number | string;
    };
    Intelligence_TaskQualificationResponse: {
      status: string;
      usable: boolean;
      configuration_hash: string;
      risk_class: string;
      evaluation_run_reference: string;
      evaluation_run_hash: string;
      /** Format: date-time */
      qualified_at: string;
      /** Format: date-time */
      suspended_at: null | string;
      suspension_reason: null | string;
    };
    Intelligence_VerifyLivenessRequest: {
      subject_pseudonym: null | string;
      video_frames: null | string[];
      challenge_id: null | string;
      /** @default false */
      fallback_mode: boolean;
      study_id?: null | string;
      site_id?: null | string;
    };
    Intelligence_VerifyLivenessResponse: {
      liveness_token: string;
      is_live: boolean;
      /** Format: double */
      anti_spoof_score: number | string;
      /** Format: date-time */
      expires_at: string;
      status: string;
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
  RouteAiIntent: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Intelligence_IntentRouteRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Intelligence_IntentRouteResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiListMyRuns: {
    parameters: {
      query?: {
        limit?: number | string;
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
          "application/json": components["schemas"]["Intelligence_AgentRunSummaryResponse"][];
        };
      };
    };
  };
  AiGetMyRun: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        runId: string;
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
          "application/json": components["schemas"]["Intelligence_AgentRunResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiListTasks: {
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
          "application/json": components["schemas"]["Intelligence_AgentTaskResponse"][];
        };
      };
    };
  };
  AiRecordEvaluationRun: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Intelligence_JsonElement"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Intelligence_EvaluationRunStateResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Task.Unknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.AlreadyExists`, `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Evaluation.RunInvalid`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiGetEvaluationRun: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        runId: string;
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
          "application/json": components["schemas"]["Intelligence_EvaluationRunResponse"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiListEvaluationRuns: {
    parameters: {
      query?: {
        version?: number | string;
        limit?: number | string;
      };
      header?: never;
      path: {
        key: string;
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
          "application/json": components["schemas"]["Intelligence_EvaluationRunSummaryResponse"][];
        };
      };
    };
  };
  AiQualifyTask: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        key: string;
        version: number;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Intelligence_QualificationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Intelligence_QualificationResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Evaluation.RunNotFound`, `M.Intelligence.Task.Unknown`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`, `M.Intelligence.Configuration.Changed`, `M.Intelligence.Qualification.AlreadyQualified`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`, `M.Intelligence.Configuration.NotQualifiable`, `M.Intelligence.Qualification.AttestationRejected`, `M.Intelligence.Qualification.EvaluationNotAccepted`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiSuspendTask: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        key: string;
        version: number;
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
          "application/json": components["schemas"]["Intelligence_QualificationResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Not Found. Kernel errors: `M.Intelligence.Qualification.NotQualified`. */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiListControls: {
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
          "application/json": components["schemas"]["Intelligence_AgentControlResponse"][];
        };
      };
    };
  };
  AiSetControl: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        scope: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Intelligence_AgentControlRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Intelligence_AgentControlResponse"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden. Kernel errors: `K.Authorization.Denied`, `K.Subscription.Expired`. */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Conflict. Kernel errors: `K.Aggregate.ConcurrencyConflict`, `K.Command.IdempotencyConflict`. */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity. Kernel errors: `K.Command.IdempotencyKeyRequired`, `K.Command.Invalid`, `K.Command.ReasonForChangeRequired`, `K.Command.TenantRequired`. */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
  AiVerifyLiveness: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Intelligence_VerifyLivenessRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Intelligence_VerifyLivenessResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Unauthorized. Kernel errors: `K.Actor.Unresolved`. */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Intelligence_ProblemDetails"];
        };
      };
    };
  };
}
