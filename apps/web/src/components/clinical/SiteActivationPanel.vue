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

    <p v-if="!props.studyCode" class="unavailable-note">
      Milestones, monitoring visits, delegation, financials, start-up and RTSM
      aren't available yet in this pre-release environment (Orbit and Allocate
      roadmap). See
      <component
        :is="routerLinkComponent ?? 'a'"
        v-bind="
          routerLinkComponent ? { to: '/plans' } : { href: fallbackPlansHref }
        "
        >plans &amp; product-family roadmap</component
      >.
    </p>
  </section>
</template>

<script setup>
import { computed, getCurrentInstance, onMounted, ref, watch } from "vue";
import { executionApi } from "../../api/executionApi";
import { useClinicalStore } from "../../stores/clinical";
import { useToastStore } from "../../stores/toasts.js";

/**
 * Site activation against Execution: the site's eTMF completeness for SITE_ACTIVATION, and activation once
 * it is complete. Execution refuses activation for an incomplete site and audits every attempt, with the
 * reason given here as the change reason.
 */
const props = defineProps({
  /** Fixes the study (the Clinical Canvas passes the study it shows); without it the user types the study. */
  studyCode: { type: String, default: "" },
});

const store = useClinicalStore();
const toasts = useToastStore();
const instance = getCurrentInstance();
const routerLinkComponent = instance?.appContext.components.RouterLink;
const fallbackPlansHref = `${(import.meta.env.BASE_URL || "/").replace(/\/?$/, "/")}plans`;

const studyCode = ref(props.studyCode || store.activeStudyId);
const siteCode = ref(store.activeSiteId);
const compliance = ref(null);
const checked = ref(false);
const loading = ref(false);
const loadError = ref(false);
const reason = ref("");
const activating = ref(false);

// The canvas switched study: the status shown was for the previous one.
watch(
  () => props.studyCode,
  (value) => {
    if (!value) return;
    studyCode.value = value;
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

defineExpose({ checkStatus });
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

.scope input {
  flex: 0 1 12rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
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
}

.status-incomplete,
.status-error {
  color: #b91c1c;
}

.unavailable-note {
  margin: 0;
  color: var(--text-muted, #64748b);
  font-size: 0.9rem;
}
</style>
