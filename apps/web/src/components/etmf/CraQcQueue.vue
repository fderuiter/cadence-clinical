<!-- apps/web/src/components/etmf/CraQcQueue.vue -->
<template>
  <div class="qc-queue-container">
    <div class="qc-queue-header">
      <div>
        <h3 style="margin: 0; font-size: var(--font-size-md); font-weight: 700">
          🛡️ CRA Quality Control Review Queue
        </h3>
        <p
          style="
            margin: 2px 0 0 0;
            font-size: var(--font-size-2xs);
            color: var(--semantic-color-text-secondary);
          "
        >
          Adjudicate AI auto-classified documents staged in TECHNICAL_QC state
          under FDA 21 CFR Part 11
        </p>
      </div>

      <div style="display: flex; gap: 8px">
        <button
          class="btn btn-secondary btn-sm btn-refresh-qc"
          style="font-size: var(--font-size-2xs)"
          @click="loadQueue"
        >
          🔄 Refresh Queue
        </button>
        <button
          class="btn btn-primary btn-sm btn-batch-accept"
          style="font-size: var(--font-size-2xs)"
          :disabled="!canReviewQc || selectedItemIds.length === 0"
          :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
          @click="openBatchAcceptModal"
        >
          ✓ Batch Accept Classifications ({{ selectedItemIds.length }})
        </button>
      </div>
    </div>

    <div
      v-if="!canReviewQc"
      id="cra-qc-role-note"
      class="qc-role-banner"
      role="note"
    >
      {{ ETMF_QC_REVIEW_ROLE_REASON }}
    </div>
    <div
      v-if="etmfStore.scopeForbiddenGuidance"
      id="cra-qc-scope-forbidden"
      class="qc-error-banner"
      role="alert"
    >
      <strong>Study or Site Assignment Required:</strong>
      {{ etmfStore.scopeForbiddenGuidance }}
    </div>
    <div
      v-else-if="queueError"
      id="cra-qc-error"
      class="qc-error-banner"
      role="alert"
    >
      ❌ {{ queueError }}
    </div>

    <!-- Selection / Filter Ribbon -->
    <div
      class="filter-ribbon"
      style="
        padding: 10px 16px;
        background: var(--semantic-color-surface-muted);
        border-bottom: 1px solid var(--border);
        display: flex;
        justify-content: space-between;
        align-items: center;
      "
    >
      <div style="display: flex; align-items: center; gap: 12px">
        <label
          style="
            font-size: var(--font-size-2xs);
            display: flex;
            align-items: center;
            gap: 6px;
            cursor: pointer;
          "
        >
          <input
            type="checkbox"
            :checked="isAllSelected"
            :indeterminate="isPartiallySelected"
            :disabled="!canReviewQc"
            :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
            @change="toggleSelectAll"
          />
          Select All ({{ queueItems.length }})
        </label>
        <span
          v-if="selectedItemIds.length > 0"
          class="badge status-approved"
          style="font-size: var(--font-size-2xs)"
        >
          {{ selectedItemIds.length }} Selected
        </span>
      </div>

      <div style="display: flex; gap: 8px">
        <input
          v-model="searchQuery"
          type="text"
          aria-label="Filter by filename or code"
          class="form-control"
          placeholder="Filter by filename or code..."
          style="
            font-size: var(--semantic-font-size-control);
            width: 220px;
            padding: 4px 8px;
          "
        />
      </div>
    </div>

    <!-- QC Queue Table -->
    <div class="qc-table-wrapper">
      <div
        v-if="etmfStore.scopeForbiddenGuidance"
        class="empty-qc-state"
        style="
          padding: 40px;
          text-align: center;
          color: var(--semantic-color-text-secondary);
        "
      >
        <span style="font-size: 2rem" aria-hidden="true">🔒</span>
        <p
          style="
            margin: 8px 0 0 0;
            font-size: var(--font-size-sm);
            font-weight: 600;
          "
        >
          Study or Site Scope Assignment Required
        </p>
        <p style="margin: 4px 0 0 0; font-size: var(--font-size-2xs)">
          {{ etmfStore.scopeForbiddenGuidance }}
        </p>
      </div>
      <div
        v-else-if="filteredItems.length === 0"
        class="empty-qc-state"
        style="
          padding: 40px;
          text-align: center;
          color: var(--semantic-color-text-secondary);
        "
      >
        <span style="font-size: 2rem">✅</span>
        <p
          style="
            margin: 8px 0 0 0;
            font-size: var(--font-size-sm);
            font-weight: 600;
          "
        >
          No staged documents pending Quality Control review
        </p>
        <p style="margin: 4px 0 0 0; font-size: var(--font-size-2xs)">
          All batch ingested files have been reviewed and adjudicated.
        </p>
      </div>

      <table v-else class="qc-table">
        <thead>
          <tr>
            <th style="width: 36px" />
            <th>Document File</th>
            <th>Staged At / By</th>
            <th>Predicted DIA Taxonomy</th>
            <th>AI Confidence</th>
            <th>Discrepancy Notes</th>
            <th style="text-align: right">Adjudication</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in filteredItems"
            :key="item.document_id"
            class="qc-row"
          >
            <td>
              <input
                v-model="selectedItemIds"
                type="checkbox"
                :aria-label="`Select ${item.filename || item.document_id}`"
                :value="item.document_id"
                :disabled="!canReviewQc"
                :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
              />
            </td>
            <td>
              <div style="display: flex; flex-direction: column">
                <span
                  class="font-mono font-bold"
                  style="font-size: var(--font-size-2xs)"
                  >{{ item.filename }}</span
                >
                <span class="text-muted" style="font-size: var(--font-size-2xs)"
                  >ID: {{ item.document_id }}</span
                >
              </div>
            </td>
            <td>
              <div
                style="
                  display: flex;
                  flex-direction: column;
                  font-size: var(--font-size-2xs);
                "
              >
                <span>{{ item.staged_by }}</span>
                <span class="text-muted">{{ formatDate(item.staged_at) }}</span>
              </div>
            </td>
            <td>
              <div
                v-if="item.intelligence_report"
                style="
                  display: flex;
                  flex-direction: column;
                  font-size: var(--font-size-2xs);
                "
              >
                <span class="font-mono font-bold">{{
                  item.intelligence_report.primary_classification.artifact_code
                }}</span>
                <span>{{
                  item.intelligence_report.primary_classification.artifact_type
                }}</span>
                <span class="text-muted"
                  >Zone
                  {{ item.intelligence_report.primary_classification.zone }} -
                  Section
                  {{
                    item.intelligence_report.primary_classification.section
                  }}</span
                >
              </div>
            </td>
            <td>
              <span
                v-if="item.intelligence_report"
                class="badge"
                :class="
                  getConfidenceClass(item.intelligence_report.confidence_tier)
                "
              >
                {{
                  Math.round(
                    item.intelligence_report.primary_classification.confidence *
                      100
                  )
                }}% {{ item.intelligence_report.confidence_tier }}
              </span>
            </td>
            <td>
              <div
                v-if="
                  item.discrepancy_notes && item.discrepancy_notes.length > 0
                "
              >
                <span
                  v-for="(note, nIdx) in item.discrepancy_notes"
                  :key="nIdx"
                  class="badge status-hold"
                  style="font-size: var(--font-size-2xs)"
                >
                  ⚠️ {{ note }}
                </span>
              </div>
              <span
                v-else
                class="text-muted"
                style="font-size: var(--font-size-2xs)"
                >No discrepancies</span
              >
            </td>
            <td style="text-align: right">
              <button
                type="button"
                class="btn btn-secondary btn-xs btn-inspect-qc"
                style="font-size: var(--font-size-2xs); padding: 4px 8px"
                @click="openInspectionModal(item)"
              >
                <span aria-hidden="true">📋 </span>Inspect &amp; Edit<span
                  class="sr-only"
                >
                  {{ item.filename || item.document_id }}</span
                >
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Batch Accept Modal -->
    <div
      v-if="showBatchModal"
      class="qc-modal-overlay"
      role="presentation"
      @click.self="showBatchModal = false"
    >
      <div
        ref="batchModalRef"
        class="qc-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="qc-batch-modal-title"
        tabindex="-1"
      >
        <h4
          id="qc-batch-modal-title"
          style="margin: 0 0 8px 0; font-size: var(--font-size-md)"
        >
          Batch Accept AI Classifications
        </h4>
        <p
          style="
            font-size: var(--font-size-2xs);
            color: var(--semantic-color-text-secondary);
            margin-bottom: 12px;
          "
        >
          You are approving predicted DIA TMF taxonomy tags for
          <strong>{{ selectedItemIds.length }}</strong> staged documents.
        </p>
        <div class="form-group" style="margin-bottom: 16px">
          <label
            for="batch-accept-reason"
            class="form-label"
            style="font-size: var(--font-size-2xs); font-weight: 600"
          >
            21 CFR Part 11 Reason for Approval <span class="required">*</span>
          </label>
          <input
            id="batch-accept-reason"
            v-model="batchReason"
            type="text"
            class="form-control"
            placeholder="e.g. Verified AI classifications against source regulatory document originals"
            style="font-size: var(--semantic-font-size-control)"
          />
        </div>
        <div style="display: flex; justify-content: flex-end; gap: 8px">
          <button
            class="btn btn-secondary"
            style="font-size: var(--font-size-2xs)"
            @click="showBatchModal = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary btn-confirm-batch-accept"
            style="font-size: var(--font-size-2xs)"
            :disabled="!batchReason || batchReason.length < 10"
            @click="executeBatchAccept"
          >
            ✓ Confirm Batch Accept
          </button>
        </div>
      </div>
    </div>

    <!-- Single Item Inspection & Override Modal Drawer -->
    <div
      v-if="inspectedItem"
      class="qc-modal-overlay"
      role="presentation"
      @click.self="inspectedItem = null"
    >
      <div
        ref="inspectModalRef"
        class="qc-modal qc-modal-lg"
        role="dialog"
        aria-modal="true"
        aria-labelledby="qc-inspect-modal-title"
        tabindex="-1"
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 12px;
            border-bottom: 1px solid var(--border);
            padding-bottom: 8px;
          "
        >
          <h4
            id="qc-inspect-modal-title"
            style="
              margin: 0;
              font-size: var(--font-size-base);
              font-weight: 700;
            "
          >
            CRA Quality Control Review: {{ inspectedItem.filename }}
          </h4>
          <button
            type="button"
            class="btn btn-secondary btn-xs"
            aria-label="Close"
            @click="inspectedItem = null"
          >
            ✕
          </button>
        </div>

        <div
          style="
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 16px;
            margin-bottom: 16px;
          "
        >
          <!-- Predicted Classification Panel -->
          <div class="info-card">
            <h5
              style="
                margin: 0 0 8px 0;
                font-size: var(--font-size-2xs);
                color: var(--primary);
              "
            >
              Predicted Taxonomy
            </h5>
            <p style="margin: 2px 0; font-size: var(--font-size-2xs)">
              <strong>Artifact Code:</strong>
              <code class="font-mono">{{
                inspectedItem.intelligence_report?.primary_classification
                  ?.artifact_code
              }}</code>
            </p>
            <p style="margin: 2px 0; font-size: var(--font-size-2xs)">
              <strong>Artifact Type:</strong>
              {{
                inspectedItem.intelligence_report?.primary_classification
                  ?.artifact_type
              }}
            </p>
            <p style="margin: 2px 0; font-size: var(--font-size-2xs)">
              <strong>Zone:</strong>
              {{
                inspectedItem.intelligence_report?.primary_classification?.zone
              }}
              - Section
              {{
                inspectedItem.intelligence_report?.primary_classification
                  ?.section
              }}
            </p>
            <p style="margin: 2px 0; font-size: var(--font-size-2xs)">
              <strong>AI Confidence:</strong>
              {{
                Math.round(
                  (inspectedItem.intelligence_report?.primary_classification
                    ?.confidence || 0) * 100
                )
              }}%
            </p>
          </div>

          <!-- Override Taxonomy Panel -->
          <div class="info-card">
            <h5
              style="
                margin: 0 0 8px 0;
                font-size: var(--font-size-2xs);
                color: var(--primary);
              "
            >
              Override Taxonomy Tag
            </h5>
            <label
              class="form-label"
              style="font-size: var(--font-size-2xs)"
              for="qc-artifact-code-override"
              >Artifact Code Override</label
            >
            <select
              id="qc-artifact-code-override"
              v-model="overrideArtifactCode"
              class="form-control"
              style="
                font-size: var(--semantic-font-size-control);
                margin-bottom: 8px;
              "
            >
              <option value="">
                -- Keep Predicted ({{
                  inspectedItem.intelligence_report?.primary_classification
                    ?.artifact_code
                }}) --
              </option>
              <option value="01.01.01">
                01.01.01 - Clinical Trial Protocol
              </option>
              <option value="01.01.02">
                01.01.02 - Clinical Trial Protocol Amendment
              </option>
              <option value="02.01.01">
                02.01.01 - Investigator's Brochure
              </option>
              <option value="04.02.01">
                04.02.01 - IRB/IEC Approval Notification
              </option>
              <option value="05.02.01">05.02.01 - FDA Form 1572</option>
              <option value="05.02.02">05.02.02 - Financial Disclosure</option>
              <option value="05.02.03">05.02.03 - Investigator CV</option>
              <option value="05.02.04">
                05.02.04 - Delegation of Authority Log
              </option>
              <option value="05.02.05">05.02.05 - Informed Consent Form</option>
              <option value="07.01.01">
                07.01.01 - Serious Adverse Event Report
              </option>
              <option value="08.01.01">
                08.01.01 - Central Laboratory Certificate
              </option>
              <option value="11.01.01">
                11.01.01 - Statistical Analysis Plan
              </option>
            </select>
          </div>
        </div>

        <!-- 21 CFR Part 11 Change Reason -->
        <div class="form-group" style="margin-bottom: 16px">
          <label
            for="review-reason-input"
            class="form-label"
            style="font-size: var(--font-size-2xs); font-weight: 600"
          >
            21 CFR Part 11 Reason for Change <span class="required">*</span>
          </label>
          <input
            id="review-reason-input"
            v-model="reviewReason"
            type="text"
            class="form-control"
            placeholder="Mandatory Part 11 justification (min 10 chars)"
            style="font-size: var(--semantic-font-size-control)"
          />
        </div>

        <!-- Action Buttons -->
        <div
          style="
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            border-top: 1px solid var(--border);
            padding-top: 12px;
          "
        >
          <button
            class="btn btn-secondary"
            style="font-size: var(--font-size-2xs)"
            @click="inspectedItem = null"
          >
            Cancel
          </button>
          <button
            class="btn btn-danger btn-reject-qc"
            style="font-size: var(--font-size-2xs)"
            :disabled="
              !canReviewQc || !reviewReason || reviewReason.length < 10
            "
            :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
            @click="submitDecision('REJECT')"
          >
            ✕ Reject
          </button>
          <button
            class="btn btn-primary btn-accept-qc"
            style="font-size: var(--font-size-2xs)"
            :disabled="
              !canReviewQc || !reviewReason || reviewReason.length < 10
            "
            :title="!canReviewQc ? ETMF_QC_REVIEW_ROLE_REASON : undefined"
            @click="
              submitDecision(overrideArtifactCode ? 'OVERRIDE' : 'ACCEPT')
            "
          >
            ✓
            {{
              overrideArtifactCode
                ? "Override Tag & Approve"
                : "Accept & Approve"
            }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useFocusTrap } from "ui";
