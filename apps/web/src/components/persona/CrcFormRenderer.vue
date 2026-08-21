<template>
  <div class="crc-form-renderer">
    <div class="card">
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: var(--spacing-sm);
        "
      >
        <div
          class="card-title"
          style="margin-bottom: 0"
        >
          Subject eCRF Data Entry Form
        </div>
        <div style="display: flex; align-items: center; gap: 8px">
          <span
            class="badge"
            :style="{
              backgroundColor:
                activeSubjectVersion === '2.0.0' ? '#dbeafe' : '#f1f5f9',
              color: activeSubjectVersion === '2.0.0' ? '#1e40af' : '#475569',
              padding: '4px 8px',
              borderRadius: '4px',
              fontWeight: 600,
              fontSize: '0.75rem',
            }"
          >
            Protocol Schema v{{ activeSubjectVersion }}
          </span>
          <button
            id="btn-enroll-subject"
            type="button"
            class="btn btn-secondary"
            style="
              display: inline-flex;
              align-items: center;
              gap: 4px;
              padding: 6px 12px;
              font-size: 0.85rem;
              font-weight: 600;
              border: 1px solid var(--border);
              border-radius: 4px;
              cursor: pointer;
            "
            @click="showEnrollModal = true"
          >
            <span>➕</span> Enroll New Subject
          </button>
        </div>
      </div>

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
        <div
          class="form-group"
          style="flex: 1"
        >
          <label
            for="ecrf-subject-selector"
            style="font-weight: bold"
          >Active Subject ID</label>
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
        <div
          class="form-group"
          style="flex: 1"
        >
          <label
            for="ecrf-visit-selector"
            style="font-weight: bold"
          >Active Visit / Encounter</label>
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
            <option value="Screening">
              Screening / Day -7
            </option>
            <option value="Week2">
              Week 2 Treatment
            </option>
            <option value="Week4">
              Week 4 Treatment
            </option>
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
            <span>Protocol Amendment Active (v2.0.0) — Re-Consent Required</span>
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

      <!-- Live Edit Checks Summary Bar -->
      <div
        v-if="activeEditCheckAlerts.length > 0"
        id="edit-checks-summary-bar"
        style="
          background-color: #fffbeb;
          border: 1px solid #fde68a;
          border-left: 4px solid #f59e0b;
          border-radius: 6px;
          padding: 10px 14px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 8px;
        "
      >
        <div
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 0.875rem;
            color: #92400e;
            font-weight: 600;
          "
        >
          <span>⚡ Live Edit Checks:</span>
          <span
            v-if="discrepancyAlertsCount > 0"
            class="badge badge-discrepancy"
            style="
              background-color: #ef4444;
              color: white;
              padding: 2px 6px;
              border-radius: 4px;
              font-size: 0.75rem;
            "
          >
            {{ discrepancyAlertsCount }} Discrepancy
          </span>
          <span
            v-if="warningAlertsCount > 0"
            class="badge badge-warning"
            style="
              background-color: #f59e0b;
              color: white;
              padding: 2px 6px;
              border-radius: 4px;
              font-size: 0.75rem;
            "
          >
            {{ warningAlertsCount }} Warning
          </span>
        </div>
        <div style="font-size: 0.8rem; color: #b45309">
          Real-time rule evaluation active
        </div>
      </div>

      <!-- Batch Verification Action Bar (CRA Persona Integration Slot / Element) -->
      <slot name="batch-sdv-bar" />

      <form
        id="form-VS_DEMO"
        class="clinical-form responsive-grid"
        @submit.prevent
      >
        <fieldset
          :disabled="isReconsentGated"
          style="border: none; padding: 0; margin: 0; display: contents"
          @click="handleFieldsetInteraction"
        >
          <template
            v-for="field in renderedFields"
            :key="field.id"
          >
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

              <!-- Live Edit Check Inline Banner -->
              <template
                v-for="alert in getFieldAlerts(field.id)"
                :key="alert.id"
              >
                <div
                  :id="`edit-check-banner-${field.id}`"
                  class="edit-check-banner"
                  :style="{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: '8px',
                    padding: '6px 10px',
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    backgroundColor:
                      alert.severity === 'DISCREPANCY' ? '#fef2f2' : '#fffbeb',
                    border:
                      alert.severity === 'DISCREPANCY'
                        ? '1px solid #f87171'
                        : '1px solid #fde68a',
                    color:
                      alert.severity === 'DISCREPANCY' ? '#991b1b' : '#92400e',
                  }"
                >
                  <div style="display: flex; align-items: center; gap: 6px">
                    <span
                      :class="`badge badge-${alert.severity.toLowerCase()}`"
                      :style="{
                        backgroundColor:
                          alert.severity === 'DISCREPANCY'
                            ? '#ef4444'
                            : '#f59e0b',
                        color: 'white',
                        padding: '2px 6px',
                        borderRadius: '4px',
                        fontSize: '0.7rem',
                        fontWeight: 'bold',
                        letterSpacing: '0.5px',
                      }"
                    >
                      {{ alert.severity }}
                    </span>
                    <span>{{ alert.message }}</span>
                  </div>
                  <button
                    v-if="!store.formQueries[field.id]"
                    type="button"
                    class="btn-raise-query"
                    style="
                      background: transparent;
                      border: 1px solid currentColor;
                      border-radius: 4px;
                      padding: 2px 6px;
                      font-size: 0.75rem;
                      font-weight: 600;
                      cursor: pointer;
                      color: inherit;
                    "
                    @click="raiseQueryForAlert(field.id, alert.message)"
                  >
                    Raise Query
                  </button>
                </div>
              </template>

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
                >
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
                >
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
          :style="{
            backgroundColor: isReconsentGated ? '#dc2626' : undefined,
            opacity: isReconsentGated ? 0.9 : 1,
          }"
          @click="handleSubmitClick"
        >
          {{
            isReconsentGated
              ? "Locked (Re-Consent Required)"
              : "Submit eCRF Session"
          }}
        </button>
      </div>
    </div>

    <!-- Subject Enrollment Modal Dialog -->
    <div
      v-if="showEnrollModal"
      id="enroll-subject-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 520px"
      >
        <div class="modal-header">
          ➕ Enroll New Clinical Subject
        </div>
        <div class="modal-body">
          <p style="font-size: 0.9rem; color: var(--text-muted)">
            Assign subject identifier, study site, initial protocol consent
            date, and treatment arm (PRD-SYS-001).
          </p>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label
              for="enroll-subject-id"
              style="font-size: 0.85rem; font-weight: 600"
            >Subject ID:</label>
            <input
              id="enroll-subject-id"
              v-model="enrollForm.subjectId"
              type="text"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
              placeholder="e.g. SUBJ-101-011"
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label
              for="enroll-site-id"
              style="font-size: 0.85rem; font-weight: 600"
            >Site ID:</label>
            <select
              id="enroll-site-id"
              v-model="enrollForm.siteId"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
            >
              <option value="SITE-101">
                SITE-101 (Main Clinical Center)
              </option>
              <option value="SITE-102">
                SITE-102 (Regional Oncology Center)
              </option>
              <option value="SITE-103">
                SITE-103 (Metropolitan Research Site)
              </option>
            </select>
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label
              for="enroll-consent-date"
              style="font-size: 0.85rem; font-weight: 600"
            >Informed Consent Date:</label>
            <input
              id="enroll-consent-date"
              v-model="enrollForm.consentDate"
              type="date"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label
              for="enroll-arm-id"
              style="font-size: 0.85rem; font-weight: 600"
            >Assigned Study Arm:</label>
            <select
              id="enroll-arm-id"
              v-model="enrollForm.armId"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
            >
              <option value="ARM-A">
                Arm A: Active Cadence-001 (10mg/day)
              </option>
              <option value="ARM-B">
                Arm B: Placebo Control
              </option>
            </select>
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label
              for="enroll-change-reason"
              style="font-size: 0.85rem; font-weight: 600"
            >GxP Reason for Change:</label>
            <input
              id="enroll-change-reason"
              v-model="enrollForm.changeReason"
              type="text"
              class="form-control"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                box-sizing: border-box;
              "
              placeholder="Initial subject enrollment"
            >
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
            id="btn-cancel-enroll"
            class="btn btn-secondary"
            @click="showEnrollModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-enroll"
            class="btn btn-primary"
            style="background-color: #2563eb; color: white"
            :disabled="!enrollForm.subjectId.trim()"
            @click="handleEnrollSubmit"
          >
            Enroll Subject
          </button>
        </div>
      </div>
    </div>

    <!-- Re-Consent Gate Blocking Modal Dialog -->
    <div
      v-if="showReconsentGateModal"
      id="reconsent-gate-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 520px"
      >
        <div
          class="modal-header"
          style="
            color: #991b1b;
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <span>⚠️</span> Re-Consent Gate Active — Data Entry Blocked
        </div>
        <div class="modal-body">
          <p style="font-size: 0.95rem; color: #7f1d1d; font-weight: 600">
            Subject <strong>{{ selectedSubjectId }}</strong> is currently gated
            under Protocol Amendment v2.0.0.
          </p>
          <p style="font-size: 0.875rem; color: var(--text-muted)">
            Per 21 CFR Part 11 and PRD-SUB-007, visit data entry and clinical
            observations cannot be submitted until re-consent has been completed
            and verified by site clinical staff.
          </p>
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
            @click="showReconsentGateModal = false"
          >
            Close
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #dc2626; color: white"
            @click="
              showReconsentGateModal = false;
              $emit('open-econsent-modal');
            "
          >
            ✍️ Open eConsent Form
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            style="border: 1px solid #f87171; color: #991b1b"
            @click="
              showReconsentGateModal = false;
              $emit('open-paper-icf-modal');
            "
          >
            📄 Upload Signed Paper ICF
          </button>
        </div>
      </div>
    </div>

    <!-- eConsent Signing Modal Dialog -->
    <div
      v-if="showEconsentModal"
      id="econsent-modal"
      class="modal-overlay"
      style="display: flex"
    >
      <div
        class="modal"
        style="max-width: 520px"
      >
        <div class="modal-header">
          Execute Electronic Re-Consent (ICF v2.0.0)
        </div>
        <div class="modal-body">
          <p>
            Recording 21 CFR Part 11 compliant digital informed consent for
            Subject <strong>{{ selectedSubjectId }}</strong> under Protocol
            Version <strong>2.0.0</strong>.
          </p>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label style="font-size: 0.85rem; font-weight: 600">Signer Printed Name:</label>
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
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label style="font-size: 0.85rem; font-weight: 600">Consent Declaration:</label>
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
      <div
        class="modal"
        style="max-width: 520px"
      >
        <div class="modal-header">
          Register Signed Paper ICF (v2.0.0)
        </div>
        <div class="modal-body">
          <p>
            Upload or register site-verified paper Informed Consent Form for
            Subject <strong>{{ selectedSubjectId }}</strong>.
          </p>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label style="font-size: 0.85rem; font-weight: 600">Date ICF Signed by Subject:</label>
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
            >
          </div>
          <div
            class="form-group"
            style="margin-bottom: 12px"
          >
            <label style="font-size: 0.85rem; font-weight: 600">Investigator Verification Note:</label>
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
            >
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
import { computed, reactive, ref } from "vue";
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
  "enroll-subject",
]);

