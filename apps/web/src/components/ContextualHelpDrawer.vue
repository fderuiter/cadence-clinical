<template>
  <div ref="drawerRootRef">
    <!-- Contextual Help Slide-Out Drawer -->
    <ClinicalDrawer
      id="contextual-help-drawer"
      :is-open="knowledgeStore.isOpen && !knowledgeStore.isEscalating"
      :restore-focus="() => !knowledgeStore.isEscalating"
      :teleport="false"
      size="sm"
      overlay-id="contextual-help-drawer-overlay"
      overlay-class="drawer-overlay"
      panel-class="drawer-panel"
      body-class="drawer-body"
      aria-labelledby="help-drawer-title"
      @close="knowledgeStore.closeDrawer()"
    >
      <template #header>
        <!-- Compact Single-Row Header -->
        <header class="drawer-header">
          <div class="header-title-group">
            <span class="header-icon" aria-hidden="true">
              <BookOpen :size="18" aria-hidden="true" />
            </span>
            <h3 id="help-drawer-title" class="drawer-title">
              Clinical Guidance &amp; SOPs
            </h3>
          </div>

          <div class="header-actions">
            <ClinicalIconButton
              v-if="knowledgeStore.activeDrawerTab === 'guidance'"
              class="btn-icon-toggle"
              variant="ghost"
              size="sm"
              :class="{ active: isSearchOpen }"
              :aria-pressed="isSearchOpen ? 'true' : 'false'"
              label="Toggle Search SOPs"
              title="Toggle Search SOPs"
              @click="toggleSearch"
            >
              <Search :size="16" aria-hidden="true" />
            </ClinicalIconButton>
            <ClinicalIconButton
              class="btn-close"
              variant="ghost"
              size="sm"
              label="Close Help Drawer"
              @click="knowledgeStore.closeDrawer()"
            >
              <X :size="18" aria-hidden="true" />
            </ClinicalIconButton>
          </div>
        </header>
      </template>

      <!-- Secondary Context Badges, View Tabs & Search Bar inside Scroll Body -->
      <div class="context-badges">
        <span class="context-badge route-badge" title="Active Route">
          <MapPin :size="12" aria-hidden="true" />
          <span>{{ knowledgeStore.currentRoute }}</span>
        </span>
        <span class="context-badge persona-badge" title="Active Persona">
          {{ activePersonaLabel }}
        </span>
      </div>

      <div
        class="drawer-tabs"
        role="tablist"
        aria-label="Contextual assistance views"
      >
        <button
          id="tab-contextual-guidance"
          type="button"
          role="tab"
          class="drawer-tab-btn"
          :class="{ active: knowledgeStore.activeDrawerTab === 'guidance' }"
          :aria-selected="
            knowledgeStore.activeDrawerTab === 'guidance' ? 'true' : 'false'
          "
          aria-controls="panel-contextual-guidance"
          @click="switchTab('guidance')"
        >
          <BookOpen :size="14" aria-hidden="true" />
          <span>SOP Guidance</span>
        </button>
        <button
          id="tab-contextual-tickets"
          type="button"
          role="tab"
          class="drawer-tab-btn"
          :class="{ active: knowledgeStore.activeDrawerTab === 'tickets' }"
          :aria-selected="
            knowledgeStore.activeDrawerTab === 'tickets' ? 'true' : 'false'
          "
          aria-controls="panel-contextual-tickets"
          @click="switchTab('tickets')"
        >
          <Ticket :size="14" aria-hidden="true" />
          <span>Support Tickets</span>
          <span
            v-if="knowledgeStore.hasLoadedDrawerTickets"
            class="tab-count-pill"
          >
            {{ knowledgeStore.drawerTickets.length }}
          </span>
        </button>
      </div>

      <div
        v-if="knowledgeStore.activeDrawerTab === 'guidance' && isSearchOpen"
        class="search-bar-row"
      >
        <label for="help-search-input" class="sr-only">
          Search clinical SOPs, guidelines, or topics
        </label>
        <input
          id="help-search-input"
          ref="searchInputRef"
          v-model="searchInput"
          type="text"
          class="search-input"
          placeholder="Search SOPs, guidelines, or topics..."
          @input="onSearchInput"
        />
        <ClinicalIconButton
          v-if="searchInput"
          class="btn-clear-search"
          variant="ghost"
          size="sm"
          label="Clear SOP search"
          @click="clearSearch"
        >
          <X :size="14" aria-hidden="true" />
        </ClinicalIconButton>
      </div>

      <!-- ================================================================ -->
      <!-- TAB 1: SOP GUIDANCE                                              -->
      <!-- ================================================================ -->
      <div
        v-if="knowledgeStore.activeDrawerTab === 'guidance'"
        id="panel-contextual-guidance"
        role="tabpanel"
        aria-labelledby="tab-contextual-guidance"
        class="drawer-tabpanel"
      >
        <!-- Loading State -->
        <div v-if="knowledgeStore.loading" class="loading-state">
          <div class="spinner" />
          <p>Resolving contextual SOP guidance...</p>
        </div>

        <!-- Error State -->
        <div
          v-else-if="knowledgeStore.error"
          id="contextual-help-error-alert"
          class="help-error-alert"
          role="alert"
          aria-live="assertive"
        >
          <div class="error-alert-header">
            <AlertTriangle
              class="error-alert-icon"
              :size="18"
              aria-hidden="true"
            />
            <h4 class="error-alert-title">
              {{
                knowledgeStore.lastFailedAction?.type === "search"
                  ? "Unable to Search SOP Articles"
                  : "Unable to Load Contextual SOP Guidance"
              }}
            </h4>
          </div>
          <p class="error-alert-message">{{ knowledgeStore.error }}</p>
          <div class="error-alert-actions">
            <button
              id="btn-contextual-help-retry"
              type="button"
              class="btn btn-secondary btn-retry"
              @click="handleRetry"
            >
              <RefreshCw :size="14" aria-hidden="true" />
              <span>Retry</span>
            </button>
          </div>
        </div>

        <!-- Search Results View -->
        <div
          v-else-if="isSearchOpen && searchInput.trim()"
          class="search-results-section"
        >
          <div class="section-subhead">
            <h4>Search Results ({{ knowledgeStore.searchResults.length }})</h4>
            <button
              v-if="knowledgeStore.selectedArticle"
              type="button"
              class="btn-link"
              @click="backFromSearchSelection"
            >
              Back to Matched SOP
            </button>
          </div>

          <div
            v-if="knowledgeStore.searchResults.length === 0"
            class="empty-search"
          >
            <p>
              No SOPs matched your query "<em>{{ searchInput }}</em
              >".
            </p>
          </div>

          <div v-else class="search-items-list">
            <button
              v-for="item in knowledgeStore.searchResults"
              :key="item.id"
              type="button"
              class="search-result-card"
              :class="{
                active: knowledgeStore.displayedArticle?.id === item.id,
              }"
              :aria-pressed="
                knowledgeStore.displayedArticle?.id === item.id
                  ? 'true'
                  : 'false'
              "
              @click="selectSearchResult(item)"
              @keydown.enter.prevent="selectSearchResult(item)"
              @keydown.space.prevent="selectSearchResult(item)"
            >
              <span class="result-card-header">
                <span class="result-title">
                  {{ item.title }}
                </span>
                <span v-if="item.version_label" class="badge badge-version">
                  v{{ item.version_label }}
                </span>
              </span>
              <span v-if="item.tags" class="tags-row">
                <span
                  v-for="t in formatTags(item.tags)"
                  :key="t"
                  class="tag-pill"
                >
                  #{{ t }}
                </span>
              </span>
            </button>
          </div>
        </div>

        <!-- Matched / Selected SOP View -->
        <div
          v-else-if="knowledgeStore.displayedArticle"
          class="guidance-content"
        >
          <!-- Back to Matched Spotlight SOP Bar if inspecting related -->
          <div v-if="knowledgeStore.selectedArticle" class="back-bar">
            <button
              ref="backToPrimaryBtnRef"
              type="button"
              class="btn-back"
              @click="returnToPrimarySop"
            >
              <ArrowLeft :size="14" aria-hidden="true" />
              <span>Return to Route Spotlight SOP</span>
            </button>
          </div>

          <!-- Spotlight SOP Card -->
          <div class="sop-card">
            <div class="sop-meta-header">
              <span class="category-badge">
                {{
                  knowledgeStore.displayedArticle.category_name ||
                  "Clinical Guidance"
                }}
              </span>
              <span
                v-if="knowledgeStore.displayedArticle.version_label"
                class="badge badge-version"
              >
                v{{ knowledgeStore.displayedArticle.version_label }}
              </span>
              <span class="badge badge-status">PUBLISHED</span>
            </div>

            <h4 class="sop-title">
              {{ knowledgeStore.displayedArticle.title }}
            </h4>

            <!-- Tags -->
            <div v-if="knowledgeStore.displayedArticle.tags" class="tags-row">
              <span
                v-for="tag in formatTags(knowledgeStore.displayedArticle.tags)"
                :key="tag"
                class="tag-pill"
              >
                #{{ tag }}
              </span>
            </div>

            <!-- Formatted Section Anchor Callout -->
            <div
              v-if="knowledgeStore.activeSectionAnchor"
              class="anchor-callout"
              :data-anchor-target="normalizedSectionAnchorId"
            >
              <Target class="anchor-icon" :size="16" aria-hidden="true" />
              <div class="anchor-text">
                <strong>Spotlight Section:</strong>
                <button
                  id="btn-jump-spotlight-section"
                  type="button"
                  class="anchor-jump-btn"
                  :title="`Scroll to ${formattedSectionAnchor}`"
                  @click="handleJumpToSectionAnchor"
                >
                  {{ formattedSectionAnchor }}
                </button>
              </div>
            </div>

            <!-- Article Body Header & Toggle -->
            <div class="body-toggle-bar">
              <span class="body-toggle-label">
                Standard Operating Procedure Content
              </span>
              <button
                ref="toggleBodyBtnRef"
                type="button"
                class="btn-toggle-body"
                :aria-expanded="
                  knowledgeStore.isArticleBodyExpanded ? 'true' : 'false'
                "
                aria-controls="contextual-help-sop-body"
                @click="knowledgeStore.toggleArticleBody()"
              >
                {{
                  knowledgeStore.isArticleBodyExpanded
                    ? "Collapse Body"
                    : "Expand Body"
                }}
              </button>
            </div>

            <!-- Collapsible Article Body -->
            <div
              v-if="knowledgeStore.isArticleBodyExpanded"
              id="contextual-help-sop-body"
              ref="sopBodyRef"
              class="sop-body prose-content"
            >
              <div v-if="displayedArticleHtml" v-html="displayedArticleHtml" />
              <div v-else-if="displayedArticleMarkdown" class="markdown-pre">
                {{ displayedArticleMarkdown }}
              </div>
            </div>
          </div>

          <!-- Related Articles / Secondary Guides -->
          <div
            v-if="
              !knowledgeStore.selectedArticle &&
              knowledgeStore.relatedArticles.length > 0
            "
            class="related-section"
          >
            <h5 class="related-title">
              <Library :size="14" aria-hidden="true" />
              <span>
                Related Clinical SOPs ({{
                  knowledgeStore.relatedArticles.length
                }})
              </span>
            </h5>
            <div class="related-list">
              <button
                v-for="rel in knowledgeStore.relatedArticles"
                :key="rel.id"
                type="button"
                class="related-card"
                @click="selectRelatedArticle(rel)"
                @keydown.enter.prevent="selectRelatedArticle(rel)"
                @keydown.space.prevent="selectRelatedArticle(rel)"
              >
                <span class="related-card-top">
                  <span class="related-card-title">{{ rel.title }}</span>
                  <span v-if="rel.version_label" class="badge badge-version-sm">
                    v{{ rel.version_label }}
                  </span>
                </span>
                <span v-if="rel.tags" class="tags-row">
                  <span
                    v-for="t in formatTags(rel.tags).slice(0, 2)"
                    :key="t"
                    class="tag-pill tag-pill-sm"
                  >
                    #{{ t }}
                  </span>
                </span>
              </button>
            </div>
          </div>
        </div>

        <!-- Empty Fallback State -->
        <div v-else class="empty-state">
          <Search class="empty-icon" :size="32" aria-hidden="true" />
          <h4>No Specific SOP Mapped for This Route</h4>
          <p>
            There is currently no published SOP mapped specifically to
            <code>{{ knowledgeStore.currentRoute }}</code> for persona
            <strong>{{ activePersonaLabel }}</strong>.
          </p>
          <div class="empty-actions">
            <button
              type="button"
              class="btn btn-secondary"
              @click="openSearchWithFallback"
            >
              <Search :size="14" aria-hidden="true" />
              <span>Search Knowledge Hub</span>
            </button>
            <button
              type="button"
              class="btn btn-secondary"
              @click="openHubRoute('/knowledge')"
            >
              <Library :size="14" aria-hidden="true" />
              <span>Open Full SOP Hub</span>
            </button>
          </div>
        </div>
      </div>

      <!-- ================================================================ -->
      <!-- TAB 2: INLINE SUPPORT TICKETS                                    -->
      <!-- ================================================================ -->
      <div
        v-else
        id="panel-contextual-tickets"
        role="tabpanel"
        aria-labelledby="tab-contextual-tickets"
        class="drawer-tabpanel tickets-panel"
      >
        <div class="tickets-toolbar">
          <div class="tickets-scope-summary">
            <span class="tickets-scope-label">Scope:</span>
            <span class="tickets-scope-value">{{ activeScopeSummary }}</span>
          </div>
          <button
            id="btn-refresh-drawer-tickets"
            type="button"
            class="btn btn-secondary btn-compact"
            :disabled="knowledgeStore.isTicketsLoading"
            @click="handleRefreshDrawerTickets"
          >
            <RefreshCw :size="13" aria-hidden="true" />
            <span>Refresh</span>
          </button>
        </div>

        <!-- Inline Ticket Detail Card -->
        <div
          v-if="knowledgeStore.selectedTicket"
          id="drawer-ticket-detail-card"
          class="ticket-detail-card"
        >
          <div class="ticket-detail-nav">
            <button
              id="btn-back-to-ticket-list"
              type="button"
              class="btn-back"
              @click="handleBackToTicketList"
            >
              <ArrowLeft :size="14" aria-hidden="true" />
              <span>Back to Scoped Tickets</span>
            </button>
            <span
              v-if="knowledgeStore.isTicketDetailLoading"
              class="detail-sync-indicator"
            >
              Refreshing...
            </span>
          </div>

          <div
            v-if="knowledgeStore.ticketDetailError"
            id="drawer-ticket-detail-error"
            class="help-error-alert"
            role="alert"
          >
            <div class="error-alert-header">
              <AlertTriangle :size="16" aria-hidden="true" />
              <h4 class="error-alert-title">
                Unable to Refresh Ticket Details
              </h4>
            </div>
            <p class="error-alert-message">
              {{ knowledgeStore.ticketDetailError }}
            </p>
          </div>

          <div class="ticket-detail-header">
            <span class="ticket-ref-code">
              {{
                knowledgeStore.selectedTicket.reference ||
                knowledgeStore.selectedTicket.id
              }}
            </span>
            <span class="badge badge-status">
              {{ knowledgeStore.selectedTicket.status || "OPEN" }}
            </span>
            <span
              v-if="knowledgeStore.selectedTicket.priority"
              class="badge badge-priority"
            >
              {{ knowledgeStore.selectedTicket.priority }}
            </span>
            <span
              v-if="knowledgeStore.selectedTicket.gxp_severity"
              class="badge badge-severity"
            >
              GxP: {{ knowledgeStore.selectedTicket.gxp_severity }}
            </span>
          </div>

          <h4 class="ticket-detail-title">
            {{ knowledgeStore.selectedTicket.title }}
          </h4>

          <p
            v-if="knowledgeStore.selectedTicket.description"
            class="ticket-detail-description"
          >
            {{ knowledgeStore.selectedTicket.description }}
          </p>

          <dl class="ticket-meta-grid">
            <div class="ticket-meta-item">
              <dt>Assigned Queue</dt>
              <dd class="mono-val">
                {{
                  knowledgeStore.selectedTicket.assigned_queue || "UNASSIGNED"
                }}
              </dd>
            </div>
            <div class="ticket-meta-item">
              <dt>Category</dt>
              <dd class="mono-val">
                {{ knowledgeStore.selectedTicket.category || "SYSTEM_SUPPORT" }}
              </dd>
            </div>
            <div class="ticket-meta-item">
              <dt>Study / Site</dt>
              <dd class="mono-val">
                {{
                  formatTicketScope(
                    knowledgeStore.selectedTicket.study_id,
                    knowledgeStore.selectedTicket.site_id
                  )
                }}
              </dd>
            </div>
            <div class="ticket-meta-item">
              <dt>Subject ID</dt>
              <dd class="mono-val">
                {{ knowledgeStore.selectedTicket.subject_id || "—" }}
              </dd>
            </div>
            <div class="ticket-meta-item">
              <dt>Acknowledge Target</dt>
              <dd class="mono-val">
                <Clock :size="12" aria-hidden="true" />
                <span>{{
                  formatTimestamp(
                    knowledgeStore.selectedTicket.acknowledge_target_at
                  )
                }}</span>
              </dd>
            </div>
            <div class="ticket-meta-item">
              <dt>Resolution SLA Target</dt>
              <dd class="mono-val">
                <Clock :size="12" aria-hidden="true" />
                <span>{{
                  formatTimestamp(knowledgeStore.selectedTicket.sla_target_at)
                }}</span>
              </dd>
            </div>
          </dl>
        </div>

        <!-- Inline Tickets Loading State -->
        <div v-else-if="knowledgeStore.isTicketsLoading" class="loading-state">
          <div class="spinner" />
          <p>Loading scoped support tickets...</p>
        </div>

        <!-- Inline Tickets Error State -->
        <div
          v-else-if="knowledgeStore.ticketsError"
          id="drawer-tickets-error-alert"
          class="help-error-alert"
          role="alert"
        >
          <div class="error-alert-header">
            <AlertTriangle :size="18" aria-hidden="true" />
            <h4 class="error-alert-title">Unable to Load Support Tickets</h4>
          </div>
          <p class="error-alert-message">{{ knowledgeStore.ticketsError }}</p>
          <div class="error-alert-actions">
            <button
              id="btn-retry-drawer-tickets"
              type="button"
              class="btn btn-secondary btn-retry"
              @click="handleRefreshDrawerTickets"
            >
              <RefreshCw :size="14" aria-hidden="true" />
              <span>Retry</span>
            </button>
          </div>
        </div>

        <!-- Empty Scoped Tickets State -->
        <div
          v-else-if="knowledgeStore.drawerTickets.length === 0"
          id="drawer-tickets-empty-state"
          class="empty-state"
        >
          <Ticket class="empty-icon" :size="32" aria-hidden="true" />
          <h4>No Support Tickets in Scope</h4>
          <p>
            No persisted support tickets currently match
            <strong>{{ activeScopeSummary }}</strong>.
          </p>
        </div>

        <!-- Scoped Ticket Cards List -->
        <div v-else id="drawer-tickets-list" class="tickets-list">
          <button
            v-for="tkt in knowledgeStore.drawerTickets"
            :key="tkt.id || tkt.reference"
            type="button"
            class="drawer-ticket-card"
            :data-ticket-ref="tkt.reference || tkt.id"
            @click="handleSelectDrawerTicket(tkt)"
          >
            <div class="drawer-ticket-card-top">
              <span class="ticket-ref-code">
                {{ tkt.reference || tkt.id }}
              </span>
              <div class="drawer-ticket-badges">
                <span class="badge badge-status">
                  {{ tkt.status || "OPEN" }}
                </span>
                <span v-if="tkt.priority" class="badge badge-priority">
                  {{ tkt.priority }}
                </span>
              </div>
            </div>
            <div class="drawer-ticket-title">{{ tkt.title }}</div>
            <div class="drawer-ticket-meta">
              <span class="mono-val">{{
                tkt.assigned_queue || "UNASSIGNED"
              }}</span>
              <span
                v-if="tkt.study_id || tkt.site_id"
                class="ticket-meta-sep"
                aria-hidden="true"
                >·</span
              >
              <span v-if="tkt.study_id || tkt.site_id" class="mono-val">
                {{ formatTicketScope(tkt.study_id, tkt.site_id) }}
              </span>
            </div>
          </button>
        </div>
      </div>

      <!-- Footer -->
      <template #footer>
        <div class="drawer-footer">
          <div class="drawer-footer-primary">
            <button
              id="btn-escalate-support-ticket"
              type="button"
              class="btn btn-escalate"
              :disabled="isReadOnlyAuditor"
              :aria-disabled="isReadOnlyAuditor ? 'true' : undefined"
              :aria-describedby="
                isReadOnlyAuditor
                  ? 'auditor-escalation-readonly-note'
                  : undefined
              "
              :title="
                isReadOnlyAuditor
                  ? 'Ticket escalation is disabled for read-only auditor roles'
                  : 'Log Support Ticket pre-populated with current route context'
              "
              @click="handleEscalate"
            >
              <Lock
                v-if="isReadOnlyAuditor"
                class="btn-icon"
                :size="15"
                aria-hidden="true"
              />
              <Ticket v-else class="btn-icon" :size="15" aria-hidden="true" />
              <span>Escalate to Support Ticket</span>
            </button>

            <p
              v-if="isReadOnlyAuditor"
              id="auditor-escalation-readonly-note"
              class="auditor-readonly-note"
            >
              Read-only auditor access: creating support tickets is disabled to
              preserve inspection independence. Use the Support Tickets tab to
              inspect scoped records.
            </p>

            <div class="drawer-hub-links">
              <button
                id="btn-open-knowledge-hub"
                type="button"
                class="btn-hub-link"
                @click="openHubRoute('/knowledge')"
              >
                <Library :size="13" aria-hidden="true" />
                <span>Browse SOP Hub</span>
              </button>
              <span class="hub-link-sep" aria-hidden="true">·</span>
              <button
                id="btn-open-tickets-hub"
                type="button"
                class="btn-hub-link"
                @click="openHubRoute('/tickets')"
              >
                <Ticket :size="13" aria-hidden="true" />
                <span>Support Queue</span>
              </button>
            </div>
          </div>
          <button
            type="button"
            class="btn btn-secondary"
            @click="knowledgeStore.closeDrawer()"
          >
            Close
          </button>
        </div>
      </template>
    </ClinicalDrawer>

    <!-- Ticket Escalation Modal -->
    <TicketCreateModal
      v-if="knowledgeStore.isEscalating && !isReadOnlyAuditor"
      :is-open="knowledgeStore.isEscalating"
      :initial-data="knowledgeStore.escalationPayload || {}"
      @close="knowledgeStore.cancelEscalation()"
      @created="handleTicketCreated"
    />
  </div>
