import { http, HttpResponse } from "msw";
import { readJsonBody } from "./shared.js";

export function createKnowledgeAndTerminologyHandlers(store) {
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
      };
      store.state.knowledgeArticles.push(created);
      return HttpResponse.json(created, { status: 201 });
    }),
    http.get("*/api/v1/knowledge/articles/:articleId/versions", ({ params }) =>
      HttpResponse.json([
        {
          id: `ver-${params.articleId}`,
          version_index: 1,
          version_label: "1.0",
          status_at_snapshot: "PUBLISHED",
          is_locked: true,
          created_by: "qa_director",
          reason_for_change: "Published controlled SOP",
          created_at: "2026-06-01T11:15:00Z",
        },
      ])
    ),
    http.get("*/api/v1/knowledge/articles/:articleId/audit-log", ({ params }) =>
      HttpResponse.json([
        {
          id: `kaud-${params.articleId}`,
          action: "ARTICLE_PUBLISHED",
          actor_id: "qa_director",
          timestamp: "2026-06-01T11:15:00Z",
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
    http.get("*/api/v1/terminology/validate/:code", ({ params }) => {
      const code = decodeURIComponent(String(params.code));
      return HttpResponse.json({
        code,
        valid: true,
        preferred_term: `CDISC Controlled Concept (${code})`,
        codelist_code: "C66741",
        synonyms: [code],
      });
    }),
    http.get("*/api/v1/terminology/search", ({ request }) => {
      const url = new URL(request.url);
      const term = url.searchParams.get("term") || "VS";
      return HttpResponse.json({
        total: 2,
        items: [
          {
            code: "C25298",
            preferred_term: `Systolic Blood Pressure (${term})`,
            codelist_code: "C66741",
            definition:
              "Maximum arterial pressure during ventricular contraction.",
          },
          {
            code: "C25299",
            preferred_term: "Diastolic Blood Pressure",
            codelist_code: "C66741",
            definition: "Minimum arterial pressure between contractions.",
          },
        ],
      });
    }),
    http.get("*/api/v1/mdr/concepts", () =>
      HttpResponse.json([
        {
          code: "SYSBP",
          nci_code: "C25298",
          name: "Systolic Blood Pressure",
          domain: "VS",
          datatype: "integer",
          unit: "mm[Hg]",
        },
        {
          code: "DIABP",
          nci_code: "C25299",
          name: "Diastolic Blood Pressure",
          domain: "VS",
          datatype: "integer",
          unit: "mm[Hg]",
        },
      ])
    ),
    http.get("*/api/v1/studies/:studyId/terminology-validation", ({ params }) =>
      HttpResponse.json({
        study_id: params.studyId,
        valid: true,
        issues: [],
      })
    ),
    http.get("*/api/v1/studies/:studyId/ct-validation", ({ params }) =>
      HttpResponse.json({
        study_id: params.studyId,
        valid: true,
        findings: [],
      })
    ),
    http.get("*/api/v1/studies/:studyId/rules", () =>
      HttpResponse.json(store.state.rules)
    ),
    http.post("*/api/v1/studies/:studyId/rules/validate", () =>
      HttpResponse.json({ valid: true, errors: [] })
    ),
    http.post("*/api/v1/studies/:studyId/rules/preview", () =>
      HttpResponse.json({ matched_count: 1, sample_subjects: ["SUBJ-1001"] })
    ),
    http.post(
      "*/api/v1/studies/:studyId/rules",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const created = {
          id: `rule-${Date.now()}`,
          study_id: params.studyId,
          is_active: true,
          ...body,
        };
        store.state.rules.push(created);
        return HttpResponse.json(created, { status: 201 });
      }
    ),
    http.put(
      "*/api/v1/studies/:studyId/rules/:ruleId",
      async ({ params, request }) => {
        const body = await readJsonBody(request);
        const found = store.state.rules.find((r) => r.id === params.ruleId);
        if (found) Object.assign(found, body);
        return HttpResponse.json(found || { id: params.ruleId, ...body });
      }
    ),
    http.delete("*/api/v1/studies/:studyId/rules/:ruleId", ({ params }) => {
      store.state.rules = store.state.rules.filter(
        (r) => r.id !== params.ruleId
      );
      return HttpResponse.json({ deleted: true, id: params.ruleId });
    }),
    http.get("*/api/v1/studies/:studyId", ({ params }) =>
      HttpResponse.json({
        id: params.studyId,
        code: params.studyId,
        title: `Synthetic Clinical Study ${params.studyId}`,
        status: "ACTIVE",
      })
    ),
    http.get("*/api/v1/studies", () =>
      HttpResponse.json([...store.state.studies.values()])
    ),
  ];
}
