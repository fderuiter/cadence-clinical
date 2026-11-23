/**
 * Generated from docs/openapi/designer_openapi.json by scripts/generate-api-types.js.
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
    /** Protocol designer metadata health probe. */
    get: operations["DesignerHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/studies/{id}/rules/batch-validate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Executes study-wide parallel batch compilation and validation across pinned edit check rule packs. */
    post: operations["BatchValidateRules"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/studies/{id}/rules/batch-validate/{jobId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves the aggregated report for an asynchronous batch validation job. */
    get: operations["GetBatchValidationJob"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/studies/{id}/rules/amendment-regression": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Compares baseline and target rule pack versions during protocol amendments to highlight rule diffs and broken dependencies. */
    post: operations["AmendmentRegression"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/studies": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists active CDISC USDM study protocol definitions. */
    get: operations["ListStudies"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/protocols/compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Compiles CDISC USDM protocol JSON into DDF IR and EDC runtime schemas. */
    post: operations["CompileProtocol"];
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
    Designer_AccessibilityReport: {
      /** @default PASS */
      status: string;
      /** @default  */
      formHash: string;
      violations?: null | Record<string, never>[];
      passes?: null | Record<string, never>[];
      incomplete?: null | Record<string, never>[];
      inapplicable?: null | Record<string, never>[];
      layoutErrors?: null | string[];
    };
    Designer_AmendmentRegressionApiRequest: {
      baselineVersion?: null | string;
      targetVersion?: null | string;
      baselinePack?: unknown;
      targetPack?: unknown;
      records?: null | components["schemas"]["Designer_RuleRecord"][];
    };
    Designer_AmendmentRegressionReport: {
      baselineVersion: string;
      targetVersion: string;
      ruleDiffs: components["schemas"]["Designer_RuleDiffSummary"];
      brokenAstReferences: components["schemas"]["Designer_BrokenAstReference"][];
      newViolations: components["schemas"]["Designer_ValidationDetection"][];
      resolvedFindings: components["schemas"]["Designer_ValidationDetection"][];
      timing: components["schemas"]["Designer_ValidationTimingMetrics"];
    };
    Designer_AnonymousTypeOfstringAndstringAndstringAndstring: {
      status: null | string;
      service: null | string;
      primaryDatastore: null | string;
      compiler: null | string;
    };
    Designer_BatchValidateApiRequest: {
      records?: null | components["schemas"]["Designer_RuleRecord"][];
      rulePacks?: null | unknown[];
      isDryRun?: null | boolean;
      /** Format: int32 */
      asyncThreshold?: null | number | string;
    };
    Designer_BatchValidationReport: {
      jobId: string;
      study: components["schemas"]["Designer_StudyId"];
      isAsync: boolean;
      status: string;
      timing: components["schemas"]["Designer_ValidationTimingMetrics"];
      syntaxErrors: string[];
      circularDependencies: string[];
      conflictingRules: string[];
      detections: components["schemas"]["Designer_ValidationDetection"][];
    };
    Designer_BrokenAstReference: {
      ruleId: string;
      /** Format: int32 */
      ruleVersion: number | string;
      referencedVariable: string;
      detail: string;
    };
    Designer_CompileProtocolRequest: {
      usdmJson: string;
      studyId?: null | string;
      versionTag?: null | string;
    };
    Designer_DDFCompilationResult: {
      studyId: string;
      name: string;
      odmXml: string;
      openRosaXml: string;
      formHash: string;
      warnings?: null | Record<string, never>[];
      accessibilityReport?:
        null | components["schemas"]["Designer_AccessibilityReport"];
      /** @default COMPLETED */
      status: string;
    };
    Designer_EffectivePeriod: {
      /** Format: date */
      from: null | string;
      /** Format: date */
      until: null | string;
    };
    Designer_FindingTarget: {
      kind: components["schemas"]["Designer_FindingTargetKind"];
      dataset: string;
      key: string;
      variable?: null | string;
      node?: components["schemas"]["Designer_NodeRef"];
    };
    Designer_FindingTargetKind: number;
    Designer_NodeRef: unknown;
    Designer_ObservedVariable: {
      variable: string;
      value: null | string;
    };
    Designer_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Designer_Rule: {
      id: string;
      /** Format: int32 */
      version: number | string;
      authority: components["schemas"]["Designer_RuleAuthority"];
      citation: components["schemas"]["Designer_SourceCitation"];
      applicability: components["schemas"]["Designer_RuleApplicability"];
      severity: components["schemas"]["Designer_ValidationSeverity"];
      condition: components["schemas"]["Designer_RuleExpression"];
      explanation: string;
      remediationGuidance: null | string;
      effective: components["schemas"]["Designer_EffectivePeriod"];
    };
    Designer_RuleApplicability: {
      stage: components["schemas"]["Designer_ValidationStage"];
      standard: null | string;
      standardVersion: null | string;
      agency: null | string;
      dataset: null | string;
    };
    Designer_RuleAuthority: number;
    Designer_RuleDiffSummary: {
      addedRules: components["schemas"]["Designer_Rule"][];
      removedRules: components["schemas"]["Designer_Rule"][];
      modifiedRules: components["schemas"]["Designer_Rule"][];
    };
    Designer_RuleExpression: unknown;
    Designer_RuleRecord: {
      dataset: string;
      key: string;
      values: Record<string, never>;
    };
    Designer_SourceCitation: {
      document: string;
      section: null | string;
      uri: null | string;
    };
    Designer_StudyId: unknown;
    Designer_ValidationDetection: {
      packKey: string;
      ruleId: string;
      /** Format: int32 */
      ruleVersion: number | string;
      severity: components["schemas"]["Designer_ValidationSeverity"];
      target: components["schemas"]["Designer_FindingTarget"];
      message: string;
      expected: null | string;
      observed: components["schemas"]["Designer_ObservedVariable"][];
    };
    Designer_ValidationSeverity: number;
    Designer_ValidationStage: number;
    Designer_ValidationTimingMetrics: {
      /** Format: date-time */
      startedAt: string;
      /** Format: date-time */
      completedAt: string;
      /** Format: double */
      durationMs: number | string;
      /** Format: int32 */
      rulesEvaluated: number | string;
      /** Format: int32 */
      recordsEvaluated: number | string;
      /** Format: int32 */
      parallelWorkerCount: number | string;
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
  DesignerHealthCheck: {
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
          "application/json": components["schemas"]["Designer_AnonymousTypeOfstringAndstringAndstringAndstring"];
        };
      };
    };
  };
  BatchValidateRules: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          null | components["schemas"]["Designer_BatchValidateApiRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Designer_BatchValidationReport"];
        };
      };
    };
  };
  GetBatchValidationJob: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
        jobId: string;
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
          "application/json": components["schemas"]["Designer_BatchValidationReport"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content?: never;
      };
    };
  };
  AmendmentRegression: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        id: string;
      };
      cookie?: never;
    };
    requestBody?: {
      content: {
        "application/json":
          | null
          | components["schemas"]["Designer_AmendmentRegressionApiRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Designer_AmendmentRegressionReport"];
        };
      };
    };
  };
  ListStudies: {
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
          "application/json": string[];
        };
      };
    };
  };
  CompileProtocol: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Designer_CompileProtocolRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Designer_DDFCompilationResult"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Designer_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Designer_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Designer_ProblemDetails"];
        };
      };
    };
  };
}
