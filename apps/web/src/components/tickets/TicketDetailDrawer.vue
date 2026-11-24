<template>
  <ClinicalDrawer
    ref="clinicalDrawerRef"
    :model-value="isOpen"
    :teleport="false"
    size="lg"
    fallback-focus="#btn-create-ticket"
    overlay-class="drawer-backdrop"
    panel-class="drawer-panel"
    aria-labelledby="drawer-ticket-title"
    @close="close"
  >
    <template #header>
      <div class="drawer-header">
        <div class="drawer-header-title-row">
          <span class="ref-badge">{{
            ticket.reference || `#${ticket.id?.substring(0, 8)}`
          }}</span>
          <h2 id="drawer-ticket-title" class="drawer-title" tabindex="-1">
            {{ ticket.title }}
          </h2>
        </div>

        <button
          id="ticket-drawer-close-btn"
          type="button"
          class="drawer-close"
          aria-label="Close"
          @click="close"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <div class="drawer-header-meta">
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
          <FileSignature :size="12" aria-hidden="true" />
          <span>Part 11 Signed</span>
        </span>
      </div>

      <div
        v-if="ticket.sla_target_at"
        class="sla-banner"
        :class="slaBannerClass"
      >
        <div class="sla-banner-left">
          <component
            :is="slaIconComponent"
            :size="15"
            class="sla-icon"
            aria-hidden="true"
          />
          <span class="sla-text">{{ slaStatusText }}</span>
        </div>
        <div class="sla-progress-track">
          <div
            class="sla-progress-fill"
            :style="{ width: `${Math.min(slaElapsedPercent, 100)}%` }"
            :class="slaProgressClass"
          />
        </div>
      </div>

      <!-- Action Bar -->
      <div class="drawer-actions">
        <div v-if="isSigned" class="signed-lock-notice">
          <Lock :size="14" aria-hidden="true" />
          <span
            >Locked under 21 CFR Part 11 — Ticket record and notes are
            immutable.</span
          >
        </div>
        <div v-else-if="canMutateTicket" class="action-btn-group">
          <button
            v-if="ticket.status === 'OPEN'"
            type="button"
            class="btn btn-sm btn-primary"
            :disabled="actionLoading"
            @click="transitionTicket('IN_PROGRESS')"
          >
            <Play :size="14" aria-hidden="true" />
            <span>Start Work</span>
          </button>
          <button
            v-if="ticket.status === 'IN_PROGRESS'"
            type="button"
            class="btn btn-sm btn-warning"
            :disabled="actionLoading"
            @click="transitionTicket('WAITING_ON_SITE')"
          >
            <Pause :size="14" aria-hidden="true" />
            <span>Pause (Wait Site)</span>
          </button>
          <button
            v-if="ticket.status === 'IN_PROGRESS'"
            type="button"
            class="btn btn-sm btn-warning"
            :disabled="actionLoading"
            @click="transitionTicket('WAITING_ON_SPONSOR')"
          >
            <Pause :size="14" aria-hidden="true" />
            <span>Pause (Wait Sponsor)</span>
          </button>
          <button
            v-if="
              ticket.status === 'WAITING_ON_SITE' ||
              ticket.status === 'WAITING_ON_SPONSOR'
            "
            type="button"
            class="btn btn-sm btn-primary"
            :disabled="actionLoading"
            @click="transitionTicket('IN_PROGRESS')"
          >
            <Play :size="14" aria-hidden="true" />
            <span>Resume Work</span>
          </button>
          <button
            v-if="
              ticket.status === 'IN_PROGRESS' ||
              ticket.status === 'WAITING_ON_SITE' ||
              ticket.status === 'WAITING_ON_SPONSOR'
            "
            id="btn-open-resolve"
            type="button"
            class="btn btn-sm btn-success"
            :disabled="actionLoading"
            @click="openResolveModal"
          >
            <Check :size="14" aria-hidden="true" />
            <span>Resolve Issue</span>
          </button>
          <button
            v-if="ticket.status === 'RESOLVED'"
            type="button"
            class="btn btn-sm btn-secondary"
            :disabled="actionLoading"
            @click="transitionTicket('CLOSED')"
          >
            <Lock :size="14" aria-hidden="true" />
            <span>Final Close</span>
          </button>
          <input
            v-if="ticket.status !== 'CLOSED'"
            id="transition-reason-input"
            v-model="transitionReason"
            type="text"
            aria-label="Reason for workflow status transition"
            class="form-control transition-reason-field"
            placeholder="GxP transition reason..."
          />
        </div>
        <div v-else class="signed-lock-notice">
          <Eye :size="14" aria-hidden="true" />
          <span
            >Read-only view — Status transitions require operational staff
            access.</span
          >
        </div>

        <button
          v-if="canSignTicket"
          id="btn-open-part11-sign"
          type="button"
          class="btn btn-sm btn-sign"
          @click="isSignModalOpen = true"
        >
          <FileSignature :size="14" aria-hidden="true" />
          <span>Part 11 eSign</span>
        </button>
      </div>

      <!-- Inline Root-Cause Resolution Panel (inside .clinical-drawer-body per Req 5) -->
      <div
        v-if="isResolveModalOpen && canMutateTicket && !isSigned"
        class="resolve-panel"
      >
        <h4 class="card-title">Document Root Cause &amp; Resolution</h4>
        <div class="resolve-grid">
          <label class="resolve-field">
            <span class="meta-label">Root-Cause Category</span>
            <select
              id="resolve-rca-category"
              v-model="resolveForm.root_cause_category"
              class="form-control"
            >
              <option value="PROCESS_WORKFLOW_DEFICIT">
                Process / Workflow Deficit
              </option>
              <option value="SITE_TRAINING_GAP">Site Training Gap</option>
              <option value="DATA_ENTRY_ERROR">Data Entry Discrepancy</option>
              <option value="SYSTEM_CONFIGURATION">System Configuration</option>
              <option value="EQUIPMENT_CALIBRATION">
                Equipment / Cold-Chain Calibration
              </option>
            </select>
          </label>
          <label class="resolve-field">
            <span class="meta-label">Resolution Code</span>
            <input
              id="resolve-code"
              v-model="resolveForm.resolution_code"
              type="text"
              class="form-control"
              placeholder="e.g. CORRECTIVE_ACTION_IMPLEMENTED"
            />
          </label>
        </div>
        <label class="resolve-field">
          <span class="meta-label"
            >Root-Cause &amp; Corrective Action Summary *</span
          >
          <textarea
            id="resolve-rca-summary"
            v-model="resolveForm.root_cause_summary"
            rows="2"
            class="form-control"
            placeholder="Describe de-identified root cause and corrective action (no participant PII)..."
          />
        </label>
        <div class="comment-submit-row">
          <input
            id="resolve-change-reason"
            v-model="resolveForm.reason_for_change"
            type="text"
            aria-label="GxP reason for resolving ticket"
            class="form-control flex-1"
            placeholder="Mandatory GxP reason for resolution..."
          />
          <button
            type="button"
            class="btn btn-sm btn-secondary"
            @click="closeResolveModal"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-resolve"
            type="button"
            class="btn btn-sm btn-success"
            :disabled="
              !resolveForm.root_cause_summary.trim() ||
              !resolveForm.reason_for_change.trim() ||
              actionLoading
            "
            @click="submitResolution"
          >
            <Check :size="14" aria-hidden="true" />
            <span>Confirm Resolution</span>
          </button>
        </div>
      </div>

      <!-- Drawer Tabs -->
      <ClinicalTabs
        v-model="activeTab"
        :tabs="drawerTabs"
        variant="underline"
        aria-label="Ticket detail sections"
        id-prefix="ticket-drawer"
        list-class="drawer-tabs"
        tab-class="tab-item"
      />

      <!-- TAB 1: OVERVIEW -->
      <div
        v-if="activeTab === 'overview'"
        id="ticket-drawer-panel-overview"
        role="tabpanel"
        aria-labelledby="ticket-drawer-tab-overview"
        class="tab-pane"
      >
        <div class="section-card">
          <h4 class="card-title">Clinical Narrative</h4>
          <p class="narrative-text">
            {{ ticket.description }}
          </p>
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

        <!-- Grounded Protocol RAG Triage Card -->
        <div v-if="aiTriage" class="section-card ai-triage-card">
          <div class="ai-card-header">
            <div class="ai-card-title-group">
              <Bot :size="17" class="ai-robot-icon" aria-hidden="true" />
              <h4 class="card-title ai-title">Grounded Protocol RAG Triage</h4>
            </div>
            <span
              class="badge"
              :class="
                aiTriage.rag_status === 'DRAFT_AVAILABLE'
                  ? 'badge-ai-grounded'
                  : 'badge-ai-failclosed'
              "
            >
              <Target
                v-if="aiTriage.rag_status === 'DRAFT_AVAILABLE'"
                :size="12"
                aria-hidden="true"
              />
              <AlertTriangle v-else :size="12" aria-hidden="true" />
              <span>{{
                aiTriage.rag_status === "DRAFT_AVAILABLE"
                  ? `${Math.round(aiTriage.faithfulness_score * 100)}% Faithfulness (Grounded)`
                  : "Routed to Human Queue (< 85% Confidence)"
              }}</span>
            </span>
          </div>

          <div
            v-if="aiTriage.rag_status === 'DRAFT_AVAILABLE'"
            class="ai-draft-content"
          >
            <p class="ai-draft-text">
              {{ aiTriage.draft_answer }}
            </p>
            <div
              v-if="aiTriage.citations && aiTriage.citations.length > 0"
              class="citation-badge-list"
            >
              <span class="citation-label">Verified Protocol Citations:</span>
              <button
                v-for="(cit, cIdx) in aiTriage.citations"
                :key="cIdx"
                type="button"
                class="citation-chip-btn"
                title="Open protocol document viewer at referenced page"
                @click="openCitationViewer(cit)"
              >
                <BookOpen :size="13" aria-hidden="true" />
                <span>{{
                  cit.citation_marker ||
                  `Protocol ${cit.protocol_version} §${cit.section_number || "General"} p.${cit.page_number}`
                }}</span>
              </button>
            </div>
            <div class="ai-draft-actions">
              <button
                type="button"
                class="btn btn-sm btn-secondary"
                @click="applyAiDraftToComment"
              >
                <MessageSquare :size="14" aria-hidden="true" />
                <span>Use Draft as Public Comment</span>
              </button>
            </div>
          </div>

          <div v-else class="ai-failclosed-box">
            <p class="ai-failclosed-text">
              {{
                aiTriage.routing_reason ||
                "Inquiry confidence is below the 85% gating threshold. Automatically routed to human Data Manager review."
              }}
            </p>
            <div class="human-routed-badge">
              <User :size="13" aria-hidden="true" />
              <span>Assigned Queue: <strong>Data Manager Review</strong></span>
            </div>
          </div>
        </div>

        <div v-if="relatedEntityType" class="section-card">
          <h4 class="card-title">Cross-App Entity Linkage</h4>
          <div class="entity-badge-box">
            <span class="entity-type-badge">{{ relatedEntityType }}</span>
            <span class="entity-id-value">{{ relatedEntityId }}</span>
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
          <p class="rca-summary">
            {{ ticket.root_cause_summary }}
          </p>
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
      <div
        v-if="activeTab === 'discussion'"
        id="ticket-drawer-panel-discussion"
        role="tabpanel"
        aria-labelledby="ticket-drawer-tab-discussion"
        class="tab-pane"
      >
        <div class="comments-filter-bar">
          <button
            type="button"
            class="filter-chip"
            :class="{ active: commentFilter === 'ALL' }"
            @click="commentFilter = 'ALL'"
          >
            All Notes
          </button>
          <button
            type="button"
            class="filter-chip"
            :class="{ active: commentFilter === 'PUBLIC' }"
            @click="commentFilter = 'PUBLIC'"
          >
            <Globe :size="13" aria-hidden="true" />
            <span>Public (Site &amp; Sponsor)</span>
          </button>
          <button
            v-if="canViewInternalSponsorNotes"
            type="button"
            class="filter-chip"
            :class="{ active: commentFilter === 'INTERNAL_SPONSOR' }"
            @click="commentFilter = 'INTERNAL_SPONSOR'"
          >
            <Lock :size="13" aria-hidden="true" />
            <span>Internal Sponsor Only</span>
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
              <span class="comment-author">{{
                c.created_by || c.user_id
              }}</span>
              <span
                class="badge"
                :class="
                  c.visibility === 'INTERNAL_SPONSOR'
                    ? 'badge-internal'
                    : 'badge-public'
                "
              >
                <Lock
                  v-if="c.visibility === 'INTERNAL_SPONSOR'"
                  :size="11"
                  aria-hidden="true"
                />
                <Globe v-else :size="11" aria-hidden="true" />
                <span>{{
                  c.visibility === "INTERNAL_SPONSOR"
                    ? "Internal Note"
                    : "Public"
                }}</span>
              </span>
              <span class="comment-time">{{ formatDate(c.created_at) }}</span>
            </div>
            <div class="comment-body">
              {{ c.body ?? c.content }}
            </div>
          </div>

          <div v-if="filteredComments.length === 0" class="empty-placeholder">
            No comments match the selected visibility filter.
          </div>
        </div>

        <div v-if="canMutateTicket && !isSigned" class="new-comment-box">
          <div class="visibility-selector">
            <label class="radio-label">
              <input
                v-model="newCommentVisibility"
                type="radio"
                value="PUBLIC"
              />
              <Globe :size="14" aria-hidden="true" />
              <span>Public (Site Visible)</span>
            </label>
            <label v-if="canViewInternalSponsorNotes" class="radio-label">
              <input
                v-model="newCommentVisibility"
                type="radio"
                value="INTERNAL_SPONSOR"
              />
              <Lock :size="14" aria-hidden="true" />
              <span>Internal Sponsor Note</span>
            </label>
          </div>
          <textarea
            v-model="newCommentContent"
            rows="3"
            aria-label="Clinical discussion note"
            class="form-control"
            placeholder="Add a clinical note, investigator response, or internal audit review (no participant PII)..."
          />
          <div class="comment-submit-row">
            <input
              v-model="newCommentReason"
              type="text"
              aria-label="Reason for adding note"
              class="form-control flex-1"
              placeholder="Reason for change (GxP justification)..."
            />
            <button
              id="btn-post-ticket-comment"
              type="button"
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
        <div v-else class="empty-placeholder">
          {{
            isSigned
              ? "Discussion is locked because this ticket has been signed under 21 CFR Part 11."
              : "Read-only auditor view — adding discussion notes is disabled."
          }}
        </div>
      </div>

      <!-- TAB 3: ATTACHMENTS (Demo mode only while blob storage is out of scope) -->
      <div
        v-if="demoBuild && activeTab === 'attachments'"
        id="ticket-drawer-panel-attachments"
        role="tabpanel"
        aria-labelledby="ticket-drawer-tab-attachments"
        class="tab-pane"
      >
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
              aria-label="Evidence attachment file"
              class="file-input"
              @change="onFileSelected"
            />
            <input
              v-model="attachmentReason"
              type="text"
              aria-label="Reason for attaching evidence"
              class="form-control"
              placeholder="Mandatory GxP reason for attachment..."
            />
            <button
              type="button"
              class="btn btn-primary"
              :disabled="
                !selectedFile || !attachmentReason.trim() || uploadSubmitting
              "
              @click="uploadAttachment"
            >
              <Paperclip :size="15" aria-hidden="true" />
              <span>Attach Evidence</span>
            </button>
          </div>
        </div>

        <div class="attachments-list">
          <div v-for="att in attachments" :key="att.id" class="attachment-card">
            <div class="attachment-icon">
              <FileText :size="20" aria-hidden="true" />
            </div>
            <div class="attachment-details">
              <div class="attachment-filename">
                {{ att.filename }}
              </div>
              <div class="attachment-meta">
                <span>{{ (att.file_size_bytes / 1024).toFixed(1) }} KB</span>
                &bull; <span>Uploaded by {{ att.uploaded_by }}</span> &bull;
                <span>{{ formatDate(att.uploaded_at) }}</span>
              </div>
              <div class="sha-hash" :title="att.sha256_hash">
                SHA-256:
                <code>{{ att.sha256_hash?.substring(0, 16) }}...</code>
              </div>
            </div>
            <div class="attachment-badges">
              <span v-if="att.deid_scrubbed" class="badge badge-deid">
                <ShieldCheck :size="12" aria-hidden="true" />
                <span>DEID Verified</span>
              </span>
            </div>
          </div>

          <div v-if="attachments.length === 0" class="empty-placeholder">
            <EmptyState
              title="No Evidence Attached"
              message="No evidence files have been attached to this ticket yet."
            />
          </div>
        </div>
      </div>

      <!-- TAB 4: SIGNATURES -->
      <div
        v-if="activeTab === 'signatures'"
        id="ticket-drawer-panel-signatures"
        role="tabpanel"
        aria-labelledby="ticket-drawer-tab-signatures"
        class="tab-pane"
      >
        <div v-if="ticket.signature_token" class="signature-record-card">
          <div class="sig-card-header">
            <ShieldCheck :size="24" class="sig-seal-icon" aria-hidden="true" />
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
            v-if="canSignTicket"
            type="button"
            class="btn btn-primary btn-sign-action"
            @click="isSignModalOpen = true"
          >
            <FileSignature :size="15" aria-hidden="true" />
            <span>Sign &amp; Authorize Ticket</span>
          </button>
          <p v-else class="sig-prereq-hint">
            {{
              canMutateTicket
                ? "Resolve the clinical issue first to unlock 21 CFR Part 11 closure sign-off."
                : "Read-only auditor view — electronic signature capture requires operational staff access."
            }}
          </p>
        </div>
      </div>

      <!-- TAB 5: AUDIT TRAIL -->
      <div
        v-if="activeTab === 'audit'"
        id="ticket-drawer-panel-audit"
        role="tabpanel"
        aria-labelledby="ticket-drawer-tab-audit"
        class="tab-pane"
      >
        <div class="audit-timeline">
          <template v-if="isAuditLoading">
            <SkeletonLoader type="timeline" :count="3" />
          </template>
          <template v-else-if="auditLogs && auditLogs.length > 0">
            <div v-for="(log, idx) in auditLogs" :key="idx" class="audit-row">
              <div class="audit-bullet" />
              <div class="audit-content">
                <div class="audit-row-header">
                  <span class="audit-action">{{ log.action }}</span>
                  <span class="audit-user">{{ log.user_id }}</span>
                  <span class="audit-time">{{
                    formatDate(log.timestamp)
                  }}</span>
                </div>
                <div class="audit-reason">
                  Reason: {{ log.reason_for_change || log.change_reason }}
                </div>
                <div v-if="log.block_hash" class="audit-hash">
                  Ledger Hash:
                  <code>{{ String(log.block_hash).substring(0, 16) }}...</code>
                </div>
              </div>
            </div>
          </template>
          <template v-else>
            <EmptyState
              title="No Audit Entries Found"
              message="No GxP 21 CFR Part 11 audit trail events have been recorded for this ticket."
            />
          </template>
        </div>
      </div>
    </div>
  </ClinicalDrawer>

  <!-- Sibling Protocol Document Viewer Modal (Req 7) -->
  <ClinicalModal
    :model-value="Boolean(isCitationViewerOpen && activeCitation)"
    :teleport="false"
    size="lg"
    fallback-focus="#ticket-drawer-close-btn"
    :show-close="false"
    backdrop-class="citation-modal-backdrop"
    dialog-class="citation-modal-panel"
    aria-labelledby="citation-viewer-title"
    @close="closeCitationViewer"
  >
    <template #header>
      <div class="citation-modal-header">
        <div class="citation-header-content">
          <div v-if="activeCitation" class="citation-modal-meta">
            <span class="badge badge-signed">21 CFR Part 11 Verified</span>
            <span class="badge badge-public"
              >Protocol {{ activeCitation.protocol_version }}</span
            >
            <span
              v-if="activeCitation.section_number"
              class="badge badge-status-open"
            >
              Section {{ activeCitation.section_number }}
            </span>
            <span class="badge badge-page"
              >Page {{ activeCitation.page_number }}</span
            >
          </div>
          <h3 id="citation-viewer-title" class="citation-modal-title">
            <BookOpen :size="16" aria-hidden="true" />
            <span>{{
              activeCitation?.section_title || "Study Protocol Reference"
            }}</span>
          </h3>
        </div>
        <button
          type="button"
          class="citation-close-btn"
          aria-label="Close"
          @click="closeCitationViewer"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div v-if="activeCitation" class="citation-modal-body">
      <div class="citation-coordinates-box">
        <div class="coord-item">
          <span class="coord-label">Citation Tag:</span>
          <span class="coord-val"
            ><code>{{
              activeCitation.citation_marker ||
              `[Protocol ${activeCitation.protocol_version}, Section ${activeCitation.section_number || "General"}, Page ${activeCitation.page_number}]`
            }}</code></span
          >
        </div>
        <div class="coord-item">
          <span class="coord-label">Status:</span>
          <span class="coord-val text-success">
            <Check :size="14" aria-hidden="true" />
            <span>Approved Protocol Version</span>
          </span>
        </div>
      </div>
      <div class="citation-excerpt-card">
        <h4 class="excerpt-heading">
          Page {{ activeCitation.page_number }} Protocol Excerpt
        </h4>
        <div class="excerpt-body">
          {{
            activeCitation.chunk_text ||
            "Referenced protocol section content verified for clinical compliance."
          }}
        </div>
      </div>
    </div>

    <template #footer>
      <div class="citation-modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          @click="closeCitationViewer"
        >
          Close Preview
        </button>
      </div>
    </template>
  </ClinicalModal>

  <!-- Sign Modal -->
  <TicketSignModal
    :is-open="isSignModalOpen"
    :ticket="ticket"
    fallback-focus="#ticket-drawer-close-btn"
    @close="isSignModalOpen = false"
    @signed="onTicketSigned"
  />
