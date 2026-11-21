<template>
  <ClinicalDrawer
    id="randomize-participant-drawer"
    :is-open="isOpen"
    title="Randomize participant"
    :subtitle="`${participant.subject_id} · ${studyCode}`"
    size="md"
    :dismissible="!submitting"
    @close="onClose"
  >
    <p v-if="loadingScheme" id="randomize-loading" class="note" role="status">
      Loading approved randomization scheme for {{ studyCode }}...
    </p>

    <div
      v-else-if="receipt"
      id="randomization-receipt"
      class="receipt-card"
      role="status"
    >
      <h3>Allocation recorded</h3>
      <dl class="receipt-details">
        <dt>Participant</dt>
        <dd>{{ participant.subject_id }}</dd>
        <dt>Treatment allocation</dt>
        <dd id="randomization-receipt-arm">Randomized, arm masked</dd>
        <dt>Assigned kit</dt>
        <dd id="randomization-receipt-kit">
          {{ receipt.kit_number || "No kit required by scheme" }}
        </dd>
        <dt>Stratum</dt>
        <dd id="randomization-receipt-stratum">{{ receipt.stratum_key }}</dd>
        <dt>Scheme version</dt>
        <dd>v{{ receipt.scheme_version }}</dd>
      </dl>
      <button
        id="btn-close-randomize-receipt"
        type="button"
        class="btn btn-primary"
        @click="onClose"
      >
        Done
      </button>
    </div>

    <div
      v-else-if="!approvedScheme"
      id="randomize-no-scheme"
      class="note"
      role="status"
    >
      <p>
        No approved randomization scheme is active for
        <strong>{{ studyCode }}</strong>. An unblinded statistician must draft a
        scheme and a second unblinded statistician must approve it under Start →
        Randomization schemes before participants can be randomized.
      </p>
    </div>

    <form
      v-else
      id="randomize-participant-form"
      class="randomize-form"
      @submit.prevent="submit"
    >
      <p class="scheme-banner">
        Active scheme <strong>v{{ approvedScheme.version }}</strong> ·
        {{
          approvedScheme.method === "minimization"
            ? "Minimization"
            : "Permuted block"
        }}
        ·
        {{
          approvedScheme.requires_kit
            ? "Assigns site supply kit"
            : "No kit assignment"
        }}
      </p>

      <fieldset v-if="factorEntries.length" class="fieldset">
        <legend>Stratification levels</legend>
        <div
          v-for="[factorName, levels] in factorEntries"
          :key="factorName"
          class="field"
        >
          <label :for="`randomize-factor-${factorName}`">{{
            factorName
          }}</label>
          <select
            :id="`randomize-factor-${factorName}`"
            v-model="selectedFactors[factorName]"
          >
            <option v-for="level in levels" :key="level" :value="level">
              {{ level }}
            </option>
          </select>
        </div>
      </fieldset>
      <p v-else class="note">This randomization scheme is unstratified.</p>

      <div class="field">
        <label for="randomize-reason">Clinical reason</label>
        <textarea
          id="randomize-reason"
          v-model="reason"
          rows="2"
          placeholder="Eligibility and signed informed consent verified prior to randomization"
        />
      </div>

      <fieldset class="fieldset">
        <legend>Electronic signature (21 CFR Part 11)</legend>
        <div class="field">
          <label for="randomize-password">Password</label>
          <input
            id="randomize-password"
            v-model="password"
            v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
          />
        </div>
        <div class="field">
          <label for="randomize-totp"
            >One-time verification code (optional)</label
          >
          <input
            id="randomize-totp"
            v-model="totp"
            type="text"
            autocomplete="one-time-code"
          />
        </div>
      </fieldset>

      <p
        v-if="errorMessage"
        id="randomize-error"
        class="error-note"
        role="alert"
      >
        {{ errorMessage }}
      </p>

      <div class="drawer-actions">
        <button
          id="btn-confirm-randomize"
          type="submit"
          class="btn btn-primary"
          :disabled="submitting || !canSubmit"
        >
          {{ submitting ? "Signing & randomizing..." : "Sign & randomize" }}
        </button>
        <button
          id="btn-cancel-randomize"
          type="button"
          class="btn btn-secondary"
          :disabled="submitting"
          @click="onClose"
        >
          Cancel
        </button>
      </div>
    </form>
  </ClinicalDrawer>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  ClinicalDrawer,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
  useEscapeClose,
} from "ui";
import {
  executionApi,
  type RandomizationResult,
  type RandomizationScheme,
  type SubjectSummary,
} from "../../api/executionApi";
import {
  RANDOMIZE_SUBJECT_SEMANTIC_ACTION,
  randomizeSubjectSignatureAction,
} from "../../canvas/rtsm";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

