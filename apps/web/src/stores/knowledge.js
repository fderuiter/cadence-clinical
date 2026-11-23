import { defineStore, getActivePinia } from "pinia";
import { findSection, findStage } from "../canvas/lifecycle";
import { collectionInstanceId, parseNodeRef } from "../canvas/nodeRef";
import { describeApiError } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { useAuthStore } from "./auth.js";

function getLoadedStudyCanvasStore() {
  return getActivePinia()?._s?.get("studyCanvas") || null;
}

let knowledgeServicePromise;
const loadKnowledgeService = () =>
  (knowledgeServicePromise ??= import("../api/knowledge.js").then(
    (module) => module.knowledgeService
  ));

const DEMO_ESCALATED_TICKETS_STORAGE_KEY = "cadence_demo_escalated_tickets";
const PSEUDONYMOUS_SUBJECT_CODE_REGEX =
  /^[A-Za-z]{2,12}[-_][A-Za-z0-9][A-Za-z0-9_-]{0,24}$/;

export function sanitizePseudonymousSubjectCode(value) {
  const trimmed = typeof value === "string" ? value.trim() : "";
  return PSEUDONYMOUS_SUBJECT_CODE_REGEX.test(trimmed) ? trimmed : "";
}

const UUID_IN_TEXT_REGEX =
  /\b([0-9a-f]{8})-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;

function formatSafeForTicketFreeText(value) {
  return String(value || "").replace(UUID_IN_TEXT_REGEX, "REF-$1");
}

function firstQueryString(value) {
  if (Array.isArray(value)) {
    return typeof value[0] === "string" ? value[0].trim() : "";
  }
  return typeof value === "string" ? value.trim() : "";
}