</template>

<script setup>
import { ref, reactive, computed, watch } from "vue";
import {
  ClinicalDrawer,
  ClinicalModal,
  ClinicalTabs,
  EmptyState,
  handoffFocus,
  useInlineFocusHandoff,
} from "ui";
import {
  AlertTriangle,
  BookOpen,
  Bot,
  Check,
  Clock,
  Eye,
  FileSignature,
  FileText,
  Globe,
  Lock,
  MessageSquare,
  Paperclip,
  Pause,
  Play,
  ShieldCheck,
  Siren,
  Target,
  User,
  X,
} from "lucide-vue-next";
import TicketSignModal from "./TicketSignModal.vue";
import SkeletonLoader from "../common/SkeletonLoader.vue";
import { useAuthStore, normalizeRole } from "../../stores/auth";
import { isDemoBuild } from "../../utils/demoMode.js";
import { useTicketMutationPermission } from "../../utils/ticketPermissions";

const SITE_PERSONAS = new Set(["site_crc", "site_investigator"]);
const SPONSOR_ROLES = new Set([
  "sponsor_admin",
  "sponsor_designer",
  "data_manager",
  "cra",
  "monitor",
  "safety_reviewer",
  "safety_officer",
  "sponsor_mm",
  "auditor",
  "tmf_auditor",
]);
const SITE_ONLY_ROLES = new Set([
  "crc",
  "site_investigator",
  "principal_investigator",
  "investigator",
]);

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
  isAuditLoading: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "close",
  "transition",
  "comment",
  "attachment",
  "signed",
]);

