<template>
  <section
    id="randomization-schemes-section"
    class="schemes-section"
    aria-labelledby="randomization-schemes-title"
    :aria-busy="loading"
  >
    <header class="schemes-header">
      <div>
        <h2 id="randomization-schemes-title">Randomization schemes</h2>
        <p class="section-subtitle">
          Allocation designs and stratification schedules for {{ studyCode }}.
          Drafting and signed approval require two distinct unblinded
          statisticians.
        </p>
      </div>
      <button
        v-if="canManageSchemes && !drafting"
        id="btn-draft-scheme"
        type="button"
        class="btn btn-secondary"
        @click="openDraftForm"
      >
        Draft scheme
      </button>
    </header>

    <form
      v-if="canManageSchemes && drafting"
      id="draft-scheme-form"
      class="card draft-form"
      aria-labelledby="draft-scheme-heading"
      @submit.prevent="submitDraft"
    >
      <h3 id="draft-scheme-heading">Draft randomization scheme</h3>

      <div class="field-row">
        <div class="field">
          <label for="scheme-method">Allocation method</label>
          <select id="scheme-method" v-model="method">
            <option value="permuted_block">Permuted block</option>
            <option value="minimization">Minimization</option>
          </select>
        </div>

        <template v-if="method === 'permuted_block'">
          <div class="field">
            <label for="scheme-block-sizes"
              >Block sizes (comma-separated)</label
            >
            <input
              id="scheme-block-sizes"
              v-model="blockSizesInput"
              type="text"
              autocomplete="off"
            />
          </div>
          <div class="field">
            <label for="scheme-slots-per-stratum">Slots per stratum</label>
            <input
              id="scheme-slots-per-stratum"
              v-model.number="slotsPerStratum"
              type="number"
              min="2"
            />
          </div>
        </template>

        <div v-else class="field">
          <label for="scheme-preferred-prob">Preferred arm probability</label>
          <input
            id="scheme-preferred-prob"
            v-model.number="preferredArmProbability"
            type="number"
            min="0.5"
            max="1"
            step="0.05"
          />
        </div>
      </div>

      <fieldset class="fieldset">
        <legend>Treatment arms &amp; allocation ratios</legend>
        <div
          v-for="(arm, index) in armRows"
          :key="`arm-${index}`"
          class="inline-row"
        >
          <div class="field">
            <label :for="`scheme-arm-code-${index}`">Arm code</label>
            <input
              :id="`scheme-arm-code-${index}`"
              v-model="arm.code"
              type="text"
              autocomplete="off"
              placeholder="e.g. ACTIVE"
            />
          </div>
          <div class="field field-narrow">
            <label :for="`scheme-arm-ratio-${index}`">Ratio</label>
            <input
              :id="`scheme-arm-ratio-${index}`"
              v-model.number="arm.ratio"
              type="number"
              min="1"
            />
          </div>
          <button
            v-if="armRows.length > 2"
            :id="`btn-remove-arm-${index}`"
            type="button"
            class="btn btn-secondary"
            :aria-label="`Remove arm ${index + 1}`"
            @click="armRows.splice(index, 1)"
          >
            Remove
          </button>
        </div>
        <button
          id="btn-add-arm"
          type="button"
          class="btn btn-secondary btn-self-start"
          @click="armRows.push({ code: '', ratio: 1 })"
        >
          Add arm
        </button>
      </fieldset>

      <fieldset class="fieldset">
        <legend>Stratification factors</legend>
        <div
          v-for="(factor, index) in factorRows"
          :key="`factor-${index}`"
          class="inline-row"
        >
          <div class="field">
            <label :for="`scheme-factor-name-${index}`">Factor name</label>
            <input
              :id="`scheme-factor-name-${index}`"
              v-model="factor.name"
              type="text"
              autocomplete="off"
              placeholder="e.g. AGE_GROUP"
            />
          </div>
          <div class="field field-grow">
            <label :for="`scheme-factor-levels-${index}`"
              >Levels (comma-separated)</label
            >
            <input
              :id="`scheme-factor-levels-${index}`"
              v-model="factor.levels"
              type="text"
              autocomplete="off"
              placeholder="e.g. ADULT, SENIOR"
            />
          </div>
          <button
            :id="`btn-remove-factor-${index}`"
            type="button"
            class="btn btn-secondary"
            :aria-label="`Remove factor ${index + 1}`"
            @click="factorRows.splice(index, 1)"
          >
            Remove
          </button>
        </div>
        <button
          id="btn-add-factor"
          type="button"
          class="btn btn-secondary btn-self-start"
          @click="factorRows.push({ name: '', levels: '' })"
        >
          Add stratification factor
        </button>
      </fieldset>

      <label for="scheme-requires-kit" class="checkbox-row">
        <input id="scheme-requires-kit" v-model="requiresKit" type="checkbox" />
        <span>Assign an investigational supply kit on randomization</span>
      </label>

      <div class="field">
        <label for="scheme-draft-reason">Reason for drafting</label>
        <input
          id="scheme-draft-reason"
          v-model="draftReason"
          type="text"
          autocomplete="off"
          placeholder="Protocol randomization schedule specification"
        />
      </div>

      <p
        v-if="draftError"
        id="draft-scheme-error"
        class="error-note"
        role="alert"
      >
        {{ draftError }}
      </p>

      <div class="form-actions">
        <button
          id="btn-submit-draft-scheme"
          type="submit"
          class="btn btn-primary"
          :disabled="submittingDraft || !canSubmitDraft"
        >
          {{ submittingDraft ? "Drafting..." : "Save draft scheme" }}
        </button>
        <button
          id="btn-cancel-draft-scheme"
          type="button"
          class="btn btn-secondary"
          :disabled="submittingDraft"
          @click="drafting = false"
        >
          Cancel
        </button>
      </div>
    </form>

    <p id="schemes-status-line" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <table v-if="schemes.length" id="schemes-table" class="schemes-table">
      <thead>
        <tr>
          <th scope="col">Version</th>
          <th scope="col">Status</th>
          <th scope="col">Method</th>
          <th scope="col">Arms</th>
          <th scope="col">Stratification</th>
          <th scope="col">Supply kit</th>
          <th scope="col">Sign-off</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="scheme in schemes"
          :key="scheme.id"
          :id="`scheme-row-v${scheme.version}`"
        >
          <th scope="row" class="numeric">v{{ scheme.version }}</th>
          <td>
            <span
              class="status-pill"
              :class="`status-${String(scheme.status || '').toLowerCase()}`"
            >
              {{ scheme.status }}
            </span>
          </td>
          <td>{{ formatMethod(scheme.method) }}</td>
          <td>{{ formatArms(scheme.arms) }}</td>
          <td>{{ formatFactors(scheme.stratification_factors) }}</td>
          <td>{{ scheme.requires_kit ? "Required" : "None" }}</td>
          <td>
            <template
              v-if="
                canManageSchemes &&
                String(scheme.status || '').toUpperCase() === 'DRAFT'
              "
            >
              <span
                v-if="isDraftedByCurrentUser(scheme)"
                :id="`scheme-sod-note-${scheme.version}`"
                class="sod-note"
              >
                Drafted by you — requires a second unblinded statistician to
                approve.
              </span>
              <button
                v-else
                :id="`btn-approve-scheme-${scheme.version}`"
                type="button"
                class="btn btn-primary"
                @click="openApproveModal(scheme)"
              >
                Sign &amp; approve v{{ scheme.version }}
              </button>
            </template>
            <span v-else-if="scheme.approved_by" class="meta-note">
              Approved by {{ scheme.approved_by }}
            </span>
            <span v-else class="meta-note">
              Drafted by {{ scheme.drafted_by }}
            </span>
          </td>
        </tr>
      </tbody>
    </table>

    <ClinicalModal
      id="approve-scheme-modal"
      :model-value="Boolean(approvingScheme)"
      title="Approve randomization scheme"
      size="md"
      :dismissible="!approvingBusy"
      @update:model-value="onApproveModalUpdate"
      @close="closeApproveModal"
    >
      <form
        v-if="approvingScheme"
        id="approve-scheme-form"
        class="approve-form"
        @submit.prevent="confirmApprove"
      >
        <p class="approve-summary">
          Approving scheme <strong>v{{ approvingScheme.version }}</strong> for
          <strong>{{ studyCode }}</strong> (hash
          <code>{{ approvingScheme.configuration_hash }}</code>) activates it
          for participant randomization and retires any prior approved version.
        </p>

        <div class="field">
          <label for="approve-scheme-password"
            >Password (electronic signature)</label
          >
          <input
            id="approve-scheme-password"
            v-model="approvePassword"
            type="password"
            autocomplete="current-password"
          />
        </div>

        <div class="field">
          <label for="approve-scheme-totp"
            >One-time verification code (if MFA enabled)</label
          >
          <input
            id="approve-scheme-totp"
            v-model="approveTotp"
            type="text"
            autocomplete="one-time-code"
          />
        </div>

        <div class="field">
          <label for="approve-scheme-reason">Approval reason</label>
          <textarea
            id="approve-scheme-reason"
            v-model="approveReason"
            rows="2"
            placeholder="Independent biostatistical verification of scheme specification"
          />
        </div>

        <p
          v-if="approveError"
          id="approve-scheme-error"
          class="error-note"
          role="alert"
        >
          {{ approveError }}
        </p>
      </form>

      <template #footer>
        <button
          id="btn-cancel-approve-scheme"
          type="button"
          class="btn btn-secondary"
          :disabled="approvingBusy"
          @click="closeApproveModal"
        >
          Cancel
        </button>
        <button
          id="btn-confirm-approve-scheme"
          type="button"
          class="btn btn-primary"
          :disabled="approvingBusy || !approvePassword || !approveReason.trim()"
          @click="confirmApprove"
        >
          {{ approvingBusy ? "Signing & approving..." : "Sign & approve" }}
        </button>
      </template>
    </ClinicalModal>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ClinicalModal, useEscapeClose } from "ui";