</template>

<script setup>
import {
  ref,
  computed,
  watch,
  nextTick,
  onMounted,
  onUnmounted,
  getCurrentInstance,
} from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import {
  AlertTriangle,
  ArrowLeft,
  BookOpen,
  Clock,
  Library,
  Lock,
  MapPin,
  RefreshCw,
  Search,
  Target,
  Ticket,
  X,
} from "lucide-vue-next";
import {
  ClinicalDrawer,
  ClinicalIconButton,
  hasActiveModalFocusTrap,
  useEscapeClose,
} from "ui";
import { isScreenAvailable } from "../router/availability.js";
import {
  buildScopedWorkflowPath,
  resolveWorkspaceNavigationScope,
} from "../canvas/palette";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import {
  canonicalizeContextualRoute,
  useKnowledgeStore,
} from "../stores/knowledge.js";
import {
  useAuthStore,
  PERSONA_PRESETS,
  normalizeRole,
} from "../stores/auth.js";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "../utils/demoMode.js";
import TicketCreateModal from "./tickets/TicketCreateModal.vue";

const OPERATIONAL_MUTATION_ROLES = new Set([
  "sponsor_admin",
  "super_admin",
  "sponsor_designer",
  "data_manager",
  "cra",
  "monitor",
  "safety_reviewer",
  "safety_officer",
  "sponsor_mm",
  "crc",
  "site_investigator",
  "principal_investigator",
  "investigator",
]);

