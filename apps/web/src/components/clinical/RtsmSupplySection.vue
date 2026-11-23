<template>
  <section
    id="rtsm-supply-section"
    class="card ctms-section"
    aria-labelledby="rtsm-supply-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="rtsm-supply-title" class="card-title" tabindex="-1">
          Investigational supply
        </h3>
        <p class="section-subtitle">
          Chain-of-custody trace, site kit accountability, and randomization
          scheme verification.
        </p>
      </div>
    </div>

    <CtmsSectionScopeBar
      v-model:study="studyInput"
      v-model:site="siteInput"
      prefix="rtsm"
      form-id="rtsm-scope-form"
      aria-label="Investigational supply scope"
      :show-study-input="!props.studyCode"
      :assigned-studies="assignedStudies"
      :assigned-sites="assignedSites"
      submit-id="btn-load-supply"
      :submit-label="loading ? 'Loading...' : 'Load supply status'"
      :submit-disabled="!studyInput.trim() || loading"
      :scope-guidance="scopeGuidance"
      :load-error="loadError"
      @submit="loadSupplyOverview"
    >
      <template #after-site>
        <label for="rtsm-subject"
          >Participant (optional for custody trace)</label
        >
        <input
          id="rtsm-subject"
          v-model="subjectInput"
          type="text"
          autocomplete="off"
          placeholder="Participant code"
        />
      </template>
    </CtmsSectionScopeBar>

    <div id="rtsm-cgt-panel" class="subpanel">
      <h4>Autologous chain-of-custody &amp; disposition ledger</h4>
      <p class="subpanel-meta">
        Destruction certificates recorded:
        <strong>{{ cgtCertificates.length }}</strong>
        · Vial return reconciliations:
        <strong>{{ cgtReconciliations.length }}</strong>
      </p>

      <div v-if="cocTrace" id="rtsm-coc-trace" class="trace-box">
        <p>
          <strong>Participant {{ cocTrace.subject_id }}</strong>
          <template v-if="cocTrace.din"> · DIN {{ cocTrace.din }}</template>
          <template v-if="cocTrace.current_stage">
            · Stage: {{ formatClinicalStatus(cocTrace.current_stage) }}
          </template>
        </p>
        <ul v-if="cocTrace.milestones?.length" class="milestone-list">
          <li v-for="(ms, idx) in cocTrace.milestones" :key="idx">
            <strong>{{ formatClinicalEnum(ms.stage) }}</strong> ({{
              formatClinicalEnum(ms.milestone_type)
            }})
            <template v-if="ms.location_name">
              at {{ ms.location_name }}
            </template>
          </li>
        </ul>
      </div>
      <p v-else-if="cocNotFoundMessage" class="muted-status">
        {{ cocNotFoundMessage }}
      </p>

      <form
        v-if="canWriteCoc"
        id="rtsm-apheresis-form"
        class="mutation-card"
        @submit.prevent="submitApheresis"
      >
        <h5>Initiate apheresis custody record</h5>
        <div class="form-row">
          <label for="cgt-aph-subject">Participant</label>
          <input
            id="cgt-aph-subject"
            v-model="aphSubject"
            type="text"
            placeholder="Participant code"
          />
          <label for="cgt-aph-din">ISBT-128 DIN</label>
          <input
            id="cgt-aph-din"
            v-model="aphDin"
            type="text"
            placeholder="ISBT-128 DIN code"
          />
          <label for="cgt-aph-reason">Reason for change</label>
          <input
            id="cgt-aph-reason"
            v-model="aphReason"
            type="text"
            placeholder="Apheresis collection intake"
          />
          <button
            id="btn-record-apheresis"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !studyInput.trim() ||
              !siteInput.trim() ||
              !aphSubject.trim() ||
              !aphReason.trim() ||
              saving
            "
          >
            Record apheresis
          </button>
        </div>
      </form>
    </div>

    <div id="rtsm-kits-panel" class="subpanel">
      <h4>Site investigational product kit accountability</h4>
      <template v-if="canReconcile">
        <p v-if="!siteInput.trim()" class="muted-status">
          Enter a site code above to inspect blinded kit inventory and launch
          batch return reconciliation.
        </p>
        <template v-else>
          <div class="kit-toolbar">
            <span>
              Loaded kits at {{ siteInput.trim() }}:
              <strong>{{ kitInventory.length }}</strong>
            </span>
            <button
              id="btn-open-kit-reconciliation"
              type="button"
              class="btn btn-primary"
              @click="reconciliationDrawerOpen = true"
            >
              Open batch return reconciliation
            </button>
          </div>
          <div v-if="kitInventory.length" class="table-wrap">
            <table class="data-table">
              <thead>
                <tr>
                  <th scope="col">Kit number</th>
                  <th scope="col">Lot</th>
                  <th scope="col">Status</th>
                  <th scope="col">Expiration</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="kit in kitInventory" :key="kit.kit_number">
                  <td>
                    <strong>{{ kit.kit_number }}</strong>
                  </td>
                  <td>{{ kit.lot_number }}</td>
                  <td>{{ formatClinicalStatus(kit.status) }}</td>
                  <td>{{ kit.expiration_date || "—" }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </template>
      </template>
      <p v-else id="rtsm-pharmacist-separation-note" class="readonly-note">
        Separation of duties: blinded IP kit inventory and return reconciliation
        are restricted to unblinded Site Pharmacist authorization.
      </p>

      <form
        v-if="canSeed"
        id="rtsm-seed-kits-form"
        class="mutation-card"
        @submit.prevent="submitSeedKit"
      >
        <h5>Seed initial site kit inventory</h5>
        <div class="form-row">
          <label for="seed-kit-number">Kit number</label>
          <input
            id="seed-kit-number"
            v-model="seedKitNumber"
            type="text"
            placeholder="KIT-1001"
          />
          <label for="seed-lot-number">Lot number</label>
          <input
            id="seed-lot-number"
            v-model="seedLotNumber"
            type="text"
            placeholder="LOT-ALPHA"
          />
          <label for="seed-treatment-arm">Treatment arm</label>
          <input
            id="seed-treatment-arm"
            v-model="seedTreatmentArm"
            type="text"
            placeholder="ACTIVE"
          />
          <label for="seed-expiry">Expiration</label>
          <input id="seed-expiry" v-model="seedExpiry" type="date" />
          <label for="seed-reason">Reason for change</label>
          <input
            id="seed-reason"
            v-model="seedReason"
            type="text"
            placeholder="Initial depot release"
          />
          <button
            id="btn-seed-kit-inventory"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !studyInput.trim() ||
              !siteInput.trim() ||
              !seedKitNumber.trim() ||
              !seedLotNumber.trim() ||
              !seedTreatmentArm.trim() ||
              !seedExpiry ||
              !seedReason.trim() ||
              saving
            "
          >
            Seed kit
          </button>
        </div>
      </form>
    </div>

    <div
      v-if="canReadSchemes || canManageSchemes"
      id="rtsm-schemes-panel"
      class="subpanel"
    >
      <div class="kit-toolbar">
        <h4>Randomization schemes</h4>
        <button
          v-if="canManageSchemes"
          id="btn-toggle-draft-scheme"
          type="button"
          class="btn btn-secondary"
          @click="draftSchemeFormOpen = !draftSchemeFormOpen"
        >
          {{ draftSchemeFormOpen ? "Close draft form" : "Draft scheme" }}
        </button>
      </div>

      <form
        v-if="canManageSchemes && draftSchemeFormOpen"
        id="rtsm-draft-scheme-form"
        class="mutation-card"
        @submit.prevent="submitDraftScheme"
      >
        <h5>Draft randomization scheme</h5>
        <div class="form-row">
          <label for="scheme-method">Method</label>
          <select id="scheme-method" v-model="schemeMethod">
            <option value="permuted_block">Permuted block</option>
            <option value="minimization">Pocock-Simon minimization</option>
          </select>
          <label for="scheme-arms">Arms (CODE:ratio)</label>
          <input
            id="scheme-arms"
            v-model="schemeArmsInput"
            type="text"
            placeholder="PLACEBO:1, ACTIVE:1"
          />
          <label for="scheme-strat-factors">
            Stratification factors (optional)
          </label>
          <input
            id="scheme-strat-factors"
            v-model="schemeStratFactorsInput"
            type="text"
            placeholder="age_group=<65,>=65; stage=II,III"
          />
          <template v-if="schemeMethod === 'permuted_block'">
            <label for="scheme-block-sizes">Block sizes</label>
            <input
              id="scheme-block-sizes"
              v-model="schemeBlockSizesInput"
              type="text"
              placeholder="4"
            />
            <label for="scheme-slots-per-stratum">Slots per stratum</label>
            <input
              id="scheme-slots-per-stratum"
              v-model.number="schemeSlotsPerStratum"
              type="number"
              min="2"
            />
          </template>
          <template v-else>
            <label for="scheme-preferred-prob">Preferred arm probability</label>
            <input
              id="scheme-preferred-prob"
              v-model.number="schemePreferredProbability"
              type="number"
              step="0.05"
              min="0.5"
              max="1"
            />
          </template>
          <label class="checkbox-label" for="scheme-requires-kit">
            <input
              id="scheme-requires-kit"
              v-model="schemeRequiresKit"
              type="checkbox"
            />
            Requires site IP kit assignment
          </label>
          <label for="scheme-draft-reason">Reason for change</label>
          <input
            id="scheme-draft-reason"
            v-model="schemeDraftReason"
            type="text"
            placeholder="Initial SAP randomization specification"
          />
          <button
            id="btn-submit-draft-scheme"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !studyInput.trim() ||
              !schemeArmsInput.trim() ||
              !schemeDraftReason.trim() ||
              saving
            "
          >
            Save draft scheme
          </button>
        </div>
      </form>

      <p v-if="!schemes.length" class="muted-status">
        No randomization schemes configured yet for {{ studyInput.trim() }}.
      </p>
      <div v-else class="table-wrap">
        <table class="data-table">
          <thead>
            <tr>
              <th scope="col">Version</th>
              <th scope="col">Method</th>
              <th scope="col">Status</th>
              <th scope="col">Requires kit</th>
              <th scope="col">Drafted by</th>
              <th scope="col">Approved by</th>
              <th v-if="canManageSchemes" scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="scheme in schemes" :key="scheme.id">
              <td>v{{ scheme.version }}</td>
              <td>{{ formatClinicalEnum(scheme.method) }}</td>
              <td>{{ formatClinicalStatus(scheme.status) }}</td>
              <td>{{ scheme.requires_kit ? "Yes" : "No" }}</td>
              <td>{{ scheme.drafted_by || "—" }}</td>
              <td>{{ scheme.approved_by || "—" }}</td>
              <td v-if="canManageSchemes">
                <template v-if="scheme.status === 'DRAFT'">
                  <button
                    :id="`btn-approve-scheme-${scheme.id}`"
                    data-testid="btn-approve-scheme"
                    type="button"
                    class="btn btn-primary"
                    :disabled="saving || isSelfDraftedScheme(scheme)"
                    @click="openApproveSchemeModal(scheme)"
                  >
                    Approve scheme
                  </button>
                  <span
                    v-if="isSelfDraftedScheme(scheme)"
                    data-testid="self-approval-blocked-note"
                    class="self-approval-note"
                  >
                    Requires second-person statistician approval
                  </span>
                </template>
                <span v-else class="muted-status">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <p id="rtsm-dose-escalation-scope-note" class="unavailable-note">
      Adaptive oncology dose escalation (BOIN / CRM / RAR cohort transitions) is
      not backed by a Cadence service yet and runs only in demo builds (Allocate
      roadmap). See <PlansRoadmapLink />.
    </p>

    <BulkIpKitReconciliationDrawer
      v-if="canReconcile"
      :is-open="reconciliationDrawerOpen"
      :study-id="studyInput.trim()"
      :site-id="siteInput.trim()"
      @close="reconciliationDrawerOpen = false"
      @reconciled="loadSupplyOverview"
    />

    <SignatureCaptureModal
      v-if="canManageSchemes"
      :is-open="Boolean(approvingScheme)"
      :username="authStore.user?.username || ''"
      :signer-name="
        authStore.user?.displayName || authStore.user?.username || ''
      "
      signer-role="Unblinded Statistician"
      :reasons="SCHEME_APPROVAL_REASONS"
      :action-url="
        approvingScheme
          ? approveRandomizationSchemeAction(approvingScheme.id)
          : ''
      "
      :semantic-action="APPROVE_RANDOMIZATION_SCHEME_SEMANTIC_ACTION"
      :on-sign="handleApproveSchemeSigned"
      @cancel="approvingScheme = null"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ApiError } from "../../api/client";
