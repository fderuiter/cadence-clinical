import { defineStore } from "pinia";
import { describeApiError } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

let knowledgeServicePromise;
const loadKnowledgeService = () =>
  (knowledgeServicePromise ??= import("../api/knowledge.js").then(
    (module) => module.knowledgeService
  ));

let ticketsApiPromise;
const loadTicketsApi = () =>
  (ticketsApiPromise ??= import("../api/ticketsApi").then(
    (module) => module.ticketsApi
  ));

const DEMO_ESCALATED_TICKETS_STORAGE_KEY = "cadence_demo_escalated_tickets";

/**
 * Matches `TicketPiiValidator.PseudonymousSubjectCodeRegex` (`^[A-Za-z]{2,12}[-_][A-Za-z0-9][A-Za-z0-9_-]{0,24}$`).
 * Raw `participant:<uuid>` NodeRefs and raw UUIDs are intentionally rejected so ticket pre-fill never violates PII/pseudonymity validation.
 */
export const PSEUDONYMOUS_SUBJECT_CODE_REGEX =
  /^[A-Za-z]{2,12}[-_][A-Za-z0-9][A-Za-z0-9_-]{0,24}$/;

export function isPseudonymousSubjectCode(value) {
  if (typeof value !== "string") return false;
  const trimmed = value.trim();
  return trimmed.length > 0 && PSEUDONYMOUS_SUBJECT_CODE_REGEX.test(trimmed);
}

function firstNonEmptyParam(searchParams, keys) {
  for (const key of keys) {
    const val = searchParams.get(key);
    if (typeof val === "string" && val.trim().length > 0) {
      return val.trim();
    }
  }
  return "";
}

function safeDecodeSegment(segment) {
  try {
    return decodeURIComponent(segment).trim();
  } catch {
    return String(segment || "").trim();
  }
}

/**
 * Extracts `study_id`, `site_id`, and pseudonymous `subject_id` from active route path segments and query parameters.
 */
