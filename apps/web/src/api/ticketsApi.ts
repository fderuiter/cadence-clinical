import { encodeChangeReason } from "./changeReason";
import { ApiError, createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/tickets";
import { requestStepUpSignature } from "./signatureVerification";
import { isDemoBuild } from "../utils/demoMode.js";

/**
 * Typed client for the .NET Tickets service, generated from docs/openapi/tickets_openapi.json.
 */
type Schemas = components["schemas"];

export type TicketResponse = Schemas["Tickets_TicketResponseDto"];
export type CreateTicketRequest = Schemas["Tickets_CreateTicketRequestDto"];
export type TicketCommentResponse = Schemas["Tickets_TicketCommentResponseDto"];
export type CreateTicketCommentRequest =
  Schemas["Tickets_CreateTicketCommentRequestDto"];
export type TransitionTicketRequest =
  Schemas["Tickets_TransitionTicketRequestDto"];
export type SignTicketRequest = Schemas["Tickets_SignTicketRequestDto"];
export type TicketAuditEntry = Schemas["Tickets_TicketAuditEntryDto"];

export interface TicketStepUpVerificationParams {
  ticketId: string;
  username: string;
  password: string;
  totp?: string | null;
}

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
const SUBJECT_CODE_PATTERN =
  /^[A-Za-z]{2,12}[-_][A-Za-z0-9][A-Za-z0-9_-]{0,24}$/;

function normalizeTicketRoute(rawRoute?: string | null): string {
  const raw = (rawRoute || "").trim();
  if (!raw) return "";
  const withoutQuery = raw.split(/[?#]/)[0] || "";
  const prefixed = withoutQuery.startsWith("/")
    ? withoutQuery
    : `/${withoutQuery}`;
  return prefixed.replace(/\/+$/, "").toLowerCase();
}

function startsWithRouteSegment(route: string, prefix: string): boolean {
  return route === prefix || route.startsWith(`${prefix}/`);
}

export function routeDemoTicketQueue(input: {
  category?: string | null;
  source_route?: string | null;
  persona?: string | null;
  entity_type?: string | null;
}): string {
  const category = (input.category || "SYSTEM_SUPPORT").trim().toUpperCase();
  const route = normalizeTicketRoute(input.source_route);
  const persona = (input.persona || "").trim().toLowerCase();
  const entityType = (input.entity_type || "").trim().toUpperCase();

  if (category === "ACCESS_CONTROL" || category === "TECHNICAL_SYSTEM") {
    return "PLATFORM_ADMIN_ACCESS";
  }
  if (category === "REGULATORY_QUERY" || persona === "auditor") {
    return "REGULATORY_COMPLIANCE";
  }
  if (category === "SUPPLY_EXCURSION") {
    return "SUBJECT_MANAGEMENT_RTSM";
  }

  if (
    startsWithRouteSegment(route, "/ecrf") ||
    startsWithRouteSegment(route, "/rules") ||
    startsWithRouteSegment(route, "/coding") ||
    startsWithRouteSegment(route, "/data-lock") ||
    startsWithRouteSegment(route, "/observations")
  ) {
    return "DATA_CAPTURE_ECRF";
  }
  if (
    startsWithRouteSegment(route, "/mdr") ||
    startsWithRouteSegment(route, "/designer") ||
    startsWithRouteSegment(route, "/protocol") ||
    startsWithRouteSegment(route, "/digitization") ||
    startsWithRouteSegment(route, "/amendment-diff")
  ) {
    return "PROTOCOL_DESIGNER";
  }
  if (startsWithRouteSegment(route, "/studies")) {
    if (category === "DATA_QUERY") {
      return "DATA_CAPTURE_ECRF";
    }
    if (
      category === "SITE_OPERATIONS" ||
      category === "MONITORING_FINDING" ||
      category === "SAFETY_ADVERSE_EVENT"
    ) {
      return "SUBJECT_MANAGEMENT_RTSM";
    }
    return "PROTOCOL_DESIGNER";
  }
  if (
    startsWithRouteSegment(route, "/rtsm") ||
    startsWithRouteSegment(route, "/supply") ||
    startsWithRouteSegment(route, "/ssu") ||
    startsWithRouteSegment(route, "/ctms") ||
    startsWithRouteSegment(route, "/safety") ||
    startsWithRouteSegment(route, "/dct") ||
    startsWithRouteSegment(route, "/devices") ||
    startsWithRouteSegment(route, "/device-investigation")
  ) {
    return "SUBJECT_MANAGEMENT_RTSM";
  }
  if (
    startsWithRouteSegment(route, "/exports") ||
    startsWithRouteSegment(route, "/analytics") ||
    startsWithRouteSegment(route, "/reports") ||
    startsWithRouteSegment(route, "/payments") ||
    startsWithRouteSegment(route, "/sce") ||
    startsWithRouteSegment(route, "/lakehouse")
  ) {
    return "REPORTING_ANALYTICS";
  }
  if (
    startsWithRouteSegment(route, "/audit") ||
    startsWithRouteSegment(route, "/auditor") ||
    startsWithRouteSegment(route, "/quality") ||
    startsWithRouteSegment(route, "/etmf") ||
    startsWithRouteSegment(route, "/documents") ||
    startsWithRouteSegment(route, "/eisf") ||
    startsWithRouteSegment(route, "/econsent") ||
    startsWithRouteSegment(route, "/econsent-authoring") ||
    startsWithRouteSegment(route, "/icf-builder") ||
    startsWithRouteSegment(route, "/training")
  ) {
    return "REGULATORY_COMPLIANCE";
  }
  if (
    startsWithRouteSegment(route, "/admin") ||
    startsWithRouteSegment(route, "/users") ||
    startsWithRouteSegment(route, "/org") ||
    startsWithRouteSegment(route, "/settings") ||
    startsWithRouteSegment(route, "/forbidden") ||
    startsWithRouteSegment(route, "/unavailable")
  ) {
    return "PLATFORM_ADMIN_ACCESS";
  }

  switch (entityType) {
    case "CRF_FORM":
      return "DATA_CAPTURE_ECRF";
    case "SUBJECT":
    case "SUPPLY_KIT":
    case "SAE_CASE":
    case "MVR_VISIT":
      return "SUBJECT_MANAGEMENT_RTSM";
    case "CAPA_ACTION":
    case "TMF_DOCUMENT":
      return "REGULATORY_COMPLIANCE";
  }

  switch (category) {
    case "DATA_QUERY":
      return "DATA_CAPTURE_ECRF";
    case "SITE_OPERATIONS":
    case "MONITORING_FINDING":
    case "SAFETY_ADVERSE_EVENT":
      return "SUBJECT_MANAGEMENT_RTSM";
    case "PROTOCOL_DEVIATION":
      return "REGULATORY_COMPLIANCE";
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
  const entityType = (payload.entity_type || "").trim().toUpperCase();
  const entityId = (payload.entity_id || "").trim();
  if (
    entityType === "SUBJECT" &&
    entityId &&
    !SUBJECT_CODE_PATTERN.test(entityId)
  ) {
    return "Linked SUBJECT entity ID must be a pseudonymous clinical subject code (for example SUBJ-8812).";
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

function createSimulatedDemoTicket(
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
    entity_type: payload.entity_type,
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
    sla_paused_at: null,
    sla_total_paused_seconds: 0,
    sla_breached: false,
    sla_amber_warned: false,
    escalation_count: 0,
    due_date: null,
    resolution_code: null,
    root_cause_category: null,
    root_cause_summary: null,
    signature_action: null,
    signature_token: null,
    signature_meaning: null,
    signature_timestamp: null,
    signature_user: null,
    reason_for_change: reasonForChange,
    created_at: new Date(nowMs).toISOString(),
    updated_at: new Date(nowMs).toISOString(),
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

  /** Lists comments visible to the caller on a ticket (GET /api/v1/tickets/{ticket_id}/comments). */
  async listComments(ticketId: string): Promise<TicketCommentResponse[]> {
    const resp = await unwrapResponse(
      client.GET("/api/v1/tickets/{ticket_id}/comments", {
        params: { path: { ticket_id: ticketId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },

  /** Posts a comment on a ticket with mandatory X-Change-Reason (POST /api/v1/tickets/{ticket_id}/comments). */
  createComment(
    ticketId: string,
    payload: CreateTicketCommentRequest,
    reasonForChange: string
  ): Promise<TicketCommentResponse> {
    const trimmedReason = (reasonForChange || "").trim();
    if (!trimmedReason) {
      throw new ApiError(
        "X-Change-Reason justification is required to post a ticket comment.",
        400,
        "Bad Request"
      );
    }
    return unwrapResponse(
      client.POST("/api/v1/tickets/{ticket_id}/comments", {
        params: { path: { ticket_id: ticketId } },
        body: payload,
        headers: {
          "X-Change-Reason": encodeChangeReason(trimmedReason),
        },
      })
    );
  },

  /** Transitions ticket workflow status with SLA pause/resume and mandatory X-Change-Reason (POST /api/v1/tickets/{ticket_id}/transitions). */
  transitionTicket(
    ticketId: string,
    payload: TransitionTicketRequest,
    reasonForChange: string
  ): Promise<TicketResponse> {
    const trimmedReason = (reasonForChange || "").trim();
    if (!trimmedReason) {
      throw new ApiError(
        "X-Change-Reason justification is required to transition a ticket.",
        400,
        "Bad Request"
      );
    }
    return unwrapResponse(
      client.POST("/api/v1/tickets/{ticket_id}/transitions", {
        params: { path: { ticket_id: ticketId } },
        body: payload,
        headers: {
          "X-Change-Reason": encodeChangeReason(trimmedReason),
        },
      })
    );
  },

  /** Requests a single-use 21 CFR Part 11 step-up signature token for signing a resolved ticket (POST /api/v1/auth/signature-verification). */
  verifyTicketSignature({
    ticketId,
    username,
    password,
    totp,
  }: TicketStepUpVerificationParams): Promise<{ signature_token: string }> {
    const action = `/api/v1/tickets/${encodeURIComponent(ticketId)}/sign`;
    return requestStepUpSignature<{ signature_token: string }>(
      {
        username,
        password: password || "",
        totp: totp || undefined,
        action,
        semanticAction: "tickets.ticket.sign",
      },
      { requireSigToken: false }
    );
  },

  /** Captures an authoritative 21 CFR Part 11 electronic signature on a resolved ticket (POST /api/v1/tickets/{ticket_id}/sign). */
  signTicket(
    ticketId: string,
    payload: SignTicketRequest,
    sigToken: string,
    reasonForChange: string
  ): Promise<TicketResponse> {
    const trimmedReason = (reasonForChange || "").trim();
    if (!trimmedReason) {
      throw new ApiError(
        "X-Change-Reason justification is required to sign a ticket.",
        400,
        "Bad Request"
      );
    }
    if (!sigToken) {
      throw new ApiError(
        "Step-up X-Sig-Token is required for 21 CFR Part 11 electronic signatures.",
        401,
        "Unauthorized"
      );
    }
    return unwrapResponse(
      client.POST("/api/v1/tickets/{ticket_id}/sign", {
        params: { path: { ticket_id: ticketId } },
        body: payload,
        headers: {
          "X-Sig-Token": sigToken,
          "X-Change-Reason": encodeChangeReason(trimmedReason),
        },
      })
    );
  },

  /** Lists Merkle-chained 21 CFR Part 11 audit trail entries for a ticket (GET /api/v1/tickets/{ticket_id}/audit-trail). */
  async getAuditTrail(ticketId: string): Promise<TicketAuditEntry[]> {
    const resp = await unwrapResponse(
      client.GET("/api/v1/tickets/{ticket_id}/audit-trail", {
        params: { path: { ticket_id: ticketId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },
};
