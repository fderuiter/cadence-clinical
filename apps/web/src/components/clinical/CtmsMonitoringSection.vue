<template>
  <section
    id="ctms-monitoring-section"
    class="card ctms-section"
    aria-labelledby="ctms-monitoring-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-monitoring-title" class="card-title" tabindex="-1">
          Monitoring visits
        </h3>
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
      <ClinicalDataTable
        v-else
        hide-header
        plain
        labelled-by="ctms-monitoring-title"
        wrapper-class="table-wrap"
        table-id="monitoring-visits-table"
        table-class="data-table"
        :columns="visitColumns"
        :data="visits"
        row-key="id"
      >
        <template #cell-site_id="{ row: visit }">
          <strong>{{ visit.site_id }}</strong>
        </template>
        <template #cell-visit_type="{ row: visit }">
          {{ formatClinicalEnum(visit.visit_type) }}
        </template>
        <template #cell-cra_id="{ row: visit }">
          {{ visit.cra_id }}
        </template>
        <template #cell-scheduled_date="{ row: visit }">
          {{ formatIsoDateShort(visit.scheduled_date) }}
        </template>
        <template #cell-status="{ row: visit }">
          <span
            class="status-pill"
            :class="`visit-${visit.status.toLowerCase()}`"
          >
            {{ formatClinicalStatus(visit.status) }}
          </span>
        </template>
        <template #cell-findings="{ row: visit }">
          {{ visit.findings?.length ?? 0 }}
        </template>
        <template #cell-actions="{ row: visit }">
          <div class="action-cell">
            <button
              :id="`btn-visit-letters-${visit.id}`"
              type="button"
              class="btn btn-secondary btn-sm"
              @click="viewLetters(visit)"
            >
              Letters<span class="sr-only">
                for {{ visit.visit_type }} visit at {{ visit.site_id }}</span
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
                for {{ visit.visit_type }} visit at {{ visit.site_id }}</span
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
          </div>
        </template>
      </ClinicalDataTable>

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
            id="btn-close-monitoring-letters"
            type="button"
            class="btn btn-secondary btn-sm"
            @click="closeLettersPanel"
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
            {{ formatIsoDateShort(letter.created_at) }}
            <pre class="letter-body">{{ letter.rendered_content }}</pre>
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
          <ClinicalInput
            id="complete-visit-date"
            v-model="completeActualDate"
            label="Actual date"
            type="date"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="complete-finding-desc"
            v-model="completeFindingDesc"
            label="Quick observation"
            type="text"
            placeholder="Describe any monitoring observation"
            density="compact"
            :show-query-flag="false"
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
          <ClinicalInput
            :id="`finding-text-${idx}`"
            v-model="finding.text"
            :label="`Finding ${idx + 1}`"
            type="text"
            placeholder="Monitoring finding description"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            :id="`finding-severity-${idx}`"
            v-model="finding.severity"
            as="select"
            label="Severity"
            density="compact"
            :show-query-flag="false"
          >
            <option value="MINOR">Minor</option>
            <option value="MAJOR">Major</option>
            <option value="CRITICAL">Critical</option>
          </ClinicalInput>
          <ClinicalInput
            :id="`finding-resolution-${idx}`"
            v-model="finding.resolution_status"
            as="select"
            label="Resolution"
            density="compact"
            :show-query-flag="false"
          >
            <option value="OPEN">Open</option>
            <option value="IN_PROGRESS">In progress</option>
            <option value="RESOLVED">Resolved</option>
          </ClinicalInput>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="removeStructuredFinding(idx)"
          >
            Remove
          </button>
        </div>

        <div class="form-row">
          <ClinicalInput
            id="complete-visit-reason"
            v-model="completeReason"
            label="Reason for change"
            type="text"
            placeholder="Trip completion summary"
            density="compact"
            :show-query-flag="false"
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
            @click="cancelComplete"
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
          <ClinicalInput
            id="compile-letter-template"
            v-model="compileTemplateName"
            as="select"
            label="Template"
            density="compact"
            :show-query-flag="false"
          >
            <option value="confirmation_letter">Confirmation letter</option>
            <option value="follow_up_letter">Follow-up letter</option>
          </ClinicalInput>
          <label class="checkbox-inline">
            <input
              id="compile-letter-finalize"
              v-model="compileFinalize"
              type="checkbox"
            />
            Finalize with Part 11 signature &amp; eTMF filing
          </label>
          <ClinicalFieldLayout
            v-if="compileFinalize"
            id="compile-sig-password"
            label="Password (Part 11)"
            density="compact"
            :show-query-flag="false"
          >
            <input
              id="compile-sig-password"
              v-model="compilePassword"
              class="clinical-input-control"
              v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
              placeholder="Re-enter password to sign"
            />
          </ClinicalFieldLayout>
          <ClinicalInput
            id="compile-letter-reason"
            v-model="compileReason"
            label="Reason for change"
            type="text"
            placeholder="Reason for issuing visit letter"
            density="compact"
            :show-query-flag="false"
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
          <ClinicalInput
            id="signoff-visit-reason"
            v-model="signOffReason"
            label="Endorsement rationale"
            type="text"
            placeholder="Supervisory review endorsement"
            density="compact"
            :show-query-flag="false"
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
            @click="cancelSignOff"
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
          <ClinicalInput
            id="new-visit-site"
            v-model="newSiteId"
            label="Site"
            type="text"
            placeholder="Site code"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="new-visit-type"
            v-model="newVisitType"
            as="select"
            label="Type"
            density="compact"
            :show-query-flag="false"
          >
            <option value="SIV">Site Initiation (SIV)</option>
            <option value="IMV">Interim Monitoring (IMV)</option>
            <option value="COV">Close-Out (COV)</option>
          </ClinicalInput>
          <ClinicalInput
            id="new-visit-cra"
            v-model="newCraId"
            label="Monitor ID"
            type="text"
            placeholder="cra_monitor"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="new-visit-date"
            v-model="newScheduledDate"
            label="Scheduled date"
            type="date"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="new-visit-reason"
            v-model="newReason"
            label="Reason for change"
            type="text"
            placeholder="Scheduling justification"
            density="compact"
            :show-query-flag="false"
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
  ClinicalDataTable,
  ClinicalFieldLayout,
  ClinicalInput,
  handoffFocus,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  useInlineFocusHandoff,
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
  scopedWorkspaceSectionProps,
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

