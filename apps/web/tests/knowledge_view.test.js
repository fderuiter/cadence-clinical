import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router/index.js";
import { useAuthStore } from "../src/stores/auth.js";
import { signInAs } from "./helpers/signIn.js";
import KnowledgeView from "../src/views/KnowledgeView.vue";
import AppShell from "../src/components/AppShell.vue";
import {
  DEFAULT_CATEGORIES,
  DEFAULT_CONTEXTUAL_SOPS,
  DEFAULT_MOCK_ARTICLES,
  knowledgeService,
} from "../src/api/knowledge.js";

// These tests cover screens that are hidden until their .NET backend exists
// (#4829), so they treat every screen as available.
vi.mock("../src/router/availability", async (importOriginal) => ({
  ...(await importOriginal()),
  isScreenAvailable: () => true,
}));
// The offline DEFAULT_MOCK_ARTICLES only hold the eCRF and MDR SOPs, neither of
// which is filed under Regulatory & Quality Compliance or mentions MedDRA. The
// category and search tests below need articles that do, so they serve this
// wider catalogue from the mocked listArticles.
const REGULATORY_ARTICLE = DEFAULT_CONTEXTUAL_SOPS["/audit"].primary_article;
const MEDDRA_CODING_ARTICLE = {
  id: "art-coding-sop",
  title: "SOP-DM-510: MedDRA & WHODrug Medical Coding Conventions",
  slug: "sop-dm-510-meddra-whodrug-coding",
  status: "PUBLISHED",
  version_index: 1,
  version_label: "1.0",
  tags: ["coding", "dictionary", "data-management"],
  category_id: "cat-data-management",
  category_name: "Data Management & Coding",
  author_user_id: "data_manager",
  last_edited_by: "data_manager",
  approved_by: "qa_director",
  created_at: "2026-01-20T09:00:00Z",
  updated_at: "2026-06-01T11:15:00Z",
  body_markdown: "## Adverse event and concomitant medication coding",
  body_html: "<h2>Adverse event and concomitant medication coding</h2>",
};
const EXTENDED_ARTICLE_CATALOGUE = [
  ...DEFAULT_MOCK_ARTICLES,
  REGULATORY_ARTICLE,
  MEDDRA_CODING_ARTICLE,
];

