/**
 * Generated from docs/openapi/gateway_openapi.json by scripts/generate-api-types.js.
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
    /** Gateway liveness and readiness probe. */
    get: operations["HealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/auth/signature-verification": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /**
     * Re-authenticates the signer and issues a single-use e-signature token for one action.
     * @description 21 CFR 11.200(a)(1) step-up credential check: verifies the signed-in caller's password (and optional TOTP) against Keycloak and returns a single-use 60-second X-Sig-Token bound to one canonical API path and semantic action.
     */
    post: operations["IssueSignatureToken"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns gateway dispatch status. */
    get: operations["GatewayStatus"];
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
    Gateway_GatewayHealthResponse: {
      status: string;
      service: string;
      version: string;
    };
    Gateway_GatewayStatusResponse: {
      mode: string;
      gateway: string;
    };
    Gateway_HttpValidationProblemDetails: {
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
    Gateway_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Gateway_SignatureTokenResponse: {
      sig_token: string;
      /** Format: int32 */
      expires_in: number | string;
    };
    Gateway_SignatureVerificationRequest: {
      username: null | string;
      password: null | string;
      totp: null | string;
      action: null | string;
      semantic_action: null | string;
      batch_id: null | string;
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
  HealthCheck: {
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
          "application/json": components["schemas"]["Gateway_GatewayHealthResponse"];
        };
      };
    };
  };
  IssueSignatureToken: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional reason-for-change header validated by GatewayIdentityForwarding (printable ASCII or RFC 8187 UTF-8''); malformed values return 400 Bad Request. */
        "X-Change-Reason"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Gateway_SignatureVerificationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Gateway_SignatureTokenResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Gateway_HttpValidationProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Gateway_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Gateway_ProblemDetails"];
        };
      };
      /** @description Too Many Requests */
      429: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Gateway_ProblemDetails"];
        };
      };
      /** @description Service Unavailable */
      503: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Gateway_ProblemDetails"];
        };
      };
    };
  };
  GatewayStatus: {
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
          "application/json": components["schemas"]["Gateway_GatewayStatusResponse"];
        };
      };
    };
  };
}
