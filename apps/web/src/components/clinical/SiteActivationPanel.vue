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
        />
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
      />
      <button
        id="btn-check-site-compliance"
        type="submit"
        class="btn btn-secondary"
        :disabled="!canCheck || loading"
      >
        Check eTMF status
      </button>
    </form>

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
    <p
      v-else-if="loadError"
      id="site-compliance-status"
      class="status status-error"
      role="status"
    >
      The eTMF status couldn't be loaded, so activation is unavailable.
    </p>
    <div v-else-if="compliance" id="site-compliance-status" role="status">
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

    <div v-if="compliance?.is_complete" class="activate">
      <label for="site-activation-reason">Reason for activation</label>
      <textarea
        id="site-activation-reason"
        v-model="reason"
        rows="2"
        :disabled="!canActivate"
        placeholder="Why the site is being activated (recorded in the audit trail)"
      />
      <p
        v-if="!canActivate"
        id="site-activation-role-note"
        class="role-note"
        role="note"
      >
        {{ SITE_ACTIVATION_ROLE_REASON }}
      </p>
      <button
        id="btn-activate-site"
        type="button"
        class="btn btn-primary"
        :disabled="!canActivate || !reason.trim() || activating"
        :title="!canActivate ? SITE_ACTIVATION_ROLE_REASON : undefined"
        @click="activate"
      >
        {{
          activating ? "Activating..." : `Activate site ${compliance.site_id}`
        }}
      </button>
    </div>
  </section>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { ctmsApi } from "../../api/ctmsApi";
import { executionApi } from "../../api/executionApi";
import {
  SITE_ACTIVATION_ROLE_REASON,
  canActivateSite,
} from "../../canvas/authority";
import { useAuthStore } from "../../stores/auth";
import { useClinicalStore } from "../../stores/clinical";
import { useToastStore } from "../../stores/toasts.js";

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
});

const authStore = useAuthStore();
const store = useClinicalStore();
const toasts = useToastStore();

const studyCode = ref(props.studyCode || store.activeStudyId);
const siteCode = ref(store.activeSiteId);
const discoveredSites = ref([]);
const compliance = ref(null);
const checked = ref(false);
const loading = ref(false);
const loadError = ref(false);
const reason = ref("");
const activating = ref(false);

const canActivate = computed(() => canActivateSite(authStore));

const siteOptions = computed(() => {
  const merged = new Set(DEFAULT_KNOWN_SITES);
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
  }
);

const canCheck = computed(
  () => Boolean(studyCode.value.trim()) && Boolean(siteCode.value.trim())
);

async function checkStatus() {
  if (!canCheck.value) return;
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  store.activeStudyId = study;
  store.activeSiteId = site;
  checked.value = true;
  loading.value = true;
  loadError.value = false;
  compliance.value = null;
  try {
    compliance.value = await executionApi.getSiteComplianceStatus(site, study);
  } catch (err) {
    loadError.value = true;
    toasts.apiError("Couldn't load the site's eTMF status", err);
  } finally {
    loading.value = false;
  }
}

async function activate() {
  if (!canActivate.value) return;
  const current = compliance.value;
  const why = reason.value.trim();
  if (!current?.is_complete || !why || !current.site_id) return;
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
  } catch (err) {
    toasts.apiError(`Site ${current.site_id} was not activated`, err);
  } finally {
    activating.value = false;
  }
}

// Opened for a study and site (CTMS reads ?studyId= and ?siteId=): show their status straight away.
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
  background: var(--surface, #ffffff);
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
  color: #166534;
  font-weight: 600;
}

.status-incomplete {
  color: #92400e;
  font-weight: 600;
}

.status-error {
  color: #991b1b;
  font-weight: 600;
}

.role-note {
  margin: 0;
  font-size: 0.875rem;
  color: #92400e;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 4px;
  padding: 8px 12px;
}
</style>
