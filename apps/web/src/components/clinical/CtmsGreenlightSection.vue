<template>
  <section
    id="ctms-greenlight-section"
    class="card ctms-section"
    aria-labelledby="ctms-greenlight-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-greenlight-title" class="card-title" tabindex="-1">
          Startup readiness
        </h3>
        <p class="section-subtitle">
          Six-facet country and site greenlight checklist and Part 11 readiness
          certification.
        </p>
      </div>
    </div>

    <CtmsSectionScopeBar
      v-model:study="studyInput"
      v-model:site="siteInput"
      prefix="greenlight"
      form-id="greenlight-scope-form"
      aria-label="Startup readiness scope"
      :show-study-input="!isStudyLocked"
      site-label="Site (optional for matrix)"
      :assigned-studies="assignedStudies"
      :assigned-sites="assignedSites"
      submit-id="btn-load-greenlight"
      :submit-label="loading ? 'Evaluating...' : 'Evaluate readiness'"
      :submit-disabled="!studyInput.trim() || loading"
      :scope-guidance="scopeGuidance"
      :load-error="loadError"
      :idle-message="
        !loaded && !loading
          ? 'Select a study and country (and optionally a site) to evaluate startup greenlight gates.'
          : ''
      "
      @submit="loadReadiness"
    >
      <template #before-site>
        <ClinicalInput
          id="greenlight-country"
          v-model="countryCode"
          as="select"
          label="Country"
          density="compact"
          :show-query-flag="false"
        >
          <option value="US">United States (US)</option>
          <option value="DE">Germany (DE)</option>
          <option value="GB">United Kingdom (GB)</option>
          <option value="JP">Japan (JP)</option>
          <option value="EU">European Union (EU)</option>
          <option value="GLOBAL">Global (GLOBAL)</option>
        </ClinicalInput>
      </template>
    </CtmsSectionScopeBar>

    <div
      v-if="singleGate"
      id="greenlight-site-card"
      class="gate-card"
      role="region"
      :aria-label="`Site ${singleGate.site_id} readiness`"
    >
      <div class="gate-header">
        <div>
          <strong>Site {{ singleGate.site_id }}</strong>
          <span class="meta-sep">·</span>
          <span>{{ singleGate.country_code }}</span>
        </div>
        <span
          class="gate-badge"
          :class="`gate-${singleGate.overall_status.toLowerCase()}`"
        >
          {{ formatClinicalStatus(singleGate.overall_status) }}
        </span>
      </div>

      <ul class="facet-list">
        <li
          v-for="facet in getGateFacets(singleGate)"
          :key="facet.facet_code"
          class="facet-row"
        >
          <span class="facet-name">{{ facet.display_name }}</span>
          <span class="facet-detail">{{ facet.evidence_summary }}</span>
          <span
            class="facet-status"
            :class="facet.is_satisfied ? 'facet-ok' : 'facet-blocked'"
          >
            {{ facet.is_satisfied ? "Satisfied" : "Blocking" }}
          </span>
        </li>
      </ul>

      <p
        v-if="singleGate.certified_at || singleGate.greenlight_certified_at"
        class="certified-meta"
      >
        Certified by
        {{ singleGate.certified_by || singleGate.greenlight_certified_by }} on
        {{
          formatUtcDate(
            singleGate.certified_at || singleGate.greenlight_certified_at
          )
        }}
      </p>

      <div
        v-if="
          isGateReady(singleGate) &&
          !isGateCertified(singleGate.overall_status) &&
          canCertify
        "
        class="certify-box"
      >
        <ClinicalInput
          id="greenlight-certify-reason"
          v-model="certifyReason"
          label="Part 11 certification rationale"
          type="text"
          placeholder="State clinical justification for certifying site greenlight"
          density="compact"
          :show-query-flag="false"
          extra-class="certify-input-field"
        />
        <label for="greenlight-certify-password">Password (Part 11)</label>
        <input
          id="greenlight-certify-password"
          v-model="certifyPassword"
          v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
          placeholder="Re-enter password to sign"
        />
        <button
          id="btn-certify-site-greenlight"
          type="button"
          class="btn btn-primary"
          :disabled="
            !certifyReason.trim() || !certifyPassword.trim() || certifying
          "
          @click="certifySingleSite"
        >
          {{ certifying ? "Certifying..." : `Certify ${singleGate.site_id}` }}
        </button>
      </div>
      <p
        v-else-if="
          isGateReady(singleGate) &&
          !isGateCertified(singleGate.overall_status) &&
          !canCertify
        "
        id="greenlight-readonly-note"
        class="readonly-note"
      >
        Read-only view: startup greenlight certification requires Sponsor Data
        Management or System Administrator authorization.
      </p>
    </div>

    <div v-if="loaded" class="matrix-block">
      <h4 id="greenlight-matrix-heading" class="subsection-title">
        Country readiness matrix ({{ countryCode }})
      </h4>
      <p v-if="!countryMatrix.length" class="muted-status">
        No sites with milestones, monitoring visits, or delegation records were
        found in your assigned scope for {{ studyInput.trim() }} ({{
          countryCode
        }}).
      </p>
      <template v-else>
        <ClinicalDataTable
          hide-header
          plain
          labelled-by="greenlight-matrix-heading"
          wrapper-class="table-wrap"
          table-class="data-table"
          :columns="matrixColumns"
          :data="countryMatrix"
          row-key="site_id"
        >
          <template #cell-select="{ row }">
            <input
              v-model="selectedMatrixSites"
              type="checkbox"
              :value="row.site_id"
              :disabled="
                !isGateReady(row) || isGateCertified(row.overall_status)
              "
              :aria-label="`Select ${row.site_id} for batch certification`"
            />
          </template>
          <template #cell-site_id="{ row }">
            <strong>{{ row.site_id }}</strong>
          </template>
          <template #cell-overall_status="{ row }">
            <span
              class="gate-badge"
              :class="`gate-${row.overall_status.toLowerCase()}`"
            >
              {{ formatClinicalStatus(row.overall_status) }}
            </span>
          </template>
          <template #cell-facets="{ row }">
            {{ getGateFacets(row).filter((f) => f.is_satisfied).length }} /
            {{ getGateFacets(row).length }}
          </template>
          <template #cell-reasons="{ row }">
            <span v-if="getBlockingReasons(row).length">
              {{ getBlockingReasons(row).join("; ") }}
            </span>
            <span v-else class="text-ok">None</span>
          </template>
          <template #cell-certified="{ row }">
            <span v-if="row.certified_by || row.greenlight_certified_by">
              {{ row.certified_by || row.greenlight_certified_by }}
            </span>
            <span v-else class="muted-inline">—</span>
          </template>
        </ClinicalDataTable>

        <div
          v-if="canCertify && selectedMatrixSites.length"
          class="certify-box"
        >
          <ClinicalInput
            id="batch-certify-reason"
            v-model="batchReason"
            :label="`Batch certification rationale (${selectedMatrixSites.length} selected)`"
            type="text"
            placeholder="Part 11 justification for batch country certification"
            density="compact"
            :show-query-flag="false"
            extra-class="certify-input-field"
          />
          <label for="batch-certify-password">Password (Part 11)</label>
          <input
            id="batch-certify-password"
            v-model="batchPassword"
            v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
            placeholder="Re-enter password to sign"
          />
          <button
            id="btn-batch-certify-greenlight"
            type="button"
            class="btn btn-primary"
            :disabled="
              !batchReason.trim() || !batchPassword.trim() || certifying
            "
            @click="certifyBatch"
          >
            {{
              certifying
                ? "Certifying..."
                : `Batch certify (${selectedMatrixSites.length})`
            }}
          </button>
        </div>
      </template>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  ClinicalDataTable,
  ClinicalInput,
  handoffFocus,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
} from "ui";
import { ctmsApi, type SiteGreenlightGate } from "../../api/ctmsApi";
import { requestStepUpSignature } from "../../api/signatureVerification";
import { canCertifyGreenlight } from "../../canvas/authority";
import {
  scopedWorkspaceSectionProps,
  useScopedWorkspaceSection,
} from "../../composables/useScopedWorkspaceSection";
import { formatClinicalStatus } from "../../utils/clinicalLabels";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