const CLINICAL_ACRONYMS = {
  ecrf: "eCRF",
  crf: "CRF",
  sdv: "SDV",
  ctms: "CTMS",
  ssu: "SSU",
  etmf: "eTMF",
  tmf: "TMF",
  gxp: "GxP",
  edc: "EDC",
  soa: "SoA",
  usdm: "USDM",
  pi: "PI",
  sae: "SAE",
  rtsm: "RTSM",
  irb: "IRB",
  iec: "IEC",
  qc: "QC",
  mdr: "MDR",
  capa: "CAPA",
  sla: "SLA",
  sop: "SOP",
};

const LOWERCASE_CONNECTORS = new Set([
  "and",
  "or",
  "of",
  "for",
  "in",
  "on",
  "to",
  "by",
  "with",
]);

const knowledgeStore = useKnowledgeStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const route = useRoute();
const instance = getCurrentInstance();
const { activeStudyCode } = useActiveStudyScope();
const activeCanvasStore = computed(() => {
  const pinia = getActivePinia();
  return pinia ? useStudyCanvasStore(pinia) : null;
});
let injectedRouter = null;
try {
  injectedRouter = useRouter();
} catch {
  injectedRouter = null;
}

const drawerRootRef = ref(null);
const searchInputRef = ref(null);
const backToPrimaryBtnRef = ref(null);
const toggleBodyBtnRef = ref(null);
const sopBodyRef = ref(null);
const isSearchOpen = ref(false);
const searchInput = ref("");
let searchDebounceTimer = null;