import { useEtmfStore } from "../../stores/etmf.js";
import { useAuthStore } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";
import {
  canReviewEtmfQc,
  ETMF_QC_REVIEW_ROLE_REASON,
} from "../../canvas/authority.ts";

const etmfStore = useEtmfStore();
const authStore = useAuthStore();
const toastStore = useToastStore();

const hasActiveAuth = computed(() =>
  Boolean(
    authStore.isAuthenticated ||
    (Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0)
  )
);
const canReviewQc = computed(
  () => !hasActiveAuth.value || canReviewEtmfQc(authStore)
);
const queueError = ref("");

const searchQuery = ref("");
const selectedItemIds = ref([]);
const showBatchModal = ref(false);
const batchReason = ref(
  "Verified AI classifications against source regulatory document originals"
);
const inspectedItem = ref(null);
const reviewReason = ref("CRA QC review verified taxonomy metadata");
const overrideArtifactCode = ref("");
const batchModalRef = ref(null);
const inspectModalRef = ref(null);

useFocusTrap(batchModalRef, {
  isOpen: () => showBatchModal.value,
  onEscape: () => {
    showBatchModal.value = false;
  },
});

useFocusTrap(inspectModalRef, {
  isOpen: () => Boolean(inspectedItem.value),
  onEscape: () => {
    inspectedItem.value = null;
  },
});