import {
  executionApi,
  type CgtChainOfCustodyTrace,
  type RandomizationScheme,
  type SiteIpKitInventoryItem,
} from "../../api/executionApi";
import {
  canManageRandomizationSchemes,
  canReadRandomizationSchemes,
  canReconcileIpKits,
  canRecordChainOfCustody,
  canSeedIpKits,
} from "../../canvas/authority";
import {
  APPROVE_RANDOMIZATION_SCHEME_SEMANTIC_ACTION,
  approveRandomizationSchemeAction,
} from "../../canvas/lifecycle";
import { useScopedWorkspaceSection } from "../../composables/useScopedWorkspaceSection";
import {
  formatClinicalEnum,
  formatClinicalStatus,
} from "../../utils/clinicalLabels";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import BulkIpKitReconciliationDrawer from "./BulkIpKitReconciliationDrawer.vue";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import PlansRoadmapLink from "./PlansRoadmapLink.vue";
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
  saving,
  loadError,
  scopeGuidance,
  syncActiveScope,
  recordLoadFailure,
} = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => {
    void loadSupplyOverview();
  },
  fallbackErrorTitle: "Supply status could not be loaded",
  fallbackErrorMessage: "Investigational supply status could not be loaded.",
});

const canReconcile = computed(() => canReconcileIpKits(authStore));
const canSeed = computed(() => canSeedIpKits(authStore));
const canWriteCoc = computed(() => canRecordChainOfCustody(authStore));
const canReadSchemes = computed(() => canReadRandomizationSchemes(authStore));
const canManageSchemes = computed(() =>
  canManageRandomizationSchemes(authStore)
);

