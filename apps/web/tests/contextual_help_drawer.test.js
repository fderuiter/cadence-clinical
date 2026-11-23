import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import { router } from "../src/router/index.js";
import { useAuthStore } from "../src/stores/auth.js";
import { useKnowledgeStore } from "../src/stores/knowledge.js";
import { useToastStore } from "../src/stores/toasts.js";
import {
  knowledgeService,
  getFallbackContextualHelp,
} from "../src/api/knowledge.js";
import { ticketsApi } from "../src/api/ticketsApi";
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
      body_html:
        '<h2 id="subject-enrollment-and-visit-entry">Purpose &amp; Scope</h2><p>Subject enrollment guidelines.</p>',
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
    // Uses the demo personas and roles (demo builds only, #4523).
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();
    knowledgeStore = useKnowledgeStore();

    authStore.isAuthenticated = true;
    authStore.currentPersona = "site_crc";
    if (typeof sessionStorage !== "undefined") {
      sessionStorage.clear();
    }
    knowledgeStore.clearEscalatedTickets();

    vi.clearAllMocks();
  });

  describe("1. API Service & Offline Fallbacks", () => {
    it("returns correct fallback SOPs for /studies, /studies/:studyCode, /ecrf, /mdr, /ctms, /ssu, and /audit", () => {
      const studiesListHelp = getFallbackContextualHelp(
        "/studies",
        "sponsor_designer"
      );
      expect(studiesListHelp.primary_article.title).toContain("SOP-STD-101");

      const studyCanvasHelp = getFallbackContextualHelp(
        "/studies/DEMO-01",
        "site_crc"
      );
      expect(studyCanvasHelp.primary_article.title).toContain("SOP-STD-102");
      expect(studyCanvasHelp.matched_mapping.route_pattern).toBe(
        "/studies/:studyCode"
      );

      const ecrfHelp = getFallbackContextualHelp("/ecrf", "site_crc");
      expect(ecrfHelp.primary_article.title).toContain("SOP-ECRF-201");
      expect(ecrfHelp.section_anchor).toBe(
        "#subject-enrollment-and-visit-entry"
      );

      const mdrHelp = getFallbackContextualHelp("/mdr", "sponsor_designer");
      expect(mdrHelp.primary_article.title).toContain("SOP-MDR-104");

      const ctmsHelp = getFallbackContextualHelp("/ctms", "cra_monitor");
      expect(ctmsHelp.primary_article.title).toContain("SOP-CTMS-302");

      const ssuHelp = getFallbackContextualHelp("/ssu", "cra_monitor");
      expect(ssuHelp.primary_article.title).toContain("SOP-SSU-205");

      const auditHelp = getFallbackContextualHelp("/audit", "auditor");
      expect(auditHelp.primary_article.title).toContain("SOP-AUD-401");
    });

    it("returns empty structure for unknown unmapped routes", () => {
      const emptyHelp = getFallbackContextualHelp(
        "/unknown/unmapped/path",
        "site_crc"
      );
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
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );

      await knowledgeStore.fetchContextualHelp("/ecrf/subjects", "site_crc");

      expect(knowledgeStore.currentRoute).toBe("/ecrf/subjects");
      expect(knowledgeStore.activePersona).toBe("site_crc");
      expect(knowledgeStore.primaryArticle.id).toBe("art-test-01");
      expect(knowledgeStore.sectionAnchor).toBe(
        "#subject-enrollment-and-visit-entry"
      );
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
      const related = {
        id: "rel-01",
        title: "Related SOP",
        body_html: "<p>Related</p>",
      };
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
      expect(payload.reason_for_change).toContain(
        "Escalated to Support Ticket"
      );

      knowledgeStore.cancelEscalation();
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(knowledgeStore.escalationPayload).toBeNull();
    });

    it("resets contextual state when invalidateContextualState is called or fetchContextualHelp runs", async () => {
      knowledgeStore.selectedArticle = {
        id: "stale-art",
        title: "Stale Article",
      };
      knowledgeStore.searchQuery = "stale query";
      knowledgeStore.searchResults = [{ id: "stale-art" }];
      knowledgeStore.isSearching = true;
      knowledgeStore.isEscalating = true;
      knowledgeStore.escalationPayload = { title: "Stale Ticket" };

      knowledgeStore.invalidateContextualState();

      expect(knowledgeStore.selectedArticle).toBeNull();
      expect(knowledgeStore.searchQuery).toBe("");
      expect(knowledgeStore.searchResults).toEqual([]);
      expect(knowledgeStore.isSearching).toBe(false);
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(knowledgeStore.escalationPayload).toBeNull();

      // Verify fetchContextualHelp calls invalidateContextualState
      knowledgeStore.selectedArticle = {
        id: "stale-art",
        title: "Stale Article",
      };
      knowledgeStore.isEscalating = true;
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");

      expect(knowledgeStore.selectedArticle).toBeNull();
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(knowledgeStore.displayedArticle.id).toBe("art-test-01");
    });
  });

  describe("3. ContextualHelpDrawer.vue Component Rendering & Interactions", () => {
    it("renders drawer when isOpen is true, displaying route, persona, and SOP content", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
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
      expect(wrapper.text()).toContain(
        "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture"
      );
      expect(wrapper.text()).toContain("v2.1");
      expect(wrapper.text()).toContain("Subject Enrollment and Visit Entry");
      expect(
        wrapper.find(".anchor-callout").attributes("data-anchor-target")
      ).toBe("subject-enrollment-and-visit-entry");
      expect(wrapper.text()).toContain("Subject enrollment guidelines.");
    });

    it("toggles article body collapse when button is clicked", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
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
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
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
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
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
      expect(wrapper.find("#ticket-desc").element.value).toContain(
        "SOP-ECRF-201"
      );
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

      // The shared router's initial navigation ("/" redirects to a lazily
      // loaded landing view) can settle at any point after the first mount.
      // Since #4734 a route change intentionally clears the drawer's search
      // input and results, so let it settle before exercising search.
      await router.isReady();
      await wrapper.vm.$nextTick();

      // Open search
      await wrapper.find(".btn-icon-toggle").trigger("click");
      const searchInputEl = wrapper.find("#help-search-input");
      expect(searchInputEl.exists()).toBe(true);

      // Search query via input
      await searchInputEl.setValue("safety");
      // The search is debounced by 250 ms; wait for it rather than sleeping a
      // fixed time, which fails when the suite runs under load.
      await vi.waitFor(
        () => expect(wrapper.text()).toContain("Search Results (1)"),
        { timeout: 5000 }
      );
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

    it("clears selectedArticle, search input, and resets displayedArticle when switching personas", async () => {
      const mockDesignerSop = {
        primary_article: {
          id: "art-mdr-01",
          title:
            "SOP-MDR-104: Schedule of Activities Matrix & Protocol Authoring",
        },
      };

      vi.spyOn(knowledgeService, "resolveContextualHelp")
        .mockResolvedValueOnce(mockResolvedSop)
        .mockResolvedValueOnce(mockDesignerSop);

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.selectArticle({
        id: "art-rel-01",
        title: "Related Article",
      });
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      // Open search and type query
      await wrapper.find(".btn-icon-toggle").trigger("click");
      const searchInputEl = wrapper.find("#help-search-input");
      await searchInputEl.setValue("query");

      expect(knowledgeStore.selectedArticle.id).toBe("art-rel-01");
      expect(knowledgeStore.displayedArticle.id).toBe("art-rel-01");

      // Switch persona
      authStore.currentPersona = "sponsor_designer";
      await wrapper.vm.$nextTick();
      await new Promise((r) => setTimeout(r, 50));

      expect(knowledgeStore.selectedArticle).toBeNull();
      expect(knowledgeStore.displayedArticle.id).toBe("art-mdr-01");
      expect(wrapper.find("#help-search-input").exists()).toBe(false);
    });
    it("loads contextual SOP guidance when opening /ecrf on initial application mount", async () => {
      const localRouter = createRouter({
        history: createMemoryHistory(),
        routes: [{ path: "/ecrf", component: { template: "<div />" } }],
      });
      await localRouter.push("/ecrf");
      await localRouter.isReady();

      // Reset knowledgeStore to initial application mount state (currentRoute === "/ecrf", hasLoadedContextualHelp === false)
      knowledgeStore.$reset();
      knowledgeStore.openDrawer();

      const resolveSpy = vi
        .spyOn(knowledgeService, "resolveContextualHelp")
        .mockResolvedValue(mockResolvedSop);

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, localRouter],
        },
      });

      await vi.waitFor(() =>
        expect(resolveSpy).toHaveBeenCalledWith({
          route: "/ecrf",
          persona: "site_crc",
        })
      );
      await wrapper.vm.$nextTick();

      expect(knowledgeStore.primaryArticle?.id).toBe("art-test-01");
      expect(wrapper.text()).toContain(
        "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture"
      );
      wrapper.unmount();
    });

    it("displays an accessible error alert and retry button when contextual SOP lookup fails", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "false");
      const resolveSpy = vi
        .spyOn(knowledgeService, "resolveContextualHelp")
        .mockRejectedValueOnce(
          Object.assign(new Error("Upstream knowledge service unavailable"), {
            status: 500,
          })
        )
        .mockResolvedValueOnce(mockResolvedSop);

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      const alertEl = wrapper.find("#contextual-help-error-alert");
      expect(alertEl.exists()).toBe(true);
      expect(alertEl.attributes("role")).toBe("alert");
      expect(alertEl.text()).toContain(
        "Unable to Load Contextual SOP Guidance"
      );
      expect(alertEl.text()).toContain("HTTP 500");
      expect(wrapper.text()).not.toContain(
        "No Specific SOP Mapped for This Route"
      );

      const retryBtn = wrapper.find("#btn-contextual-help-retry");
      expect(retryBtn.exists()).toBe(true);
      await retryBtn.trigger("click");
      await vi.waitFor(() => expect(resolveSpy).toHaveBeenCalledTimes(2));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#contextual-help-error-alert").exists()).toBe(false);
      expect(wrapper.text()).toContain(
        "SOP-ECRF-201: Subject Enrollment & eCRF Data Capture"
      );
    });

    it("displays an accessible error alert and retry button when article search fails", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "false");
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
      const searchSpy = vi
        .spyOn(knowledgeService, "listArticles")
        .mockRejectedValueOnce(
          Object.assign(new Error("Knowledge article index unreachable"), {
            status: 500,
          })
        )
        .mockResolvedValueOnce([
          {
            id: "art-search-02",
            title: "SOP-SSU-205: Site Startup Readiness",
            body_html: "<p>Greenlight verification</p>",
            tags: ["ssu", "greenlight"],
            version_label: "1.1",
          },
        ]);

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });
      await router.isReady();
      await wrapper.vm.$nextTick();

      await wrapper.find(".btn-icon-toggle").trigger("click");
      await wrapper.find("#help-search-input").setValue("greenlight");

      await vi.waitFor(
        () =>
          expect(wrapper.find("#contextual-help-error-alert").exists()).toBe(
            true
          ),
        { timeout: 5000 }
      );
      const alertEl = wrapper.find("#contextual-help-error-alert");
      expect(alertEl.attributes("role")).toBe("alert");
      expect(alertEl.text()).toContain("Unable to Search SOP Articles");
      expect(wrapper.text()).not.toContain("No SOPs matched your query");

      await wrapper.find("#btn-contextual-help-retry").trigger("click");
      await vi.waitFor(() => expect(searchSpy).toHaveBeenCalledTimes(2));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#contextual-help-error-alert").exists()).toBe(false);
      expect(wrapper.text()).toContain("SOP-SSU-205: Site Startup Readiness");
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
      wrapper.unmount();
      // Navigating lazily imports the route views, which can exceed the
      // default 5s timeout on a loaded machine.
    }, 20000);
  });

  describe("5. Live Ticket Escalation & Demo Session Retention", () => {
    it("submits escalated tickets to POST /api/v1/tickets with X-Change-Reason outside demo mode and shows a toast with the returned reference ID", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "false");
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );

      const persistedTicket = {
        id: "a1b2c3d4-0000-4000-8000-000000000104", // deid-ignore
        reference: "DEV-2026-0104",
        title: "Site 102 blood pressure field locked",
        description:
          "Coordinator cannot enter systolic value for subject SUBJ-8812.",
        category: "PROTOCOL_DEVIATION",
        priority: "MEDIUM",
        gxp_severity: "MINOR",
        status: "OPEN",
        assigned_queue: "DATA_CAPTURE_ECRF",
        assignee_role: "DATA_CAPTURE_ECRF",
        assignee_user: null,
        reporter: "usr-crc-01",
        created_by: "usr-crc-01",
        study_id: "STUDY-ONC-202",
        site_id: "SITE-102",
        subject_id: "SUBJ-8812",
        related_entity_type: "CRF_FORM",
        related_entity_id: "art-test-01",
        source_route: "/ecrf",
        acknowledge_target_at: "2026-08-05T14:00:00Z",
        sla_target_at: "2026-08-06T10:00:00Z",
        sla_breached: false,
        sla_amber_warned: false,
        escalation_count: 0,
        reason_for_change:
          "Escalated to Support Ticket from Contextual Help on /ecrf",
        created_at: "2026-08-05T10:00:00Z",
        version_index: 1,
      };

      const fetchMock = vi.fn(async (input, init) => {
        const req = input instanceof Request ? input : new Request(input, init);
        expect(req.url).toContain("/api/v1/tickets");
        expect(req.method).toBe("POST");
        expect(req.headers.get("X-Change-Reason")).toBeTruthy();
        return new Response(JSON.stringify(persistedTicket), {
          status: 201,
          headers: { "Content-Type": "application/json" },
        });
      });
      vi.stubGlobal("fetch", fetchMock);

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      await wrapper.find(".btn-escalate").trigger("click");
      expect(knowledgeStore.isEscalating).toBe(true);

      await wrapper
        .find("#ticket-title")
        .setValue("Site 102 blood pressure field locked");
      await wrapper
        .find("#ticket-desc")
        .setValue(
          "Coordinator cannot enter systolic value for subject SUBJ-8812."
        );
      await wrapper.find("#ticket-subject").setValue("SUBJ-8812");

      await wrapper.find("#btn-submit-ticket").trigger("click");
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
      await vi.waitFor(() => expect(knowledgeStore.isEscalating).toBe(false));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#ticket-escalation-refusal").exists()).toBe(false);
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(knowledgeStore.isOpen).toBe(true);
      expect(knowledgeStore.activeDrawerTab).toBe("tickets");
      expect(wrapper.find("#drawer-ticket-detail-card").exists()).toBe(true);
      expect(wrapper.find("#drawer-ticket-detail-card").text()).toContain(
        "DEV-2026-0104"
      );
      expect(wrapper.find("#drawer-ticket-detail-card").text()).toContain(
        "DATA_CAPTURE_ECRF"
      );

      const toasts = useToastStore(pinia).toasts;
      expect(toasts.length).toBeGreaterThan(0);
      expect(toasts[0].tone).toBe("success");
      expect(toasts[0].title).toContain("DEV-2026-0104");
      expect(toasts[0].message).toContain("DATA_CAPTURE_ECRF");
      expect(toasts[0].action).toEqual({
        label: "Open DEV-2026-0104",
        to: "/tickets?ref=DEV-2026-0104",
      });
      wrapper.unmount();
    });

    it("provides direct deep-link buttons to Knowledge Hub (/knowledge) and Support Hub (/tickets)", async () => {
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const pushSpy = vi.spyOn(router, "push").mockResolvedValue();
      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      const knowledgeBtn = wrapper.find("#btn-open-knowledge-hub");
      const ticketsBtn = wrapper.find("#btn-open-tickets-hub");
      expect(knowledgeBtn.exists()).toBe(true);
      expect(ticketsBtn.exists()).toBe(true);

      await knowledgeBtn.trigger("click");
      expect(pushSpy).toHaveBeenCalledWith("/knowledge");
      expect(knowledgeStore.isOpen).toBe(false);

      knowledgeStore.openDrawer();
      await wrapper.vm.$nextTick();
      await wrapper.find("#btn-open-tickets-hub").trigger("click");
      expect(pushSpy).toHaveBeenCalledWith("/tickets");
      expect(knowledgeStore.isOpen).toBe(false);

      pushSpy.mockRestore();
      wrapper.unmount();
    });

    it("displays an accessible alert and preserves user input when live ticket submission fails", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "false");
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
      vi.spyOn(ticketsApi, "createTicket").mockRejectedValueOnce(
        Object.assign(
          new Error(
            "Direct participant PII (email address) is prohibited in support tickets."
          ),
          {
            status: 400,
            data: {
              title: "Direct participant PII is prohibited",
              status: 400,
              detail:
                "Direct participant PII (email address) is prohibited in support tickets.",
            },
          }
        )
      );

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      await wrapper.find(".btn-escalate").trigger("click");
      await wrapper
        .find("#ticket-title")
        .setValue("Site 102 blood pressure field locked");
      await wrapper
        .find("#ticket-desc")
        .setValue("Coordinator cannot enter systolic value.");

      await wrapper.find("#btn-submit-ticket").trigger("click");
      await vi.waitFor(() =>
        expect(wrapper.find("#ticket-escalation-refusal").exists()).toBe(true)
      );

      const refusalBanner = wrapper.find("#ticket-escalation-refusal");
      expect(refusalBanner.attributes("role")).toBe("alert");
      expect(refusalBanner.text()).toContain(
        "Direct participant PII (email address) is prohibited"
      );
      expect(knowledgeStore.isEscalating).toBe(true);
      expect(wrapper.find("#ticket-title").element.value).toBe(
        "Site 102 blood pressure field locked"
      );
      expect(wrapper.find("#ticket-desc").element.value).toBe(
        "Coordinator cannot enter systolic value."
      );
      wrapper.unmount();
    });

    it("retains escalated tickets in session state during demo mode and shows a simulated reference ID", async () => {
      vi.stubEnv("VITE_DEMO_MODE", "true");
      vi.stubGlobal(
        "fetch",
        vi.fn(() => Promise.reject(new TypeError("Demo offline mode")))
      );
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );

      await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: {
          plugins: [pinia, router],
        },
      });

      await wrapper.find(".btn-escalate").trigger("click");
      await wrapper
        .find("#ticket-title")
        .setValue("Demo vital signs edit check question");
      await wrapper
        .find("#ticket-desc")
        .setValue("Subject SUBJ-8812 systolic validation query.");
      await wrapper.find("#btn-submit-ticket").trigger("click");

      await vi.waitFor(() => expect(knowledgeStore.isEscalating).toBe(false));
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#ticket-escalation-refusal").exists()).toBe(false);
      expect(knowledgeStore.escalatedTickets.length).toBe(1);
      const savedTicket = knowledgeStore.escalatedTickets[0];
      expect(savedTicket.reference).toMatch(/^SIM-\d{4}-\d{4}$/);
      expect(savedTicket.assigned_queue).toBe("DATA_CAPTURE_ECRF");

      const storedRaw = sessionStorage.getItem(
        "cadence_demo_escalated_tickets"
      );
      expect(storedRaw).toBeTruthy();
      expect(JSON.parse(storedRaw)[0].reference).toBe(savedTicket.reference);

      const toasts = useToastStore(pinia).toasts;
      expect(toasts.length).toBeGreaterThan(0);
      expect(toasts[0].title).toContain(savedTicket.reference);
      wrapper.unmount();
    });
  });

  describe("6. Complete SOP HTML Steps, Formatted Spotlight Headings & Anchor Scrolling", () => {
    it("renders all 13 ordered procedural steps across /studies, /studies/:studyCode, /ecrf, /ctms, and /ssu", () => {
      const routeExpectedCounts = [
        ["/studies", 3],
        ["/studies/DEMO-01", 3],
        ["/ecrf", 3],
        ["/ctms", 2],
        ["/ssu", 2],
      ];

      let totalProceduralSteps = 0;
      for (const [routePath, expectedSteps] of routeExpectedCounts) {
        const resolved = getFallbackContextualHelp(routePath, "site_crc");
        const container = document.createElement("div");
        container.innerHTML = resolved.primary_article.body_html;
        const stepElements = container.querySelectorAll("ol li");
        expect(
          stepElements.length,
          `Expected ${expectedSteps} procedural steps on ${routePath}`
        ).toBe(expectedSteps);
        const anchorId = resolved.section_anchor.replace(/^#+/, "");
        expect(
          container.querySelector(`[id="${anchorId}"]`),
          `Expected matching element id="${anchorId}" in ${routePath} SOP HTML`
        ).not.toBeNull();
        totalProceduralSteps += stepElements.length;
      }

      expect(totalProceduralSteps).toBe(13);
    });

    it("formats clinical acronyms in section anchors and scrolls matching HTML heading id into view", async () => {
      const sdvSop = {
        ...mockResolvedSop,
        section_anchor: "#ecrf-sdv-and-soa-review",
        primary_article: {
          ...mockResolvedSop.primary_article,
          body_html:
            '<h2 id="overview">Overview</h2><ol><li>Step 1</li></ol><h3 id="ecrf-sdv-and-soa-review">eCRF SDV and SoA Review</h3><p>Verify source charts.</p>',
        },
      };

      const scrollSpy = vi.fn();
      const origScrollIntoView = Element.prototype.scrollIntoView;
      Element.prototype.scrollIntoView = scrollSpy;

      try {
        vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
          sdvSop
        );
        await knowledgeStore.fetchContextualHelp("/ecrf", "cra_monitor");
        knowledgeStore.openDrawer();

        const wrapper = mount(ContextualHelpDrawer, {
          global: { plugins: [pinia, router] },
        });
        await wrapper.vm.$nextTick();

        const jumpBtn = wrapper.find("#btn-jump-spotlight-section");
        expect(jumpBtn.exists()).toBe(true);
        expect(jumpBtn.text()).toBe("eCRF SDV and SoA Review");

        const targetHeading = wrapper.find("#ecrf-sdv-and-soa-review");
        expect(targetHeading.exists()).toBe(true);
        expect(targetHeading.classes()).toContain("spotlight-target-heading");
        expect(scrollSpy).toHaveBeenCalled();

        // Collapse body, then click jump button to auto-expand and scroll again
        scrollSpy.mockClear();
        await wrapper.find(".btn-toggle-body").trigger("click");
        expect(knowledgeStore.isArticleBodyExpanded).toBe(false);

        await jumpBtn.trigger("click");
        await wrapper.vm.$nextTick();
        expect(knowledgeStore.isArticleBodyExpanded).toBe(true);
        expect(scrollSpy).toHaveBeenCalledTimes(1);
        wrapper.unmount();
      } finally {
        Element.prototype.scrollIntoView = origScrollIntoView;
      }
    });
  });

  describe("7. Full Route Query Watching Across /studies/:studyCode Canvas Sections & Zoom", () => {
    it("watches route.fullPath and resolves distinct guidance across all 9 Clinical Canvas query states", async () => {
      const localRouter = createRouter({
        history: createMemoryHistory(),
        routes: [
          { path: "/studies/:studyCode", component: { template: "<div />" } },
        ],
      });
      await localRouter.push("/studies/DEMO-01");
      await localRouter.isReady();

      knowledgeStore.$reset();
      const resolveSpy = vi
        .spyOn(knowledgeService, "resolveContextualHelp")
        .mockImplementation(({ route: r, persona: p }) =>
          Promise.resolve(getFallbackContextualHelp(r, p))
        );

      knowledgeStore.openDrawer();
      const wrapper = mount(ContextualHelpDrawer, {
        global: { plugins: [pinia, localRouter] },
      });
      await vi.waitFor(() => expect(resolveSpy).toHaveBeenCalledTimes(1));

      const queryTransitions = [
        [
          "/studies/DEMO-01?stage=design&section=protocol-versions",
          "SOP-STD-102",
          "Protocol Versions and SoA Design",
        ],
        [
          "/studies/DEMO-01?stage=startup&section=site-activation",
          "SOP-SSU-205",
          "Site Greenlight Activation Matrix",
        ],
        [
          "/studies/DEMO-01?stage=conduct&section=participants",
          "SOP-STD-102",
          "Participant Enrollment and Visit Matrix",
        ],
        [
          "/studies/DEMO-01?stage=conduct&section=queries",
          "SOP-ECRF-201",
          "Discrepancy Management and Coding",
        ],
        [
          "/studies/DEMO-01?stage=closeout&section=locks",
          "SOP-STD-102",
          "Database Freeze and Lock Controls",
        ],
        [
          "/studies/DEMO-01?stage=closeout&section=exports",
          "SOP-STD-102",
          "Regulatory Submission and Exports",
        ],
        [
          "/studies/DEMO-01?stage=conduct&participant=SUBJ-1001",
          "SOP-STD-102",
          "Participant Timeline and Visit Record",
        ],
        [
          "/studies/DEMO-01?stage=conduct&form=FORM-101",
          "SOP-ECRF-201",
          "eCRF Form Capture and Change Reason",
        ],
        [
          "/studies/DEMO-01?stage=conduct&observation=OBS-101",
          "SOP-OBS-210",
          "Clinical Observation Provenance Review",
        ],
      ];

      for (const [
        fullRoute,
        expectedSopCode,
        expectedFormattedAnchor,
      ] of queryTransitions) {
        await localRouter.push(fullRoute);
        await vi.waitFor(() => {
          expect(knowledgeStore.currentRoute).toBe(fullRoute);
          expect(knowledgeStore.loading).toBe(false);
        });
        await wrapper.vm.$nextTick();

        expect(resolveSpy).toHaveBeenLastCalledWith({
          route: fullRoute,
          persona: "site_crc",
        });
        expect(wrapper.text()).toContain(expectedSopCode);
        expect(wrapper.find("#btn-jump-spotlight-section").text()).toBe(
          expectedFormattedAnchor
        );
      }

      wrapper.unmount();
    });
  });

  describe("8. Inline Support Tickets Tab, Scope Pre-Fill, Queue Routing & Auditor Read-Only UX", () => {
    it("pre-fills study_id, site_id, and pseudonymous subject_id from route queries while excluding raw participant UUIDs and CRF_FORM", async () => {
      knowledgeStore.activePersona = "site_crc";
      knowledgeStore.primaryArticle = mockResolvedSop.primary_article;

      const ecrfPayload = knowledgeStore.prepareEscalation(
        "/ecrf?studyId=STUDY-A&siteId=SITE-101&subjectId=SUBJ-1001"
      );
      expect(ecrfPayload.study_id).toBe("STUDY-A");
      expect(ecrfPayload.site_id).toBe("SITE-101");
      expect(ecrfPayload.subject_id).toBe("SUBJ-1001");
      expect(ecrfPayload.entity_type).toBe("");
      expect(ecrfPayload.entity_id).toBeNull();

      // Raw participant:<uuid> NodeRefs must NOT populate subject_id
      const uuidNodeRoute =
        "/studies/STUDY-A?stage=conduct&site=SITE-101&participant=participant:00000000-0000-4000-8000-000000000101"; // deid-ignore: synthetic NodeRef UUID fixture
      const canvasPayload = knowledgeStore.prepareEscalation(uuidNodeRoute);
      expect(canvasPayload.study_id).toBe("STUDY-A");
      expect(canvasPayload.site_id).toBe("SITE-101");
      expect(canvasPayload.subject_id).toBe("");
      expect(canvasPayload.entity_type).toBe("");

      // Escalating from /overview with SYSTEM_SUPPORT routes to PLATFORM_ADMIN_ACCESS instead of DATA_CAPTURE_ECRF
      knowledgeStore.activePersona = "super_admin";
      const overviewPayload = knowledgeStore.prepareEscalation("/overview");
      expect(overviewPayload.category).toBe("SYSTEM_SUPPORT");
      expect(overviewPayload.entity_type).toBe("");

      vi.stubEnv("VITE_DEMO_MODE", "true");
      vi.stubGlobal(
        "fetch",
        vi.fn(() => Promise.reject(new TypeError("Offline demo fallback")))
      );
      const createdOverviewTicket = await ticketsApi.createTicket(
        overviewPayload,
        overviewPayload.reason_for_change
      );
      expect(createdOverviewTicket.assigned_queue).toBe(
        "PLATFORM_ADMIN_ACCESS"
      );
    });

    it("loads scoped tickets in the Support Tickets tab, opens ticket detail via getTicket, and refreshes persisted records", async () => {
      const scopedTicketSummary = {
        id: "b2c3d4e5-0000-4000-8000-000000000109", // deid-ignore: synthetic ticket UUID fixture
        reference: "TKT-2026-000109",
        title: "Cycle 2 lab unit query on STUDY-A",
        description: "Verify SI unit mapping for SUBJ-1001.",
        category: "DATA_QUERY",
        priority: "HIGH",
        gxp_severity: "MAJOR",
        status: "OPEN",
        assigned_queue: "DATA_CAPTURE_ECRF",
        study_id: "STUDY-A",
        site_id: "SITE-101",
        subject_id: "SUBJ-1001",
        acknowledge_target_at: "2026-11-20T15:00:00Z", // deid-ignore: synthetic SLA timestamp fixture
        sla_target_at: "2026-11-20T22:00:00Z", // deid-ignore: synthetic SLA timestamp fixture
      };

      const listSpy = vi
        .spyOn(ticketsApi, "listTickets")
        .mockResolvedValue([scopedTicketSummary]);
      const getSpy = vi.spyOn(ticketsApi, "getTicket").mockResolvedValue({
        ...scopedTicketSummary,
        description:
          "Detailed chemistry SI unit verification for SUBJ-1001 at SITE-101.",
      });

      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        mockResolvedSop
      );
      await knowledgeStore.fetchContextualHelp(
        "/ecrf?studyId=STUDY-A&siteId=SITE-101&subjectId=SUBJ-1001",
        "site_crc"
      );
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: { plugins: [pinia, router] },
      });

      await wrapper.find("#tab-contextual-tickets").trigger("click");
      await vi.waitFor(() =>
        expect(listSpy).toHaveBeenCalledWith({
          study_id: "STUDY-A",
          site_id: "SITE-101",
        })
      );
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#drawer-tickets-list").exists()).toBe(true);
      expect(wrapper.text()).toContain("TKT-2026-000109");
      expect(wrapper.text()).toContain("Cycle 2 lab unit query on STUDY-A");

      await wrapper.find(".drawer-ticket-card").trigger("click");
      await vi.waitFor(() =>
        expect(getSpy).toHaveBeenCalledWith(scopedTicketSummary.id)
      );
      await wrapper.vm.$nextTick();

      const detailCard = wrapper.find("#drawer-ticket-detail-card");
      expect(detailCard.exists()).toBe(true);
      expect(detailCard.text()).toContain("TKT-2026-000109");
      expect(detailCard.text()).toContain("DATA_CAPTURE_ECRF");
      expect(detailCard.text()).toContain("STUDY-A / SITE-101");
      expect(detailCard.text()).toContain("SUBJ-1001");
      expect(detailCard.text()).toContain("2026-11-20 15:00 UTC"); // deid-ignore: synthetic SLA display assertion
      expect(detailCard.text()).toContain("2026-11-20 22:00 UTC"); // deid-ignore: synthetic SLA display assertion
      expect(detailCard.text()).toContain(
        "Detailed chemistry SI unit verification"
      );

      await wrapper.find("#btn-back-to-ticket-list").trigger("click");
      expect(wrapper.find("#drawer-ticket-detail-card").exists()).toBe(false);
      expect(wrapper.find("#drawer-tickets-list").exists()).toBe(true);

      await wrapper.find("#btn-refresh-drawer-tickets").trigger("click");
      await vi.waitFor(() => expect(listSpy).toHaveBeenCalledTimes(2));
      wrapper.unmount();
    });

    it("disables ticket escalation for read-only auditor roles with a visible explanation while allowing SOP and ticket inspection", async () => {
      authStore.currentPersona = "auditor";
      authStore.rawRoles = ["Auditor"];

      const auditorTicket = {
        id: "c3d4e5f6-0000-4000-8000-000000000201", // deid-ignore: synthetic ticket UUID fixture
        reference: "TKT-2026-000201",
        title: "Audit inspection review of STUDY-A",
        description: "Read-only ticket visible to auditor.",
        category: "REGULATORY_QUERY",
        priority: "MEDIUM",
        gxp_severity: "MINOR",
        status: "OPEN",
        assigned_queue: "REGULATORY_COMPLIANCE",
        study_id: "STUDY-A",
        site_id: null,
        subject_id: null,
        acknowledge_target_at: "2026-11-20T18:00:00Z", // deid-ignore: synthetic SLA timestamp fixture
        sla_target_at: "2026-11-21T14:00:00Z", // deid-ignore: synthetic SLA timestamp fixture
      };

      const createSpy = vi.spyOn(ticketsApi, "createTicket");
      const listSpy = vi
        .spyOn(ticketsApi, "listTickets")
        .mockResolvedValue([auditorTicket]);
      vi.spyOn(ticketsApi, "getTicket").mockResolvedValue(auditorTicket);
      vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue(
        getFallbackContextualHelp("/audit", "auditor")
      );

      await knowledgeStore.fetchContextualHelp("/audit", "auditor");
      knowledgeStore.openDrawer();

      const wrapper = mount(ContextualHelpDrawer, {
        global: { plugins: [pinia, router] },
      });
      await wrapper.vm.$nextTick();

      // Auditor can read the SOP
      expect(wrapper.text()).toContain("SOP-AUD-401");

      // Escalate button is disabled and accompanied by visible read-only explanation
      const escalateBtn = wrapper.find("#btn-escalate-support-ticket");
      expect(escalateBtn.exists()).toBe(true);
      expect(escalateBtn.attributes("disabled")).toBeDefined();
      expect(escalateBtn.attributes("aria-disabled")).toBe("true");

      const readonlyNote = wrapper.find("#auditor-escalation-readonly-note");
      expect(readonlyNote.exists()).toBe(true);
      expect(readonlyNote.text()).toContain("Read-only auditor access");

      // Attempting to click escalate does not open TicketCreateModal or issue POST
      await escalateBtn.trigger("click");
      expect(knowledgeStore.isEscalating).toBe(false);
      expect(wrapper.find(".ticket-create-modal").exists()).toBe(false);
      expect(createSpy).not.toHaveBeenCalled();

      // Auditor can still switch to Support Tickets tab and inspect scoped tickets
      await wrapper.find("#tab-contextual-tickets").trigger("click");
      await vi.waitFor(() => expect(listSpy).toHaveBeenCalledTimes(1));
      await wrapper.vm.$nextTick();

      expect(wrapper.text()).toContain("TKT-2026-000201");
      await wrapper.find(".drawer-ticket-card").trigger("click");
      await wrapper.vm.$nextTick();
      expect(wrapper.find("#drawer-ticket-detail-card").text()).toContain(
        "REGULATORY_COMPLIANCE"
      );
      wrapper.unmount();
    });
  });
});