import { executionApi, type RandomizationScheme } from "../../api/executionApi";
import {
  APPROVE_SCHEME_SEMANTIC_ACTION,
  approveSchemeSignatureAction,
} from "../../canvas/rtsm";
import { useToastStore } from "../../stores/toasts.js";

const props = defineProps<{
  studyCode: string;
  canManageSchemes?: boolean;
  currentUserId?: string;
  currentUsername?: string;
}>();

const toasts = useToastStore();

const schemes = ref<RandomizationScheme[]>([]);
const loading = ref(false);
const loadError = ref(false);

const drafting = ref(false);
const submittingDraft = ref(false);
const draftError = ref("");
const method = ref<"permuted_block" | "minimization">("permuted_block");
const blockSizesInput = ref("4, 6");
const slotsPerStratum = ref(24);
const preferredArmProbability = ref(0.8);
const requiresKit = ref(true);
const draftReason = ref("");
const armRows = ref<Array<{ code: string; ratio: number }>>([
  { code: "ACTIVE", ratio: 1 },
  { code: "PLACEBO", ratio: 1 },
]);
const factorRows = ref<Array<{ name: string; levels: string }>>([
  { name: "AGE_GROUP", levels: "ADULT, SENIOR" },
]);

const approvingScheme = ref<RandomizationScheme | null>(null);
const approvePassword = ref("");
const approveTotp = ref("");
const approveReason = ref("");
const approveError = ref("");
const approvingBusy = ref(false);