const SCHEME_APPROVAL_REASONS = [
  "Independent second-person verification and approval of randomization scheme",
  "APPROVAL",
];

const subjectInput = ref("");
const kitInventory = ref<SiteIpKitInventoryItem[]>([]);
const schemes = ref<RandomizationScheme[]>([]);
const cgtCertificates = ref<Array<Record<string, unknown>>>([]);
const cgtReconciliations = ref<Array<Record<string, unknown>>>([]);
const cocTrace = ref<CgtChainOfCustodyTrace | null>(null);
const cocNotFoundMessage = ref("");
const reconciliationDrawerOpen = ref(false);

const aphSubject = ref("");
const aphDin = ref("");
const aphReason = ref("");

const seedKitNumber = ref("");
const seedLotNumber = ref("");
const seedTreatmentArm = ref("");
const seedExpiry = ref("");
const seedReason = ref("");

const draftSchemeFormOpen = ref(false);
const schemeMethod = ref("permuted_block");
const schemeArmsInput = ref("PLACEBO:1, ACTIVE:1");
const schemeStratFactorsInput = ref("");
const schemeBlockSizesInput = ref("4");
const schemeSlotsPerStratum = ref(8);
const schemePreferredProbability = ref(0.8);
const schemeRequiresKit = ref(true);
const schemeDraftReason = ref("");
const approvingScheme = ref<RandomizationScheme | null>(null);