const props = defineProps(scopedWorkspaceSectionProps);
const workspace = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => void loadReadiness(),
  onSitePropChange: () => {
    if (studyInput.value.trim()) {
      void loadReadiness();
    }
  },
  fallbackErrorTitle: "Startup readiness could not be loaded",
  fallbackErrorMessage: "Startup readiness could not be loaded.",
});
const { authStore, toasts, isStudyLocked, syncActiveScope, recordLoadFailure } =
  workspace;
const {
  assignedStudies,
  assignedSites,
  studyInput,
  siteInput,
  loading,
  loaded,
  loadError,
  scopeGuidance,
} = workspace;

const canCertify = computed(() => canCertifyGreenlight(authStore));
const matrixColumns = computed(() => {
  const cols = [
    { key: "site_id", label: "Site", rowHeader: true },
    { key: "overall_status", label: "Status" },
    { key: "facets", label: "Satisfied facets" },
    { key: "reasons", label: "Blocking reasons" },
    { key: "certified", label: "Certified" },
  ];
  return canCertify.value
    ? [{ key: "select", label: "Select" }, ...cols]
    : cols;
});
const countryCode = ref("US");
const singleGate = ref<SiteGreenlightGate | null>(null);
const countryMatrix = ref<SiteGreenlightGate[]>([]);
const selectedMatrixSites = ref<string[]>([]);
const certifyReason = ref("");
const certifyPassword = ref("");
const batchReason = ref("");
const batchPassword = ref("");
const certifying = ref(false);