const queueItems = computed(() => etmfStore.qcQueue || []);

const filteredItems = computed(() => {
  if (!searchQuery.value) return queueItems.value;
  const q = searchQuery.value.toLowerCase();
  return queueItems.value.filter(
    (item) =>
      item.filename.toLowerCase().includes(q) ||
      (item.intelligence_report?.primary_classification?.artifact_code || "")
        .toLowerCase()
        .includes(q)
  );
});

const isAllSelected = computed(() => {
  return (
    filteredItems.value.length > 0 &&
    selectedItemIds.value.length === filteredItems.value.length
  );
});

const isPartiallySelected = computed(() => {
  return selectedItemIds.value.length > 0 && !isAllSelected.value;
});

function toggleSelectAll(event) {
  if (event.target.checked) {
    selectedItemIds.value = filteredItems.value.map((i) => i.document_id);
  } else {
    selectedItemIds.value = [];
  }
}

async function loadQueue() {
  queueError.value = "";
  try {
    await etmfStore.fetchQcQueue();
  } catch (err) {
    queueError.value = err?.message || "Failed to load CRA QC queue.";
    toastStore.apiError("Failed to load CRA QC queue", err);
  }
}

function formatDate(isoStr) {
  if (!isoStr) return "—";
  return new Date(isoStr).toLocaleString();
}

