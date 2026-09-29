import { isDemoBuild } from "../utils/demoMode.js";
import { openApiClient, unwrapResponse } from "./client";

/**
 * Bundled offline help categories. Served only by a demo build when the API is unreachable (#4523).
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
 * Bundled offline help SOPs. Served only by a demo build when the API is unreachable (#4523).
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

const BUNDLED_ARTICLES = DEFAULT_MOCK_ARTICLES as Array<
  Record<string, unknown>
>;

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value));
}

/** Bundled, read-only offline help articles filtered like the list endpoint (demo builds only). */
function getFallbackArticles(
  params: { status?: string; categoryId?: string } = {}
) {
  let filtered = [...BUNDLED_ARTICLES];
  if (params.status && params.status !== "ALL") {
    filtered = filtered.filter((a) => a.status === params.status);
  }
  if (params.categoryId) {
    filtered = filtered.filter((a) => a.category_id === params.categoryId);
  }
  return clone(filtered);
}

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

/**
 * Knowledge base API. Writes, approvals, version history and audit logs always come from the server and throw on
 * failure: an approval, publish or audit entry is never synthesised locally (#4523). Only read-only help content
 * (contextual help, article list/detail, categories) may fall back to the bundled offline set, and only in a demo
 * build.
 */
export const knowledgeService = {
  async resolveContextualHelp({
    route,
    persona,
  }: { route?: string; persona?: string } = {}) {
    let response: Record<string, unknown>;
    try {
      response = (await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/contextual-help", {
          params: {
            query: {
              route: route || "/",
              persona,
            },
          },
        })
      )) as Record<string, unknown>;
    } catch (err) {
      if (isDemoBuild()) return getFallbackContextualHelp(route, persona);
      throw err;
    }
    if (response && (response.primary_article || response.article)) {
      return response;
    }
    // No mapped SOP: a demo build shows bundled help, any other build shows the server's (empty) answer.
    return isDemoBuild() ? getFallbackContextualHelp(route, persona) : response;
  },

  async getArticle(articleId: string) {
    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/articles/{article_id}", {
          params: { path: { article_id: articleId } },
        })
      );
    } catch (err) {
      if (!isDemoBuild()) throw err;
      const found = BUNDLED_ARTICLES.find(
        (a) => a.id === articleId || a.slug === articleId
      );
      if (found) {
        return clone(found);
      }
      throw new Error(`Article with id ${articleId} not found`, {
        cause: err,
      });
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
      return Array.isArray(resp) ? resp : [];
    } catch (err) {
      if (isDemoBuild()) return getFallbackArticles(params);
      throw err;
    }
  },

  async listCategories() {
    try {
      const resp = await unwrapResponse(
        openApiClient.GET("/api/v1/knowledge/categories")
      );
      return Array.isArray(resp) ? resp : [];
    } catch (err) {
      if (isDemoBuild()) return clone(DEFAULT_CATEGORIES);
      throw err;
    }
  },

  async createCategory(payload: Record<string, unknown>) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/categories", {
        body: payload as never,
      })
    );
  },

  async deleteCategory(categoryId: string, reason = "Soft deleted") {
    return await unwrapResponse(
      openApiClient.DELETE("/api/v1/knowledge/categories/{category_id}", {
        params: { path: { category_id: categoryId } },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  async createArticle(payload: Record<string, unknown>) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles", {
        body: payload as never,
      })
    );
  },

  async updateArticleDraft(
    articleId: string,
    payload: Record<string, unknown>
  ) {
    return await unwrapResponse(
      openApiClient.PUT("/api/v1/knowledge/articles/{article_id}", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async submitArticleReview(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST(
        "/api/v1/knowledge/articles/{article_id}/submit-review",
        {
          params: { path: { article_id: articleId } },
          body: payload as never,
        }
      )
    );
  },

  async approveArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/approve", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async rejectArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/reject", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async publishArticle(
    articleId: string,
    payload: Record<string, unknown> = {}
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/publish", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async transitionArticle(
    articleId: string,
    payload: { target_status: string }
  ) {
    return await unwrapResponse(
      openApiClient.POST("/api/v1/knowledge/articles/{article_id}/transition", {
        params: { path: { article_id: articleId } },
        body: payload as never,
      })
    );
  },

  async listArticleVersions(articleId: string) {
    return await unwrapResponse(
      openApiClient.GET("/api/v1/knowledge/articles/{article_id}/versions", {
        params: { path: { article_id: articleId } },
      })
    );
  },

  async getArticleAuditLog(articleId: string) {
    return await unwrapResponse(
      openApiClient.GET("/api/v1/knowledge/articles/{article_id}/audit-log", {
        params: { path: { article_id: articleId } },
      })
    );
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