// Subject Enrollment Modal State
const showEnrollModal = ref(false);
const showReconsentGateModal = ref(false);
const enrollForm = reactive({
  subjectId: "",
  siteId: "SITE-101",
  consentDate: new Date().toISOString().split("T")[0],
  armId: "ARM-A",
  changeReason: "Initial subject enrollment",
});

// Dynamic Active Subject & Protocol Schema Projection
const currentStore = computed(() => props.store || clinicalStore);

const activeSubject = computed(() => {
  const list = currentStore.value?.subjects || [];
  return list.find((s) => s.id === props.selectedSubjectId);
});

const activeSubjectVersion = computed(() => {
  return activeSubject.value?.active_protocol_version || "1.0.0";
});

const renderedFields = computed(() => {
  if (typeof currentStore.value?.getEcrfFieldsForVersion === "function") {
    return currentStore.value.getEcrfFieldsForVersion(
      activeSubjectVersion.value
    );
  }
  return currentStore.value?.ecrfFields || [];
});

// Real-Time Edit Check Rule Evaluation
const activeEditCheckAlerts = computed(() => {
  const alerts = [];
  const vals = currentStore.value?.formValues || {};

  const vssbp = parseFloat(vals.vssbp);
  const vsdpb = parseFloat(vals.vsdpb);
  const pulse = parseFloat(vals.pulse);
  const wbc = parseFloat(vals.lb_wbc || vals.wbc);
  const gluc = parseFloat(vals.lb_gluc || vals.gluc);

  // 1. Cross-field Blood Pressure Discrepancy (Diastolic >= Systolic)
  if (!isNaN(vssbp) && !isNaN(vsdpb) && vsdpb >= vssbp) {
    alerts.push({
      id: "EC_VSDPB_VSSBP",
      fieldId: "vsdpb",
      severity: "DISCREPANCY",
      message: `Diastolic BP (${vsdpb} mmHg) cannot equal or exceed Systolic BP (${vssbp} mmHg).`,
    });
  }

  // 2. Severe Hypertension Alert (Systolic > 180 mmHg)
  if (!isNaN(vssbp) && vssbp > 180) {
    alerts.push({
      id: "EC_VSSBP_HIGH",
      fieldId: "vssbp",
      severity: "WARNING",
      message: `Systolic BP (${vssbp} mmHg) exceeds 180 mmHg threshold (Stage 3 crisis alert).`,
    });
  }

  // 3. Tachycardia Alert (Pulse > 100 bpm)
  if (!isNaN(pulse) && pulse > 100) {
    alerts.push({
      id: "EC_VSHR_HIGH",
      fieldId: "pulse",
      severity: "WARNING",
      message: `Tachycardia warning: Pulse Rate (${pulse} bpm) exceeds 100 bpm.`,
    });
  }

  // 4. Bradycardia Alert (Pulse < 40 bpm)
  if (!isNaN(pulse) && pulse > 0 && pulse < 40) {
    alerts.push({
      id: "EC_VSHR_LOW",
      fieldId: "pulse",
      severity: "WARNING",
      message: `Bradycardia warning: Pulse Rate (${pulse} bpm) is below 40 bpm.`,
    });
  }

  // 5. White Blood Cell Reference Range Alert
  if (!isNaN(wbc) && (wbc < 4.0 || wbc > 11.0)) {
    alerts.push({
      id: "EC_LB_WBC",
      fieldId: "lb_wbc",
      severity: "WARNING",
      message: `Lab Alert: WBC (${wbc} 10^9/L) is outside normal clinical reference range (4.0 - 11.0 10^9/L).`,
    });
  }

  // 6. Fasting Glucose Reference Range Alert
  if (!isNaN(gluc) && gluc > 126.0) {
    alerts.push({
      id: "EC_LB_GLUC",
      fieldId: "lb_gluc",
      severity: "WARNING",
      message: `Lab Alert: Fasting Glucose (${gluc} mg/dL) exceeds normal threshold (>126 mg/dL indicative of hyperglycemia).`,
    });
  }

  return alerts;
});

