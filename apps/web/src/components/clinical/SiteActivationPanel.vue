<template>
  <section
    id="site-activation-panel"
    class="card site-activation"
    aria-labelledby="site-activation-title"
  >
    <h3 id="site-activation-title" class="card-title" tabindex="-1">
      Site activation
    </h3>

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
        <option
          v-for="option in siteOptions"
          :key="option.code"
          :value="option.code"
        >
          {{ option.label }}
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
      <button
        v-if="canQuickCreateSite"
        id="btn-site-activation-new-site"
        type="button"
        class="btn btn-secondary"
        @click="toggleCreateSiteShortcut"
      >
        {{ showCreateSiteForm ? "Cancel new site" : "+ Register & link site" }}
      </button>
    </form>

    <form
      v-if="showCreateSiteForm"
      id="site-activation-create-site-form"
      class="quick-site-form"
      aria-label="Register and link a site"
      @submit.prevent="submitQuickSite"
    >
      <p v-if="quickSiteError" class="status status-error" role="alert">
        {{ quickSiteError }}
      </p>
      <div class="quick-site-grid">
        <div class="quick-field">
          <label for="site-activation-new-code">Site code</label>
          <input
            id="site-activation-new-code"
            v-model="newSiteCode"
            type="text"
            maxlength="64"
            autocomplete="off"
            placeholder="e.g. SITE-01"
            :disabled="creatingQuickSite"
            required
          />
        </div>
        <div class="quick-field">
          <label for="site-activation-new-country">Country (ISO-2)</label>
          <input
            id="site-activation-new-country"
            v-model="newSiteCountry"
            type="text"
            maxlength="2"
            autocomplete="off"
            placeholder="US"
            :disabled="creatingQuickSite"
            required
          />
        </div>
        <div class="quick-field">
          <label for="site-activation-new-name">Site name</label>
          <input
            id="site-activation-new-name"
            v-model="newSiteName"
            type="text"
            maxlength="200"
            autocomplete="off"
            placeholder="e.g. Metro Cardiology Research Center"
            :disabled="creatingQuickSite"
            required
          />
        </div>
        <div class="quick-field">
          <label for="site-activation-new-institution">Site institution</label>
          <input
            id="site-activation-new-institution"
            v-model="newSiteInstitution"
            type="text"
            maxlength="200"
            autocomplete="off"
            placeholder="e.g. Metro University Hospital"
            :disabled="creatingQuickSite"
            required
          />
        </div>
        <div class="quick-field quick-field-wide">
          <label for="site-activation-new-reason">
            Reason for change (required)
          </label>
          <input
            id="site-activation-new-reason"
            v-model="newSiteReason"
            type="text"
            maxlength="500"
            autocomplete="off"
            placeholder="e.g. Register clinical site for study startup"
            :disabled="creatingQuickSite"
            required
          />
        </div>
      </div>
      <div class="quick-site-actions">
        <button
          id="btn-confirm-site-activation-new-site"
          type="submit"
          class="btn btn-primary"
          :disabled="!canSubmitQuickSite"
        >
          {{
            creatingQuickSite
              ? "Registering…"
              : studyCode.trim()
                ? `Register & link to ${studyCode.trim()}`
                : "Register site"
          }}
        </button>
      </div>
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

    <div v-if="compliance && !compliance.is_activated" class="activate">
      <template v-if="isActivationAllowed">
        <label for="site-activation-reason">Reason for activation</label>
        <textarea
          id="site-activation-reason"
          v-model="reason"
          rows="2"
          placeholder="Why the site is being activated (recorded in the audit trail)"
        />
      </template>
      <p
        v-if="!canActivate"
        id="site-activation-readonly-note"
        class="readonly-note"
      >
        Read-only view: site activation requires Sponsor Data Management or
        System Administrator authorization (site_compliance:activate).
      </p>
      <div class="activate-actions">
        <button
          id="btn-activate-site"
          type="button"
          class="btn btn-primary"
          :disabled="!isActivationAllowed || !reason.trim() || activating"
          @click="activate"
        >
          {{
            activating ? "Activating..." : `Activate site ${compliance.site_id}`
          }}
        </button>
        <button
          v-if="
            !isActivationAllowed || Boolean(canI.decision.value?.explanation)
          "
          id="btn-explain-site-activation"
          type="button"
          class="btn btn-secondary"
          :aria-expanded="showExplanation ? 'true' : 'false'"
          aria-controls="site-activation-explanation"
          @click="showExplanation = !showExplanation"
        >
          {{ showExplanation ? "Hide explanation" : "Explain" }}
        </button>
      </div>
      <div
        v-if="showExplanation"
        id="site-activation-explanation"
        class="activation-explanation"
        role="region"
        aria-label="Site activation eligibility explanation"
      >
        <ul
          v-if="unmetConditions.length"
          id="site-activation-unmet-conditions"
          class="activation-conditions"
        >
          <li v-for="condition in unmetConditions" :key="condition">
            {{ condition }}
          </li>
        </ul>
        <KernelExplanation
          v-if="canI.decision.value?.explanation"
          :explanation="canI.decision.value.explanation"
        />
      </div>
    </div>

    <div
      v-if="kernelStudyRef && siteCode.trim()"
      id="kernel-site-governance"
      class="kernel-governance"
      aria-labelledby="kernel-site-governance-title"
    >
      <h4 id="kernel-site-governance-title" class="kernel-governance-title">
        Clinical Kernel study-site &amp; protocol effectivity
      </h4>

      <div
        v-if="kernelSiteFeedback"
        id="kernel-site-feedback"
        class="kernel-banner kernel-banner-success"
        role="status"
      >
        {{ kernelSiteFeedback }}
      </div>
      <div
        v-if="kernelSiteError"
        id="kernel-site-error"
        class="kernel-banner kernel-banner-error"
        role="alert"
      >
        {{ kernelSiteError }}
      </div>

      <template v-if="matchedKernelSite">
        <div
          v-if="matchedKernelSite.status === 'Active'"
          id="kernel-site-active-banner"
          class="activated-banner"
        >
          <span class="activated-badge">Kernel Active</span>
          <span>
            Study site {{ matchedKernelSite.site_code || siteCode.trim() }} is
            active in the Clinical Kernel
            <template v-if="matchedKernelSite.effective_versions?.length">
              (governing protocol:
              {{
                matchedKernelSite.effective_versions
                  .map((v) => `v${v.number} (${v.scope_level})`)
                  .join(", ")
              }})
            </template>
            .
          </span>
        </div>
        <p v-else id="kernel-site-status" class="status">
          Kernel status: <strong>{{ matchedKernelSite.status }}</strong>
          <template v-if="matchedKernelSite.effective_versions?.length">
            · Effective protocol:
            {{
              matchedKernelSite.effective_versions
                .map((v) => `Version ${v.number} (${v.scope_level})`)
                .join(", ")
            }}
          </template>
          <template v-else>
            · No protocol version is Effective for this site yet (K-TIME-4).
          </template>
        </p>

        <div
          v-if="matchedKernelSite.status !== 'Active' && canManageKernelSites"
          class="activate"
        >
          <label for="kernel-site-activate-reason">
            Reason for kernel site activation
          </label>
          <input
            id="kernel-site-activate-reason"
            v-model="kernelSiteReason"
            type="text"
            class="kernel-input"
            maxlength="500"
            placeholder="e.g. Activate study site in kernel with effective protocol v1"
            :disabled="kernelBusy"
          />
          <button
            id="btn-activate-kernel-site"
            type="button"
            class="btn btn-primary"
            :disabled="
              kernelBusy ||
              !kernelSiteReason.trim() ||
              !matchedKernelSite.effective_versions?.length
            "
            @click="activateKernelSite"
          >
            {{
              kernelBusy
                ? "Activating kernel site…"
                : `Activate kernel site ${matchedKernelSite.site_code || siteCode.trim()}`
            }}
          </button>
        </div>
      </template>

      <template v-else>
        <p id="kernel-site-status" class="status">
          Site <strong>{{ siteCode.trim() }}</strong> is not linked to
          <strong>{{ studyCode.trim() }}</strong> in the Clinical Kernel yet.
        </p>

        <div v-if="canManageKernelSites" class="kernel-link-controls">
          <div class="kernel-link-grid">
            <div class="kernel-field">
              <label for="kernel-site-name">Site name</label>
              <input
                id="kernel-site-name"
                v-model="kernelSiteName"
                type="text"
                class="kernel-input"
                placeholder="e.g. General Clinical Research Center"
                :disabled="kernelBusy"
              />
            </div>
            <div class="kernel-field">
              <label for="kernel-site-country">Country (ISO alpha-2)</label>
              <input
                id="kernel-site-country"
                v-model="kernelSiteCountry"
                type="text"
                class="kernel-input"
                maxlength="2"
                placeholder="US"
                :disabled="kernelBusy"
              />
            </div>
            <div class="kernel-field">
              <label for="kernel-site-link-reason">Reason for linking</label>
              <input
                id="kernel-site-link-reason"
                v-model="kernelSiteReason"
                type="text"
                class="kernel-input"
                maxlength="500"
                placeholder="e.g. Link clinical site for startup and enrollment"
                :disabled="kernelBusy"
              />
            </div>
          </div>
          <button
            id="btn-link-kernel-site"
            type="button"
            class="btn btn-secondary"
            :disabled="kernelBusy || !kernelSiteReason.trim()"
            @click="linkSiteInKernel"
          >
            {{
              kernelBusy
                ? "Linking…"
                : `Link ${siteCode.trim()} to study in kernel`
            }}
          </button>
        </div>
      </template>
    </div>

    <p v-if="!props.studyCode" class="unavailable-note">
      Grant financials, CRA workload allocation, recruitment target updates, and
      adaptive dose escalation aren't available yet in this pre-release
      environment (Orbit and Allocate roadmap). See <PlansRoadmapLink />.
    </p>
  </section>
