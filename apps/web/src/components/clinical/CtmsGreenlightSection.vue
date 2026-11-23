<template>
  <section
    id="ctms-greenlight-section"
    class="card ctms-section"
    aria-labelledby="ctms-greenlight-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-greenlight-title" class="card-title">Startup readiness</h3>
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
          <span v-if="singleGate.site_name" class="meta-sep">·</span>
          <span v-if="singleGate.site_name">{{ singleGate.site_name }}</span>
          <span class="meta-sep">·</span>
          <span>{{ singleGate.country_code }}</span>
        </div>
        <span
          class="gate-badge"
          :class="`gate-${singleGate.overall_status.toLowerCase()}`"
        >
          {{ singleGate.overall_status }}
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
          !isGateCertified(singleGate.overall_status) &&
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

    <form
      v-if="
        loaded &&
        siteInput.trim() &&
        canWrite &&
        (missingSiteGate ||
          (singleGate && !isGateCertified(singleGate.overall_status)))
      "
      id="greenlight-upsert-form"
      class="gate-card"
      @submit.prevent="saveSiteGate"
    >
      <h4 class="subsection-title">
        {{
          missingSiteGate
            ? `Initialize startup greenlight gate for ${siteInput.trim()}`
            : `Update 6-facet readiness for ${siteInput.trim()}`
        }}
      </h4>
      <p v-if="missingSiteGate" class="muted-status">
        No startup greenlight gate has been initialized yet for
        {{ siteInput.trim() }} in {{ studyInput.trim() }}.
      </p>
      <div class="certify-box">
        <label for="greenlight-site-name">Site name</label>
        <input
          id="greenlight-site-name"
          v-model="upsertForm.siteName"
          type="text"
          placeholder="Clinical research center name"
        />
      </div>
      <div class="facet-toggle-grid">
        <label for="facet-reg-irb" class="facet-toggle-item">
          <input
            id="facet-reg-irb"
            v-model="upsertForm.facetRegulatoryIrb"
            type="checkbox"
          />
          <span>Regulatory &amp; IRB / Ethics</span>
        </label>
        <label for="facet-cta-budget" class="facet-toggle-item">
          <input
            id="facet-cta-budget"
            v-model="upsertForm.facetCtaBudget"
            type="checkbox"
          />
          <span>CTA &amp; Budget Schedule</span>
        </label>
        <label for="facet-eisf-baseline" class="facet-toggle-item">
          <input
            id="facet-eisf-baseline"
            v-model="upsertForm.facetEisfBaseline"
            type="checkbox"
          />
          <span>eISF Baseline Documents</span>
        </label>
        <label for="facet-doa-log" class="facet-toggle-item">
          <input
            id="facet-doa-log"
            v-model="upsertForm.facetDoaLog"
            type="checkbox"
          />
          <span>Delegation of Authority (DOA) Log</span>
        </label>
        <label for="facet-staff-training" class="facet-toggle-item">
          <input
            id="facet-staff-training"
            v-model="upsertForm.facetStaffTraining"
            type="checkbox"
          />
          <span>Protocol &amp; GCP Staff Training</span>
        </label>
        <label for="facet-ip-release" class="facet-toggle-item">
          <input
            id="facet-ip-release"
            v-model="upsertForm.facetIpRelease"
            type="checkbox"
          />
          <span>IP Depot Greenlight Release</span>
        </label>
      </div>
      <div class="certify-box">
        <label for="greenlight-upsert-reason">Reason for change</label>
        <input
          id="greenlight-upsert-reason"
          v-model="upsertForm.reasonForChange"
          type="text"
          placeholder="Document evidence justification for startup facet readiness"
        />
        <button
          id="btn-save-site-greenlight"
          type="submit"
          class="btn btn-primary"
          :disabled="!upsertForm.reasonForChange.trim() || savingGate"
        >
          {{
            savingGate
              ? "Saving..."
              : missingSiteGate
                ? `Initialize ${siteInput.trim()}`
                : `Update ${siteInput.trim()} readiness`
          }}
        </button>
      </div>
    </form>
    <p
      v-else-if="loaded && missingSiteGate && !canWrite"
      id="greenlight-write-role-note"
      class="readonly-note"
    >
      {{ GREENLIGHT_WRITE_ROLE_REASON }}
    </p>

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
                      isGateCertified(row.overall_status)
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
                    {{ row.overall_status }}
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
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ctmsApi, type SiteGreenlightGate } from "../../api/ctmsApi";
import {
  GREENLIGHT_WRITE_ROLE_REASON,
  canCertifyGreenlight,
  canWriteGreenlight,
} from "../../canvas/authority";
import { useScopedWorkspaceSection } from "../../composables/useScopedWorkspaceSection";
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

const canCertify = computed(() => canCertifyGreenlight(authStore));
const canWrite = computed(() => canWriteGreenlight(authStore));
const countryCode = ref("US");
const singleGate = ref<SiteGreenlightGate | null>(null);
const missingSiteGate = ref(false);
const countryMatrix = ref<SiteGreenlightGate[]>([]);
const selectedMatrixSites = ref<string[]>([]);
const certifyReason = ref("");
const batchReason = ref("");
const certifying = ref(false);
const savingGate = ref(false);

const upsertForm = reactive({
  siteName: "",
  facetRegulatoryIrb: false,
  facetCtaBudget: false,
  facetEisfBaseline: false,
  facetDoaLog: false,
  facetStaffTraining: false,
  facetIpRelease: false,
  reasonForChange: "",
});