useEscapeClose(closeApproveModal, {
  isOpen: () => Boolean(approvingScheme.value) && !approvingBusy.value,
});

const statusLine = computed(() => {
  if (loading.value) {
    return `Loading randomization schemes for ${props.studyCode}...`;
  }
  if (loadError.value) {
    return `Randomization schemes for ${props.studyCode} couldn't be loaded.`;
  }
  if (!schemes.value.length) {
    return `No randomization schemes configured for ${props.studyCode} yet.`;
  }
  const approved = schemes.value.find(
    (s) => String(s.status || "").toUpperCase() === "APPROVED"
  );
  return approved
    ? `${schemes.value.length} scheme version(s) · Active approved version: v${approved.version}.`
    : `${schemes.value.length} scheme version(s) · None approved yet.`;
});

const parsedArms = computed(() => {
  const result: Record<string, number> = {};
  for (const row of armRows.value) {
    const code = row.code.trim();
    const ratio = Number(row.ratio);
    if (code && Number.isFinite(ratio) && ratio >= 1) {
      result[code] = Math.floor(ratio);
    }
  }
  return result;
});

const canSubmitDraft = computed(
  () =>
    Object.keys(parsedArms.value).length >= 2 &&
    Boolean(draftReason.value.trim())
);

async function loadSchemes() {
  if (
    !props.studyCode ||
    typeof executionApi.listRandomizationSchemes !== "function"
  ) {
    schemes.value = [];
    return;
  }
  loading.value = true;
  loadError.value = false;
  try {
    schemes.value = await executionApi.listRandomizationSchemes(
      props.studyCode
    );
  } catch (err) {
    loadError.value = true;
    toasts.apiError("Couldn't load randomization schemes", err);
  } finally {
    loading.value = false;
  }
}

