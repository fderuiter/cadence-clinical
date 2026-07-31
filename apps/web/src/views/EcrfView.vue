<template>
  <div
    id="section-ecrf"
    class="dashboard-section active"
  >
    <div class="section-header">
      <h2>eCRF Runtime Renderer</h2>
      <p>
        Live, dynamic data entry form populated from CDASH metadata with
        client-side field validation and real-time query management.
      </p>
    </div>

    <div class="grid-2">
      <!-- Dynamic eCRF Form -->
      <div class="card">
        <div class="card-title">
          Subject eCRF Data Entry Form
        </div>
        <form
          id="form-VS_DEMO"
          class="clinical-form clinical-form-grid"
          style="
            display: grid;
            grid-template-columns: repeat(12, 1fr);
            gap: 16px;
          "
          @submit.prevent
        >
          <ClinicalFormField
            v-for="field in store.ecrfFields"
            v-show="store.fieldVisibility[field.id] !== false"
            :key="field.id"
            :field="field"
            :model-value="store.formValues[field.id]"
            :query="store.formQueries[field.id]"
            :error="getValidationError(field)"
            :lookup-status="lookupStatuses[field.id]"
            @update:model-value="store.formValues[field.id] = $event"
            @input="handleLookupInput(field, $event)"
            @change="(val, target) => handleFieldChange(field, val, target)"
            @create-query="createQuery(field.id, $event)"
            @respond-query="respondQuery(field.id, $event)"
            @close-query="closeQuery(field.id)"
            @reopen-query="reopenQuery(field.id)"
          />
        </form>

        <div class="form-actions">
          <button
            id="btn-clear-ecrf"
            class="btn"
            @click="clearForm"
          >
            Clear Form
          </button>
          <button
            id="btn-submit-ecrf"
            class="btn btn-primary"
            @click="submitEcrf"
          >
            Submit eCRF Session
          </button>
        </div>
      </div>

      <!-- Live Form State & Meta -->
      <div
        class="card"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <div>
          <div class="card-title">
            CDASH Metadata Specification
          </div>
          <p style="font-size: 0.85rem; color: #475569; margin-bottom: 8px">
            The fields on the left are dynamically rendered using structural
            CDASH metadata tags (e.g. <code>DM.BRTHDT</code>,
            <code>VS.VSSBP</code>).
          </p>
        </div>

        <div
          style="
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 12px;
            background-color: #f8fafc;
          "
        >
          <h3
            style="
              font-size: 0.9rem;
              font-weight: 700;
              margin-bottom: 8px;
              color: var(--primary);
            "
          >
            Real-time Field Validation Rules:
          </h3>
          <ul
            style="
              font-size: 0.8rem;
              padding-left: 20px;
              color: #475569;
              display: flex;
              flex-direction: column;
              gap: 6px;
            "
          >
            <li><strong>Birth Date:</strong> Must match YYYY-MM-DD pattern.</li>
            <li>
              <strong>Systolic BP:</strong> Numeric value between 50 and 250
              mmHg.
            </li>
            <li>
              <strong>Diastolic BP:</strong> Numeric value between 30 and 150
              mmHg.
            </li>
            <li>
              <strong>Pulse Rate:</strong> Numeric value between 30 and 200 bpm.
            </li>
          </ul>
        </div>

        <div
          style="
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 12px;
            background-color: #f8fafc;
          "
        >
          <h3
            style="
              font-size: 0.9rem;
              font-weight: 700;
              margin-bottom: 8px;
              color: var(--primary);
            "
          >
            Query Management Actions:
          </h3>
          <p style="font-size: 0.8rem; color: #475569; line-height: 1.4">
            Click the 💬 / ⚠️ flags next to input fields to raise, answer,
            close, or reopen discrepancy notes. All query transitions are
            audit-logged in real-time.
          </p>
        </div>
      </div>

      <!-- PI Sign-Off Worklist and Verification Card -->
      <div
        class="card"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <div class="card-title">
          PI Sign-Off Worklist & Verification
        </div>
        <p style="font-size: 0.85rem; color: #475569; margin-bottom: 4px">
          Perform a 21 CFR Part 11 compliant electronic signature. This action
          requires re-authenticating the Principal Investigator credentials to
          obtain a secure single-use signature token.
        </p>

        <div style="display: flex; flex-direction: column; gap: 12px">
          <div class="form-group">
            <label for="signoff-target-type">Sign-Off Scope (Granularity)</label>
            <select
              id="signoff-target-type"
              v-model="signoffTargetType"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option value="FORM">
                FORM Level
              </option>
              <option value="VISIT">
                VISIT Level
              </option>
              <option value="SUBJECT">
                SUBJECT Level
              </option>
            </select>
          </div>

          <div class="form-group">
            <label for="signoff-target-id">Select Target ID</label>
            <select
              id="signoff-target-id"
              v-model="signoffTargetId"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option value="">
                -- Choose ID --
              </option>
              <template v-if="signoffTargetType === 'SUBJECT'">
                <option
                  v-for="sub in availableSubjects"
                  :key="sub"
                  :value="sub"
                >
                  {{ sub }}
                </option>
              </template>
              <template v-else-if="signoffTargetType === 'VISIT'">
                <option
                  v-for="visit in availableVisits"
                  :key="visit"
                  :value="visit"
                >
                  {{ visit }}
                </option>
              </template>
              <template v-else-if="signoffTargetType === 'FORM'">
                <option
                  v-for="form in availableFormSubmissions"
                  :key="form"
                  :value="form"
                >
                  {{ form }}
                </option>
              </template>
              <option value="custom">
                -- Enter Custom --
              </option>
            </select>
          </div>

          <div
            v-if="signoffTargetId === 'custom'"
            class="form-group"
          >
            <label for="signoff-custom-target-id">Custom Target ID Value</label>
            <input
              id="signoff-custom-target-id"
              type="text"
              placeholder="Enter custom target ID..."
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @input="(e) => (customTargetId = e.target.value)"
            >
          </div>

          <div class="form-group">
            <label for="signoff-reason">Signing Reason / Attestation</label>
            <select
              id="signoff-reason"
              v-model="signoffReason"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
              <option
                v-for="reason in validSigningReasons"
                :key="reason"
                :value="reason"
              >
                {{ reason }}
              </option>
            </select>
          </div>
        </div>

        <div style="display: flex; justify-content: flex-end; margin-top: 8px">
          <button
            id="btn-pi-signoff"
            class="btn btn-primary"
            type="button"
            @click="handleSignOffSubmit"
          >
            ✍️ Sign Off Target
          </button>
        </div>
      </div>

      <!-- Protocol Ingestion & Candidate Review Card -->
      <div
        class="card"
        style="
          display: flex;
          flex-direction: column;
          gap: 16px;
          grid-column: span 12;
        "
      >
        <div class="card-title">
          Ultimate CRF Builder: Protocol Ingestion & Review
        </div>
        <p style="font-size: 0.85rem; color: #475569">
          Upload a clinical protocol document (PDF/DOCX) to automatically
          generate candidate SoA visits and form fields with trace citations and
          confidence levels. Accept, edit, or reject each item before promoting
          reviewed candidates into a formal study draft.
        </p>

        <!-- Upload File Section -->
        <div
          style="
            border: 1px dashed var(--border);
            border-radius: 8px;
            padding: 16px;
            text-align: center;
            background-color: #f8fafc;
          "
        >
          <input
            ref="fileInputRef"
            type="file"
            accept=".pdf,.docx"
            style="display: none"
            @change="triggerDocumentUpload"
          >
          <button
            class="btn"
            type="button"
            :disabled="store.ingestionLoading"
            @click="triggerFileSelect"
          >
            {{
              store.ingestionLoading
                ? "Processing Document..."
                : "📁 Select Protocol PDF/DOCX"
            }}
          </button>
          <div
            v-if="selectedFileName"
            style="margin-top: 8px; font-size: 0.8rem; color: #475569"
          >
            Selected:
            <strong class="selected-file-name">{{ selectedFileName }}</strong>
          </div>
        </div>

        <!-- Ingestion Error Display -->
        <div
          v-if="store.ingestionError"
          style="color: #ef4444; font-size: 0.85rem; margin-top: 8px"
        >
          Error: {{ store.ingestionError }}
        </div>

        <!-- Candidate Draft Item Review List -->
        <div
          v-if="store.candidateDraft"
          style="
            display: flex;
            flex-direction: column;
            gap: 16px;
            margin-top: 12px;
          "
          class="candidate-draft-section"
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              border-bottom: 1px solid var(--border);
              padding-bottom: 8px;
            "
          >
            <span style="font-size: 0.9rem; font-weight: 700">
              Candidate ID:
              <code
                style="
                  background-color: #f1f5f9;
                  padding: 2px 4px;
                  border-radius: 4px;
                "
                class="candidate-id"
              >{{ store.candidateDraft.id }}</code>
              <!-- deid: ignore -->
            </span>
            <span
              :class="[
                'badge',
                store.candidateDraft.status === 'PROMOTED'
                  ? 'lookup-valid'
                  : 'lookup-degraded',
              ]"
              style="font-size: 0.8rem; padding: 4px 8px; border-radius: 4px"
              class="candidate-status"
            >
              {{ store.candidateDraft.status }}
            </span>
          </div>

          <div
            style="font-size: 0.85rem; font-weight: bold; color: var(--primary)"
          >
            Candidate Items Under Review:
          </div>

          <div
            style="
              display: flex;
              flex-direction: column;
              gap: 12px;
              max-height: 400px;
              overflow-y: auto;
            "
          >
            <div
              v-for="item in Object.values(store.candidateDraft.items)"
              :key="item.id"
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 12px;
                display: flex;
                flex-direction: column;
                gap: 8px;
              "
              class="candidate-item-card"
            >
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: flex-start;
                "
              >
                <div>
                  <span
                    class="badge"
                    style="
                      background-color: #e2e8f0;
                      color: #475569;
                      font-size: 0.75rem;
                      text-transform: uppercase;
                      margin-right: 6px;
                    "
                  >
                    <!-- deid: ignore -->
                    {{ item.type }}
                  </span>
                  <strong class="item-label">{{
                    item.type === "visit" ? item.name : item.label
                  }}</strong>
                </div>

                <!-- Confidence badge and citations -->
                <div style="display: flex; align-items: center; gap: 6px">
                  <span
                    :class="[
                      'badge',
                      getConfidenceClass(item.confidence_level),
                    ]"
                    style="font-size: 0.7rem"
                    class="item-confidence"
                  >
                    {{ (item.confidence * 100).toFixed(0) }}% ({{
                      item.confidence_level
                    }})
                  </span>
                  <span
                    style="font-size: 0.75rem; color: #64748b"
                    title="Source Reference"
                    class="item-citation"
                  >
                    📖 {{ item.source_citation }}
                  </span>
                </div>
              </div>

              <!-- Item Edit Fields if user is editing -->
              <div
                v-if="editingItemId === item.id"
                style="
                  display: flex;
                  flex-direction: column;
                  gap: 8px;
                  background-color: #f8fafc;
                  padding: 8px;
                  border-radius: 6px;
                "
                class="item-edit-section"
              >
                <div class="form-group">
                  <label style="font-size: 0.75rem">Modify Candidate Name/Label</label>
                  <input
                    v-model="editItemValue"
                    type="text"
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="edit-item-input"
                  >
                </div>
                <div class="form-group">
                  <label style="font-size: 0.75rem">Change Reason Justification (Mandatory)</label>
                  <input
                    v-model="editItemReason"
                    type="text"
                    placeholder="Enter mandatory reason..."
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="edit-item-reason"
                  >
                </div>
                <div style="display: flex; justify-content: flex-end; gap: 6px">
                  <button
                    class="btn btn-sm"
                    @click="cancelEditItem"
                  >
                    Cancel
                  </button>
                  <button
                    class="btn btn-primary btn-sm save-edit-btn"
                    @click="saveEditItem(item.id)"
                  >
                    Save Edit
                  </button>
                </div>
              </div>

              <!-- Reason Prompt modal/input inline for Rejection -->
              <div
                v-else-if="rejectingItemId === item.id"
                style="
                  display: flex;
                  flex-direction: column;
                  gap: 8px;
                  background-color: #fef2f2;
                  padding: 8px;
                  border-radius: 6px;
                "
                class="item-reject-section"
              >
                <div class="form-group">
                  <label
                    style="
                      font-size: 0.75rem;
                      color: #ef4444;
                      font-weight: bold;
                    "
                  >Provide Rejection Reason (Mandatory)</label>
                  <input
                    v-model="rejectItemReason"
                    type="text"
                    placeholder="Provide justification for rejecting candidate..."
                    style="
                      width: 100%;
                      padding: 6px;
                      border: 1px solid var(--border);
                      border-radius: 4px;
                      font-size: 0.8rem;
                    "
                    class="reject-item-reason"
                  >
                </div>
                <div style="display: flex; justify-content: flex-end; gap: 6px">
                  <button
                    class="btn btn-sm"
                    @click="cancelRejectItem"
                  >
                    Cancel
                  </button>
                  <button
                    class="btn btn-primary btn-sm confirm-reject-btn"
                    style="background-color: #ef4444"
                    @click="confirmRejectItem(item.id)"
                  >
                    Confirm Reject
                  </button>
                </div>
              </div>

              <!-- General Item Actions & Metadata -->
              <div
                v-else
                style="
                  display: flex;
                  justify-content: space-between;
                  align-items: center;
                  font-size: 0.8rem;
                "
              >
                <div style="color: #64748b">
                  Status:
                  <span
                    :class="['badge', getStatusClass(item.review_status)]"
                    style="font-size: 0.75rem"
                    class="item-review-status"
                  >
                    {{ item.review_status }}
                  </span>
                  <span
                    v-if="item.reason"
                    style="margin-left: 6px; font-style: italic"
                    class="item-review-reason"
                  >
                    - "{{ item.reason }}"
                  </span>
                </div>

                <div
                  v-if="store.candidateDraft.status !== 'PROMOTED'"
                  style="display: flex; gap: 6px"
                >
                  <button
                    class="btn btn-sm accept-btn"
                    style="padding: 2px 8px; font-size: 0.75rem"
                    @click="acceptItem(item.id)"
                  >
                    ✔️ Accept
                  </button>
                  <button
                    class="btn btn-sm edit-btn"
                    style="padding: 2px 8px; font-size: 0.75rem"
                    @click="startEditItem(item)"
                  >
                    ✏️ Edit
                  </button>
                  <button
                    class="btn btn-sm reject-btn"
                    style="
                      padding: 2px 8px;
                      font-size: 0.75rem;
                      background-color: #fecaca;
                      color: #991b1b;
                    "
                    @click="startRejectItem(item.id)"
                  >
                    ❌ Reject
                  </button>
                </div>
              </div>
            </div>
          </div>

          <!-- Promotion Gating Controls -->
          <div
            v-if="store.candidateDraft.status !== 'PROMOTED'"
            style="
              border-top: 1px solid var(--border);
              padding-top: 12px;
              display: flex;
              flex-direction: column;
              gap: 12px;
            "
          >
            <div class="form-group">
              <label
                for="promote-change-reason"
                style="font-weight: bold"
              >Promotion Change Reason (Mandatory)</label>
              <input
                id="promote-change-reason"
                v-model="promoteChangeReason"
                type="text"
                placeholder="Enter justification to promote reviewed draft into formal protocol..."
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid var(--border);
                  border-radius: 4px;
                "
                class="promote-change-reason"
              >
            </div>

            <div
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
              "
            >
              <span
                style="font-size: 0.8rem; color: #64748b"
                class="remaining-reviews-text"
              >
                {{
                  unreviewedCount === 0
                    ? "✅ All items reviewed. Ready to promote."
                    : `⚠️ ${unreviewedCount} items remaining to be reviewed.`
                }}
              </span>
              <button
                id="btn-promote-candidate"
                class="btn btn-primary"
                type="button"
                :disabled="
                  unreviewedCount > 0 ||
                    !promoteChangeReason.trim() ||
                    store.ingestionLoading
                "
                @click="promoteCandidate"
              >
                🚀 Promote Reviewed Candidate
              </button>
            </div>
          </div>
          <div
            v-else
            style="
              background-color: #f0fdf4;
              border: 1px solid #bbf7d0;
              border-radius: 8px;
              padding: 12px;
              color: #166534;
              font-size: 0.85rem;
              text-align: center;
            "
            class="promoted-success-banner"
          >
            🎉 This candidate draft has been successfully promoted to formal
            draft study version!
          </div>
        </div>
      </div>
    </div>

    <!-- Reason for Change Modal Dialog -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data field."
      :options="ecrfReasonOptions"
      default-option="Initial Entry"
      @confirm="saveChange"
      @cancel="cancelChange"
    />

    <!-- Conflict Resolution Modal Dialog -->
    <ConflictResolutionModal
      :show="showConflictModal"
      :conflict="activeConflict"
      @confirm="handleResolveConflict"
      @cancel="handleCancelConflict"
    />

    <!-- Re-authentication Modal Dialog -->
    <div
      v-if="showReauthModal"
      id="reauth-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal">
        <div class="modal-header">
          Identity Re-Authentication Required
        </div>
        <div class="modal-body">
          <p>
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must re-verify your identity before performing this
            high-security action.
          </p>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label for="reauth-username">Username</label>
            <input
              id="reauth-username"
              v-model="reauthUsername"
              type="text"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label for="reauth-password">Password</label>
            <input
              id="reauth-password"
              v-model="reauthPassword"
              type="password"
              placeholder="Enter your password to confirm identity..."
              required
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
              @keyup.enter="confirmReauth"
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label for="reauth-totp">MFA/TOTP Token (Optional)</label>
            <input
              id="reauth-totp"
              v-model="reauthTotp"
              type="text"
              placeholder="Enter 6-digit TOTP code..."
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
              "
            >
          </div>
          <div
            v-if="reauthError"
            class="validation-error-msg"
            style="margin-top: 8px; color: #ef4444"
          >
            {{ reauthError }}
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-cancel-reauth"
            class="btn"
            @click="cancelReauth"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-reauth"
            class="btn btn-primary"
            @click="confirmReauth"
          >
            Verify & Confirm
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, watch, onMounted, computed } from "vue";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { soaClient } from "../api/soaClient";
import { validateField, debounce } from "ui";
import { evaluateAST } from "../evaluator.js";
import { terminologyClient } from "../api/terminologyClient";
import ClinicalFormField from "../components/clinical/ClinicalFormField.vue";
import ReasonModal from "../components/ReasonModal.vue";
import ConflictResolutionModal from "../components/ConflictResolutionModal.vue";
import { useSyncStore } from "../stores/sync";
import { ClientSyncEngine } from "../utils/syncEngine";

