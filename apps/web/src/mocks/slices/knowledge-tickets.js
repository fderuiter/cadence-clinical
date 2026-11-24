import {
  DEFAULT_CATEGORIES,
  DEFAULT_MOCK_ARTICLES,
  compileSopMarkdownToHtml,
  getFallbackContextualHelp,
} from "../../api/knowledge";
import {
  routeDemoTicketQueue,
  validateDemoTicketNoDirectPii,
} from "../../api/ticketsApi";
import { demoNowMs } from "../../utils/demoMode.js";
import {
  cloneJson,
  makeUuidFromIndex,
  readJsonBody,
  readRequiredReasonHeader,
} from "./shared.js";

function createInitialTickets() {
  return [
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
      study_id: "STUDY-ONC-202",
      site_id: "SITE-101",
      subject_id: null,
      related_entity_type: "SUPPLY_KIT",
      related_entity_id: "KIT-9941",
      source_route: "/ssu",
      acknowledge_target_at: "2026-08-04T10:30:00Z", // deid-ignore
      sla_target_at: "2026-08-06T09:30:00Z", // deid-ignore
      sla_breached: false,
      sla_amber_warned: false,
      escalation_count: 0,
      reason_for_change: "Initial cold-chain deviation triage",
      created_at: "2026-08-04T09:30:00Z", // deid-ignore
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
      study_id: "STUDY-ONC-202",
      site_id: "SITE-102",
      subject_id: "SUBJ-8812",
      related_entity_type: "SUBJECT",
      related_entity_id: "SUBJ-8812",
      source_route: "/ecrf",
      acknowledge_target_at: "2026-08-03T13:30:00Z", // deid-ignore
      sla_target_at: "2026-08-05T09:30:00Z", // deid-ignore
      sla_breached: false,
      sla_amber_warned: false,
      escalation_count: 0,
      reason_for_change: "Logged protocol window deviation",
      created_at: "2026-08-03T09:30:00Z", // deid-ignore
      version_index: 2,
    },
  ];
}

