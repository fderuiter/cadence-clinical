import { encodeChangeReason } from "./changeReason";
import { ApiError, createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/tickets";
import { isDemoBuild } from "../utils/demoMode.js";

/**
 * Typed client for the .NET Tickets service, generated from docs/openapi/tickets_openapi.json.
 */
type Schemas = components["schemas"];

export type TicketResponse = Schemas["Tickets_TicketResponseDto"];
export type CreateTicketRequest = Schemas["Tickets_CreateTicketRequestDto"];

export interface ListTicketsQuery {
  status?: string;
  category?: string;
  priority?: string;
  gxp_severity?: string;
  assigned_queue?: string;
  study_id?: string;
  site_id?: string;
  search?: string;
  limit?: number;
  offset?: number;
}

const client = createOpenApiClient<paths>();

let simulatedTicketCounter = 1;

const EMAIL_PATTERN = /\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b/;
const SSN_PATTERN = /\b\d{3}-\d{2}-\d{4}\b/;
const PHONE_PATTERN =
  /(?:\+?1[-.\s]?)?(?:\(\d{3}\)\s*\d{3}[-.\s]?\d{4}|\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b)/;
const DOB_PATTERN = /\b(?:dob|date\s+of\s+birth|born\s+on)\s*[:=-]?\s*\S+/i;
const SUBJECT_CODE_PATTERN = /^[A-Z0-9][A-Z0-9_-]{1,63}$/i;

export function routeDemoTicketQueue(input: {
  category?: string | null;
  source_route?: string | null;
  persona?: string | null;
}): string {
  const category = (input.category || "SYSTEM_SUPPORT").trim().toUpperCase();
  const route = (input.source_route || "").trim().toLowerCase();
  const persona = (input.persona || "").trim().toLowerCase();

  if (category === "REGULATORY_QUERY" || persona === "auditor") {
    return "REGULATORY_COMPLIANCE";
  }
  if (category === "ACCESS_CONTROL") {
    return "PLATFORM_ADMIN_ACCESS";
  }
  if (category === "SUPPLY_EXCURSION") {
    return "SUBJECT_MANAGEMENT_RTSM";
  }
  if (
    route.startsWith("/mdr") ||
    route.startsWith("/studies") ||
    route.startsWith("/digitization") ||
    route.startsWith("/amendment-diff")
  ) {
    return persona === "data_manager" && category === "DATA_QUERY"
      ? "DATA_CAPTURE_ECRF"
      : "PROTOCOL_DESIGNER";
  }
  if (
    route.startsWith("/ecrf") ||
    route.startsWith("/rules") ||
    route.startsWith("/coding") ||
    route.startsWith("/data-lock")
  ) {
    return "DATA_CAPTURE_ECRF";
  }
  if (
    route.startsWith("/ssu") ||
    route.startsWith("/ctms") ||
    route.startsWith("/dct")
  ) {
    return category === "MONITORING_FINDING"
      ? "REPORTING_ANALYTICS"
      : "SUBJECT_MANAGEMENT_RTSM";
  }
  if (route.startsWith("/audit") || route.startsWith("/auditor")) {
    return "REGULATORY_COMPLIANCE";
  }
  if (
    route.startsWith("/quality") ||
    route.startsWith("/exports") ||
    route.startsWith("/lakehouse")
  ) {
    return "REPORTING_ANALYTICS";
  }

  switch (category) {
    case "DATA_QUERY":
    case "PROTOCOL_DEVIATION":
      return "DATA_CAPTURE_ECRF";
    case "SITE_OPERATIONS":
    case "SAFETY_ADVERSE_EVENT":
      return "SUBJECT_MANAGEMENT_RTSM";
    case "MONITORING_FINDING":
      return "REPORTING_ANALYTICS";
    default:
      return "PLATFORM_ADMIN_ACCESS";
  }
}

export function validateDemoTicketNoDirectPii(
  payload: Partial<CreateTicketRequest>
): string | null {
  const combined = `${payload.title || ""}\n${payload.description || ""}`;
  if (EMAIL_PATTERN.test(combined)) {
    return "Direct participant PII (email address) is prohibited in support tickets. Use pseudonymous subject codes only.";
  }
  if (SSN_PATTERN.test(combined)) {
    return "Direct participant PII (SSN) is prohibited in support tickets. Use pseudonymous subject codes only.";
  }
  if (PHONE_PATTERN.test(combined)) {
    return "Direct participant PII (telephone number) is prohibited in support tickets. Use pseudonymous subject codes only.";
  }
  if (DOB_PATTERN.test(combined)) {
    return "Direct participant PII (date of birth) is prohibited in support tickets. Use pseudonymous subject codes only.";
  }
  const subjectId = (payload.subject_id || "").trim();
  if (subjectId && !SUBJECT_CODE_PATTERN.test(subjectId)) {
    return "Subject ID must be a pseudonymous clinical subject code (for example SUBJ-8812) without spaces or direct identifiers.";
  }
  return null;
}

function computeDemoSlaTargets(
  priority: string,
  severity: string,
  nowMs: number
) {
  let ackHours = 8;
  let resolveHours = 48;
  if (priority === "CRITICAL" || severity === "CRITICAL") {
    ackHours = 1;
    resolveHours = 4;
  } else if (priority === "HIGH" || severity === "MAJOR") {
    ackHours = 4;
    resolveHours = 24;
  } else if (priority === "LOW") {
    ackHours = 24;
    resolveHours = 120;
  }
  return {
    acknowledge_target_at: new Date(nowMs + ackHours * 3600_000).toISOString(),
    sla_target_at: new Date(nowMs + resolveHours * 3600_000).toISOString(),
  };
}

export function createSimulatedDemoTicket(
  payload: CreateTicketRequest,
  reasonForChange: string
): TicketResponse {
  const piiError = validateDemoTicketNoDirectPii(payload);
  if (piiError) {
    throw new ApiError(piiError, 400, "Bad Request", {
      title: "Direct participant PII is prohibited",
      status: 400,
      detail: piiError,
    });
  }

  const nowMs = Date.now();
  const year = new Date(nowMs).getUTCFullYear();
  const seq = String(simulatedTicketCounter++).padStart(4, "0");
  const priority = (payload.priority || "MEDIUM").toUpperCase();
  const severity = (payload.gxp_severity || "MINOR").toUpperCase();
  const category = (payload.category || "SYSTEM_SUPPORT").toUpperCase();
  const assignedQueue = routeDemoTicketQueue({
    category,
    source_route: payload.source_route,
    persona: payload.persona,
  });
  const targets = computeDemoSlaTargets(priority, severity, nowMs);

  return {
    id: `sim-tkt-${seq}`,
    reference: `SIM-${year}-${seq}`,
    title: payload.title,
    description: payload.description,
    category,
    priority,
    gxp_severity: severity,
    status: "OPEN",
    assigned_queue: assignedQueue,
    assignee_role: assignedQueue,
    assignee_user: null,
    reporter: payload.persona || "demo_operator",
    created_by: payload.persona || "demo_operator",
    study_id: payload.study_id ?? null,
    site_id: payload.site_id ?? null,
    subject_id: payload.subject_id ?? null,
    related_entity_type: payload.entity_type ?? null,
    related_entity_id: payload.entity_id ?? null,
    source_route: payload.source_route ?? null,
    acknowledge_target_at: targets.acknowledge_target_at,
    sla_target_at: targets.sla_target_at,
    sla_breached: false,
    sla_amber_warned: false,
    escalation_count: 0,
    reason_for_change: reasonForChange,
    created_at: new Date(nowMs).toISOString(),
    version_index: 1,
  };
}

export const ticketsApi = {
  /**
   * Submits a new support ticket (POST /api/v1/tickets) with mandatory X-Change-Reason.
   * Outside demo mode every failure throws so no unpersisted GxP ticket is ever reported as saved.
   * In demo mode, if the MSW handler or backend is unreachable (e.g. offline static/unit contexts),
   * falls back to a clearly marked SIM-* session ticket while still enforcing PII validation.
   */
  async createTicket(
    payload: CreateTicketRequest,
    reasonForChange: string
  ): Promise<TicketResponse> {
    const trimmedReason = (reasonForChange || "").trim();
    if (!trimmedReason) {
      throw new ApiError(
        "X-Change-Reason justification is required to create a support ticket.",
        400,
        "Bad Request"
      );
    }

    try {
      return await unwrapResponse(
        client.POST("/api/v1/tickets", {
          body: payload,
          headers: {
            "X-Change-Reason": encodeChangeReason(trimmedReason),
          },
        })
      );
    } catch (err) {
      const status = (err as { status?: number | null })?.status;
      if (isDemoBuild() && (!status || status === 404 || status >= 500)) {
        return createSimulatedDemoTicket(payload, trimmedReason);
      }
      throw err;
    }
  },

  /** Lists support tickets visible within the caller's RBAC and study/site scope (GET /api/v1/tickets). */
  async listTickets(query: ListTicketsQuery = {}): Promise<TicketResponse[]> {
    const resp = await unwrapResponse(
      client.GET("/api/v1/tickets", {
        params: {
          query: {
            status: query.status,
            category: query.category,
            priority: query.priority,
            gxp_severity: query.gxp_severity,
            assigned_queue: query.assigned_queue,
            study_id: query.study_id,
            site_id: query.site_id,
            search: query.search,
            limit: query.limit,
            offset: query.offset,
          },
        },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },

  /** Retrieves a single support ticket by UUID or reference code (GET /api/v1/tickets/{ticket_id}). */
  getTicket(ticketId: string): Promise<TicketResponse> {
    return unwrapResponse(
      client.GET("/api/v1/tickets/{ticket_id}", {
        params: { path: { ticket_id: ticketId } },
      })
    );
  },
};
