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
      :show-study-input="!isStudyLocked"
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
        <ClinicalInput
          v-if="canReadCoc"
          id="rtsm-subject"
          v-model="subjectInput"
          label="Participant (optional for custody trace)"
          type="text"
          autocomplete="off"
          placeholder="Participant code"
          density="compact"
          :show-query-flag="false"
        />
      </template>
    </CtmsSectionScopeBar>

    <div v-if="canReadCoc" id="rtsm-cgt-panel" class="subpanel">
      <h4>Autologous chain-of-custody &amp; disposition ledger</h4>
      <p v-if="cgtError" id="rtsm-cgt-error" class="error-banner" role="alert">
        {{ cgtError }}
      </p>
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
          <ClinicalInput
            id="cgt-aph-subject"
            v-model="aphSubject"
            label="Participant"
            type="text"
            placeholder="Participant code"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="cgt-aph-din"
            v-model="aphDin"
            label="ISBT-128 DIN"
            type="text"
            placeholder="ISBT-128 DIN code"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="cgt-aph-reason"
            v-model="aphReason"
            label="Reason for change"
            type="text"
            placeholder="Apheresis collection intake"
            density="compact"
            :show-query-flag="false"
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
      <p
        v-if="kitsError"
        id="rtsm-kits-error"
        class="error-banner"
        role="alert"
      >
        {{ kitsError }}
      </p>
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
          <ClinicalDataTable
            v-if="kitInventory.length"
            hide-header
            plain
            aria-label="Site investigational product kit inventory"
            wrapper-class="table-wrap"
            table-class="data-table"
            :columns="kitColumns"
            :data="kitInventory"
            row-key="kit_number"
          >
            <template #cell-kit_number="{ row: kit }">
              <strong>{{ kit.kit_number }}</strong>
            </template>
            <template #cell-lot_number="{ row: kit }">
              {{ kit.lot_number }}
            </template>
            <template #cell-status="{ row: kit }">
              {{ formatClinicalStatus(kit.status) }}
            </template>
            <template #cell-expiration_date="{ row: kit }">
              {{ kit.expiration_date || "—" }}
            </template>
          </ClinicalDataTable>
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
          <ClinicalInput
            id="seed-kit-number"
            v-model="seedKitNumber"
            label="Kit number"
            type="text"
            placeholder="KIT-1001"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="seed-lot-number"
            v-model="seedLotNumber"
            label="Lot number"
            type="text"
            placeholder="LOT-ALPHA"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="seed-treatment-arm"
            v-model="seedTreatmentArm"
            label="Treatment arm"
            type="text"
            placeholder="ACTIVE"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="seed-expiry"
            v-model="seedExpiry"
            label="Expiration"
            type="date"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="seed-reason"
            v-model="seedReason"
            label="Reason for change"
            type="text"
            placeholder="Initial depot release"
            density="compact"
            :show-query-flag="false"
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
          class="btn btn-secondary btn-sm"
          @click="toggleDraftSchemeForm"
        >
          {{ draftSchemeFormOpen ? "Close draft form" : "Draft scheme" }}
        </button>
      </div>
      <p
        v-if="schemesError"
        id="rtsm-schemes-error"
        class="error-banner"
        role="alert"
      >
        {{ schemesError }}
      </p>

      <form
        v-if="canManageSchemes && draftSchemeFormOpen"
        id="rtsm-draft-scheme-form"
        class="mutation-card"
        @submit.prevent="submitDraftScheme"
      >
        <h5>Draft randomization scheme</h5>
        <div class="form-row">
          <ClinicalInput
            id="scheme-method"
            v-model="schemeMethod"
            as="select"
            label="Method"
            density="compact"
            :show-query-flag="false"
          >
            <option value="permuted_block">Permuted block</option>
            <option value="minimization">Pocock-Simon minimization</option>
          </ClinicalInput>
          <ClinicalInput
            id="scheme-arms"
            v-model="schemeArmsInput"
            label="Arms (CODE:ratio)"
            type="text"
            placeholder="PLACEBO:1, ACTIVE:1"
            density="compact"
            :show-query-flag="false"
          />
          <ClinicalInput
            id="scheme-strat-factors"
            v-model="schemeStratFactorsInput"
            label="Stratification factors (optional)"
            type="text"
            placeholder="age_group=<65,>=65; stage=II,III"
            density="compact"
            :show-query-flag="false"
          />
          <template v-if="schemeMethod === 'permuted_block'">
            <ClinicalInput
              id="scheme-block-sizes"
              v-model="schemeBlockSizesInput"
              label="Block sizes"
              type="text"
              placeholder="4"
              density="compact"
              :show-query-flag="false"
            />
            <ClinicalInput
              id="scheme-slots-per-stratum"
              :model-value="schemeSlotsPerStratum"
              label="Slots per stratum"
              type="number"
              min="2"
              density="compact"
              :show-query-flag="false"
              @update:model-value="schemeSlotsPerStratum = Number($event)"
            />
          </template>
          <template v-else>
            <ClinicalInput
              id="scheme-preferred-prob"
              :model-value="schemePreferredProbability"
              label="Preferred arm probability"
              type="number"
              step="0.05"
              min="0.5"
              max="1"
              density="compact"
              :show-query-flag="false"
              @update:model-value="schemePreferredProbability = Number($event)"
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
          <ClinicalInput
            id="scheme-draft-reason"
            v-model="schemeDraftReason"
            label="Reason for change"
            type="text"
            placeholder="Initial SAP randomization specification"
            density="compact"
            :show-query-flag="false"
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
      <ClinicalDataTable
        v-else
        hide-header
        plain
        aria-label="Randomization schemes"
        wrapper-class="table-wrap"
        table-class="data-table"
        :columns="schemeColumns"
        :data="schemes"
        row-key="id"
      >
        <template #cell-version="{ row: scheme }">
          v{{ scheme.version }}
        </template>
        <template #cell-method="{ row: scheme }">
          {{ formatClinicalEnum(scheme.method) }}
        </template>
        <template #cell-status="{ row: scheme }">
          {{ formatClinicalStatus(scheme.status) }}
        </template>
        <template #cell-requires_kit="{ row: scheme }">
          {{ scheme.requires_kit ? "Yes" : "No" }}
        </template>
        <template #cell-drafted_by="{ row: scheme }">
          {{ scheme.drafted_by || "—" }}
        </template>
        <template #cell-approved_by="{ row: scheme }">
          {{ scheme.approved_by || "—" }}
        </template>
        <template #cell-actions="{ row: scheme }">
          <template v-if="scheme.status === 'DRAFT'">
            <button
              :id="`btn-approve-scheme-${scheme.id}`"
              data-testid="btn-approve-scheme"
              type="button"
              class="btn btn-primary btn-sm"
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
        </template>
      </ClinicalDataTable>
    </div>

    <p id="rtsm-dose-escalation-scope-note" class="unavailable-note">
      Adaptive oncology dose escalation (BOIN / CRM / RAR cohort transitions) is
      not backed by a Cadence service yet and runs only in demo builds (Allocate
      roadmap). See <PlansRoadmapLink />.
    </p>

    <BulkIpKitReconciliationDrawer
      v-if="canReconcile && reconciliationDrawerOpen"
      :is-open="reconciliationDrawerOpen"
      :study-id="studyInput.trim()"
      :site-id="siteInput.trim()"
      :pharmacist-name="pharmacistDisplayName"
      @close="reconciliationDrawerOpen = false"
      @reconciled="loadSupplyOverview"
    />

    <SignatureCaptureModal
      v-if="canManageSchemes && Boolean(approvingScheme)"
      :is-open="Boolean(approvingScheme)"
      fallback-focus="#btn-toggle-draft-scheme"
      :username="authStore.user?.username || ''"
      :signer-name="signerFullName"
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

