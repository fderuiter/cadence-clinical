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
    get: operations["ListTickets"];
    put?: never;
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
    get: operations["GetTicket"];
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
      acknowledge_target_at: null | string;
      /** Format: date-time */
      sla_target_at: null | string;
      sla_breached: boolean;
      sla_amber_warned: boolean;
      /** Format: int32 */
      escalation_count: number | string;
      reason_for_change: string;
      /** Format: date-time */
      created_at: string;
      /** Format: int32 */
      version_index: number | string;
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
      header?: never;
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
}