function resolveEscalationRoute() {
  const liveRoute = resolveActiveRouteLocation(route);
  const storeRoute = String(
    knowledgeStore.rawRoute || knowledgeStore.currentRoute || ""
  ).trim();
  if (!knowledgeStore.hasLoadedContextualHelp || !storeRoute) {
    return liveRoute || storeRoute || "/";
  }
  if (liveRoute && liveRoute.includes("?")) {
    const livePath = liveRoute.split("?")[0] || "/";
    const storePath = storeRoute.split("?")[0] || "/";
    if (livePath === storePath) {
      return liveRoute;
    }
  }
  return storeRoute;
}

function openHubRoute(path) {
  const scope = resolveWorkspaceNavigationScope(
    route,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  );
  const scopedPath = buildScopedWorkflowPath(path, {
    studyCode: scope.studyCode,
    siteId: scope.siteId,
    subjectCode: scope.subjectCode,
  });
  knowledgeStore.closeDrawer();
  const router =
    injectedRouter || instance?.appContext?.config?.globalProperties?.$router;
  if (router && typeof router.push === "function") {
    router.push(scopedPath);
  }
}

useEscapeClose(() => knowledgeStore.closeDrawer(), {
  isOpen: () => knowledgeStore.isOpen && !knowledgeStore.isEscalating,
});