<script lang="ts">
import { defineLazyOverlay } from "../../utils/lazyOverlay";

const SignatureCaptureModal = defineLazyOverlay(
  () => import("../SignatureCaptureModal.vue")
);
const BulkIpKitReconciliationDrawer = defineLazyOverlay(
  () => import("./BulkIpKitReconciliationDrawer.vue")
);
</script>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  ClinicalDataTable,
  ClinicalInput,
  handoffFocus,
  useInlineFocusHandoff,
} from "ui";
import { ApiError } from "../../api/client";
import {
  executionApi,
  type CgtChainOfCustodyTrace,
  type RandomizationScheme,
  type SiteIpKitInventoryItem,
} from "../../api/executionApi";
import {
  canManageRandomizationSchemes,
  canReadChainOfCustody,
  canReadRandomizationSchemes,
  canReconcileIpKits,
  canRecordChainOfCustody,
  canSeedIpKits,
} from "../../canvas/authority";
import {
  APPROVE_RANDOMIZATION_SCHEME_SEMANTIC_ACTION,
  approveRandomizationSchemeAction,
} from "../../canvas/lifecycle";
import {
  scopedWorkspaceSectionProps,
  useScopedWorkspaceSection,
} from "../../composables/useScopedWorkspaceSection";
import {
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
} from "../../utils/callerScope";
import {
  formatClinicalEnum,
  formatClinicalStatus,
} from "../../utils/clinicalLabels";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import PlansRoadmapLink from "./PlansRoadmapLink.vue";
import "./ctmsWorkspaceSection.css";