const discrepancyAlertsCount = computed(() => {
  return activeEditCheckAlerts.value.filter((a) => a.severity === "DISCREPANCY")
    .length;
});

const warningAlertsCount = computed(() => {
  return activeEditCheckAlerts.value.filter((a) => a.severity === "WARNING")
    .length;
});

function getFieldAlerts(fieldId) {
  return activeEditCheckAlerts.value.filter((a) => a.fieldId === fieldId);
}

function raiseQueryForAlert(fieldId, message) {
  emit("create-query", fieldId, message);
}

function handleFieldsetInteraction() {
  if (props.isReconsentGated) {
    showReconsentGateModal.value = true;
  }
}

function handleSubmitClick() {
  if (props.isReconsentGated) {
    showReconsentGateModal.value = true;
    return;
  }
  emit("submit-ecrf");
}

async function handleEnrollSubmit() {
  if (!enrollForm.subjectId.trim()) return;

  const storeInstance = props.store || clinicalStore;
  if (typeof storeInstance.enrollSubject === "function") {
    await storeInstance.enrollSubject({
      id: enrollForm.subjectId.trim(),
      siteId: enrollForm.siteId,
      consentDate: enrollForm.consentDate,
      armId: enrollForm.armId,
      protocolVersion: "1.0.0",
      reason: enrollForm.changeReason,
    });
  }

  emit("enroll-subject", { ...enrollForm });
  emit("update:selectedSubjectId", enrollForm.subjectId.trim());
  emit("load-ecrf-session");

  showEnrollModal.value = false;
  enrollForm.subjectId = "";
}

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