const normalizedCallerRoles = computed(() =>
  (authStore.rawRoles || []).map(normalizeRole).filter(Boolean)
);

const isReadOnlyAuditor = computed(() => {
  if (authStore.currentPersona === "auditor") {
    return true;
  }
  const roles = normalizedCallerRoles.value;
  const hasAuditorRole = roles.some(
    (r) => r === "auditor" || r === "tmf_auditor"
  );
  if (!hasAuditorRole) {
    return false;
  }
  if (!authStore.isDemoMode) {
    return true;
  }
  return !roles.some((r) => OPERATIONAL_MUTATION_ROLES.has(r));
});

const canonicalRoute = computed(() =>
  canonicalizeContextualRoute({
    path: route?.path || "/",
    query: route?.query || {},
  })
);

const activePersonaLabel = computed(() => {
  const pKey =
    authStore.currentPersona || knowledgeStore.activePersona || "super_admin";
  const p = PERSONA_PRESETS.find((preset) => preset.key === pKey);
  return p ? p.label.replace(/^.+?\s/, "") : "Super Admin";
});

const displayedArticleHtml = computed(() => {
  const article = knowledgeStore.displayedArticle;
  if (!article) return "";
  if (article.body_html) return article.body_html;
  if (
    !knowledgeStore.selectedArticle &&
    knowledgeStore.primaryVersion?.body_html
  ) {
    return knowledgeStore.primaryVersion.body_html;
  }
  return "";
});

const displayedArticleMarkdown = computed(() => {
  const article = knowledgeStore.displayedArticle;
  if (!article) return "";
  if (article.body_markdown) return article.body_markdown;
  if (
    !knowledgeStore.selectedArticle &&
    knowledgeStore.primaryVersion?.body_markdown
  ) {
    return knowledgeStore.primaryVersion.body_markdown;
  }
  return "";
});

