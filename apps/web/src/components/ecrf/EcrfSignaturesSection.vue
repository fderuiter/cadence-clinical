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
          signature.superseded
            ? 'ecrf-signature--superseded'
            : 'ecrf-signature--current',
        ]"
        :data-signature="signature.attestation_id"
      >
        <div>
          <strong>{{ signaturesState.meaningLabel(signature.meaning) }}</strong>
          by {{ signature.signed_by }} at {{ signature.signed_at }}:
          {{ signature.reason }}
          <span
            v-if="signature.superseded"
            class="ecrf-signature-state ecrf-signature-state--superseded"
            data-testid="ecrf-signature-superseded"
            >Superseded by a later correction</span
          >
          <span v-else class="ecrf-signature-state">Current</span>
        </div>
        <div
          v-if="signature.superseded && signature.signed_values?.length"
          class="ecrf-signed-values-scroll"
        >
          <table class="ecrf-signed-values" data-testid="ecrf-signed-values">
            <caption>
              What this signature signed, beside the current values
            </caption>
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col">Signed value</th>
                <th scope="col">Signed revision</th>
                <th scope="col">Current value</th>
                <th scope="col">Current revision</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="signed in signature.signed_values"
                :key="signed.item_id"
                :data-signed-item="signed.item_id"
                :class="{
                  'ecrf-signed-changed': signaturesState.changedSince(signed),
                }"
              >
                <th scope="row" class="ecrf-signed-item-code">
                  {{ signed.item_id }}
                </th>
                <td>{{ formatObservationValue(signed.value) }}</td>
                <td>
                  <component
                    :is="routerLinkTag"
                    v-if="
                      itemObservationLink(signed.observation_id, signed.item_id)
                    "
                    class="ecrf-observation-link"
                    :to="
                      itemObservationLink(
                        signed.observation_id,
                        signed.item_id
                      )!
                    "
                  >
                    {{ signed.revision }}
                  </component>
                  <template v-else>
                    {{ signed.revision }}
                  </template>
                </td>
                <td>
                  {{ signaturesState.currentValueText(signed.item_id) }}
                </td>
                <td>
                  {{
                    signaturesState.currentOf(signed.item_id)?.revision ??
                    "None"
                  }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </li>
    </ul>

    <div v-if="signaturesState.hasSignAuthority" class="ecrf-form-row">
      <label for="ecrf-sign-meaning">Meaning</label>
      <select
        id="ecrf-sign-meaning"
        :value="signaturesState.signMeaning"
        :disabled="signing"
        @change="
          signaturesState.signMeaning = ($event.target as HTMLSelectElement)
            .value as EcrfSignatureMeaning
        "
      >
        <option value="approved">Approved</option>
        <option value="responsibility">Responsibility</option>
      </select>
    </div>

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
        Form signed: {{ signaturesState.meaningLabel(signOutcome.meaning) }}.
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
    v-if="signaturesState.showSignModal"
    :is-open="signaturesState.showSignModal"
    :username="signaturesState.signerUsername"
    :signer-name="signaturesState.signerFullName"
    signer-role="Investigator"
    :reasons="signaturesState.ECRF_INVESTIGATOR_REASONS"
    :action-url="formSignatureAction(form.form_id)"
    :semantic-action="FORM_SIGN_SEMANTIC_ACTION"
    :on-sign="signaturesState.signWithToken"
    @cancel="signaturesState.showSignModal = false"
    @success="signaturesState.showSignModal = false"
  />
</template>

<script setup lang="ts">
import {
  getCurrentInstance,
  reactive,
  toRef,
  type UnwrapNestedRefs,
} from "vue";
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
  signatures?: UnwrapNestedRefs<ReturnType<typeof useEcrfSignatures>>;
}>();

const emit = defineEmits<{
  (e: "reload-stale"): void;
}>();

const instance = getCurrentInstance();
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const localSignatures = reactive(
  useEcrfSignatures({
    form: toRef(props, "form"),
    items: toRef(props, "items"),
    dirty: toRef(props, "dirty"),
    signing: toRef(props, "signing"),
    sign: (meaning, sigToken, signingReason) =>
      props.sign(meaning, sigToken, signingReason),
  })
);

const signaturesState = props.signatures ?? localSignatures;
</script>

<style scoped>
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-signatures {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.85rem;
}
.ecrf-signature {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  min-width: 0;
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
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
}
.ecrf-signed-values-scroll {
  overflow-x: auto;
  max-width: 100%;
}
.ecrf-signed-values {
  margin-top: 6px;
  border-collapse: collapse;
  font-size: 0.8rem;
  width: 100%;
}
.ecrf-signed-values caption {
  text-align: left;
  font-weight: 600;
  padding-bottom: 4px;
}
.ecrf-signed-values th,
.ecrf-signed-values td {
  border-bottom: 1px solid var(--border);
  padding: 4px 6px;
  text-align: left;
}
.ecrf-signed-item-code {
  font-family: var(--font-mono);
}
.ecrf-signed-changed {
  background-color: var(--semantic-color-warning-bg-soft);
}
.ecrf-form-outcome {
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.ecrf-form-outcome--saved {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.ecrf-form-outcome--failed {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
</style>
