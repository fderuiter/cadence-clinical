<template>
  <div id="section-tickets-hub" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R3)</span>
          <span class="badge badge-standard">ICH Q9(R1) Quality Risk</span>
          <span class="badge badge-standard">GxP Audited Trail</span>
        </div>
        <h2 class="view-title">Clinical Issue &amp; Operations Hub</h2>
        <p class="view-subtitle">
          Unified eClinical operations desk synthesizing protocol deviations,
          site queries, data discrepancies, supply excursions, and CAPA linkages
          with multi-tier clinical SLA timers, 21 CFR Part 11 electronic
          signatures, and dual-visibility discussion streams.
        </p>
      </div>

      <div class="header-action-stack">
        <div class="header-action-group">
          <TicketsDemoWorkspace
            v-if="demoBuild"
            mode="header-actions"
            @export="exportAuditTrail"
          />
          <button
            id="btn-create-ticket"
            type="button"
            class="btn btn-primary"
            :disabled="!canMutateTicket"
            :aria-disabled="!canMutateTicket ? 'true' : undefined"
            :aria-describedby="
              !canMutateTicket ? 'tickets-auditor-readonly-note' : undefined
            "
            :title="
              !canMutateTicket
                ? auditorReadOnlyExplanation
                : 'Log New Clinical Issue'
            "
            @click="openCreateTicketModal"
          >
            <svg
              v-if="!canMutateTicket"
              xmlns="http://www.w3.org/2000/svg"
              width="15"
              height="15"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              class="btn-icon-svg"
              aria-hidden="true"
            >
              <rect width="18" height="11" x="3" y="11" rx="2" ry="2" />
              <path d="M7 11V7a5 5 0 0 1 10 0v4" />
            </svg>
            <svg
              v-else
              xmlns="http://www.w3.org/2000/svg"
              width="15"
              height="15"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              class="btn-icon-svg"
              aria-hidden="true"
            >
              <path d="M5 12h14" />
              <path d="M12 5v14" />
            </svg>
            <span>Log Issue</span>
          </button>
        </div>
        <p
          v-if="!canMutateTicket"
          id="tickets-auditor-readonly-note"
          class="auditor-readonly-note"
        >
          {{ auditorReadOnlyExplanation }}
        </p>
      </div>
    </div>

    <!-- KRI Metric Cards Grid -->
    <div class="stats-grid">
      <button
        type="button"
        class="stat-card stat-card-btn"
        @click="filterStatus = 'ALL'"
      >
        <span class="stat-icon alert-icon" aria-hidden="true">🎫</span>
        <span class="stat-details">
          <span class="stat-value tabular-nums">
            {{ activeTicketsCount }}
          </span>
          <span class="stat-label">Active Clinical Issues</span>
        </span>
      </button>
      <button
        type="button"
        class="stat-card stat-card-btn"
        @click="filterSeverity = 'CRITICAL'"
      >
        <span class="stat-icon capa-icon" aria-hidden="true">🚨</span>
        <span class="stat-details">
          <span class="stat-value text-danger tabular-nums">
            {{ criticalTicketsCount }}
          </span>
          <span class="stat-label">Critical GxP Deviations</span>
        </span>
      </button>
      <div class="stat-card">
        <div class="stat-icon rbqm-icon">⏱</div>
        <div class="stat-details">
          <div
            id="kpi-sla-compliance"
            class="stat-value tabular-nums"
            :class="slaComplianceClass"
          >
            {{ slaComplianceDisplay }}
          </div>
          <div class="stat-label">SLA Target Compliance</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon qtl-icon">⚡</div>
        <div class="stat-details">
          <div id="kpi-mttr" class="stat-value tabular-nums">
            {{ mttrDisplay }}
          </div>
          <div class="stat-label">Mean Resolution Time (MTTR)</div>
        </div>
      </div>
      <button
        type="button"
        class="stat-card stat-card-btn"
        @click="filterStatus = 'WAITING_ON_SITE'"
      >
        <span class="stat-icon breach-icon" aria-hidden="true">⏸</span>
        <span class="stat-details">
          <span class="stat-value tabular-nums">
            {{ pausedTicketsCount }}
          </span>
          <span class="stat-label">SLA Paused (Waiting Site)</span>
        </span>
      </button>
    </div>

    <!-- Filter & View Mode Control Bar -->
    <div class="controls-card">
      <div class="search-box">
        <span class="search-icon" aria-hidden="true">🔍</span>
        <ClinicalInput
          id="tickets-search-input"
          v-model="searchQuery"
          label="Search clinical issues"
          hide-label
          density="compact"
          :show-query-flag="false"
          extra-class="search-field"
          control-class="search-input"
          placeholder="Search by ref #, title, subject ID, or site..."
        />
      </div>

      <div class="filter-group">
        <ClinicalInput
          id="tickets-filter-category"
          v-model="filterCategory"
          as="select"
          label="Filter by category"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select"
        >
          <option value="ALL">All Categories</option>
          <option value="PROTOCOL_DEVIATION">Protocol Deviation</option>
          <option value="DATA_QUERY">Data Query / Discrepancy</option>
          <option value="SAFETY_ADVERSE_EVENT">Safety &amp; SAE</option>
          <option value="SUPPLY_EXCURSION">Supply &amp; Temperature</option>
          <option value="SITE_OPERATIONS">Site Operations</option>
          <option value="MONITORING_FINDING">Monitoring Finding</option>
          <option value="SYSTEM_SUPPORT">System Support</option>
          <option value="ACCESS_CONTROL">Access Control</option>
          <option value="REGULATORY_QUERY">Regulatory Query</option>
          <option value="TECHNICAL_SYSTEM">Technical / Bug</option>
        </ClinicalInput>

        <ClinicalInput
          id="tickets-filter-severity"
          v-model="filterSeverity"
          as="select"
          label="Filter by GxP severity"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select"
        >
          <option value="ALL">All Severities</option>
          <option value="CRITICAL">Critical</option>
          <option value="MAJOR">Major</option>
          <option value="MINOR">Minor</option>
        </ClinicalInput>

        <ClinicalInput
          id="tickets-filter-status"
          v-model="filterStatus"
          as="select"
          label="Filter by workflow status"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select"
        >
          <option value="ALL">All Statuses</option>
          <option value="OPEN">Open</option>
          <option value="IN_PROGRESS">In Progress</option>
          <option value="WAITING_ON_SITE">Waiting on Site</option>
          <option value="WAITING_ON_SPONSOR">Waiting on Sponsor</option>
          <option value="RESOLVED">Resolved</option>
          <option value="CLOSED">Closed</option>
        </ClinicalInput>

        <ClinicalInput
          id="tickets-study-filter"
          v-model="filterStudyId"
          label="Filter issues by study"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select scope-filter-input"
          placeholder="Study (all)"
          :attributes="{ 'aria-label': 'Filter issues by study' }"
          @change="applyTicketScope"
        />
        <ClinicalInput
          id="tickets-site-filter"
          v-model="filterSiteId"
          label="Filter issues by site"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select scope-filter-input"
          placeholder="Site (all)"
          :attributes="{ 'aria-label': 'Filter issues by site' }"
          @change="applyTicketScope"
        />
        <ClinicalInput
          id="tickets-subject-filter"
          v-model="filterSubjectId"
          label="Filter issues by participant"
          hide-label
          density="compact"
          :show-query-flag="false"
          control-class="filter-select scope-filter-input"
          placeholder="Participant (all)"
          :attributes="{ 'aria-label': 'Filter issues by participant' }"
        />
      </div>

      <ClinicalTabs
        v-model="viewMode"
        :tabs="viewModeTabs"
        variant="segmented"
        aria-label="Tickets workspace view mode"
        id-prefix="tickets-view"
        list-class="view-mode-toggle"
        tab-class="view-toggle-btn"
      />
    </div>

    <!-- Bulk Action Toolbar (Demo mode only) -->
    <TicketsDemoWorkspace
      v-if="demoBuild && canMutateTicket"
      v-model:batch-status="batchStatus"
      v-model:batch-severity="batchSeverity"
      mode="bulk-bar"
      :selected-ticket-count="selectedTicketCount"
      @batch-transition="handleBatchTransition"
      @batch-severity="handleBatchSeverityUpdate"
      @clear-selection="clearSelection"
    />

    <!-- MAIN WORKSPACE: KANBAN VIEW -->
    <div
      v-if="viewMode === 'kanban'"
      id="tickets-view-panel-kanban"
      role="tabpanel"
      aria-labelledby="tickets-view-tab-kanban"
      class="kanban-board"
      tabindex="0"
    >
      <!-- Column 1: OPEN -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-open" />
            <span class="column-title">Open / Triage</span>
          </div>
          <span class="column-count">{{
            getColumnTickets("OPEN").length
          }}</span>
        </div>
        <div
          class="column-cards"
          tabindex="0"
          role="region"
          aria-label="Open and triage tickets"
        >
          <button
            v-for="t in getColumnTickets('OPEN')"
            :key="t.id"
            type="button"
            class="ticket-card"
            @click="openTicketDetail(t)"
            @keydown.enter.prevent="openTicketDetail(t)"
            @keydown.space.prevent="openTicketDetail(t)"
          >
            <span class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span
                class="badge"
                :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`"
                >{{ t.gxp_severity }}</span
              >
            </span>
            <span class="card-ticket-title">
              {{ t.title }}
            </span>
            <span class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <span
                v-if="t.sla_target_at"
                class="sla-mini-badge"
                :class="getSlaClass(t)"
              >
                {{ formatSlaRemaining(t) }}
              </span>
            </span>
          </button>
        </div>
      </div>

      <!-- Column 2: IN PROGRESS -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-progress" />
            <span class="column-title">Investigation</span>
          </div>
          <span class="column-count">{{
            getColumnTickets("IN_PROGRESS").length
          }}</span>
        </div>
        <div
          class="column-cards"
          tabindex="0"
          role="region"
          aria-label="Investigation tickets"
        >
          <button
            v-for="t in getColumnTickets('IN_PROGRESS')"
            :key="t.id"
            type="button"
            class="ticket-card"
            @click="openTicketDetail(t)"
            @keydown.enter.prevent="openTicketDetail(t)"
            @keydown.space.prevent="openTicketDetail(t)"
          >
            <span class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span
                class="badge"
                :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`"
                >{{ t.gxp_severity }}</span
              >
            </span>
            <span class="card-ticket-title">
              {{ t.title }}
            </span>
            <span class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <span
                v-if="t.sla_target_at"
                class="sla-mini-badge"
                :class="getSlaClass(t)"
              >
                {{ formatSlaRemaining(t) }}
              </span>
            </span>
          </button>
        </div>
      </div>

      <!-- Column 3: PAUSED (WAITING SITE/SPONSOR) -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-paused" />
            <span class="column-title">Awaiting Action</span>
          </div>
          <span class="column-count">{{
            getColumnTickets([
              "WAITING_ON_SITE",
              "WAITING_ON_SPONSOR",
              "PENDING_REGULATORY_REVIEW",
            ]).length
          }}</span>
        </div>
        <div
          class="column-cards"
          tabindex="0"
          role="region"
          aria-label="Awaiting action tickets"
        >
          <button
            v-for="t in getColumnTickets([
              'WAITING_ON_SITE',
              'WAITING_ON_SPONSOR',
              'PENDING_REGULATORY_REVIEW',
            ])"
            :key="t.id"
            type="button"
            class="ticket-card paused-card"
            @click="openTicketDetail(t)"
            @keydown.enter.prevent="openTicketDetail(t)"
            @keydown.space.prevent="openTicketDetail(t)"
          >
            <span class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span class="badge badge-paused">{{
                formatStatus(t.status)
              }}</span>
            </span>
            <span class="card-ticket-title">
              {{ t.title }}
            </span>
            <span class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <span class="sla-mini-badge fill-paused">⏸ SLA Paused</span>
            </span>
          </button>
        </div>
      </div>

      <!-- Column 4: RESOLVED -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-resolved" />
            <span class="column-title">Resolved</span>
          </div>
          <span class="column-count">{{
            getColumnTickets("RESOLVED").length
          }}</span>
        </div>
        <div
          class="column-cards"
          tabindex="0"
          role="region"
          aria-label="Resolved tickets"
        >
          <button
            v-for="t in getColumnTickets('RESOLVED')"
            :key="t.id"
            type="button"
            class="ticket-card resolved-card"
            @click="openTicketDetail(t)"
            @keydown.enter.prevent="openTicketDetail(t)"
            @keydown.space.prevent="openTicketDetail(t)"
          >
            <span class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span v-if="t.signature_token" class="badge badge-signed"
                >✍️ eSigned</span
              >
              <span v-else class="badge badge-pending-sign"
                >Pending Sign-off</span
              >
            </span>
            <span class="card-ticket-title">
              {{ t.title }}
            </span>
            <span class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <span class="rca-mini-tag">{{
                t.resolution_code || "Resolved"
              }}</span>
            </span>
          </button>
        </div>
      </div>

      <!-- Column 5: CLOSED -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-closed" />
            <span class="column-title">Closed &amp; Archived</span>
          </div>
          <span class="column-count">{{
            getColumnTickets("CLOSED").length
          }}</span>
        </div>
        <div
          class="column-cards"
          tabindex="0"
          role="region"
          aria-label="Closed and archived tickets"
        >
          <button
            v-for="t in getColumnTickets('CLOSED')"
            :key="t.id"
            type="button"
            class="ticket-card closed-card"
            @click="openTicketDetail(t)"
            @keydown.enter.prevent="openTicketDetail(t)"
            @keydown.space.prevent="openTicketDetail(t)"
          >
            <span class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span class="badge badge-closed">🔒 Closed</span>
            </span>
            <span class="card-ticket-title">
              {{ t.title }}
            </span>
            <span class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
            </span>
          </button>
        </div>
      </div>
    </div>

    <!-- MAIN WORKSPACE: TABLE VIEW -->
    <div
      v-else
      id="tickets-view-panel-table"
      role="tabpanel"
      aria-labelledby="tickets-view-tab-table"
      class="table-card"
    >
      <ClinicalDataTable
        title="Clinical Issue & Operations Table"
        hide-header
        plain
        :columns="ticketTableColumns"
        :data="filteredTickets"
        row-key="id"
        :page-size="Math.max(filteredTickets.length, 50)"
        table-class="clinical-table"
        :row-class="resolveTicketRowClass"
        @row-click="openTicketDetail"
      >
        <template #head-select>
          <input
            type="checkbox"
            class="ticket-checkbox"
            aria-label="Select all visible tickets"
            :checked="isAllSelected"
            :indeterminate="isSomeSelected"
            @change="toggleSelectAll"
          />
        </template>

        <template #cell-select="{ row }">
          <input
            type="checkbox"
            class="ticket-checkbox"
            :aria-label="'Select ticket ' + (row.reference || row.id)"
            :checked="selectedTicketIds.has(row.id)"
            @click.stop
            @change="toggleSelectTicket(row.id)"
          />
        </template>

        <template #cell-reference="{ row }">
          {{ row.reference || `#${row.id?.substring(0, 8)}` }}
        </template>

        <template #cell-title="{ row }">
          {{ row.title }}
        </template>

        <template #cell-category="{ row }">
          <span class="category-pill">{{ formatCategory(row.category) }}</span>
        </template>

        <template #cell-gxp_severity="{ row }">
          <span
            class="badge"
            :class="`badge-sev-${row.gxp_severity?.toLowerCase()}`"
            >{{ row.gxp_severity }}</span
          >
        </template>

        <template #cell-status="{ row }">
          <span
            class="badge"
            :class="`badge-status-${row.status?.toLowerCase()}`"
            >{{ formatStatus(row.status) }}</span
          >
        </template>

        <template #cell-context="{ row }">
          <span v-if="row.site_id">{{ row.site_id }}</span>
          <span v-if="row.subject_id"> / {{ row.subject_id }}</span>
          <span v-if="!row.site_id && !row.subject_id">{{
            row.study_id || "—"
          }}</span>
        </template>

        <template #cell-priority="{ row }">
          <span
            class="priority-tag"
            :class="`priority-${row.priority?.toLowerCase()}`"
            >{{ row.priority }}</span
          >
        </template>

        <template #cell-sla="{ row }">
          <div
            v-if="row.sla_target_at"
            class="sla-mini-badge"
            :class="getSlaClass(row)"
          >
            {{ formatSlaRemaining(row) }}
          </div>
          <span v-else>—</span>
        </template>

        <template #cell-signoff="{ row }">
          <span v-if="row.signature_token" class="badge badge-signed"
            >eSigned</span
          >
          <span
            v-else-if="row.status === 'RESOLVED'"
            class="badge badge-pending-sign"
            >Needs Sign</span
          >
          <span v-else>—</span>
        </template>

        <template #cell-actions="{ row }">
          <button
            type="button"
            class="btn btn-xs btn-secondary"
            @click.stop="openTicketDetail(row)"
          >
            Inspect<span class="sr-only"> {{ row.reference || row.id }}</span>
          </button>
        </template>

        <template #empty>
          No clinical issues match the selected search and filter criteria.
        </template>
      </ClinicalDataTable>
    </div>

    <!-- Ticket Detail Drawer -->
    <TicketDetailDrawer
      :is-open="isDrawerOpen"
      :ticket="selectedTicket"
      :comments="selectedTicketComments"
      :attachments="selectedTicketAttachments"
      :audit-logs="selectedTicketAuditLogs"
      :is-audit-loading="isAuditLoading"
      @close="isDrawerOpen = false"
      @transition="handleTicketTransition"
      @comment="handleTicketComment"
      @attachment="handleTicketAttachment"
      @signed="handleTicketSigned"
    />

    <!-- Ticket Create Modal -->
    <TicketCreateModal
      v-if="canMutateTicket"
      :is-open="isCreateModalOpen"
      :default-study-id="defaultCreateStudyId"
      :default-site-id="defaultCreateSiteId"
      :default-subject-id="defaultCreateSubjectId"
      @close="isCreateModalOpen = false"
      @created="handleTicketCreated"
    />
  </div>