const normalizedSectionAnchorId = computed(() =>
  String(knowledgeStore.activeSectionAnchor || "")
    .trim()
    .replace(/^#+/, "")
);

function formatAnchorSlug(rawAnchor) {
  const clean = String(rawAnchor || "")
    .trim()
    .replace(/^#+/, "");
  if (!clean) return "";
  const words = clean.split(/[-_\s]+/).filter(Boolean);
  return words
    .map((word, idx) => {
      const lower = word.toLowerCase();
      if (CLINICAL_ACRONYMS[lower]) {
        return CLINICAL_ACRONYMS[lower];
      }
      if (idx > 0 && LOWERCASE_CONNECTORS.has(lower)) {
        return lower;
      }
      return lower.charAt(0).toUpperCase() + lower.slice(1);
    })
    .join(" ");
}

const formattedSectionAnchor = computed(() =>
  formatAnchorSlug(knowledgeStore.activeSectionAnchor)
);

const activeScopeSummary = computed(() => {
  const scope = knowledgeStore.activeRouteScope || {};
  const parts = [];
  if (scope.study_id) parts.push(`Study ${scope.study_id}`);
  if (scope.site_id) parts.push(`Site ${scope.site_id}`);
  return parts.length > 0
    ? parts.join(" · ")
    : "All authorized studies & sites";
});

function formatTicketScope(studyId, siteId) {
  if (studyId && siteId) return `${studyId} / ${siteId}`;
  if (studyId) return studyId;
  if (siteId) return siteId;
  return "Global";
}

function formatTimestamp(isoString) {
  if (!isoString) return "Pending";
  const parsed = new Date(isoString);
  if (Number.isNaN(parsed.getTime())) return String(isoString);
  return parsed.toISOString().replace("T", " ").slice(0, 16) + " UTC";
}

function scrollToSectionAnchorTarget() {
  const anchorId = normalizedSectionAnchorId.value;
  const container = sopBodyRef.value;
  if (!anchorId || !container) return false;

  const elementsWithId = container.querySelectorAll("[id]");
  let targetEl = null;
  for (const el of elementsWithId) {
    el.classList.remove("spotlight-target-heading");
    if (el.id === anchorId) {
      targetEl = el;
    }
  }

  if (!targetEl) return false;
  targetEl.classList.add("spotlight-target-heading");
  if (typeof targetEl.scrollIntoView === "function") {
    targetEl.scrollIntoView({ behavior: "smooth", block: "start" });
  }
  return true;
}

function resolveActiveRouteLocation(r = route) {
  if (!r) return "";
  if (typeof r.fullPath === "string" && r.fullPath.trim()) {
    return r.fullPath.trim();
  }
  if (typeof r.path === "string" && r.path.trim()) {
    return r.path.trim();
  }
  return "";
}

async function handleJumpToSectionAnchor() {
  if (!knowledgeStore.isArticleBodyExpanded) {
    knowledgeStore.isArticleBodyExpanded = true;
    await nextTick();
    return;
  }
  scrollToSectionAnchorTarget();
}

function switchTab(tab) {
  knowledgeStore.setActiveDrawerTab(tab);
  if (tab === "tickets" && !knowledgeStore.selectedTicket) {
    knowledgeStore.fetchDrawerTickets();
  }
}

async function handleRefreshDrawerTickets() {
  if (knowledgeStore.selectedTicketId) {
    await knowledgeStore.selectDrawerTicket(knowledgeStore.selectedTicketId);
  }
  await knowledgeStore.fetchDrawerTickets();
}

async function handleSelectDrawerTicket(ticket) {
  await knowledgeStore.selectDrawerTicket(ticket);
}

async function handleBackToTicketList() {
  knowledgeStore.clearSelectedDrawerTicket();
  if (!knowledgeStore.hasLoadedDrawerTickets) {
    await knowledgeStore.fetchDrawerTickets();
  }
}

function formatTags(tags) {
  if (!tags) return [];
  if (Array.isArray(tags)) return tags;
  if (typeof tags === "string") {
    try {
      const parsed = JSON.parse(tags);
      if (Array.isArray(parsed)) return parsed;
    } catch {
      return tags
        .split(",")
        .map((t) => t.trim())
        .filter(Boolean);
    }
  }
  return [];
}

async function toggleSearch() {
  isSearchOpen.value = !isSearchOpen.value;
  if (isSearchOpen.value) {
    searchInput.value = "";
    await nextTick();
    searchInputRef.value?.focus?.();
  }
}

async function openSearchWithFallback() {
  isSearchOpen.value = true;
  searchInput.value = "";
  await nextTick();
  searchInputRef.value?.focus?.();
}

async function clearSearch() {
  searchInput.value = "";
  knowledgeStore.search("");
  await nextTick();
  searchInputRef.value?.focus?.();
}

function onSearchInput() {
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
  searchDebounceTimer = setTimeout(() => {
    knowledgeStore.search(searchInput.value);
  }, 250);
}

function selectSearchResult(item) {
  knowledgeStore.selectArticle(item);
}

async function selectRelatedArticle(item) {
  knowledgeStore.selectArticle(item);
  await nextTick();
  backToPrimaryBtnRef.value?.focus?.();
}

async function returnToPrimarySop() {
  knowledgeStore.backToPrimary();
  await nextTick();
  const firstRelated = drawerRootRef.value?.querySelector?.(".related-card");
  if (firstRelated && typeof firstRelated.focus === "function") {
    firstRelated.focus();
  } else {
    toggleBodyBtnRef.value?.focus?.();
  }
}

async function backFromSearchSelection() {
  knowledgeStore.backToPrimary();
  await nextTick();
  searchInputRef.value?.focus?.();
}

function handleEscalate() {
  if (isReadOnlyAuditor.value) {
    return;
  }
  const hasRouteScope =
    route?.path &&
    canonicalRoute.value === knowledgeStore.currentRoute &&
    route.query &&
    Object.keys(route.query).length > 0;
  knowledgeStore.prepareEscalation({
    route: resolveEscalationRoute(),
    query: hasRouteScope ? route.query : undefined,
  });
}

async function handleRetry() {
  await knowledgeStore.retryLastAction();
}

function handleTicketCreated(createdTicket) {
  if (!createdTicket) {
    knowledgeStore.cancelEscalation();
    return;
  }

  knowledgeStore.openCreatedTicketInDrawer(createdTicket);

  const refId = createdTicket.reference || createdTicket.id;
  const queueSuffix = createdTicket.assigned_queue
    ? ` (${createdTicket.assigned_queue})`
    : "";
  const simulated = isDemoBuild() || String(refId || "").startsWith("SIM-");
  toastStore.push({
    tone: "success",
    title: simulated
      ? `Simulated ticket ${refId} saved in demo session`
      : `Support ticket ${refId} submitted`,
    message: `Reference ID: ${refId}${queueSuffix}`,
    action:
      refId && isScreenAvailable("tickets")
        ? {
            label: `Open ${refId}`,
            to: `/tickets?ref=${encodeURIComponent(refId)}`,
          }
        : null,
  });
}

// Watch canonical route path + Clinical Canvas stage/section/zoom queries
watch(canonicalRoute, (newRoute) => {
  if (newRoute && newRoute !== knowledgeStore.currentRoute) {
    searchInput.value = "";
    isSearchOpen.value = false;
    knowledgeStore.fetchContextualHelp(
      resolveActiveRouteLocation(route) || newRoute,
      authStore.currentPersona
    );
    if (
      knowledgeStore.isOpen &&
      knowledgeStore.activeDrawerTab === "tickets" &&
      !knowledgeStore.selectedTicket
    ) {
      knowledgeStore.fetchDrawerTickets();
    }
  }
});

// Keep raw route scope synchronized when non-canonical query params (e.g. studyId/siteId) change on the same path
watch(
  () => resolveActiveRouteLocation(route),
  (newFullRoute) => {
    if (
      newFullRoute &&
      canonicalRoute.value === knowledgeStore.currentRoute &&
      newFullRoute !== knowledgeStore.rawRoute
    ) {
      knowledgeStore.rawRoute = newFullRoute;
      if (
        knowledgeStore.isOpen &&
        knowledgeStore.activeDrawerTab === "tickets" &&
        !knowledgeStore.selectedTicket
      ) {
        knowledgeStore.fetchDrawerTickets();
      }
    }
  }
);

// Watch persona changes to automatically refresh contextual help
watch(
  () => authStore.currentPersona,
  (newPersona) => {
    if (newPersona && newPersona !== knowledgeStore.activePersona) {
      if (isReadOnlyAuditor.value && knowledgeStore.isEscalating) {
        knowledgeStore.cancelEscalation();
      }
      searchInput.value = "";
      isSearchOpen.value = false;
      knowledgeStore.fetchContextualHelp(
        resolveActiveRouteLocation(route) ||
          canonicalRoute.value ||
          knowledgeStore.currentRoute,
        newPersona
      );
      if (
        knowledgeStore.isOpen &&
        knowledgeStore.activeDrawerTab === "tickets" &&
        !knowledgeStore.selectedTicket
      ) {
        knowledgeStore.fetchDrawerTickets();
      }
    }
  }
);

// Auto-load scoped tickets when the drawer opens on the Support Tickets tab
watch(
  () => [knowledgeStore.isOpen, knowledgeStore.activeDrawerTab],
  ([isOpen, tab]) => {
    if (
      isOpen &&
      tab === "tickets" &&
      !knowledgeStore.selectedTicket &&
      !knowledgeStore.isTicketsLoading &&
      !knowledgeStore.hasLoadedDrawerTickets
    ) {
      knowledgeStore.fetchDrawerTickets();
    }
  },
  { immediate: true }
);

// Scroll matching HTML heading id into view when SOP guidance and spotlight anchor resolve
watch(
  () => [
    knowledgeStore.isOpen,
    knowledgeStore.activeDrawerTab,
    knowledgeStore.displayedArticle?.id,
    knowledgeStore.activeSectionAnchor,
    knowledgeStore.isArticleBodyExpanded,
  ],
  async ([isOpen, tab, articleId, anchor, expanded]) => {
    if (isOpen && tab === "guidance" && articleId && anchor && expanded) {
      await nextTick();
      scrollToSectionAnchorTarget();
    }
  },
  { immediate: true }
);

function isEditableOrFormControl(node) {
  const el =
    node instanceof HTMLElement
      ? node
      : typeof document !== "undefined" &&
          document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;
  if (!el) return false;
  if (el.isContentEditable) return true;
  const tag = el.tagName?.toUpperCase?.() || "";
  if (["INPUT", "TEXTAREA", "SELECT", "OPTION"].includes(tag)) return true;
  if (
    typeof el.closest === "function" &&
    el.closest(
      '[contenteditable="true"], [contenteditable=""], [contenteditable="plaintext-only"]'
    )
  ) {
    return true;
  }
  const role = (el.getAttribute?.("role") || "").toLowerCase();
  return ["textbox", "searchbox", "combobox", "listbox", "spinbutton"].includes(
    role
  );
}

function hasActiveExternalModalTrap() {
  if (knowledgeStore.isEscalating) return true;
  if (hasActiveModalFocusTrap({ excludeId: "contextual-help-drawer" })) {
    return true;
  }
  let ancestor = drawerRootRef.value;
  while (ancestor && ancestor !== document.body) {
    if (
      ancestor.inert ||
      ancestor.hasAttribute?.("inert") ||
      ancestor.getAttribute?.("aria-hidden") === "true"
    ) {
      return true;
    }
    ancestor = ancestor.parentElement;
  }
  if (typeof document === "undefined") return false;
  const activeDialog = document.activeElement?.closest?.(
    '[aria-modal="true"], [role="dialog"], [role="alertdialog"]'
  );
  if (
    activeDialog &&
    activeDialog.id !== "contextual-help-drawer" &&
    !activeDialog.closest?.("#contextual-help-drawer")
  ) {
    return true;
  }
  const modalDialogs = document.querySelectorAll(
    '[aria-modal="true"], [role="alertdialog"]'
  );
  for (const dialog of modalDialogs) {
    if (dialog.id === "contextual-help-drawer") continue;
    if (dialog.closest?.("#contextual-help-drawer")) continue;
    return true;
  }
  return false;
}

// Keyboard accessibility: F1 / '?' to toggle contextual help; guarded against form controls and modal traps
function handleKeydown(e) {
  if (e.defaultPrevented) return;
  if (e.ctrlKey || e.metaKey || e.altKey) return;
  if (e.key !== "F1" && e.key !== "?") return;
  if (hasActiveExternalModalTrap()) return;
  if (isEditableOrFormControl(e.target)) return;

  e.preventDefault();
  knowledgeStore.toggleDrawer();
}

onMounted(() => {
  window.addEventListener("keydown", handleKeydown);
  const initialRoute = resolveActiveRouteLocation(route);
  if (
    initialRoute &&
    !knowledgeStore.hasLoadedContextualHelp &&
    !knowledgeStore.primaryArticle &&
    !knowledgeStore.loading
  ) {
    knowledgeStore.fetchContextualHelp(
      initialRoute || canonicalRoute.value,
      authStore.currentPersona
    );
  }
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeydown);
  if (searchDebounceTimer) clearTimeout(searchDebounceTimer);
});
</script>

<style scoped>
:deep(.drawer-overlay) {
  z-index: 1050;
}

:deep(.drawer-panel) {
  font-family: var(
    --font,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif
  );
  color: var(--primary);
}

.drawer-header {
  padding: 8px 16px;
  min-height: 48px;
  border-bottom: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  box-sizing: border-box;
}

.header-title-group {
  display: flex;
  gap: 8px;
  align-items: center;
  min-width: 0;
  flex: 1;
}

.header-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  color: var(--accent);
}