</template>

<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { handoffFocus } from "ui";
import { ctmsApi } from "../../api/ctmsApi";
import { executionApi } from "../../api/executionApi";
import { describeKernelError, kernelApi } from "../../api/kernelApi";
import { kernelGovernanceApi } from "../../api/kernelGovernanceApi";
import {
  SITE_ACTIVATION_ROLE_REASON,
  canActivateSite,
  canManageKernelStudySites,
} from "../../canvas/authority";
import {
  findKernelStudyRef,
  findStudySites,
  invalidateStudySitesCache,
} from "../../canvas/kernelStudy";
import { useKernelCanI } from "../../composables/useKernelCanI";
import { useAuthStore } from "../../stores/auth";
import { useClinicalStore } from "../../stores/clinical";
import { syncActiveStudyScope } from "../../stores/studyScopeBridge";
import { useToastStore } from "../../stores/toasts.js";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../../utils/callerScope";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode";
import KernelExplanation from "../canvas/KernelExplanation.vue";
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
const assignedSites = computed(() =>
  (authStore.assignedSites || []).filter((site) => site && site !== "*")
);
const canActivate = computed(() => canActivateSite(authStore));
const canManageKernelSites = computed(() =>
  canManageKernelStudySites(authStore)
);

const studyCode = ref(
  props.studyCode ||
    store.activeStudyId ||
    (assignedStudies.value.length ? assignedStudies.value[0] : "")
);
const reusableActiveSiteId =
  props.studyCode &&
  store.activeStudyId &&
  store.activeStudyId !== props.studyCode
    ? ""
    : store.activeSiteId;