</template>

<script setup>
import {
  ref,
  reactive,
  computed,
  watch,
  onMounted,
  getCurrentInstance,
  defineAsyncComponent,
} from "vue";
import { ClinicalDataTable, ClinicalInput, ClinicalTabs } from "ui";
import TicketDetailDrawer from "../components/tickets/TicketDetailDrawer.vue";
import TicketCreateModal from "../components/tickets/TicketCreateModal.vue";
import { ticketsApi } from "../api/ticketsApi";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import { useAuthStore } from "../stores/auth.js";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import {
  DEMO_TICKET_KPIS,
  applyDemoBatchSeverityUpdate,
  applyDemoBatchStatusTransition,
  applyDemoTicketSignature,
  applyDemoTicketTransition,
  buildDemoTicketAttachment,
  buildDemoTicketComment,
  buildDemoTicketDetailSeed,
  createInitialMockTickets,
  exportDemoAuditTrail,
  mergeDemoEscalatedTickets,
} from "../stores/demo/ticketsDemo";
import {
  sanitizePseudonymousSubjectCode,
  useKnowledgeStore,
} from "../stores/knowledge.js";
import { getSyncedStudyScope } from "../stores/studyScopeBridge";
import { useToastStore } from "../stores/toasts.js";
import {
  resolveHydratedWorkspaceScope,
  syncWorkspaceScope,
} from "../stores/workspaceScope";
import { isDemoBuild } from "../utils/demoMode.js";
import { useTicketMutationPermission } from "../utils/ticketPermissions";