function parseSchemeArms(raw: string): Record<string, number> | null {
  const arms: Record<string, number> = {};
  for (const entry of raw
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean)) {
    const [armRaw, ratioRaw] = entry.split(":").map((s) => s.trim());
    const ratio =
      ratioRaw === undefined || ratioRaw === "" ? 1 : Number(ratioRaw);
    if (!armRaw || !Number.isInteger(ratio) || ratio < 1) {
      return null;
    }
    arms[armRaw] = ratio;
  }
  return Object.keys(arms).length >= 2 ? arms : null;
}

function parseStratificationFactors(
  raw: string
): Record<string, string[]> | null {
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const factors: Record<string, string[]> = {};
  for (const clause of trimmed
    .split(";")
    .map((s) => s.trim())
    .filter(Boolean)) {
    const eqIndex = clause.indexOf("=");
    if (eqIndex <= 0) return null;
    const name = clause.slice(0, eqIndex).trim();
    const levels = clause
      .slice(eqIndex + 1)
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    if (!name || levels.length < 2) return null;
    factors[name] = levels;
  }
  return Object.keys(factors).length > 0 ? factors : null;
}

function isSelfDraftedScheme(scheme: RandomizationScheme): boolean {
  const draftedBy = String(scheme.drafted_by || "")
    .trim()
    .toLowerCase();
  if (!draftedBy) return false;
  const userId = String(authStore.user?.id || "")
    .trim()
    .toLowerCase();
  const username = String(authStore.user?.username || "")
    .trim()
    .toLowerCase();
  return (
    (Boolean(userId) && draftedBy === userId) ||
    (Boolean(username) && draftedBy === username)
  );
}

