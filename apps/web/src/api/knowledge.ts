import { openApiClient, unwrapResponse } from "./client";

/**
 * Default fallback categories for offline sandbox demo mode.
 */
export const DEFAULT_CATEGORIES = [
  {
    id: "cat-clinical-ops",
    name: "Clinical Operations",
    slug: "clinical-operations",
    description: "eCRF data capture, subject enrollment, and visit procedures.",
    persona_visibility: [
      "site_crc",
      "cra_monitor",
      "data_manager",
      "super_admin",
    ],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
  {
    id: "cat-study-authoring",
    name: "Study Authoring & MDR",
    slug: "study-authoring-mdr",
    description:
      "CDISC USDM protocol authoring, SoA matrix design, and digitizer workflows.",
    persona_visibility: ["sponsor_designer", "data_manager", "super_admin"],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
  {
    id: "cat-site-monitoring",
    name: "Site Monitoring & CTMS",
    slug: "site-monitoring-ctms",
    description:
      "CRA monitoring visits, source data verification, and trip reports.",
    persona_visibility: ["cra_monitor", "site_crc", "super_admin"],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
  {
    id: "cat-regulatory-compliance",
    name: "Regulatory & Quality Compliance",
    slug: "regulatory-compliance",
    description:
      "21 CFR Part 11 compliance, audit trail inspection, and digital signatures.",
    persona_visibility: [
      "auditor",
      "super_admin",
      "sponsor_designer",
      "data_manager",
    ],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
  {
    id: "cat-data-management",
    name: "Data Management & Coding",
    slug: "data-management-coding",
    description:
      "MedDRA / WHODrug coding, query management, and data lock workflows.",
    persona_visibility: ["data_manager", "sponsor_designer", "super_admin"],
    parent_id: null,
    is_deleted: false,
    created_at: "2026-01-15T08:00:00Z",
    created_by: "system_admin",
  },
];

/**
 * Default fallback clinical SOPs for offline sandbox demo mode.
 */
export const DEFAULT_CONTEXTUAL_SOPS: Record<
  string,
  Record<string, unknown>
> = {
  "/ecrf": {
    matched_mapping: {
      id: "map-ecrf-01",
      route_pattern: "/ecrf/*",
      persona: "site_crc",
      article_id: "art-ecrf-sop",
      section_anchor: "#subject-enrollment-and-visit-entry",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-ecrf-sop",
      title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
      slug: "sop-ecrf-201-subject-enrollment",
      status: "PUBLISHED",
      version_index: 2,
      version_label: "2.1",
      tags: ["ecrf", "enrollment", "gxp-edc", "site-sop"],
      category_id: "cat-clinical-ops",
      category_name: "Clinical Operations",
      author_user_id: "lead_data_manager",
      last_edited_by: "lead_data_manager",
      approved_by: "qa_director",
      created_at: "2026-02-10T10:00:00Z",
      updated_at: "2026-05-18T14:30:00Z",
      body_markdown: `## Purpose & Scope
This Standard Operating Procedure (SOP) governs all investigator site personnel executing electronic Case Report Form (eCRF) data capture, subject registration, and visit assessment recording within the Cadence Clinical Research Platform.`,
      body_html: `<h2>Purpose & Scope</h2>`,
    },
    primary_version: {
      id: "ver-ecrf-201",
      version_index: 2,
      version_label: "2.1",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "lead_data_manager",
      reason_for_change: "Annual review & protocol amendment 2 alignment",
      created_at: "2026-05-18T14:30:00Z",
    },
    section_anchor: "#subject-enrollment-and-visit-entry",
    related_articles: [],
  },
  "/mdr": {
    matched_mapping: {
      id: "map-mdr-01",
      route_pattern: "/mdr/*",
      persona: "sponsor_designer",
      article_id: "art-mdr-sop",
      section_anchor: "#schedule-of-activities-matrix-authoring",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-mdr-sop",
      title: "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
      slug: "sop-mdr-104-soa-matrix-authoring",
      status: "PUBLISHED",
      version_index: 3,
      version_label: "3.0",
      tags: ["mdr", "usdm", "soa-matrix", "study-design"],
      category_id: "cat-study-authoring",
      category_name: "Study Authoring & MDR",
      author_user_id: "sponsor_study_lead",
      last_edited_by: "sponsor_study_lead",
      approved_by: "qa_director",
      created_at: "2026-01-20T09:00:00Z",
      updated_at: "2026-06-01T11:15:00Z",
      body_markdown: `## Protocol Metadata Management & SoA Design`,
      body_html: `<h2>Protocol Metadata Management & SoA Design</h2>`,
    },
    primary_version: {
      id: "ver-mdr-104",
      version_index: 3,
      version_label: "3.0",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "sponsor_study_lead",
      reason_for_change: "CDISC USDM v3.0 standard alignment",
      created_at: "2026-06-01T11:15:00Z",
    },
    section_anchor: "#schedule-of-activities-matrix-authoring",
    related_articles: [],
  },
  "/ctms": {
    matched_mapping: {
      id: "map-ctms-01",
      route_pattern: "/ctms/*",
      persona: "cra_monitor",
      article_id: "art-ctms-sop",
      section_anchor: "#site-monitoring-and-sdv",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-ctms-sop",
      title: "SOP-CTMS-302: Site Monitoring Visits & Source Data Verification",
      slug: "sop-ctms-302-site-monitoring",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.0",
      tags: ["ctms", "monitoring", "sdv", "cra"],
      category_id: "cat-site-monitoring",
      category_name: "Site Monitoring & CTMS",
      author_user_id: "cra_monitor",
      last_edited_by: "cra_monitor",
      approved_by: "qa_director",
      created_at: "2026-01-20T09:00:00Z",
      updated_at: "2026-06-01T11:15:00Z",
      body_markdown: "## Site Monitoring & SDV Procedures",
      body_html: "<h2>Site Monitoring & SDV Procedures</h2>",
    },
    primary_version: {
      id: "ver-ctms-302",
      version_index: 1,
      version_label: "1.0",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "cra_monitor",
      reason_for_change: "Initial release",
      created_at: "2026-06-01T11:15:00Z",
    },
    section_anchor: "#site-monitoring-and-sdv",
    related_articles: [],
  },
  "/audit": {
    matched_mapping: {
      id: "map-audit-01",
      route_pattern: "/audit/*",
      persona: "auditor",
      article_id: "art-audit-sop",
      section_anchor: "#gxp-audit-trail-inspection",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-audit-sop",
      title:
        "SOP-AUD-401: 21 CFR Part 11 Audit Trail & Qualification Inspection",
      slug: "sop-aud-401-audit-trail-inspection",
      status: "PUBLISHED",
      version_index: 1,
      version_label: "1.0",
      tags: ["audit", "part11", "gxp", "compliance"],
      category_id: "cat-regulatory-compliance",
      category_name: "Regulatory & Quality Compliance",
      author_user_id: "qa_director",
      last_edited_by: "qa_director",
      approved_by: "qa_director",
      created_at: "2026-01-20T09:00:00Z",
      updated_at: "2026-06-01T11:15:00Z",
      body_markdown: "## Audit Trail Inspection Guidelines",
      body_html: "<h2>Audit Trail Inspection Guidelines</h2>",
    },
    primary_version: {
      id: "ver-aud-401",
      version_index: 1,
      version_label: "1.0",
      status_at_snapshot: "PUBLISHED",
      is_locked: true,
      created_by: "qa_director",
      reason_for_change: "Initial release",
      created_at: "2026-06-01T11:15:00Z",
    },
    section_anchor: "#gxp-audit-trail-inspection",
    related_articles: [],
  },
};

export const DEFAULT_MOCK_ARTICLES = [
  DEFAULT_CONTEXTUAL_SOPS["/ecrf"].primary_article,
  DEFAULT_CONTEXTUAL_SOPS["/mdr"].primary_article,
];

let memoryArticles = JSON.parse(JSON.stringify(DEFAULT_MOCK_ARTICLES));
let memoryCategories = JSON.parse(JSON.stringify(DEFAULT_CATEGORIES));
const memoryVersions = new Map<string, unknown>();
const memoryAuditLogs = new Map<string, unknown>();

export function getFallbackContextualHelp(route?: string, _persona?: string) {
  const cleanRoute = (route || "/").split("?")[0].replace(/\/+$/, "") || "/";

  for (const [key, fallback] of Object.entries(DEFAULT_CONTEXTUAL_SOPS)) {
    if (cleanRoute === key || cleanRoute.startsWith(`${key}/`)) {
      return JSON.parse(JSON.stringify(fallback));
    }
  }

  if (
    cleanRoute.includes("subject") ||
    cleanRoute.includes("form") ||
    cleanRoute.includes("visit")
  ) {
    return JSON.parse(JSON.stringify(DEFAULT_CONTEXTUAL_SOPS["/ecrf"]));
  }

  return {
    matched_mapping: null,
    primary_article: null,
    primary_version: null,
    section_anchor: null,
    related_articles: [],
    article: null,
    version: null,
  };
}

export const knowledgeService = {
  async resolveContextualHelp({
    route,
    persona,
  }: { route?: string; persona?: string } = {}) {
    try {
      const response = (await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/contextual-help", {
          params: {
            query: {
              route: route || "/",
              persona,
            },
          },
        })
      )) as Record<string, unknown>;
      if (response && (response.primary_article || response.article)) {
        return response;
      }
      return getFallbackContextualHelp(route, persona);
    } catch {
      return getFallbackContextualHelp(route, persona);
    }
  },

  async getArticle(articleId: string) {
    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/articles/{article_id}", {
          params: { path: { article_id: articleId } },
        })
      );
    } catch {
      const found = memoryArticles.find(
        (a: Record<string, unknown>) =>
          a.id === articleId || a.slug === articleId
      );
      if (found) {
        return JSON.parse(JSON.stringify(found));
      }
      throw new Error(`Article with id ${articleId} not found`);
    }
  },

  async listArticles(params: { status?: string; categoryId?: string } = {}) {
    try {
      const resp = await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/articles", {
          params: {
            query: {
              status_filter: (params.status ?? undefined) as unknown as
                | "DRAFT"
                | "IN_REVIEW"
                | "APPROVED"
                | "PUBLISHED"
                | "ARCHIVED"
                | "REJECTED"
                | "SUPERSEDED"
                | null
                | undefined,
              category_id: params.categoryId,
            },
          },
        })
      );
      if (Array.isArray(resp)) return resp;
      return memoryArticles;
    } catch {
      let filtered = [...memoryArticles];
      if (params.status && params.status !== "ALL") {
        filtered = filtered.filter(
          (a: Record<string, unknown>) => a.status === params.status
        );
      }
      if (params.categoryId) {
        filtered = filtered.filter(
          (a: Record<string, unknown>) => a.category_id === params.categoryId
        );
      }
      return JSON.parse(JSON.stringify(filtered));
    }
  },

  async listCategories() {
    try {
      const resp = await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/categories")
      );
      if (Array.isArray(resp)) return resp;
      return memoryCategories;
    } catch {
      return JSON.parse(
        JSON.stringify(
          memoryCategories.filter((c: Record<string, unknown>) => !c.is_deleted)
        )
      );
    }
  },

  async createCategory(payload: Record<string, unknown>) {
    try {
      return await unwrapResponse(
        openApiClient.POST("/api/v1/knowledge/categories", {
          body: payload as never,
        })
      );
    } catch {
      const name = (payload.name as string) || "New Category";
      const newCat = {
        id: `cat-${Date.now()}`,
        name,
        slug:
          (payload.slug as string) ||
          name.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
        description: (payload.description as string) || "",
        persona_visibility: payload.persona_visibility || [],
        parent_id: payload.parent_id || null,
        is_deleted: false,
        created_at: new Date().toISOString(),
        created_by: "current_user",
      };
      memoryCategories.push(newCat);
      return newCat;
    }
  },

  async deleteCategory(categoryId: string, reason = "Soft deleted") {
    try {
      return await unwrapResponse(
        openApiClient.DELETE("/api/v1/knowledge/categories/{category_id}", {
          params: { path: { category_id: categoryId } },
          headers: { "X-Change-Reason": reason },
        })
      );
    } catch {
      const idx = memoryCategories.findIndex(
        (c: Record<string, unknown>) => c.id === categoryId
      );
      if (idx !== -1) {
        memoryCategories[idx].is_deleted = true;
      }
      return { success: true, id: categoryId };
    }
  },

  async createArticle(payload: Record<string, unknown>) {
    try {
      return await unwrapResponse(
        openApiClient.POST("/api/v1/knowledge/articles", {
          body: payload as never,
        })
      );
    } catch {
      const title = (payload.title as string) || "New Article";
      const category = memoryCategories.find(
        (c: Record<string, unknown>) => c.id === payload.category_id
      );
      const newArt = {
        id: `art-${Date.now()}`,
        title,
        slug:
          (payload.slug as string) ||
          title.toLowerCase().replace(/[^a-z0-9]+/g, "-"),
        status: "DRAFT",
        version_index: 1,
        version_label: (payload.version_label as string) || "0.1",
        category_id: payload.category_id,
        category_name: category ? category.name : "General Guidance",
        tags: Array.isArray(payload.tags)
          ? payload.tags
          : typeof payload.tags === "string"
            ? (payload.tags as string).split(",").map((t) => t.trim())
            : [],
        author_user_id: "current_user",
        last_edited_by: "current_user",
        approved_by: null,
        created_at: new Date().toISOString(),
        updated_at: new Date().toISOString(),
        body_markdown: (payload.body_markdown as string) || "",
        body_html: `<p>${payload.body_markdown || ""}</p>`,
      };
      memoryArticles.unshift(newArt);
      return newArt;
    }
  },

  async updateArticleDraft(
    articleId: string,
    payload: Record<string, unknown>
  ) {
    try {
      return await unwrapResponse(
        openApiClient.PUT("/api/v1/knowledge/articles/{article_id}", {
          params: { path: { article_id: articleId } },
          body: payload as never,
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      if (payload.title) art.title = payload.title;
      if (payload.slug) art.slug = payload.slug;
      if (payload.category_id) {
        art.category_id = payload.category_id;
        const cat = memoryCategories.find(
          (c: Record<string, unknown>) => c.id === payload.category_id
        );
        if (cat) art.category_name = cat.name;
      }
      if (payload.tags) art.tags = payload.tags;
      if (payload.body_markdown !== undefined) {
        art.body_markdown = payload.body_markdown;
        art.body_html = `<p>${payload.body_markdown}</p>`;
      }
      art.last_edited_by = "current_user";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async submitArticleReview(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    try {
      return await unwrapResponse(
        openApiClient.POST(
          "/api/v1/knowledge/articles/{article_id}/submit-review",
          {
            params: { path: { article_id: articleId } },
            body: payload as never,
          }
        )
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      art.status = "IN_REVIEW";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async approveArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    try {
      return await unwrapResponse(
        openApiClient.POST("/api/v1/knowledge/articles/{article_id}/approve", {
          params: { path: { article_id: articleId } },
          body: payload as never,
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      art.status = "APPROVED";
      art.approved_by = "current_reviewer";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async rejectArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    try {
      return await unwrapResponse(
        openApiClient.POST("/api/v1/knowledge/articles/{article_id}/reject", {
          params: { path: { article_id: articleId } },
          body: payload as never,
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      art.status = "REJECTED";
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async publishArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    try {
      return await unwrapResponse(
        openApiClient.POST("/api/v1/knowledge/articles/{article_id}/publish", {
          params: { path: { article_id: articleId } },
          body: payload as never,
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      art.status = "PUBLISHED";
      if (payload.version_label) art.version_label = payload.version_label;
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async transitionArticle(
    articleId: string,
    payload: { target_status: string }
  ) {
    try {
      return await unwrapResponse(
        openApiClient.POST(
          "/api/v1/knowledge/articles/{article_id}/transition",
          {
            params: { path: { article_id: articleId } },
            body: payload as never,
          }
        )
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) throw new Error("Article not found");
      art.status = payload.target_status;
      art.updated_at = new Date().toISOString();
      return JSON.parse(JSON.stringify(art));
    }
  },

  async listArticleVersions(articleId: string) {
    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/articles/{article_id}/versions", {
          params: { path: { article_id: articleId } },
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) return [];
      const cached = memoryVersions.get(articleId);
      if (cached) return cached;
      return [
        {
          id: `ver-${art.id}-1`,
          article_id: art.id,
          version_index: 1,
          version_label: "1.0",
          status_at_snapshot: "PUBLISHED",
          body_markdown: art.body_markdown,
          body_html: art.body_html,
          is_locked: true,
          created_by: art.author_user_id,
          created_at: art.created_at,
          reason_for_change: "Initial baseline SOP creation",
        },
      ];
    }
  },

  async getArticleAuditLog(articleId: string) {
    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/articles/{article_id}/audit-log", {
          params: { path: { article_id: articleId } },
        })
      );
    } catch {
      const art = memoryArticles.find(
        (a: Record<string, unknown>) => a.id === articleId
      );
      if (!art) return [];
      const cached = memoryAuditLogs.get(articleId);
      if (cached) return cached;
      return [
        {
          id: `log-${art.id}-1`,
          article_id: art.id,
          event_type: "ARTICLE_CREATED",
          actor_user_id: art.author_user_id,
          target_status: "DRAFT",
          version_index: 1,
          version_label: "0.1",
          reason_for_change: "Initial GxP document authoring initiated",
          sha256_hash:
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
          created_at: art.created_at,
        },
      ];
    }
  },

  async searchArticles(query: string) {
    const q = (query || "").trim().toLowerCase();
    if (!q) return this.listArticles();
    const all = (await this.listArticles()) as Array<Record<string, unknown>>;
    return all.filter((art) => {
      const matchTitle = (art.title as string)?.toLowerCase().includes(q);
      const matchBody = ((art.body_markdown || art.body_html || "") as string)
        .toLowerCase()
        .includes(q);
      const matchTags = Array.isArray(art.tags)
        ? art.tags.some((t: string) => t.toLowerCase().includes(q))
        : typeof art.tags === "string"
          ? (art.tags as string).toLowerCase().includes(q)
          : false;
      const matchCategory = (art.category_name as string)
        ?.toLowerCase()
        .includes(q);
      return matchTitle || matchBody || matchTags || matchCategory;
    });
  },
};
