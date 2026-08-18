<template>
  <div v-if="isOpen" class="drawer-backdrop" @click.self="close">
    <div
      class="drawer-panel"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-ticket-title"
    >
      <!-- Drawer Header -->
      <div class="drawer-header">
        <div class="drawer-header-meta">
          <span class="ref-badge">{{
            ticket.reference || `#${ticket.id?.substring(0, 8)}`
          }}</span>
          <span
            class="badge"
            :class="`badge-cat-${ticket.category?.toLowerCase()}`"
          >
            {{ formatCategory(ticket.category) }}
          </span>
          <span
            class="badge"
            :class="`badge-sev-${ticket.gxp_severity?.toLowerCase()}`"
          >
            {{ ticket.gxp_severity }}
          </span>
          <span
            class="badge"
            :class="`badge-status-${ticket.status?.toLowerCase()}`"
          >
            {{ formatStatus(ticket.status) }}
          </span>
          <span v-if="ticket.signature_token" class="badge badge-signed">
            ✍️ Part 11 Signed
          </span>
        </div>

        <h2 id="drawer-ticket-title" class="drawer-title">
          {{ ticket.title }}
        </h2>

        <div
          v-if="ticket.sla_target_at"
          class="sla-banner"
          :class="slaBannerClass"
        >
          <div class="sla-banner-left">
            <span class="sla-icon">{{ slaIcon }}</span>
            <span class="sla-text">{{ slaStatusText }}</span>
          </div>
          <div class="sla-progress-track">
            <div
              class="sla-progress-fill"
              :style="{ width: `${Math.min(slaElapsedPercent, 100)}%` }"
              :class="slaProgressClass"
            ></div>
          </div>
        </div>

        <button
          type="button"
          class="drawer-close"
          aria-label="Close"
          @click="close"
        >
          ✕
        </button>
      </div>

      <!-- Action Bar -->
      <div class="drawer-actions">
        <div class="action-btn-group">
          <button
            v-if="ticket.status === 'OPEN'"
            class="btn btn-sm btn-primary"
            :disabled="actionLoading"
            @click="transitionTicket('IN_PROGRESS')"
          >
            ▶ Start Work
          </button>
          <button
            v-if="ticket.status === 'IN_PROGRESS'"
            class="btn btn-sm btn-warning"
            :disabled="actionLoading"
            @click="transitionTicket('WAITING_ON_SITE')"
          >
            ⏸ Pause (Wait Site)
          </button>
          <button
            v-if="ticket.status === 'IN_PROGRESS'"
            class="btn btn-sm btn-warning"
            :disabled="actionLoading"
            @click="transitionTicket('WAITING_ON_SPONSOR')"
          >
            ⏸ Pause (Wait Sponsor)
          </button>
          <button
            v-if="
              ticket.status === 'WAITING_ON_SITE' ||
              ticket.status === 'WAITING_ON_SPONSOR'
            "
            class="btn btn-sm btn-primary"
            :disabled="actionLoading"
            @click="transitionTicket('IN_PROGRESS')"
          >
            ▶ Resume Work
          </button>
          <button
            v-if="
              ticket.status === 'IN_PROGRESS' ||
              ticket.status === 'WAITING_ON_SITE' ||
              ticket.status === 'WAITING_ON_SPONSOR'
            "
            class="btn btn-sm btn-success"
            :disabled="actionLoading"
            @click="openResolveModal"
          >
            ✓ Resolve Issue
          </button>
          <button
            v-if="ticket.status === 'RESOLVED'"
            class="btn btn-sm btn-secondary"
            :disabled="actionLoading"
            @click="transitionTicket('CLOSED')"
          >
            🔒 Final Close
          </button>
        </div>

        <button
          v-if="
            !ticket.signature_token &&
            (ticket.gxp_severity === 'CRITICAL' ||
              ticket.gxp_severity === 'MAJOR' ||
              ticket.status === 'RESOLVED')
          "
          class="btn btn-sm btn-sign"
          @click="isSignModalOpen = true"
        >
          ✍️ Part 11 eSign
        </button>
      </div>

      <!-- Drawer Tabs -->
      <div class="drawer-tabs">
        <button
          class="tab-item"
          :class="{ active: activeTab === 'overview' }"
          @click="activeTab = 'overview'"
        >
          📋 Overview &amp; Context
        </button>
        <button
          class="tab-item"
          :class="{ active: activeTab === 'discussion' }"
          @click="activeTab = 'discussion'"
        >
          💬 Discussion ({{ comments.length }})
        </button>
        <button
          class="tab-item"
          :class="{ active: activeTab === 'attachments' }"
          @click="activeTab = 'attachments'"
        >
          📎 Evidence ({{ attachments.length }})
        </button>
        <button
          class="tab-item"
          :class="{ active: activeTab === 'signatures' }"
          @click="activeTab = 'signatures'"
        >
          ✍️ eSignatures
        </button>
        <button
          class="tab-item"
          :class="{ active: activeTab === 'audit' }"
          @click="activeTab = 'audit'"
        >
          🔒 Audit Trail
        </button>
      </div>

      <!-- Drawer Content -->
      <div class="drawer-body">
        <!-- TAB 1: OVERVIEW -->
        <div v-if="activeTab === 'overview'" class="tab-pane">
          <div class="section-card">
            <h4 class="card-title">Clinical Narrative</h4>
            <p class="narrative-text">{{ ticket.description }}</p>
          </div>

          <div class="section-card">
            <h4 class="card-title">Trial &amp; Site Context</h4>
            <div class="meta-grid">
              <div class="meta-item">
                <span class="meta-label">Study ID</span>
                <span class="meta-value">{{ ticket.study_id || "N/A" }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Site ID</span>
                <span class="meta-value">{{ ticket.site_id || "N/A" }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Subject ID</span>
                <span class="meta-value">{{ ticket.subject_id || "N/A" }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Priority</span>
                <span
                  class="meta-value priority-tag"
                  :class="`priority-${ticket.priority?.toLowerCase()}`"
                >
                  {{ ticket.priority }}
                </span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Created By</span>
                <span class="meta-value">{{ ticket.created_by }}</span>
              </div>
              <div class="meta-item">
                <span class="meta-label">Created At</span>
                <span class="meta-value">{{
                  formatDate(ticket.created_at)
                }}</span>
              </div>
            </div>
          </div>

          <div v-if="ticket.entity_type" class="section-card">
            <h4 class="card-title">Cross-App Entity Linkage</h4>
            <div class="entity-badge-box">
              <span class="entity-type-badge">{{ ticket.entity_type }}</span>
              <span class="entity-id-value">{{ ticket.entity_id }}</span>
            </div>
          </div>

          <div
            v-if="ticket.root_cause_category || ticket.root_cause_summary"
            class="section-card rca-card"
          >
            <h4 class="card-title">Root Cause Analysis (5-Whys)</h4>
            <div class="rca-category-pill">
              Category: {{ formatRCA(ticket.root_cause_category) }}
            </div>
            <div v-if="ticket.resolution_code" class="rca-category-pill">
              Resolution: {{ ticket.resolution_code }}
            </div>
            <p class="rca-summary">{{ ticket.root_cause_summary }}</p>
          </div>

          <div
            v-if="
              ticket.context_payload &&
              Object.keys(ticket.context_payload).length > 0
            "
            class="section-card"
          >
            <h4 class="card-title">Structured Ingestion Payload</h4>
            <pre class="json-viewer">{{
              JSON.stringify(ticket.context_payload, null, 2)
            }}</pre>
          </div>
        </div>

        <!-- TAB 2: DISCUSSION -->
        <div v-if="activeTab === 'discussion'" class="tab-pane">
          <div class="comments-filter-bar">
            <button
              class="filter-chip"
              :class="{ active: commentFilter === 'ALL' }"
              @click="commentFilter = 'ALL'"
            >
              All Notes
            </button>
            <button
              class="filter-chip"
              :class="{ active: commentFilter === 'PUBLIC' }"
              @click="commentFilter = 'PUBLIC'"
            >
              🌐 Public (Site &amp; Sponsor)
            </button>
            <button
              class="filter-chip"
              :class="{ active: commentFilter === 'INTERNAL_SPONSOR' }"
              @click="commentFilter = 'INTERNAL_SPONSOR'"
            >
              🔒 Internal Sponsor Only
            </button>
          </div>

          <div class="comment-stream">
            <div
              v-for="c in filteredComments"
              :key="c.id"
              class="comment-card"
              :class="{
                'comment-internal': c.visibility === 'INTERNAL_SPONSOR',
              }"
            >
              <div class="comment-header">
                <span class="comment-author">{{ c.user_id }}</span>
                <span
                  class="badge"
                  :class="
                    c.visibility === 'INTERNAL_SPONSOR'
                      ? 'badge-internal'
                      : 'badge-public'
                  "
                >
                  {{
                    c.visibility === "INTERNAL_SPONSOR"
                      ? "🔒 Internal Note"
                      : "🌐 Public"
                  }}
                </span>
                <span class="comment-time">{{ formatDate(c.created_at) }}</span>
              </div>
              <div class="comment-body">{{ c.content }}</div>
            </div>

            <div v-if="filteredComments.length === 0" class="empty-placeholder">
              No comments match the selected visibility filter.
            </div>
          </div>

          <div class="new-comment-box">
            <div class="visibility-selector">
              <label class="radio-label">
                <input
                  v-model="newCommentVisibility"
                  type="radio"
                  value="PUBLIC"
                />
                <span>🌐 Public (Site Visible)</span>
              </label>
              <label class="radio-label">
                <input
                  v-model="newCommentVisibility"
                  type="radio"
                  value="INTERNAL_SPONSOR"
                />
                <span>🔒 Internal Sponsor Note</span>
              </label>
            </div>
            <textarea
              v-model="newCommentContent"
              rows="3"
              class="form-control"
              placeholder="Add a clinical note, investigator response, or internal audit review..."
            ></textarea>
            <div class="comment-submit-row">
              <input
                v-model="newCommentReason"
                type="text"
                class="form-control flex-1"
                placeholder="Reason for change (GxP justification)..."
              />
              <button
                class="btn btn-primary"
                :disabled="
                  !newCommentContent.trim() ||
                  !newCommentReason.trim() ||
                  commentSubmitting
                "
                @click="submitComment"
              >
                Post Note
              </button>
            </div>
          </div>
        </div>

        <!-- TAB 3: ATTACHMENTS -->
        <div v-if="activeTab === 'attachments'" class="tab-pane">
          <div class="attachment-upload-box">
            <h4 class="card-title">Upload Audited Evidence Blob</h4>
            <p class="upload-hint">
              Upload logs, temperature traces, source certificates, or PDFs (21
              CFR Part 11 &amp; DEID scanned).
            </p>
            <div class="upload-inputs">
              <input
                ref="fileInput"
                type="file"
                class="file-input"
                @change="onFileSelected"
              />
              <input
                v-model="attachmentReason"
                type="text"
                class="form-control"
                placeholder="Mandatory GxP reason for attachment..."
              />
              <button
                class="btn btn-primary"
                :disabled="
                  !selectedFile || !attachmentReason.trim() || uploadSubmitting
                "
                @click="uploadAttachment"
              >
                📎 Attach Evidence
              </button>
            </div>
          </div>

          <div class="attachments-list">
            <div
              v-for="att in attachments"
              :key="att.id"
              class="attachment-card"
            >
              <div class="attachment-icon">📄</div>
              <div class="attachment-details">
                <div class="attachment-filename">{{ att.filename }}</div>
                <div class="attachment-meta">
                  <span>{{ (att.file_size_bytes / 1024).toFixed(1) }} KB</span>
                  • <span>Uploaded by {{ att.uploaded_by }}</span> •
                  <span>{{ formatDate(att.uploaded_at) }}</span>
                </div>
                <div class="sha-hash" :title="att.sha256_hash">
                  SHA-256:
                  <code>{{ att.sha256_hash?.substring(0, 16) }}...</code>
                </div>
              </div>
              <div class="attachment-badges">
                <span v-if="att.deid_scrubbed" class="badge badge-deid"
                  >🛡️ DEID Verified</span
                >
              </div>
            </div>

            <div v-if="attachments.length === 0" class="empty-placeholder">
              No evidence files attached to this ticket.
            </div>
          </div>
        </div>

        <!-- TAB 4: SIGNATURES -->
        <div v-if="activeTab === 'signatures'" class="tab-pane">
          <div v-if="ticket.signature_token" class="signature-record-card">
            <div class="sig-card-header">
              <span class="sig-seal-icon">🛡️</span>
              <div>
                <h4 class="sig-card-title">
                  Authoritative 21 CFR Part 11 Electronic Signature
                </h4>
                <p class="sig-card-subtitle">
                  Legally binding sign-off recorded in tamper-evident ledger
                </p>
              </div>
            </div>
            <div class="sig-details-grid">
              <div class="sig-item">
                <span class="sig-label">Signer Identity:</span>
                <span class="sig-val">{{ ticket.signature_user }}</span>
              </div>
              <div class="sig-item">
                <span class="sig-label">Timestamp (UTC):</span>
                <span class="sig-val">{{
                  formatDate(ticket.signature_timestamp)
                }}</span>
              </div>
              <div class="sig-item full-width">
                <span class="sig-label">Signature Meaning:</span>
                <span class="sig-val meaning-val">{{
                  ticket.signature_meaning
                }}</span>
              </div>
              <div class="sig-item full-width">
                <span class="sig-label">Cryptographic Token:</span>
                <span class="sig-val token-val"
                  ><code>{{ ticket.signature_token }}</code></span
                >
              </div>
            </div>
          </div>

          <div v-else class="empty-sig-box">
            <p>
              No 21 CFR Part 11 electronic signature has been captured for this
              ticket.
            </p>
            <button
              class="btn btn-primary btn-sign-action"
              @click="isSignModalOpen = true"
            >
              ✍️ Sign &amp; Authorize Ticket
            </button>
          </div>
        </div>

        <!-- TAB 5: AUDIT TRAIL -->
        <div v-if="activeTab === 'audit'" class="tab-pane">
          <div class="audit-timeline">
            <div v-for="(log, idx) in auditLogs" :key="idx" class="audit-row">
              <div class="audit-bullet"></div>
              <div class="audit-content">
                <div class="audit-row-header">
                  <span class="audit-action">{{ log.action }}</span>
                  <span class="audit-user">{{ log.user_id }}</span>
                  <span class="audit-time">{{
                    formatDate(log.timestamp)
                  }}</span>
                </div>
                <div class="audit-reason">Reason: {{ log.change_reason }}</div>
              </div>
            </div>

            <div v-if="auditLogs.length === 0" class="empty-placeholder">
              Audit logs loading or empty.
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Sign Modal -->
    <TicketSignModal
      :is-open="isSignModalOpen"
      :ticket="ticket"
      @close="isSignModalOpen = false"
      @signed="onTicketSigned"
    />
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import TicketSignModal from "./TicketSignModal.vue";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  ticket: {
    type: Object,
    default: () => ({}),
  },
  comments: {
    type: Array,
    default: () => [],
  },
  attachments: {
    type: Array,
    default: () => [],
  },
  auditLogs: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits([
  "close",
  "transition",
  "comment",
  "attachment",
  "signed",
]);

const activeTab = ref("overview");
const actionLoading = ref(false);
const isSignModalOpen = ref(false);

// Comment state
const commentFilter = ref("ALL");
const newCommentContent = ref("");
const newCommentReason = ref("Standard clinical progress note");
const newCommentVisibility = ref("PUBLIC");
const commentSubmitting = ref(false);

// Attachment state
const fileInput = ref(null);
const selectedFile = ref(null);
const attachmentReason = ref("Supporting regulatory evidence attachment");
const uploadSubmitting = ref(false);

const close = () => {
  emit("close");
};

// SLA calculations
const slaElapsedPercent = computed(() => {
  if (!props.ticket?.sla_target_at || !props.ticket?.created_at) return 0;
  const start = new Date(props.ticket.created_at).getTime();
  const end = new Date(props.ticket.sla_target_at).getTime();
  const total = end - start;
  if (total <= 0) return 100;
  const now = Date.now();
  const elapsed =
    now - start - (props.ticket.sla_total_paused_seconds || 0) * 1000;
  return Math.max(0, Math.min(100, (elapsed / total) * 100));
});

const slaBannerClass = computed(() => {
  if (props.ticket.sla_breached) return "sla-breached";
  if (props.ticket.sla_amber_warned || slaElapsedPercent.value >= 75)
    return "sla-warning";
  if (props.ticket.status?.startsWith("WAITING_")) return "sla-paused";
  return "sla-normal";
});

const slaProgressClass = computed(() => {
  if (props.ticket.sla_breached) return "fill-breached";
  if (props.ticket.sla_amber_warned || slaElapsedPercent.value >= 75)
    return "fill-warning";
  if (props.ticket.status?.startsWith("WAITING_")) return "fill-paused";
  return "fill-normal";
});

const slaIcon = computed(() => {
  if (props.ticket.sla_breached) return "🚨";
  if (props.ticket.sla_amber_warned || slaElapsedPercent.value >= 75)
    return "⚠️";
  if (props.ticket.status?.startsWith("WAITING_")) return "⏸";
  return "⏱";
});

const slaStatusText = computed(() => {
  if (props.ticket.sla_breached)
    return "SLA Breached - Escalated to Lead CRA & QA";
  if (props.ticket.status?.startsWith("WAITING_"))
    return "SLA Paused (Waiting on Response)";
  if (props.ticket.sla_amber_warned || slaElapsedPercent.value >= 75)
    return "SLA Warning (>75% Elapsed)";
  return "SLA Active & Compliant";
});

// Formatters
const formatCategory = (cat) => {
  return cat?.replace(/_/g, " ") || "GENERAL";
};

const formatStatus = (st) => {
  return st?.replace(/_/g, " ") || "OPEN";
};

const formatRCA = (rca) => {
  return rca?.replace(/_/g, " ") || "Unclassified";
};

const formatDate = (d) => {
  if (!d) return "N/A";
  try {
    return new Date(d).toLocaleString();
  } catch {
    return String(d);
  }
};

const filteredComments = computed(() => {
  if (commentFilter.value === "ALL") return props.comments;
  return props.comments.filter((c) => c.visibility === commentFilter.value);
});

const transitionTicket = (newStatus) => {
  emit("transition", {
    ticketId: props.ticket.id,
    newStatus,
    reason_for_change: `Status transition to ${newStatus}`,
  });
};

const openResolveModal = () => {
  emit("transition", {
    ticketId: props.ticket.id,
    newStatus: "RESOLVED",
    root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
    root_cause_summary:
      "Clinical team addressed protocol checklist with site coordinator.",
    resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
    reason_for_change:
      "Resolving ticket following comprehensive clinical assessment.",
  });
};

const submitComment = () => {
  if (!newCommentContent.value.trim() || !newCommentReason.value.trim()) return;
  emit("comment", {
    ticketId: props.ticket.id,
    content: newCommentContent.value,
    visibility: newCommentVisibility.value,
    reason_for_change: newCommentReason.value,
  });
  newCommentContent.value = "";
};

const onFileSelected = (e) => {
  selectedFile.value = e.target.files?.[0] || null;
};

const uploadAttachment = () => {
  if (!selectedFile.value || !attachmentReason.value.trim()) return;
  emit("attachment", {
    ticketId: props.ticket.id,
    file: selectedFile.value,
    reason_for_change: attachmentReason.value,
  });
  selectedFile.value = null;
  if (fileInput.value) fileInput.value.value = "";
};

const onTicketSigned = (sigPayload) => {
  emit("signed", {
    ticketId: props.ticket.id,
    ...sigPayload,
  });
};
</script>

<style scoped>
.drawer-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(3px);
  display: flex;
  justify-content: flex-end;
  z-index: 999;
}

.drawer-panel {
  width: 100%;
  max-width: 720px;
  height: 100vh;
  background: #ffffff;
  display: flex;
  flex-direction: column;
  box-shadow: -10px 0 25px -5px rgba(0, 0, 0, 0.2);
  border-left: 1px solid #cbd5e1;
  animation: slideLeft 0.25s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes slideLeft {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  padding: 20px 24px;
  background: #0f172a;
  color: #ffffff;
  position: relative;
}

.drawer-header-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.ref-badge {
  font-family: monospace;
  font-size: 0.85rem;
  font-weight: 700;
  background: rgba(255, 255, 255, 0.15);
  padding: 3px 8px;
  border-radius: 4px;
  color: #38bdf8;
}

.badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-sev-critical {
  background: #dc2626;
  color: #ffffff;
}

.badge-sev-major {
  background: #ea580c;
  color: #ffffff;
}

.badge-sev-minor {
  background: #2563eb;
  color: #ffffff;
}

.badge-status-open {
  background: #3b82f6;
  color: #ffffff;
}

.badge-status-in_progress {
  background: #8b5cf6;
  color: #ffffff;
}

.badge-status-waiting_on_site,
.badge-status-waiting_on_sponsor {
  background: #f59e0b;
  color: #0f172a;
}

.badge-status-resolved {
  background: #10b981;
  color: #ffffff;
}

.badge-status-closed {
  background: #64748b;
  color: #ffffff;
}

.badge-signed {
  background: #059669;
  color: #ffffff;
}

.drawer-title {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0 0 12px 0;
  line-height: 1.3;
}

.drawer-close {
  position: absolute;
  top: 16px;
  right: 16px;
  background: none;
  border: none;
  color: #94a3b8;
  font-size: 1.3rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.drawer-close:hover {
  background: rgba(255, 255, 255, 0.1);
  color: #ffffff;
}

.sla-banner {
  padding: 8px 12px;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.sla-banner.sla-normal {
  background: rgba(37, 99, 235, 0.2);
  border: 1px solid rgba(37, 99, 235, 0.4);
}

.sla-banner.sla-warning {
  background: rgba(245, 158, 11, 0.25);
  border: 1px solid rgba(245, 158, 11, 0.5);
}

.sla-banner.sla-breached {
  background: rgba(220, 38, 38, 0.3);
  border: 1px solid rgba(220, 38, 38, 0.6);
}

.sla-banner.sla-paused {
  background: rgba(148, 163, 184, 0.25);
  border: 1px solid rgba(148, 163, 184, 0.4);
}

.sla-banner-left {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.8rem;
  font-weight: 600;
}

.sla-progress-track {
  height: 4px;
  background: rgba(255, 255, 255, 0.2);
  border-radius: 2px;
  overflow: hidden;
}

.sla-progress-fill {
  height: 100%;
  transition: width 0.3s ease;
}

.fill-normal {
  background: #38bdf8;
}
.fill-warning {
  background: #f59e0b;
}
.fill-breached {
  background: #ef4444;
}
.fill-paused {
  background: #94a3b8;
}

.drawer-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 24px;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
}

.action-btn-group {
  display: flex;
  gap: 8px;
}

.drawer-tabs {
  display: flex;
  background: #ffffff;
  border-bottom: 1px solid #e2e8f0;
  padding: 0 16px;
  overflow-x: auto;
}

.tab-item {
  padding: 12px 16px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.85rem;
  font-weight: 600;
  color: #64748b;
  cursor: pointer;
  white-space: nowrap;
}

.tab-item.active {
  color: #2563eb;
  border-bottom-color: #2563eb;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
  background: #f1f5f9;
}

.tab-pane {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.section-card {
  background: #ffffff;
  border-radius: 8px;
  padding: 16px;
  border: 1px solid #e2e8f0;
}

.card-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0 0 10px 0;
}

.narrative-text {
  font-size: 0.875rem;
  color: #334155;
  line-height: 1.5;
  white-space: pre-wrap;
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.meta-label {
  font-size: 0.72rem;
  color: #64748b;
  text-transform: uppercase;
  font-weight: 600;
}

.meta-value {
  font-size: 0.875rem;
  color: #1e293b;
  font-weight: 500;
}

.entity-badge-box {
  display: flex;
  align-items: center;
  gap: 10px;
}

.entity-type-badge {
  background: #e0f2fe;
  color: #0369a1;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 4px 8px;
  border-radius: 4px;
}

.entity-id-value {
  font-family: monospace;
  font-size: 0.875rem;
  font-weight: 600;
  color: #0f172a;
}

.rca-card {
  background: #fffbeb;
  border-color: #fde68a;
}

.rca-category-pill {
  display: inline-block;
  background: #fef3c7;
  color: #92400e;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  margin-bottom: 8px;
}

.json-viewer {
  background: #0f172a;
  color: #38bdf8;
  padding: 12px;
  border-radius: 6px;
  font-size: 0.78rem;
  overflow-x: auto;
}

/* Discussion tab */
.comments-filter-bar {
  display: flex;
  gap: 8px;
}

.filter-chip {
  padding: 5px 10px;
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 20px;
  font-size: 0.78rem;
  font-weight: 600;
  color: #475569;
  cursor: pointer;
}

.filter-chip.active {
  background: #2563eb;
  color: #ffffff;
  border-color: #2563eb;
}

.comment-stream {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.comment-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 14px;
}

.comment-card.comment-internal {
  background: #fffbeb;
  border-color: #fde68a;
  border-left: 4px solid #f59e0b;
}

.comment-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.comment-author {
  font-size: 0.82rem;
  font-weight: 700;
  color: #0f172a;
}

.badge-public {
  background: #e0f2fe;
  color: #0369a1;
}

.badge-internal {
  background: #fef3c7;
  color: #b45309;
}

.comment-time {
  font-size: 0.72rem;
  color: #94a3b8;
  margin-left: auto;
}

.comment-body {
  font-size: 0.85rem;
  color: #334155;
  line-height: 1.45;
}

.new-comment-box {
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.visibility-selector {
  display: flex;
  gap: 16px;
}

.radio-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  color: #334155;
  cursor: pointer;
}

.comment-submit-row {
  display: flex;
  gap: 8px;
}

/* Attachments tab */
.attachment-upload-box {
  background: #ffffff;
  border: 1px dashed #cbd5e1;
  border-radius: 8px;
  padding: 16px;
}

.upload-hint {
  font-size: 0.78rem;
  color: #64748b;
  margin-bottom: 12px;
}

.upload-inputs {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.attachments-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.attachment-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.attachment-icon {
  font-size: 1.5rem;
}

.attachment-details {
  flex: 1;
}

.attachment-filename {
  font-size: 0.85rem;
  font-weight: 700;
  color: #0f172a;
}

.attachment-meta {
  font-size: 0.72rem;
  color: #64748b;
  margin: 2px 0;
}

.sha-hash {
  font-size: 0.7rem;
  color: #94a3b8;
}

.badge-deid {
  background: #dcfce7;
  color: #166534;
}

/* Signatures tab */
.signature-record-card {
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 8px;
  padding: 16px;
}

.sig-card-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 14px;
}

.sig-seal-icon {
  font-size: 1.75rem;
}

.sig-card-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: #166534;
  margin: 0;
}

.sig-card-subtitle {
  font-size: 0.75rem;
  color: #15803d;
  margin: 2px 0 0 0;
}

.sig-details-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 10px;
}

.sig-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.sig-item.full-width {
  grid-column: span 2;
}

.sig-label {
  font-size: 0.72rem;
  font-weight: 600;
  color: #15803d;
  text-transform: uppercase;
}

.sig-val {
  font-size: 0.85rem;
  color: #0f172a;
  font-weight: 500;
}

.meaning-val {
  font-style: italic;
  color: #166534;
}

.token-val {
  font-family: monospace;
  font-size: 0.75rem;
  word-break: break-all;
}

.empty-sig-box {
  background: #ffffff;
  border: 1px dashed #cbd5e1;
  border-radius: 8px;
  padding: 32px;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 14px;
}

/* Audit timeline */
.audit-timeline {
  display: flex;
  flex-direction: column;
  position: relative;
  padding-left: 20px;
}

.audit-timeline::before {
  content: "";
  position: absolute;
  top: 10px;
  left: 6px;
  bottom: 10px;
  width: 2px;
  background: #cbd5e1;
}

.audit-row {
  display: flex;
  position: relative;
  padding-bottom: 16px;
}

.audit-bullet {
  position: absolute;
  left: -20px;
  top: 4px;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: #2563eb;
  border: 2px solid #ffffff;
  box-shadow: 0 0 0 1px #cbd5e1;
}

.audit-content {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 12px;
  width: 100%;
}

.audit-row-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.audit-action {
  font-size: 0.8rem;
  font-weight: 700;
  color: #0f172a;
}

.audit-user {
  font-size: 0.75rem;
  color: #2563eb;
}

.audit-time {
  font-size: 0.72rem;
  color: #94a3b8;
  margin-left: auto;
}

.audit-reason {
  font-size: 0.78rem;
  color: #475569;
}

.empty-placeholder {
  padding: 24px;
  text-align: center;
  color: #94a3b8;
  font-size: 0.85rem;
}

.btn {
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
}

.btn-sm {
  padding: 4px 10px;
  font-size: 0.78rem;
}

.btn-primary {
  background: #2563eb;
  color: #ffffff;
}
.btn-primary:hover:not(:disabled) {
  background: #1d4ed8;
}
.btn-secondary {
  background: #ffffff;
  border-color: #cbd5e1;
  color: #475569;
}
.btn-secondary:hover:not(:disabled) {
  background: #f1f5f9;
  color: #0f172a;
}
.btn-warning {
  background: #f59e0b;
  color: #ffffff;
}
.btn-warning:hover:not(:disabled) {
  background: #d97706;
}
.btn-success {
  background: #10b981;
  color: #ffffff;
}
.btn-success:hover:not(:disabled) {
  background: #059669;
}
.btn-sign {
  background: #059669;
  color: #ffffff;
  font-weight: 700;
}
.btn-sign:hover:not(:disabled) {
  background: #047857;
}
.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.form-control {
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.82rem;
  color: #1e293b;
  background: #ffffff;
  outline: none;
}

.form-control:focus {
  border-color: #2563eb;
}

.flex-1 {
  flex: 1;
}
</style>
