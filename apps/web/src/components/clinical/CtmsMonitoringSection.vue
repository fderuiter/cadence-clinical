<template>
  <section
    id="ctms-monitoring-section"
    class="card ctms-section"
    aria-labelledby="ctms-monitoring-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-monitoring-title" class="card-title">Monitoring visits</h3>
        <p class="section-subtitle">
          Schedule monitoring visits, record structured trip findings, compile
          Part 11 signed visit letters, and complete supervisory sign-off.
        </p>
      </div>
    </div>

    <CtmsSectionScopeBar
      v-model:study="studyInput"
      v-model:site="siteInput"
      prefix="monitoring"
      form-id="monitoring-scope-form"
      aria-label="Monitoring visits scope"
      :show-study-input="!props.studyCode"
      site-placeholder="Site code (optional)"
      :assigned-studies="assignedStudies"
      :assigned-sites="assignedSites"
      submit-id="btn-load-monitoring-visits"
      :submit-label="loading ? 'Loading...' : 'Load visits'"
      :submit-disabled="!studyInput.trim() || loading"
      :scope-guidance="scopeGuidance"
      :load-error="loadError"
      :idle-message="
        !loaded && !loading
          ? 'Select a study to view monitoring visits within your assigned scope.'
          : ''
      "
      @submit="loadVisits"
    />

    <template v-if="loaded">
      <p
        v-if="!canSignOff"
        id="ctms-visit-signoff-role-note"
        class="muted-status"
        role="note"
      >
        {{ MONITORING_VISIT_SIGNOFF_ROLE_REASON }}
      </p>
      <p
        v-if="!canSchedule"
        id="ctms-visit-schedule-role-note"
        class="muted-status"
        role="note"
      >
        {{ MONITORING_LETTER_COMPILE_ROLE_REASON }}
      </p>

      <p v-if="!visits.length" class="muted-status">
        No monitoring visits scheduled yet for this scope.
      </p>
      <div v-else class="table-wrap">
        <table id="monitoring-visits-table" class="data-table">
          <thead>
            <tr>
              <th scope="col">Site</th>
              <th scope="col">Type</th>
              <th scope="col">Monitor</th>
              <th scope="col">Scheduled</th>
              <th scope="col">Status</th>
              <th scope="col">Findings</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="visit in visits" :key="visit.id">
              <td>
                <strong>{{ visit.site_id }}</strong>
              </td>
              <td>{{ formatClinicalEnum(visit.visit_type) }}</td>
              <td>{{ visit.cra_id }}</td>
              <td>{{ formatIsoDateShort(visit.scheduled_date) }}</td>
              <td>
                <span
                  class="status-pill"
                  :class="`visit-${visit.status.toLowerCase()}`"
                >
                  {{ formatClinicalStatus(visit.status) }}
                </span>
              </td>
              <td>{{ visit.findings?.length ?? 0 }}</td>
              <td class="action-cell">
                <button
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="viewLetters(visit)"
                >
                  Letters<span class="sr-only">
                    for {{ visit.visit_type }} visit at
                    {{ visit.site_id }}</span
                  >
                </button>
                <button
                  v-if="canSchedule"
                  :data-testid="`btn-compile-letter-${visit.id}`"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="prepareCompileLetter(visit)"
                >
                  Compile letter<span class="sr-only">
                    for {{ visit.visit_type }} visit at
                    {{ visit.site_id }}</span
                  >
                </button>
                <button
                  v-if="canComplete && visit.status === 'SCHEDULED'"
                  :data-testid="`btn-complete-visit-${visit.id}`"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="prepareComplete(visit)"
                >
                  Complete<span class="sr-only">
                    {{ visit.visit_type }} visit at {{ visit.site_id }}</span
                  >
                </button>
                <button
                  v-if="canSignOff && visit.status === 'COMPLETED'"
                  :data-testid="`btn-signoff-visit-${visit.id}`"
                  type="button"
                  class="btn btn-primary btn-sm"
                  @click="prepareSignOff(visit)"
                >
                  Sign off<span class="sr-only">
                    {{ visit.visit_type }} visit at {{ visit.site_id }}</span
                  >
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        v-if="compiledLetters.length"
        id="ctms-compiled-letters-container"
        class="mutation-card"
      >
        <h4>Compiled GxP visit letters</h4>
        <ul class="letter-list">
          <li
            v-for="item in compiledLetters"
            :key="item.letterId"
            class="letter-item"
          >
            <div class="card-subheader">
              <div>
                <strong>{{ formatClinicalEnum(item.letterType) }}</strong> ·
                <span>{{ formatClinicalStatus(item.status) }}</span>
                <span v-if="item.etmfDocumentId"> · Filed in eTMF</span>
                <SupportDetails
                  summary-label="Filing &amp; seal details"
                  :detail="formatCompiledLetterDetail(item)"
                  :reference="item.pdfSha256Hash || item.letterId"
                />
              </div>
              <button
                :data-testid="`btn-download-letter-${item.letterId}`"
                type="button"
                class="btn btn-secondary btn-sm"
                @click="downloadCompiledLetter(item)"
              >
                Download PDF
              </button>
            </div>
          </li>
        </ul>
      </div>

      <div
        v-if="selectedLettersVisit"
        id="monitoring-letters-panel"
        class="mutation-card"
      >
        <div class="card-subheader">
          <h4>
            Generated letters for {{ selectedLettersVisit.site_id }} ({{
              formatClinicalEnum(selectedLettersVisit.visit_type)
            }})
          </h4>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="selectedLettersVisit = null"
          >
            Close
          </button>
        </div>
        <p v-if="!letters.length" class="muted-status">
          No generated letters available for this visit yet.
        </p>
        <ul v-else class="letter-list">
          <li v-for="letter in letters" :key="letter.id" class="letter-item">
            <strong>{{ formatClinicalEnum(letter.letter_type) }}</strong> ·
            {{ letter.subject || formatIsoDateShort(letter.created_at) }}
            <pre class="letter-body">{{
              letter.rendered_content || letter.body_markdown
            }}</pre>
          </li>
        </ul>
      </div>

      <div
        v-if="activeCompleteVisit && canComplete"
        id="modal-complete-visit"
        class="mutation-card"
      >
        <h4>
          Complete {{ activeCompleteVisit.visit_type }} visit for
          {{ activeCompleteVisit.site_id }}
        </h4>
        <div class="form-row">
          <label for="complete-visit-date">Actual date</label>
          <input
            id="complete-visit-date"
            v-model="completeActualDate"
            type="date"
          />
          <label for="complete-finding-desc">Quick observation</label>
          <input
            id="complete-finding-desc"
            v-model="completeFindingDesc"
            type="text"
            placeholder="Describe any monitoring observation"
          />
          <button
            id="btn-add-visit-finding"
            type="button"
            class="btn btn-secondary btn-sm"
            @click="addStructuredFinding"
          >
            + Add structured finding
          </button>
        </div>

        <div
          v-for="(finding, idx) in structuredFindings"
          :key="idx"
          class="form-row"
        >
          <label :for="`finding-text-${idx}`">Finding {{ idx + 1 }}</label>
          <input
            :id="`finding-text-${idx}`"
            v-model="finding.text"
            type="text"
            placeholder="Monitoring finding description"
          />
          <label :for="`finding-severity-${idx}`">Severity</label>
          <select :id="`finding-severity-${idx}`" v-model="finding.severity">
            <option value="MINOR">Minor</option>
            <option value="MAJOR">Major</option>
            <option value="CRITICAL">Critical</option>
          </select>
          <label :for="`finding-resolution-${idx}`">Resolution</label>
          <select
            :id="`finding-resolution-${idx}`"
            v-model="finding.resolution_status"
          >
            <option value="OPEN">Open</option>
            <option value="IN_PROGRESS">In progress</option>
            <option value="RESOLVED">Resolved</option>
          </select>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="removeStructuredFinding(idx)"
          >
            Remove
          </button>
        </div>

        <div class="form-row">
          <label for="complete-visit-reason">Reason for change</label>
          <input
            id="complete-visit-reason"
            v-model="completeReason"
            type="text"
            placeholder="Trip completion summary"
          />
          <button
            id="btn-confirm-complete-visit"
            type="button"
            class="btn btn-primary"
            :disabled="!completeActualDate || !completeReason.trim() || saving"
            @click="confirmComplete"
          >
            {{ saving ? "Saving..." : "Submit completion" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="activeCompleteVisit = null"
          >
            Cancel
          </button>
        </div>
      </div>

      <div
        v-if="activeCompileVisit && canSchedule"
        id="modal-compile-letter"
        class="mutation-card"
      >
        <h4>
          Compile GxP visit letter for {{ activeCompileVisit.site_id }} ({{
            activeCompileVisit.visit_type
          }})
        </h4>
        <div class="form-row">
          <label for="compile-letter-template">Template</label>
          <select id="compile-letter-template" v-model="compileTemplateName">
            <option value="confirmation_letter">Confirmation letter</option>
            <option value="follow_up_letter">Follow-up letter</option>
          </select>
          <label class="checkbox-inline">
            <input
              id="compile-letter-finalize"
              v-model="compileFinalize"
              type="checkbox"
            />
            Finalize with Part 11 signature &amp; eTMF filing
          </label>
          <template v-if="compileFinalize">
            <label for="compile-sig-password">Password (Part 11)</label>
            <input
              id="compile-sig-password"
              v-model="compilePassword"
              v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
              placeholder="Re-enter password to sign"
            />
          </template>
          <label for="compile-letter-reason">Reason for change</label>
          <input
            id="compile-letter-reason"
            v-model="compileReason"
            type="text"
            placeholder="Reason for issuing visit letter"
          />
          <button
            id="btn-confirm-compile-letter"
            type="button"
            class="btn btn-primary"
            :disabled="
              !compileReason.trim() ||
              (compileFinalize && !compilePassword) ||
              saving
            "
            @click="confirmCompileLetter"
          >
            {{ saving ? "Compiling..." : "Compile letter" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="cancelCompileLetter"
          >
            Cancel
          </button>
        </div>
      </div>

      <div v-if="activeSignOffVisit && canSignOff" class="mutation-card">
        <h4>
          Supervisory sign-off for {{ activeSignOffVisit.site_id }} ({{
            activeSignOffVisit.visit_type
          }})
        </h4>
        <div class="form-row">
          <label for="signoff-visit-reason">Endorsement rationale</label>
          <input
            id="signoff-visit-reason"
            v-model="signOffReason"
            type="text"
            placeholder="Supervisory review endorsement"
          />
          <button
            id="btn-confirm-signoff-visit"
            type="button"
            class="btn btn-primary"
            :disabled="!signOffReason.trim() || saving"
            @click="confirmSignOff"
          >
            {{ saving ? "Signing..." : "Confirm sign-off" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="activeSignOffVisit = null"
          >
            Cancel
          </button>
        </div>
      </div>

      <form
        v-if="canSchedule"
        id="schedule-visit-form"
        class="mutation-card"
        @submit.prevent="scheduleVisit"
      >
        <h4>Schedule monitoring visit</h4>
        <div class="form-row">
          <label for="new-visit-site">Site</label>
          <input
            id="new-visit-site"
            v-model="newSiteId"
            type="text"
            placeholder="Site code"
          />
          <label for="new-visit-type">Type</label>
          <select id="new-visit-type" v-model="newVisitType">
            <option value="SIV">Site Initiation (SIV)</option>
            <option value="IMV">Interim Monitoring (IMV)</option>
            <option value="COV">Close-Out (COV)</option>
          </select>
          <label for="new-visit-cra">Monitor ID</label>
          <input
            id="new-visit-cra"
            v-model="newCraId"
            type="text"
            placeholder="cra_monitor"
          />
          <label for="new-visit-date">Scheduled date</label>
          <input id="new-visit-date" v-model="newScheduledDate" type="date" />
          <label for="new-visit-reason">Reason for change</label>
          <input
            id="new-visit-reason"
            v-model="newReason"
            type="text"
            placeholder="Scheduling justification"
          />
          <button
            id="btn-schedule-visit"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !studyInput.trim() ||
              !newSiteId.trim() ||
              !newCraId.trim() ||
              !newScheduledDate ||
              !newReason.trim() ||
              saving
            "
          >
            {{ saving ? "Scheduling..." : "Schedule visit" }}
          </button>
        </div>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  buildStepUpVerificationPayload,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
} from "ui";
import {
  ctmsApi,
  type GeneratedVisitLetter,
  type MonitoringFindingCreate,
  type MonitoringVisit,
  type VisitLetterCompilationResult,
} from "../../api/ctmsApi";
import {
  MONITORING_LETTER_COMPILE_ROLE_REASON,
  MONITORING_VISIT_SIGNOFF_ROLE_REASON,
  canCompleteMonitoringVisits,
  canScheduleMonitoringVisits,
  canSignOffMonitoringVisits,
} from "../../canvas/authority";
import {
  formatIsoDateShort,
  useScopedWorkspaceSection,
} from "../../composables/useScopedWorkspaceSection";
import apiClient from "../../services/api";
import {
  formatClinicalEnum,
  formatClinicalStatus,
} from "../../utils/clinicalLabels";
import SupportDetails from "../canvas/SupportDetails.vue";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

function formatCompiledLetterDetail(
  item: VisitLetterCompilationResult
): string {
  const parts = [`Letter ID: ${item.letterId}`];
  if (item.etmfDocumentId) {
    parts.push(`eTMF Document: ${item.etmfDocumentId}`);
  }
  return parts.join(" · ");
}

interface StructuredFindingDraft {
  text: string;
  severity: string;
  resolution_status: string;
}

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    siteId?: string;
  }>(),
  {
    studyCode: "",
    siteId: "",
  }
);

const newSiteId = ref("");
const {
  authStore,
  toasts,
  assignedStudies,
  assignedSites,
  studyInput,
  siteInput,
  loading,
  loaded,
  saving,
  loadError,
  scopeGuidance,
  syncActiveScope,
  recordLoadFailure,
} = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => {
    void loadVisits();
  },
  onSitePropChange: (val) => {
    if (val) newSiteId.value = val;
  },
  fallbackErrorTitle: "Monitoring visits could not be loaded",
  fallbackErrorMessage: "Monitoring visits could not be loaded.",
});

newSiteId.value = siteInput.value;
const canSchedule = computed(() => canScheduleMonitoringVisits(authStore));
const canComplete = computed(() => canCompleteMonitoringVisits(authStore));
const canSignOff = computed(() => canSignOffMonitoringVisits(authStore));

const visits = ref<MonitoringVisit[]>([]);
const newVisitType = ref("IMV");
const newCraId = ref(authStore.userId || "cra_monitor");
const newScheduledDate = ref("");
const newReason = ref("");

const activeCompleteVisit = ref<MonitoringVisit | null>(null);
const completeActualDate = ref("");
const completeFindingDesc = ref("");
const structuredFindings = ref<StructuredFindingDraft[]>([]);
const completeReason = ref("");

const activeCompileVisit = ref<MonitoringVisit | null>(null);
const compileTemplateName = ref("follow_up_letter");
const compileFinalize = ref(true);
const compilePassword = ref("");
const compileReason = ref("");
const compiledLetters = ref<VisitLetterCompilationResult[]>([]);

const activeSignOffVisit = ref<MonitoringVisit | null>(null);
const signOffReason = ref("");

const selectedLettersVisit = ref<MonitoringVisit | null>(null);
const letters = ref<GeneratedVisitLetter[]>([]);

async function loadVisits() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  if (!study) return;

  syncActiveScope(study, site);
  if (site) newSiteId.value = site;

  loading.value = true;
  loadError.value = "";
  scopeGuidance.value = "";

  try {
    visits.value = await ctmsApi.listMonitoringVisits({
      studyId: study,
      siteId: site || undefined,
    });
    loaded.value = true;
  } catch (err) {
    visits.value = [];
    recordLoadFailure(err, study, site);
  } finally {
    loading.value = false;
  }
}