const TicketsDemoWorkspace = defineAsyncComponent(
  () => import("./demo/TicketsDemoWorkspace.vue")
);

const demoBuild = isDemoBuild();
const instance = getCurrentInstance();
const { route, router } = resolveRouteAndRouter();
const authStore = useAuthStore();
const { canMutateTicket, auditorReadOnlyExplanation } =
  useTicketMutationPermission(authStore);
const scopeStore = useClinicalScopeStore();
function getEffectiveRoute() {
  return route ?? instance?.proxy?.$route ?? null;
}
const knowledgeStore = useKnowledgeStore();
const toastStore = useToastStore();
const initialRouteScope = readRouteScopeQuery(route);
const hydratedScope = resolveHydratedWorkspaceScope(getEffectiveRoute());
const tickets = ref([]);
const kpis = reactive({
  active_tickets: 0,
  sla_compliance_rate: demoBuild
    ? (DEMO_TICKET_KPIS?.sla_compliance_rate ?? 96.4)
    : null,
  mttr_hours: demoBuild ? (DEMO_TICKET_KPIS?.mttr_hours ?? 4.2) : null,
  critical_tickets: 0,
});

// UI State
const viewMode = ref("kanban"); // "kanban" | "table"
const viewModeTabs = [
  { id: "kanban", label: "Kanban" },
  { id: "table", label: "Table" },
];
const searchQuery = ref("");
const filterCategory = ref("ALL");
const filterSeverity = ref("ALL");
const filterStatus = ref("ALL");
const filterStudyId = ref(initialRouteScope.studyId || hydratedScope.studyId);
const filterSiteId = ref(initialRouteScope.siteId || hydratedScope.siteId);
const filterSubjectId = ref(initialRouteScope.subjectId);