function getConfidenceClass(tier) {
  switch (tier) {
    case "HIGH":
      return "status-approved";
    case "MEDIUM":
      return "status-review";
    case "LOW":
      return "status-hold";
    default:
      return "status-draft";
  }
}

function openBatchAcceptModal() {
  if (!canReviewQc.value) {
    toastStore.push({
      message: ETMF_QC_REVIEW_ROLE_REASON,
      tone: "warning",
    });
    return;
  }
  batchReason.value =
    "Verified AI classifications against source regulatory document originals";
  showBatchModal.value = true;
}

async function executeBatchAccept() {
  if (!canReviewQc.value) return;
  if (selectedItemIds.value.length === 0) return;
  if (!batchReason.value || batchReason.value.length < 10) return;

  const count = selectedItemIds.value.length;
  try {
    await etmfStore.batchAcceptClassifications(
      selectedItemIds.value,
      batchReason.value
    );

    showBatchModal.value = false;
    selectedItemIds.value = [];
    toastStore.push({
      message: `Batch Accepted: Approved classifications for ${count} documents.`,
      tone: "success",
    });
  } catch (err) {
    queueError.value = err?.message || "Batch QC acceptance failed.";
    toastStore.apiError("Batch QC acceptance failed", err);
  }
}

function openInspectionModal(item) {
  inspectedItem.value = item;
  reviewReason.value = "CRA QC review verified taxonomy metadata";
  overrideArtifactCode.value = "";
}