async function scheduleVisit() {
  const study = studyInput.value.trim();
  const site = newSiteId.value.trim();
  const reason = newReason.value.trim();
  if (
    !study ||
    !site ||
    !newCraId.value.trim() ||
    !newScheduledDate.value ||
    !reason
  ) {
    return;
  }
  saving.value = true;
  try {
    await ctmsApi.scheduleMonitoringVisit(
      {
        study_id: study,
        site_id: site,
        cra_id: newCraId.value.trim(),
        visit_type: newVisitType.value,
        scheduled_date: `${newScheduledDate.value}T09:00:00Z`,
        reason_for_change: reason,
      },
      reason
    );
    newReason.value = "";
    toasts.push({
      tone: "success",
      title: "Monitoring visit scheduled",
      message: `${newVisitType.value} scheduled for ${site}.`,
    });
    await loadVisits();
  } catch (err) {
    toasts.apiError("Visit could not be scheduled", err);
  } finally {
    saving.value = false;
  }
}

function prepareComplete(visit: MonitoringVisit) {
  activeCompleteVisit.value = visit;
  completeActualDate.value = new Date().toISOString().slice(0, 10);
  completeFindingDesc.value = "";
  structuredFindings.value = [];
  completeReason.value = "";
}

function addStructuredFinding() {
  structuredFindings.value.push({
    text: "",
    severity: "MINOR",
    resolution_status: "OPEN",
  });
}