.drawer-title {
  margin: 0;
  font-size: 0.98rem;
  font-weight: 700;
  color: var(--primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.context-badges {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.context-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
  max-width: 100%;
  word-break: break-word;
}

.route-badge {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  word-break: break-all;
}

.persona-badge {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-icon-toggle,
.btn-close {
  min-width: 44px;
  min-height: 44px;
  font-size: 0.95rem;
}

.btn-icon-toggle.active {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.drawer-tabs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
  background-color: var(--semantic-color-surface-header);
  padding: 3px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border-control);
}

.drawer-tab-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 38px;
  min-width: 0;
  padding: 6px 10px;
  border: none;
  border-radius: 4px;
  background: transparent;
  color: var(--semantic-color-text-secondary);
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
}

.drawer-tab-btn.active {
  background-color: var(--semantic-color-surface);
  color: var(--primary);
  box-shadow: var(--semantic-shadow-sm);
}

.tab-count-pill {
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  font-variant-numeric: tabular-nums;
  font-size: 0.7rem;
  padding: 1px 5px;
  border-radius: 999px;
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.search-bar-row {
  position: relative;
  display: flex;
  align-items: center;
}

.search-input {
  width: 100%;
  max-width: 100%;
  min-height: 44px;
  padding: 8px 44px 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 1rem;
  background-color: var(--semantic-color-surface);
  color: var(--primary);
  box-sizing: border-box;
}

.search-input:focus-visible {
  border-color: var(--accent);
}

.btn-clear-search {
  position: absolute;
  right: 4px;
  min-width: 36px;
  min-height: 36px;
}

:deep(.drawer-body) {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  background-color: var(--semantic-color-surface);
}

.drawer-tabpanel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.loading-state,
.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 36px 16px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  gap: 10px;
}

.empty-icon {
  color: var(--semantic-color-text-secondary);
}

.empty-state h4 {
  margin: 0;
  color: var(--primary);
  font-size: 0.98rem;
}

.empty-state p {
  margin: 0;
  font-size: 0.82rem;
  line-height: 1.4;
}

.empty-actions {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
}

.spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--semantic-color-border);
  border-top-color: var(--accent);
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.guidance-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.back-bar {
  margin-bottom: -6px;
}

