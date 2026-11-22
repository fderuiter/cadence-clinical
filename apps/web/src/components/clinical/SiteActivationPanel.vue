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

    <section
      v-if="
        checked && !loading && !scopeGuidance && canActivate && kernelStudyRef
      "
      id="kernel-site-readiness"
      class="kernel-readiness"
      aria-labelledby="kernel-site-readiness-title"
    >
      <h4 id="kernel-site-readiness-title" class="kernel-readiness-title">
        Kernel protocol &amp; site start-up readiness
      </h4>
      <p class="kernel-readiness-desc">
        Before activating {{ siteCode.trim() }} for data capture, link the site
        to {{ studyCode.trim() }}, record regulatory/IRB approval evidence, and
        mark an approved protocol version effective (K-TIME-4 / K-TIME-5).
      </p>

      <p
        v-if="kernelReadinessMessage"
        id="kernel-readiness-outcome"
        data-testid="kernel-readiness-outcome"
        class="status status-complete"
        role="status"
      >
        {{ kernelReadinessMessage }}
      </p>
      <p
        v-if="kernelReadinessError"
        id="kernel-readiness-error"
        data-testid="kernel-readiness-error"
        class="status status-error"
        role="alert"
      >
        {{ kernelReadinessError }}
      </p>

      <div class="kernel-step">
        <strong>1. Study site link:</strong>
        <span v-if="kernelStudySite" data-testid="kernel-study-site-status">
          Linked ({{ kernelStudySite.site_code }} ·
          {{ kernelStudySite.country }} · status: {{ kernelStudySite.status }})
        </span>
        <div v-else class="kernel-inline-controls">
          <label for="kernel-site-country">Country (ISO-2)</label>
          <input
            id="kernel-site-country"
            v-model="kernelSiteCountry"
            type="text"
            maxlength="2"
            placeholder="US"
            :disabled="kernelBusy"
          />
          <button
            id="btn-kernel-link-site"
            type="button"
            class="btn btn-secondary"
            :disabled="kernelBusy || !kernelSiteCountry.trim()"
            @click="registerAndLinkKernelSite"
          >
            Register &amp; link {{ siteCode.trim() }}
          </button>
        </div>
      </div>

      <div class="kernel-step">
        <strong>2. Regulatory / ethics evidence:</strong>
        <span v-if="kernelArtifacts.length > 0">
          {{ kernelArtifacts.length }}
          {{ kernelArtifacts.length === 1 ? "artifact" : "artifacts" }} recorded
        </span>
        <div class="kernel-inline-controls">
          <label for="kernel-artifact-kind">Evidence kind</label>
          <select
            id="kernel-artifact-kind"
            v-model="kernelArtifactKind"
            :disabled="kernelBusy"
          >
            <option value="EthicsApproval">EthicsApproval</option>
            <option value="RegulatoryApproval">RegulatoryApproval</option>
            <option value="CtaAcknowledgement">CtaAcknowledgement</option>
          </select>
          <label for="kernel-artifact-title">Document title</label>
          <input
            id="kernel-artifact-title"
            v-model="kernelArtifactTitle"
            type="text"
            placeholder="IRB approval letter"
            :disabled="kernelBusy"
          />
          <button
            id="btn-kernel-register-artifact"
            type="button"
            class="btn btn-secondary"
            :disabled="kernelBusy || !kernelArtifactTitle.trim()"
            @click="recordKernelApprovalArtifact"
          >
            Record approval evidence
          </button>
        </div>
      </div>

      <div v-if="kernelStudySite" class="kernel-step">
        <strong>3. Protocol effectivity:</strong>
        <span
          v-if="kernelStudySite.effective_versions.length > 0"
          data-testid="kernel-effective-versions"
        >
          Version
          {{
            kernelStudySite.effective_versions
              .map((v) => v.protocol_version_number)
              .join(", ")
          }}
          effective at {{ kernelStudySite.site_code }}
        </span>
        <span v-else-if="approvedProtocolVersions.length === 0">
          Approve a protocol version in Design → Protocol versions first.
        </span>
        <div v-else class="kernel-inline-controls">
          <label for="kernel-effectivity-version">Approved version</label>
          <select
            id="kernel-effectivity-version"
            v-model="selectedProtocolVersionRef"
            :disabled="kernelBusy"
          >
            <option
              v-for="ver in approvedProtocolVersions"
              :key="ver.ref"
              :value="ver.ref"
            >
              Version {{ ver.number }}
            </option>
          </select>
          <label for="kernel-effectivity-artifact">Approval evidence</label>
          <select
            id="kernel-effectivity-artifact"
            v-model="selectedArtifactVersionRef"
            :disabled="kernelBusy"
          >
            <option value="">Select recorded evidence</option>
            <option
              v-for="art in kernelArtifacts"
              :key="art.latest_version.ref"
              :value="art.latest_version.ref"
            >
              {{ art.title }} ({{ art.kind }} v{{ art.latest_version.number }})
            </option>
          </select>
          <button
            id="btn-kernel-make-effective"
            type="button"
            class="btn btn-primary"
            :disabled="
              kernelBusy ||
              !selectedProtocolVersionRef ||
              !selectedArtifactVersionRef
            "
            @click="declareAndMakeProtocolEffective"
          >
            Declare &amp; mark protocol effective
          </button>
        </div>
      </div>

      <div
        v-if="kernelStudySite && kernelStudySite.status !== 'Active'"
        class="kernel-step"
      >
        <strong>4. Kernel site activation:</strong>
        <button
          id="btn-kernel-activate-site"
          type="button"
          class="btn btn-primary"
          :disabled="
            kernelBusy || kernelStudySite.effective_versions.length === 0
          "
          @click="activateKernelSiteDirectly"
        >
          Activate {{ kernelStudySite.site_code }} in kernel
        </button>
      </div>
    </section>

    <p v-if="!props.studyCode" class="unavailable-note">
      Grant financials, CRA workload allocation, recruitment target updates, and
      adaptive dose escalation aren't available yet in this pre-release
      environment (Orbit and Allocate roadmap). See <PlansRoadmapLink />.
    </p>
  </section>