watch(() => props.studyCode, loadSchemes, { immediate: true });

function openDraftForm() {
  draftError.value = "";
  drafting.value = true;
}

function isDraftedByCurrentUser(scheme: RandomizationScheme): boolean {
  const author = String(scheme.drafted_by || "")
    .trim()
    .toLowerCase();
  if (!author) return false;
  const userId = String(props.currentUserId || "")
    .trim()
    .toLowerCase();
  const username = String(props.currentUsername || "")
    .trim()
    .toLowerCase();
  return (
    (Boolean(userId) && author === userId) ||
    (Boolean(username) && author === username)
  );
}

function formatMethod(raw: string): string {
  return raw === "minimization" ? "Minimization" : "Permuted block";
}

function formatArms(arms: Record<string, number | string> | undefined): string {
  if (!arms) return "—";
  return Object.entries(arms)
    .map(([code, ratio]) => `${code} (${ratio})`)
    .join(", ");
}

function formatFactors(factors: Record<string, string[]> | undefined): string {
  if (!factors || !Object.keys(factors).length) return "Unstratified";
  return Object.entries(factors)
    .map(([name, levels]) => `${name}: ${levels.join(" / ")}`)
    .join("; ");
}

async function submitDraft() {
  if (!canSubmitDraft.value) return;
  draftError.value = "";
  submittingDraft.value = true;

  const stratificationFactors: Record<string, string[]> = {};
  for (const row of factorRows.value) {
    const name = row.name.trim();
    const levels = row.levels
      .split(",")
      .map((item) => item.trim())
      .filter(Boolean);
    if (name && levels.length >= 2) {
      stratificationFactors[name] = levels;
    }
  }

  const blockSizes = blockSizesInput.value
    .split(",")
    .map((part) => Number(part.trim()))
    .filter((num) => Number.isFinite(num) && num >= 2);

  try {
    const created = await executionApi.draftRandomizationScheme(
      {
        study_id: props.studyCode,
        method: method.value,
        arms: parsedArms.value,
        stratification_factors: stratificationFactors,
        block_sizes:
          method.value === "permuted_block" && blockSizes.length
            ? blockSizes
            : undefined,
        slots_per_stratum:
          method.value === "permuted_block" ? slotsPerStratum.value : undefined,
        preferred_arm_probability:
          method.value === "minimization"
            ? preferredArmProbability.value
            : undefined,
        requires_kit: requiresKit.value,
      },
      draftReason.value.trim()
    );
    toasts.push({
      tone: "success",
      title: `Randomization scheme v${created.version} drafted`,
      message: "A second unblinded statistician can now sign and approve it.",
    });
    draftReason.value = "";
    drafting.value = false;
    await loadSchemes();
  } catch (err) {
    draftError.value =
      err instanceof Error ? err.message : "Scheme could not be drafted.";
    toasts.apiError("Randomization scheme was not drafted", err);
  } finally {
    submittingDraft.value = false;
  }
}

