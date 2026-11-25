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
          <span class="ecrf-signature-actor">{{ signature.signed_by }}</span>
          at
          <span class="ecrf-signature-timestamp">{{
            signature.signed_at
          }}</span>:
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
          class="ecrf-table-scroll ecrf-signed-values-scroll scrollable-table-wrapper"
          data-testid="ecrf-signed-values-scroll"
          role="region"
          :aria-label="`Superseded signed values (${meaningLabel(signature.meaning)} by ${signature.signed_by})`"
          tabindex="0"
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
                  'ecrf-signed-changed': changedSince(signed),
                }"
              >
                <th scope="row">
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
                <td>{{ currentValueText(signed.item_id) }}</td>
                <td>{{ currentOf(signed.item_id)?.revision ?? "None" }}</td>
              </tr>
            </tbody>
          </table>
        </div>
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
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-secondary);
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
#ecrf-sign-meaning {
  font-size: var(--font-size-md);
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
  font-size: var(--font-size-sm);
  font-variant-numeric: tabular-nums;
  min-width: 0;
  max-width: 100%;
}
.ecrf-signature-actor,
.ecrf-signature-timestamp,
.ecrf-signed-values th[scope="row"],
.ecrf-signed-values td {
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
  font-size: var(--font-size-2xs);
  font-weight: 600;
}
.ecrf-signature-state--superseded {
  background-color: var(--semantic-badge-warning-bg);
  color: var(--semantic-badge-warning-text);
}
.ecrf-signed-values-scroll {
  position: relative;
  overflow-x: auto;
  max-width: 100%;
  min-width: 0;
  -webkit-overflow-scrolling: touch;
  padding: 4px;
  margin-top: 6px;
}
.ecrf-signed-values-scroll:focus-visible {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
  border-radius: 4px;
}
.ecrf-signed-values {
  width: 100%;
  min-width: 34rem;
  border-collapse: collapse;
  font-size: var(--font-size-xs);
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
.ecrf-signed-changed {
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-text);
}
</style>

<style scoped src="./ecrfFormBanners.css"></style>