.btn-back {
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  padding: 0;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.btn-back:hover {
  text-decoration: underline;
}

.sop-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  background-color: var(--semantic-color-surface);
  box-shadow: var(--semantic-shadow-sm);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.sop-meta-header {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.category-badge {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
}

.badge-version {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-status {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-priority,
.badge-severity {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border);
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.sop-title {
  margin: 0;
  font-size: 1.08rem;
  font-weight: 700;
  color: var(--primary);
  line-height: 1.35;
}

.tags-row {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.tag-pill {
  font-size: 0.7rem;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
  padding: 1px 5px;
  border-radius: 3px;
}

.tag-pill-sm {
  font-size: 0.65rem;
  padding: 1px 4px;
}

.anchor-callout {
  background-color: var(--semantic-color-warning-bg-badge);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.78rem;
  color: var(--semantic-color-warning-dark);
}

.anchor-icon {
  flex-shrink: 0;
}

.anchor-text {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.anchor-jump-btn {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-warning-border);
  color: var(--semantic-color-warning-dark);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  text-align: left;
}

.anchor-jump-btn:hover {
  text-decoration: underline;
}

.body-toggle-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 6px;
  border-top: 1px solid var(--semantic-color-surface-header);
}

.body-toggle-label {
  font-size: 0.76rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.btn-toggle-body {
  background: transparent;
  border: none;
  color: var(--accent);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
  padding: 2px 4px;
}

.btn-toggle-body:hover {
  text-decoration: underline;
}

.sop-body {
  font-size: 0.86rem;
  line-height: 1.55;
  color: var(--semantic-color-text-body);
  border-top: 1px dashed var(--semantic-color-border);
  padding-top: 10px;
}

.sop-body :deep(h2),
.sop-body :deep(h3),
.sop-body :deep(h4) {
  color: var(--primary);
  margin: 12px 0 6px 0;
  font-weight: 700;
  scroll-margin-top: 12px;
}

.sop-body :deep(.spotlight-target-heading) {
  background-color: var(--semantic-color-warning-bg-badge);
  border-left: 3px solid var(--semantic-color-warning-border);
  padding: 2px 6px;
  border-radius: 3px;
}

.sop-body :deep(h2) {
  font-size: 0.98rem;
}

.sop-body :deep(h3) {
  font-size: 0.9rem;
}

.sop-body :deep(p) {
  margin: 0 0 8px 0;
}

.sop-body :deep(ul),
.sop-body :deep(ol) {
  padding-left: 18px;
  margin: 0 0 8px 0;
}

.sop-body :deep(li) {
  margin-bottom: 4px;
}

.sop-body :deep(code) {
  background-color: var(--semantic-color-surface-header);
  padding: 2px 4px;
  border-radius: 3px;
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  font-size: 0.8rem;
}

.markdown-pre {
  white-space: pre-wrap;
  font-family: inherit;
}

.related-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.related-title {
  margin: 0;
  font-size: 0.84rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.02em;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.related-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.related-card {
  width: 100%;
  text-align: left;
  font-family: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
  background-color: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.related-card:hover,
.related-card:focus-visible {
  background-color: var(--semantic-color-surface);
  border-color: var(--accent);
  box-shadow: var(--semantic-shadow-sm);
}

.related-card:focus-visible,
.search-result-card:focus-visible,
.drawer-ticket-card:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.related-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.related-card-title {
  font-size: 0.84rem;
  font-weight: 600;
  color: var(--primary);
}

.badge-version-sm {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  font-size: 0.68rem;
  font-weight: 700;
  padding: 1px 5px;
  border-radius: 3px;
}

.search-results-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.section-subhead {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.section-subhead h4 {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
}

.btn-link {
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.78rem;
  cursor: pointer;
  padding: 0;
}

.search-items-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.search-result-card {
  width: 100%;
  text-align: left;
  font-family: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
  background-color: var(--semantic-color-surface);
  cursor: pointer;
  transition: all 0.15s;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.search-result-card:hover {
  border-color: var(--accent);
  background-color: var(--semantic-color-surface-muted);
}

.search-result-card.active {
  border-color: var(--accent);
  background-color: var(--semantic-color-indigo-bg);
}

.result-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 6px;
}

.result-title {
  margin: 0;
  font-size: 0.86rem;
  font-weight: 600;
  color: var(--primary);
}

.empty-search {
  padding: 20px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  font-size: 0.84rem;
}

/* Inline Support Tickets Panel */
.tickets-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--semantic-color-border);
}

.tickets-scope-summary {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 0.76rem;
  color: var(--semantic-color-text-secondary);
}

.tickets-scope-label {
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.tickets-scope-value {
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  color: var(--primary);
}

.btn-compact {
  padding: 5px 10px;
  font-size: 0.76rem;
}

.tickets-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.drawer-ticket-card {
  width: 100%;
  text-align: left;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 12px;
  background-color: var(--semantic-color-surface);
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 6px;
  transition: all 0.15s ease;
}

.drawer-ticket-card:hover {
  border-color: var(--accent);
  background-color: var(--semantic-color-surface-muted);
}

.drawer-ticket-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
}

.drawer-ticket-badges {
  display: flex;
  align-items: center;
  gap: 4px;
}

.ticket-ref-code {
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--semantic-color-indigo-text);
  background-color: var(--semantic-color-indigo-bg);
  padding: 2px 6px;
  border-radius: 4px;
}

.drawer-ticket-title {
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--primary);
  line-height: 1.35;
}

.drawer-ticket-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  font-size: 0.74rem;
  color: var(--semantic-color-text-secondary);
}

.ticket-meta-sep {
  color: var(--semantic-color-text-secondary);
}

.ticket-detail-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 14px;
  background-color: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.ticket-detail-nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.detail-sync-indicator {
  font-size: 0.74rem;
  color: var(--semantic-color-text-secondary);
}

.ticket-detail-header {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.ticket-detail-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--primary);
  line-height: 1.35;
}

.ticket-detail-description {
  margin: 0;
  font-size: 0.84rem;
  line-height: 1.5;
  color: var(--semantic-color-text-body);
  white-space: pre-wrap;
}

.ticket-meta-grid {
  margin: 0;
  padding-top: 10px;
  border-top: 1px dashed var(--semantic-color-border);
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.ticket-meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.ticket-meta-item dt {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--semantic-color-text-secondary);
}

.ticket-meta-item dd {
  margin: 0;
  font-size: 0.78rem;
  color: var(--primary);
  display: inline-flex;
  align-items: center;
  gap: 4px;
  word-break: break-word;
}

.mono-val {
  font-family: var(--font-mono, "JetBrains Mono", monospace);
  font-variant-numeric: tabular-nums;
}

.help-error-alert {
  padding: 16px;
  border: 1px solid var(--semantic-color-error);
  border-radius: 8px;
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-deep);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.error-alert-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.error-alert-title {
  margin: 0;
  font-size: 0.92rem;
  font-weight: 700;
  color: var(--semantic-color-error-text-deep);
}

.error-alert-message {
  margin: 0;
  font-size: 0.84rem;
  line-height: 1.45;
  color: var(--semantic-color-error-text-dark);
}

.error-alert-actions {
  display: flex;
  justify-content: flex-start;
}

.btn-retry {
  border-color: var(--semantic-color-error-dark);
  color: var(--semantic-color-error-text-deep);
}

:deep(.clinical-drawer-footer) {
  padding: 0;
  border-top: none;
}

.drawer-footer {
  width: 100%;
  padding: 10px 16px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 8px;
  box-sizing: border-box;
}

.drawer-footer-primary {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  flex: 1 1 200px;
  min-width: 0;
}

.auditor-readonly-note {
  margin: 0;
  font-size: 0.76rem;
  line-height: 1.4;
  color: var(--semantic-color-text-secondary);
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 8px;
}

.drawer-hub-links {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.btn-hub-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-height: var(--semantic-control-target-min, 24px);
  background: none;
  border: none;
  padding: 2px 4px;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-hub-link:hover {
  text-decoration: underline;
}

.hub-link-sep {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}

.btn {
  min-height: 44px;
  padding: 8px 14px;
  font-size: 0.84rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  box-sizing: border-box;
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header);
}

.btn-escalate {
  background-color: var(--primary);
  color: var(--semantic-color-text-on-primary);
  border: 1px solid var(--primary);
}

.btn-escalate:hover:not(:disabled) {
  background-color: var(--primary-dark);
  border-color: var(--primary-dark);
}

.btn-escalate:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.btn-icon {
  flex-shrink: 0;
}

@media (max-width: 480px) {
  .drawer-tabs,
  .ticket-meta-grid {
    grid-template-columns: 1fr;
  }

  .drawer-tab-btn {
    min-height: 44px;
  }

  .tickets-toolbar {
    flex-wrap: wrap;
  }
}
</style>