const ecrfReasonOptions = [
  { value: "Initial Entry", text: "Initial Data Entry" },
  { value: "Typographical Error", text: "Correction of typographical error" },
  { value: "Re-measurement", text: "Re-measurement of vitals" },
  { value: "Transcription Error", text: "Correction of transcription error" },
  { value: "Other", text: "Other (specify below)" },
];

const store = useClinicalStore();
const authStore = useAuthStore();

const syncStore = useSyncStore();
const syncEngine = new ClientSyncEngine();

if (typeof window !== "undefined") {
  window.syncStore = syncStore;
  window.syncEngine = syncEngine;
}

const showConflictModal = computed(() => syncStore.status === "CONFLICT_DETECTED");
const activeConflict = computed(() => syncStore.conflict);

async function handleResolveConflict({ strategy, reason }) {
  if (activeConflict.value && activeConflict.value.conflictItem) {
    await syncEngine.resolveConflict(activeConflict.value.conflictItem.deltaId, strategy, reason);
  }
}

function handleCancelConflict() {
  syncStore.setStatus("IDLE");
  syncStore.clearConflict();
}

const conceptValidationStates = reactive({});
const conceptRequestIds = reactive({});

// eslint-disable-next-line no-unused-vars
function handleConceptCodeInput(field, newValue) {
  store.formValues[field.id] = newValue;

  if (field._debounceTimer) {
    clearTimeout(field._debounceTimer);
  }

  if (!newValue || !newValue.trim()) {
    conceptValidationStates[field.id] = null;
    return;
  }

  if (!conceptRequestIds[field.id]) {
    conceptRequestIds[field.id] = 0;
  }
  const currentReqId = ++conceptRequestIds[field.id];

  field._debounceTimer = setTimeout(async () => {
    try {
      const response = await terminologyClient.validateSingleCode(newValue, {
        changeReason: "Validate code",
      });

      if (currentReqId !== conceptRequestIds[field.id]) {
        return;
      }

      conceptValidationStates[field.id] = {
        state: response.state,
        decode: response.decode,
        errorMessage: response.error_message,
      };
    } catch (err) {
      if (currentReqId !== conceptRequestIds[field.id]) {
        return;
      }
      conceptValidationStates[field.id] = {
        state: "DEGRADED",
        errorMessage: err.message || "Terminology service offline",
      };
    }
  }, 300);
}

