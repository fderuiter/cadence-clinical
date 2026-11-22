<template>
  <section
    id="site-activation-panel"
    class="card site-activation"
    aria-labelledby="site-activation-title"
  >
    <h3 id="site-activation-title" class="card-title">Site activation</h3>

    <form
      id="site-activation-scope"
      class="scope"
      aria-label="Study and site"
      @submit.prevent="checkStatus"
    >
      <template v-if="!props.studyCode">
        <label for="site-activation-study">Study</label>
        <input
          id="site-activation-study"
          v-model="studyCode"
          type="text"
          autocomplete="off"
          placeholder="Study code"
          :list="
            assignedStudies.length
              ? 'site-activation-assigned-studies'
              : undefined
          "
        />
        <datalist
          v-if="assignedStudies.length"
          id="site-activation-assigned-studies"
        >
          <option
            v-for="study in assignedStudies"
            :key="study"
            :value="study"
          />
        </datalist>
      </template>
      <label for="site-activation-site-picker">Pick site</label>
      <select
        id="site-activation-site-picker"
        :value="pickerValue"
        class="site-picker"
        aria-label="Pick a study site"
        @focus="loadStudySites"
        @change="onPickSite"
      >
        <option value="">Custom / enter site code</option>
        <option v-for="code in siteOptions" :key="code" :value="code">
          {{ code }}
        </option>
      </select>
      <label for="site-activation-site">Site</label>
      <input
        id="site-activation-site"
        v-model="siteCode"
        type="text"
        autocomplete="off"
        placeholder="Site code"
        :list="
          assignedSites.length ? 'site-activation-assigned-sites' : undefined
        "
      />
      <datalist v-if="assignedSites.length" id="site-activation-assigned-sites">
        <option v-for="site in assignedSites" :key="site" :value="site" />
      </datalist>
      <button
        id="btn-check-site-compliance"
        type="submit"
        class="btn btn-secondary"
        :disabled="!canCheck || loading"
      >
        Check eTMF status
      </button>
    </form>

    <div
      v-if="
        (!props.studyCode && assignedStudies.length) || assignedSites.length
      "
      id="site-activation-assigned-scope"
      class="assigned-scope"
      aria-label="Assigned scope shortcuts"
    >
      <div
        v-if="!props.studyCode && assignedStudies.length"
        class="scope-pill-group"
      >
        <span class="scope-pill-label">Assigned studies:</span>
        <button
          v-for="study in assignedStudies"
          :key="study"
          type="button"
          class="scope-pill"
          :class="{ active: studyCode.trim() === study }"
          @click="selectAssignedStudy(study)"
        >
          {{ study }}
        </button>
      </div>
      <div v-if="assignedSites.length" class="scope-pill-group">
        <span class="scope-pill-label">Assigned sites:</span>
        <button
          v-for="site in assignedSites"
          :key="site"
          type="button"
          class="scope-pill"
          :class="{ active: siteCode.trim() === site }"
          @click="selectAssignedSite(site)"
        >
          {{ site }}
        </button>
      </div>
    </div>

    <p v-if="!checked" id="site-compliance-status" class="status" role="status">
      Enter a study and site to see whether eTMF reports the site's activation
      documents complete.
    </p>
    <p
      v-else-if="loading"
      id="site-compliance-status"
      class="status"
      role="status"
    >
      Checking eTMF status...
    </p>
    <div
      v-else-if="scopeGuidance"
      id="site-compliance-status"
      class="status status-scope-guidance"
      role="status"
    >
      <p id="site-scope-guidance" class="scope-guidance-text">
        {{ scopeGuidance }}
      </p>
    </div>
    <p
      v-else-if="loadError"
      id="site-compliance-status"
      class="status status-error"
      role="status"
    >
      The eTMF status couldn't be loaded, so activation is unavailable.
    </p>
    <div v-else-if="compliance" id="site-compliance-status" role="status">
      <div
        v-if="compliance.is_activated"
        id="site-activated-summary"
        class="activated-banner"
      >
        <span class="activated-badge">Activated</span>
        <span>
          Site {{ compliance.site_id }} is active for enrollment in
          {{ compliance.study_id }}
          <template v-if="compliance.activated_by">
            · activated by {{ compliance.activated_by }}
          </template>
          <template v-if="compliance.activated_at">
            on {{ formatTimestamp(compliance.activated_at) }}
          </template>
          .
        </span>
      </div>

      <p v-if="compliance.is_complete" class="status status-complete">
        Complete: eTMF reports every activation document for site
        {{ compliance.site_id }} in {{ compliance.study_id }} approved.
      </p>
      <template v-else-if="compliance.missing_documents.length">
        <p class="status status-incomplete">
          Incomplete: eTMF reports these activation documents missing.
        </p>
        <ul id="site-missing-documents">
          <li v-for="doc in compliance.missing_documents" :key="doc">
            {{ doc }}
          </li>
        </ul>
      </template>
      <p v-else class="status status-incomplete">
        Not confirmed: eTMF has not reported this site's activation documents
        complete.
      </p>
    </div>

    <div
      v-if="compliance?.is_complete && !compliance?.is_activated && canActivate"
      class="activate"
    >
      <label for="site-activation-reason">Reason for activation</label>
      <textarea
        id="site-activation-reason"
        v-model="reason"
        rows="2"
        placeholder="Why the site is being activated (recorded in the audit trail)"
      />
      <button
        id="btn-activate-site"
        type="button"
        class="btn btn-primary"
        :disabled="!reason.trim() || activating"
        @click="activate"
      >
        {{
          activating ? "Activating..." : `Activate site ${compliance.site_id}`
        }}
      </button>
    </div>
    <p
      v-else-if="
        compliance?.is_complete && !compliance?.is_activated && !canActivate
      "
      id="site-activation-readonly-note"
      class="readonly-note"
    >
      Read-only view: site activation requires Sponsor Data Management or System
      Administrator authorization (site_compliance:activate).
    </p>

    <p v-if="!props.studyCode" class="unavailable-note">
      Grant financials, CRA workload allocation, recruitment target updates, and
      adaptive dose escalation aren't available yet in this pre-release
      environment (Orbit and Allocate roadmap). See <PlansRoadmapLink />.
    </p>
  </section>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { ctmsApi } from "../../api/ctmsApi";