export function canonicalizeContextualRoute(routeOrLocation) {
  let rawPath = "/";
  let rawStage = "";
  let rawSection = "";

  if (typeof routeOrLocation === "string") {
    const clean = routeOrLocation.trim().split("#")[0] || "/";
    const qIdx = clean.indexOf("?");
    rawPath = qIdx >= 0 ? clean.slice(0, qIdx) : clean;
    if (qIdx >= 0) {
      const params = new URLSearchParams(clean.slice(qIdx + 1));
      rawStage = (params.get("stage") || "").trim().toLowerCase();
      rawSection = (params.get("section") || "").trim().toLowerCase();
    }
  } else if (routeOrLocation && typeof routeOrLocation === "object") {
    rawPath = String(routeOrLocation.path || "/").split(/[?#]/)[0] || "/";
    rawStage = firstQueryString(routeOrLocation.query?.stage).toLowerCase();
    rawSection = firstQueryString(routeOrLocation.query?.section).toLowerCase();
  }

  const prefixed = rawPath.startsWith("/") ? rawPath : `/${rawPath}`;
  const normalizedPath = prefixed.replace(/\/+$/, "") || "/";

  const isStudyCanvasPath =
    normalizedPath === "/studies" || /^\/studies\/[^/]+$/.test(normalizedPath);
  if (!isStudyCanvasPath) {
    return normalizedPath;
  }

  const matchedSection = rawSection ? findSection(rawSection) : undefined;
  const canonicalSection = matchedSection
    ? matchedSection.section.id
    : rawSection || "";
  const canonicalStage = matchedSection
    ? matchedSection.stage.id
    : rawStage
      ? findStage(rawStage)?.id || rawStage
      : "";

  if (canonicalStage && canonicalSection) {
    return `${normalizedPath}?stage=${canonicalStage}&section=${canonicalSection}`;
  }
  if (canonicalStage) {
    return `${normalizedPath}?stage=${canonicalStage}`;
  }
  if (canonicalSection) {
    return `${normalizedPath}?section=${canonicalSection}`;
  }
  return normalizedPath;
}

function resolveAuthorizedScope(candidate, lists, allowAll = false) {
  const clean = typeof candidate === "string" ? candidate.trim() : "";
  if (!clean) return "";
  const allowed = lists.flat().filter(Boolean);
  if (allowAll || allowed.length === 0 || allowed.includes("*")) return clean;
  const upper = clean.toUpperCase();
  return allowed.some((s) => String(s).toUpperCase() === upper) ? clean : "";
}

const ROUTE_CATEGORY_GROUPS = [
  ["PROTOCOL_DEVIATION", ["/ecrf"]],
  [
    "DATA_QUERY",
    ["/rules", "/coding", "/data-lock", "/observations", "/lakehouse", "/labs"],
  ],
  ["MONITORING_FINDING", ["/ctms"]],
  ["SITE_OPERATIONS", ["/ssu", "/dct", "/sensors"]],
  ["SUPPLY_EXCURSION", ["/devices", "/device-investigation"]],
  ["SAFETY_ADVERSE_EVENT", ["/safety", "/adjudication", "/cec"]],
  [
    "REGULATORY_QUERY",
    [
      "/audit",
      "/auditor",
      "/etmf",
      "/documents",
      "/quality",
      "/training",
      "/icf-builder",
      "/econsent-authoring",
      "/exports",
      "/lay-summaries",
    ],
  ],
  ["ACCESS_CONTROL", ["/forbidden"]],
  ["TECHNICAL_SYSTEM", ["/unavailable"]],
];

const CANVAS_DATA_SECTIONS = new Set(["queries", "locks", "validation"]);
const CANVAS_REG_SECTIONS = new Set(["documents", "training", "exports"]);
const CANVAS_SITE_SECTIONS = new Set([
  "sites",
  "site-activation",
  "startup-greenlight",
  "site-milestones",
  "delegation",
  "participants",
]);

function inferEscalationCategory({
  path,
  stage,
  section,
  hasActiveFormOrObservation,
  hasActiveParticipantOrSite,
}) {
  if (path === "/studies" || path.startsWith("/studies/")) {
    if (
      hasActiveFormOrObservation ||
      CANVAS_DATA_SECTIONS.has(section) ||
      stage === "data" ||
      stage === "analyze"
    ) {
      return "DATA_QUERY";
    }
    if (section === "monitoring-visits") return "MONITORING_FINDING";
    if (section === "supply") return "SUPPLY_EXCURSION";
    if (CANVAS_REG_SECTIONS.has(section) || stage === "submit") {
      return "REGULATORY_QUERY";
    }
    if (
      CANVAS_SITE_SECTIONS.has(section) ||
      stage === "start" ||
      stage === "conduct" ||
      hasActiveParticipantOrSite
    ) {
      return "SITE_OPERATIONS";
    }
    return "SYSTEM_SUPPORT";
  }

  for (const [category, prefixes] of ROUTE_CATEGORY_GROUPS) {
    if (prefixes.some((p) => path.startsWith(p))) return category;
  }
  return "SYSTEM_SUPPORT";
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
    canvasEscalationContext: null,
    escalatedTickets: isDemoBuild() ? loadSessionEscalatedTickets() : [],
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
    resetDrawerViewState() {
      this.selectedArticle = null;
      this.searchQuery = "";
      this.searchResults = [];
      this.isSearching = false;
    },

    invalidateContextualState() {
      this.resetDrawerViewState();
      this.isEscalating = false;
      this.escalationPayload = null;
    },

    setCanvasEscalationContext(context) {
      this.canvasEscalationContext =
        context && typeof context === "object" ? { ...context } : null;
    },

    clearCanvasEscalationContext() {
      this.canvasEscalationContext = null;
    },

    openDrawer() {
      this.isOpen = true;
    },

    closeDrawer() {
      this.isOpen = false;
      this.resetDrawerViewState();
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
      const rawRoute = route || this.currentRoute || "/";
      const targetRoute = canonicalizeContextualRoute(rawRoute);
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

    prepareEscalation(overrides = {}) {
      const authStore = useAuthStore();
      const canvasStore = getLoadedStudyCanvasStore();
      const activeArticle = this.displayedArticle;
      const articleTitle = activeArticle?.title || "None";
      const articleId = activeArticle?.id || "N/A";
      const route = String(overrides.route || this.currentRoute || "/").trim();
      const persona = this.activePersona;

      const cleanWithoutHash = route.split("#")[0] || "/";
      const qIdx = cleanWithoutHash.indexOf("?");
      const path =
        (qIdx >= 0
          ? cleanWithoutHash.slice(0, qIdx)
          : cleanWithoutHash
        ).replace(/\/+$/, "") || "/";
      const routeParams = new URLSearchParams(
        qIdx >= 0 ? cleanWithoutHash.slice(qIdx + 1) : ""
      );

      const queryObj =
        overrides.query && typeof overrides.query === "object"
          ? overrides.query
          : null;
      const getQuery = (key) =>
        firstQueryString(queryObj?.[key]) ||
        (routeParams.get(key) || "").trim();

      const isCanvasRoute = path === "/studies" || path.startsWith("/studies/");
      const studyMatch = path.match(/^\/studies\/([^/?#]+)/);
      const studyFromRoute = studyMatch
        ? decodeURIComponent(studyMatch[1])
        : getQuery("studyId") || getQuery("studyCode");

      const canvasCtx =
        isCanvasRoute &&
        this.canvasEscalationContext &&
        (!studyFromRoute ||
          !this.canvasEscalationContext.studyCode ||
          this.canvasEscalationContext.studyCode === studyFromRoute)
          ? this.canvasEscalationContext
          : null;

      const rawSection = (
        overrides.section ||
        canvasCtx?.section ||
        getQuery("section")
      ).toLowerCase();
      const matchedSection = rawSection ? findSection(rawSection) : undefined;
      const section = matchedSection ? matchedSection.section.id : rawSection;
      const rawStage = (
        overrides.stage ||
        canvasCtx?.stage ||
        getQuery("stage")
      ).toLowerCase();
      const stage = matchedSection
        ? matchedSection.stage.id
        : findStage(rawStage)?.id || rawStage;

      const candidateStudy =
        overrides.studyId ||
        studyFromRoute ||
        canvasCtx?.studyCode ||
        (isCanvasRoute ? canvasStore?.studyCode : "") ||
        "";

      const participantQueryRef = parseNodeRef(
        overrides.participant ||
          canvasCtx?.participantNodeRef ||
          getQuery("participant")
      );
      const selectedNodeRef = parseNodeRef(
        overrides.node || canvasCtx?.nodeRef || getQuery("node")
      );
      const participantUuid =
        (participantQueryRef?.kind === "participant"
          ? participantQueryRef.id
          : "") ||
        (selectedNodeRef?.kind === "participant" ? selectedNodeRef.id : "");
      const knownParticipant = participantUuid
        ? canvasStore?.knownParticipants?.[participantUuid] || null
        : null;

      const subjectFromEcrfPath = path.match(/^\/ecrf\/subjects\/([^/?#]+)/);
      const rawSubjectCandidate =
        overrides.subjectCode ||
        canvasCtx?.subjectCode ||
        knownParticipant?.subject_id ||
        (isCanvasRoute ? canvasStore?.record?.subjectCode : "") ||
        getQuery("subjectId") ||
        getQuery("subject") ||
        (subjectFromEcrfPath ? decodeURIComponent(subjectFromEcrfPath[1]) : "");
      const subjectCode = sanitizePseudonymousSubjectCode(rawSubjectCandidate);

      const candidateSite =
        overrides.siteCode ||
        canvasCtx?.siteCode ||
        knownParticipant?.site_id ||
        getQuery("site") ||
        getQuery("siteId") ||
        (isCanvasRoute ? canvasStore?.participantsFilter?.siteId : "") ||
        "";

      const authorizedStudyId = resolveAuthorizedScope(
        candidateStudy,
        [authStore?.assignedStudies || [], authStore?.studyIds || []],
        Boolean(authStore?.hasAllStudiesScope)
      );
      const authorizedSiteId = resolveAuthorizedScope(candidateSite, [
        authStore?.assignedSites || [],
        authStore?.siteIds || [],
      ]);

      const rawFormParam =
        overrides.form || canvasCtx?.form || getQuery("form");
      const parsedFormId =
        overrides.formId ||
        canvasCtx?.formId ||
        (rawFormParam && rawFormParam !== "new"
          ? collectionInstanceId(rawFormParam) ||
            (/^[A-Za-z0-9][A-Za-z0-9_-]{1,63}$/.test(rawFormParam)
              ? rawFormParam
              : "")
          : "") ||
        getQuery("formId");
      const formSpecCode =
        overrides.formSpecId ||
        canvasCtx?.formSpecId ||
        getQuery("spec") ||
        getQuery("collectionSpecId");
      const isNewForm = Boolean(canvasCtx?.isNewForm) || rawFormParam === "new";

      const obsPathMatch = path.match(/^\/observations\/([^/?#]+)/);
      const observationRef =
        overrides.observationRef ||
        canvasCtx?.observationRef ||
        getQuery("observation") ||
        (obsPathMatch ? decodeURIComponent(obsPathMatch[1]) : "");

      const proposalRef =
        overrides.proposalRef || canvasCtx?.proposalRef || getQuery("proposal");

      let entityType = "";
      let entityId = "";
      if (parsedFormId) {
        entityType = "CRF_FORM";
        entityId = parsedFormId;
      } else if (isNewForm && formSpecCode) {
        entityType = "CRF_FORM";
        entityId = formSpecCode;
      } else if (subjectCode) {
        entityType = "SUBJECT";
        entityId = subjectCode;
      } else if (
        section === "documents" ||
        path.startsWith("/etmf") ||
        path.startsWith("/documents")
      ) {
        const docId = getQuery("documentId") || getQuery("artifactId");
        if (docId) {
          entityType = "TMF_DOCUMENT";
          entityId = docId;
        }
      } else if (section === "monitoring-visits" || path.startsWith("/ctms")) {
        const visitId = getQuery("visitId") || getQuery("mvrId");
        if (visitId) {
          entityType = "MVR_VISIT";
          entityId = visitId;
        }
      } else if (section === "supply") {
        const kitId = getQuery("kitId");
        if (kitId) {
          entityType = "SUPPLY_KIT";
          entityId = kitId;
        }
      }

      const defaultCategory = inferEscalationCategory({
        path,
        stage,
        section,
        hasActiveFormOrObservation: Boolean(
          parsedFormId || isNewForm || observationRef
        ),
        hasActiveParticipantOrSite: Boolean(subjectCode || authorizedSiteId),
      });

      const safeRouteLabel = formatSafeForTicketFreeText(
        isCanvasRoute ? canonicalizeContextualRoute(route) : route
      );
      const contextLines = [
        `- Route: ${safeRouteLabel}`,
        `- Persona: ${persona}`,
        `- Matched SOP: ${articleTitle} (ID: ${formatSafeForTicketFreeText(articleId)})`,
      ];
      if (authorizedStudyId) contextLines.push(`- Study: ${authorizedStudyId}`);
      if (authorizedSiteId) contextLines.push(`- Site: ${authorizedSiteId}`);
      if (subjectCode) contextLines.push(`- Subject: ${subjectCode}`);
      if (parsedFormId) {
        contextLines.push(
          `- Form ID: ${formatSafeForTicketFreeText(parsedFormId)}`
        );
      }
      if (observationRef) {
        contextLines.push(
          `- Observation: ${formatSafeForTicketFreeText(observationRef)}`
        );
      }
      if (proposalRef) {
        contextLines.push(
          `- Proposal: ${formatSafeForTicketFreeText(proposalRef)}`
        );
      }
      contextLines.push(`- Timestamp: ${new Date().toISOString()}`);

      this.escalationPayload = {
        title: `[Help Escalation] Operational Assistance on ${safeRouteLabel}`,
        description: `User requested operational assistance from in-page Contextual Help.\n\nContext Details:\n${contextLines.join("\n")}\n\nPlease review and advise site/operations team.`,
        category: defaultCategory,
        gxp_severity: "MINOR",
        priority: "MEDIUM",
        study_id: authorizedStudyId,
        site_id: authorizedSiteId,
        subject_id: subjectCode,
        entity_type: entityType,
        entity_id: entityId,
        source_route: safeRouteLabel,
        persona,
        reason_for_change: `Escalated to Support Ticket from Contextual Help on ${safeRouteLabel}`,
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
