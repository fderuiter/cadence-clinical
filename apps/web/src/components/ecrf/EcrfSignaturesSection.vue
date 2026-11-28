<template>
  <section
    id="ecrf-form-signatures-section"
    aria-labelledby="ecrf-form-signatures-title"
  >
    <h3 id="ecrf-form-signatures-title">Signatures</h3>
    <p v-if="form.signatures.length === 0" data-testid="ecrf-no-signatures">
      Not signed. Signing records a kernel attestation over the form's current
      values; a later correction to a signed value supersedes the signature.
    </p>
    <ul v-else class="ecrf-signatures" data-testid="ecrf-form-signatures">
      <li
        v-for="signature in form.signatures"
        :key="signature.attestation_id"
        :class="[
          'ecrf-signature',
          'tabular-nums',
          signature.superseded
            ? 'ecrf-signature--superseded'
            : 'ecrf-signature--current',
        ]"
        :data-signature="signature.attestation_id"
      >
        <div>
          <strong>{{ meaningLabel(signature.meaning) }}</strong> by
          {{ signature.signed_by }} at {{ signature.signed_at }}:
          {{ signature.reason }}
          <span
            v-if="signature.superseded"
            class="ecrf-signature-state ecrf-signature-state--superseded"
            data-testid="ecrf-signature-superseded"
            >Superseded by a later correction</span
          >
          <span v-else class="ecrf-signature-state">Current</span>
        </div>
        <ClinicalDataTable
          v-if="signature.superseded && signature.signed_values?.length"
          plain
          hide-header
          caption-visible
          density="compact"
          :interactive-rows="false"
          :page-size="Math.max(signature.signed_values.length, 50)"
          title="What this signature signed, beside the current values"
          :aria-label="`Superseded signed values (${meaningLabel(signature.meaning)} by ${signature.signed_by})`"
          wrapper-class="ecrf-table-scroll ecrf-signed-values-scroll"
          wrapper-test-id="ecrf-signed-values-scroll"
          table-class="ecrf-signed-values"
          table-test-id="ecrf-signed-values"
          :columns="signedValueColumns"
          :data="signature.signed_values"
          row-key="item_id"
          :row-attrs="(signed) => ({ 'data-signed-item': signed.item_id })"
          :row-class="
            (signed) => ({ 'ecrf-signed-changed': changedSince(signed) })
          "
        >
          <template #cell-item_id="{ row: signed }">
            {{ signed.item_id }}
          </template>
          <template #cell-signed_value="{ row: signed }">
            {{ formatObservationValue(signed.value) }}
          </template>
          <template #cell-signed_revision="{ row: signed }">
            <component
              :is="routerLinkTag"
              v-if="itemObservationLink(signed.observation_id, signed.item_id)"
              class="ecrf-observation-link"
              :to="itemObservationLink(signed.observation_id, signed.item_id)!"
            >
              {{ signed.revision }}
            </component>
            <template v-else>
              {{ signed.revision }}
            </template>
          </template>
          <template #cell-current_value="{ row: signed }">
            {{ currentValueText(signed.item_id) }}
          </template>
          <template #cell-current_revision="{ row: signed }">
            {{ currentOf(signed.item_id)?.revision ?? "None" }}
          </template>
        </ClinicalDataTable>
      </li>
    </ul>

    <div v-if="hasSignAuthority" class="ecrf-form-row">
      <label for="ecrf-sign-meaning">Meaning</label>
      <select id="ecrf-sign-meaning" v-model="signMeaning" :disabled="signing">
        <option value="approved">Approved</option>
        <option value="responsibility">Responsibility</option>
      </select>
      <button
        id="ecrf-form-sign"
        class="btn btn-primary"
        type="button"
        :disabled="!canSign"
        :aria-describedby="signHint ? 'ecrf-sign-hint' : undefined"
        @click="showSignModal = true"
      >
        {{ signing ? "Signing…" : "Sign form" }}
      </button>
      <span
        v-if="signHint"
        id="ecrf-sign-hint"
        class="ecrf-form-hint"
        data-testid="ecrf-sign-hint"
        >{{ signHint }}</span
      >
    </div>
    <p
      v-else
      id="ecrf-sign-unauthorized"
      class="ecrf-form-hint"
      data-testid="ecrf-sign-unauthorized"
    >
      Only an authorised investigator (collection.sign) may sign this form.
    </p>

    <div
      v-if="signOutcome"
      data-testid="ecrf-sign-outcome"
      :class="[
        'ecrf-form-outcome',
        `ecrf-form-outcome--${signOutcome.kind === 'signed' ? 'saved' : 'failed'}`,
      ]"
      :role="signOutcome.kind === 'signed' ? 'status' : 'alert'"
    >
      <template v-if="signOutcome.kind === 'signed'">
        Form signed: {{ meaningLabel(signOutcome.meaning) }}.
      </template>
      <template v-else>
        {{ signOutcome.message }}
      </template>
      <button
        v-if="
          signOutcome.kind === 'changed' || signOutcome.kind === 'not_ready'
        "
        id="ecrf-sign-reload"
        class="btn btn-secondary"
        type="button"
        @click="emit('reload-stale')"
      >
        Reload form
      </button>
    </div>
  </section>

  <SignatureCaptureModal
    v-if="showSignModal"
    :is-open="showSignModal"
    :username="signerUsername"
    :signer-name="signerFullName"
    signer-role="Investigator"
    :reasons="ECRF_INVESTIGATOR_REASONS"
    :action-url="formSignatureAction(form.form_id)"
    :semantic-action="FORM_SIGN_SEMANTIC_ACTION"
    :on-sign="signWithToken"
    @cancel="showSignModal = false"
    @success="showSignModal = false"
  />