const props = defineProps(scopedWorkspaceSectionProps);
const workspace = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => void loadSupplyOverview(),
  onSitePropChange: () => {
    if (studyInput.value.trim()) {
      void loadSupplyOverview();
    }
  },
  fallbackErrorTitle: "Supply status could not be loaded",
  fallbackErrorMessage: "Investigational supply status could not be loaded.",
});
const { authStore, toasts, isStudyLocked, syncActiveScope, saving } = workspace;
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

const pharmacistDisplayName = computed(() => {
  const fullName = [authStore.user?.firstName, authStore.user?.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || authStore.user?.username || "Site Pharmacist";
});

const signerFullName = computed(() => {
  const identity = authStore.user;
  if (!identity) return "";
  const full = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return full || identity.username || "";
});

const canReconcile = computed(() => canReconcileIpKits(authStore));
const canSeed = computed(() => canSeedIpKits(authStore));
const canReadCoc = computed(() => canReadChainOfCustody(authStore));
const canWriteCoc = computed(() => canRecordChainOfCustody(authStore));
const canReadSchemes = computed(() => canReadRandomizationSchemes(authStore));
const canManageSchemes = computed(() =>
  canManageRandomizationSchemes(authStore)
);

const kitColumns = [
  { key: "kit_number", label: "Kit number", rowHeader: true },
  { key: "lot_number", label: "Lot" },
  { key: "status", label: "Status" },
  { key: "expiration_date", label: "Expiration" },
];

const schemeColumns = computed(() => {
  const cols = [
    { key: "version", label: "Version", rowHeader: true },
    { key: "method", label: "Method" },
    { key: "status", label: "Status" },
    { key: "requires_kit", label: "Requires kit" },
    { key: "drafted_by", label: "Drafted by" },
    { key: "approved_by", label: "Approved by" },
  ];
  return canManageSchemes.value
    ? [...cols, { key: "actions", label: "Actions" }]
    : cols;
});

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
const cgtError = ref("");
const kitsError = ref("");
const schemesError = ref("");
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

const draftSchemeHandoff = useInlineFocusHandoff({
  openTarget: "#scheme-method",
  closeFallback: ["#btn-toggle-draft-scheme", "#rtsm-supply-title"],
  scrollIfOffscreen: true,
});

function toggleDraftSchemeForm() {
  if (draftSchemeFormOpen.value) {
    draftSchemeFormOpen.value = false;
    void draftSchemeHandoff.restoreTrigger([
      "#btn-toggle-draft-scheme",
      "#rtsm-supply-title",
    ]);
  } else {
    draftSchemeHandoff.rememberTrigger("#btn-toggle-draft-scheme");
    draftSchemeFormOpen.value = true;
    void draftSchemeHandoff.handoff("#scheme-method", {
      scrollIfOffscreen: true,
    });
  }
}

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

function formatSubpanelError(
  err: unknown,
  study: string,
  site: string,
  fallbackMessage: string
): string {
  if (isForbiddenScopeError(err)) {
    return formatScopeForbiddenGuidance(err, {
      studyId: study || undefined,
      siteId: site || undefined,
      assignedStudies: assignedStudies.value,
      assignedSites: assignedSites.value,
      hasAllStudiesScope: authStore.hasAllStudiesScope,
    });
  }
  return err instanceof Error && err.message ? err.message : fallbackMessage;
}

async function loadCgtSubpanel(study: string, site: string, subject: string) {
  cgtError.value = "";
  cocTrace.value = null;
  cocNotFoundMessage.value = "";

  if (!canReadCoc.value) {
    cgtCertificates.value = [];
    cgtReconciliations.value = [];
    return;
  }

  try {
    const [certs, recs] = await Promise.all([
      executionApi.listCgtCertificates(study).catch(() => []),
      executionApi.listCgtReconciliations(study).catch(() => []),
    ]);
    cgtCertificates.value = certs || [];
    cgtReconciliations.value = recs || [];

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
    cgtError.value = formatSubpanelError(
      err,
      study,
      site,
      "Autologous chain-of-custody records could not be loaded."
    );
  }
}

async function loadKitsSubpanel(study: string, site: string) {
  kitsError.value = "";

  if (!canReconcile.value || !site) {
    kitInventory.value = [];
    return;
  }

  try {
    kitInventory.value =
      (await executionApi.getSiteIpKitInventory(study, site)) || [];
  } catch (err) {
    kitsError.value = formatSubpanelError(
      err,
      study,
      site,
      "Site IP kit inventory could not be loaded."
    );
  }
}

async function loadSchemesSubpanel(study: string, site: string) {
  schemesError.value = "";

  if (!canReadSchemes.value && !canManageSchemes.value) {
    schemes.value = [];
    return;
  }

  try {
    schemes.value = (await executionApi.listRandomizationSchemes(study)) || [];
  } catch (err) {
    schemesError.value = formatSubpanelError(
      err,
      study,
      site,
      "Randomization schemes could not be loaded."
    );
  }
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

  try {
    await Promise.all([
      loadCgtSubpanel(study, site, subject),
      loadKitsSubpanel(study, site),
      loadSchemesSubpanel(study, site),
    ]);
    loaded.value = true;
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
    saving.value = false;
    void draftSchemeHandoff.handoff([
      created?.id ? `[id="btn-approve-scheme-${created.id}"]` : null,
      "#btn-toggle-draft-scheme",
      "#rtsm-supply-title",
    ]);
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
    saving.value = false;
    void handoffFocus(["#btn-toggle-draft-scheme", "#rtsm-supply-title"]);
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
  font-variant-numeric: tabular-nums;
}

.kit-toolbar {
  font-variant-numeric: tabular-nums;
}

#rtsm-subject,
#cgt-aph-subject,
#cgt-aph-din,
#seed-kit-number,
#seed-lot-number,
#rtsm-kits-panel td:nth-child(1),
#rtsm-kits-panel td:nth-child(2),
#rtsm-kits-panel td:nth-child(4) {
  font-family: var(--semantic-font-mono);
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