export function extractClinicalScopeFromRoute(rawRoute) {
  const clean = String(rawRoute || "/").trim();
  const withoutHash = clean.split("#")[0] || "/";
  const queryIdx = withoutHash.indexOf("?");
  const pathname = queryIdx >= 0 ? withoutHash.slice(0, queryIdx) : withoutHash;
  const search = queryIdx >= 0 ? withoutHash.slice(queryIdx + 1) : "";
  const searchParams = new URLSearchParams(search);

  const studyPathMatch = pathname.match(/^\/studies\/([^/?#]+)/);
  const pathStudyId = studyPathMatch
    ? safeDecodeSegment(studyPathMatch[1])
    : "";
  const queryStudyId = firstNonEmptyParam(searchParams, [
    "studyId",
    "study_id",
    "studyCode",
    "study_code",
    "study",
  ]);
  const study_id = pathStudyId || queryStudyId;

  const sitePathMatch = pathname.match(/\/sites\/([^/?#]+)/);
  const pathSiteId = sitePathMatch ? safeDecodeSegment(sitePathMatch[1]) : "";
  const querySiteId = firstNonEmptyParam(searchParams, [
    "siteId",
    "site_id",
    "siteCode",
    "site_code",
    "site",
  ]);
  const site_id = querySiteId || pathSiteId;

  const subjectPathMatch = pathname.match(/\/subjects\/([^/?#]+)/);
  const pathSubjectCandidate = subjectPathMatch
    ? safeDecodeSegment(subjectPathMatch[1])
    : "";
  const querySubjectCandidate = firstNonEmptyParam(searchParams, [
    "subjectId",
    "subject_id",
    "subjectCode",
    "subject_code",
    "subject",
    "participant",
  ]);

  let subject_id = "";
  if (isPseudonymousSubjectCode(querySubjectCandidate)) {
    subject_id = querySubjectCandidate.trim();
  } else if (isPseudonymousSubjectCode(pathSubjectCandidate)) {
    subject_id = pathSubjectCandidate.trim();
  }

  return {
    pathname: pathname || "/",
    study_id,
    site_id,
    subject_id,
  };
}

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
    activeDrawerTab: "guidance",
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
    drawerTickets: [],
    hasLoadedDrawerTickets: false,
    isTicketsLoading: false,
    ticketsError: null,
    selectedTicketId: null,
    selectedTicket: null,
    isTicketDetailLoading: false,
    ticketDetailError: null,
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
    activeRouteScope: (state) => {
      return extractClinicalScopeFromRoute(state.currentRoute);
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

    openDrawer(tab) {
      if (tab === "tickets" || tab === "guidance") {
        this.activeDrawerTab = tab;
      }
      this.isOpen = true;
    },

    closeDrawer() {
      this.isOpen = false;
      this.activeDrawerTab = "guidance";
      this.selectedTicketId = null;
      this.selectedTicket = null;
      this.ticketDetailError = null;
      this.invalidateContextualState();
    },

    toggleDrawer() {
      if (this.isOpen) {
        this.closeDrawer();
      } else {
        this.openDrawer();
      }
    },

    setActiveDrawerTab(tab) {
      this.activeDrawerTab = tab === "tickets" ? "tickets" : "guidance";
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
        const knowledgeService = await loadKnowledgeService();
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
        const knowledgeService = await loadKnowledgeService();
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

    prepareEscalation(routeOverride) {
      const activeArticle = this.displayedArticle;
      const articleTitle = activeArticle?.title || "None";
      const articleId = activeArticle?.id || "N/A";
      const route =
        (typeof routeOverride === "string" && routeOverride.trim()) ||
        this.currentRoute ||
        "/";
      const persona = this.activePersona;
      const { pathname, study_id, site_id, subject_id } =
        extractClinicalScopeFromRoute(route);

      let defaultCategory = "SYSTEM_SUPPORT";
      if (pathname.startsWith("/ecrf")) {
        defaultCategory = "PROTOCOL_DEVIATION";
      } else if (
        pathname.startsWith("/mdr") ||
        pathname.startsWith("/rules") ||
        pathname.startsWith("/studies")
      ) {
        defaultCategory = "DATA_QUERY";
      } else if (pathname.startsWith("/ctms")) {
        defaultCategory = "MONITORING_FINDING";
      } else if (pathname.startsWith("/ssu")) {
        defaultCategory = "SITE_OPERATIONS";
      } else if (
        pathname.startsWith("/audit") ||
        pathname.startsWith("/auditor")
      ) {
        defaultCategory = "REGULATORY_QUERY";
      }

      this.escalationPayload = {
        title: `[Help Escalation] Operational Assistance on ${route}`,
        description: `User requested operational assistance from in-page Contextual Help.\n\nContext Details:\n- Route: ${route}\n- Persona: ${persona}\n- Matched SOP: ${articleTitle} (ID: ${articleId})\n- Timestamp: ${new Date().toISOString()}\n\nPlease review and advise site/operations team.`,
        category: defaultCategory,
        gxp_severity: "MINOR",
        priority: "MEDIUM",
        study_id,
        site_id,
        subject_id,
        entity_type: "",
        entity_id: null,
        source_route: route,
        persona,
        reason_for_change: `Escalated to Support Ticket from Contextual Help on ${route}`,
      };

      this.isEscalating = true;
      return this.escalationPayload;
    },

    async fetchDrawerTickets(filtersOverride) {
      this.isTicketsLoading = true;
      this.ticketsError = null;
      try {
        let filters = {};
        if (filtersOverride && typeof filtersOverride === "object") {
          filters = { ...filtersOverride };
        } else {
          const { study_id, site_id } = extractClinicalScopeFromRoute(
            this.currentRoute
          );
          if (study_id) filters.study_id = study_id;
          if (site_id) filters.site_id = site_id;
        }
        const ticketsApi = await loadTicketsApi();
        const items = await ticketsApi.listTickets(filters);
        this.drawerTickets = Array.isArray(items) ? items : [];
        this.hasLoadedDrawerTickets = true;
        return this.drawerTickets;
      } catch (err) {
        this.ticketsError =
          describeApiError(err) ||
          err?.message ||
          "Unable to load scoped support tickets.";
        return [];
      } finally {
        this.isTicketsLoading = false;
      }
    },

    async selectDrawerTicket(ticketOrId) {
      const summaryTicket =
        ticketOrId && typeof ticketOrId === "object" ? ticketOrId : null;
      const ticketId =
        summaryTicket?.id ||
        summaryTicket?.reference ||
        (typeof ticketOrId === "string" ? ticketOrId.trim() : "");
      if (!ticketId) return null;

      this.selectedTicketId = ticketId;
      if (summaryTicket) {
        this.selectedTicket = summaryTicket;
      }
      this.isTicketDetailLoading = true;
      this.ticketDetailError = null;

      try {
        const ticketsApi = await loadTicketsApi();
        const detailed = await ticketsApi.getTicket(ticketId);
        if (detailed) {
          this.selectedTicket = detailed;
          this.drawerTickets = this.drawerTickets.map((existing) =>
            existing.id === detailed.id ||
            existing.reference === detailed.reference
              ? detailed
              : existing
          );
        }
        return this.selectedTicket;
      } catch (err) {
        this.ticketDetailError =
          describeApiError(err) ||
          err?.message ||
          "Unable to load support ticket details.";
        return this.selectedTicket;
      } finally {
        this.isTicketDetailLoading = false;
      }
    },

    clearSelectedDrawerTicket() {
      this.selectedTicketId = null;
      this.selectedTicket = null;
      this.ticketDetailError = null;
    },

    openCreatedTicketInDrawer(ticket) {
      if (!ticket) return;
      this.recordEscalatedTicket(ticket);
      this.cancelEscalation();
      const deduped = this.drawerTickets.filter(
        (existing) =>
          existing.id !== ticket.id && existing.reference !== ticket.reference
      );
      this.drawerTickets = [ticket, ...deduped];
      this.hasLoadedDrawerTickets = true;
      this.ticketsError = null;
      this.ticketDetailError = null;
      this.selectedTicketId = ticket.id || ticket.reference || null;
      this.selectedTicket = ticket;
      this.activeDrawerTab = "tickets";
      this.isOpen = true;
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
