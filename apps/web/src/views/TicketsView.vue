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

      <div class="header-action-group">
        <button
          v-if="demoBuild"
          class="btn btn-secondary"
          title="Export Regulatory Audit Trail CSV"
          @click="exportAuditTrail('csv')"
        >
          <span class="btn-icon">📊</span> Export CSV
        </button>
        <button
          v-if="demoBuild"
          class="btn btn-secondary"
          title="Export Regulatory Audit Trail JSON"
          @click="exportAuditTrail('json')"
        >
          <span class="btn-icon">📄</span> Export JSON
        </button>
        <button
          class="btn btn-primary"
          title="Log New Clinical Issue"
          @click="isCreateModalOpen = true"
        >
          <span class="btn-icon">➕</span> Log Issue
        </button>
      </div>
    </div>

    <!-- KRI Metric Cards Grid -->
    <div class="stats-grid">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="stat-card" @click="filterStatus = 'ALL'">
        <div class="stat-icon alert-icon">🎫</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ activeTicketsCount }}
          </div>
          <div class="stat-label">Active Clinical Issues</div>
        </div>
      </div>
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="stat-card" @click="filterSeverity = 'CRITICAL'">
        <div class="stat-icon capa-icon">🚨</div>
        <div class="stat-details">
          <div class="stat-value text-danger">
            {{ criticalTicketsCount }}
          </div>
          <div class="stat-label">Critical GxP Deviations</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon rbqm-icon">⏱</div>
        <div class="stat-details">
          <div
            id="kpi-sla-compliance"
            class="stat-value"
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
          <div id="kpi-mttr" class="stat-value">
            {{ mttrDisplay }}
          </div>
          <div class="stat-label">Mean Resolution Time (MTTR)</div>
        </div>
      </div>
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="stat-card" @click="filterStatus = 'WAITING_ON_SITE'">
        <div class="stat-icon breach-icon">⏸</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ pausedTicketsCount }}
          </div>
          <div class="stat-label">SLA Paused (Waiting Site)</div>
        </div>
      </div>
    </div>

    <!-- Filter & View Mode Control Bar -->
    <div class="controls-card">
      <div class="search-box">
        <span class="search-icon">🔍</span>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          v-model="searchQuery"
          type="text"
          class="search-input"
          placeholder="Search by ref #, title, subject ID, or site..."
        />
      </div>

      <div class="filter-group">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <select v-model="filterCategory" class="filter-select">
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
        </select>

        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <select v-model="filterSeverity" class="filter-select">
          <option value="ALL">All Severities</option>
          <option value="CRITICAL">Critical</option>
          <option value="MAJOR">Major</option>
          <option value="MINOR">Minor</option>
        </select>

        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <select v-model="filterStatus" class="filter-select">
          <option value="ALL">All Statuses</option>
          <option value="OPEN">Open</option>
          <option value="IN_PROGRESS">In Progress</option>
          <option value="WAITING_ON_SITE">Waiting on Site</option>
          <option value="WAITING_ON_SPONSOR">Waiting on Sponsor</option>
          <option value="RESOLVED">Resolved</option>
          <option value="CLOSED">Closed</option>
        </select>
      </div>

      <div class="view-mode-toggle">
        <button
          class="view-toggle-btn"
          :class="{ active: viewMode === 'kanban' }"
          title="Switch to Kanban Board"
          @click="viewMode = 'kanban'"
        >
          📋 Kanban
        </button>
        <button
          class="view-toggle-btn"
          :class="{ active: viewMode === 'table' }"
          title="Switch to Density Table"
          @click="viewMode = 'table'"
        >
          📄 Table
        </button>
      </div>
    </div>

    <!-- Bulk Action Toolbar (Demo mode only) -->
    <div v-if="demoBuild && selectedTicketCount > 0" class="bulk-action-bar">
      <div class="bulk-info">
        <span class="bulk-count-badge"
          >{{ selectedTicketCount }} ticket(s) selected</span
        >
      </div>

      <div class="bulk-actions">
        <div class="bulk-action-group">
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <select v-model="batchStatus" class="filter-select batch-select">
            <option value="" disabled>Change Status...</option>
            <option value="OPEN">Open</option>
            <option value="IN_PROGRESS">In Progress</option>
            <option value="WAITING_ON_SITE">Waiting on Site</option>
            <option value="WAITING_ON_SPONSOR">Waiting on Sponsor</option>
            <option value="RESOLVED">Resolved</option>
            <option value="CLOSED">Closed</option>
          </select>
          <button
            class="btn btn-sm btn-primary"
            :disabled="!batchStatus"
            @click="handleBatchTransition(batchStatus)"
          >
            Apply Status
          </button>
        </div>

        <div class="bulk-action-group">
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <select v-model="batchSeverity" class="filter-select batch-select">
            <option value="" disabled>Change Severity...</option>
            <option value="CRITICAL">Critical</option>
            <option value="MAJOR">Major</option>
            <option value="MINOR">Minor</option>
          </select>
          <button
            class="btn btn-sm btn-primary"
            :disabled="!batchSeverity"
            @click="handleBatchSeverityUpdate(batchSeverity)"
          >
            Apply Severity
          </button>
        </div>

        <button class="btn btn-sm btn-secondary" @click="clearSelection">
          Clear Selection
        </button>
      </div>
    </div>

    <!-- MAIN WORKSPACE: KANBAN VIEW -->
    <div v-if="viewMode === 'kanban'" class="kanban-board">
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
        <div class="column-cards">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="t in getColumnTickets('OPEN')"
            :key="t.id"
            class="ticket-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span
                class="badge"
                :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`"
                >{{ t.gxp_severity }}</span
              >
            </div>
            <h4 class="card-ticket-title">
              {{ t.title }}
            </h4>
            <div class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <div
                v-if="t.sla_target_at"
                class="sla-mini-badge"
                :class="getSlaClass(t)"
              >
                {{ formatSlaRemaining(t) }}
              </div>
            </div>
          </div>
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
        <div class="column-cards">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="t in getColumnTickets('IN_PROGRESS')"
            :key="t.id"
            class="ticket-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span
                class="badge"
                :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`"
                >{{ t.gxp_severity }}</span
              >
            </div>
            <h4 class="card-ticket-title">
              {{ t.title }}
            </h4>
            <div class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <div
                v-if="t.sla_target_at"
                class="sla-mini-badge"
                :class="getSlaClass(t)"
              >
                {{ formatSlaRemaining(t) }}
              </div>
            </div>
          </div>
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
        <div class="column-cards">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="t in getColumnTickets([
              'WAITING_ON_SITE',
              'WAITING_ON_SPONSOR',
              'PENDING_REGULATORY_REVIEW',
            ])"
            :key="t.id"
            class="ticket-card paused-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span class="badge badge-paused">{{
                formatStatus(t.status)
              }}</span>
            </div>
            <h4 class="card-ticket-title">
              {{ t.title }}
            </h4>
            <div class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <div class="sla-mini-badge fill-paused">⏸ SLA Paused</div>
            </div>
          </div>
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
        <div class="column-cards">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="t in getColumnTickets('RESOLVED')"
            :key="t.id"
            class="ticket-card resolved-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span v-if="t.signature_token" class="badge badge-signed"
                >✍️ eSigned</span
              >
              <span v-else class="badge badge-pending-sign"
                >Pending Sign-off</span
              >
            </div>
            <h4 class="card-ticket-title">
              {{ t.title }}
            </h4>
            <div class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
              <span class="rca-mini-tag">{{
                t.resolution_code || "Resolved"
              }}</span>
            </div>
          </div>
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
        <div class="column-cards">
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
          <div
            v-for="t in getColumnTickets('CLOSED')"
            :key="t.id"
            class="ticket-card closed-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{
                t.reference || `#${t.id?.substring(0, 8)}`
              }}</span>
              <span class="badge badge-closed">🔒 Closed</span>
            </div>
            <h4 class="card-ticket-title">
              {{ t.title }}
            </h4>
            <div class="card-footer">
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- MAIN WORKSPACE: TABLE VIEW -->
    <div v-else class="table-card">
      <table class="clinical-table">
        <thead>
          <tr>
            <th v-if="demoBuild" class="checkbox-cell">
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                type="checkbox"
                class="ticket-checkbox"
                :checked="isAllSelected"
                :indeterminate="isSomeSelected"
                @change="toggleSelectAll"
              />
            </th>
            <th>Reference</th>
            <th>Title</th>
            <th>Category</th>
            <th>GxP Severity</th>
            <th>Status</th>
            <th>Study / Site / Subject</th>
            <th>Priority</th>
            <th>SLA Status</th>
            <th>Sign-Off</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="t in filteredTickets"
            :key="t.id"
            class="table-row"
            :class="{
              'selected-row': demoBuild && selectedTicketIds.has(t.id),
            }"
            @click="openTicketDetail(t)"
          >
            <td v-if="demoBuild" class="checkbox-cell" @click.stop>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                type="checkbox"
                class="ticket-checkbox"
                :checked="selectedTicketIds.has(t.id)"
                @change="toggleSelectTicket(t.id)"
              />
            </td>
            <td class="ref-cell font-mono">
              {{ t.reference || `#${t.id?.substring(0, 8)}` }}
            </td>
            <td class="title-cell font-semibold">
              {{ t.title }}
            </td>
            <td>
              <span class="category-pill">{{
                formatCategory(t.category)
              }}</span>
            </td>
            <td>
              <span
                class="badge"
                :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`"
                >{{ t.gxp_severity }}</span
              >
            </td>
            <td>
              <span
                class="badge"
                :class="`badge-status-${t.status?.toLowerCase()}`"
                >{{ formatStatus(t.status) }}</span
              >
            </td>
            <td class="context-cell">
              <span v-if="t.site_id">{{ t.site_id }}</span>
              <span v-if="t.subject_id"> / {{ t.subject_id }}</span>
              <span v-if="!t.site_id && !t.subject_id">{{
                t.study_id || "—"
              }}</span>
            </td>
            <td>
              <span
                class="priority-tag"
                :class="`priority-${t.priority?.toLowerCase()}`"
                >{{ t.priority }}</span
              >
            </td>
            <td>
              <div
                v-if="t.sla_target_at"
                class="sla-mini-badge"
                :class="getSlaClass(t)"
              >
                {{ formatSlaRemaining(t) }}
              </div>
              <span v-else>—</span>
            </td>
            <td>
              <span v-if="t.signature_token" class="badge badge-signed"
                >✍️ eSigned</span
              >
              <span
                v-else-if="t.status === 'RESOLVED'"
                class="badge badge-pending-sign"
                >Needs Sign</span
              >
              <span v-else>—</span>
            </td>
            <td>
              <button
                class="btn btn-xs btn-secondary"
                @click.stop="openTicketDetail(t)"
              >
                Inspect
              </button>
            </td>
          </tr>

          <tr v-if="filteredTickets.length === 0">
            <td :colspan="demoBuild ? 11 : 10" class="empty-table-cell">
              No clinical issues match the selected search and filter criteria.
            </td>
          </tr>
        </tbody>
      </table>
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
      :is-open="isCreateModalOpen"
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
} from "vue";
import TicketDetailDrawer from "../components/tickets/TicketDetailDrawer.vue";
import TicketCreateModal from "../components/tickets/TicketCreateModal.vue";
import { ticketsApi } from "../api/ticketsApi";
import { useKnowledgeStore } from "../stores/knowledge.js";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";

const demoBuild = isDemoBuild();
const instance = getCurrentInstance();
const knowledgeStore = useKnowledgeStore();
const toastStore = useToastStore();
const tickets = ref([]);
const kpis = reactive({
  active_tickets: 0,
  sla_compliance_rate: demoBuild ? 96.4 : null,
  mttr_hours: demoBuild ? 4.2 : null,
  critical_tickets: 0,
});

// UI State
const viewMode = ref("kanban"); // "kanban" | "table"
const searchQuery = ref("");
const filterCategory = ref("ALL");
const filterSeverity = ref("ALL");
const filterStatus = ref("ALL");

// Selection & Batch Action State
const selectedTicketIds = ref(new Set());
const batchStatus = ref("");
const batchSeverity = ref("");

const selectedTicketCount = computed(() => selectedTicketIds.value.size);

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

// Initial Seed Scenario Data for Demo Workspace
const initialMockTickets = [
  {
    id: "tk-8801",
    reference: "DEV-2026-0041",
    title: "Investigational Product Cold-Chain Excursion at Site 101",
    description:
      "Cold-chain continuous temperature logger logged 9.4°C (protocol limit 2.0-8.0°C) during weekend storage.",
    category: "SUPPLY_EXCURSION",
    gxp_severity: "CRITICAL",
    priority: "CRITICAL",
    status: "OPEN",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: null,
    entity_type: "SUPPLY_KIT",
    entity_id: "KIT-9941",
    created_by: "cra.monitor@cadence.clinical",
    created_at: new Date(Date.now() - 3600 * 1000 * 2).toISOString(),
    sla_target_at: new Date(Date.now() + 3600 * 1000 * 2).toISOString(),
    sla_breached: false,
    sla_amber_warned: false,
    version_index: 1,
  },
  {
    id: "tk-8802",
    reference: "DEV-2026-0042",
    title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
    description:
      "Subject 8812 PK blood sample collection missed due to scheduling conflict.",
    category: "PROTOCOL_DEVIATION",
    gxp_severity: "MAJOR",
    priority: "HIGH",
    status: "IN_PROGRESS",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-102",
    subject_id: "SUBJ-8812",
    entity_type: "SUBJECT",
    entity_id: "SUBJ-8812",
    created_by: "crc.smith@cadence.clinical",
    created_at: new Date(Date.now() - 3600 * 1000 * 8).toISOString(),
    sla_target_at: new Date(Date.now() + 3600 * 1000 * 16).toISOString(),
    sla_breached: false,
    sla_amber_warned: false,
    version_index: 2,
  },
  {
    id: "tk-8803",
    reference: "QRY-2026-0109",
    title: "Inconsistent Diastolic BP Observation in Vital Signs Form",
    description:
      "Systolic 120 recorded with Diastolic 130 mmHg. Out-of-bounds cross-field validation query.",
    category: "DATA_QUERY",
    gxp_severity: "MINOR",
    priority: "MEDIUM",
    status: "WAITING_ON_SITE",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: "SUBJ-8815",
    entity_type: "CRF_FORM",
    entity_id: "FORM-VS-003",
    created_by: "dm.lead@cadence.clinical",
    created_at: new Date(Date.now() - 3600 * 1000 * 24).toISOString(),
    sla_target_at: new Date(Date.now() + 3600 * 1000 * 48).toISOString(),
    sla_total_paused_seconds: 3600 * 12,
    sla_breached: false,
    version_index: 2,
  },
  {
    id: "tk-8804",
    reference: "DEV-2026-0038",
    title: "Informed Consent Form Re-Consent Signed on V2 Instead of V3",
    description:
      "Subject 8804 re-consented on outdated paper template version during Protocol Amendment 2 rollout.",
    category: "PROTOCOL_DEVIATION",
    gxp_severity: "MAJOR",
    priority: "HIGH",
    status: "RESOLVED",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: "SUBJ-8804",
    entity_type: "SUBJECT",
    entity_id: "SUBJ-8804",
    created_by: "cra.monitor@cadence.clinical",
    created_at: new Date(Date.now() - 3600 * 1000 * 72).toISOString(),
    sla_target_at: new Date(Date.now() - 3600 * 1000 * 24).toISOString(),
    signature_token: "sig_token_hex_a982fe41b3",
    signature_user: "dr_smith_pi",
    signature_timestamp: new Date(Date.now() - 3600 * 1000 * 12).toISOString(),
    signature_meaning:
      "I approve the clinical assessment and corrective medical action.",
    root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
    root_cause_summary:
      "Site ICF version binders were updated and CRC re-training completed.",
    resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
    version_index: 4,
  },
];

function mergeDemoEscalatedTickets(baseTickets) {
  if (!isDemoBuild() || !knowledgeStore.escalatedTickets.length) {
    return baseTickets;
  }
  const seen = new Set(baseTickets.map((t) => t.id || t.reference));
  const extra = knowledgeStore.escalatedTickets.filter(
    (t) => !seen.has(t.id) && !seen.has(t.reference)
  );
  return [...extra, ...baseTickets];
}

function getRouteRefParam() {
  const proxyRoute = instance?.proxy?.$route;
  const raw =
    proxyRoute?.query?.ref ??
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
    tickets.value = mergeDemoEscalatedTickets([...initialMockTickets]);
  }
  await fetchTicketsFromApi();
  await openDeepLinkedTicketIfRequested();
});

watch(
  () => instance?.proxy?.$route?.query?.ref,
  async (nextRef, prevRef) => {
    if (nextRef && nextRef !== prevRef) {
      await openDeepLinkedTicketIfRequested();
    }
  }
);

const fetchTicketsFromApi = async () => {
  try {
    const res = await ticketsApi.listTickets();
    if (res && Array.isArray(res)) {
      tickets.value = demoBuild ? mergeDemoEscalatedTickets(res) : res;
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
    selectedTicketComments.value = [
      {
        id: "cm-001",
        user_id: t.created_by || "cra.monitor@cadence.clinical",
        content: `Logged initial ${formatCategory(t.category)}: ${t.description}`,
        visibility: "PUBLIC",
        created_at: t.created_at,
      },
      {
        id: "cm-002",
        user_id: "sponsor.qa@cadence.clinical",
        content:
          "Quality assessment initiated under ICH E6(R3). Reviewing cold-chain calibration records.",
        visibility: "INTERNAL_SPONSOR",
        created_at: new Date(
          new Date(t.created_at).getTime() + 1800 * 1000
        ).toISOString(),
      },
    ];
    selectedTicketAttachments.value = [
      {
        id: "att-001",
        filename: "logger_raw_export.csv",
        file_size_bytes: 42100,
        mime_type: "text/csv",
        sha256_hash:
          "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
        uploaded_by: t.created_by || "cra.monitor@cadence.clinical",
        uploaded_at: t.created_at,
        deid_scrubbed: true,
      },
    ];
    selectedTicketAuditLogs.value = [
      {
        action: "CREATE",
        user_id: t.created_by || "cra.monitor@cadence.clinical",
        timestamp: t.created_at,
        change_reason: "Initial ticket registration",
      },
    ];
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
    const index = tickets.value.findIndex((t) => t.id === ticketId);
    if (index !== -1) {
      tickets.value[index].status = newStatus;
      if (root_cause_category)
        tickets.value[index].root_cause_category = root_cause_category;
      if (root_cause_summary)
        tickets.value[index].root_cause_summary = root_cause_summary;
      if (resolution_code)
        tickets.value[index].resolution_code = resolution_code;
      selectedTicket.value = { ...tickets.value[index] };
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
  const statusToApply = targetStatus || batchStatus.value;
  if (!statusToApply) return;
  if (selectedTicketIds.value.size === 0) return;
  if (!allowUnbackedAction("Batch updating ticket status")) return;

  const targetIds = Array.from(selectedTicketIds.value);
  tickets.value.forEach((t) => {
    if (targetIds.includes(t.id)) {
      if (t.status === "CLOSED" || t.signature_token) {
        return;
      }
      t.status = statusToApply;
      t.version_index = (t.version_index || 1) + 1;
      t.audit_logs = t.audit_logs || [];
      t.audit_logs.push({
        action: "BATCH_STATUS_UPDATE",
        user_id: "active.user@cadence.clinical",
        timestamp: new Date().toISOString(),
        change_reason: `Batch status transition to ${statusToApply}`,
      });
    }
  });

  clearSelection();
};

const handleBatchSeverityUpdate = (targetSeverity) => {
  const severityToApply = targetSeverity || batchSeverity.value;
  if (!severityToApply) return;
  if (selectedTicketIds.value.size === 0) return;
  if (!allowUnbackedAction("Batch updating ticket severity")) return;

  const targetIds = Array.from(selectedTicketIds.value);
  tickets.value.forEach((t) => {
    if (targetIds.includes(t.id)) {
      if (t.status === "CLOSED") {
        return;
      }
      t.gxp_severity = severityToApply;
      t.version_index = (t.version_index || 1) + 1;
      t.audit_logs = t.audit_logs || [];
      t.audit_logs.push({
        action: "BATCH_SEVERITY_UPDATE",
        user_id: "active.user@cadence.clinical",
        timestamp: new Date().toISOString(),
        change_reason: `Batch severity update to ${severityToApply}`,
      });
    }
  });

  clearSelection();
};

const handleTicketComment = async ({
  ticketId,
  content,
  visibility,
  reason_for_change,
}) => {
  if (demoBuild) {
    const newComment = {
      id: "cm-" + Date.now(),
      user_id: "active.user@cadence.clinical",
      content,
      visibility,
      created_at: new Date().toISOString(),
      reason_for_change,
    };
    selectedTicketComments.value.push(newComment);
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
  if (!allowUnbackedAction("Attaching a file to a ticket")) return;
  const newAttachment = {
    id: "att-" + Date.now(),
    filename: file.name,
    file_size_bytes: file.size,
    mime_type: file.type || "application/octet-stream",
    sha256_hash:
      "a4f891b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abc", // pragma: allowlist secret
    uploaded_by: "active.user@cadence.clinical",
    uploaded_at: new Date().toISOString(),
    deid_scrubbed: true,
    reason_for_change,
  };
  selectedTicketAttachments.value.push(newAttachment);
};

const handleTicketSigned = async ({
  ticketId,
  ticket: signedTicket,
  signature_token,
  meaning,
}) => {
  if (demoBuild) {
    const index = tickets.value.findIndex((t) => t.id === ticketId);
    if (index !== -1) {
      tickets.value[index].signature_token = signature_token;
      tickets.value[index].signature_meaning = meaning;
      tickets.value[index].signature_timestamp = new Date().toISOString();
      tickets.value[index].signature_user = "active.user@cadence.clinical";
      selectedTicket.value = { ...tickets.value[index] };
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
  // The export is built from the in-browser ticket list, not an audit trail any service kept.
  if (!allowUnbackedAction("Exporting the ticket audit trail")) return;
  const data = JSON.stringify(tickets.value, null, 2);
  const blob = new Blob([data], {
    type: format === "json" ? "application/json" : "text/csv",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `tickets_part11_audit_trail.${format}`;
  a.click();
  URL.revokeObjectURL(url);
};
</script>

<style scoped>
.dashboard-section {
  padding: 24px 32px;
  display: flex;
  flex-direction: column;
  gap: 24px;
  min-width: 0;
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

.header-content-group {
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

.header-action-group {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  min-width: 0;
}

/* Stats Cards Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 12.5rem), 1fr));
  gap: 16px;
  min-width: 0;
}

.stat-card {
  background: var(--semantic-color-surface);
  border-radius: 10px;
  padding: 16px 20px;
  border: 1px solid var(--semantic-color-border);
  display: flex;
  align-items: center;
  gap: 16px;
  min-width: 0;
  cursor: pointer;
  transition:
    transform 0.15s ease,
    box-shadow 0.15s ease;
}

.stat-card:hover {
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
  min-width: 0;
}

.search-box {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 12px;
  flex: 1 1 15rem;
  min-width: 0;
  max-width: 100%;
}

.search-icon {
  font-size: 0.9rem;
}

.search-input {
  border: none;
  border-radius: 4px;
  background: transparent;
  font-size: 0.85rem;
  width: 100%;
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
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
  min-width: var(--semantic-control-target-min, 24px);
  max-width: 100%;
}

.view-mode-toggle {
  display: flex;
  background: var(--semantic-color-surface-header);
  border-radius: 6px;
  padding: 2px;
}

.view-toggle-btn {
  padding: 6px 12px;
  border: none;
  background: transparent;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  border-radius: 4px;
  cursor: pointer;
}

.view-toggle-btn.active {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

/* KANBAN BOARD VIEW */
.kanban-board {
  display: grid;
  grid-template-columns: repeat(5, minmax(15rem, 1fr));
  gap: 16px;
  overflow-x: auto;
  align-items: flex-start;
  min-width: 0;
  max-width: 100%;
}

.kanban-column {
  background: var(--semantic-color-surface-muted);
  border-radius: 10px;
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 320px);
  min-width: 0;
}

@media (max-width: 767px) {
  .dashboard-section {
    padding: 12px 0;
  }
  .kanban-board {
    grid-template-columns: minmax(0, 1fr);
    overflow-x: visible;
  }
  .kanban-column {
    max-height: none;
  }
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
  background: var(--semantic-color-ai);
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
}

.column-cards {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  overflow-y: auto;
}

.ticket-card {
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

.ticket-card:hover {
  transform: translateY(-2px);
  border-color: var(--semantic-color-interactive-primary);
  box-shadow: 0 4px 8px -1px rgba(0, 0, 0, 0.1);
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
  background: var(--semantic-color-surface);
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  overflow-x: auto;
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
  transition:
    background-color 0.15s ease-in-out,
    border-color 0.15s ease-in-out,
    color 0.15s ease-in-out,
    box-shadow 0.15s ease-in-out,
    opacity 0.15s ease-in-out;
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
.btn-primary:hover {
  background: var(--semantic-color-interactive-primary);
}
.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}
/* Bulk Action Toolbar */
.bulk-action-bar {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  border-radius: 8px;
  padding: 12px 18px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.bulk-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.bulk-count-badge {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-info);
  background: var(--semantic-color-info-light);
  padding: 4px 10px;
  border-radius: 12px;
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.bulk-action-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.batch-select {
  font-size: 0.82rem;
  padding: 5px 10px;
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

.btn-sm {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 5px 10px;
  font-size: 0.8rem;
}

.btn-secondary:hover {
  background: var(--semantic-color-surface-header);
}
</style>