const props = defineProps<{
  isOpen: boolean;
  studyCode: string;
  participant: SubjectSummary;
  username?: string;
}>();

const emit = defineEmits<{
  close: [];
  randomized: [result: RandomizationResult];
}>();

const canvas = useStudyCanvasStore();

const loadingScheme = ref(false);
const approvedScheme = ref<RandomizationScheme | null>(null);
const selectedFactors = ref<Record<string, string>>({});
const reason = ref("");
const password = ref("");
const totp = ref("");
const submitting = ref(false);
const errorMessage = ref("");
const receipt = ref<RandomizationResult | null>(null);

useEscapeClose(onClose, {
  isOpen: () => props.isOpen && !submitting.value,
});

const factorEntries = computed<[string, string[]][]>(() => {
  const factors = approvedScheme.value?.stratification_factors;
  if (!factors) return [];
  return Object.entries(factors);
});

const canSubmit = computed(() => {
  if (!approvedScheme.value) return false;
  if (!reason.value.trim() || !password.value) return false;
  for (const [factorName] of factorEntries.value) {
    if (!selectedFactors.value[factorName]) return false;
  }
  return true;
});

async function loadApprovedScheme() {
  if (
    !props.studyCode ||
    typeof executionApi.listRandomizationSchemes !== "function"
  ) {
    approvedScheme.value = null;
    return;
  }
  loadingScheme.value = true;
  errorMessage.value = "";
  try {
    const schemes = await executionApi.listRandomizationSchemes(
      props.studyCode
    );
    const active =
      schemes.find(
        (item) => String(item.status || "").toUpperCase() === "APPROVED"
      ) ?? null;
    approvedScheme.value = active;
    const defaults: Record<string, string> = {};
    if (active?.stratification_factors) {
      for (const [name, levels] of Object.entries(
        active.stratification_factors
      )) {
        defaults[name] = levels[0] ?? "";
      }
    }
    selectedFactors.value = defaults;
  } catch (err) {
    approvedScheme.value = null;
    errorMessage.value =
      err instanceof Error
        ? err.message
        : "Couldn't load the study's randomization scheme.";
  } finally {
    loadingScheme.value = false;
  }
}

watch(
  () => props.isOpen,
  (open) => {
    if (open) {
      receipt.value = null;
      reason.value = "";
      password.value = "";
      totp.value = "";
      errorMessage.value = "";
      loadApprovedScheme();
    }
  },
  { immediate: true }
);

function onClose() {
  if (submitting.value) return;
  password.value = "";
  totp.value = "";
  emit("close");
}

async function submit() {
  if (!canSubmit.value) return;
  submitting.value = true;
  errorMessage.value = "";
  try {
    const token = await executionApi.requestSignatureToken({
      password: password.value,
      totp: totp.value.trim() || undefined,
      username: props.username || undefined,
      action: randomizeSubjectSignatureAction(props.participant.id),
      semanticAction: RANDOMIZE_SUBJECT_SEMANTIC_ACTION,
    });
    password.value = "";
    totp.value = "";

    const result = await canvas.randomizeParticipant(props.participant, {
      stratificationFactors: { ...selectedFactors.value },
      reason: reason.value.trim(),
      sigToken: token.sig_token,
    });
    if (!result) {
      errorMessage.value =
        "Execution refused randomization. Verify signed consent for the active protocol version and available site kit stock.";
      return;
    }
    receipt.value = result;
    emit("randomized", result);
  } catch (err) {
    password.value = "";
    errorMessage.value =
      err instanceof Error ? err.message : "Randomization failed.";
  } finally {
    submitting.value = false;
  }
}
</script>

<style scoped>
.randomize-form,
.receipt-card {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.scheme-banner {
  margin: 0;
  padding: 10px 12px;
  border-radius: 6px;
  background: var(--neutral-light);
  border: 1px solid var(--border);
  font-size: 0.88rem;
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

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.field input,
.field select,
.field textarea {
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-card);
  color: var(--text-main);
}

.drawer-actions {
  display: flex;
  gap: 8px;
}

.receipt-card {
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--success-bg);
}

.receipt-card h3 {
  margin: 0;
  font-size: 1rem;
  color: var(--success);
}

.receipt-details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 6px 12px;
  margin: 0;
}

.receipt-details dt {
  color: var(--neutral-dark);
  font-weight: 600;
}

.receipt-details dd {
  margin: 0;
}

.note {
  margin: 0;
  color: var(--neutral-dark);
  font-size: 0.9rem;
}

.error-note {
  margin: 0;
  color: var(--danger);
  font-size: 0.85rem;
}
</style>