if (filterStudyId.value || filterSiteId.value) {
  syncWorkspaceScope({
    ...(filterStudyId.value ? { studyId: filterStudyId.value } : {}),
    ...(filterSiteId.value || filterStudyId.value
      ? { siteId: filterSiteId.value }
      : {}),
    router,
    route: getEffectiveRoute(),
    syncRouteQuery:
      !demoBuild &&
      Boolean(initialRouteScope.studyId || initialRouteScope.siteId),
  });
}
if (route?.query && initialRouteScope.subjectId) {
  scopeStore.activeSubjectId = initialRouteScope.subjectId;
}

// Selection & Batch Action State
const selectedTicketIds = ref(new Set());
const batchStatus = ref("");
const batchSeverity = ref("");

const selectedTicketCount = computed(() => selectedTicketIds.value.size);

const ticketTableColumns = computed(() => [
  ...(demoBuild
    ? [
        {
          key: "select",
          label: "",
          headerClass: "checkbox-cell",
          cellClass: "checkbox-cell",
        },
      ]
    : []),
  { key: "reference", label: "Reference", cellClass: "ref-cell font-mono" },
  { key: "title", label: "Title", cellClass: "title-cell font-semibold" },
  { key: "category", label: "Category" },
  { key: "gxp_severity", label: "GxP Severity" },
  { key: "status", label: "Status" },
  {
    key: "context",
    label: "Study / Site / Subject",
    cellClass: "context-cell",
  },
  { key: "priority", label: "Priority" },
  { key: "sla", label: "SLA Status" },
  { key: "signoff", label: "Sign-Off" },
  { key: "actions", label: "Actions" },
]);

const resolveTicketRowClass = (t) => [
  "table-row",
  {
    "selected-row": demoBuild && selectedTicketIds.value.has(t.id),
  },
];

const isAllSelected = computed(() => {
  if (filteredTickets.value.length === 0) return false;
  return filteredTickets.value.every((t) => selectedTicketIds.value.has(t.id));
});

const isSomeSelected = computed(() => {
  if (isAllSelected.value) return false;
  return filteredTickets.value.some((t) => selectedTicketIds.value.has(t.id));
});

const toggleSelectAll = () => {
  const newSet = new Set(selectedTicketIds.value);
  if (isAllSelected.value) {
    filteredTickets.value.forEach((t) => newSet.delete(t.id));
  } else {
    filteredTickets.value.forEach((t) => newSet.add(t.id));
  }
  selectedTicketIds.value = newSet;
};

const toggleSelectTicket = (ticketId) => {
  const newSet = new Set(selectedTicketIds.value);
  if (newSet.has(ticketId)) {
    newSet.delete(ticketId);
  } else {
    newSet.add(ticketId);
  }
  selectedTicketIds.value = newSet;
};

const clearSelection = () => {
  selectedTicketIds.value = new Set();
  batchStatus.value = "";
  batchSeverity.value = "";
};

// Modal & Drawer State
const isDrawerOpen = ref(false);
const isCreateModalOpen = ref(false);
const isAuditLoading = ref(false);
const selectedTicket = ref({});
const selectedTicketComments = ref([]);
const selectedTicketAttachments = ref([]);
const selectedTicketAuditLogs = ref([]);

const defaultCreateStudyId = computed(
  () =>
    filterStudyId.value.trim() ||
    scopeStore.activeStudyId.trim() ||
    (getSyncedStudyScope() || "").trim()
);

const defaultCreateSiteId = computed(() => {
  if (filterSiteId.value.trim()) {
    return filterSiteId.value.trim();
  }
  const activeStudy = defaultCreateStudyId.value;
  const storeStudy = scopeStore.activeStudyId.trim();
  if (!activeStudy || !storeStudy || storeStudy === activeStudy) {
    return scopeStore.activeSiteId.trim();
  }
  return "";
});

