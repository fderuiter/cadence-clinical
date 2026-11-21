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
          Schedule monitoring visits, record trip completion findings, review
          generated letters, and complete supervisory sign-off.
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
              <td>{{ visit.visit_type }}</td>
              <td>{{ visit.cra_id }}</td>
              <td>{{ formatIsoDateShort(visit.scheduled_date) }}</td>
              <td>
                <span
                  class="status-pill"
                  :class="`visit-${visit.status.toLowerCase()}`"
                >
                  {{ visit.status }}
                </span>
              </td>
              <td>{{ visit.findings?.length ?? 0 }}</td>
              <td class="action-cell">
                <button
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="viewLetters(visit)"
                >
                  Letters
                </button>
                <button
                  v-if="canComplete && visit.status === 'SCHEDULED'"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  @click="prepareComplete(visit)"
                >
                  Complete
                </button>
                <button
                  v-if="canSignOff && visit.status === 'COMPLETED'"
                  type="button"
                  class="btn btn-primary btn-sm"
                  @click="prepareSignOff(visit)"
                >
                  Sign off
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        v-if="selectedLettersVisit"
        id="monitoring-letters-panel"
        class="mutation-card"
      >
        <div class="card-subheader">
          <h4>
            Generated letters for {{ selectedLettersVisit.site_id }} ({{
              selectedLettersVisit.visit_type
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
            <strong>{{ letter.letter_type }}</strong> · {{ letter.subject }}
            <pre class="letter-body">{{ letter.body_markdown }}</pre>
          </li>
        </ul>
      </div>

      <div v-if="activeCompleteVisit && canComplete" class="mutation-card">
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
          <label for="complete-finding-desc">Optional finding</label>
          <input
            id="complete-finding-desc"
            v-model="completeFindingDesc"
            type="text"
            placeholder="Describe any monitoring observation"
          />
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
  ctmsApi,
  type GeneratedVisitLetter,
  type MonitoringVisit,
} from "../../api/ctmsApi";
import {
  canCompleteMonitoringVisits,
  canScheduleMonitoringVisits,
  canSignOffMonitoringVisits,
} from "../../canvas/authority";
import {
  formatIsoDateShort,
  useScopedWorkspaceSection,
} from "../../composables/useScopedWorkspaceSection";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

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
const completeReason = ref("");

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
  completeFindingDesc.value = "";
  completeReason.value = "";
}

async function confirmComplete() {
  const visit = activeCompleteVisit.value;
  const reason = completeReason.value.trim();
  if (!visit || !reason || !canComplete.value) return;

  saving.value = true;
  try {
    const findings = completeFindingDesc.value.trim()
      ? [
          {
            description: completeFindingDesc.value.trim(),
            severity: "MINOR",
          },
        ]
      : [];
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
  margin-bottom: 8px;
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

.data-table {
  font-variant-numeric: tabular-nums;
}
</style>
