import { DEMO_STUDY } from "demo-fixtures";
import {
  DEFAULT_CATEGORIES,
  DEFAULT_MOCK_ARTICLES,
  getFallbackContextualHelp,
} from "../../api/knowledge";
import {
  routeDemoTicketQueue,
  validateDemoTicketNoDirectPii,
} from "../../api/ticketsApi";
import { demoNowMs } from "../../utils/demoMode.js";
import { cloneJson, makeUuidFromIndex } from "./shared.js";

export function createKnowledgeAndTicketsDomain(ctx) {
  const { state, nextCounter } = ctx;

  state.knowledgeCategories = cloneJson(DEFAULT_CATEGORIES);
  state.knowledgeArticles = cloneJson(DEFAULT_MOCK_ARTICLES);
  state.tickets = [];

  function seed() {
    state.knowledgeCategories = cloneJson(DEFAULT_CATEGORIES);
    state.knowledgeArticles = cloneJson(DEFAULT_MOCK_ARTICLES);
    state.tickets = [
      {
        id: "tk-8801",
        reference: "SIM-2026-0041",
        title: "Investigational Product Cold-Chain Excursion at Site 101",
        description:
          "Cold-chain continuous temperature logger logged 9.4C during weekend storage.",
        category: "SUPPLY_EXCURSION",
        gxp_severity: "CRITICAL",
        priority: "CRITICAL",
        status: "OPEN",
        assigned_queue: "SUBJECT_MANAGEMENT_RTSM",
        assignee_role: "SUBJECT_MANAGEMENT_RTSM",
        assignee_user: null,
        reporter: "cra_monitor",
        created_by: "cra_monitor",
        study_id: DEMO_STUDY.code || "CADENCE-101",
        site_id: "SITE-101",
        subject_id: null,
        related_entity_type: "SUPPLY_KIT",
        related_entity_id: "KIT-9941",
        source_route: "/ssu",
        acknowledge_target_at: "2026-08-04T10:30:00Z",
        sla_target_at: "2026-08-06T09:30:00Z",
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: "Initial cold-chain deviation triage",
        created_at: "2026-08-04T09:30:00Z",
        version_index: 1,
      },
      {
        id: "tk-8802",
        reference: "SIM-2026-0042",
        title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
        description:
          "Subject SUBJ-8812 PK blood sample collection missed due to scheduling conflict.",
        category: "PROTOCOL_DEVIATION",
        gxp_severity: "MAJOR",
        priority: "HIGH",
        status: "IN_PROGRESS",
        assigned_queue: "DATA_CAPTURE_ECRF",
        assignee_role: "DATA_CAPTURE_ECRF",
        assignee_user: null,
        reporter: "site_crc",
        created_by: "site_crc",
        study_id: DEMO_STUDY.code || "CADENCE-101",
        site_id: "SITE-102",
        subject_id: "SUBJ-8812",
        related_entity_type: "SUBJECT",
        related_entity_id: "SUBJ-8812",
        source_route: "/ecrf",
        acknowledge_target_at: "2026-08-03T13:30:00Z",
        sla_target_at: "2026-08-05T09:30:00Z",
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: "Logged protocol window deviation",
        created_at: "2026-08-03T09:30:00Z",
        version_index: 2,
      },
    ];
  }

  const methods = {
    resolveContextualHelp(route, persona) {
      return cloneJson(getFallbackContextualHelp(route, persona));
    },

    listTickets(filters = {}) {
      let results = [...state.tickets];
      if (filters.status) {
        results = results.filter((t) => t.status === filters.status);
      }
      if (filters.category) {
        results = results.filter((t) => t.category === filters.category);
      }
      if (filters.assigned_queue) {
        results = results.filter(
          (t) => t.assigned_queue === filters.assigned_queue
        );
      }
      if (filters.study_id) {
        const requestedStudy = String(filters.study_id).trim().toUpperCase();
        const canonicalDemoStudies = new Set([
          String(DEMO_STUDY.code || "CADENCE-101")
            .trim()
            .toUpperCase(),
          "DEMO-01",
        ]);
        results = results.filter((t) => {
          const ticketStudy = String(t.study_id || "")
            .trim()
            .toUpperCase();
          if (ticketStudy === requestedStudy) return true;
          return (
            canonicalDemoStudies.has(requestedStudy) &&
            canonicalDemoStudies.has(ticketStudy)
          );
        });
      }
      if (filters.site_id) {
        results = results.filter((t) => t.site_id === filters.site_id);
      }
      if (filters.search) {
        const q = String(filters.search).toLowerCase();
        results = results.filter(
          (t) =>
            String(t.title || "")
              .toLowerCase()
              .includes(q) ||
            String(t.reference || "")
              .toLowerCase()
              .includes(q)
        );
      }
      return cloneJson(results);
    },

    getTicket(ticketId) {
      const found = state.tickets.find(
        (t) => t.id === ticketId || t.reference === ticketId
      );
      return found ? cloneJson(found) : null;
    },

    createTicket(body, reasonForChange) {
      const title = String(body?.title || "").trim();
      const description = String(body?.description || "").trim();
      const reason = String(reasonForChange || "").trim();
      if (!title || !description) {
        return {
          invalid: true,
          problem: {
            title: "Invalid ticket request",
            status: 400,
            detail: "Ticket title and description are required.",
          },
        };
      }
      if (!reason) {
        return {
          invalid: true,
          problem: {
            title: "Missing X-Change-Reason",
            status: 400,
            detail: "X-Change-Reason header is required for ticket creation.",
          },
        };
      }
      const piiViolation = validateDemoTicketNoDirectPii(body || {});
      if (piiViolation) {
        return {
          invalid: true,
          problem: {
            title: "Direct participant PII is prohibited",
            status: 400,
            detail: piiViolation,
          },
        };
      }

      const idx = nextCounter();
      const nowMs = demoNowMs(idx);
      const nowIso = new Date(nowMs).toISOString();
      const year = new Date(nowMs).getUTCFullYear();
      const seq = String(idx).padStart(4, "0");
      const category = String(body?.category || "SYSTEM_SUPPORT").toUpperCase();
      const priority = String(body?.priority || "MEDIUM").toUpperCase();
      const severity = String(body?.gxp_severity || "MINOR").toUpperCase();
      const assignedQueue = routeDemoTicketQueue({
        category,
        source_route: body?.source_route,
        persona: body?.persona,
      });

      const created = {
        id: makeUuidFromIndex("00007100", idx),
        reference: `SIM-${year}-${seq}`,
        title,
        description,
        category,
        priority,
        gxp_severity: severity,
        status: "OPEN",
        assigned_queue: assignedQueue,
        assignee_role: assignedQueue,
        assignee_user: null,
        reporter: body?.persona || "usr-demo-operator",
        created_by: body?.persona || "usr-demo-operator",
        study_id: body?.study_id || null,
        site_id: body?.site_id || null,
        subject_id: body?.subject_id || null,
        related_entity_type: body?.entity_type || null,
        related_entity_id: body?.entity_id || null,
        source_route: body?.source_route || null,
        acknowledge_target_at: new Date(nowMs + 4 * 3600_000).toISOString(),
        sla_target_at: new Date(nowMs + 24 * 3600_000).toISOString(),
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change: reason,
        created_at: nowIso,
        version_index: 1,
      };
      state.tickets.unshift(created);
      ctx.recordActivity(
        "tickets.created",
        "tickets",
        "ticket",
        `ticket:${created.reference}`,
        created.created_by
      );
      return { data: cloneJson(created) };
    },
  };

  return { seed, methods };
}