const defaultCreateSubjectId = computed(() => {
  const candidate =
    filterSubjectId.value.trim() ||
    (!defaultCreateStudyId.value ||
    !scopeStore.activeStudyId.trim() ||
    scopeStore.activeStudyId.trim() === defaultCreateStudyId.value
      ? scopeStore.activeSubjectId.trim()
      : "");
  return sanitizePseudonymousSubjectCode(candidate);
});

const openCreateTicketModal = () => {
  if (!canMutateTicket.value) {
    return;
  }
  isCreateModalOpen.value = true;
};

watch(canMutateTicket, (allowed) => {
  if (!allowed) {
    isCreateModalOpen.value = false;
    clearSelection();
  }
});

function getRouteRefParam() {
  const activeRoute = getEffectiveRoute();
  const raw =
    activeRoute?.query?.ref ??
    (typeof window !== "undefined"
      ? new URLSearchParams(window.location.search).get("ref")
      : null);
  if (Array.isArray(raw)) return String(raw[0] || "").trim();
  return typeof raw === "string" ? raw.trim() : "";
}

async function openDeepLinkedTicketIfRequested() {
  const targetRef = getRouteRefParam();
  if (!targetRef) return;
  const existing = tickets.value.find(
    (t) => t.reference === targetRef || t.id === targetRef
  );
  if (existing) {
    await openTicketDetail(existing);
    return;
  }
  if (!demoBuild) {
    try {
      const fetched = await ticketsApi.getTicket(targetRef);
      if (fetched) {
        upsertTicketInList(fetched);
        await openTicketDetail(fetched);
      }
    } catch (err) {
      toastStore.apiError(`Loading ticket ${targetRef} failed`, err);
    }
  }
}

function upsertTicketInList(updated) {
  if (!updated) return;
  const idx = tickets.value.findIndex(
    (t) =>
      t.id === updated.id ||
      (updated.reference && t.reference === updated.reference)
  );
  if (idx !== -1) {
    tickets.value[idx] = { ...tickets.value[idx], ...updated };
  } else {
    tickets.value.unshift(updated);
  }
}

onMounted(async () => {
  // Sample tickets only in a demo build (#4523, #4827); elsewhere the list shows what the service returns.
  if (demoBuild) {
    tickets.value = mergeDemoEscalatedTickets(
      createInitialMockTickets(),
      knowledgeStore.escalatedTickets
    );
  }
  await fetchTicketsFromApi();
  await openDeepLinkedTicketIfRequested();
});

watch(
  () => [
    route?.query?.studyId,
    route?.query?.siteId,
    route?.query?.site,
    route?.query?.subjectId,
    route?.query?.participant,
  ],
  async () => {
    if (!route?.query) return;
    const nextScope = readRouteScopeQuery(route);
    const prevStudy = filterStudyId.value;
    const prevSite = filterSiteId.value;
    filterStudyId.value = nextScope.studyId;
    filterSiteId.value = nextScope.siteId;
    filterSubjectId.value = nextScope.subjectId;
    syncWorkspaceScope({
      studyId: nextScope.studyId,
      siteId: nextScope.siteId,
    });
    scopeStore.activeSubjectId = nextScope.subjectId;
    if (nextScope.studyId !== prevStudy || nextScope.siteId !== prevSite) {
      await fetchTicketsFromApi();
    }
  }
);

watch(
  [filterStudyId, filterSiteId, filterSubjectId],
  ([nextStudy, nextSite, nextSubject]) => {
    const trimmedStudy = nextStudy.trim();
    const trimmedSite = nextSite.trim();
    const trimmedSubject = nextSubject.trim();
    syncWorkspaceScope({
      studyId: trimmedStudy,
      siteId: trimmedSite,
    });
    scopeStore.activeSubjectId = trimmedSubject;
    void replaceRouteQueryParams(router, route, {
      studyId: trimmedStudy || null,
      siteId: trimmedSite || null,
      subjectId: trimmedSubject || null,
    });
  }
);

watch(
  () => getEffectiveRoute()?.query?.ref,
  async (nextRef, prevRef) => {
    if (nextRef && nextRef !== prevRef) {
      await openDeepLinkedTicketIfRequested();
    }
  }
);

const applyTicketScope = async () => {
  const synced = syncWorkspaceScope({
    studyId: filterStudyId.value.trim(),
    siteId: filterSiteId.value.trim(),
    router,
    route: getEffectiveRoute(),
    syncRouteQuery: true,
  });
  filterStudyId.value = synced.studyId;
  filterSiteId.value = synced.siteId;
  await fetchTicketsFromApi();
};

const fetchTicketsFromApi = async () => {
  try {
    const query = {};
    const scopedStudy = filterStudyId.value.trim();
    const scopedSite = filterSiteId.value.trim();
    if (scopedStudy) query.study_id = scopedStudy;
    if (scopedSite) query.site_id = scopedSite;
    const res =
      Object.keys(query).length > 0
        ? await ticketsApi.listTickets(query)
        : await ticketsApi.listTickets();
    if (res && Array.isArray(res)) {
      tickets.value = demoBuild
        ? mergeDemoEscalatedTickets(res, knowledgeStore.escalatedTickets)
        : res;
    }
  } catch (err) {
    if (!demoBuild) {
      toastStore.apiError("Loading tickets failed", err);
    }
  }
};

// Summary metrics
const activeTicketsCount = computed(() => {
  return tickets.value.filter((t) => t.status !== "CLOSED").length;
});

const criticalTicketsCount = computed(() => {
  return tickets.value.filter(
    (t) => t.gxp_severity === "CRITICAL" && t.status !== "CLOSED"
  ).length;
});

const pausedTicketsCount = computed(() => {
  return tickets.value.filter(
    (t) => t.status === "WAITING_ON_SITE" || t.status === "WAITING_ON_SPONSOR"
  ).length;
});

const liveSlaComplianceRate = computed(() => {
  if (demoBuild) return kpis.sla_compliance_rate ?? 96.4;
  const tracked = tickets.value.filter(
    (t) => Boolean(t.sla_target_at) || typeof t.sla_breached === "boolean"
  );
  if (tracked.length === 0) return null;
  const compliant = tracked.filter((t) => {
    if (t.sla_breached) return false;
    if (
      t.sla_target_at &&
      t.status !== "RESOLVED" &&
      t.status !== "CLOSED" &&
      new Date(t.sla_target_at).getTime() < Date.now()
    ) {
      return false;
    }
    return true;
  }).length;
  return Number(((compliant / tracked.length) * 100).toFixed(1));
});

