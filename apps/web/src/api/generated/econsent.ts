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
    /** Publishes a specific eConsent template version and records whether participant re-consent is required. */
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
    /** Publishes the next version of an eConsent template for the specified protocol version. */
    post: operations["PublishTemplate"];
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
    /** Captures a participant's 21 CFR Part 11 electronic consent signature against a versioned template. */
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
    /** Captures a participant's 21 CFR Part 11 electronic consent signature against the latest published template. */
    post: operations["CaptureConsentLegacy"];
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
  CaptureConsentVersioned: {
    parameters: {
      query?: never;
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
      };
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
}