function removeStructuredFinding(index: number) {
  structuredFindings.value.splice(index, 1);
}

async function confirmComplete() {
  const visit = activeCompleteVisit.value;
  const reason = completeReason.value.trim();
  if (!visit || !completeActualDate.value || !reason || !canComplete.value) {
    return;
  }

  const findings: MonitoringFindingCreate[] = [];
  if (completeFindingDesc.value.trim()) {
    findings.push({
      text: completeFindingDesc.value.trim(),
      severity: "MINOR",
      resolution_status: "OPEN",
    });
  }
  for (const item of structuredFindings.value) {
    if (item.text.trim()) {
      findings.push({
        text: item.text.trim(),
        severity: item.severity,
        resolution_status: item.resolution_status,
      });
    }
  }

  saving.value = true;
  try {
    await ctmsApi.completeMonitoringVisit(
      visit.id,
      {
        actual_date: `${completeActualDate.value}T17:00:00Z`,
        findings,
        reason_for_change: reason,
      },
      reason
    );
    activeCompleteVisit.value = null;
    completeReason.value = "";
    structuredFindings.value = [];
    toasts.push({
      tone: "success",
      title: "Monitoring visit completed",
      message: `${visit.visit_type} completed for ${visit.site_id}.`,
    });
    await loadVisits();
  } catch (err) {
    toasts.apiError("Visit could not be completed", err);
  } finally {
    saving.value = false;
  }
}

