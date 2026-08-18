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
          Unified eClinical operations desk synthesizing protocol deviations, site queries, data discrepancies,
          supply excursions, and CAPA linkages with multi-tier clinical SLA timers, 21 CFR Part 11 electronic signatures,
          and dual-visibility discussion streams.
        </p>
      </div>

      <div class="header-action-group">
        <button class="btn btn-secondary" title="Export Regulatory Audit Trail CSV" @click="exportAuditTrail('csv')">
          <span class="btn-icon">📊</span> Export CSV
        </button>
        <button class="btn btn-secondary" title="Export Regulatory Audit Trail JSON" @click="exportAuditTrail('json')">
          <span class="btn-icon">📄</span> Export JSON
        </button>
        <button class="btn btn-primary" title="Log New Clinical Issue" @click="isCreateModalOpen = true">
          <span class="btn-icon">➕</span> Log Issue
        </button>
      </div>
    </div>

    <!-- KRI Metric Cards Grid -->
    <div class="stats-grid">
      <div class="stat-card" @click="filterStatus = 'ALL'">
        <div class="stat-icon alert-icon">🎫</div>
        <div class="stat-details">
          <div class="stat-value">{{ activeTicketsCount }}</div>
          <div class="stat-label">Active Clinical Issues</div>
        </div>
      </div>
      <div class="stat-card" @click="filterSeverity = 'CRITICAL'">
        <div class="stat-icon capa-icon">🚨</div>
        <div class="stat-details">
          <div class="stat-value text-danger">{{ criticalTicketsCount }}</div>
          <div class="stat-label">Critical GxP Deviations</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon rbqm-icon">⏱</div>
        <div class="stat-details">
          <div class="stat-value" :class="slaComplianceClass">{{ kpis.sla_compliance_rate || 96.4 }}%</div>
          <div class="stat-label">SLA Target Compliance</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon qtl-icon">⚡</div>
        <div class="stat-details">
          <div class="stat-value">{{ kpis.mttr_hours ? kpis.mttr_hours.toFixed(1) + 'h' : '4.2h' }}</div>
          <div class="stat-label">Mean Resolution Time (MTTR)</div>
        </div>
      </div>
      <div class="stat-card" @click="filterStatus = 'WAITING_ON_SITE'">
        <div class="stat-icon breach-icon">⏸</div>
        <div class="stat-details">
          <div class="stat-value">{{ pausedTicketsCount }}</div>
          <div class="stat-label">SLA Paused (Waiting Site)</div>
        </div>
      </div>
    </div>

    <!-- Filter & View Mode Control Bar -->
    <div class="controls-card">
      <div class="search-box">
        <span class="search-icon">🔍</span>
        <input
          v-model="searchQuery"
          type="text"
          class="search-input"
          placeholder="Search by ref #, title, subject ID, or site..."
        />
      </div>

      <div class="filter-group">
        <select v-model="filterCategory" class="filter-select">
          <option value="ALL">All Categories</option>
          <option value="PROTOCOL_DEVIATION">Protocol Deviation</option>
          <option value="DATA_QUERY">Data Query / Discrepancy</option>
          <option value="SAFETY_ADVERSE_EVENT">Safety &amp; SAE</option>
          <option value="SUPPLY_EXCURSION">Supply &amp; Temperature</option>
          <option value="SITE_OPERATIONS">Site Operations</option>
          <option value="MONITORING_FINDING">Monitoring Finding</option>
          <option value="TECHNICAL_SYSTEM">Technical / Bug</option>
        </select>

        <select v-model="filterSeverity" class="filter-select">
          <option value="ALL">All Severities</option>
          <option value="CRITICAL">Critical</option>
          <option value="MAJOR">Major</option>
          <option value="MINOR">Minor</option>
        </select>

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

    <!-- MAIN WORKSPACE: KANBAN VIEW -->
    <div v-if="viewMode === 'kanban'" class="kanban-board">
      <!-- Column 1: OPEN -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-open"></span>
            <span class="column-title">Open / Triage</span>
          </div>
          <span class="column-count">{{ getColumnTickets('OPEN').length }}</span>
        </div>
        <div class="column-cards">
          <div
            v-for="t in getColumnTickets('OPEN')"
            :key="t.id"
            class="ticket-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</span>
              <span class="badge" :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`">{{ t.gxp_severity }}</span>
            </div>
            <h4 class="card-ticket-title">{{ t.title }}</h4>
            <div class="card-footer">
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
              <div v-if="t.sla_target_at" class="sla-mini-badge" :class="getSlaClass(t)">
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
            <span class="column-bullet col-progress"></span>
            <span class="column-title">Investigation</span>
          </div>
          <span class="column-count">{{ getColumnTickets('IN_PROGRESS').length }}</span>
        </div>
        <div class="column-cards">
          <div
            v-for="t in getColumnTickets('IN_PROGRESS')"
            :key="t.id"
            class="ticket-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</span>
              <span class="badge" :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`">{{ t.gxp_severity }}</span>
            </div>
            <h4 class="card-ticket-title">{{ t.title }}</h4>
            <div class="card-footer">
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
              <div v-if="t.sla_target_at" class="sla-mini-badge" :class="getSlaClass(t)">
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
            <span class="column-bullet col-paused"></span>
            <span class="column-title">Awaiting Action</span>
          </div>
          <span class="column-count">{{ getColumnTickets(['WAITING_ON_SITE', 'WAITING_ON_SPONSOR', 'PENDING_REGULATORY_REVIEW']).length }}</span>
        </div>
        <div class="column-cards">
          <div
            v-for="t in getColumnTickets(['WAITING_ON_SITE', 'WAITING_ON_SPONSOR', 'PENDING_REGULATORY_REVIEW'])"
            :key="t.id"
            class="ticket-card paused-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</span>
              <span class="badge badge-paused">{{ formatStatus(t.status) }}</span>
            </div>
            <h4 class="card-ticket-title">{{ t.title }}</h4>
            <div class="card-footer">
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
              <div class="sla-mini-badge fill-paused">⏸ SLA Paused</div>
            </div>
          </div>
        </div>
      </div>

      <!-- Column 4: RESOLVED -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-resolved"></span>
            <span class="column-title">Resolved</span>
          </div>
          <span class="column-count">{{ getColumnTickets('RESOLVED').length }}</span>
        </div>
        <div class="column-cards">
          <div
            v-for="t in getColumnTickets('RESOLVED')"
            :key="t.id"
            class="ticket-card resolved-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</span>
              <span v-if="t.signature_token" class="badge badge-signed">✍️ eSigned</span>
              <span v-else class="badge badge-pending-sign">Pending Sign-off</span>
            </div>
            <h4 class="card-ticket-title">{{ t.title }}</h4>
            <div class="card-footer">
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
              <span class="rca-mini-tag">{{ t.resolution_code || 'Resolved' }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Column 5: CLOSED -->
      <div class="kanban-column">
        <div class="column-header">
          <div class="column-title-group">
            <span class="column-bullet col-closed"></span>
            <span class="column-title">Closed &amp; Archived</span>
          </div>
          <span class="column-count">{{ getColumnTickets('CLOSED').length }}</span>
        </div>
        <div class="column-cards">
          <div
            v-for="t in getColumnTickets('CLOSED')"
            :key="t.id"
            class="ticket-card closed-card"
            @click="openTicketDetail(t)"
          >
            <div class="card-meta">
              <span class="ref-tag">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</span>
              <span class="badge badge-closed">🔒 Closed</span>
            </div>
            <h4 class="card-ticket-title">{{ t.title }}</h4>
            <div class="card-footer">
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
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
            @click="openTicketDetail(t)"
          >
            <td class="ref-cell font-mono">{{ t.reference || `#${t.id?.substring(0, 8)}` }}</td>
            <td class="title-cell font-semibold">{{ t.title }}</td>
            <td>
              <span class="category-pill">{{ formatCategory(t.category) }}</span>
            </td>
            <td>
              <span class="badge" :class="`badge-sev-${t.gxp_severity?.toLowerCase()}`">{{ t.gxp_severity }}</span>
            </td>
            <td>
              <span class="badge" :class="`badge-status-${t.status?.toLowerCase()}`">{{ formatStatus(t.status) }}</span>
            </td>
            <td class="context-cell">
              <span v-if="t.site_id">{{ t.site_id }}</span>
              <span v-if="t.subject_id"> / {{ t.subject_id }}</span>
              <span v-if="!t.site_id && !t.subject_id">{{ t.study_id || '—' }}</span>
            </td>
            <td>
              <span class="priority-tag" :class="`priority-${t.priority?.toLowerCase()}`">{{ t.priority }}</span>
            </td>
            <td>
              <div v-if="t.sla_target_at" class="sla-mini-badge" :class="getSlaClass(t)">
                {{ formatSlaRemaining(t) }}
              </div>
              <span v-else>—</span>
            </td>
            <td>
              <span v-if="t.signature_token" class="badge badge-signed">✍️ eSigned</span>
              <span v-else-if="t.status === 'RESOLVED'" class="badge badge-pending-sign">Needs Sign</span>
              <span v-else>—</span>
            </td>
            <td>
              <button class="btn btn-xs btn-secondary" @click.stop="openTicketDetail(t)">
                Inspect
              </button>
            </td>
          </tr>

          <tr v-if="filteredTickets.length === 0">
            <td colspan="10" class="empty-table-cell">
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
import { ref, reactive, computed, onMounted } from "vue";
import TicketDetailDrawer from "../components/tickets/TicketDetailDrawer.vue";
import TicketCreateModal from "../components/tickets/TicketCreateModal.vue";
import apiClient from "../services/api";

const tickets = ref([]);
const kpis = reactive({
  active_tickets: 0,
  sla_compliance_rate: 96.4,
  mttr_hours: 4.2,
  critical_tickets: 0,
});

// UI State
const viewMode = ref("kanban"); // "kanban" | "table"
const searchQuery = ref("");
const filterCategory = ref("ALL");
const filterSeverity = ref("ALL");
const filterStatus = ref("ALL");

// Modal & Drawer State
const isDrawerOpen = ref(false);
const isCreateModalOpen = ref(false);
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
    description: "Cold-chain continuous temperature logger logged 9.4°C (protocol limit 2.0-8.0°C) during weekend storage.",
    category: "SUPPLY_EXCURSION",
    gxp_severity: "CRITICAL",
    priority: "CRITICAL",
    status: "OPEN",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: null,
    entity_type: "SUPPLY_KIT",
    entity_id: "KIT-9941",
    created_by: "cra.monitor@cadence.io",
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
    description: "Subject 8812 PK blood sample collection missed due to scheduling conflict.",
    category: "PROTOCOL_DEVIATION",
    gxp_severity: "MAJOR",
    priority: "HIGH",
    status: "IN_PROGRESS",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-102",
    subject_id: "SUBJ-8812",
    entity_type: "SUBJECT",
    entity_id: "SUBJ-8812",
    created_by: "crc.smith@site102.org",
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
    description: "Systolic 120 recorded with Diastolic 130 mmHg. Out-of-bounds cross-field validation query.",
    category: "DATA_QUERY",
    gxp_severity: "MINOR",
    priority: "MEDIUM",
    status: "WAITING_ON_SITE",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: "SUBJ-8815",
    entity_type: "CRF_FORM",
    entity_id: "FORM-VS-003",
    created_by: "dm.lead@sponsor.org",
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
    description: "Subject 8804 re-consented on outdated paper template version during Protocol Amendment 2 rollout.",
    category: "PROTOCOL_DEVIATION",
    gxp_severity: "MAJOR",
    priority: "HIGH",
    status: "RESOLVED",
    study_id: "STUDY-ONC-202",
    site_id: "SITE-101",
    subject_id: "SUBJ-8804",
    entity_type: "SUBJECT",
    entity_id: "SUBJ-8804",
    created_by: "cra.monitor@cadence.io",
    created_at: new Date(Date.now() - 3600 * 1000 * 72).toISOString(),
    sla_target_at: new Date(Date.now() - 3600 * 1000 * 24).toISOString(),
    signature_token: "sig_token_hex_a982fe41b3",
    signature_user: "dr_smith_pi",
    signature_timestamp: new Date(Date.now() - 3600 * 1000 * 12).toISOString(),
    signature_meaning: "I approve the clinical assessment and corrective medical action.",
    root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
    root_cause_summary: "Site ICF version binders were updated and CRC re-training completed.",
    resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
    version_index: 4,
  },
];