function openApproveModal(scheme: RandomizationScheme) {
  approvingScheme.value = scheme;
  approvePassword.value = "";
  approveTotp.value = "";
  approveReason.value = "";
  approveError.value = "";
}

function closeApproveModal() {
  if (approvingBusy.value) return;
  approvingScheme.value = null;
  approvePassword.value = "";
  approveTotp.value = "";
  approveReason.value = "";
  approveError.value = "";
}

function onApproveModalUpdate(open: boolean) {
  if (!open) closeApproveModal();
}

async function confirmApprove() {
  const scheme = approvingScheme.value;
  const why = approveReason.value.trim();
  const pwd = approvePassword.value;
  if (!scheme || !why || !pwd) return;

  approvingBusy.value = true;
  approveError.value = "";
  try {
    const token = await executionApi.requestSignatureToken({
      password: pwd,
      totp: approveTotp.value.trim() || undefined,
      username: props.currentUsername || undefined,
      action: approveSchemeSignatureAction(scheme.id),
      semanticAction: APPROVE_SCHEME_SEMANTIC_ACTION,
    });
    approvePassword.value = "";
    approveTotp.value = "";

    const approved = await executionApi.approveRandomizationScheme(scheme.id, {
      reason: why,
      sigToken: token.sig_token,
    });
    toasts.push({
      tone: "success",
      title: `Randomization scheme v${approved.version} approved`,
      message: `Active for ${props.studyCode}.`,
    });
    approvingScheme.value = null;
    await loadSchemes();
  } catch (err) {
    approvePassword.value = "";
    approveError.value =
      err instanceof Error ? err.message : "Scheme approval failed.";
    toasts.apiError("Randomization scheme was not approved", err);
  } finally {
    approvingBusy.value = false;
  }
}
</script>

<style scoped>
.schemes-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.schemes-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.schemes-header h2 {
  margin: 0;
  font-size: 1.15rem;
}

.section-subtitle,
.status-line,
.meta-note {
  margin: 4px 0 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.draft-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 16px;
}

.draft-form h3 {
  margin: 0;
  font-size: 1rem;
}

.field-row,
.inline-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-narrow {
  max-width: 110px;
}

.field-grow {
  flex: 1 1 220px;
}

.field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.field input,
.field select,
.field textarea {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-card);
  color: var(--text-main);
}

.fieldset {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.fieldset legend {
  padding: 0 6px;
  font-size: 0.85rem;
  font-weight: 600;
}

.checkbox-row {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9rem;
}

.btn-self-start {
  align-self: flex-start;
}

.form-actions {
  display: flex;
  gap: 8px;
}

.schemes-table {
  width: 100%;
  border-collapse: collapse;
}

.schemes-table th,
.schemes-table td {
  padding: 10px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
  vertical-align: middle;
  font-size: 0.9rem;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  border: 1px solid var(--border);
  background: var(--neutral-light);
}

.status-approved {
  background: var(--success-bg);
  color: var(--success);
}

.status-draft {
  background: var(--warning-bg);
  color: var(--warning);
}

.sod-note {
  color: var(--neutral-dark);
  font-size: 0.82rem;
}

.approve-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.approve-summary {
  margin: 0;
  font-size: 0.9rem;
}

.error-note {
  margin: 0;
  color: var(--danger);
  font-size: 0.85rem;
}
</style>