function prepareCompileLetter(visit: MonitoringVisit) {
  activeCompileVisit.value = visit;
  compileTemplateName.value =
    visit.status === "SCHEDULED" ? "confirmation_letter" : "follow_up_letter";
  compileFinalize.value = true;
  compilePassword.value = "";
  compileReason.value = "";
}

function cancelCompileLetter() {
  compilePassword.value = "";
  activeCompileVisit.value = null;
}

async function confirmCompileLetter() {
  const visit = activeCompileVisit.value;
  const reason = compileReason.value.trim();
  const password = compilePassword.value;
  compilePassword.value = "";
  if (!visit || !reason || !canSchedule.value) return;
  if (compileFinalize.value && !password) return;

  saving.value = true;
  try {
    let sigToken: string | undefined;
    const signerUsername =
      authStore.identity?.username ||
      authStore.user?.username ||
      authStore.userId ||
      visit.cra_id;

    if (compileFinalize.value) {
      const verificationPayload = buildStepUpVerificationPayload({
        username: signerUsername,
        password,
        totp: null,
        action: "/api/v1/ctms/letters/compile",
        semanticAction: "ctms.letter.compile",
      });
      const verifyRes = await apiClient.post(
        "/api/v1/auth/signature-verification",
        verificationPayload
      );
      sigToken = verifyRes?.sig_token;
      if (!sigToken) {
        throw new Error(
          "Signature verification did not return a signature token."
        );
      }
    }

    const compiled = await ctmsApi.compileVisitLetter(
      compileTemplateName.value,
      {
        craId: visit.cra_id || signerUsername,
        createdAt: new Date().toISOString(),
        studyId: visit.study_id || studyInput.value.trim(),
        siteId: visit.site_id,
        visitType: visit.visit_type,
        scheduledDate: visit.scheduled_date,
        actualDate: visit.actual_date ?? null,
        findings: (visit.findings ?? []).map((f) => ({
          text: f.text,
          severity: f.severity,
          resolutionStatus: f.resolution_status || "OPEN",
        })),
        visitId: visit.id,
        finalize: compileFinalize.value,
        signerName: compileFinalize.value ? signerUsername : null,
        signingPurpose: compileFinalize.value
          ? "Monitoring Visit Report Issuance"
          : null,
        investigatorEmail: null,
        reasonForChange: reason,
      },
      {
        changeReason: reason,
        sigToken,
      }
    );

    compiledLetters.value = [
      compiled,
      ...compiledLetters.value.filter((c) => c.letterId !== compiled.letterId),
    ];
    activeCompileVisit.value = null;
    compileReason.value = "";
    toasts.push({
      tone: "success",
      title: "Visit letter compiled",
      message: `${compiled.letterId} (${compiled.status}) compiled.`,
    });
  } catch (err) {
    toasts.apiError("Visit letter could not be compiled", err);
  } finally {
    saving.value = false;
  }
}