</template>

<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { ctmsApi } from "../../api/ctmsApi";
import { executionApi } from "../../api/executionApi";
import { describeKernelError, kernelApi } from "../../api/kernelApi";
import { canActivateSite } from "../../canvas/authority";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { useAuthStore } from "../../stores/auth";
import { useClinicalStore } from "../../stores/clinical";
import { useToastStore } from "../../stores/toasts.js";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../../utils/callerScope";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode.js";
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

const kernelStudyRef = ref("");
const kernelStudySites = ref([]);
const kernelArtifacts = ref([]);
const kernelProtocolVersions = ref([]);
const kernelSiteCountry = ref("US");
const kernelArtifactKind = ref("EthicsApproval");
const kernelArtifactTitle = ref("IRB / Ethics approval letter");
const selectedProtocolVersionRef = ref("");
const selectedArtifactVersionRef = ref("");
const kernelBusy = ref(false);
const kernelReadinessMessage = ref("");
const kernelReadinessError = ref("");

const kernelStudySite = computed(() => {
  const target = siteCode.value?.trim().toUpperCase();
  if (!target) return null;
  return (
    kernelStudySites.value.find((s) => s.site_code?.toUpperCase() === target) ||
    null
  );
});

const approvedProtocolVersions = computed(() =>
  kernelProtocolVersions.value.filter(
    (v) => v.ref && v.status === "Approved" && !v.mask
  )
);

let unmounted = false;