// eslint-disable-next-line no-unused-vars
function getConceptStatusClass(fieldId) {
  const stateObj = conceptValidationStates[fieldId];
  if (!stateObj) return "";
  if (stateObj.state === "VALID") return "lookup-valid";
  if (stateObj.state === "INVALID") return "lookup-invalid";
  if (stateObj.state === "DEGRADED") return "lookup-degraded";
  return "";
}

// eslint-disable-next-line no-unused-vars
function getConceptStatusText(fieldId) {
  const stateObj = conceptValidationStates[fieldId];
  if (!stateObj) return "";
  if (stateObj.state === "VALID") {
    return `Code is valid: "${stateObj.decode}"`;
  }
  if (stateObj.state === "INVALID") {
    return `Invalid code: ${stateObj.errorMessage || ""}`;
  }
  if (stateObj.state === "DEGRADED") {
    return `Terminology service degraded. ${stateObj.errorMessage || ""}`;
  }
  return "";
}

// Live validation states
const requestCounters = reactive({});
const conceptStatuses = reactive({});
const conceptMessages = reactive({});

const debouncedValidate = debounce(async (fieldId, value) => {
  if (!value || !value.trim()) {
    conceptStatuses[fieldId] = "none";
    conceptMessages[fieldId] = "";
    return;
  }

  requestCounters[fieldId] = (requestCounters[fieldId] || 0) + 1;
  const currentReqId = requestCounters[fieldId];

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      changeReason: "Validate code",
    });

    if (requestCounters[fieldId] !== currentReqId) {
      return; // Discard stale response
    }

    if (res.state === "VALID") {
      conceptStatuses[fieldId] = "valid";
      conceptMessages[fieldId] = `Code is valid: "${res.decode}"`;
    } else if (res.state === "INVALID") {
      conceptStatuses[fieldId] = "invalid";
      conceptMessages[fieldId] = `Invalid code: "${value}"`;
    } else if (res.state === "DEGRADED") {
      conceptStatuses[fieldId] = "degraded";
      conceptMessages[fieldId] =
        res.error_message ||
        "Terminology service degraded. Validation offline.";
    }
  } catch {
    if (requestCounters[fieldId] !== currentReqId) {
      return;
    }
    conceptStatuses[fieldId] = "degraded";
    conceptMessages[fieldId] =
      "Terminology service degraded. Validation offline.";
  }
}, 300);

function handleConceptInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (!value || !value.trim()) {
    conceptStatuses[fieldId] = "none";
    conceptMessages[fieldId] = "";
    return;
  }

  conceptStatuses[fieldId] = "loading";
  conceptMessages[fieldId] = "Searching terminology database...";

  debouncedValidate(fieldId, value);
}

// Deep watch formValues to evaluate rules debounced
watch(
  () => store.formValues,
  () => {
    store.triggerValueChange();
  },
  { deep: true }
);

onMounted(() => {
  store.evaluateRules();
  // Initialize lookup validation for any pre-populated concept_code fields on mount
  store.ecrfFields.forEach((field) => {
    if (field.type === "concept_code" && store.formValues[field.id]) {
      handleConceptInput(field, store.formValues[field.id]);
    }
  });
});

// Lookup Status States
const lookupStatuses = ref({});
const lastLookupRequestIds = {};
const debounceTimers = {};

async function performConceptCodeValidation(fieldId, value) {
  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  if (lastLookupRequestIds[fieldId] === undefined) {
    lastLookupRequestIds[fieldId] = 0;
  }
  lastLookupRequestIds[fieldId]++;
  const requestId = lastLookupRequestIds[fieldId];

  lookupStatuses.value[fieldId] = {
    status: "loading",
    message: "Searching terminology database...",
  };

  try {
    const res = await terminologyClient.validateSingleCode(value, {
      changeReason: "Validate code",
    });

    if (requestId !== lastLookupRequestIds[fieldId]) {
      return;
    }

    if (res.state === "VALID") {
      lookupStatuses.value[fieldId] = {
        status: "valid",
        message: `Code is valid: "${res.decode}"`,
      };
    } else if (res.state === "INVALID") {
      lookupStatuses.value[fieldId] = {
        status: "invalid",
        message: `Invalid code "${value}". Not found in NCI Thesaurus.`,
      };
    } else if (res.state === "DEGRADED") {
      lookupStatuses.value[fieldId] = {
        status: "degraded",
        message: "Terminology service degraded. Validation offline.",
      };
    }
  } catch (error) {
    if (requestId !== lastLookupRequestIds[fieldId]) {
      return;
    }
    lookupStatuses.value[fieldId] = {
      status: "degraded",
      message:
        error.message || "Terminology service degraded. Validation offline.",
    };
  }
}