function openApproveSchemeModal(scheme: RandomizationScheme) {
  if (isSelfDraftedScheme(scheme)) {
    toasts.push({
      tone: "error",
      title: "Second-person approval required",
      message:
        "A second unblinded statistician must approve a randomization scheme you drafted.",
    });
    return;
  }
  approvingScheme.value = scheme;
}

async function loadSupplyOverview() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  const subject = subjectInput.value.trim();
  if (!study) return;

  syncActiveScope(study, site);
  loading.value = true;
  loadError.value = "";
  scopeGuidance.value = "";
  cocTrace.value = null;
  cocNotFoundMessage.value = "";

  try {
    const [certs, recs] = await Promise.all([
      executionApi.listCgtCertificates(study),
      executionApi.listCgtReconciliations(study),
    ]);
    cgtCertificates.value = certs || [];
    cgtReconciliations.value = recs || [];

    kitInventory.value =
      canReconcile.value && site
        ? (await executionApi.getSiteIpKitInventory(study, site)) || []
        : [];

    schemes.value =
      canReadSchemes.value || canManageSchemes.value
        ? (await executionApi.listRandomizationSchemes(study)) || []
        : [];

    if (subject) {
      try {
        cocTrace.value = await executionApi.getChainOfCustody(study, subject);
      } catch (cocErr) {
        if (cocErr instanceof ApiError && cocErr.status === 404) {
          cocNotFoundMessage.value = `No autologous chain-of-custody record found for participant ${subject}.`;
        } else {
          throw cocErr;
        }
      }
    }
  } catch (err) {
    recordLoadFailure(err, study, site);
  } finally {
    loading.value = false;
  }
}

async function submitApheresis() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  const subject = aphSubject.value.trim();
  const reason = aphReason.value.trim();
  if (!study || !site || !subject || !reason || !canWriteCoc.value) return;

  saving.value = true;
  try {
    await executionApi.recordCgtApheresis(
      {
        study_id: study,
        collection_site_id: site,
        subject_id: subject,
        din: aphDin.value.trim() || null,
      },
      reason
    );
    subjectInput.value = subject;
    aphReason.value = "";
    toasts.push({
      tone: "success",
      title: "Apheresis recorded",
      message: `Chain of custody started for ${subject}.`,
    });
    await loadSupplyOverview();
  } catch (err) {
    toasts.apiError("Apheresis record could not be created", err);
  } finally {
    saving.value = false;
  }
}

async function submitSeedKit() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  const treatmentArm = seedTreatmentArm.value.trim();
  const reason = seedReason.value.trim();
  if (
    !study ||
    !site ||
    !seedKitNumber.value.trim() ||
    !seedLotNumber.value.trim() ||
    !treatmentArm ||
    !seedExpiry.value ||
    !reason ||
    !canSeed.value
  ) {
    return;
  }
  saving.value = true;
  try {
    await executionApi.seedSiteIpKitInventory(
      {
        study_id: study,
        site_id: site,
        kits: [
          {
            kit_number: seedKitNumber.value.trim(),
            lot_number: seedLotNumber.value.trim(),
            expiration_date: `${seedExpiry.value}T00:00:00Z`,
            status: "ACTIVE_AT_SITE",
            treatment_arm: treatmentArm,
          },
        ],
      },
      reason
    );
    seedKitNumber.value = "";
    seedTreatmentArm.value = "";
    seedReason.value = "";
    toasts.push({
      tone: "success",
      title: "Site kit inventory seeded",
      message: `Kit added as ACTIVE_AT_SITE for site ${site}.`,
    });
    await loadSupplyOverview();
  } catch (err) {
    toasts.apiError("Kit inventory could not be seeded", err);
  } finally {
    saving.value = false;
  }
}

