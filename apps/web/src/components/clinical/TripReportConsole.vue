<template>
  <div class="trip-report-console card" style="margin-top: 16px; padding: 20px">
    <!-- Console Header Bar -->
    <div
      class="report-header"
      style="
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: 16px;
        padding-bottom: 16px;
        margin-bottom: 20px;
        border-bottom: 2px solid var(--semantic-color-border, #e2e8f0);
      "
    >
      <div>
        <div style="display: flex; align-items: center; gap: 12px">
          <h3
            style="
              margin: 0;
              font-size: 1.25rem;
              font-weight: 700;
              color: #0f172a;
            "
          >
            Structured Visit Questionnaire &amp; Trip Report Engine
          </h3>
          <span
            class="badge"
            :style="
              activeReport?.status === 'FINALIZED'
                ? { backgroundColor: '#166534', color: '#ffffff' }
                : { backgroundColor: '#3b82f6', color: '#ffffff' }
            "
          >
            {{ activeReport?.status || "DRAFT" }}
          </span>
          <span
            v-if="activeReport?.isSigned"
            class="badge"
            style="background-color: #15803d; color: #ffffff"
            title="21 CFR Part 11 Compliant Electronic Signature Recorded"
          >
            ✍️ 21 CFR Part 11 Signed
          </span>
        </div>
        <p style="margin: 4px 0 0 0; font-size: 0.85rem; color: #64748b">
          GxP Monitoring Questionnaire for PSV, SIV, IMV, and COV visits with
          automated PDF generation and eTMF filing.
        </p>
      </div>

      <!-- Visit Controls & Actions -->
      <div
        style="display: flex; align-items: center; gap: 12px; flex-wrap: wrap"
      >
        <!-- Visit Type / ID Selector -->
        <div style="display: flex; align-items: center; gap: 6px">
          <label
            for="trip-visit-select"
            style="font-size: 0.8rem; font-weight: 600; color: #475569"
          >
            Visit:
          </label>
          <select
            id="trip-visit-select"
            v-model="selectedVisitId"
            style="
              padding: 6px 12px;
              border: 1px solid #cbd5e1;
              border-radius: 6px;
              font-weight: 600;
              font-size: 0.85rem;
            "
            @change="handleVisitChange"
          >
            <option value="VISIT-PSV-101">Pre-Study Visit (PSV)</option>
            <option value="VISIT-SIV-101">Site Initiation Visit (SIV)</option>
            <option value="VISIT-IMV-101">
              Interim Monitoring Visit (IMV)
            </option>
            <option value="VISIT-COV-101">Close-Out Visit (COV)</option>
          </select>
        </div>

        <!-- Action Buttons -->
        <button
          id="btn-save-draft"
          type="button"
          class="btn"
          style="
            background-color: #f1f5f9;
            color: #1e293b;
            border: 1px solid #cbd5e1;
            font-weight: 600;
            padding: 6px 14px;
            font-size: 0.85rem;
            border-radius: 6px;
            cursor: pointer;
          "
          :disabled="activeReport?.status === 'FINALIZED' || loading"
          @click="handleSaveDraft"
        >
          💾 Save Draft
        </button>

        <button
          id="btn-finalize-sign"
          type="button"
          class="btn btn-primary"
          style="
            background-color: #0f4c81;
            color: #ffffff;
            font-weight: 600;
            padding: 6px 16px;
            font-size: 0.85rem;
            border-radius: 6px;
            cursor: pointer;
          "
          :disabled="activeReport?.status === 'FINALIZED' || loading"
          @click="openSignatureModal"
        >
          ✍️ Finalize &amp; Sign Report
        </button>
      </div>
    </div>

    <!-- Active Report Summary / eTMF Status Info -->
    <div
      v-if="activeReport"
      style="
        background-color: #f8fafc;
        border: 1px solid #e2e8f0;
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: 20px;
        display: flex;
        gap: 20px;
        flex-wrap: wrap;
        font-size: 0.85rem;
        color: #334155;
      "
    >
      <div><strong>Study ID:</strong> {{ activeReport.studyId }}</div>
      <div><strong>Site ID:</strong> {{ activeReport.siteId }}</div>
      <div><strong>CRA:</strong> {{ activeReport.craId }}</div>
      <div>
        <strong>Template:</strong> <code>{{ activeReport.templateName }}</code>
      </div>
      <div
        v-if="activeReport.etmfDocumentId"
        style="margin-left: auto; color: #166534; font-weight: 600"
      >
        📁 eTMF Filed Document: <code>{{ activeReport.etmfDocumentId }}</code>
      </div>
    </div>

    <!-- Sectional Navigation Tabs -->
    <div
      class="section-tabs"
      style="
        display: flex;
        gap: 8px;
        border-bottom: 2px solid #e2e8f0;
        margin-bottom: 20px;
        overflow-x: auto;
      "
    >
      <button
        v-for="section in sections"
        :key="section.sectionKey"
        :id="`tab-sec-${section.sectionKey}`"
        type="button"
        class="tab-btn"
        style="
          padding: 10px 16px;
          border: none;
          background: none;
          font-weight: 600;
          font-size: 0.9rem;
          cursor: pointer;
          border-bottom: 3px solid transparent;
          display: flex;
          align-items: center;
          gap: 8px;
          white-space: nowrap;
        "
        :style="
          activeSectionKey === section.sectionKey
            ? {
                borderBottomColor: '#0f4c81',
                color: '#0f4c81',
                backgroundColor: '#f1f5f9',
              }
            : { color: '#64748b' }
        "
        @click="switchSection(section.sectionKey)"
      >
        <span>{{ section.title }}</span>
        <!-- Section Verification Indicator -->
        <span
          v-if="section.isVerified"
          class="badge"
          style="
            background-color: #dcfce7;
            color: #166534;
            font-size: 0.7rem;
            padding: 2px 6px;
          "
        >
          ✓ Verified
        </span>
        <span
          v-else
          class="badge"
          style="
            background-color: #f1f5f9;
            color: #64748b;
            font-size: 0.7rem;
            padding: 2px 6px;
          "
        >
          Pending
        </span>
        <!-- Findings Count Badge -->
        <span
          v-if="section.findings && section.findings.length > 0"
          class="badge"
          style="
            background-color: #fef3c7;
            color: #b45309;
            font-size: 0.7rem;
            padding: 2px 6px;
          "
        >
          ⚠️ {{ section.findings.length }} Finding(s)
        </span>
      </button>
    </div>

    <!-- Section Content & Questionnaire Form -->
    <div v-if="activeSection" class="section-workspace">
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 16px;
        "
      >
        <h4 style="margin: 0; font-size: 1.1rem; color: #1e293b">
          {{ activeSection.title }} Assessment
        </h4>
        <!-- Section Level Verification Toggle -->
        <label
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            font-weight: 600;
            font-size: 0.85rem;
            cursor: pointer;
            color: #0f172a;
          "
        >
          <input
            :id="`chk-verify-${activeSection.sectionKey}`"
            type="checkbox"
            :checked="activeSection.isVerified"
            :disabled="activeReport?.status === 'FINALIZED'"
            style="
              width: 18px;
              height: 18px;
              accent-color: #166534;
              cursor: pointer;
            "
            @change="toggleSectionVerification($event.target.checked)"
          />
          Mark Section Verified by CRA
        </label>
      </div>

      <!-- Questionnaire Items Table / Form List -->
      <div
        class="responses-list"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <div
          v-for="resp in activeSection.responses"
          :key="resp.questionKey"
          class="response-card"
          style="
            background-color: #ffffff;
            border: 1px solid #e2e8f0;
            border-radius: 8px;
            padding: 16px;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
          "
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: flex-start;
              gap: 16px;
              flex-wrap: wrap;
            "
          >
            <div style="flex: 1; min-width: 250px">
              <div
                style="
                  font-weight: 600;
                  font-size: 0.95rem;
                  color: #0f172a;
                  margin-bottom: 4px;
                "
              >
                {{ resp.questionText }}
              </div>
              <div
                style="
                  font-size: 0.75rem;
                  color: #64748b;
                  font-family: monospace;
                "
              >
                Key: {{ resp.questionKey }} | Type: {{ resp.responseType }}
              </div>
            </div>

            <!-- Value Controls based on responseType -->
            <div style="min-width: 200px">
              <template v-if="resp.responseType === 'boolean'">
                <div style="display: flex; gap: 8px">
                  <button
                    type="button"
                    class="btn"
                    :style="
                      resp.value === 'PASS'
                        ? {
                            backgroundColor: '#22c55e',
                            color: '#ffffff',
                            fontWeight: 'bold',
                          }
                        : { backgroundColor: '#f1f5f9', color: '#475569' }
                    "
                    style="
                      padding: 6px 16px;
                      border-radius: 6px;
                      border: 1px solid #cbd5e1;
                      cursor: pointer;
                    "
                    :disabled="activeReport?.status === 'FINALIZED'"
                    @click="updateResponseValue(resp, 'PASS')"
                  >
                    PASS
                  </button>
                  <button
                    type="button"
                    class="btn"
                    :style="
                      resp.value === 'FAIL'
                        ? {
                            backgroundColor: '#ef4444',
                            color: '#ffffff',
                            fontWeight: 'bold',
                          }
                        : { backgroundColor: '#f1f5f9', color: '#475569' }
                    "
                    style="
                      padding: 6px 16px;
                      border-radius: 6px;
                      border: 1px solid #cbd5e1;
                      cursor: pointer;
                    "
                    :disabled="activeReport?.status === 'FINALIZED'"
                    @click="updateResponseValue(resp, 'FAIL')"
                  >
                    FAIL
                  </button>
                  <button
                    type="button"
                    class="btn"
                    :style="
                      resp.value === 'N/A'
                        ? {
                            backgroundColor: '#64748b',
                            color: '#ffffff',
                            fontWeight: 'bold',
                          }
                        : { backgroundColor: '#f1f5f9', color: '#475569' }
                    "
                    style="
                      padding: 6px 12px;
                      border-radius: 6px;
                      border: 1px solid #cbd5e1;
                      cursor: pointer;
                    "
                    :disabled="activeReport?.status === 'FINALIZED'"
                    @click="updateResponseValue(resp, 'N/A')"
                  >
                    N/A
                  </button>
                </div>
              </template>

              <template v-else-if="resp.responseType === 'number'">
                <input
                  :id="'input-num-' + resp.questionKey"
                  type="number"
                  :value="resp.value"
                  aria-label="Numerical response"
                  style="
                    width: 100%;
                    padding: 6px 10px;
                    border: 1px solid #cbd5e1;
                    border-radius: 6px;
                  "
                  :disabled="activeReport?.status === 'FINALIZED'"
                  @input="updateResponseValue(resp, $event.target.value)"
                />
              </template>

              <template v-else>
                <input
                  :id="'input-txt-' + resp.questionKey"
                  type="text"
                  :value="resp.value"
                  aria-label="Text response"
                  style="
                    width: 100%;
                    padding: 6px 10px;
                    border: 1px solid #cbd5e1;
                    border-radius: 6px;
                  "
                  :disabled="activeReport?.status === 'FINALIZED'"
                  @input="updateResponseValue(resp, $event.target.value)"
                />
              </template>
            </div>
          </div>

          <!-- Commentary Textarea & Attach Finding Button -->
          <div
            style="
              margin-top: 12px;
              display: flex;
              gap: 12px;
              align-items: flex-start;
              flex-wrap: wrap;
            "
          >
            <div style="flex: 1; min-width: 250px">
              <textarea
                :id="'commentary-' + resp.questionKey"
                :value="resp.commentary || ''"
                placeholder="CRA notes, observations, or rationale..."
                aria-label="Commentary and rationale"
                rows="2"
                style="
                  width: 100%;
                  padding: 8px;
                  border: 1px solid #cbd5e1;
                  border-radius: 6px;
                  font-size: 0.85rem;
                  resize: vertical;
                "
                :disabled="activeReport?.status === 'FINALIZED'"
                @input="updateResponseCommentary(resp, $event.target.value)"
              ></textarea>
            </div>

            <div style="display: flex; flex-direction: column; gap: 8px">
              <button
                type="button"
                class="btn"
                style="
                  background-color: #fff7ed;
                  color: #c2410c;
                  border: 1px solid #fdba74;
                  padding: 6px 12px;
                  font-size: 0.8rem;
                  font-weight: 600;
                  border-radius: 6px;
                  cursor: pointer;
                  white-space: nowrap;
                "
                :disabled="activeReport?.status === 'FINALIZED'"
                @click="openAttachFindingModal(resp)"
              >
                🚨 Attach Finding
              </button>
            </div>
          </div>

          <!-- Attached Item Findings -->
          <div
            v-if="getItemFindings(resp.questionKey).length > 0"
            style="
              margin-top: 10px;
              display: flex;
              flex-direction: column;
              gap: 6px;
            "
          >
            <div
              v-for="finding in getItemFindings(resp.questionKey)"
              :key="finding.id"
              style="
                background-color: #fff1f2;
                border: 1px solid #fecdd3;
                border-radius: 6px;
                padding: 8px 12px;
                font-size: 0.8rem;
                display: flex;
                align-items: center;
                gap: 10px;
                justify-content: space-between;
              "
            >
              <div>
                <span
                  class="badge"
                  :style="
                    finding.severity === 'CRITICAL'
                      ? { backgroundColor: '#be123c', color: '#ffffff' }
                      : finding.severity === 'MAJOR'
                        ? { backgroundColor: '#c2410c', color: '#ffffff' }
                        : { backgroundColor: '#b45309', color: '#ffffff' }
                  "
                >
                  {{ finding.severity }}
                </span>
                <span
                  style="margin-left: 8px; color: #1e293b; font-weight: 500"
                >
                  {{ finding.text }}
                </span>
              </div>
              <span
                class="badge"
                style="background-color: #e2e8f0; color: #475569"
              >
                Status: {{ finding.resolutionStatus }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Attach Finding Modal -->
    <div v-if="showFindingModal" class="modal-overlay">
      <div class="modal card" style="max-width: 500px">
        <div class="modal-header" style="background-color: #0f4c81">
          Attach Site Finding to Question
        </div>
        <div class="modal-body">
          <div
            style="
              font-weight: 600;
              font-size: 0.9rem;
              margin-bottom: 12px;
              color: #334155;
            "
          >
            Question: {{ targetFindingResp?.questionText }}
          </div>

          <div class="form-group" style="margin-bottom: 12px">
            <label
              for="input-finding-text"
              style="
                font-weight: 600;
                font-size: 0.8rem;
                display: block;
                margin-bottom: 4px;
              "
            >
              Finding Details / Observation *
            </label>
            <textarea
              id="input-finding-text"
              v-model="newFindingText"
              rows="3"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid #cbd5e1;
                border-radius: 6px;
              "
              placeholder="Describe non-compliance, temperature excursion, missing log..."
            ></textarea>
          </div>

          <div style="display: flex; gap: 12px; margin-bottom: 12px">
            <div style="flex: 1">
              <label
                for="select-finding-severity"
                style="
                  font-weight: 600;
                  font-size: 0.8rem;
                  display: block;
                  margin-bottom: 4px;
                "
              >
                Severity *
              </label>
              <select
                id="select-finding-severity"
                v-model="newFindingSeverity"
                style="
                  width: 100%;
                  padding: 6px 10px;
                  border: 1px solid #cbd5e1;
                  border-radius: 6px;
                "
              >
                <option value="MINOR">MINOR</option>
                <option value="MAJOR">MAJOR</option>
                <option value="CRITICAL">CRITICAL</option>
              </select>
            </div>

            <div style="flex: 1">
              <label
                for="select-finding-status"
                style="
                  font-weight: 600;
                  font-size: 0.8rem;
                  display: block;
                  margin-bottom: 4px;
                "
              >
                Resolution Status *
              </label>
              <select
                id="select-finding-status"
                v-model="newFindingStatus"
                style="
                  width: 100%;
                  padding: 6px 10px;
                  border: 1px solid #cbd5e1;
                  border-radius: 6px;
                "
              >
                <option value="OPEN">OPEN</option>
                <option value="RESOLVED">RESOLVED</option>
                <option value="ESC-TO-CAPA">ESC-TO-CAPA</option>
              </select>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn"
            style="border: 1px solid #cbd5e1"
            @click="showFindingModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #0f4c81; color: white"
            @click="confirmAttachFinding"
          >
            Attach Finding
          </button>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Electronic Signature Modal -->
    <div v-if="showSigModal" class="modal-overlay">
      <div class="modal card" style="max-width: 520px">
        <div class="modal-header" style="background-color: #0f4c81">
          ✍️ 21 CFR Part 11 Electronic Signature &amp; Finalization
        </div>
        <div class="modal-body">
          <div
            style="
              background-color: #eff6ff;
              border: 1px solid #bfdbfe;
              border-radius: 6px;
              padding: 10px 12px;
              font-size: 0.8rem;
              color: #1e40af;
              margin-bottom: 16px;
            "
          >
            Submitting this visit report compiles the pre-configured Liquid
            template, generates a cryptographic SHA-256 hash, and files the
            final GxP PDF directly into eTMF.
          </div>

          <div class="form-group" style="margin-bottom: 12px">
            <label
              for="input-signer-name"
              style="
                font-weight: 600;
                font-size: 0.8rem;
                display: block;
                margin-bottom: 4px;
              "
            >
              Full Legal Name of Signer *
            </label>
            <input
              id="input-signer-name"
              v-model="signerName"
              type="text"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid #cbd5e1;
                border-radius: 6px;
              "
            />
          </div>

          <div class="form-group" style="margin-bottom: 12px">
            <label
              for="input-signing-purpose"
              style="
                font-weight: 600;
                font-size: 0.8rem;
                display: block;
                margin-bottom: 4px;
              "
            >
              Signing Purpose / Manifestation *
            </label>
            <input
              id="input-signing-purpose"
              v-model="signingPurpose"
              type="text"
              readonly
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid #e2e8f0;
                background-color: #f8fafc;
                border-radius: 6px;
              "
            />
          </div>

          <div class="form-group" style="margin-bottom: 12px">
            <label
              for="input-sig-reason"
              style="
                font-weight: 600;
                font-size: 0.8rem;
                display: block;
                margin-bottom: 4px;
              "
            >
              Reason for Change / Finalization Rationale *
            </label>
            <input
              id="input-sig-reason"
              v-model="sigReason"
              type="text"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid #cbd5e1;
                border-radius: 6px;
              "
            />
          </div>

          <label
            style="
              display: flex;
              align-items: flex-start;
              gap: 8px;
              font-size: 0.8rem;
              color: #0f172a;
              cursor: pointer;
              margin-top: 12px;
            "
          >
            <input
              v-model="sigAgreed"
              type="checkbox"
              style="margin-top: 2px"
            />
            <span>
              I certify under penalty of perjury that this visit questionnaire
              and trip report accurately reflects the site monitoring
              observations and ICH GCP compliance checks.
            </span>
          </label>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn"
            style="border: 1px solid #cbd5e1"
            @click="showSigModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #166534; color: white"
            :disabled="!sigAgreed || !signerName.trim()"
            @click="confirmSubmitAndSign"
          >
            Submit &amp; Digitally Sign
          </button>
        </div>
      </div>
    </div>

    <!-- Unsaved Changes Confirmation Dialog Modal -->
    <div v-if="showDirtyWarningModal" class="modal-overlay">
      <div class="modal card" style="max-width: 480px">
        <div class="modal-header" style="background-color: #b45309">
          ⚠️ Unsaved Changes Warning
        </div>
        <div class="modal-body" style="font-size: 0.9rem; color: #334155">
          You have unsaved changes in the trip report questionnaire. Would you
          like to save your draft before switching sections/visits?
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn"
            style="border: 1px solid #cbd5e1"
            @click="cancelDirtyNavigation"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn"
            style="background-color: #ef4444; color: white"
            @click="discardAndProceed"
          >
            Discard Changes
          </button>
          <button
            type="button"
            class="btn btn-primary"
            style="background-color: #0f4c81; color: white"
            @click="saveDraftAndProceed"
          >
            Save Draft &amp; Proceed
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import {
  useClinicalStore,
  type TripReportResponseDto,
} from "../../stores/clinical.js";
import { useAuthStore } from "../../stores/auth.js";
import { useToastStore } from "../../stores/toasts.js";

const props = defineProps({
  visitIdProp: {
    type: String,
    default: "VISIT-IMV-101",
  },
});

const clinicalStore = useClinicalStore();
const authStore = useAuthStore();
const toastStore = useToastStore();

const selectedVisitId = ref(props.visitIdProp);
const activeSectionKey = ref("protocol_compliance");
const loading = computed(() => clinicalStore.tripReportLoading);

const activeReport = computed(() => clinicalStore.activeTripReport);
const sections = computed(() => activeReport.value?.sections || []);
const activeSection = computed(() =>
  sections.value.find((s) => s.sectionKey === activeSectionKey.value)
);

// Unsaved changes navigation modal state
const showDirtyWarningModal = ref(false);
const pendingNavigationAction = ref<(() => void) | null>(null);

// Finding Modal State
const showFindingModal = ref(false);
const targetFindingResp = ref<TripReportResponseDto | null>(null);
const newFindingText = ref("");
const newFindingSeverity = ref("MAJOR");
const newFindingStatus = ref("OPEN");

// Signature Modal State
const showSigModal = ref(false);
const signerName = ref("");
const signingPurpose = ref(
  "21 CFR Part 11 Electronic Signature / Visit Report Sign-off"
);
const sigReason = ref("Final trip report submission");
const sigAgreed = ref(false);

onMounted(async () => {
  await clinicalStore.fetchTripReport(selectedVisitId.value);
  signerName.value = authStore.identity?.username || "Frank de Ruiter";
});

async function handleVisitChange() {
  if (clinicalStore.isTripReportDirty) {
    pendingNavigationAction.value = async () => {
      await clinicalStore.fetchTripReport(selectedVisitId.value);
    };
    showDirtyWarningModal.value = true;
    return;
  }
  await clinicalStore.fetchTripReport(selectedVisitId.value);
}

function switchSection(sectionKey: string) {
  if (clinicalStore.isTripReportDirty) {
    pendingNavigationAction.value = () => {
      activeSectionKey.value = sectionKey;
    };
    showDirtyWarningModal.value = true;
    return;
  }
  activeSectionKey.value = sectionKey;
}

function updateResponseValue(resp: TripReportResponseDto, newVal: string) {
  resp.value = newVal;
  clinicalStore.markTripReportDirty(true);
}

function updateResponseCommentary(
  resp: TripReportResponseDto,
  newComment: string
) {
  resp.commentary = newComment;
  clinicalStore.markTripReportDirty(true);
}

function toggleSectionVerification(isVerified: boolean) {
  if (activeSection.value) {
    activeSection.value.isVerified = isVerified;
    clinicalStore.markTripReportDirty(true);
  }
}

function getItemFindings(questionKey: string) {
  if (!activeSection.value || !activeSection.value.findings) return [];
  return activeSection.value.findings.filter(
    (f) => f.questionKey === questionKey
  );
}

function openAttachFindingModal(resp: TripReportResponseDto) {
  targetFindingResp.value = resp;
  newFindingText.value = "";
  newFindingSeverity.value = "MAJOR";
  newFindingStatus.value = "OPEN";
  showFindingModal.value = true;
}

async function confirmAttachFinding() {
  if (!newFindingText.value.trim() || !targetFindingResp.value) return;

  await clinicalStore.attachTripReportFinding({
    visitId: selectedVisitId.value,
    sectionKey: activeSectionKey.value,
    questionKey: targetFindingResp.value.questionKey,
    text: newFindingText.value,
    severity: newFindingSeverity.value,
    resolutionStatus: newFindingStatus.value,
  });

  toastStore.addToast?.({
    title: "Finding Attached",
    message: `Finding linked to ${targetFindingResp.value.questionKey}`,
    type: "success",
  });

  showFindingModal.value = false;
}

async function handleSaveDraft() {
  if (!activeReport.value) return;

  const saveSections = sections.value.map((sec) => ({
    sectionKey: sec.sectionKey,
    isVerified: sec.isVerified,
    responses: sec.responses.map((r) => ({
      questionKey: r.questionKey,
      questionText: r.questionText,
      responseType: r.responseType,
      value: r.value,
      commentary: r.commentary,
      findingId: r.findingId,
    })),
  }));

  await clinicalStore.saveTripReportDraft({
    tripReportId: selectedVisitId.value,
    sections: saveSections,
    reasonForChange: "CRA draft save",
  });

  toastStore.addToast?.({
    title: "Draft Saved",
    message: "Visit questionnaire draft successfully saved.",
    type: "success",
  });
}

function openSignatureModal() {
  sigAgreed.value = false;
  showSigModal.value = true;
}

async function confirmSubmitAndSign() {
  if (!sigAgreed.value || !signerName.value.trim()) return;

  // Save current draft first if dirty
  if (clinicalStore.isTripReportDirty) {
    await handleSaveDraft();
  }

  const result = await clinicalStore.submitTripReport({
    tripReportId: selectedVisitId.value,
    finalize: true,
    signerName: signerName.value,
    signingPurpose: signingPurpose.value,
    reasonForChange: sigReason.value,
  });

  showSigModal.value = false;
  toastStore.addToast?.({
    title: "Report Finalized & Signed",
    message: `Trip report finalized and filed to eTMF (${result.etmfDocumentId}).`,
    type: "success",
  });
}

async function saveDraftAndProceed() {
  showDirtyWarningModal.value = false;
  await handleSaveDraft();
  if (pendingNavigationAction.value) {
    pendingNavigationAction.value();
    pendingNavigationAction.value = null;
  }
}

function discardAndProceed() {
  showDirtyWarningModal.value = false;
  clinicalStore.markTripReportDirty(false);
  if (pendingNavigationAction.value) {
    pendingNavigationAction.value();
    pendingNavigationAction.value = null;
  }
}

function cancelDirtyNavigation() {
  showDirtyWarningModal.value = false;
  pendingNavigationAction.value = null;
}
</script>

<style scoped>
.modal-overlay {
  display: flex;
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: white;
  border-radius: 8px;
  width: 100%;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  overflow: hidden;
}

.modal-header {
  padding: 14px 16px;
  font-weight: 600;
  font-size: 15px;
  color: white;
}

.modal-body {
  padding: 16px;
}

.modal-footer {
  padding: 12px 16px;
  background: #f8fafc;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid #e2e8f0;
}
</style>
