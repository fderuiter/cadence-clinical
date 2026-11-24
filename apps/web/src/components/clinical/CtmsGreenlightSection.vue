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
      :show-study-input="!props.studyCode"
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
        <label for="greenlight-country">Country</label>
        <select id="greenlight-country" v-model="countryCode">
          <option value="US">United States (US)</option>
          <option value="DE">Germany (DE)</option>
          <option value="GB">United Kingdom (GB)</option>
          <option value="JP">Japan (JP)</option>
          <option value="EU">European Union (EU)</option>
          <option value="GLOBAL">Global (GLOBAL)</option>
        </select>
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
          v-for="facet in singleGate.facets"
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

      <p v-if="singleGate.certified_at" class="certified-meta">
        Certified by {{ singleGate.certified_by }} on
        {{ formatUtcDate(singleGate.certified_at) }}
      </p>

      <div
        v-if="
          singleGate.is_ready_for_certification &&
          singleGate.overall_status !== 'CERTIFIED' &&
          canCertify
        "
        class="certify-box"
      >
        <label for="greenlight-certify-reason"
          >Part 11 certification rationale</label
        >
        <input
          id="greenlight-certify-reason"
          v-model="certifyReason"
          type="text"
          placeholder="State clinical justification for certifying site greenlight"
        />
        <button
          id="btn-certify-site-greenlight"
          type="button"
          class="btn btn-primary"
          :disabled="!certifyReason.trim() || certifying"
          @click="certifySingleSite"
        >
          {{ certifying ? "Certifying..." : `Certify ${singleGate.site_id}` }}
        </button>
      </div>
      <p
        v-else-if="
          singleGate.is_ready_for_certification &&
          singleGate.overall_status !== 'CERTIFIED' &&
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
      <h4 class="subsection-title">
        Country readiness matrix ({{ countryCode }})
      </h4>
      <p v-if="!countryMatrix.length" class="muted-status">
        No sites with milestones, monitoring visits, or delegation records were
        found in your assigned scope for {{ studyInput.trim() }} ({{
          countryCode
        }}).
      </p>
      <template v-else>
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th v-if="canCertify" scope="col">Select</th>
                <th scope="col">Site</th>
                <th scope="col">Status</th>
                <th scope="col">Satisfied facets</th>
                <th scope="col">Blocking reasons</th>
                <th scope="col">Certified</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in countryMatrix" :key="row.site_id">
                <td v-if="canCertify">
                  <input
                    v-model="selectedMatrixSites"
                    type="checkbox"
                    :value="row.site_id"
                    :disabled="
                      !row.is_ready_for_certification ||
                      row.overall_status === 'CERTIFIED'
                    "
                    :aria-label="`Select ${row.site_id} for batch certification`"
                  />
                </td>
                <td>
                  <strong>{{ row.site_id }}</strong>
                </td>
                <td>
                  <span
                    class="gate-badge"
                    :class="`gate-${row.overall_status.toLowerCase()}`"
                  >
                    {{ formatClinicalStatus(row.overall_status) }}
                  </span>
                </td>
                <td>
                  {{ row.facets.filter((f) => f.is_satisfied).length }} /
                  {{ row.facets.length }}
                </td>
                <td>
                  <span v-if="row.blocking_reasons.length">
                    {{ row.blocking_reasons.join("; ") }}
                  </span>
                  <span v-else class="text-ok">None</span>
                </td>
                <td>
                  <span v-if="row.certified_by">
                    {{ row.certified_by }}
                  </span>
                  <span v-else class="muted-inline">—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div
          v-if="canCertify && selectedMatrixSites.length"
          class="certify-box"
        >
          <label for="batch-certify-reason"
            >Batch certification rationale ({{
              selectedMatrixSites.length
            }}
            selected)</label
          >
          <input
            id="batch-certify-reason"
            v-model="batchReason"
            type="text"
            placeholder="Part 11 justification for batch country certification"
          />
          <button
            id="btn-batch-certify-greenlight"
            type="button"
            class="btn btn-primary"
            :disabled="!batchReason.trim() || certifying"
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

    <SignatureCaptureModal
      :is-open="sigModalOpen"
      :username="signerUsername"
      :signer-name="signerDisplayName"
      :reasons="GREENLIGHT_SIGNING_REASONS"
      :action-url="sigActionUrl"
      :semantic-action="sigSemanticAction"
      :batch-id="sigBatchId"
      :on-sign="handleGreenlightSigned"
      @cancel="sigModalOpen = false"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { SEMANTIC_ACTIONS, sha256 } from "ui";
import { ctmsApi, type SiteGreenlightGate } from "../../api/ctmsApi";
import { canCertifyGreenlight } from "../../canvas/authority";
import { useScopedWorkspaceSection } from "../../composables/useScopedWorkspaceSection";
import { formatClinicalStatus } from "../../utils/clinicalLabels";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
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