import { executionApi } from "../../api/executionApi";
import { canActivateSite } from "../../canvas/authority";
import { useAuthStore } from "../../stores/auth";
import { useClinicalStore } from "../../stores/clinical";
import { useToastStore } from "../../stores/toasts.js";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../../utils/callerScope";
import PlansRoadmapLink from "./PlansRoadmapLink.vue";

/**
 * Site activation against Execution: the site's eTMF completeness for SITE_ACTIVATION, and activation once
 * it is complete. Execution refuses activation for an incomplete site and audits every attempt, with the
 * reason given here as the change reason. Only callers holding site_compliance:activate (sysadmin or
 * sponsor_dm) can submit activation.
 */
const DEFAULT_KNOWN_SITES = ["SITE-101", "SITE-102"];

const props = defineProps({
  /** Fixes the study (the Clinical Canvas passes the study it shows); without it the user types the study. */
  studyCode: { type: String, default: "" },
  /** Optional site filter passed from Clinical Canvas URL query. */
  siteId: { type: String, default: "" },
});

const authStore = useAuthStore();
const store = useClinicalStore();
const toasts = useToastStore();

const assignedStudies = computed(() => authStore.assignedStudies || []);
const assignedSites = computed(() => authStore.assignedSites || []);
const canActivate = computed(() => canActivateSite(authStore));

const studyCode = ref(
  props.studyCode ||
    store.activeStudyId ||
    (assignedStudies.value.length ? assignedStudies.value[0] : "")
);
const siteCode = ref(
  props.siteId ||
    store.activeSiteId ||
    (assignedSites.value.length ? assignedSites.value[0] : "")
);
const discoveredSites = ref([]);
const compliance = ref(null);
const checked = ref(false);
const loading = ref(false);
const loadError = ref(false);
const scopeGuidance = ref("");
const reason = ref("");
const activating = ref(false);

const siteOptions = computed(() => {
  const merged = new Set(DEFAULT_KNOWN_SITES);
  for (const s of assignedSites.value) {
    if (s) merged.add(s);
  }
  for (const s of discoveredSites.value) {
    if (s) merged.add(s);
  }
  if (siteCode.value?.trim()) {
    merged.add(siteCode.value.trim());
  }
  return [...merged].sort((a, b) => a.localeCompare(b));
});

const pickerValue = computed(() => {
  const trimmed = siteCode.value?.trim() || "";
  return siteOptions.value.includes(trimmed) ? trimmed : "";
});

function onPickSite(event) {
  const chosen = event.target?.value || "";
  if (chosen) {
    siteCode.value = chosen;
  }
}

async function loadStudySites() {
  const study = studyCode.value?.trim();
  if (!study) return;
  try {
    const milestones = await ctmsApi.listSiteMilestones({ study_id: study });
    if (Array.isArray(milestones)) {
      discoveredSites.value = milestones
        .map((m) => m.site_id)
        .filter((s) => Boolean(s));
    }
  } catch {
    // Optional site discovery should never block manual site entry.
  }
}

// The canvas switched study: the status shown was for the previous one.
watch(
  () => props.studyCode,
  (value) => {
    if (!value) return;
    studyCode.value = value;
    discoveredSites.value = [];
    compliance.value = null;
    checked.value = false;
    loadError.value = false;
    scopeGuidance.value = "";
  }
);