// Mock knowledge API service
vi.mock("../src/api/knowledge.js", async (importOriginal) => {
  const actual = await importOriginal();
  return {
    ...actual,
    knowledgeService: {
      resolveContextualHelp: vi.fn((params) =>
        Promise.resolve(
          actual.getFallbackContextualHelp(params?.route, params?.persona)
        )
      ),
      getArticle: vi.fn((id) => {
        const found = actual.DEFAULT_MOCK_ARTICLES.find(
          (a) => a.id === id || a.slug === id
        );
        return Promise.resolve(found || actual.DEFAULT_MOCK_ARTICLES[0]);
      }),
      listArticles: vi.fn(() =>
        Promise.resolve([...actual.DEFAULT_MOCK_ARTICLES])
      ),
      listCategories: vi.fn(() =>
        Promise.resolve([...actual.DEFAULT_CATEGORIES])
      ),
      createCategory: vi.fn((payload) =>
        Promise.resolve({
          id: `cat-mock-${Date.now()}`,
          name: payload.name,
          slug: payload.slug,
          description: payload.description,
          persona_visibility: payload.persona_visibility || [],
          is_deleted: false,
          created_at: new Date().toISOString(),
        })
      ),
      createArticle: vi.fn((payload) =>
        Promise.resolve({
          id: `art-mock-${Date.now()}`,
          title: payload.title,
          slug: payload.slug,
          status: "DRAFT",
          version_index: 1,
          version_label: payload.version_label || "0.1",
          category_id: payload.category_id || "cat-clinical-ops",
          category_name: "Clinical Operations",
          tags: payload.tags || [],
          author_user_id: "admin.user",
          last_edited_by: "admin.user",
          approved_by: null,
          created_at: new Date().toISOString(),
          updated_at: new Date().toISOString(),
          body_markdown: payload.body_markdown || "",
          body_html: `<p>${payload.body_markdown || ""}</p>`,
        })
      ),
      updateArticleDraft: vi.fn((id, payload) =>
        Promise.resolve({
          id,
          title: payload.title,
          status: "DRAFT",
          version_index: 1,
          version_label: "0.1",
          category_id: payload.category_id,
          category_name: "Clinical Operations",
          tags: payload.tags || [],
          body_markdown: payload.body_markdown || "",
          body_html: `<p>${payload.body_markdown || ""}</p>`,
          updated_at: new Date().toISOString(),
        })
      ),
      submitArticleReview: vi.fn((id) => {
        const found =
          actual.DEFAULT_MOCK_ARTICLES.find((a) => a.id === id) ||
          actual.DEFAULT_MOCK_ARTICLES[0];
        return Promise.resolve({
          ...found,
          status: "IN_REVIEW",
          updated_at: new Date().toISOString(),
        });
      }),
      approveArticle: vi.fn((id) => {
        const found =
          actual.DEFAULT_MOCK_ARTICLES.find((a) => a.id === id) ||
          actual.DEFAULT_MOCK_ARTICLES[0];
        return Promise.resolve({
          ...found,
          status: "APPROVED",
          approved_by: "qa_approver",
          updated_at: new Date().toISOString(),
        });
      }),
      rejectArticle: vi.fn((id) => {
        const found =
          actual.DEFAULT_MOCK_ARTICLES.find((a) => a.id === id) ||
          actual.DEFAULT_MOCK_ARTICLES[0];
        return Promise.resolve({
          ...found,
          status: "REJECTED",
          updated_at: new Date().toISOString(),
        });
      }),
      publishArticle: vi.fn((id, payload) => {
        const found =
          actual.DEFAULT_MOCK_ARTICLES.find((a) => a.id === id) ||
          actual.DEFAULT_MOCK_ARTICLES[0];
        return Promise.resolve({
          ...found,
          status: "PUBLISHED",
          version_label: payload.version_label || "2.0",
          updated_at: new Date().toISOString(),
        });
      }),
      transitionArticle: vi.fn((id, payload) => {
        const found =
          actual.DEFAULT_MOCK_ARTICLES.find((a) => a.id === id) ||
          actual.DEFAULT_MOCK_ARTICLES[0];
        return Promise.resolve({
          ...found,
          status: payload.target_status,
          updated_at: new Date().toISOString(),
        });
      }),
      listArticleVersions: vi.fn((id) =>
        Promise.resolve([
          {
            id: `ver-${id}-1`,
            version_index: 1,
            version_label: "1.0",
            status_at_snapshot: "PUBLISHED",
            is_locked: true,
            created_by: "admin.user",
            created_at: new Date().toISOString(),
            reason_for_change: "Initial version",
          },
        ])
      ),
      getArticleAuditLog: vi.fn((id) =>
        Promise.resolve([
          {
            id: `audit-${id}-1`,
            event_type: "CREATED",
            actor_user_id: "admin.user",
            target_status: "DRAFT",
            version_label: "0.1",
            reason_for_change: "Initial creation",
            sha256_hash: "sha256:abcd1234ef5678",
            created_at: new Date().toISOString(),
          },
        ])
      ),
    },
  };
});