function makeKernelIdempotencyKey() {
  if (isDemoBuild()) {
    return `site-kernel-${demoRandomToken(12, 36, "site-kernel.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `site-kernel-${demoRandomToken(12, 36, "site-kernel.idempotency")}`;
}

async function loadKernelReadiness(study, site) {
  kernelStudyRef.value = "";
  kernelStudySites.value = [];
  kernelArtifacts.value = [];
  kernelProtocolVersions.value = [];
  if (unmounted || !canActivate.value || !study || !site) return;
  if (typeof kernelApi.listStudySites !== "function") return;

  try {
    const resolvedStudy = await findKernelStudyRef(study);
    if (unmounted || !resolvedStudy) return;
    kernelStudyRef.value = resolvedStudy;

    const [sites, artifacts, versions] = await Promise.all([
      kernelApi.listStudySites(resolvedStudy),
      typeof kernelApi.listStudyArtifacts === "function"
        ? kernelApi.listStudyArtifacts(resolvedStudy)
        : Promise.resolve([]),
      typeof kernelApi.listProtocolVersions === "function"
        ? kernelApi.listProtocolVersions(resolvedStudy)
        : Promise.resolve([]),
    ]);
    if (unmounted) return;
    kernelStudySites.value = Array.isArray(sites) ? sites : [];
    kernelArtifacts.value = Array.isArray(artifacts) ? artifacts : [];
    kernelProtocolVersions.value = Array.isArray(versions) ? versions : [];

    if (
      !selectedProtocolVersionRef.value &&
      approvedProtocolVersions.value.length > 0
    ) {
      selectedProtocolVersionRef.value = approvedProtocolVersions.value[0].ref;
    }
    if (!selectedArtifactVersionRef.value && kernelArtifacts.value.length > 0) {
      selectedArtifactVersionRef.value =
        kernelArtifacts.value[0].latest_version?.ref || "";
    }
  } catch {
    // Optional kernel readiness lookup never blocks eTMF status display.
  }
}

async function registerAndLinkKernelSite() {
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  const country = kernelSiteCountry.value.trim().toUpperCase();
  if (!kernelStudyRef.value || !site || !country || kernelBusy.value) return;
  kernelBusy.value = true;
  kernelReadinessError.value = "";
  kernelReadinessMessage.value = "";
  try {
    const registered = await kernelApi.registerSite({
      siteCode: site,
      name: `${site} Clinical Research Center`,
      country,
      institutionCode: `INST-${site}`,
      institutionName: `${site} Medical Institution`,
      reason: `Register clinical site ${site} for ${study}`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await kernelApi.linkSiteToStudy({
      studyRef: kernelStudyRef.value,
      siteRef: registered.ref,
      reason: `Link clinical site ${site} to ${study}`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await loadKernelReadiness(study, site);
    kernelReadinessMessage.value = `Registered and linked ${site} to ${study}.`;
  } catch (err) {
    kernelReadinessError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

async function recordKernelApprovalArtifact() {
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  const title = kernelArtifactTitle.value.trim();
  if (!kernelStudyRef.value || !title || kernelBusy.value) return;
  kernelBusy.value = true;
  kernelReadinessError.value = "";
  kernelReadinessMessage.value = "";
  try {
    const created = await kernelApi.registerArtifact({
      kind: kernelArtifactKind.value,
      title,
      studyRef: kernelStudyRef.value,
      documentText: `${kernelArtifactKind.value} for ${study} / ${site}: ${title}`,
      mediaType: "text/plain",
      reason: `Record ${kernelArtifactKind.value} evidence for ${site}`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await loadKernelReadiness(study, site);
    selectedArtifactVersionRef.value = created.latest_version?.ref || "";
    kernelReadinessMessage.value = `Recorded ${created.kind} evidence (${created.title}).`;
  } catch (err) {
    kernelReadinessError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

async function declareAndMakeProtocolEffective() {
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  if (
    !kernelStudySite.value?.ref ||
    !selectedProtocolVersionRef.value ||
    !selectedArtifactVersionRef.value ||
    kernelBusy.value
  ) {
    return;
  }
  kernelBusy.value = true;
  kernelReadinessError.value = "";
  kernelReadinessMessage.value = "";
  try {
    const declared = await kernelApi.declareEffectivity({
      protocolVersionRef: selectedProtocolVersionRef.value,
      scopeLevel: "Site",
      studySiteRef: kernelStudySite.value.ref,
      reason: `Declare protocol effective scope at ${site}`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await kernelApi.changeEffectivityStatus({
      effectivityRef: declared.ref,
      status: "Effective",
      evidenceVersionRefs: [selectedArtifactVersionRef.value],
      reason: `Mark protocol version effective at ${site} on regulatory evidence`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await loadKernelReadiness(study, site);
    kernelReadinessMessage.value = `Marked Version ${declared.protocol_version_number} effective at ${site}.`;
  } catch (err) {
    kernelReadinessError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

async function activateKernelSiteDirectly() {
  const study = studyCode.value.trim();
  const site = siteCode.value.trim();
  if (!kernelStudySite.value?.ref || kernelBusy.value) return;
  kernelBusy.value = true;
  kernelReadinessError.value = "";
  kernelReadinessMessage.value = "";
  try {
    await kernelApi.activateStudySite({
      studySiteRef: kernelStudySite.value.ref,
      reason:
        reason.value.trim() || `Activate clinical site ${site} in ${study}`,
      idempotencyKey: makeKernelIdempotencyKey(),
    });
    await loadKernelReadiness(study, site);
    kernelReadinessMessage.value = `Activated ${site} in the kernel for ${study}.`;
  } catch (err) {
    kernelReadinessError.value = describeKernelError(err);
  } finally {
    kernelBusy.value = false;
  }
}

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
  let shouldLoadKernelReadiness = false;
  try {
    compliance.value = await executionApi.getSiteComplianceStatus(site, study);
    shouldLoadKernelReadiness = true;
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
  if (shouldLoadKernelReadiness) {
    void loadKernelReadiness(study, site);
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

onUnmounted(() => {
  unmounted = true;
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

.kernel-readiness {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
}

.kernel-readiness-title {
  margin: 0;
  font-size: 0.95rem;
}

.kernel-readiness-desc {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.kernel-step {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding-top: 8px;
  border-top: 1px solid var(--border);
  font-size: 0.88rem;
}

.kernel-inline-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.kernel-inline-controls input,
.kernel-inline-controls select {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
}
</style>
