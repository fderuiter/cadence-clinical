import {
  DEFAULT_CATEGORIES,
  DEFAULT_CONTEXTUAL_SOPS,
  DEFAULT_MOCK_ARTICLES,
  getFallbackArticles,
  getFallbackCategories,
  getFallbackContextualHelp,
  knowledgeDemoAuthoringService,
} from "../stores/demo/knowledgeDemo";
import { isDemoBuild } from "../utils/demoMode.js";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { paths as KnowledgePaths } from "./generated/knowledge";

const knowledgeClient = createOpenApiClient<KnowledgePaths>();

export {
  DEFAULT_CATEGORIES,
  DEFAULT_CONTEXTUAL_SOPS,
  DEFAULT_MOCK_ARTICLES,
  getFallbackContextualHelp,
};

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
        knowledgeClient.GET("/api/v1/knowledge/contextual-help", {
          params: {
            query: {
              route: route || "/",
              persona,
            },
          },
        })
      )) as unknown as Record<string, unknown>;
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

  async listArticles(
    params: {
      status?: string;
      categoryId?: string;
      q?: string;
      persona?: string;
    } = {}
  ) {
    try {
      const resp = await unwrapResponse(
        knowledgeClient.GET("/api/v1/knowledge/articles", {
          params: {
            query: {
              status_filter: params.status ?? undefined,
              category_id: params.categoryId,
              q: params.q,
              persona: params.persona,
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

  async listCategories(params: { persona?: string } = {}) {
    try {
      const resp = await unwrapResponse(
        knowledgeClient.GET("/api/v1/knowledge/categories", {
          params: {
            query: {
              persona: params.persona,
            },
          },
        })
      );
      return Array.isArray(resp) ? resp : [];
    } catch (err) {
      if (isDemoBuild()) return getFallbackCategories();
      throw err;
    }
  },

  ...knowledgeDemoAuthoringService,

  async listArticleVersions(articleId: string) {
    const resp = await unwrapResponse(
      knowledgeClient.GET("/api/v1/knowledge/articles/{article_id}/versions", {
        params: { path: { article_id: articleId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },

  async getArticleAuditLog(articleId: string) {
    const resp = await unwrapResponse(
      knowledgeClient.GET("/api/v1/knowledge/articles/{article_id}/audit-log", {
        params: { path: { article_id: articleId } },
      })
    );
    return Array.isArray(resp) ? resp : [];
  },
};
