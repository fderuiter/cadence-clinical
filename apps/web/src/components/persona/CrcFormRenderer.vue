<template>
  <div class="crc-form-renderer">
    <div class="card">
      <div class="card-title">Subject eCRF Data Entry Form</div>

      <!-- Subject & Visit Selection Panel -->
      <div
        style="
          display: flex;
          flex-wrap: wrap;
          gap: var(--spacing-md);
          margin-bottom: var(--spacing-md);
          border-bottom: 1px solid var(--border);
          padding-bottom: 12px;
        "
      >
        <div class="form-group" style="flex: 1">
          <label for="ecrf-subject-selector" style="font-weight: bold"
            >Active Subject ID</label
          >
          <select
            id="ecrf-subject-selector"
            :value="selectedSubjectId"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="
              $emit('update:selectedSubjectId', $event.target.value);
              $emit('load-ecrf-session');
            "
          >
            <option
              v-for="sub in store?.subjects || clinicalStore.subjects"
              :key="sub.id"
              :value="sub.id"
            >
              {{ sub.label || `${sub.id} (${sub.status})` }}
            </option>
          </select>
        </div>
        <div class="form-group" style="flex: 1">
          <label for="ecrf-visit-selector" style="font-weight: bold"
            >Active Visit / Encounter</label
          >
          <select
            id="ecrf-visit-selector"
            :value="selectedVisitId"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="
              $emit('update:selectedVisitId', $event.target.value);
              $emit('load-ecrf-session');
            "
          >
            <option value="Screening">Screening / Day -7</option>
            <option value="Week2">Week 2 Treatment</option>
            <option value="Week4">Week 4 Treatment</option>
          </select>
        </div>
      </div>

      <!-- Protocol Amendment Re-Consent Gating Banner -->
      <div
        v-if="isReconsentGated"
        id="reconsent-gating-banner"
        class="reconsent-banner"
        style="
          background-color: #fef2f2;
          border: 2px solid #ef4444;
          border-radius: 8px;
          padding: 16px 20px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 12px;
        "
      >
        <div>
          <div
            style="
              font-size: 1.05rem;
              font-weight: 700;
              color: #991b1b;
              display: flex;
              align-items: center;
              gap: 8px;
            "
          >
            <span>⚠️</span>
            <span
              >Protocol Amendment Active (v2.0.0) — Re-Consent Required</span
            >
          </div>
          <p style="margin: 4px 0 0 0; font-size: 0.875rem; color: #7f1d1d">
            This subject must complete re-consent for Protocol Version 2.0.0
            before further visit data entry can be saved (PRD-SUB-007). All
            input fields are currently locked.
          </p>
        </div>
        <div style="display: flex; gap: 8px; flex-wrap: wrap">
          <button
            id="btn-open-econsent"
            type="button"
            class="btn btn-primary"
            style="
              background-color: #dc2626;
              color: white;
              font-weight: bold;
              padding: 8px 14px;
              font-size: 0.85rem;
              border-radius: 6px;
              border: none;
              cursor: pointer;
            "
            @click="$emit('open-econsent-modal')"
          >
            ✍️ Open eConsent Form
          </button>
          <button
            id="btn-upload-paper-icf"
            type="button"
            class="btn btn-secondary"
            style="
              background-color: #ffffff;
              color: #991b1b;
              border: 1px solid #f87171;
              font-weight: bold;
              padding: 8px 14px;
              font-size: 0.85rem;
              border-radius: 6px;
              cursor: pointer;
            "
            @click="$emit('open-paper-icf-modal')"
          >
            📄 Upload Signed Paper ICF
          </button>
        </div>
      </div>

      <!-- Batch Verification Action Bar (CRA Persona Integration Slot / Element) -->
      <slot name="batch-sdv-bar"></slot>

      <form
        id="form-VS_DEMO"
        class="clinical-form responsive-grid"
        @submit.prevent
      >
        <fieldset
          :disabled="isReconsentGated"
          style="border: none; padding: 0; margin: 0; display: contents"
        >
          <template v-for="field in store.ecrfFields" :key="field.id">
            <div
              v-show="store.fieldVisibility[field.id] !== false"
              :style="`grid-column: span ${field.gridSpan || 12}; display: flex; flex-direction: column; gap: 8px;`"
              style="margin-bottom: 8px"
            >
              <ClinicalFormField
                :field="field"
                :model-value="store.formValues[field.id]"
                :query="store.formQueries[field.id]"
                :error="getValidationError(field)"
                :lookup-status="lookupStatuses[field.id]"
                :can-manage-queries="store.canManageQueries"
                :query-label="store.getQueryLabel(store.formQueries[field.id])"
                @update:model-value="updateFieldValue(field.id, $event)"
                @input="$emit('handle-lookup-input', field, $event)"
                @change="
                  (val, target) =>
                    $emit('handle-field-change', field, val, target)
                "
                @create-query="$emit('create-query', field.id, $event)"
                @respond-query="$emit('respond-query', field.id, $event)"
                @close-query="$emit('close-query', field.id)"
                @reopen-query="$emit('reopen-query', field.id)"
              />

              <!-- Single Field SDV Checkbox -->
              <div
                v-if="isCraUser"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  background-color: #f0fdf4;
                  border: 1px dashed #bbf7d0;
                  padding: 8px;
                  border-radius: 4px;
                  margin-top: -6px;
                "
                class="sdv-box"
              >
                <input
                  :id="`sdv-${field.id}`"
                  type="checkbox"
                  :checked="sdvStates[getSdvKey(field.id)] === true"
                  style="cursor: pointer"
                  @change="
                    $emit('handle-sdv-toggle', field.id, $event.target.checked)
                  "
                />
                <label
                  :for="`sdv-${field.id}`"
                  style="
                    font-size: 0.8rem;
                    color: #166534;
                    font-weight: 600;
                    margin: 0;
                    cursor: pointer;
                  "
                >
                  Source Document Verified (SDV)
                </label>
              </div>

              <!-- Batch SDV Selection Checkbox -->
              <div
                v-if="isAuthorizedForBulkSdv"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  background-color: #eff6ff;
                  border: 1px dashed #bfdbfe;
                  padding: 8px;
                  border-radius: 4px;
                  margin-top: 4px;
                "
                class="batch-sdv-box"
              >
                <input
                  :id="`batch-sdv-${field.id}`"
                  type="checkbox"
                  :value="field.id"
                  :checked="selectedBatchFields.includes(field.id)"
                  style="cursor: pointer"
                  class="batch-sdv-checkbox"
                  @change="toggleBatchField(field.id, $event.target.checked)"
                />
                <label
                  :for="`batch-sdv-${field.id}`"
                  style="
                    font-size: 0.8rem;
                    color: #1e40af;
                    font-weight: 600;
                    margin: 0;
                    cursor: pointer;
                  "
                >
                  Select for Batch SDV
                </label>
              </div>
            </div>
          </template>
        </fieldset>
      </form>

      <div class="form-actions">
        <button
          id="btn-clear-ecrf"
          class="btn"
          :disabled="isReconsentGated"
          @click="$emit('clear-form')"
        >
          Clear Form
        </button>
        <button
          id="btn-submit-ecrf"
          class="btn btn-primary"
          :disabled="isReconsentGated"
          @click="$emit('submit-ecrf')"
        >
          {{
            isReconsentGated
              ? "Locked (Re-Consent Required)"
              : "Submit eCRF Session"
          }}
        </button>
      </div>
    </div>

    <!-- eConsent Signing Modal Dialog -->
    <div
      v-if="showEconsentModal"
      id="econsent-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal" style="max-width: 520px">
        <div class="modal-header">
          Execute Electronic Re-Consent (ICF v2.0.0)
        </div>
        <div class="modal-body">
          <p>
            Recording 21 CFR Part 11 compliant digital informed consent for
            Subject <strong>{{ selectedSubjectId }}</strong> under Protocol
            Version <strong>2.0.0</strong>.
          </p>
          <div class="form-group" style="margin-bottom: 12px">
            <label style="font-size: 0.85rem; font-weight: 600"
              >Signer Printed Name:</label
            >
            <input
              :value="econsentSignerName"
              type="text"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
              placeholder="Full legal name of subject"
              @input="$emit('update:econsentSignerName', $event.target.value)"
            />
          </div>
          <div class="form-group" style="margin-bottom: 12px">
            <label style="font-size: 0.85rem; font-weight: 600"
              >Consent Declaration:</label
            >
            <div
              style="
                background: #f8fafc;
                padding: 8px 12px;
                border-radius: 4px;
                border: 1px solid var(--border);
                font-size: 0.8rem;
                color: var(--text-muted);
              "
            >
              "I confirm that I have reviewed the amended protocol details
              (v2.0.0) and agree to continue participation."
            </div>
          </div>
        </div>
        <div
          class="modal-footer"
          style="
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            padding: 12px 16px;
          "
        >
          <button
            class="btn btn-secondary"
            @click="$emit('update:showEconsentModal', false)"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary"
            style="background-color: #2563eb; color: white"
            :disabled="!econsentSignerName.trim() || reconsentSubmitting"
            @click="$emit('handle-complete-reconsent', 'ECONSENT')"
          >
            {{
              reconsentSubmitting ? "Signing..." : "Confirm & Sign ICF v2.0.0"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- Paper ICF Upload Modal Dialog -->
    <div
      v-if="showPaperIcfModal"
      id="paper-icf-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div class="modal" style="max-width: 520px">
        <div class="modal-header">Register Signed Paper ICF (v2.0.0)</div>
        <div class="modal-body">
          <p>
            Upload or register site-verified paper Informed Consent Form for
            Subject <strong>{{ selectedSubjectId }}</strong
            >.
          </p>
          <div class="form-group" style="margin-bottom: 12px">
            <label style="font-size: 0.85rem; font-weight: 600"
              >Date ICF Signed by Subject:</label
            >
            <input
              :value="paperIcfDate"
              type="date"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
              @input="$emit('update:paperIcfDate', $event.target.value)"
            />
          </div>
          <div class="form-group" style="margin-bottom: 12px">
            <label style="font-size: 0.85rem; font-weight: 600"
              >Investigator Verification Note:</label
            >
            <input
              :value="paperIcfNote"
              type="text"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
              placeholder="Paper ICF verified and archived in ISF binder."
              @input="$emit('update:paperIcfNote', $event.target.value)"
            />
          </div>
        </div>
        <div
          class="modal-footer"
          style="
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            padding: 12px 16px;
          "
        >
          <button
            class="btn btn-secondary"
            @click="$emit('update:showPaperIcfModal', false)"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary"
            style="background-color: #2563eb; color: white"
            :disabled="reconsentSubmitting"
            @click="$emit('handle-complete-reconsent', 'PAPER_UPLOAD')"
          >
            {{ reconsentSubmitting ? "Uploading..." : "Verify & Unlock eCRF" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ClinicalFormField } from "ui";
import { useClinicalStore } from "../../stores/clinical";

const clinicalStore = useClinicalStore();

const props = defineProps({
  store: { type: Object, required: true },
  selectedSubjectId: { type: String, required: true },
  selectedVisitId: { type: String, required: true },
  isReconsentGated: { type: Boolean, required: true },
  showEconsentModal: { type: Boolean, required: true },
  showPaperIcfModal: { type: Boolean, required: true },
  reconsentSubmitting: { type: Boolean, required: true },
  econsentSignerName: { type: String, required: true },
  paperIcfDate: { type: String, required: true },
  paperIcfNote: { type: String, required: true },
  lookupStatuses: { type: Object, required: true },
  getValidationError: { type: Function, required: true },
  isCraUser: { type: Boolean, required: true },
  isAuthorizedForBulkSdv: { type: Boolean, required: true },
  sdvStates: { type: Object, required: true },
  getSdvKey: { type: Function, required: true },
  selectedBatchFields: { type: Array, required: true },
});

const emit = defineEmits([
  "update:selectedSubjectId",
  "update:selectedVisitId",
  "update:showEconsentModal",
  "update:showPaperIcfModal",
  "update:econsentSignerName",
  "update:paperIcfDate",
  "update:paperIcfNote",
  "update:selectedBatchFields",
  "load-ecrf-session",
  "open-econsent-modal",
  "open-paper-icf-modal",
  "handle-complete-reconsent",
  "handle-lookup-input",
  "handle-field-change",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
  "handle-sdv-toggle",
  "clear-form",
  "submit-ecrf",
]);

function updateFieldValue(fieldId, val) {
  const storeInstance = props.store || clinicalStore;
  storeInstance.formValues[fieldId] = val;
}

function toggleBatchField(fieldId, checked) {
  const updated = [...props.selectedBatchFields];
  if (checked) {
    if (!updated.includes(fieldId)) updated.push(fieldId);
  } else {
    const idx = updated.indexOf(fieldId);
    if (idx !== -1) updated.splice(idx, 1);
  }
  emit("update:selectedBatchFields", updated);
}
</script>