const slaComplianceDisplay = computed(() => {
  if (liveSlaComplianceRate.value === null) return "—";
  return `${liveSlaComplianceRate.value}%`;
});

const mttrDisplay = computed(() => {
  if (demoBuild) {
    return kpis.mttr_hours ? `${kpis.mttr_hours.toFixed(1)}h` : "4.2h";
  }
  const resolved = tickets.value.filter(
    (t) =>
      (t.status === "RESOLVED" || t.status === "CLOSED") &&
      t.created_at &&
      t.updated_at
  );
  if (resolved.length === 0) return "—";
  const totalHours = resolved.reduce((acc, t) => {
    const start = new Date(t.created_at).getTime();
    const end = new Date(t.updated_at).getTime();
    const pausedMs = (t.sla_total_paused_seconds || 0) * 1000;
    const netHours = Math.max(0, end - start - pausedMs) / (3600 * 1000);
    return acc + netHours;
  }, 0);
  return `${(totalHours / resolved.length).toFixed(1)}h`;
});

const slaComplianceClass = computed(() => {
  if (liveSlaComplianceRate.value === null) return "";
  return liveSlaComplianceRate.value >= 95 ? "text-success" : "text-warning";
});

// Filtering
const filteredTickets = computed(() => {
  return tickets.value.filter((t) => {
    // Search query match
    if (searchQuery.value.trim()) {
      const q = searchQuery.value.toLowerCase();
      const match =
        t.title?.toLowerCase().includes(q) ||
        t.reference?.toLowerCase().includes(q) ||
        t.subject_id?.toLowerCase().includes(q) ||
        t.site_id?.toLowerCase().includes(q) ||
        t.study_id?.toLowerCase().includes(q);
      if (!match) return false;
    }
    // Category match
    if (filterCategory.value !== "ALL" && t.category !== filterCategory.value) {
      return false;
    }
    // Severity match
    if (
      filterSeverity.value !== "ALL" &&
      t.gxp_severity !== filterSeverity.value
    ) {
      return false;
    }
    // Status match
    if (filterStatus.value !== "ALL" && t.status !== filterStatus.value) {
      return false;
    }
    // Clinical scope match
    const scopedStudy = filterStudyId.value.trim().toLowerCase();
    if (scopedStudy && t.study_id) {
      const ticketStudy = t.study_id.trim().toLowerCase();
      const isCanonicalDemoAlias =
        demoBuild &&
        (scopedStudy === "cadence-101" || scopedStudy === "demo-01") &&
        (ticketStudy === "cadence-101" || ticketStudy === "demo-01");
      if (ticketStudy !== scopedStudy && !isCanonicalDemoAlias) {
        return false;
      }
    }
    const scopedSite = filterSiteId.value.trim().toLowerCase();
    if (scopedSite && t.site_id && t.site_id.toLowerCase() !== scopedSite) {
      return false;
    }
    const scopedSubject = filterSubjectId.value.trim().toLowerCase();
    if (
      scopedSubject &&
      (!t.subject_id || t.subject_id.toLowerCase() !== scopedSubject)
    ) {
      return false;
    }
    return true;
  });
});

const getColumnTickets = (statuses) => {
  const statusArray = Array.isArray(statuses) ? statuses : [statuses];
  return filteredTickets.value.filter((t) => statusArray.includes(t.status));
};

// SLA utilities
const formatSlaRemaining = (t) => {
  if (!t.sla_target_at) return "No SLA";
  const diff = new Date(t.sla_target_at).getTime() - Date.now();
  if (diff <= 0) return "🚨 SLA BREACH";
  const hours = Math.floor(diff / (3600 * 1000));
  if (hours < 24) return `⏱ ${hours}h left`;
  const days = Math.floor(hours / 24);
  return `⏱ ${days}d left`;
};

const getSlaClass = (t) => {
  if (t.sla_breached) return "sla-tag-breached";
  if (t.sla_amber_warned) return "sla-tag-warned";
  return "sla-tag-normal";
};

const formatCategory = (cat) => {
  return cat?.replace(/_/g, " ") || "GENERAL";
};

const formatStatus = (st) => {
  return st?.replace(/_/g, " ") || "OPEN";
};

// Modal & Drawer Handlers
const refreshLiveAuditTrail = async (ticketIdOrRef) => {
  if (demoBuild || !ticketIdOrRef) return;
  isAuditLoading.value = true;
  try {
    const logs = await ticketsApi.getAuditTrail(ticketIdOrRef);
    selectedTicketAuditLogs.value = Array.isArray(logs) ? logs : [];
  } catch (err) {
    toastStore.apiError("Loading ticket audit trail failed", err);
  } finally {
    isAuditLoading.value = false;
  }
};

const openTicketDetail = async (t) => {
  selectedTicket.value = { ...t };
  if (demoBuild) {
    const seed = buildDemoTicketDetailSeed(t, formatCategory);
    selectedTicketComments.value = seed.comments;
    selectedTicketAttachments.value = seed.attachments;
    selectedTicketAuditLogs.value = seed.auditLogs;
    isDrawerOpen.value = true;
    return;
  }

  selectedTicketComments.value = [];
  selectedTicketAttachments.value = [];
  selectedTicketAuditLogs.value = [];
  isAuditLoading.value = true;
  isDrawerOpen.value = true;

  const lookupId = t.id || t.reference;
  if (!lookupId) {
    isAuditLoading.value = false;
    return;
  }

  const [detailRes, commentsRes, auditRes] = await Promise.allSettled([
    ticketsApi.getTicket(lookupId),
    ticketsApi.listComments(lookupId),
    ticketsApi.getAuditTrail(lookupId),
  ]);

  if (detailRes.status === "fulfilled" && detailRes.value) {
    selectedTicket.value = detailRes.value;
    upsertTicketInList(detailRes.value);
  }
  if (commentsRes.status === "fulfilled" && Array.isArray(commentsRes.value)) {
    selectedTicketComments.value = commentsRes.value;
  } else if (commentsRes.status === "rejected") {
    toastStore.apiError("Loading ticket comments failed", commentsRes.reason);
  }
  if (auditRes.status === "fulfilled" && Array.isArray(auditRes.value)) {
    selectedTicketAuditLogs.value = auditRes.value;
  } else if (auditRes.status === "rejected") {
    toastStore.apiError("Loading ticket audit trail failed", auditRes.reason);
  }
  isAuditLoading.value = false;
};