async function submitDecision(decision) {
  if (!canReviewQc.value) return;
  if (!inspectedItem.value) return;
  if (!reviewReason.value || reviewReason.value.length < 10) return;

  const currentFilename = inspectedItem.value.filename;

  try {
    await etmfStore.reviewQcDocument(inspectedItem.value.document_id, {
      decision,
      reason_for_change: reviewReason.value,
      override_artifact_code: overrideArtifactCode.value || null,
    });

    inspectedItem.value = null;
    toastStore.push({
      message: `QC Adjudication submitted for '${currentFilename || "document"}': ${decision}`,
      tone: "success",
    });
  } catch (err) {
    queueError.value = err?.message || "QC adjudication failed.";
    toastStore.apiError("QC adjudication failed", err);
  }
}

watch(
  () => [etmfStore.currentStudyId, etmfStore.currentSiteId],
  () => {
    selectedItemIds.value = [];
    loadQueue();
  }
);

onMounted(() => {
  loadQueue();
});
</script>

<style scoped>
.qc-queue-container {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  overflow: hidden;
}

.qc-queue-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.qc-role-banner {
  padding: 8px 16px;
  font-size: var(--font-size-2xs);
  background: var(--semantic-color-warning-bg-soft);
  border-bottom: 1px solid var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.qc-error-banner {
  padding: 8px 16px;
  font-size: var(--font-size-2xs);
  background: var(--semantic-color-error-bg-soft);
  border-bottom: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.qc-table-wrapper {
  overflow-x: auto;
  max-height: 500px;
}

.qc-table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--font-size-2xs);
}

.qc-table th,
.qc-table td {
  padding: 10px 14px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.qc-table th {
  background: var(--semantic-color-surface-muted);
  font-weight: 600;
  position: sticky;
  top: 0;
}

.qc-row:hover {
  background-color: var(--semantic-color-surface-muted);
}

.qc-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1200;
  display: flex;
  align-items: center;
  justify-content: center;
}

.qc-modal {
  width: 440px;
  max-width: 90vw;
  background: var(--semantic-color-surface);
  border-radius: 8px;
  padding: 20px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
}

.qc-modal-lg {
  width: 620px;
}

.info-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 10px 12px;
}
</style>