interface GateFacetSummary {
  facet_code: string;
  display_name: string;
  evidence_summary: string;
  is_satisfied: boolean;
}

function isGateCertified(status?: string | null): boolean {
  const normalized = String(status || "").toUpperCase();
  return (
    normalized === "CERTIFIED" ||
    normalized === "APPROVED" ||
    normalized === "OVERRIDDEN"
  );
}

function isGateReady(gate: SiteGreenlightGate): boolean {
  return Boolean(gate.is_ready_for_certification ?? gate.is_compliant);
}

function getBlockingReasons(gate: SiteGreenlightGate): string[] {
  if (gate.blocking_reasons?.length) {
    return gate.blocking_reasons;
  }
  return gate.unfulfilled_reasons ?? [];
}

function resolveSignerUsername(): string {
  return authStore.user?.username || authStore.userId || "";
}

function getGateFacets(gate: SiteGreenlightGate): GateFacetSummary[] {
  if (gate.facets?.length) {
    return gate.facets;
  }
  return [
    {
      facet_code: "REGULATORY_IRB",
      display_name: "IRB / EC Favorable Opinion",
      evidence_summary: gate.facet_regulatory_irb
        ? "Approved regulatory milestone on file"
        : "Pending IRB / EC approval",
      is_satisfied: Boolean(gate.facet_regulatory_irb),
    },
    {
      facet_code: "CTA_BUDGET",
      display_name: "CTA & Budget Executed",
      evidence_summary: gate.facet_cta_budget
        ? "Executed clinical trial agreement verified"
        : "Pending CTA / budget execution",
      is_satisfied: Boolean(gate.facet_cta_budget),
    },
    {
      facet_code: "EISF_BASELINE",
      display_name: "eISF Baseline Complete",
      evidence_summary: gate.facet_eisf_baseline
        ? "Essential investigator site file complete"
        : "Missing baseline eISF artifacts",
      is_satisfied: Boolean(gate.facet_eisf_baseline),
    },
    {
      facet_code: "DOA_LOG",
      display_name: "PI Delegation Log Signed",
      evidence_summary: gate.facet_doa_log
        ? "PI-endorsed DoA log active"
        : "Pending PI delegation sign-off",
      is_satisfied: Boolean(gate.facet_doa_log),
    },
    {
      facet_code: "STAFF_TRAINING",
      display_name: "GCP & Protocol Training",
      evidence_summary: gate.facet_staff_training
        ? "Delegated staff training verified"
        : "Incomplete staff training records",
      is_satisfied: Boolean(gate.facet_staff_training),
    },
    {
      facet_code: "IP_RELEASE",
      display_name: "Investigational Supply Released",
      evidence_summary: gate.facet_ip_release
        ? "Site supply authorization active"
        : "Investigational product hold",
      is_satisfied: Boolean(gate.facet_ip_release),
    },
  ];
}

function formatUtcDate(iso?: string | null): string {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toUTCString();
}

async function loadReadiness() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  if (!study) return;

  syncActiveScope(study, site);
  loading.value = true;
  loadError.value = "";
  scopeGuidance.value = "";
  selectedMatrixSites.value = [];

  try {
    const matrixPromise = ctmsApi.getCountryGreenlightMatrix(
      study,
      countryCode.value
    );
    const gatePromise = site
      ? ctmsApi.evaluateSiteGreenlight(site, study, countryCode.value)
      : Promise.resolve(null);

    const [matrix, gate] = await Promise.all([matrixPromise, gatePromise]);
    countryMatrix.value = matrix || [];
    singleGate.value = gate;
    loaded.value = true;
  } catch (err) {
    singleGate.value = null;
    countryMatrix.value = [];
    recordLoadFailure(err, study, site);
  } finally {
    loading.value = false;
  }
}