const siteCode = ref(
  props.siteId ||
    reusableActiveSiteId ||
    (assignedSites.value.length ? assignedSites.value[0] : "")
);
const discoveredSites = ref([]);
const kernelLinkedSites = ref([]);
const compliance = ref(null);
const checked = ref(false);
const loading = ref(false);
const loadError = ref(false);
const scopeGuidance = ref("");
const reason = ref("");
const activating = ref(false);
const showExplanation = ref(false);
const canI = useKernelCanI();

const isKernelPermitted = computed(() =>
  canI.decision.value ? Boolean(canI.decision.value.permitted) : true
);

const isActivationAllowed = computed(() =>
  Boolean(
    compliance.value?.is_complete &&
    !compliance.value?.is_activated &&
    canActivate.value &&
    isKernelPermitted.value
  )
);

const unmetConditions = computed(() => {
  const items = [];
  if (!compliance.value || compliance.value.is_activated) return items;
  if (!canActivate.value) {
    items.push(SITE_ACTIVATION_ROLE_REASON);
  }
  if (!compliance.value.is_complete) {
    if (compliance.value.missing_documents?.length) {
      items.push(
        `Missing required activation documents: ${compliance.value.missing_documents.join(", ")}.`
      );
    } else {
      items.push(
        "eTMF has not confirmed activation document completeness for this site."
      );
    }
  }
  if (canI.decision.value && !canI.decision.value.permitted) {
    const kernelReason = canI.decision.value.reason?.trim();
    if (kernelReason) {
      items.push(kernelReason);
    }
  }
  return items;
});