export function createKnowledgeTicketsSliceStore(ctx) {
  const { state, nextCounter } = ctx;

  function reset() {
    state.knowledgeCategories = cloneJson(DEFAULT_CATEGORIES);
    state.knowledgeArticles = cloneJson(DEFAULT_MOCK_ARTICLES);
    state.tickets = createInitialTickets();
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
        results = results.filter((t) => t.study_id === filters.study_id);
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

  return {
    reset,
    methods,
  };
}

export function createKnowledgeTicketsSliceHandlers(
  store,
  { http, HttpResponse }
) {
  return [
    http.get("*/api/v1/knowledge/contextual-help", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.resolveContextualHelp(
          url.searchParams.get("route") || "/ecrf",
          url.searchParams.get("persona") || "super_admin"
        )
      );
    }),
    http.get("*/api/v1/knowledge/categories", () =>
      HttpResponse.json(store.state.knowledgeCategories)
    ),
    http.post("*/api/v1/knowledge/categories", async ({ request }) => {
      const body = await readJsonBody(request);
      const cat = {
        id: `cat-${Date.now()}`,
        is_deleted: false,
        created_at: new Date().toISOString(),
        ...body,
      };
      store.state.knowledgeCategories.push(cat);
      return HttpResponse.json(cat, { status: 201 });
    }),
    http.get("*/api/v1/knowledge/articles", ({ request }) => {
      const url = new URL(request.url);
      const status = url.searchParams.get("status_filter");
      const categoryId = url.searchParams.get("category_id");
      const q = (url.searchParams.get("q") || "").trim().toLowerCase();
      let list = [...store.state.knowledgeArticles];
      if (status && status !== "ALL") {
        list = list.filter((a) => a.status === status);
      }
      if (categoryId) {
        list = list.filter((a) => a.category_id === categoryId);
      }
      if (q) {
        list = list.filter((a) => {
          const title = String(a.title || "").toLowerCase();
          const body = String(
            a.body_markdown || a.body_html || ""
          ).toLowerCase();
          const tags = Array.isArray(a.tags)
            ? a.tags.some((t) => String(t).toLowerCase().includes(q))
            : false;
          return title.includes(q) || body.includes(q) || tags;
        });
      }
      return HttpResponse.json(list);
    }),
    http.post("*/api/v1/knowledge/articles", async ({ request }) => {
      const body = await readJsonBody(request);
      const compiledHtml =
        body?.body_html ||
        (body?.body_markdown
          ? compileSopMarkdownToHtml(String(body.body_markdown))
          : null);
      const created = {
        id: `art-${Date.now()}`,
        slug: `sop-demo-${Date.now()}`,
        status: "DRAFT",
        version_index: 1,
        version_label: "1.0",
        tags: body?.tags || ["demo-sop"],
        created_at: new Date().toISOString(),
        updated_at: new Date().toISOString(),
        ...body,
        ...(compiledHtml ? { body_html: compiledHtml } : {}),
      };
      store.state.knowledgeArticles.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.get(
      "*/api/v1/knowledge/articles/:articleId/versions",
      ({ params }) => {
        const found =
          store.state.knowledgeArticles.find(
            (a) => a.id === params.articleId || a.slug === params.articleId
          ) || null;
        const bodyMarkdown = found?.body_markdown ?? null;
        const bodyHtml =
          found?.body_html ||
          (bodyMarkdown
            ? compileSopMarkdownToHtml(String(bodyMarkdown))
            : null);
        return HttpResponse.json([
          {
            id: `ver-${params.articleId}`,
            article_id: found?.id || String(params.articleId),
            version_index: found?.version_index ?? 1,
            version_label: found?.version_label || "1.0",
            body_markdown: bodyMarkdown,
            body_html: bodyHtml,
            status_at_snapshot: found?.status || "PUBLISHED",
            is_locked: true,
            created_by: found?.approved_by || "qa_director",
            reason_for_change: "Published controlled SOP",
            created_at: "2026-06-01T11:15:00Z", // deid-ignore
          },
        ]);
      }
    ),
    http.get("*/api/v1/knowledge/articles/:articleId/audit-log", ({ params }) =>
      HttpResponse.json([
        {
          id: `kaud-${params.articleId}`,
          action: "ARTICLE_PUBLISHED",
          actor_id: "qa_director",
          timestamp: "2026-06-01T11:15:00Z", // deid-ignore
          reason_for_change: "Initial controlled publication",
        },
      ])
    ),
    http.get("*/api/v1/knowledge/articles/:articleId", ({ params }) => {
      const found =
        store.state.knowledgeArticles.find(
          (a) => a.id === params.articleId || a.slug === params.articleId
        ) || store.state.knowledgeArticles[0];
      return HttpResponse.json(found);
    }),

    http.get("*/api/v1/tickets", ({ request }) => {
      const url = new URL(request.url);
      return HttpResponse.json(
        store.listTickets({
          status: url.searchParams.get("status") || undefined,
          category: url.searchParams.get("category") || undefined,
          assigned_queue: url.searchParams.get("assigned_queue") || undefined,
          study_id: url.searchParams.get("study_id") || undefined,
          site_id: url.searchParams.get("site_id") || undefined,
          search: url.searchParams.get("search") || undefined,
        })
      );
    }),
    http.post("*/api/v1/tickets", async ({ request }) => {
      const decodedReason = readRequiredReasonHeader(request);
      const body = await readJsonBody(request);
      const result = store.createTicket(body, decodedReason);
      if (result.invalid) {
        return HttpResponse.json(result.problem, { status: 400 });
      }
      return HttpResponse.json(result.data, { status: 201 });
    }),
    http.get("*/api/v1/tickets/:ticketId", ({ params }) => {
      const found = store.getTicket(
        decodeURIComponent(String(params.ticketId))
      );
      if (!found) {
        return HttpResponse.json(
          {
            title: "Ticket not found",
            status: 404,
            detail: `No ticket exists for ${params.ticketId}.`,
          },
          { status: 404 }
        );
      }
      return HttpResponse.json(found);
    }),
  ];
}