function handleLookupInput(field, value) {
  const fieldId = field.id;
  store.formValues[fieldId] = value;

  if (debounceTimers[fieldId]) {
    clearTimeout(debounceTimers[fieldId]);
  }

  if (!value || !value.trim()) {
    lookupStatuses.value[fieldId] = null;
    return;
  }

  debounceTimers[fieldId] = setTimeout(() => {
    performConceptCodeValidation(fieldId, value);
  }, 300);
}

// Reason Modal States
const showReasonModal = ref(false);
const pendingValueChange = ref(null);

// Re-authentication Modal States
const showReauthModal = ref(false);
const reauthUsername = ref(store.user.username);
const reauthPassword = ref("");
const reauthTotp = ref("");
const reauthError = ref("");
const reauthAction = ref(""); // "CLOSE_QUERY" or "BATCH_SIGN_OFF"
const pendingCloseQueryFieldId = ref(null);

// PI Sign-Off Worklist States
const signoffTargetType = ref("FORM"); // "FORM", "VISIT", "SUBJECT"
const signoffTargetId = ref("");
const customTargetId = ref("");
const signoffReason = ref("PI approval and sign-off.");

const availableSubjects = ref(["SUBJ-001", "SUBJ-002", "SUBJ-003"]);
const availableVisits = ref(["V-SCR", "V-TRT-A1", "V-TRT-A2", "V-TRT-B1"]);
const availableFormSubmissions = ref(["FSUB-001", "FSUB-002", "FSUB-003"]);