const clinicalDrawerRef = ref(null);
const drawerRef = computed(() => clinicalDrawerRef.value?.drawerRef ?? null);
const demoBuild = isDemoBuild();
const authStore = useAuthStore();
const activeTab = ref("overview");
const actionLoading = ref(false);
const isSignModalOpen = ref(false);
const isResolveModalOpen = ref(false);
const transitionReason = ref("Advancing clinical issue workflow state");
const resolveForm = reactive({
  root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
  resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
  root_cause_summary:
    "Clinical team addressed protocol checklist with site coordinator.",
  reason_for_change:
    "Resolving ticket following comprehensive clinical assessment.",
});

const normalizedCallerRoles = computed(() =>
  (authStore.rawRoles || []).map(normalizeRole).filter(Boolean)
);

const canViewInternalSponsorNotes = computed(() => {
  if (authStore.currentPersona && SITE_PERSONAS.has(authStore.currentPersona)) {
    return false;
  }
  const roles = normalizedCallerRoles.value;
  if (
    roles.length > 0 &&
    !roles.some((r) => SPONSOR_ROLES.has(r)) &&
    roles.some((r) => SITE_ONLY_ROLES.has(r))
  ) {
    return false;
  }
  return true;
});

const { canMutateTicket } = useTicketMutationPermission(authStore);