const {
  authStore,
  toasts,
  assignedStudies,
  assignedSites,
  studyInput,
  siteInput,
  loading,
  loaded,
  loadError,
  scopeGuidance,
  syncActiveScope,
  recordLoadFailure,
} = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => {
    void loadReadiness();
  },
  fallbackErrorTitle: "Startup readiness could not be loaded",
  fallbackErrorMessage: "Startup readiness could not be loaded.",
});

const GREENLIGHT_SIGNING_REASONS = [
  "I certify that all startup greenlight facets are satisfied for trial activation",
  "SPONSOR_APPROVAL",
  "APPROVAL",
];

const canCertify = computed(() => canCertifyGreenlight(authStore));
const countryCode = ref("US");
const singleGate = ref<SiteGreenlightGate | null>(null);
const countryMatrix = ref<SiteGreenlightGate[]>([]);
const selectedMatrixSites = ref<string[]>([]);
const certifyReason = ref("");
const batchReason = ref("");
const certifying = ref(false);

const sigModalOpen = ref(false);
const sigMode = ref<"single" | "batch">("single");
const sigActionUrl = ref("");
const sigSemanticAction = ref("");
const sigBatchId = ref("");

const signerUsername = computed(
  () =>
    authStore.identity?.username ||
    authStore.user?.username ||
    authStore.userId ||
    ""
);
const signerDisplayName = computed(
  () => authStore.user?.displayName || signerUsername.value
);

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

function certifySingleSite() {
  if (!singleGate.value || !certifyReason.value.trim() || !canCertify.value) {
    return;
  }
  sigMode.value = "single";
  sigActionUrl.value = `/api/v1/ctms/startup/sites/${encodeURIComponent(singleGate.value.site_id)}/greenlight/certify`;
  sigSemanticAction.value = SEMANTIC_ACTIONS.CTMS_GREENLIGHT_CERTIFY;
  sigBatchId.value = "";
  sigModalOpen.value = true;
}

async function certifyBatch() {
  const study = studyInput.value.trim();
  if (
    !study ||
    !selectedMatrixSites.value.length ||
    !batchReason.value.trim() ||
    !canCertify.value
  ) {
    return;
  }
  const canonicalSites = [...selectedMatrixSites.value]
    .map((s) => s.trim())
    .sort()
    .join(",");
  sigMode.value = "batch";
  sigActionUrl.value = "/api/v1/ctms/startup/matrix/batch-certify";
  sigSemanticAction.value = SEMANTIC_ACTIONS.CTMS_GREENLIGHT_BATCH_CERTIFY;
  sigBatchId.value = await sha256(canonicalSites);
  sigModalOpen.value = true;
}

async function handleGreenlightSigned(sigToken: string, signingReason: string) {
  certifying.value = true;
  try {
    if (sigMode.value === "single") {
      if (!singleGate.value) {
        return { status: "FAILED", message: "No site selected." };
      }
      const reason = certifyReason.value.trim() || signingReason;
      const updated = await ctmsApi.certifySiteGreenlight(
        singleGate.value.site_id,
        singleGate.value.study_id,
        reason,
        singleGate.value.country_code,
        sigToken
      );
      singleGate.value = updated;
      certifyReason.value = "";
      sigModalOpen.value = false;
      toasts.push({
        tone: "success",
        title: `Site ${updated.site_id} certified`,
        message: "Startup greenlight certification recorded.",
      });
      await loadReadiness();
      return { kind: "signed" };
    }

    const study = studyInput.value.trim();
    const reason = batchReason.value.trim() || signingReason;
    const res = await ctmsApi.batchCertifyCountrySites(
      {
        study_id: study,
        country_code: countryCode.value,
        site_ids: selectedMatrixSites.value,
      },
      reason,
      sigToken
    );
    batchReason.value = "";
    selectedMatrixSites.value = [];
    sigModalOpen.value = false;
    toasts.push({
      tone: "success",
      title: `Certified ${res.total_certified ?? res.certified_count ?? 0} site(s)`,
      message: `Country ${res.country_code} startup greenlight updated.`,
    });
    await loadReadiness();
    return { kind: "signed" };
  } catch (err) {
    toasts.apiError(
      sigMode.value === "single"
        ? "Site certification failed"
        : "Batch certification failed",
      err
    );
    return {
      status: "FAILED",
      message:
        err instanceof Error ? err.message : "Greenlight certification failed.",
    };
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
  grid-template-columns: minmax(140px, 1fr) 2fr auto;
  gap: 12px;
  align-items: center;
  padding: 8px 10px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: 0.88rem;
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
  align-items: center;
  gap: 8px;
  margin-top: 8px;
}

.certify-box input {
  flex: 1 1 240px;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
}

.subsection-title {
  margin: 0 0 8px;
  font-size: 1rem;
}

.muted-inline {
  color: var(--semantic-color-text-subtle);
}
</style>
