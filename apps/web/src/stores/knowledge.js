import { defineStore } from "pinia";
import { describeApiError } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

const DEMO_ESCALATED_TICKETS_STORAGE_KEY = "cadence_demo_escalated_tickets";

function loadSessionEscalatedTickets() {
  if (typeof sessionStorage === "undefined") return [];
  try {
    const raw = sessionStorage.getItem(DEMO_ESCALATED_TICKETS_STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function saveSessionEscalatedTickets(tickets) {
  if (typeof sessionStorage === "undefined") return;
  try {
    sessionStorage.setItem(
      DEMO_ESCALATED_TICKETS_STORAGE_KEY,
      JSON.stringify(tickets)
    );
  } catch {
    // Ignore storage quota errors in restricted environments.
  }
}

export const useKnowledgeStore = defineStore("knowledge", {
  state: () => ({
    isOpen: false,
    currentRoute: "/ecrf",
    hasLoadedContextualHelp: false,
    activePersona: "super_admin",
    primaryArticle: null,
    primaryVersion: null,
    matchedMapping: null,
    sectionAnchor: null,
    relatedArticles: [],
    selectedArticle: null,
    isArticleBodyExpanded: true,
    searchQuery: "",
    searchResults: [],
    isSearching: false,
    loading: false,
    error: null,
    lastFailedAction: null,
    isEscalating: false,
    escalationPayload: null,
    escalatedTickets: loadSessionEscalatedTickets(),
    lastEscalatedTicket: null,
  }),

  getters: {
    displayedArticle: (state) => {
      return state.selectedArticle || state.primaryArticle;
    },
    hasGuidance: (state) => {
      return Boolean(state.primaryArticle || state.selectedArticle);
    },
    activeSectionAnchor: (state) => {
      if (state.selectedArticle) return null;
      return state.sectionAnchor;
    },
  },

  actions: {
    invalidateContextualState() {
      this.selectedArticle = null;
      this.searchQuery = "";
      this.searchResults = [];
      this.isSearching = false;
      this.isEscalating = false;
      this.escalationPayload = null;
    },

    openDrawer() {
      this.isOpen = true;
    },

    closeDrawer() {
      this.isOpen = false;
      this.invalidateContextualState();
    },

    toggleDrawer() {
      if (this.isOpen) {
        this.closeDrawer();
      } else {
        this.openDrawer();
      }
    },

    toggleArticleBody() {
      this.isArticleBodyExpanded = !this.isArticleBodyExpanded;
    },

    selectArticle(article) {
      this.selectedArticle = article;
      this.isArticleBodyExpanded = true;
    },

    backToPrimary() {
      this.selectedArticle = null;
      this.isArticleBodyExpanded = true;
    },

    async fetchContextualHelp(route, persona) {
      this.invalidateContextualState();
      const authStore = useAuthStore();
      const targetRoute = route || this.currentRoute || "/";
      const targetPersona =
        persona || authStore.currentPersona || "super_admin";

      this.currentRoute = targetRoute;
      this.activePersona = targetPersona;
      this.hasLoadedContextualHelp = true;
      this.loading = true;
      this.error = null;
      this.lastFailedAction = null;

      try {
        const { knowledgeService } = await import("../api/knowledge.js");
        const response = await knowledgeService.resolveContextualHelp({
          route: targetRoute,
          persona: targetPersona,
        });

        if (response) {
          this.matchedMapping = response.matched_mapping || null;
          this.primaryArticle =
            response.primary_article || response.article || null;
          this.primaryVersion =
            response.primary_version || response.version || null;
          this.sectionAnchor = response.section_anchor || null;
          this.relatedArticles = response.related_articles || [];
        } else {
          this.matchedMapping = null;
          this.primaryArticle = null;
          this.primaryVersion = null;
          this.sectionAnchor = null;
          this.relatedArticles = [];
        }
      } catch (err) {
        this.matchedMapping = null;
        this.primaryArticle = null;
        this.primaryVersion = null;
        this.sectionAnchor = null;
        this.relatedArticles = [];
        this.error =
          describeApiError(err) ||
          err?.message ||
          "Failed to resolve contextual guidance.";
        this.lastFailedAction = {
          type: "contextual",
          route: targetRoute,
          persona: targetPersona,
        };
      } finally {
        this.loading = false;
      }
    },

    async search(query) {
      const q = (query || "").trim();
      this.searchQuery = q;
      if (!q) {
        this.searchResults = [];
        this.isSearching = false;
        if (this.lastFailedAction?.type === "search") {
          this.error = null;
          this.lastFailedAction = null;
        }
        return;
      }

      this.isSearching = true;
      this.loading = true;
      this.error = null;
      this.lastFailedAction = null;
      try {
        const { knowledgeService } = await import("../api/knowledge.js");
        const articles = await knowledgeService.listArticles({
          status: "PUBLISHED",
          q,
          persona: this.activePersona,
        });
        if (Array.isArray(articles)) {
          const lowerQ = q.toLowerCase();
          this.searchResults = articles.filter((art) => {
            const matchTitle = art.title?.toLowerCase().includes(lowerQ);
            const matchBody = (art.body_markdown || art.body_html || "")
              .toLowerCase()
              .includes(lowerQ);
            const matchTags = Array.isArray(art.tags)
              ? art.tags.some((t) => t.toLowerCase().includes(lowerQ))
              : typeof art.tags === "string"
                ? art.tags.toLowerCase().includes(lowerQ)
                : false;
            return matchTitle || matchBody || matchTags;
          });
        } else {
          this.searchResults = [];
        }
      } catch (err) {
        this.searchResults = [];
        this.error =
          describeApiError(err) ||
          err?.message ||
          "Failed to search SOP articles.";
        this.lastFailedAction = {
          type: "search",
          query: q,
        };
      } finally {
        this.loading = false;
      }
    },

    async retryLastAction() {
      if (this.lastFailedAction?.type === "search") {
        return await this.search(this.lastFailedAction.query);
      }
      const targetRoute =
        this.lastFailedAction?.route || this.currentRoute || "/";
      const targetPersona =
        this.lastFailedAction?.persona || this.activePersona;
      return await this.fetchContextualHelp(targetRoute, targetPersona);
    },

    prepareEscalation() {
      const activeArticle = this.displayedArticle;
      const articleTitle = activeArticle?.title || "None";
      const articleId = activeArticle?.id || "N/A";
      const route = this.currentRoute || "/";
      const persona = this.activePersona;

      let defaultCategory = "SYSTEM_SUPPORT";
      if (route.startsWith("/ecrf")) {
        defaultCategory = "PROTOCOL_DEVIATION";
      } else if (
        route.startsWith("/mdr") ||
        route.startsWith("/rules") ||
        route.startsWith("/studies")
      ) {
        defaultCategory = "DATA_QUERY";
      } else if (route.startsWith("/ctms")) {
        defaultCategory = "MONITORING_FINDING";
      } else if (route.startsWith("/ssu")) {
        defaultCategory = "SITE_OPERATIONS";
      } else if (route.startsWith("/audit") || route.startsWith("/auditor")) {
        defaultCategory = "REGULATORY_QUERY";
      }

      const studyMatch = route.match(/^\/studies\/([^/?#]+)/);
      const inferredStudyId = studyMatch
        ? decodeURIComponent(studyMatch[1])
        : "";

      this.escalationPayload = {
        title: `[Help Escalation] Operational Assistance on ${route}`,
        description: `User requested operational assistance from in-page Contextual Help.\n\nContext Details:\n- Route: ${route}\n- Persona: ${persona}\n- Matched SOP: ${articleTitle} (ID: ${articleId})\n- Timestamp: ${new Date().toISOString()}\n\nPlease review and advise site/operations team.`,
        category: defaultCategory,
        gxp_severity: "MINOR",
        priority: "MEDIUM",
        study_id: inferredStudyId,
        entity_type: "CRF_FORM",
        entity_id: articleId !== "N/A" ? articleId : null,
        source_route: route,
        persona,
        reason_for_change: `Escalated to Support Ticket from Contextual Help on ${route}`,
      };

      this.isEscalating = true;
      return this.escalationPayload;
    },

    recordEscalatedTicket(ticket) {
      if (!ticket) return;
      this.lastEscalatedTicket = ticket;
      const deduped = this.escalatedTickets.filter(
        (existing) =>
          existing.id !== ticket.id && existing.reference !== ticket.reference
      );
      this.escalatedTickets = [ticket, ...deduped];
      if (isDemoBuild()) {
        saveSessionEscalatedTickets(this.escalatedTickets);
      }
    },

    clearEscalatedTickets() {
      this.escalatedTickets = [];
      this.lastEscalatedTicket = null;
      if (typeof sessionStorage !== "undefined") {
        sessionStorage.removeItem(DEMO_ESCALATED_TICKETS_STORAGE_KEY);
      }
    },

    cancelEscalation() {
      this.isEscalating = false;
      this.escalationPayload = null;
    },
  },
});
