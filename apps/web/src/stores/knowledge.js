import { defineStore } from "pinia";
import { knowledgeService } from "../api/knowledge.js";
import { useAuthStore } from "./auth.js";

export const useKnowledgeStore = defineStore("knowledge", {
  state: () => ({
    isOpen: false,
    currentRoute: "/ecrf",
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
    isEscalating: false,
    escalationPayload: null,
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
    openDrawer() {
      this.isOpen = true;
    },

    closeDrawer() {
      this.isOpen = false;
      this.searchQuery = "";
      this.searchResults = [];
      this.isSearching = false;
      this.selectedArticle = null;
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
      const authStore = useAuthStore();
      const targetRoute = route || this.currentRoute || "/";
      const targetPersona = persona || authStore.currentPersona || "super_admin";

      this.currentRoute = targetRoute;
      this.activePersona = targetPersona;
      this.loading = true;
      this.error = null;

      try {
        const response = await knowledgeService.resolveContextualHelp({
          route: targetRoute,
          persona: targetPersona,
        });

        if (response) {
          this.matchedMapping = response.matched_mapping || null;
          this.primaryArticle = response.primary_article || response.article || null;
          this.primaryVersion = response.primary_version || response.version || null;
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
        this.error = err.message || "Failed to resolve contextual guidance.";
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
        return;
      }

      this.isSearching = true;
      this.loading = true;
      try {
        const articles = await knowledgeService.listArticles();
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
        }
      } catch (err) {
        console.warn("Manual article search error:", err);
      } finally {
        this.loading = false;
      }
    },

    prepareEscalation() {
      const activeArticle = this.displayedArticle;
      const articleTitle = activeArticle?.title || "None";
      const articleId = activeArticle?.id || "N/A";
      const route = this.currentRoute;
      const persona = this.activePersona;

      let defaultCategory = "SYSTEM_SUPPORT";
      if (route.startsWith("/ecrf")) {
        defaultCategory = "PROTOCOL_DEVIATION";
      } else if (route.startsWith("/mdr") || route.startsWith("/rules")) {
        defaultCategory = "DATA_QUERY";
      } else if (route.startsWith("/ctms")) {
        defaultCategory = "MONITORING_FINDING";
      } else if (route.startsWith("/audit")) {
        defaultCategory = "REGULATORY_QUERY";
      }

      this.escalationPayload = {
        title: `[Help Escalation] Operational Assistance on ${route}`,
        description: `User requested operational assistance from in-page Contextual Help.\n\nContext Details:\n- Route: ${route}\n- Persona: ${persona}\n- Matched SOP: ${articleTitle} (ID: ${articleId})\n- Timestamp: ${new Date().toISOString()}\n\nPlease review and advise site/operations team.`,
        category: defaultCategory,
        gxp_severity: "MINOR",
        priority: "MEDIUM",
        entity_type: "CRF_FORM",
        entity_id: articleId !== "N/A" ? articleId : null,
        reason_for_change: `Escalated to Support Ticket from Contextual Help on ${route}`,
      };

      this.isEscalating = true;
      return this.escalationPayload;
    },

    cancelEscalation() {
      this.isEscalating = false;
      this.escalationPayload = null;
    },
  },
});