type MonitoringVisitRow = MonitoringVisit & {
  findings?: Array<{
    text: string;
    severity: string;
    resolution_status?: string | null;
  }> | null;
};

const props = defineProps(scopedWorkspaceSectionProps);
const workspace = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => void loadVisits(),
  fallbackErrorTitle: "Monitoring visits could not be loaded",
  fallbackErrorMessage: "Monitoring visits could not be loaded.",
});
const { authStore, toasts, syncActiveScope, recordLoadFailure } = workspace;
const {
  assignedStudies,
  assignedSites,
  studyInput,
  siteInput,
  newSiteId,
  loading,
  loaded,
  saving,
  loadError,
  scopeGuidance,
} = workspace;

const canSchedule = computed(() => canScheduleMonitoringVisits(authStore));
const canComplete = computed(() => canCompleteMonitoringVisits(authStore));
const canSignOff = computed(() => canSignOffMonitoringVisits(authStore));

const visitColumns = [
  { key: "site_id", label: "Site", rowHeader: true },
  { key: "visit_type", label: "Type" },
  { key: "cra_id", label: "Monitor" },
  { key: "scheduled_date", label: "Scheduled" },
  { key: "status", label: "Status" },
  { key: "findings", label: "Findings" },
  { key: "actions", label: "Actions" },
];

const visits = ref<MonitoringVisitRow[]>([]);
const newVisitType = ref("IMV");
const newCraId = ref(authStore.userId || "cra_monitor");
const newScheduledDate = ref("");
const newReason = ref("");

const activeCompleteVisit = ref<MonitoringVisitRow | null>(null);
const completeActualDate = ref("");
const completeFindingDesc = ref("");
const structuredFindings = ref<StructuredFindingDraft[]>([]);
const completeReason = ref("");

const activeCompileVisit = ref<MonitoringVisitRow | null>(null);
const compileTemplateName = ref("follow_up_letter");
const compileFinalize = ref(true);
const compilePassword = ref("");
const compileReason = ref("");
const compiledLetters = ref<VisitLetterCompilationResult[]>([]);

const activeSignOffVisit = ref<MonitoringVisitRow | null>(null);
const signOffReason = ref("");

const selectedLettersVisit = ref<MonitoringVisitRow | null>(null);
const letters = ref<GeneratedVisitLetter[]>([]);

const completeHandoff = useInlineFocusHandoff({
  openTarget: "#complete-visit-date",
  closeFallback: "#ctms-monitoring-title",
  scrollIfOffscreen: true,
});
const compileHandoff = useInlineFocusHandoff({
  openTarget: "#compile-letter-template",
  closeFallback: "#ctms-monitoring-title",
  scrollIfOffscreen: true,
});
const signOffHandoff = useInlineFocusHandoff({
  openTarget: "#signoff-visit-reason",
  closeFallback: "#ctms-monitoring-title",
  scrollIfOffscreen: true,
});
const lettersHandoff = useInlineFocusHandoff({
  openTarget: "#btn-close-monitoring-letters",
  closeFallback: "#ctms-monitoring-title",
  scrollIfOffscreen: true,
});

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
  completeHandoff.rememberTrigger(
    `[data-testid="btn-complete-visit-${visit.id}"]`
  );
  activeCompileVisit.value = null;
  activeSignOffVisit.value = null;
  activeCompleteVisit.value = visit;
  completeActualDate.value = new Date().toISOString().slice(0, 10);
  completeFindingDesc.value = "";
  structuredFindings.value = [];
  completeReason.value = "";
  void completeHandoff.handoff("#complete-visit-date", {
    scrollIfOffscreen: true,
  });
}