const validSigningReasons = [
  "I attest that this data is accurate and complete.",
  "PI approval and sign-off.",
  "Review and confirmation.",
  "DATA_RECORDING",
  "DATA_ENTRY_COMPLETED",
  "PI_REVIEW",
  "PI_SIGN_OFF",
  "COMPLIANCE_ATTESTATION",
];

function getValidationError(field) {
  const value = store.formValues[field.id];
  const res = validateField(field, value, store.formValues, evaluateAST);
  return res.valid ? null : res.message;
}

// Reason Modal logic
function handleFieldChange(field, newValue, targetEl) {
  const oldValue = store.formValues[field.id] || "";
  if (newValue === oldValue) return;

  if (oldValue !== "" && oldValue !== null && oldValue !== undefined) {
    pendingValueChange.value = {
      field,
      oldValue,
      newValue,
      targetEl,
    };
    showReasonModal.value = true;
  } else {
    commitChange(field, oldValue, newValue, "Initial Entry");
  }
}

function cancelChange() {
  if (pendingValueChange.value && pendingValueChange.value.targetEl) {
    if (pendingValueChange.value.targetEl.type === "radio") {
      // Vue handles radio binding automatically
    } else {
      pendingValueChange.value.targetEl.value =
        pendingValueChange.value.oldValue;
    }
  }
  showReasonModal.value = false;
  pendingValueChange.value = null;
}

function saveChange(finalReason) {
  if (!pendingValueChange.value) return;

  commitChange(
    pendingValueChange.value.field,
    pendingValueChange.value.oldValue,
    pendingValueChange.value.newValue,
    finalReason
  );

  showReasonModal.value = false;
  pendingValueChange.value = null;
}

function commitChange(field, oldValue, newValue, reason) {
  store.formValues[field.id] = newValue;
  store.addLedgerBlock(
    "FIELD_CHANGE",
    {
      fieldId: field.id,
      label: field.label,
      cdash: field.cdash || "",
      oldValue,
      newValue,
    },
    reason
  );
}

// Query Operations
function createQuery(fieldId, msgFromComponent = null) {
  const msg =
    msgFromComponent !== null
      ? msgFromComponent
      : (queryInputs[fieldId] || "").trim();
  if (!msg) {
    alert("Please enter a discrepancy message!");
    return;
  }

  const queryObj = {
    status: "OPEN",
    message: msg,
    createdBy: "Data Monitor (Offline Client)",
    createdAt: new Date().toISOString().slice(0, 10),
  };

  store.formQueries[fieldId] = queryObj;
  store.addLedgerBlock(
    "QUERY_CREATE",
    { fieldId, query: queryObj },
    `Raised discrepancy: "${msg}"`
  );
}

function respondQuery(fieldId, respFromComponent = null) {
  const resp =
    respFromComponent !== null
      ? respFromComponent
      : (queryResponses[fieldId] || "").trim();
  if (!resp) {
    alert("Please enter a response!");
    return;
  }

  const queryObj = store.formQueries[fieldId];
  queryObj.status = "ANSWERED";
  queryObj.response = resp;
  queryObj.respondedBy = "Clinical Investigator (Offline Client)";
  queryObj.respondedAt = new Date().toISOString().slice(0, 10);

  store.addLedgerBlock(
    "QUERY_RESPOND",
    { fieldId, query: queryObj },
    `Responded to query: "${resp}"`
  );
}