async function submitDraftScheme() {
  const study = studyInput.value.trim();
  const reason = schemeDraftReason.value.trim();
  if (!study || !reason || !canManageSchemes.value) return;

  const arms = parseSchemeArms(schemeArmsInput.value);
  if (!arms) {
    toasts.push({
      tone: "error",
      title: "Invalid treatment arms",
      message:
        "Specify at least two arms with positive integer ratios (for example: PLACEBO:1, ACTIVE:1).",
    });
    return;
  }
  const stratFactors = parseStratificationFactors(
    schemeStratFactorsInput.value
  );
  if (schemeStratFactorsInput.value.trim() && !stratFactors) {
    toasts.push({
      tone: "error",
      title: "Invalid stratification factors",
      message:
        "Format factors as factor=Level1,Level2 separated by semicolons.",
    });
    return;
  }
  const blockSizes =
    schemeMethod.value === "permuted_block"
      ? schemeBlockSizesInput.value
          .split(",")
          .map((s) => Number(s.trim()))
          .filter((n) => Number.isInteger(n) && n > 0)
      : null;

  saving.value = true;
  try {
    const created = await executionApi.draftRandomizationScheme(
      {
        study_id: study,
        method: schemeMethod.value,
        arms,
        stratification_factors: stratFactors,
        block_sizes: blockSizes,
        slots_per_stratum:
          schemeMethod.value === "permuted_block"
            ? Number(schemeSlotsPerStratum.value) || 8
            : null,
        preferred_arm_probability:
          schemeMethod.value === "minimization"
            ? Number(schemePreferredProbability.value) || 0.8
            : null,
        requires_kit: schemeRequiresKit.value,
      },
      reason
    );
    schemeDraftReason.value = "";
    draftSchemeFormOpen.value = false;
    toasts.push({
      tone: "success",
      title: `Drafted scheme v${created.version}`,
      message:
        "A second unblinded statistician can now approve it for go-live.",
    });
    await loadSupplyOverview();
  } catch (err) {
    toasts.apiError("Randomization scheme draft failed", err);
  } finally {
    saving.value = false;
  }
}

async function handleApproveSchemeSigned(
  sigToken: string,
  signingReason: string
) {
  const target = approvingScheme.value;
  if (!target) return { status: "FAILED", message: "No scheme selected." };
  saving.value = true;
  try {
    const approved = await executionApi.approveRandomizationScheme(
      target.id,
      signingReason ||
        "Independent second-person verification of randomization scheme",
      sigToken
    );
    approvingScheme.value = null;
    toasts.push({
      tone: "success",
      title: `Approved scheme v${approved.version}`,
      message: `Scheme v${approved.version} is now live for ${approved.study_id}.`,
    });
    await loadSupplyOverview();
    return { kind: "signed" };
  } catch (err) {
    toasts.apiError("Randomization scheme approval failed", err);
    return {
      status: "FAILED",
      message:
        err instanceof Error
          ? err.message
          : "Randomization scheme approval failed.",
    };
  } finally {
    saving.value = false;
  }
}

onMounted(() => {
  if (studyInput.value.trim()) {
    void loadSupplyOverview();
  }
});
</script>

<style scoped>
.subpanel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 14px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
}

.subpanel h4 {
  margin: 0;
  font-size: 0.98rem;
}

.subpanel-meta {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
}

.trace-box {
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
}

.milestone-list {
  margin: 6px 0 0;
  padding-left: 18px;
  font-size: 0.86rem;
}

.kit-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.unavailable-note {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.88rem;
}

.self-approval-note {
  display: block;
  margin-top: 4px;
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

.checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
</style>