const showCreateSiteForm = ref(false);
const creatingQuickSite = ref(false);
const quickSiteError = ref("");
const newSiteCode = ref("");
const newSiteName = ref("");
const newSiteCountry = ref("US");
const newSiteInstitution = ref("");
const newSiteReason = ref("");

const canQuickCreateSite = computed(
  () =>
    typeof kernelApi.createSite === "function" &&
    typeof kernelApi.linkSiteToStudy === "function"
);

const canSubmitQuickSite = computed(
  () =>
    !creatingQuickSite.value &&
    Boolean(newSiteCode.value.trim()) &&
    Boolean(newSiteName.value.trim()) &&
    newSiteCountry.value.trim().length === 2 &&
    Boolean(newSiteInstitution.value.trim()) &&
    Boolean(newSiteReason.value.trim())
);

const kernelStudyRef = ref("");
const kernelSites = ref([]);
const kernelBusy = ref(false);
const kernelSiteReason = ref("");
const kernelSiteName = ref("");
const kernelSiteCountry = ref("US");
const kernelSiteFeedback = ref("");
const kernelSiteError = ref("");

const matchedKernelSite = computed(() => {
  const target = siteCode.value?.trim().toLowerCase();
  if (!target) return null;
  return (
    kernelSites.value.find(
      (s) => (s.site_code || "").trim().toLowerCase() === target
    ) || null
  );
});