function cancelComplete() {
  const visitId = activeCompleteVisit.value?.id;
  activeCompleteVisit.value = null;
  completeReason.value = "";
  void completeHandoff.restoreTrigger([
    visitId ? `[data-testid="btn-complete-visit-${visitId}"]` : null,
    "#ctms-monitoring-title",
  ]);
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
  const remaining = structuredFindings.value.length;
  void handoffFocus(
    remaining > 0
      ? [
          `#finding-text-${Math.min(index, remaining - 1)}`,
          "#btn-add-visit-finding",
        ]
      : ["#btn-add-visit-finding", "#complete-visit-date"]
  );
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
    saving.value = false;
    void completeHandoff.handoff([
      `[data-testid="btn-signoff-visit-${visit.id}"]`,
      `[data-testid="btn-compile-letter-${visit.id}"]`,
      `[id="btn-visit-letters-${visit.id}"]`,
      "#ctms-monitoring-title",
    ]);
  } catch (err) {
    toasts.apiError("Visit could not be completed", err);
  } finally {
    saving.value = false;
  }
}

function prepareCompileLetter(visit: MonitoringVisit) {
  compileHandoff.rememberTrigger(
    `[data-testid="btn-compile-letter-${visit.id}"]`
  );
  activeCompleteVisit.value = null;
  activeSignOffVisit.value = null;
  activeCompileVisit.value = visit;
  compileTemplateName.value =
    visit.status === "SCHEDULED" ? "confirmation_letter" : "follow_up_letter";
  compileFinalize.value = true;
  compilePassword.value = "";
  compileReason.value = "";
  void compileHandoff.handoff("#compile-letter-template", {
    scrollIfOffscreen: true,
  });
}

function cancelCompileLetter() {
  const visitId = activeCompileVisit.value?.id;
  compilePassword.value = "";
  activeCompileVisit.value = null;
  void compileHandoff.restoreTrigger([
    visitId ? `[data-testid="btn-compile-letter-${visitId}"]` : null,
    "#ctms-monitoring-title",
  ]);
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
    saving.value = false;
    void compileHandoff.handoff([
      `[data-testid="btn-download-letter-${compiled.letterId}"]`,
      "#ctms-compiled-letters-container button:not([disabled])",
      `[data-testid="btn-compile-letter-${visit.id}"]`,
      `[id="btn-visit-letters-${visit.id}"]`,
      "#ctms-monitoring-title",
    ]);
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
  signOffHandoff.rememberTrigger(
    `[data-testid="btn-signoff-visit-${visit.id}"]`
  );
  activeCompleteVisit.value = null;
  activeCompileVisit.value = null;
  activeSignOffVisit.value = visit;
  signOffReason.value = "";
  void signOffHandoff.handoff("#signoff-visit-reason", {
    scrollIfOffscreen: true,
  });
}

function cancelSignOff() {
  const visitId = activeSignOffVisit.value?.id;
  activeSignOffVisit.value = null;
  signOffReason.value = "";
  void signOffHandoff.restoreTrigger([
    visitId ? `[data-testid="btn-signoff-visit-${visitId}"]` : null,
    "#ctms-monitoring-title",
  ]);
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
    saving.value = false;
    void signOffHandoff.handoff([
      `[data-testid="btn-compile-letter-${visit.id}"]`,
      `[id="btn-visit-letters-${visit.id}"]`,
      "#ctms-monitoring-title",
    ]);
  } catch (err) {
    toasts.apiError("Supervisory sign-off failed", err);
  } finally {
    saving.value = false;
  }
}

async function viewLetters(visit: MonitoringVisit) {
  lettersHandoff.rememberTrigger(`[id="btn-visit-letters-${visit.id}"]`);
  selectedLettersVisit.value = visit;
  letters.value = [];
  void lettersHandoff.handoff("#btn-close-monitoring-letters", {
    scrollIfOffscreen: true,
  });
  try {
    letters.value = await ctmsApi.listMonitoringVisitLetters(visit.id);
  } catch (err) {
    toasts.apiError("Monitoring visit letters could not be loaded", err);
  }
}

function closeLettersPanel() {
  const visitId = selectedLettersVisit.value?.id;
  selectedLettersVisit.value = null;
  void lettersHandoff.restoreTrigger([
    visitId ? `[id="btn-visit-letters-${visitId}"]` : null,
    "#ctms-monitoring-title",
  ]);
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