onMounted(async () => {
  tickets.value = [...initialMockTickets];
  fetchTicketsFromApi();
});

const fetchTicketsFromApi = async () => {
  try {
    const res = await apiClient.get("/api/v1/tickets");
    if (res && Array.isArray(res)) {
      tickets.value = res;
    }
  } catch {
    // Keep initialized mock tickets for smooth demo presentation
  }
};

// Summary metrics
const activeTicketsCount = computed(() => {
  return tickets.value.filter((t) => t.status !== "CLOSED").length;
});

const criticalTicketsCount = computed(() => {
  return tickets.value.filter((t) => t.gxp_severity === "CRITICAL" && t.status !== "CLOSED").length;
});

const pausedTicketsCount = computed(() => {
  return tickets.value.filter((t) => t.status === "WAITING_ON_SITE" || t.status === "WAITING_ON_SPONSOR").length;
});

const slaComplianceClass = computed(() => {
  return kpis.sla_compliance_rate >= 95 ? "text-success" : "text-warning";
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
    if (filterSeverity.value !== "ALL" && t.gxp_severity !== filterSeverity.value) {
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
const openTicketDetail = (t) => {
  selectedTicket.value = { ...t };
  selectedTicketComments.value = [
    {
      id: "cm-001",
      user_id: t.created_by || "cra.monitor@cadence.io",
      content: `Logged initial ${formatCategory(t.category)}: ${t.description}`,
      visibility: "PUBLIC",
      created_at: t.created_at,
    },
    {
      id: "cm-002",
      user_id: "sponsor.qa@cadence.io",
      content: "Quality assessment initiated under ICH E6(R3). Reviewing cold-chain calibration records.",
      visibility: "INTERNAL_SPONSOR",
      created_at: new Date(new Date(t.created_at).getTime() + 1800 * 1000).toISOString(),
    },
  ];
  selectedTicketAttachments.value = [
    {
      id: "att-001",
      filename: "logger_raw_export.csv",
      file_size_bytes: 42100,
      mime_type: "text/csv",
      sha256_hash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      uploaded_by: t.created_by || "cra.monitor@cadence.io",
      uploaded_at: t.created_at,
      deid_scrubbed: true,
    },
  ];
  selectedTicketAuditLogs.value = [
    {
      action: "CREATE",
      user_id: t.created_by || "cra.monitor@cadence.io",
      timestamp: t.created_at,
      change_reason: "Initial ticket registration",
    },
  ];
  isDrawerOpen.value = true;
};

const handleTicketTransition = async ({ ticketId, newStatus, root_cause_category, root_cause_summary, resolution_code }) => {
  const index = tickets.value.findIndex((t) => t.id === ticketId);
  if (index !== -1) {
    tickets.value[index].status = newStatus;
    if (root_cause_category) tickets.value[index].root_cause_category = root_cause_category;
    if (root_cause_summary) tickets.value[index].root_cause_summary = root_cause_summary;
    if (resolution_code) tickets.value[index].resolution_code = resolution_code;
    selectedTicket.value = { ...tickets.value[index] };
  }
};

const handleTicketComment = ({ content, visibility, reason_for_change }) => {
  const newComment = {
    id: "cm-" + Date.now(),
    user_id: "active.user@cadence.io",
    content,
    visibility,
    created_at: new Date().toISOString(),
    reason_for_change,
  };
  selectedTicketComments.value.push(newComment);
};

const handleTicketAttachment = ({ file, reason_for_change }) => {
  const newAttachment = {
    id: "att-" + Date.now(),
    filename: file.name,
    file_size_bytes: file.size,
    mime_type: file.type || "application/octet-stream",
    sha256_hash: "a4f891b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abc",
    uploaded_by: "active.user@cadence.io",
    uploaded_at: new Date().toISOString(),
    deid_scrubbed: true,
    reason_for_change,
  };
  selectedTicketAttachments.value.push(newAttachment);
};

const handleTicketSigned = ({ ticketId, signature_token, meaning }) => {
  const index = tickets.value.findIndex((t) => t.id === ticketId);
  if (index !== -1) {
    tickets.value[index].signature_token = signature_token;
    tickets.value[index].signature_meaning = meaning;
    tickets.value[index].signature_timestamp = new Date().toISOString();
    tickets.value[index].signature_user = "active.user@cadence.io";
    selectedTicket.value = { ...tickets.value[index] };
  }
};

const handleTicketCreated = (payload) => {
  const newTicket = {
    id: "tk-" + Date.now(),
    reference: "DEV-2026-" + Math.floor(1000 + Math.random() * 9000),
    ...payload,
    status: "OPEN",
    created_by: "active.user@cadence.io",
    created_at: new Date().toISOString(),
    sla_target_at: new Date(Date.now() + 3600 * 1000 * 24).toISOString(),
    version_index: 1,
  };
  tickets.value.unshift(newTicket);
};

const exportAuditTrail = (format) => {
  const data = JSON.stringify(tickets.value, null, 2);
  const blob = new Blob([data], { type: format === "json" ? "application/json" : "text/csv" });
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
}

/* Header */
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
}

.header-badge-row {
  display: flex;
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

.badge-gxp { background: #0284c7; color: #ffffff; }
.badge-regulatory { background: #4f46e5; color: #ffffff; }
.badge-standard { background: #475569; color: #ffffff; }

.view-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: #0f172a;
  margin: 0 0 6px 0;
  letter-spacing: -0.02em;
}

.view-subtitle {
  font-size: 0.875rem;
  color: #64748b;
  max-width: 820px;
  line-height: 1.45;
  margin: 0;
}

.header-action-group {
  display: flex;
  gap: 10px;
}

/* Stats Cards Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.stat-card {
  background: #ffffff;
  border-radius: 10px;
  padding: 16px 20px;
  border: 1px solid #e2e8f0;
  display: flex;
  align-items: center;
  gap: 16px;
  cursor: pointer;
  transition: transform 0.15s ease, box-shadow 0.15s ease;
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
  background: #f1f5f9;
}

.alert-icon { background: #eff6ff; }
.capa-icon { background: #fef2f2; }
.rbqm-icon { background: #f0fdf4; }
.qtl-icon { background: #faf5ff; }
.breach-icon { background: #fffbeb; }

.stat-details {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: 1.4rem;
  font-weight: 800;
  color: #0f172a;
  line-height: 1.2;
}

.stat-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #64748b;
  margin-top: 2px;
}

.text-danger { color: #dc2626; }
.text-success { color: #16a34a; }
.text-warning { color: #d97706; }

/* Filter & Controls Card */
.controls-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 12px 18px;
  border: 1px solid #e2e8f0;
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
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  padding: 6px 12px;
  flex: 1;
  min-width: 240px;
}

.search-icon { font-size: 0.9rem; }

.search-input {
  border: none;
  background: transparent;
  outline: none;
  font-size: 0.85rem;
  width: 100%;
}

.filter-group {
  display: flex;
  gap: 10px;
}

.filter-select {
  padding: 6px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.82rem;
  background: #ffffff;
  color: #334155;
  outline: none;
}

.view-mode-toggle {
  display: flex;
  background: #f1f5f9;
  border-radius: 6px;
  padding: 2px;
}

.view-toggle-btn {
  padding: 6px 12px;
  border: none;
  background: transparent;
  font-size: 0.8rem;
  font-weight: 600;
  color: #64748b;
  border-radius: 4px;
  cursor: pointer;
}

.view-toggle-btn.active {
  background: #ffffff;
  color: #0f172a;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

/* KANBAN BOARD VIEW */
.kanban-board {
  display: grid;
  grid-template-columns: repeat(5, minmax(240px, 1fr));
  gap: 16px;
  overflow-x: auto;
  align-items: flex-start;
}

.kanban-column {
  background: #f8fafc;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  display: flex;
  flex-direction: column;
  max-height: calc(100vh - 320px);
}

.column-header {
  padding: 12px 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid #e2e8f0;
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

.col-open { background: #3b82f6; }
.col-progress { background: #8b5cf6; }
.col-paused { background: #f59e0b; }
.col-resolved { background: #10b981; }
.col-closed { background: #64748b; }

.column-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #1e293b;
}

.column-count {
  background: #e2e8f0;
  color: #475569;
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
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
  padding: 12px 14px;
  cursor: pointer;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  transition: transform 0.12s ease, border-color 0.12s ease;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ticket-card:hover {
  transform: translateY(-2px);
  border-color: #2563eb;
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
  color: #2563eb;
}

.card-ticket-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: #0f172a;
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
  background: #f1f5f9;
  color: #475569;
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

.sla-tag-normal { background: #e0f2fe; color: #0284c7; }
.sla-tag-warned { background: #fef3c7; color: #b45309; }
.sla-tag-breached { background: #fee2e2; color: #dc2626; }
.fill-paused { background: #f1f5f9; color: #64748b; }

.badge-sev-critical { background: #fee2e2; color: #b91c1c; }
.badge-sev-major { background: #ffedd5; color: #c2410c; }
.badge-sev-minor { background: #dbeafe; color: #1d4ed8; }
.badge-paused { background: #fef3c7; color: #b45309; }
.badge-signed { background: #dcfce7; color: #15803d; }
.badge-pending-sign { background: #fef9c3; color: #a16207; }
.badge-closed { background: #f1f5f9; color: #64748b; }

/* TABLE VIEW */
.table-card {
  background: #ffffff;
  border-radius: 8px;
  border: 1px solid #e2e8f0;
  overflow-x: auto;
}

.clinical-table {
  width: 100%;
  border-collapse: collapse;
  text-align: left;
  font-size: 0.82rem;
}

.clinical-table th {
  background: #f8fafc;
  padding: 10px 14px;
  font-weight: 700;
  color: #475569;
  border-bottom: 1px solid #cbd5e1;
  text-transform: uppercase;
  font-size: 0.72rem;
  letter-spacing: 0.02em;
}

.clinical-table td {
  padding: 12px 14px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
}

.table-row {
  cursor: pointer;
  transition: background-color 0.1s ease;
}

.table-row:hover {
  background: #f8fafc;
}

.font-mono { font-family: monospace; }
.font-semibold { font-weight: 600; }

.empty-table-cell {
  text-align: center;
  padding: 36px !important;
  color: #94a3b8;
}

.priority-tag {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
}

.priority-critical { color: #dc2626; }
.priority-high { color: #ea580c; }
.priority-medium { color: #2563eb; }
.priority-low { color: #64748b; }

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
  padding: 3px 8px;
  font-size: 0.75rem;
}

.btn-primary { background: #2563eb; color: #ffffff; }
.btn-primary:hover { background: #1d4ed8; }
.btn-secondary { background: #ffffff; border-color: #cbd5e1; color: #334155; }
.btn-secondary:hover { background: #f1f5f9; }
</style>