const isSigned = computed(() => Boolean(props.ticket?.signature_token));

const canSignTicket = computed(() => {
  if (isSigned.value || !canMutateTicket.value) return false;
  if (demoBuild) {
    return (
      props.ticket?.gxp_severity === "CRITICAL" ||
      props.ticket?.gxp_severity === "MAJOR" ||
      props.ticket?.status === "RESOLVED" ||
      props.ticket?.status === "CLOSED"
    );
  }
  return (
    props.ticket?.status === "RESOLVED" || props.ticket?.status === "CLOSED"
  );
});

const relatedEntityType = computed(
  () => props.ticket?.related_entity_type || props.ticket?.entity_type || ""
);
const relatedEntityId = computed(
  () => props.ticket?.related_entity_id || props.ticket?.entity_id || ""
);

// AI RAG Triage and Citation State
const activeCitation = ref(null);
const isCitationViewerOpen = ref(false);

const aiTriage = computed(() => {
  if (!props.ticket?.context_payload) return null;
  try {
    const payload =
      typeof props.ticket.context_payload === "string"
        ? JSON.parse(props.ticket.context_payload)
        : props.ticket.context_payload;
    return payload?.ai_triage || null;
  } catch {
    return null;
  }
});

const openCitationViewer = (citation) => {
  activeCitation.value = citation;
  isCitationViewerOpen.value = true;
};

