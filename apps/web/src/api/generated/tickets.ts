/**
 * Generated from docs/openapi/tickets_openapi.json by scripts/generate-api-types.js.
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
    /** Support tickets health probe. */
    get: operations["TicketsHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Tickets operational status. */
    get: operations["TicketsStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists operational tickets filtered by study, site, status, priority, or queue. */
    get: operations["ListTickets"];
    put?: never;
    /** Creates a GxP support or operational issue ticket with SLA targets and hash-chained audit entry. */
    post: operations["CreateTicket"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets/{ticket_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves a single operational ticket by ID or reference code. */
    get: operations["GetTicket"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    /** Updates mutable ticket fields with optimistic concurrency and Part 11 change reason. */
    patch: operations["UpdateTicket"];
    trace?: never;
  };
  "/api/v1/tickets/{ticket_id}/comments": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists role-filtered public and sponsor-internal comments on a ticket. */
    get: operations["ListTicketComments"];
    put?: never;
    /** Appends a visibility-scoped comment to an operational ticket. */
    post: operations["CreateTicketComment"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets/{ticket_id}/transitions": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Transitions a ticket across workflow states and updates SLA pause timers. */
    post: operations["TransitionTicket"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets/{ticket_id}/sign": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Applies a 21 CFR Part 11 step-up electronic signature to close or attest a GxP ticket. */
    post: operations["SignTicket"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/tickets/{ticket_id}/audit-trail": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves the hash-chained GxP audit trail for an operational ticket. */
    get: operations["GetTicketAuditTrail"];
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
    Tickets_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Tickets_CreateTicketCommentRequestDto: {
      body: string;
      /** @default PUBLIC */
      visibility: null | string;
    };
    Tickets_CreateTicketRequestDto: {
      title: string;
      description: string;
      /** @default SYSTEM_SUPPORT */
      category: null | string;
      /** @default MINOR */
      gxp_severity: null | string;
      /** @default MEDIUM */
      priority: null | string;
      study_id?: null | string;
      site_id?: null | string;
      subject_id?: null | string;
      entity_type?: null | string;
      entity_id?: null | string;
      source_route?: null | string;
      persona?: null | string;
    };
    Tickets_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Tickets_SignTicketRequestDto: {
      signature_meaning: string;
      /** @default SIGN_CLOSURE */
      signature_action: null | string;
    };
    Tickets_TicketAuditEntryDto: {
      /** Format: uuid */
      id: string;
      entity_name: string;
      entity_id: string;
      action: string;
      /** Format: date-time */
      timestamp: string;
      user_id: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
      changes_json: null | string;
      predecessor_hash: string;
      block_hash: string;
    };
    Tickets_TicketCommentResponseDto: {
      id: string;
      ticket_id: string;
      body: string;
      visibility: string;
      /** Format: date-time */
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int32 */
      version_index: number | string;
    };
    Tickets_TicketResponseDto: {
      id: string;
      reference: string;
      title: string;
      description: string;
      category: string;
      priority: string;
      gxp_severity: string;
      status: string;
      assigned_queue: string;
      assignee_role: null | string;
      assignee_user: null | string;
      reporter: string;
      created_by: string;
      study_id: null | string;
      site_id: null | string;
      subject_id: null | string;
      related_entity_type: null | string;
      related_entity_id: null | string;
      source_route: null | string;
      /** Format: date-time */
      due_date: null | string;
      /** Format: date-time */
      acknowledge_target_at: null | string;
      /** Format: date-time */
      sla_target_at: null | string;
      /** Format: date-time */
      sla_paused_at: null | string;
      /** Format: int32 */
      sla_total_paused_seconds: number | string;
      sla_breached: boolean;
      sla_amber_warned: boolean;
      /** Format: int32 */
      escalation_count: number | string;
      resolution_code: null | string;
      root_cause_category: null | string;
      root_cause_summary: null | string;
      signature_action: null | string;
      signature_token: null | string;
      signature_meaning: null | string;
      /** Format: date-time */
      signature_timestamp: null | string;
      signature_user: null | string;
      reason_for_change: string;
      /** Format: date-time */
      created_at: string;
      /** Format: date-time */
      updated_at: string;
      /** Format: int32 */
      version_index: number | string;
    };
    Tickets_TransitionTicketRequestDto: {
      target_status: string;
      resolution_code?: null | string;
      root_cause_summary?: null | string;
      root_cause_category?: null | string;
      assignee_user?: null | string;
    };
    Tickets_UpdateTicketRequestDto: {
      title?: null | string;
      description?: null | string;
      priority?: null | string;
      status?: null | string;
      assignee_user?: null | string;
      resolution_code?: null | string;
      root_cause_summary?: null | string;
      root_cause_category?: null | string;
      /** Format: int32 */
      expected_version_index?: null | number | string;
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
  TicketsHealthCheck: {
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
          "application/json": components["schemas"]["Tickets_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  TicketsStatus: {
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
          "application/json": components["schemas"]["Tickets_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  ListTickets: {
    parameters: {
      query?: {
        status?: string;
        category?: string;
        priority?: string;
        gxp_severity?: string;
        assigned_queue?: string;
        study_id?: string;
        site_id?: string;
        search?: string;
        limit?: number | string;
        offset?: number | string;
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
          "application/json": components["schemas"]["Tickets_TicketResponseDto"][];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  CreateTicket: {
    parameters: {
      query?: never;
      header?: {
        /** @description Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Tickets_CreateTicketRequestDto"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Tickets_TicketResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  GetTicket: {
    parameters: {
      query?: {
        revision?: number | string;
        as_of?: string;
      };
      header?: never;
      path: {
        ticket_id: string;
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
          "application/json": components["schemas"]["Tickets_TicketResponseDto"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  UpdateTicket: {
    parameters: {
      query?: never;
      header?: {
        /** @description Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        ticket_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Tickets_UpdateTicketRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Tickets_TicketResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  ListTicketComments: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        ticket_id: string;
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
          "application/json": components["schemas"]["Tickets_TicketCommentResponseDto"][];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  CreateTicketComment: {
    parameters: {
      query?: never;
      header?: {
        /** @description Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        ticket_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Tickets_CreateTicketCommentRequestDto"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Tickets_TicketCommentResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  TransitionTicket: {
    parameters: {
      query?: never;
      header?: {
        /** @description Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        ticket_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Tickets_TransitionTicketRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Tickets_TicketResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  SignTicket: {
    parameters: {
      query?: never;
      header?: {
        /** @description Single-use 60-second step-up electronic signature JWT issued by POST /api/v1/auth/signature-verification and bound to this exact action and semantic meaning (21 CFR 11.200(a)(1)). */
        "X-Sig-Token"?: string;
        /** @description Required 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        ticket_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Tickets_SignTicketRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Tickets_TicketResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
  GetTicketAuditTrail: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        ticket_id: string;
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
          "application/json": components["schemas"]["Tickets_TicketAuditEntryDto"][];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Tickets_ProblemDetails"];
        };
      };
    };
  };
}