function nextKernelIdempotencyKey() {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `site-gov-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

async function refreshKernelSites(study) {
  const code = (study ?? studyCode.value)?.trim();
  if (!code || typeof kernelGovernanceApi?.listStudySites !== "function") {
    kernelStudyRef.value = "";
    kernelSites.value = [];
    return;
  }
  try {
    const resolvedRef = await findKernelStudyRef(code);
    if (!resolvedRef) {
      kernelStudyRef.value = "";
      kernelSites.value = [];
      return;
    }
    kernelStudyRef.value = resolvedRef;
    const listed = await kernelGovernanceApi.listStudySites(resolvedRef);
    kernelSites.value = Array.isArray(listed) ? listed : [];
    for (const s of kernelSites.value) {
      if (s.site_code && !discoveredSites.value.includes(s.site_code)) {
        discoveredSites.value = [...discoveredSites.value, s.site_code];
      }
    }
  } catch {
    // Optional kernel study-site discovery should never block Execution eTMF checks.
  }
}

async function linkSiteInKernel() {
  const site = siteCode.value?.trim();
  const why = kernelSiteReason.value?.trim();
  if (
    !kernelStudyRef.value ||
    !site ||
    !why ||
    kernelBusy.value ||
    typeof kernelGovernanceApi?.linkStudySite !== "function"
  ) {
    return;
  }
  kernelBusy.value = true;
  kernelSiteError.value = "";
  kernelSiteFeedback.value = "";
  try {
    const linked = await kernelGovernanceApi.linkStudySite({
      studyRef: kernelStudyRef.value,
      siteCode: site,
      siteName: kernelSiteName.value.trim() || site,
      country: kernelSiteCountry.value.trim().toUpperCase() || "US",
      reason: why,
      idempotencyKey: nextKernelIdempotencyKey(),
    });
    kernelSiteReason.value = "";
    invalidateStudySitesCache(studyCode.value);
    await refreshKernelSites(studyCode.value);
    void loadKernelLinkedSites();
    kernelSiteFeedback.value = `Linked site ${linked.site_code || site} to study (${linked.status}).`;
  } catch (err) {
    kernelSiteError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

async function activateKernelSite() {
  const target = matchedKernelSite.value;
  const why = kernelSiteReason.value?.trim();
  if (
    !target?.study_site_ref ||
    !why ||
    kernelBusy.value ||
    typeof kernelGovernanceApi?.activateStudySite !== "function"
  ) {
    return;
  }
  kernelBusy.value = true;
  kernelSiteError.value = "";
  kernelSiteFeedback.value = "";
  try {
    const activated = await kernelGovernanceApi.activateStudySite({
      studySiteRef: target.study_site_ref,
      reason: why,
      idempotencyKey: nextKernelIdempotencyKey(),
    });
    kernelSiteReason.value = "";
    await refreshKernelSites(studyCode.value);
    kernelSiteFeedback.value = `Activated kernel study site ${activated.site_code || siteCode.value.trim()}.`;
  } catch (err) {
    kernelSiteError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

const siteOptions = computed(() => {
  const byCode = new Map();
  for (const item of kernelLinkedSites.value) {
    if (item?.code) {
      byCode.set(item.code, item.label || item.code);
    }
  }
  for (const s of DEFAULT_KNOWN_SITES) {
    if (s && !byCode.has(s)) byCode.set(s, s);
  }
  for (const s of assignedSites.value) {
    if (s && !byCode.has(s)) byCode.set(s, s);
  }
  for (const s of discoveredSites.value) {
    if (s && !byCode.has(s)) byCode.set(s, s);
  }
  if (siteCode.value?.trim()) {
    const trimmed = siteCode.value.trim();
    if (!byCode.has(trimmed)) byCode.set(trimmed, trimmed);
  }
  return [...byCode.entries()]
    .map(([code, label]) => ({ code, label }))
    .sort((a, b) => a.code.localeCompare(b.code));
});

const pickerValue = computed(() => {
  const trimmed = siteCode.value?.trim() || "";
  return siteOptions.value.some((opt) => opt.code === trimmed) ? trimmed : "";
});

function onPickSite(event) {
  const chosen = event.target?.value || "";
  if (chosen) {
    siteCode.value = chosen;
    store.activeSiteId = chosen;
  }
}

function nextQuickSiteKey(prefix) {
  if (isDemoBuild()) {
    return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `${prefix}-${demoRandomToken(12, 36, `${prefix}.idempotency`)}`;
}

function toggleCreateSiteShortcut() {
  const opening = !showCreateSiteForm.value;
  showCreateSiteForm.value = opening;
  quickSiteError.value = "";
  void handoffFocus(
    opening ? "#site-activation-new-code" : "#btn-site-activation-new-site",
    { scrollIfOffscreen: opening }
  );
}

async function loadKernelLinkedSites() {
  const study = studyCode.value?.trim();
  if (!study) return;
  try {
    kernelLinkedSites.value = await findStudySites(study);
  } catch {
    kernelLinkedSites.value = [];
  }
}

async function loadStudySites() {
  const study = studyCode.value?.trim();
  if (!study) return;
  const [linkedResult, milestoneResult] = await Promise.allSettled([
    findStudySites(study),
    ctmsApi.listSiteMilestones({ study_id: study }),
  ]);
  if (linkedResult.status === "fulfilled") {
    kernelLinkedSites.value = linkedResult.value;
  }
  if (
    milestoneResult.status === "fulfilled" &&
    Array.isArray(milestoneResult.value)
  ) {
    discoveredSites.value = milestoneResult.value
      .map((m) => m.site_id)
      .filter((s) => Boolean(s));
  }
}

async function submitQuickSite() {
  if (!canSubmitQuickSite.value) return;
  quickSiteError.value = "";
  creatingQuickSite.value = true;
  try {
    const created = await kernelApi.createSite({
      code: newSiteCode.value.trim(),
      name: newSiteName.value.trim(),
      country: newSiteCountry.value.trim().toUpperCase(),
      institutionName: newSiteInstitution.value.trim(),
      reason: newSiteReason.value.trim(),
      idempotencyKey: nextQuickSiteKey("site-activation-create"),
    });

    const study = studyCode.value.trim();
    if (study) {
      await kernelApi.linkSiteToStudy({
        studyRef: study,
        siteRef: created.ref,
        siteCode: created.code,
        reason: newSiteReason.value.trim(),
        idempotencyKey: nextQuickSiteKey("site-activation-link"),
      });
      invalidateStudySitesCache(study);
      void refreshKernelSites(study);
    }

    const label =
      created.name && created.name !== created.code
        ? `${created.code} — ${created.name}`
        : created.code;
    const merged = new Map(kernelLinkedSites.value.map((s) => [s.code, s]));
    merged.set(created.code, { code: created.code, label });
    kernelLinkedSites.value = [...merged.values()].sort((a, b) =>
      a.code.localeCompare(b.code)
    );

    siteCode.value = created.code;
    showCreateSiteForm.value = false;
    newSiteCode.value = "";
    newSiteName.value = "";
    newSiteInstitution.value = "";
    newSiteReason.value = "";
    void handoffFocus([
      "#site-activation-site-picker",
      "#site-activation-site",
    ]);
    toasts.push({
      tone: "success",
      title: `Site ${created.code} registered`,
      message: study
        ? `Linked ${created.code} to ${study}.`
        : `Registered ${created.code} in your workspace.`,
    });
  } catch (err) {
    quickSiteError.value = describeKernelError(err);
  } finally {
    creatingQuickSite.value = false;
  }
}

// The canvas switched study: the status shown was for the previous one.
watch(
  () => props.studyCode,
  (value) => {
    if (!value) return;
    studyCode.value = value;
    discoveredSites.value = [];
    kernelLinkedSites.value = [];
    compliance.value = null;
    checked.value = false;
    loadError.value = false;
    scopeGuidance.value = "";
    showExplanation.value = false;
    canI.reset();
    kernelStudyRef.value = "";
    kernelSites.value = [];
    kernelSiteFeedback.value = "";
    kernelSiteError.value = "";
    loadKernelLinkedSites();
    void refreshKernelSites(value);
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
  if (!props.studyCode) {
    store.activeStudyId = study;
  }
  if (canCheck.value) checkStatus();
}

function selectAssignedSite(site) {
  siteCode.value = site;
  store.activeSiteId = site;
  if (canCheck.value) checkStatus();
}

async function checkStatus() {
  if (!canCheck.value) return;
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  store.activeStudyId = study;
  store.activeSiteId = site;
  syncActiveStudyScope(study);
  checked.value = true;
  loading.value = true;
  loadError.value = false;
  scopeGuidance.value = "";
  compliance.value = null;
  showExplanation.value = false;
  void canI.probeStudySiteActivate(study, site);
  try {
    compliance.value = await executionApi.getSiteComplianceStatus(site, study);
    void refreshKernelSites(study);
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
  if (!isActivationAllowed.value) return;
  const current = compliance.value;
  const why = reason.value.trim();
  if (
    !isActivationAllowed.value ||
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
  if (props.studyCode) {
    loadKernelLinkedSites();
  }
  if (studyCode.value.trim()) {
    void refreshKernelSites(studyCode.value);
  }
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

.activate-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-xs);
}

.activation-explanation {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  padding: var(--spacing-sm);
  border-radius: var(--radius-sm);
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  font-size: var(--font-size-sm);
}

.activation-conditions {
  margin: 0;
  padding-left: var(--spacing-lg);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-2xs);
}

.status {
  margin: 0;
}

.status-complete {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}

.status-incomplete {
  color: var(--semantic-color-warning-dark);
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
  color: var(--semantic-color-warning-dark);
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
  color: var(--semantic-color-warning-dark);
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 4px;
  padding: 8px 12px;
}

.quick-site-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.quick-site-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
}

.quick-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.quick-field-wide {
  grid-column: 1 / -1;
}

.quick-field label {
  font-weight: 600;
  font-size: 0.84rem;
}

.quick-field input {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
}

.quick-site-actions {
  display: flex;
  gap: 8px;
}

.kernel-governance {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}

.kernel-governance-title {
  margin: 0;
  font-size: 0.95rem;
}

.kernel-link-controls {
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: flex-start;
}

.kernel-link-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 8px;
  width: 100%;
}

.kernel-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.85rem;
}

.kernel-field label {
  font-weight: 600;
}

.kernel-input {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
}

.kernel-banner {
  padding: 8px 10px;
  border-radius: 6px;
  font-size: 0.88rem;
}

.kernel-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.kernel-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error);
}
</style>