const handleTicketTransition = async ({
  ticketId,
  newStatus,
  root_cause_category,
  root_cause_summary,
  resolution_code,
  reason_for_change,
}) => {
  if (demoBuild) {
    const updatedDemo = applyDemoTicketTransition(tickets.value, {
      ticketId,
      newStatus,
      root_cause_category,
      root_cause_summary,
      resolution_code,
    });
    if (updatedDemo) {
      selectedTicket.value = updatedDemo;
    }
    return;
  }

  const reason =
    (reason_for_change || "").trim() || `Status transition to ${newStatus}`;
  try {
    const updated = await ticketsApi.transitionTicket(
      ticketId,
      {
        target_status: newStatus,
        root_cause_category: root_cause_category || null,
        root_cause_summary: root_cause_summary || null,
        resolution_code: resolution_code || null,
      },
      reason
    );
    upsertTicketInList(updated);
    selectedTicket.value = updated;
    await refreshLiveAuditTrail(updated.id || ticketId);
    toastStore.push({
      tone: "success",
      title: `Ticket ${updated.reference || updated.id} moved to ${formatStatus(updated.status)}`,
      message: reason,
    });
  } catch (err) {
    toastStore.apiError("Updating ticket status failed", err);
  }
};

const handleBatchTransition = (targetStatus) => {
  if (!demoBuild) return;
  const statusToApply = targetStatus || batchStatus.value;
  if (!statusToApply) return;
  if (selectedTicketIds.value.size === 0) return;

  const targetIds = Array.from(selectedTicketIds.value);
  if (applyDemoBatchStatusTransition(tickets.value, targetIds, statusToApply)) {
    clearSelection();
  }
};

const handleBatchSeverityUpdate = (targetSeverity) => {
  if (!demoBuild) return;
  const severityToApply = targetSeverity || batchSeverity.value;
  if (!severityToApply) return;
  if (selectedTicketIds.value.size === 0) return;

  const targetIds = Array.from(selectedTicketIds.value);
  if (applyDemoBatchSeverityUpdate(tickets.value, targetIds, severityToApply)) {
    clearSelection();
  }
};

const handleTicketComment = async ({
  ticketId,
  content,
  visibility,
  reason_for_change,
}) => {
  if (demoBuild) {
    selectedTicketComments.value.push(
      buildDemoTicketComment({ content, visibility, reason_for_change })
    );
    return;
  }

  const targetId = ticketId || selectedTicket.value?.id;
  if (!targetId) return;
  const reason =
    (reason_for_change || "").trim() || "Added ticket discussion note";
  try {
    const created = await ticketsApi.createComment(
      targetId,
      {
        body: content,
        visibility: visibility || "PUBLIC",
      },
      reason
    );
    selectedTicketComments.value.push(created);
    await refreshLiveAuditTrail(targetId);
    toastStore.push({
      tone: "success",
      title: "Discussion note recorded",
      message:
        created.visibility === "INTERNAL_SPONSOR"
          ? "Saved as Internal Sponsor Note"
          : "Saved as Public Site/Sponsor Note",
    });
  } catch (err) {
    toastStore.apiError("Posting ticket comment failed", err);
  }
};

const handleTicketAttachment = ({ file, reason_for_change }) => {
  if (!demoBuild) return;
  const newAttachment = buildDemoTicketAttachment({ file, reason_for_change });
  if (newAttachment) {
    selectedTicketAttachments.value.push(newAttachment);
  }
};

const handleTicketSigned = async ({
  ticketId,
  ticket: signedTicket,
  signature_token,
  meaning,
}) => {
  if (demoBuild) {
    const updatedDemo = applyDemoTicketSignature(tickets.value, {
      ticketId,
      signature_token,
      meaning,
    });
    if (updatedDemo) {
      selectedTicket.value = updatedDemo;
    }
    return;
  }

  if (signedTicket) {
    upsertTicketInList(signedTicket);
    selectedTicket.value = signedTicket;
    await refreshLiveAuditTrail(signedTicket.id || ticketId);
    toastStore.push({
      tone: "success",
      title: `21 CFR Part 11 signature recorded on ${signedTicket.reference || signedTicket.id}`,
      message: signedTicket.signature_meaning || meaning,
    });
  }
};

const handleTicketCreated = (createdTicket) => {
  if (!createdTicket) return;
  knowledgeStore.recordEscalatedTicket(createdTicket);
  const existingIdx = tickets.value.findIndex(
    (t) => t.id === createdTicket.id || t.reference === createdTicket.reference
  );
  if (existingIdx !== -1) {
    tickets.value.splice(existingIdx, 1);
  }
  tickets.value.unshift(createdTicket);

  const refId = createdTicket.reference || createdTicket.id;
  const simulated = isDemoBuild() || String(refId || "").startsWith("SIM-");
  toastStore.push({
    tone: "success",
    title: simulated
      ? `Simulated ticket ${refId} saved in demo session`
      : `Support ticket ${refId} submitted`,
    message: createdTicket.assigned_queue
      ? `Reference ID: ${refId} (${createdTicket.assigned_queue})`
      : `Reference ID: ${refId}`,
  });
};

const exportAuditTrail = (format) => {
  if (!demoBuild) return;
  exportDemoAuditTrail(tickets.value, format);
};
</script>

<style scoped>
.dashboard-section {
  padding: 24px 32px;
  display: flex;
  flex-direction: column;
  gap: 24px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

@media (max-width: 640px) {
  .dashboard-section {
    padding: 12px 8px;
    gap: 16px;
  }
}

/* Header */
.section-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px 24px;
  min-width: 0;
}

.header-badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-gxp {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}
.badge-regulatory {
  background: var(--semantic-color-accent);
  color: var(--semantic-color-surface);
}
.badge-standard {
  background: var(--semantic-color-text-secondary);
  color: var(--semantic-color-surface);
}

.view-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
  letter-spacing: -0.02em;
}

.view-subtitle {
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
  max-width: 820px;
  line-height: 1.45;
  margin: 0;
}