async function downloadCompiledLetter(letter: VisitLetterCompilationResult) {
  try {
    const blob = await ctmsApi.downloadVisitLetterPdf(letter.letterId);
    const blobUrl = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = blobUrl;
    link.download = `${letter.letterType}_${letter.letterId}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(blobUrl);
  } catch (err) {
    toasts.apiError("Visit letter PDF could not be downloaded", err);
  }
}

function prepareSignOff(visit: MonitoringVisit) {
  activeSignOffVisit.value = visit;
  signOffReason.value = "";
}

async function confirmSignOff() {
  const visit = activeSignOffVisit.value;
  const reason = signOffReason.value.trim();
  if (!visit || !reason || !canSignOff.value) return;

  saving.value = true;
  try {
    await ctmsApi.signOffMonitoringVisit(visit.id, reason);
    activeSignOffVisit.value = null;
    signOffReason.value = "";
    toasts.push({
      tone: "success",
      title: "Supervisory sign-off recorded",
      message: `${visit.visit_type} visit for ${visit.site_id} signed off.`,
    });
    await loadVisits();
  } catch (err) {
    toasts.apiError("Supervisory sign-off failed", err);
  } finally {
    saving.value = false;
  }
}

async function viewLetters(visit: MonitoringVisit) {
  selectedLettersVisit.value = visit;
  letters.value = [];
  try {
    letters.value = await ctmsApi.listMonitoringVisitLetters(visit.id);
  } catch (err) {
    toasts.apiError("Monitoring visit letters could not be loaded", err);
  }
}

onMounted(() => {
  if (studyInput.value.trim()) {
    void loadVisits();
  }
});
</script>

<style scoped>
.visit-signed_off,
.visit-completed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.visit-scheduled {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.card-subheader {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.checkbox-inline {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.letter-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 10px;
}

.letter-item {
  padding: 10px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
}

.letter-body {
  margin: 8px 0 0;
  white-space: pre-wrap;
  font-size: 0.82rem;
  color: var(--semantic-color-text-body);
}
</style>