function isGateCertified(status?: string | null): boolean {
  const norm = (status || "").toUpperCase();
  return norm === "APPROVED" || norm === "CERTIFIED" || norm === "OVERRIDDEN";
}

function syncUpsertForm(gate: SiteGreenlightGate | null, siteCode: string) {
  if (gate) {
    upsertForm.siteName = gate.site_name || siteCode;
    upsertForm.facetRegulatoryIrb = Boolean(gate.facet_regulatory_irb);
    upsertForm.facetCtaBudget = Boolean(gate.facet_cta_budget);
    upsertForm.facetEisfBaseline = Boolean(gate.facet_eisf_baseline);
    upsertForm.facetDoaLog = Boolean(gate.facet_doa_log);
    upsertForm.facetStaffTraining = Boolean(gate.facet_staff_training);
    upsertForm.facetIpRelease = Boolean(gate.facet_ip_release);
  } else {
    upsertForm.siteName = siteCode ? `Clinical Site ${siteCode}` : "";
    upsertForm.facetRegulatoryIrb = false;
    upsertForm.facetCtaBudget = false;
    upsertForm.facetEisfBaseline = false;
    upsertForm.facetDoaLog = false;
    upsertForm.facetStaffTraining = false;
    upsertForm.facetIpRelease = false;
  }
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
  missingSiteGate.value = false;

  try {
    const matrix = await ctmsApi.getCountryGreenlightMatrix(
      study,
      countryCode.value
    );
    countryMatrix.value = matrix || [];

    if (site) {
      try {
        const gate = await ctmsApi.evaluateSiteGreenlight(
          site,
          study,
          countryCode.value
        );
        singleGate.value = gate;
        syncUpsertForm(gate, site);
      } catch (gateErr) {
        const status = (gateErr as { status?: number } | null)?.status;
        if (status === 404) {
          singleGate.value = null;
          missingSiteGate.value = true;
          syncUpsertForm(null, site);
        } else {
          throw gateErr;
        }
      }
    } else {
      singleGate.value = null;
    }

    loaded.value = true;
  } catch (err) {
    singleGate.value = null;
    countryMatrix.value = [];
    recordLoadFailure(err, study, site);
  } finally {
    loading.value = false;
  }
}

async function saveSiteGate() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  const reason = upsertForm.reasonForChange.trim();
  if (!study || !site || !reason || !canWrite.value) {
    return;
  }

  savingGate.value = true;
  try {
    const payload = {
      study_id: study,
      country_code: countryCode.value,
      site_name: upsertForm.siteName.trim() || `Clinical Site ${site}`,
      facet_regulatory_irb: upsertForm.facetRegulatoryIrb,
      facet_cta_budget: upsertForm.facetCtaBudget,
      facet_eisf_baseline: upsertForm.facetEisfBaseline,
      facet_doa_log: upsertForm.facetDoaLog,
      facet_staff_training: upsertForm.facetStaffTraining,
      facet_ip_release: upsertForm.facetIpRelease,
    };

    const saved = missingSiteGate.value
      ? await ctmsApi.initializeSiteGreenlight(site, payload, reason)
      : await ctmsApi.updateSiteGreenlight(site, payload, reason);

    singleGate.value = saved;
    missingSiteGate.value = false;
    upsertForm.reasonForChange = "";
    toasts.push({
      tone: "success",
      title: `Site ${saved.site_id} readiness saved`,
      message: "Startup greenlight facets recorded.",
    });
    await loadReadiness();
  } catch (err) {
    toasts.apiError("Startup greenlight update failed", err);
  } finally {
    savingGate.value = false;
  }
}

async function certifySingleSite() {
  if (!singleGate.value || !certifyReason.value.trim() || !canCertify.value) {
    return;
  }
  certifying.value = true;
  try {
    const updated = await ctmsApi.certifySiteGreenlight(
      singleGate.value.site_id,
      singleGate.value.study_id,
      certifyReason.value.trim(),
      singleGate.value.country_code
    );
    singleGate.value = updated;
    certifyReason.value = "";
    toasts.push({
      tone: "success",
      title: `Site ${updated.site_id} certified`,
      message: "Startup greenlight certification recorded.",
    });
    await loadReadiness();
  } catch (err) {
    toasts.apiError("Site certification failed", err);
  } finally {
    certifying.value = false;
  }
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
  certifying.value = true;
  try {
    const res = await ctmsApi.batchCertifyCountrySites(
      {
        study_id: study,
        country_code: countryCode.value,
        site_ids: selectedMatrixSites.value,
        meaning: "Country-level startup greenlight certification",
      },
      batchReason.value.trim()
    );
    batchReason.value = "";
    selectedMatrixSites.value = [];
    toasts.push({
      tone: "success",
      title: `Certified ${res.total_certified} site(s)`,
      message: `Country ${res.country_code} startup greenlight updated.`,
    });
    await loadReadiness();
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
  text-transform: uppercase;
}

.gate-approved,
.gate-certified,
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
  grid-template-columns: minmax(140px, 1fr) 2fr auto;
  gap: 12px;
  align-items: center;
  padding: 8px 10px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: 0.88rem;
}

.facet-toggle-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 8px;
}

.facet-toggle-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: 0.86rem;
  font-weight: 500;
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