async function certifySingleSite() {
  const password = certifyPassword.value;
  certifyPassword.value = "";
  if (
    !singleGate.value ||
    !certifyReason.value.trim() ||
    !password.trim() ||
    !canCertify.value
  ) {
    return;
  }
  certifying.value = true;
  try {
    const siteId = singleGate.value.site_id;
    const verifyRes = await requestStepUpSignature({
      username: resolveSignerUsername(),
      password,
      totp: null,
      action: `/api/v1/ctms/startup/sites/${encodeURIComponent(siteId)}/greenlight/certify`,
      semanticAction: "ctms.greenlight.certify",
    });

    const updated = await ctmsApi.certifySiteGreenlight(
      siteId,
      singleGate.value.study_id,
      certifyReason.value.trim(),
      singleGate.value.country_code,
      verifyRes.sig_token
    );
    singleGate.value = updated;
    certifyReason.value = "";
    toasts.push({
      tone: "success",
      title: `Site ${updated.site_id} certified`,
      message: "Startup greenlight certification recorded.",
    });
    await loadReadiness();
    void handoffFocus("#ctms-greenlight-title");
  } catch (err) {
    toasts.apiError("Site certification failed", err);
  } finally {
    certifying.value = false;
  }
}

async function certifyBatch() {
  const study = studyInput.value.trim();
  const password = batchPassword.value;
  batchPassword.value = "";
  if (
    !study ||
    !selectedMatrixSites.value.length ||
    !batchReason.value.trim() ||
    !password.trim() ||
    !canCertify.value
  ) {
    return;
  }
  certifying.value = true;
  try {
    const verifyRes = await requestStepUpSignature({
      username: resolveSignerUsername(),
      password,
      totp: null,
      action: "/api/v1/ctms/startup/matrix/batch-certify",
      semanticAction: "ctms.greenlight.batch_certify",
    });

    const res = await ctmsApi.batchCertifyCountrySites(
      {
        study_id: study,
        country_code: countryCode.value,
        site_ids: selectedMatrixSites.value,
        meaning: batchReason.value.trim(),
      },
      batchReason.value.trim(),
      verifyRes.sig_token
    );
    batchReason.value = "";
    selectedMatrixSites.value = [];
    const count = res.total_certified ?? res.certified_count;
    toasts.push({
      tone: "success",
      title: `Certified ${count} site(s)`,
      message: `Country ${res.country_code} startup greenlight updated.`,
    });
    await loadReadiness();
    void handoffFocus("#ctms-greenlight-title");
  } catch (err) {
    toasts.apiError("Batch certification failed", err);
  } finally {
    certifying.value = false;
  }
}

onMounted(() => {
  if (studyInput.value.trim()) {
    void loadReadiness();
  }
});
</script>

<style scoped>
.gate-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
}

.gate-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.meta-sep {
  margin: 0 6px;
  color: var(--semantic-color-text-subtle);
}

.gate-badge {
  padding: 3px 9px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 700;
}

.gate-certified,
.gate-approved,
.gate-overridden,
.gate-ready {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.gate-blocked,
.gate-pending {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}

.facet-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 8px;
}

.facet-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) auto;
  gap: 12px;
  align-items: center;
  padding: 8px 10px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: 0.88rem;
}

@media (max-width: 640px) {
  .facet-row {
    grid-template-columns: minmax(0, 1fr);
    gap: 6px;
  }
}

.facet-name {
  font-weight: 600;
}

.facet-detail {
  color: var(--semantic-color-text-secondary);
}

.facet-ok {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}

.facet-blocked {
  color: var(--semantic-color-error);
  font-weight: 600;
}

.certified-meta {
  margin: 0;
  color: var(--semantic-color-success-dark);
  font-size: 0.88rem;
  font-weight: 600;
}

.certify-box {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 8px;
  margin-top: 8px;
}

.certify-box :deep(.certify-input-field) {
  flex: 1 1 240px;
  min-width: 0;
  max-width: 100%;
  margin-bottom: 0;
  box-sizing: border-box;
}

.subsection-title {
  margin: 0 0 8px;
  font-size: 1rem;
}

.muted-inline {
  color: var(--semantic-color-text-subtle);
}
</style>
