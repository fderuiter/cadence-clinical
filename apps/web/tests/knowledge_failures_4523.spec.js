import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import {
  knowledgeService,
  DEFAULT_MOCK_ARTICLES,
} from "../src/api/knowledge.js";
import { useToastStore } from "../src/stores/toasts.js";
import { router } from "../src/router/index.js";
import KnowledgeView from "../src/views/KnowledgeView.vue";

/**
 * #4523: knowledge-base writes, approvals, version history and audit logs must never be synthesised when the
 * backend fails. The real openapi-fetch client is used; only globalThis.fetch is replaced.
 */
function failEveryRequest(status) {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      new Response(JSON.stringify({ detail: `Upstream failure ${status}` }), {
        status,
        headers: { "Content-Type": "application/json" },
      })
    )
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

const ARTICLE_ID = DEFAULT_MOCK_ARTICLES[0].id;

const lifecycleCalls = {
  submitArticleReview: () =>
    knowledgeService.submitArticleReview(ARTICLE_ID, {
      reason_for_change: "Ready",
    }),
  approveArticle: () =>
    knowledgeService.approveArticle(ARTICLE_ID, {
      reason_for_change: "Verified",
    }),
  rejectArticle: () =>
    knowledgeService.rejectArticle(ARTICLE_ID, {
      reason_for_change: "Needs work",
    }),
  publishArticle: () =>
    knowledgeService.publishArticle(ARTICLE_ID, { version_label: "3.0" }),
  transitionArticle: () =>
    knowledgeService.transitionArticle(ARTICLE_ID, {
      target_status: "ARCHIVED",
    }),
  listArticleVersions: () => knowledgeService.listArticleVersions(ARTICLE_ID),
  getArticleAuditLog: () => knowledgeService.getArticleAuditLog(ARTICLE_ID),
  createArticle: () =>
    knowledgeService.createArticle({ title: "New SOP", category_id: "cat-1" }),
  updateArticleDraft: () =>
    knowledgeService.updateArticleDraft(ARTICLE_ID, { title: "Edited" }),
  createCategory: () => knowledgeService.createCategory({ name: "New" }),
};

const helpReads = {
  resolveContextualHelp: () =>
    knowledgeService.resolveContextualHelp({ route: "/ecrf" }),
  listArticles: () => knowledgeService.listArticles(),
  getArticle: () => knowledgeService.getArticle(ARTICLE_ID),
  listCategories: () => knowledgeService.listCategories(),
};

describe("knowledge API does not fabricate on failure (#4523)", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    setActivePinia(createPinia());
  });

  describe.each([500, 502, 504])(
    "when the gateway answers HTTP %i",
    (status) => {
      beforeEach(() => {
        failEveryRequest(status);
      });

      it.each(Object.keys(lifecycleCalls))(
        "%s throws instead of returning a locally changed article",
        async (name) => {
          await expect(lifecycleCalls[name]()).rejects.toMatchObject({
            status,
          });
        }
      );

      it("does not approve the article in any cached copy after a failed approval", async () => {
        await expect(lifecycleCalls.approveArticle()).rejects.toBeTruthy();
        expect(DEFAULT_MOCK_ARTICLES[0].approved_by).not.toBe(
          "current_reviewer"
        );
        expect(DEFAULT_MOCK_ARTICLES[0].status).toBe("PUBLISHED");
      });

      it.each(Object.keys(helpReads))(
        "%s throws outside a demo build",
        async (name) => {
          await expect(helpReads[name]()).rejects.toMatchObject({ status });
        }
      );

      it.each(Object.keys(helpReads))(
        "%s serves the bundled offline help in a demo build",
        async (name) => {
          vi.stubEnv("VITE_DEMO_MODE", "true");
          await expect(helpReads[name]()).resolves.toBeTruthy();
        }
      );

      it("lifecycle writes still throw in a demo build", async () => {
        vi.stubEnv("VITE_DEMO_MODE", "true");
        await expect(lifecycleCalls.approveArticle()).rejects.toMatchObject({
          status,
        });
        await expect(lifecycleCalls.getArticleAuditLog()).rejects.toMatchObject(
          {
            status,
          }
        );
      });
    }
  );
});

describe("KnowledgeView outside a demo build (#4523)", () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    setActivePinia(createPinia());
  });

  it("starts empty and shows an error toast when the knowledge base cannot be loaded", async () => {
    failEveryRequest(503);
    const pinia = createPinia();
    setActivePinia(pinia);
    const wrapper = mount(KnowledgeView, {
      global: { plugins: [pinia, router] },
    });
    await flushPromises();

    expect(wrapper.findAll(".article-card")).toHaveLength(0);
    expect(wrapper.text()).not.toContain(DEFAULT_MOCK_ARTICLES[0].title);
    const toasts = useToastStore().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0].title).toBe("Could not load the knowledge base");
    expect(toasts[0].message).toContain("HTTP 503");
  });
});