function closeQuery(fieldId) {
  pendingCloseQueryFieldId.value = fieldId;
  reauthAction.value = "CLOSE_QUERY";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

function handleSignOffSubmit() {
  const targetId =
    signoffTargetId.value === "custom"
      ? customTargetId.value
      : signoffTargetId.value;
  if (!targetId || !targetId.trim()) {
    alert("Please select or enter a valid Target ID first.");
    return;
  }
  reauthAction.value = "BATCH_SIGN_OFF";
  reauthUsername.value =
    store.user.username || authStore.identity?.username || "fderuiter";
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  showReauthModal.value = true;
}

function cancelReauth() {
  showReauthModal.value = false;
  reauthPassword.value = "";
  reauthTotp.value = "";
  reauthError.value = "";
  pendingCloseQueryFieldId.value = null;
  reauthAction.value = "";
}

async function confirmReauth() {
  if (!reauthPassword.value) {
    reauthError.value = "Password is required.";
    return;
  }

  const username = reauthUsername.value;
  const password = reauthPassword.value;
  const totp = reauthTotp.value || null;
  const action = reauthAction.value;

  // Immediately clear password to ensure GxP compliance & no state leak
  reauthPassword.value = "";

  if (action === "CLOSE_QUERY") {
    const fieldId = pendingCloseQueryFieldId.value;
    if (fieldId) {
      const queryObj = store.formQueries[fieldId];
      queryObj.status = "CLOSED";
      queryObj.closedBy = `${username} (Offline Client)`;
      queryObj.closedAt = new Date().toISOString().slice(0, 10);

      const fieldMeta = store.ecrfFields.find((f) => f.id === fieldId);
      const cdash = fieldMeta ? fieldMeta.cdash : "";
      const [domain, testCode] = cdash
        ? cdash.split(".")
        : ["VS", fieldId.toUpperCase()];

      store.addLedgerBlock(
        "QUERY_CLOSE",
        {
          fieldId,
          studyId: store.currentUsdm.studyId || "STUDY-USDM-001",
          subjectId: "SUBJ-001",
          visitId: "Screening",
          domain,
          testCode,
          query: queryObj,
        },
        "Discrepancy resolved and closed permanently."
      );
      pendingCloseQueryFieldId.value = null;
    }

    showReauthModal.value = false;
    reauthError.value = "";
    alert(
      "Identity verified. Query closed and logged to cryptographic ledger."
    );
  } else if (action === "BATCH_SIGN_OFF") {
    try {
      reauthError.value = "";

      const studyId = store.currentUsdm.studyId || "STUDY-USDM-001";
      const targetType = signoffTargetType.value;
      const targetId =
        signoffTargetId.value === "custom"
          ? customTargetId.value
          : signoffTargetId.value;
      const targetIds = [targetId];
      const signingReason = signoffReason.value;

      // Compute canonical batch binding
      const normStudy = studyId.trim();
      const normType = targetType.trim().toUpperCase();
      const normIds = [...targetIds]
        .map((id) => String(id).trim())
        .sort()
        .join(",");
      const normReason = signingReason.trim();
      const bindingStr = `${normStudy}:${normType}:${normIds}:${normReason}`;

      // Calculate SHA-256 batchId
      const msgBuffer = new TextEncoder().encode(bindingStr);
      const hashBuffer = await crypto.subtle.digest("SHA-256", msgBuffer);
      const hashArray = Array.from(new Uint8Array(hashBuffer));
      const batchId = hashArray
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");

      // 1. Obtain signature token
      const reauthRes = await soaClient.verifySignature(
        {
          username,
          password,
          totp,
          action: "/api/v1/execution/batch-sign-off",
          batchId,
        },
        authStore.accessToken
      );

      const sigToken = reauthRes.sig_token;

      // 2. Call batch sign-off
      const signoffRes = await soaClient.batchSignOff(
        {
          studyId,
          targetType,
          targetIds,
          signingReason,
        },
        {
          userId: username,
          roles: store.user.roles ? store.user.roles.join(",") : "investigator",
          changeReason: signingReason,
          sigToken,
        },
        authStore.accessToken
      );

      // 3. Document in ledger
      await store.addLedgerBlock(
        "BATCH_SIGN_OFF_SUCCESS",
        {
          targetType: signoffTargetType.value,
          targetIds: [targetId],
          signingReason: signoffReason.value,
          result: signoffRes,
        },
        `PI electronic sign-off approved: ${signoffReason.value}`
      );

      // Clean up variables & UI state
      showReauthModal.value = false;
      reauthTotp.value = "";
      alert(
        `Signature Token obtained successfully.\nBatch sign-off completed for ${signoffTargetType.value} ${targetId}!`
      );
    } catch (err) {
      // Explicitly wipe credentials on failure
      reauthPassword.value = "";
      reauthTotp.value = "";

      if (err.message === "REAUTHENTICATION_REQUIRED" || err.status === 401) {
        reauthError.value =
          "Identity verification expired or invalid. Please try again.";
        showReauthModal.value = true;
      } else {
        reauthError.value = err.message || "Failed to complete batch sign-off.";
      }
    }
  }
}

function reopenQuery(fieldId) {
  const queryObj = store.formQueries[fieldId];
  queryObj.status = "REOPENED";
  queryObj.message =
    queryObj.message + " [Reopened due to insufficient response]";

  store.addLedgerBlock(
    "QUERY_REOPEN",
    { fieldId, query: queryObj },
    "Investigator response was rejected. Query reopened."
  );
}

function clearForm() {
  store.ecrfFields.forEach((f) => {
    store.formValues[f.id] = "";
    delete store.formQueries[f.id];
  });
  store.addLedgerBlock(
    "FORM_CLEAR",
    { formId: "VS_DEMO" },
    "All eCRF form fields cleared by clinical staff."
  );
}

function submitEcrf() {
  let allValid = true;
  let errMsgs = [];

  store.ecrfFields.forEach((f) => {
    const res = validateField(
      f,
      store.formValues[f.id],
      store.formValues,
      evaluateAST
    );
    if (!res.valid) {
      allValid = false;
      errMsgs.push(`${f.label}: ${res.message}`);
    }
  });

  if (!allValid) {
    alert(
      "Cannot submit eCRF! The form contains validation errors:\n\n" +
        errMsgs.join("\n")
    );
    return;
  }

  store.addLedgerBlock(
    "SESSION_SUBMIT",
    {
      formId: "VS_DEMO",
      formValues: store.formValues,
      formQueries: store.formQueries,
    },
    "eCRF successfully verified, finalized, and electronically submitted."
  );

  alert(
    "eCRF Session successfully submitted to secure cryptographic database!"
  );
}

// Ingestion Review Setup Variables & Logic
const fileInputRef = ref(null);
const selectedFileName = ref("");
const editingItemId = ref(null);
const editItemValue = ref("");
const editItemReason = ref("");
const rejectingItemId = ref(null);
const rejectItemReason = ref("");
const promoteChangeReason = ref("");

function triggerFileSelect() {
  if (fileInputRef.value) {
    fileInputRef.value.click();
  }
}

const unreviewedCount = computed(() => {
  if (!store.candidateDraft || !store.candidateDraft.items) return 0;
  return Object.values(store.candidateDraft.items).filter(
    (item) => item.review_status === "PENDING"
  ).length;
});

async function triggerDocumentUpload(event) {
  const file = event.target.files[0];
  if (!file) return;

  selectedFileName.value = file.name;
  try {
    await store.uploadProtocolDocument(
      file,
      "Uploader triggers protocol ingestion scan."
    );
    alert("Protocol Document ingested successfully. Candidate draft loaded.");
  } catch (err) {
    alert("Ingestion failed: " + err.message);
  }
}

function getConfidenceClass(level) {
  if (level === "auto") return "lookup-valid";
  if (level === "needs-review") return "lookup-degraded";
  return "lookup-invalid";
}

function getStatusClass(status) {
  if (status === "ACCEPTED") return "lookup-valid";
  if (status === "EDITED") return "lookup-degraded";
  if (status === "REJECTED") return "lookup-invalid";
  return "";
}

async function acceptItem(itemId) {
  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "ACCEPTED",
      "Accepted by clinical reviewer"
    );
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

function startEditItem(item) {
  editingItemId.value = item.id;
  editItemValue.value = item.type === "visit" ? item.name : item.label;
  editItemReason.value = "";
}

function cancelEditItem() {
  editingItemId.value = null;
  editItemValue.value = "";
  editItemReason.value = "";
}

async function saveEditItem(itemId) {
  if (!editItemReason.value.trim()) {
    alert("Change reason justification is mandatory for edits!");
    return;
  }
  const item = store.candidateDraft.items[itemId];
  const payload =
    item.type === "visit"
      ? { name: editItemValue.value }
      : { label: editItemValue.value };

  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "EDITED",
      editItemReason.value,
      payload
    );
    editingItemId.value = null;
    editItemValue.value = "";
    editItemReason.value = "";
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

function startRejectItem(itemId) {
  rejectingItemId.value = itemId;
  rejectItemReason.value = "";
}

function cancelRejectItem() {
  rejectingItemId.value = null;
  rejectItemReason.value = "";
}

async function confirmRejectItem(itemId) {
  if (!rejectItemReason.value.trim()) {
    alert("Change reason justification is mandatory for rejection!");
    return;
  }
  try {
    await store.transitionCandidateItemState(
      store.candidateDraft.id,
      itemId,
      "REJECTED",
      rejectItemReason.value
    );
    rejectingItemId.value = null;
    rejectItemReason.value = "";
  } catch (err) {
    alert("Transition failed: " + err.message);
  }
}

async function promoteCandidate() {
  if (!promoteChangeReason.value.trim()) {
    alert("Promotion change reason justification is mandatory!");
    return;
  }
  try {
    await store.promoteCandidateDraft(
      store.candidateDraft.id,
      promoteChangeReason.value
    );
    alert("Candidate promoted successfully into formal DRAFT version!");
    promoteChangeReason.value = "";
  } catch (err) {
    alert("Promotion failed: " + err.message);
  }
}
</script>