watch(
  () => props.siteId,
  (value) => {
    if (!value) return;
    siteCode.value = value;
  }
);

const canCheck = computed(
  () => Boolean(studyCode.value.trim()) && Boolean(siteCode.value.trim())
);

function formatTimestamp(iso) {
  if (!iso) return "";
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? String(iso) : parsed.toUTCString();
}

function selectAssignedStudy(study) {
  studyCode.value = study;
  if (canCheck.value) checkStatus();
}

function selectAssignedSite(site) {
  siteCode.value = site;
  if (canCheck.value) checkStatus();
}

async function checkStatus() {
  if (!canCheck.value) return;
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  store.activeStudyId = study;
  store.activeSiteId = site;
  checked.value = true;
  loading.value = true;
  loadError.value = false;
  scopeGuidance.value = "";
  compliance.value = null;
  try {
    compliance.value = await executionApi.getSiteComplianceStatus(site, study);
  } catch (err) {
    if (isForbiddenScopeError(err)) {
      scopeGuidance.value = formatScopeForbiddenGuidance(err, {
        studyId: study,
        siteId: site,
        assignedStudies: assignedStudies.value,
        assignedSites: assignedSites.value,
        hasAllStudiesScope: authStore.hasAllStudiesScope,
      });
    } else {
      loadError.value = true;
      toasts.apiError("Couldn't load the site's eTMF status", err);
    }
  } finally {
    loading.value = false;
  }
}

async function activate() {
  if (!canActivate.value) return;
  const current = compliance.value;
  const why = reason.value.trim();
  if (
    !canActivate.value ||
    !current?.is_complete ||
    current?.is_activated ||
    !why ||
    !current.site_id
  ) {
    return;
  }
  activating.value = true;
  try {
    const result = await executionApi.activateSite(
      current.site_id,
      current.study_id,
      why
    );
    toasts.push({
      tone: "success",
      title: `Site ${current.site_id} activated`,
      message: result.message,
    });
    reason.value = "";
    try {
      const refreshed = await executionApi.getSiteComplianceStatus(
        current.site_id,
        current.study_id
      );
      if (refreshed) {
        compliance.value = refreshed;
      } else {
        compliance.value = {
          ...current,
          is_activated: true,
          activated_by: authStore.userId || current.activated_by || null,
        };
      }
    } catch {
      compliance.value = {
        ...current,
        is_activated: true,
        activated_by: authStore.userId || current.activated_by || null,
      };
    }
  } catch (err) {
    if (isForbiddenScopeError(err)) {
      scopeGuidance.value = formatScopeForbiddenGuidance(err, {
        studyId: current.study_id,
        siteId: current.site_id,
        assignedStudies: assignedStudies.value,
        assignedSites: assignedSites.value,
        hasAllStudiesScope: authStore.hasAllStudiesScope,
      });
      compliance.value = null;
    } else {
      toasts.apiError(`Site ${current.site_id} was not activated`, err);
    }
  } finally {
    activating.value = false;
  }
}

// Opened for a study and site (CTMS reads ?studyId= and ?siteId=, or token claims provide assigned scope): show status straight away.
onMounted(() => {
  if (canCheck.value) checkStatus();
});

defineExpose({ checkStatus, loadStudySites });
</script>

<style scoped>
.site-activation {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
  padding: 20px;
}

.scope,
.activate {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-sm, 8px);
}

.scope label,
.activate label {
  font-weight: bold;
}

.scope input,
.site-picker {
  flex: 0 1 12rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.assigned-scope {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  font-size: 0.85rem;
}

.scope-pill-group {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.scope-pill-label {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.scope-pill {
  padding: 2px 8px;
  border-radius: 999px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  font-size: 0.8rem;
  cursor: pointer;
}

.scope-pill.active {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.activate {
  flex-direction: column;
  align-items: stretch;
}

.activate textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
}

.activate button {
  align-self: flex-start;
}

.status {
  margin: 0;
}

.status-complete {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}

.status-incomplete {
  color: var(--semantic-color-warning-text);
  font-weight: 600;
}

.status-error {
  color: var(--semantic-color-error);
  font-weight: 600;
}

.status-scope-guidance {
  padding: 12px 14px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-warning-border-strong);
  background: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-text);
}

.scope-guidance-text {
  margin: 0;
}

.activated-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin-bottom: 10px;
  padding: 10px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-success-border);
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  font-size: 0.92rem;
}

.activated-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
}

.readonly-note {
  margin: 0;
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-secondary);
  font-size: 0.9rem;
}

.unavailable-note {
  margin: 0;
  font-size: 0.875rem;
  color: var(--semantic-color-warning-text);
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 4px;
  padding: 8px 12px;
}
</style>