.header-action-stack {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
}

.header-action-group {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
}

.btn-icon-svg {
  flex-shrink: 0;
}

.auditor-readonly-note {
  margin: 0;
  max-width: 100%;
  width: min(100%, 420px);
  font-size: 0.78rem;
  line-height: 1.4;
  color: var(--semantic-color-text-secondary);
  text-align: right;
  overflow-wrap: anywhere;
}

/* Stats Cards Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 200px), 1fr));
  gap: 16px;
}

.stat-card {
  background: var(--semantic-color-surface);
  border-radius: 10px;
  padding: 16px 20px;
  border: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  gap: 16px;
}

.stat-card-btn {
  width: 100%;
  font: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
  transition:
    transform 0.15s ease,
    box-shadow 0.15s ease;
}

.stat-card-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 12px -2px rgba(0, 0, 0, 0.08);
}

.stat-icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.3rem;
  background: var(--semantic-color-surface-header);
}

.alert-icon {
  background: var(--semantic-color-info-bg);
}
.capa-icon {
  background: var(--semantic-color-error-bg-soft);
}
.rbqm-icon {
  background: var(--semantic-color-success-bg-soft);
}
.qtl-icon {
  background: var(--semantic-color-purple-bg);
}
.breach-icon {
  background: var(--semantic-color-warning-bg-soft);
}

.stat-details {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  line-height: 1.2;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.stat-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.text-danger {
  color: var(--semantic-color-error);
}
.text-success {
  color: var(--semantic-color-success);
}
.text-warning {
  color: var(--semantic-color-warning);
}

/* Filter & Controls Card */
.controls-card {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  padding: 12px 18px;
  border: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.search-box {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 12px;
  flex: 1 1 240px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.search-icon {
  font-size: 0.9rem;
  flex-shrink: 0;
}

.search-field {
  flex: 1;
  min-width: 0;
}

.search-input {
  border: none;
  border-radius: 4px;
  background: transparent;
  font-size: 0.85rem;
  width: 100%;
  min-width: 0;
  min-height: var(--semantic-control-target-min, 24px);
}

.scope-filter-group {
  display: flex;
  gap: 8px;
}

.filter-input {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  width: 128px;
}

@media (pointer: coarse) {
  .filter-input {
    font-size: 1rem;
    min-height: 44px;
  }
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

.filter-group {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
}

.filter-select {
  padding: 6px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.scope-filter-input {
  width: 132px;
  max-width: 100%;
  font-family: var(--font-mono);
}

/* KANBAN BOARD VIEW */
.kanban-board {
  position: relative;
  display: grid;
  grid-template-columns: repeat(5, minmax(240px, 1fr));
  gap: 16px;
  overflow-x: auto;
  max-width: 100%;
  align-items: flex-start;
}

.kanban-column {
  background: var(--semantic-color-surface-muted);
  border-radius: 10px;
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 320px);
}

.column-header {
  padding: 12px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--semantic-color-border);
}

.column-title-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.column-bullet {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.col-open {
  background: var(--semantic-color-interactive-primary);
}
.col-progress {
  background: var(--semantic-color-info);
}
.col-paused {
  background: var(--semantic-color-warning-action);
}
.col-resolved {
  background: var(--semantic-color-success);
}
.col-closed {
  background: var(--semantic-color-text-secondary);
}

.column-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.column-count {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 7px;
  border-radius: 10px;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.column-cards {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  overflow-y: auto;
}

.ticket-card {
  width: 100%;
  font: inherit;
  color: inherit;
  text-align: left;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 12px 14px;
  cursor: pointer;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  transition:
    transform 0.12s ease,
    border-color 0.12s ease;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ticket-card:hover:not(:disabled) {
  transform: translateY(-2px);
  border-color: var(--semantic-color-interactive-primary);
  box-shadow: 0 4px 8px -1px rgba(0, 0, 0, 0.1);
}

.ticket-card:focus-visible,
.kanban-board:focus-visible,
.column-cards:focus-visible {
  outline: 2px solid var(--semantic-color-interactive-primary);
  outline-offset: 2px;
}

.card-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.ref-tag {
  font-family: monospace;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
}

.card-ticket-title {
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  margin: 0;
  line-height: 1.35;
}

.card-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.72rem;
}

.category-pill {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
}

.sla-mini-badge {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  font-variant-numeric: var(--font-variant-numeric-tabular);
}

.sla-tag-normal {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}
.sla-tag-warned {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}
.sla-tag-breached {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}
.fill-paused {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.badge-sev-critical {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}
.badge-sev-major {
  background: var(--semantic-color-orange-bg);
  color: var(--semantic-color-orange-text);
}
.badge-sev-minor {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-interactive-primary);
}
.badge-paused {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}
.badge-signed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}
.badge-pending-sign {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}
.badge-closed {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

/* TABLE VIEW */
.table-card {
  position: relative;
  background: var(--semantic-color-surface);
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  overflow-x: auto;
  max-width: 100%;
}

.clinical-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 0.82rem;
}

.clinical-table th {
  background: var(--semantic-color-surface-muted);
  padding: 10px 14px;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  border-bottom: 1px solid var(--semantic-color-border-control);
  text-transform: uppercase;
  font-size: 0.72rem;
  letter-spacing: 0.02em;
}

.clinical-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}

.table-row {
  cursor: pointer;
  transition: background-color 0.1s ease;
}

.table-row:hover {
  background: var(--semantic-color-surface-muted);
}

.font-mono {
  font-family: monospace;
}
.font-semibold {
  font-weight: 600;
}

.empty-table-cell {
  text-align: center;
  padding: 36px !important;
  color: var(--semantic-color-text-secondary);
}

.priority-tag {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
}

.priority-critical {
  color: var(--semantic-color-error);
}
.priority-high {
  color: var(--semantic-color-orange-text);
}
.priority-medium {
  color: var(--semantic-color-interactive-primary);
}
.priority-low {
  color: var(--semantic-color-text-secondary);
}

.btn {
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
}

.btn-xs {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 3px 8px;
  font-size: 0.75rem;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}
.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
}
.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}
.checkbox-cell {
  width: 36px;
  text-align: center;
}

.ticket-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  cursor: pointer;
  accent-color: var(--semantic-color-interactive-primary);
}

.selected-row {
  background-color: var(--semantic-color-info-bg-soft) !important;
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
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
</style>