describe("Knowledge Hub Browser View (Issue #4331)", () => {
  let pinia;
  let authStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();

    // Sponsor Admins may view every persona's workspace; switching the view
    // never changes the token roles.
    signInAs(authStore, ["Sponsor Admin"], { username: "admin.user" });
    authStore.switchPersona("super_admin");

    vi.clearAllMocks();
  });

  describe("1. Component Mounting & Header Architecture", () => {
    it("mounts KnowledgeView and displays regulatory badges and KPI metric cards", async () => {
      const wrapper = mount(KnowledgeView, {
        global: {
          plugins: [pinia, router],
        },
      });
      await flushPromises();

      expect(wrapper.find(".view-title").text()).toContain(
        "Knowledge & Support Hub"
      );
      expect(wrapper.text()).toContain("21 CFR Part 11");
      expect(wrapper.text()).toContain("ICH GCP E6(R3)");
      expect(wrapper.text()).toContain("GxP Controlled Documents");

      // Verify metric cards
      const statCards = wrapper.findAll(".stat-card");
      expect(statCards.length).toBe(4);
      expect(wrapper.text()).toContain("Total SOPs & Guides");
      expect(wrapper.text()).toContain("Active Published SOPs");
      expect(wrapper.text()).toContain("Under Four-Eyes Review");
      expect(wrapper.text()).toContain("Working Draft Revisions");
    });

    it("displays admin authoring buttons for super_admin and hides them for non-admin personas", async () => {
      // Super Admin
      authStore.switchPersona("super_admin");
      const superWrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();
      expect(superWrapper.find("#btn-create-article").exists()).toBe(true);
      expect(superWrapper.find("#btn-create-category").exists()).toBe(true);

      // Site Coordinator / CRC
      authStore.switchPersona("site_crc");
      const crcWrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();
      expect(crcWrapper.find("#btn-create-article").exists()).toBe(false);
      expect(crcWrapper.find("#btn-create-category").exists()).toBe(false);
    });
  });

  describe("2. Interactive Category Sidebar & Persona Filtering", () => {
    it("displays all categories for super_admin persona", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const categoryButtons = wrapper.findAll(".category-item-btn");
      // 1 "All Categories" + categories
      expect(categoryButtons.length).toBeGreaterThanOrEqual(
        DEFAULT_CATEGORIES.length + 1
      );
      expect(wrapper.text()).toContain("Clinical Operations");
      expect(wrapper.text()).toContain("Study Authoring & MDR");
      expect(wrapper.text()).toContain("Regulatory & Quality Compliance");
    });

    it("filters categories visible to site_crc persona", async () => {
      authStore.switchPersona("site_crc");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // site_crc should see Clinical Operations and Site Monitoring, but not Study Authoring & MDR
      const visibleText = wrapper.find(".category-list").text();
      expect(visibleText).toContain("Clinical Operations");
      expect(visibleText).toContain("Site Monitoring & CTMS");
      expect(visibleText).not.toContain("Study Authoring & MDR");
    });

    it("filters categories visible to auditor persona", async () => {
      authStore.switchPersona("auditor");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const visibleText = wrapper.find(".category-list").text();
      expect(visibleText).toContain("Regulatory & Quality Compliance");
      expect(visibleText).not.toContain("Site Monitoring & CTMS");
    });

    it("filters articles list when category is selected", async () => {
      knowledgeService.listArticles.mockResolvedValueOnce([
        ...EXTENDED_ARTICLE_CATALOGUE,
      ]);
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // Click on Regulatory & Quality Compliance category
      const regCategoryBtn = wrapper
        .findAll(".category-item-btn")
        .find((btn) => btn.text().includes("Regulatory & Quality Compliance"));
      expect(regCategoryBtn).toBeDefined();
      await regCategoryBtn.trigger("click");
      await flushPromises();

      // Verify articles shown belong to regulatory compliance
      const cards = wrapper.findAll(".article-card");
      expect(cards.length).toBeGreaterThan(0);
      cards.forEach((card) => {
        expect(card.text()).toContain("Regulatory & Quality Compliance");
      });
    });
  });

  describe("3. Live Search & Tag Filtering", () => {
    it("filters articles dynamically when typing in search input", async () => {
      knowledgeService.listArticles.mockResolvedValueOnce([
        ...EXTENDED_ARTICLE_CATALOGUE,
      ]);
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const searchInput = wrapper.find("#knowledge-search-input");
      await searchInput.setValue("MedDRA");
      await flushPromises();

      const cards = wrapper.findAll(".article-card");
      expect(cards.length).toBe(1);
      expect(cards[0].text()).toContain("MedDRA & WHODrug");

      // Clear search
      await wrapper.find(".btn-clear-search").trigger("click");
      await flushPromises();
      expect(wrapper.findAll(".article-card").length).toBeGreaterThan(1);
    });

    it("filters articles by clicking tag chips", async () => {
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const tagChips = wrapper.findAll(".tag-chip");
      const ecrfChip = tagChips.find((c) => c.text().includes("#ecrf"));
      if (ecrfChip) {
        await ecrfChip.trigger("click");
        await flushPromises();
        const cards = wrapper.findAll(".article-card");
        cards.forEach((card) => {
          expect(card.text().toLowerCase()).toContain("ecrf");
        });
      }
    });

    it("filters articles by lifecycle status filter dropdown", async () => {
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const statusSelect = wrapper.find("#status-filter-select");
      await statusSelect.setValue("IN_REVIEW");
      await flushPromises();

      const cards = wrapper.findAll(".article-card");
      cards.forEach((card) => {
        expect(card.text()).toContain("IN_REVIEW");
      });
    });
  });

  describe("4. Article Reader & GxP Inspection Tabs", () => {
    it("renders formatted Markdown/HTML, tags, version, and author metadata", async () => {
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // Select first article
      const firstCard = wrapper.find(".article-card");
      await firstCard.trigger("click");
      await flushPromises();

      const reader = wrapper.find("#knowledge-reader-pane");
      expect(reader.find(".reader-article-title").exists()).toBe(true);
      expect(reader.find(".status-pill").exists()).toBe(true);
      expect(reader.find(".badge-version-lg").exists()).toBe(true);
      expect(reader.find(".reader-author-row").text()).toContain("Author:");
      expect(reader.find("#sop-prose-viewer").exists()).toBe(true);
    });

    it("switches to Version History tab and loads snapshots table", async () => {
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const firstCard = wrapper.find(".article-card");
      await firstCard.trigger("click");
      await flushPromises();

      const versionTabBtn = wrapper
        .findAll(".reader-tab-btn")
        .find((btn) => btn.text().includes("Version History"));
      await versionTabBtn.trigger("click");
      await flushPromises();

      expect(wrapper.find("#sop-versions-viewer").exists()).toBe(true);
      expect(wrapper.text()).toContain("Immutable Snapshot Versions");
    });

    it("switches to 21 CFR Part 11 Audit Trail tab and displays ledger entries", async () => {
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      const firstCard = wrapper.find(".article-card");
      await firstCard.trigger("click");
      await flushPromises();

      const auditTabBtn = wrapper
        .findAll(".reader-tab-btn")
        .find((btn) => btn.text().includes("Audit Trail"));
      await auditTabBtn.trigger("click");
      await flushPromises();

      expect(wrapper.find("#sop-audit-viewer").exists()).toBe(true);
      expect(wrapper.text()).toContain(
        "21 CFR Part 11 Electronic Audit Ledger"
      );
    });
  });

  describe("5. Admin Authoring Controls & Lifecycle State Machine", () => {
    it("opens Create Article modal and adds new working draft", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await wrapper.find("#btn-create-article").trigger("click");
      await flushPromises();
      expect(wrapper.find("#create-art-modal-title").exists()).toBe(true);

      // Fill in form
      await wrapper
        .find("#new-art-title")
        .setValue("SOP-SAF-305: Safety Signal Detection");
      await wrapper.find("#new-art-slug").setValue("sop-saf-305-safety-signal");
      await wrapper
        .find("#new-art-body")
        .setValue("## Purpose\nSafety signal detection SOP.");
      await wrapper.find("#new-art-reason").setValue("Initial draft creation");

      await wrapper.vm.handleCreateArticle();
      await flushPromises();

      // Verify article was added
      expect(wrapper.text()).toContain("SOP-SAF-305: Safety Signal Detection");
    });

    it("submits a DRAFT article for peer review (DRAFT -> IN_REVIEW)", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // Find draft article
      const draftCard = wrapper
        .findAll(".article-card")
        .find((card) => card.text().includes("DRAFT"));
      if (draftCard) {
        await draftCard.trigger("click");
        await flushPromises();

        const submitBtn = wrapper.find("#btn-submit-review");
        expect(submitBtn.exists()).toBe(true);
        await submitBtn.trigger("click");
        await flushPromises();

        // Reason modal appears
        expect(wrapper.find("#reason-modal-title").text()).toContain(
          "Submit Draft"
        );
        await wrapper
          .find("#action-reason-input")
          .setValue("Draft completed and ready for review");
        await wrapper.vm.executeActionWithReason();
        await flushPromises();

        expect(wrapper.find("#knowledge-reader-pane").text()).toContain(
          "IN_REVIEW"
        );
      }
    });

    it("approves an article in IN_REVIEW status (IN_REVIEW -> APPROVED)", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      // Filter or find in-review article
      await wrapper.find("#status-filter-select").setValue("IN_REVIEW");
      await flushPromises();
      const inReviewCard = wrapper.find(".article-card");
      if (inReviewCard.exists()) {
        await inReviewCard.trigger("click");
        await flushPromises();

        const approveBtn = wrapper.find("#btn-approve-article");
        expect(approveBtn.exists()).toBe(true);
        await approveBtn.trigger("click");
        await flushPromises();

        expect(wrapper.find("#reason-modal-title").text()).toContain(
          "Approve Article"
        );
        await wrapper
          .find("#action-reason-input")
          .setValue("Verified compliant");
        await wrapper.vm.executeActionWithReason();
        await flushPromises();

        expect(wrapper.find("#knowledge-reader-pane").text()).toContain(
          "APPROVED"
        );
      }
    });

    it("publishes an APPROVED article (APPROVED -> PUBLISHED)", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await wrapper.find("#status-filter-select").setValue("APPROVED");
      await flushPromises();
      const approvedCard = wrapper.find(".article-card");
      if (approvedCard.exists()) {
        await approvedCard.trigger("click");
        await flushPromises();

        const publishBtn = wrapper.find("#btn-publish-article");
        expect(publishBtn.exists()).toBe(true);
        await publishBtn.trigger("click");
        await flushPromises();

        expect(wrapper.find("#reason-modal-title").text()).toContain(
          "Publish Approved SOP"
        );
        await wrapper.find("#action-version-label").setValue("2.0");
        await wrapper
          .find("#action-reason-input")
          .setValue("Formal regulatory publication");
        await wrapper.vm.executeActionWithReason();
        await flushPromises();

        expect(wrapper.find("#knowledge-reader-pane").text()).toContain(
          "PUBLISHED"
        );
      }
    });

    it("opens Create Category modal and creates a new category", async () => {
      authStore.switchPersona("super_admin");
      const wrapper = mount(KnowledgeView, {
        global: { plugins: [pinia, router] },
      });
      await flushPromises();

      await wrapper.find("#btn-create-category").trigger("click");
      await flushPromises();
      expect(wrapper.find("#create-cat-modal-title").exists()).toBe(true);

      await wrapper
        .find("#new-cat-name")
        .setValue("Safety & Pharmacovigilance");
      await wrapper.find("#new-cat-slug").setValue("safety-pharmacovigilance");
      await wrapper
        .find("#new-cat-desc")
        .setValue("Safety intake and reporting procedures.");
      await wrapper.vm.handleCreateCategory();
      await flushPromises();

      expect(wrapper.find(".category-list").text()).toContain(
        "Safety & Pharmacovigilance"
      );
    });
  });

  describe("6. Persona Switcher Verification Across All 6 Roles", () => {
    const personas = [
      { key: "super_admin", roleName: "Super Admin" },
      { key: "sponsor_designer", roleName: "Sponsor Designer" },
      { key: "site_crc", roleName: "Site CRC" },
      { key: "cra_monitor", roleName: "CRA Monitor" },
      { key: "data_manager", roleName: "Data Manager" },
      { key: "auditor", roleName: "Auditor" },
    ];

    personas.forEach(({ key, roleName }) => {
      it(`renders gracefully and maintains role-aware boundaries for ${roleName} (${key})`, async () => {
        authStore.switchPersona(key);
        const wrapper = mount(KnowledgeView, {
          global: { plugins: [pinia, router] },
        });
        await flushPromises();

        expect(wrapper.find(".view-title").exists()).toBe(true);
        expect(wrapper.find(".knowledge-sidebar").exists()).toBe(true);
        expect(wrapper.find(".article-list-panel").exists()).toBe(true);
        expect(wrapper.find(".article-reader-pane").exists()).toBe(true);

        if (key === "super_admin") {
          expect(wrapper.find("#btn-create-article").exists()).toBe(true);
        } else {
          expect(wrapper.find("#btn-create-article").exists()).toBe(false);
          expect(wrapper.find("#admin-authoring-toolbar").exists()).toBe(false);
        }
      });
    });
  });

  describe("7. Global AppShell Integration & Route Navigation", () => {
    it("renders Knowledge & SOP Hub in AppShell sidebar navigation", async () => {
      const shellWrapper = mount(AppShell, {
        global: {
          plugins: [pinia, router],
        },
      });
      await flushPromises();

      const navBtn = shellWrapper.find("#tab-btn-knowledge");
      expect(navBtn.exists()).toBe(true);
      expect(navBtn.text()).toContain("Knowledge & SOP Hub");
    });

    it("verifies router configuration includes /knowledge route", () => {
      const route = router.getRoutes().find((r) => r.path === "/knowledge");
      expect(route).toBeDefined();
      expect(route.name).toBe("knowledge");
      expect(route.meta.requiresAuth).toBe(true);
    });
  });
});
