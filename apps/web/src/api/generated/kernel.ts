/**
 * Generated from docs/openapi/kernel_openapi.json by scripts/generate-api-types.js.
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
    /** Clinical Kernel API health probe. */
    get: operations["KernelHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns the Clinical Kernel API's operational status. */
    get: operations["KernelStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Resolves a NodeRef to its summary.
     * @description Title, code and state of one kernel object in the caller's tenant, at its current version.
     */
    get: operations["KernelGetNode"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/path": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The zoom path from the tenant down to an object.
     * @description Presentation only: the path never grants or implies authority (spec §9.3).
     */
    get: operations["KernelGetZoomPath"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/children": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** The children of a scope node, with their own child counts. */
    get: operations["KernelGetChildren"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/nodes/{nodeRef}/commands": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * The kernel commands offered on an object, each with the caller's dry-run decision.
     * @description Each decision is the one sending the command would get now (ADR-2483), explained (ADR-2482).
     */
    get: operations["KernelGetCommands"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/authorization/can-i": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Whether the caller may exercise a capability on an object now, and why.
     * @description A dry run: it executes nothing and is recorded in the authorization access log, not the audit chain (ADR-2483).
     */
    get: operations["KernelCanI"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/search": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * Finds kernel objects in the caller's tenant by code or title.
     * @description Each word matches as a prefix; an exact code ranks first. Sources that reveal identity are searched only when the caller holds the capability (ADR-2484).
     */
    get: operations["KernelSearch"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/studies/{studyRef}/protocol-versions": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /**
     * A study's protocol versions, newest first, without their content.
     * @description A study the caller may not see is refused; a version the caller may not see is masked in place (ADR-2471).
     */
    get: operations["KernelListProtocolVersions"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/kernel/protocol-versions/{versionRef}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** One protocol version, without its content. */
    get: operations["KernelGetProtocolVersion"];
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
    Kernel_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Kernel_KernelChildResponse: {
      node: components["schemas"]["Kernel_KernelNodeResponse"];
      /** Format: int32 */
      child_count: null | number | string;
      group: null | string;
    };
    Kernel_KernelCommandResponse: {
      command: string;
      decision: components["schemas"]["Kernel_KernelDecisionResponse"];
    };
    Kernel_KernelConditionResponse: {
      code: string;
      description: string;
      met: boolean;
      evidence: string[];
    };
    Kernel_KernelDecisionResponse: {
      capability: string;
      verb_class: string;
      resource: string;
      permitted: boolean;
      reason: null | string;
      explanation: components["schemas"]["Kernel_KernelExplanationResponse"];
    };
    Kernel_KernelExplanationResponse: {
      subject: null | string;
      claim: string;
      producer: string;
      steps: components["schemas"]["Kernel_KernelExplanationStepResponse"][];
      conditions: components["schemas"]["Kernel_KernelConditionResponse"][];
      next_actions: components["schemas"]["Kernel_KernelNextActionResponse"][];
    };
    Kernel_KernelExplanationStepResponse: {
      claim: string;
      evidence: string[];
    };
    Kernel_KernelNextActionResponse: {
      description: string;
      capability: null | string;
      target: null | string;
    };
    Kernel_KernelNodeResponse: {
      ref: null | string;
      kind: string;
      title: null | string;
      code: null | string;
      status: null | string;
      mask: null | string;
    };
    Kernel_KernelProtocolVersionResponse: {
      ref: null | string;
      study: null | string;
      /** Format: int32 */
      number: null | number | string;
      parent_version: null | string;
      status: null | string;
      content_hash: null | string;
      amendment_intent: null | string;
      approval_attestation: null | string;
      withdrawal_reason: null | string;
      mask: null | string;
    };
    Kernel_KernelSearchHitResponse: {
      ref: string;
      kind: string;
      title: string;
      code: null | string;
      detail: null | string;
      exact_code: boolean;
      /** Format: double */
      rank: number | string;
    };
    Kernel_KernelZoomStepResponse: {
      level: string;
      label: null | string;
      ref: null | string;
      mask: null | string;
    };
    Kernel_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
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
  KernelHealthCheck: {
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
          "application/json": components["schemas"]["Kernel_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  KernelStatus: {
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
          "application/json": components["schemas"]["Kernel_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  KernelGetNode: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelNodeResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetZoomPath: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelZoomStepResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetChildren: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelChildResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetCommands: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        nodeRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelCommandResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelCanI: {
    parameters: {
      query: {
        capability: string;
        resource: string;
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
          "application/json": components["schemas"]["Kernel_KernelDecisionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelSearch: {
    parameters: {
      query: {
        q: string;
        kind?: string[];
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
          "application/json": components["schemas"]["Kernel_KernelSearchHitResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelListProtocolVersions: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelProtocolVersionResponse"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
  KernelGetProtocolVersion: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        versionRef: string;
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
          "application/json": components["schemas"]["Kernel_KernelProtocolVersionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Kernel_ProblemDetails"];
        };
      };
    };
  };
}