</template>

<script setup lang="ts">
import { getCurrentInstance, toRef } from "vue";
import { ClinicalDataTable } from "ui";
import type { RouteLocationRaw } from "vue-router";
import { formatObservationValue } from "../../api/observationValue";
import {
  FORM_SIGN_SEMANTIC_ACTION,
  formSignatureAction,
  type EcrfForm,
  type EcrfFormItem,
  type EcrfSignatureMeaning,
} from "../../api/ecrfFormsApi";
import type { EcrfSignOutcome } from "../../composables/useEcrfForm";
import { useEcrfSignatures } from "../../composables/useEcrfSignatures";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

const props = defineProps<{
  form: EcrfForm;
  items: EcrfFormItem[];
  dirty: boolean;
  signing: boolean;
  canSign: boolean;
  signOutcome: EcrfSignOutcome | null;
  sign: (
    meaning: EcrfSignatureMeaning,
    sigToken: string,
    signingReason: string
  ) => Promise<EcrfSignOutcome | null | undefined>;
  itemObservationLink: (
    observationId: string | null | undefined,
    itemId?: string | null
  ) => RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "reload-stale"): void;
}>();

const instance = getCurrentInstance();
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const signedValueColumns = [
  {
    key: "item_id",
    label: "Item",
    rowHeader: true,
    sticky: "left" as const,
    cardRole: "identifier" as const,
    mono: true,
  },
  { key: "signed_value", label: "Signed value", mono: true },
  { key: "signed_revision", label: "Signed revision", mono: true },
  { key: "current_value", label: "Current value", mono: true },
  { key: "current_revision", label: "Current revision", mono: true },
];

const {
  ECRF_INVESTIGATOR_REASONS,
  signMeaning,
  showSignModal,
  hasSignAuthority,
  signerUsername,
  signerFullName,
  meaningLabel,
  signHint,
  currentOf,
  currentValueText,
  changedSince,
  signWithToken,
} = useEcrfSignatures({
  form: toRef(props, "form"),
  items: toRef(props, "items"),
  dirty: toRef(props, "dirty"),
  signing: toRef(props, "signing"),
  sign: (meaning, sigToken, signingReason) =>
    props.sign(meaning, sigToken, signingReason),
});
</script>

<style scoped>
.ecrf-form-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
#ecrf-form-signatures-section {
  min-width: 0;
  max-width: 100%;
}
.ecrf-signatures {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
  min-width: 0;
  max-width: 100%;
}
:deep(.ecrf-signed-values th[scope="row"]),
:deep(.ecrf-signed-values td) {
  font-family: var(--semantic-font-mono);
}
.ecrf-signature {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
  box-sizing: border-box;
}
.ecrf-signature--superseded {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
}
.ecrf-signature-state {
  margin-left: 8px;
  padding: 0 8px;
  border-radius: 999px;
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-signature-state--superseded {
  background-color: var(--semantic-badge-warning-bg);
  color: var(--semantic-badge-warning-text);
}
:deep(.ecrf-signed-values-scroll) {
  position: relative;
  overflow-x: auto;
  max-width: 100%;
  min-width: 0;
  -webkit-overflow-scrolling: touch;
  padding: 4px;
  margin-top: 6px;
}
:deep(.ecrf-signed-values-scroll:focus-visible) {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
  border-radius: 4px;
}
@media (min-width: 481px) {
  :deep(table.ecrf-signed-values) {
    min-width: 34rem;
  }
}
:deep(.ecrf-signed-changed) {
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-text);
}
</style>

<style scoped src="./ecrfFormBanners.css"></style>