const closeCitationViewer = () => {
  isCitationViewerOpen.value = false;
  activeCitation.value = null;
};

const applyAiDraftToComment = () => {
  if (!aiTriage.value?.draft_answer) return;
  newCommentContent.value = aiTriage.value.draft_answer;
  newCommentVisibility.value = "PUBLIC";
  activeTab.value = "discussion";
};

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

const slaIconComponent = computed(() => {
  if (props.ticket.sla_breached) return Siren;
  if (props.ticket.sla_amber_warned || slaElapsedPercent.value >= 75)
    return AlertTriangle;
  if (props.ticket.status?.startsWith("WAITING_")) return Pause;
  return Clock;
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

watch(
  canViewInternalSponsorNotes,
  (allowed) => {
    if (!allowed) {
      if (commentFilter.value === "INTERNAL_SPONSOR") {
        commentFilter.value = "ALL";
      }
      if (newCommentVisibility.value === "INTERNAL_SPONSOR") {
        newCommentVisibility.value = "PUBLIC";
      }
    }
  },
  { immediate: true }
);

watch(
  () => props.ticket?.id,
  () => {
    pendingStatusHandoff = false;
    isResolveModalOpen.value = false;
    if (!demoBuild && activeTab.value === "attachments") {
      activeTab.value = "overview";
    }
  }
);

watch(
  () => props.isOpen,
  (open) => {
    if (!open) {
      pendingStatusHandoff = false;
    }
  }
);

const visibleComments = computed(() => {
  if (canViewInternalSponsorNotes.value) return props.comments;
  return props.comments.filter((c) => c.visibility !== "INTERNAL_SPONSOR");
});

const drawerTabs = computed(() => {
  const tabs = [
    { id: "overview", label: "Overview & Context" },
    {
      id: "discussion",
      label: `Discussion (${visibleComments.value.length})`,
    },
  ];
  if (demoBuild) {
    tabs.push({
      id: "attachments",
      label: `Evidence (${props.attachments.length})`,
    });
  }
  tabs.push(
    { id: "signatures", label: "eSignatures" },
    { id: "audit", label: "Audit Trail" }
  );
  return tabs;
});

const filteredComments = computed(() => {
  if (commentFilter.value === "ALL") return visibleComments.value;
  return visibleComments.value.filter(
    (c) => c.visibility === commentFilter.value
  );
});

const resolvePanelHandoff = useInlineFocusHandoff({
  container: drawerRef,
  includeDefaultFallbacks: false,
  openTarget: "#resolve-rca-category",
  closeFallback: [
    "#btn-open-resolve",
    "#drawer-ticket-title",
    "#ticket-drawer-close-btn",
  ],
});

let pendingStatusHandoff = false;

const handoffAfterDrawerStatusChange = () => {
  if (!props.isOpen) return;
  void handoffFocus(
    [
      () =>
        drawerRef.value?.querySelector(
          ".action-btn-group button:not([disabled])"
        ) || null,
      "#btn-open-part11-sign",
      "#drawer-ticket-title",
      "#ticket-drawer-close-btn",
    ],
    {
      container: drawerRef,
      includeDefaultFallbacks: false,
      fallback: ["#drawer-ticket-title", "#ticket-drawer-close-btn"],
    }
  );
};

watch(
  () => [props.ticket?.status, props.ticket?.signature_token],
  () => {
    if (!props.isOpen) return;
    const focusEscapedDrawer =
      typeof document !== "undefined" &&
      !isSignModalOpen.value &&
      !isCitationViewerOpen.value &&
      Boolean(
        drawerRef.value &&
        (!document.activeElement ||
          !drawerRef.value.contains(document.activeElement))
      );
    if (pendingStatusHandoff || focusEscapedDrawer) {
      pendingStatusHandoff = false;
      handoffAfterDrawerStatusChange();
    }
  }
);

const transitionTicket = (newStatus) => {
  pendingStatusHandoff = true;
  const reason =
    transitionReason.value.trim() || `Status transition to ${newStatus}`;
  emit("transition", {
    ticketId: props.ticket.id,
    newStatus,
    reason_for_change: reason,
  });
};

const openResolveModal = () => {
  if (demoBuild) {
    pendingStatusHandoff = true;
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
    return;
  }
  resolvePanelHandoff.rememberTrigger("#btn-open-resolve");
  isResolveModalOpen.value = true;
  void resolvePanelHandoff.handoff("#resolve-rca-category");
};

const closeResolveModal = () => {
  isResolveModalOpen.value = false;
  void resolvePanelHandoff.restoreTrigger([
    "#btn-open-resolve",
    "#drawer-ticket-title",
    "#ticket-drawer-close-btn",
  ]);
};

const submitResolution = () => {
  if (
    !resolveForm.root_cause_summary.trim() ||
    !resolveForm.reason_for_change.trim()
  ) {
    return;
  }
  pendingStatusHandoff = true;
  emit("transition", {
    ticketId: props.ticket.id,
    newStatus: "RESOLVED",
    root_cause_category: resolveForm.root_cause_category,
    root_cause_summary: resolveForm.root_cause_summary.trim(),
    resolution_code:
      resolveForm.resolution_code.trim() || "CORRECTIVE_ACTION_IMPLEMENTED",
    reason_for_change: resolveForm.reason_for_change.trim(),
  });
  isResolveModalOpen.value = false;
  handoffAfterDrawerStatusChange();
};

const submitComment = () => {
  if (!newCommentContent.value.trim() || !newCommentReason.value.trim()) return;
  const visibility = canViewInternalSponsorNotes.value
    ? newCommentVisibility.value
    : "PUBLIC";
  emit("comment", {
    ticketId: props.ticket.id,
    content: newCommentContent.value.trim(),
    visibility,
    reason_for_change: newCommentReason.value.trim(),
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
:deep(.drawer-backdrop) {
  overscroll-behavior: contain;
  touch-action: pan-y;
}

:deep(.drawer-panel) {
  width: var(
    --semantic-drawer-width-lg,
    var(--drawer-width-lg, min(100vw, 640px))
  );
  max-width: 100vw;
  overscroll-behavior: contain;
  touch-action: pan-y;
}

:deep(.drawer-panel > .clinical-drawer-header) {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-bottom-color: var(--semantic-color-text-dark);
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.drawer-header-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1 1 auto;
}

.ref-badge {
  font-family: var(--font-mono, monospace);
  font-size: 0.78rem;
  font-weight: 700;
  background: rgba(255, 255, 255, 0.15);
  padding: 2px 7px;
  border-radius: 4px;
  color: var(--semantic-color-text-inverse);
  flex-shrink: 0;
}

.drawer-title {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
  color: var(--semantic-color-text-inverse);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.drawer-close,
.citation-close-btn {
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  cursor: pointer;
  padding: 0;
  border-radius: 6px;
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.citation-close-btn {
  color: var(--semantic-color-text-secondary);
}

.drawer-close:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--semantic-color-surface);
}

.citation-close-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

@media (max-height: 540px) {
  .drawer-close,
  .citation-close-btn {
    min-width: 36px;
    min-height: 36px;
  }
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
  overscroll-behavior: contain;
  touch-action: pan-y;
  font-variant-numeric: tabular-nums;
}

.drawer-header-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-sev-critical {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.badge-sev-major {
  background: var(--semantic-color-warning-action);
  color: var(--semantic-color-surface);
}

.badge-sev-minor {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.badge-status-open {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.badge-status-in_progress {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  border: 1px solid var(--semantic-color-info-border);
}

.badge-status-waiting_on_site,
.badge-status-waiting_on_sponsor {
  background: var(--semantic-color-warning-action);
  color: var(--semantic-color-text);
}

.badge-status-resolved {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.badge-status-closed {
  background: var(--semantic-color-text-secondary);
  color: var(--semantic-color-surface);
}

.badge-signed {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.sla-banner {
  padding: 8px 12px;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.sla-banner.sla-normal {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.sla-banner.sla-warning {
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  color: var(--semantic-color-warning-dark);
}

.sla-banner.sla-breached {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
}

.sla-banner.sla-paused {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
}

.sla-banner-left {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.8rem;
  font-weight: 600;
}

.sla-icon {
  flex-shrink: 0;
}

.sla-progress-track {
  height: 4px;
  background: var(--semantic-color-surface-header);
  border-radius: 2px;
  overflow: hidden;
}

.sla-progress-fill {
  height: 100%;
  transition: width 0.3s ease;
}

.fill-normal {
  background: var(--semantic-color-primary);
}
.fill-warning {
  background: var(--semantic-color-warning-action);
}
.fill-breached {
  background: var(--semantic-color-error);
}
.fill-paused {
  background: var(--semantic-color-border-control);
}

.drawer-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 10px 12px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
}

.action-btn-group {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  flex: 1 1 auto;
  min-width: 0;
}

.tab-pane {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.section-card {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  padding: 14px;
  border: 1px solid var(--semantic-color-border);
  min-width: 0;
}

.card-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 10px 0;
}

.narrative-text {
  font-size: 0.875rem;
  color: var(--semantic-color-text-body);
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

.meta-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.meta-label {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  font-weight: 600;
}

.meta-value {
  font-size: 0.875rem;
  color: var(--semantic-color-text-dark);
  font-weight: 500;
  word-break: break-word;
}

.entity-badge-box {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.entity-type-badge {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 4px 8px;
  border-radius: 4px;
}

.entity-id-value {
  font-family: var(--font-mono, monospace);
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  word-break: break-all;
}

.rca-card {
  background: var(--semantic-color-warning-bg-soft);
  border-color: var(--semantic-color-warning-border);
}

.rca-category-pill {
  display: inline-block;
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  margin-bottom: 8px;
}

.json-viewer {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  padding: 12px;
  border-radius: 6px;
  font-size: 0.78rem;
  overflow-x: auto;
  max-width: 100%;
}

/* Discussion tab */
.comments-filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.filter-chip {
  min-height: 44px;
  padding: 6px 12px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.filter-chip.active {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

.comment-stream {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.comment-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 12px 14px;
}

.comment-card.comment-internal {
  background: var(--semantic-color-warning-bg-soft);
  border-color: var(--semantic-color-warning-border);
  border-left: 4px solid var(--semantic-color-warning-border-strong);
}

.comment-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 6px;
}

.comment-author {
  font-size: 0.82rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.badge-public {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.badge-internal {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.comment-time {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  margin-left: auto;
}

.comment-body {
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
  line-height: 1.45;
  word-break: break-word;
}

.new-comment-box {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.visibility-selector {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.radio-label {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 44px;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  cursor: pointer;
}

.comment-submit-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

/* Attachments tab */
.attachment-upload-box {
  background: var(--semantic-color-surface);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 14px;
}

.upload-hint {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 12px;
}

.upload-inputs {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.file-input {
  max-width: 100%;
  min-height: 44px;
  font-size: 1rem;
}

.attachments-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.attachment-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.attachment-icon {
  color: var(--semantic-color-primary);
}

.attachment-details {
  flex: 1 1 180px;
  min-width: 0;
}

.attachment-filename {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  word-break: break-all;
}

.attachment-meta {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  margin: 2px 0;
}

.sha-hash {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.badge-deid {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

/* Signatures tab */
.signature-record-card {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
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
  color: var(--semantic-color-success-dark);
  flex-shrink: 0;
}

.sig-card-title {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-success-dark);
  margin: 0;
}

.sig-card-subtitle {
  font-size: 0.75rem;
  color: var(--semantic-color-success);
  margin: 2px 0 0 0;
}

.sig-details-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}

.sig-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.sig-item.full-width {
  grid-column: 1 / -1;
}

.sig-label {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--semantic-color-success);
  text-transform: uppercase;
}

.sig-val {
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  font-weight: 500;
  word-break: break-word;
}

.meaning-val {
  font-style: italic;
  color: var(--semantic-color-success-dark);
}

.token-val {
  font-family: var(--font-mono, monospace);
  font-size: 0.75rem;
  word-break: break-all;
}

.empty-sig-box {
  background: var(--semantic-color-surface);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 24px 16px;
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
  background: var(--semantic-color-border-muted);
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
  background: var(--semantic-color-interactive-primary);
  border: 2px solid var(--semantic-color-surface);
  box-shadow: 0 0 0 1px var(--semantic-color-border-control);
}

.audit-content {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 10px 12px;
  width: 100%;
  min-width: 0;
}

.audit-row-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 4px;
}

.audit-action {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.audit-user {
  font-size: 0.75rem;
  color: var(--semantic-color-interactive-primary);
}

.audit-time {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  margin-left: auto;
}

.audit-reason {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

.empty-placeholder {
  padding: 20px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
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
  min-height: 44px;
  min-width: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  box-sizing: border-box;
}

.btn-sm {
  padding: 6px 10px;
  font-size: 0.78rem;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}
.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
}
.btn-warning {
  background: var(--semantic-color-warning-action);
  color: var(--semantic-color-surface);
}
.btn-success {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.btn-sign {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
  font-weight: 700;
}
.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.form-control {
  min-height: 44px;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 1rem;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface);
  max-width: 100%;
  box-sizing: border-box;
}

.form-control:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}

.flex-1 {
  flex: 1 1 180px;
  min-width: 0;
}

/* AI RAG Triage Styles */
.ai-triage-card {
  border-left: 4px solid var(--semantic-color-interactive-primary);
  background: var(--semantic-color-surface-muted);
}

.ai-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  flex-wrap: wrap;
  gap: 8px;
}

.ai-card-title-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.ai-robot-icon {
  color: var(--semantic-color-interactive-primary);
}

.ai-title {
  margin-bottom: 0;
  color: var(--semantic-color-text-dark);
}

.badge-ai-grounded {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  font-weight: 700;
}

.badge-ai-failclosed {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
  font-weight: 700;
}

.ai-draft-content {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.ai-draft-text {
  font-size: 0.85rem;
  line-height: 1.55;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  white-space: pre-wrap;
}

.citation-badge-list {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
}

.citation-label {
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.citation-chip-btn {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-interactive-primary);
  min-height: 44px;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 0.76rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.ai-draft-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.ai-failclosed-box {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 6px;
  padding: 10px 12px;
}

.ai-failclosed-text {
  font-size: 0.82rem;
  color: var(--semantic-color-error-text-dark);
  margin-bottom: 6px;
}

.human-routed-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

/* Citation Document Viewer Modal (ClinicalModal slots) */
.citation-modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.citation-header-content {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  flex: 1 1 auto;
}

.citation-modal-meta {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.citation-modal-title {
  margin: 0;
  font-size: 1rem;
  color: var(--semantic-color-text);
  display: flex;
  align-items: center;
  gap: 6px;
}

.badge-page {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.citation-modal-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.citation-coordinates-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 10px 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.coord-item {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 0.82rem;
}

.coord-label {
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.coord-val {
  color: var(--semantic-color-text);
  word-break: break-word;
}

.text-success {
  color: var(--semantic-color-success);
  font-weight: 600;
  display: inline-flex;
  align-items: center;
  gap: 4px;
}

.citation-excerpt-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 14px;
}

.excerpt-heading {
  margin: 0 0 8px 0;
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-body);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.excerpt-body {
  font-size: 0.88rem;
  line-height: 1.6;
  color: var(--semantic-color-text-dark);
  white-space: pre-wrap;
}

.citation-modal-footer {
  display: flex;
  justify-content: flex-end;
  width: 100%;
}

.signed-lock-notice {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 6px 10px;
}

.transition-reason-field {
  flex: 1 1 180px;
  min-width: 0;
}

.resolve-panel {
  padding: 14px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.resolve-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
}

.resolve-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.sig-prereq-hint {
  margin-top: 8px;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
}

.audit-hash {
  margin-top: 4px;
  font-size: 0.74rem;
  color: var(--semantic-color-text-secondary);
}

@media (max-width: 640px) {
  .meta-grid,
  .sig-details-grid,
  .resolve-grid {
    grid-template-columns: 1fr;
  }

  .comment-submit-row,
  .drawer-actions,
  .action-btn-group {
    flex-direction: column;
    align-items: stretch;
  }

  .action-btn-group .btn,
  .comment-submit-row .btn,
  .drawer-actions > .btn {
    width: 100%;
  }
}
</style>
