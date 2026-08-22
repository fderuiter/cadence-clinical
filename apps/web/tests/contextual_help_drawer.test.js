import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { router } from "../src/router/index.js";
import { useAuthStore } from "../src/stores/auth.js";
import { useKnowledgeStore } from "../src/stores/knowledge.js";
import { knowledgeService, getFallbackContextualHelp } from "../src/api/knowledge.js";
import ContextualHelpDrawer from "../src/components/ContextualHelpDrawer.vue";
import AppShell from "../src/components/AppShell.vue";
import App from "../src/App.vue";

describe("Contextual Help Drawer & Support Ticket Escalation (Issue #4332)", () => {
  let pinia;
  let authStore;
  let knowledgeStore;

  const mockResolvedSop = {
    matched_mapping: {
      id: "map-test-01",
      route_pattern: "/ecrf/*",
      persona: "site_crc",
      article_id: "art-test-01",
      section_anchor: "#subject-enrollment-and-visit-entry",
      priority: 10,
      is_active: true,
    },
    primary_article: {
      id: "art-test-01",
      title: "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture",
      slug: "sop-ecrf-201-subject-enrollment",
      status: "PUBLISHED",
      version_index: 2,
      version_label: "2.1",
      tags: ["ecrf", "enrollment", "site-sop"],
      category_name: "Clinical Operations",
      body_markdown: "## Purpose & Scope\n\nSubject enrollment guidelines.",
      body_html: "<h2>Purpose & Scope</h2><p>Subject enrollment guidelines.</p>",
    },
    primary_version: {
      id: "ver-test-01",
      version_index: 2,
      version_label: "2.1",
      status_at_snapshot: "PUBLISHED",
    },
    section_anchor: "#subject-enrollment-and-visit-entry",
    related_articles: [
      {
        id: "art-rel-01",
        title: "SOP-ECRF-205: Data Query Resolution",
        slug: "sop-ecrf-205-query-resolution",
        version_label: "1.2",
        tags: ["queries", "discrepancies"],
        body_markdown: "# Query Resolution\n\nProcedures for queries.",
        body_html: "<h1>Query Resolution</h1><p>Procedures for queries.</p>",
      },
    ],
  };

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();
    knowledgeStore = useKnowledgeStore();

    authStore.isAuthenticated = true;
    authStore.isDemoMode = true;
    authStore.currentPersona = "site_crc";

    vi.clearAllMocks();
  });

  describe("1. API Service & Offline Fallbacks", () => {
    it("returns correct fallback SOPs for /ecrf, /mdr, /ctms, and /audit", () => {
      const ecrfHelp = getFallbackContextualHelp("/ecrf", "site_crc");
      expect(ecrfHelp.primary_article.title).toContain("SOP-ECRF-201");
      expect(ecrfHelp.section_anchor).toBe("#subject-enrollment-and-visit-entry");

      const mdrHelp = getFallbackContextualHelp("/mdr", "sponsor_designer");
      expect(mdrHelp.primary_article.title).toContain("SOP-MDR-104");

      const ctmsHelp = getFallbackContextualHelp("/ctms", "cra_monitor");
      expect(ctmsHelp.primary_article.title).toContain("SOP-CTMS-302");

      const auditHelp = getFallbackContextualHelp("/audit", "auditor");
      expect(auditHelp.primary_article.title).toContain("SOP-AUD-401");
    });

    it("returns empty structure for unknown unmapped routes", () => {
      const emptyHelp = getFallbackContextualHelp("/unknown/unmapped/path", "site_crc");
      expect(emptyHelp.primary_article).toBeNull();
      expect(emptyHelp.related_articles).toEqual([]);
    });

    it("resolves contextual help via service calling getFallbackContextualHelp on failure or offline", async () => {
      const result = await knowledgeService.resolveContextualHelp({
        route: "/ecrf",
        persona: "site_crc",
      });
      expect(result).toBeDefined();
      expect(result.primary_article || result.article).toBeDefined();
    });
  });

  describe("2. Pinia Knowledge Store State & Actions", () => {
    it("updates store state when fetchContextualHelp resolves", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(mockResolvedSop);

      await knowledgeStore.fetchContextualHelp("/ecrf/subjects", "site_crc");

      expect(knowledgeStore.currentRoute).toBe("/ecrf/subjects");
      expect(knowledgeStore.activePersona).toBe("site_crc");
      expect(knowledgeStore.primaryArticle.id).toBe("art-test-01");
      expect(knowledgeStore.sectionAnchor).toBe("#subject-enrollment-and-visit-entry");
      expect(knowledgeStore.relatedArticles.length).toBe(1);
      expect(knowledgeStore.hasGuidance).toBe(true);
    });

    it("toggles drawer visibility, expands/collapses article body, and navigates between articles", () => {
      expect(knowledgeStore.isOpen).toBe(false);
      knowledgeStore.openDrawer();
      expect(knowledgeStore.isOpen).toBe(true);
      knowledgeStore.toggleDrawer();
      expect(knowledgeStore.isOpen).toBe(false);

      knowledgeStore.openDrawer();
      expect(knowledgeStore.isArticleBodyExpanded).toBe(true);
      knowledgeStore.toggleArticleBody();
      expect(knowledgeStore.isArticleBodyExpanded).toBe(false);

      // Select related article
      const related = { id: "rel-01", title: "Related SOP", body_html: "<p>Related</p>" };
      knowledgeStore.selectArticle(related);
      expect(knowledgeStore.displayedArticle.id).toBe("rel-01");
      expect(knowledgeStore.isArticleBodyExpanded).toBe(true);

      // Back to primary
      knowledgeStore.backToPrimary();
      expect(knowledgeStore.selectedArticle).toBeNull();
    });

    it("prepares escalation payload with route, persona, and SOP context", () => {
      knowledgeStore.currentRoute = "/ecrf/subjects/SUBJ-001";
      knowledgeStore.activePersona = "site_crc";
      knowledgeStore.primaryArticle = mockResolvedSop.primary_article;

      const payload = knowledgeStore.prepareEscalation();
      expect(knowledgeStore.isEscalating).toBe(true);
      expect(payload.title).toContain("/ecrf/subjects/SUBJ-001");
      expect(payload.description).toContain("SOP-ECRF-201");
      expect(payload.description).toContain("site_crc");
      expect(payload.category).toBe("PROTOCOL_DEVIATION");
      expect(payload.reason_for_change).toContain("Escalated to Support Ticket");

      knowledgeStore.cancelEscalation();
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(knowledgeStore.escalationPayload).toBeNull();
    });
  });

  describe("3. ContextualHelpDrawer.vue Component Rendering & Interactions", () => {
    it("renders drawer when isOpen is true, displaying route, persona, and SOP content", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(mockResolvedSop);
      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      expect(wrapper.find("#contextual-help-drawer").exists()).toBe(true);
      expect(wrapper.text()).toContain("Clinical Guidance & SOPs");
      expect(wrapper.text()).toContain("/ecrf");
      expect(wrapper.text()).toContain("Site Coordinator / CRC");
      expect(wrapper.text()).toContain("SOP-ECRF-201: Subject Enrollment & eCRF Data Capture");
      expect(wrapper.text()).toContain("v2.1");
      expect(wrapper.text()).toContain("#subject-enrollment-and-visit-entry");
      expect(wrapper.text()).toContain("Subject enrollment guidelines.");
    });

    it("toggles article body collapse when button is clicked", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(mockResolvedSop);
      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      expect(wrapper.find(".sop-body").exists()).toBe(true);
      const toggleBtn = wrapper.find(".btn-toggle-body");
      expect(toggleBtn.text()).toContain("Collapse Body");

      await toggleBtn.trigger("click");
      expect(knowledgeStore.isArticleBodyExpanded).toBe(false);
      expect(wrapper.find(".sop-body").exists()).toBe(false);

      await toggleBtn.trigger("click");
      expect(knowledgeStore.isArticleBodyExpanded).toBe(true);
      expect(wrapper.find(".sop-body").exists()).toBe(true);
    });

    it("displays related articles and allows selecting one to view", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(mockResolvedSop);
      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      expect(wrapper.find(".related-section").exists()).toBe(true);
      expect(wrapper.text()).toContain("SOP-ECRF-205: Data Query Resolution");

      // Click related article
      await wrapper.find(".related-card").trigger("click");
      expect(knowledgeStore.selectedArticle.id).toBe("art-rel-01");
      expect(wrapper.text()).toContain("Return to Route Spotlight SOP");

      // Return back
      await wrapper.find(".btn-back").trigger("click");
      expect(knowledgeStore.selectedArticle).toBeNull();
      expect(wrapper.text()).toContain("SOP-ECRF-201");
    });

    it("triggers ticket escalation modal with pre-populated initial data", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(mockResolvedSop);
      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      const escalateBtn = wrapper.find(".btn-escalate");
      expect(escalateBtn.exists()).toBe(true);

      await escalateBtn.trigger("click");
      expect(knowledgeStore.isEscalating).toBe(true);
      expect(knowledgeStore.escalationPayload).toBeDefined();
      expect(knowledgeStore.escalationPayload.title).toContain("/ecrf");

      expect(wrapper.find(".ticket-create-modal").exists()).toBe(true);
      expect(wrapper.find("#ticket-title").element.value).toContain("/ecrf");
      expect(wrapper.find("#ticket-desc").element.value).toContain("SOP-ECRF-201");
    });

    it("supports search input and displays matching articles", async () => {
      vi.spyOn(knowledgeService, "listArticles").mockResolvedValue([
        {
          id: "art-search-01",
          title: "SOP-SAF-301: Adverse Events",
          body_html: "<p>Safety events</p>",
          tags: ["safety", "sae"],
          version_label: "3.0",
        },
      ]);

      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      // Open search
      await wrapper.find(".btn-icon-toggle").trigger("click");
      const searchInputEl = wrapper.find("#help-search-input");
      expect(searchInputEl.exists()).toBe(true);

      // Search query via input
      await searchInputEl.setValue("safety");
      await new Promise((r) => setTimeout(r, 300));
      await wrapper.vm.$nextTick();

      expect(wrapper.text()).toContain("Search Results (1)");
      expect(wrapper.text()).toContain("SOP-SAF-301: Adverse Events");

      // Select search result
      await wrapper.find(".search-result-card").trigger("click");
      expect(knowledgeStore.displayedArticle.id).toBe("art-search-01");
    });

    it("closes drawer when close button or Escape key is pressed", async () => {
      knowledgeStore.openDrawer();
      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      expect(knowledgeStore.isOpen).toBe(true);
      await wrapper.find(".btn-close").trigger("click");
      expect(knowledgeStore.isOpen).toBe(false);

      // Re-open and test Escape key
      knowledgeStore.openDrawer();
      window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
      expect(knowledgeStore.isOpen).toBe(false);
    });
  });

  describe("4. Global Integration in AppShell & App.vue", () => {
    it("renders Help & SOPs trigger button in AppShell and toggles drawer", async () => {
      const wrapper = mount(AppShell, {
        global: {
          plugins: [pinia, router],
        },
      });

      const helpBtn = wrapper.find("#btn-contextual-help-trigger");
      expect(helpBtn.exists()).toBe(true);
      expect(helpBtn.text()).toContain("Help & SOPs");

      expect(knowledgeStore.isOpen).toBe(false);
      await helpBtn.trigger("click");
      expect(knowledgeStore.isOpen).toBe(true);
    });

    it("mounts ContextualHelpDrawer in App.vue and verifies presence across routes", async () => {
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Admin", "Site Investigator", "CRC"];

      await router.push("/ecrf");

      const wrapper = mount(App, {
        global: {
          plugins: [pinia, router],
        },
      });

      // ContextualHelpDrawer is rendered globally
      knowledgeStore.openDrawer();
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#contextual-help-drawer").exists()).toBe(true);
      expect(wrapper.text()).toContain("Clinical Guidance & SOPs");
    });
  });
});
