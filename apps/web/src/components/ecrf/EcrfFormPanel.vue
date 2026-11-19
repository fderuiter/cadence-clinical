<template>
  <section id="ecrf-form-instance" class="card ecrf-form-panel">
    <div class="card-title">Case report form</div>

    <!-- Open a new form or reuse one by id (ADR-2622). -->
    <div v-if="!form" class="ecrf-form-open">
      <p class="ecrf-form-hint">
        Each form is opened once for a participant's visit and then saved a page
        at a time. Reuse a form by its id, or open a new one.
      </p>
      <form class="ecrf-form-row" @submit.prevent="reuse">
        <label for="ecrf-form-id">Form id</label>
        <input
          id="ecrf-form-id"
          v-model.trim="reuseId"
          type="text"
          autocomplete="off"
          placeholder="Form id (UUID)"
        />
        <button
          id="ecrf-form-load"
          class="btn btn-secondary"
          type="submit"
          :disabled="loading || !reuseId"
        >
          Load form
        </button>
      </form>

      <form id="ecrf-form-new" class="ecrf-form-grid" @submit.prevent="openNew">
        <label for="ecrf-enrollment-id">Enrollment id</label>
        <input
          id="ecrf-enrollment-id"
          v-model.trim="enrollmentId"
          type="text"
          autocomplete="off"
        />
        <label for="ecrf-collection-spec">Collection spec</label>
        <input
          id="ecrf-collection-spec"
          v-model.trim="collectionSpecId"
          type="text"
          autocomplete="off"
        />
        <label for="ecrf-planned-activity">Visit (planned activity)</label>
        <input
          id="ecrf-planned-activity"
          v-model.trim="plannedActivityId"
          type="text"
          autocomplete="off"
          placeholder="None for an unscheduled form"
        />
        <label for="ecrf-collected-at">Collected at</label>
        <input
          id="ecrf-collected-at"
          v-model="collectedAt"
          type="datetime-local"
        />
        <button
          id="ecrf-form-open"
          class="btn btn-primary"
          type="submit"
          :disabled="opening || !canOpen"
        >
          {{ opening ? "Opening…" : "Open form" }}
        </button>
      </form>

      <p v-if="loading" role="status">Loading form…</p>
      <p v-if="loadError" class="ecrf-form-error" role="alert">
        The form could not be loaded: {{ loadError }}
      </p>
      <p v-if="openError" class="ecrf-form-error" role="alert">
        The form could not be opened: {{ openError }}
      </p>
    </div>

    <template v-else>
      <dl class="ecrf-form-summary" data-testid="ecrf-form-summary">
        <dt>Form</dt>
        <dd data-testid="ecrf-form-id">
          {{ form.form_id }}
        </dd>
        <dt>Collects</dt>
        <dd>{{ form.collection_spec_id }}</dd>
        <dt>Visit</dt>
        <dd>{{ form.planned_activity_id ?? "Unscheduled" }}</dd>
        <dt>Collected</dt>
        <dd>{{ form.collected_at }}</dd>
        <dt>Version</dt>
        <dd data-testid="ecrf-form-version">
          {{ form.version }}
        </dd>
        <dt>Status</dt>
        <dd data-testid="ecrf-form-status">
          <span v-if="form.complete" class="ecrf-form-complete">Complete</span>
          <span v-else class="ecrf-form-incomplete">
            Incomplete: {{ form.missing_required.length }} required
            {{ form.missing_required.length === 1 ? "item" : "items" }}
            without a value ({{ form.missing_required.join(", ") }})
          </span>
        </dd>
      </dl>

      <div
        v-if="locked"
        class="ecrf-form-locks"
        data-testid="ecrf-form-locks"
        role="status"
      >
        <strong>Locked.</strong> Values cannot be changed while a lock is in
        force.
        <ul>
          <li v-for="held in form.locks" :key="held.lock_id">
            {{ held.kind }} on {{ held.scope }} by {{ held.locked_by }} at
            {{ held.locked_at }}: {{ held.reason }}
          </li>
        </ul>
      </div>

      <form id="ecrf-form-page" @submit.prevent="submit">
        <table class="ecrf-form-items" data-testid="ecrf-form-items">
          <thead>
            <tr>
              <th scope="col">Item</th>
              <th scope="col">Value</th>
              <th scope="col">Or missing because</th>
              <th scope="col">Revision</th>
              <th scope="col">Queries</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="item in items" :key="item.item_id">
              <tr
                :data-item="item.item_id"
                :class="{ 'ecrf-item-edited': isEdited(item) }"
              >
                <th scope="row">
                  <label :for="`ecrf-item-${item.item_id}`">
                    {{ item.item_id }}
                    <abbr
                      v-if="item.required"
                      class="ecrf-required"
                      title="Required"
                      >*</abbr
                    >
                  </label>
                  <div v-if="item.concept" class="ecrf-item-concept">
                    {{ item.concept }}
                  </div>
                </th>
                <td>
                  <input
                    :id="`ecrf-item-${item.item_id}`"
                    v-model="entries[item.item_id].text"
                    type="text"
                    autocomplete="off"
                    :disabled="
                      locked || saving || !!entries[item.item_id].missing
                    "
                    :aria-invalid="!!itemErrors[item.item_id]"
                    :aria-describedby="`ecrf-item-${item.item_id}-error`"
                  />
                  <span v-if="item.unit" class="ecrf-item-unit">{{
                    item.unit
                  }}</span>
                  <p
                    :id="`ecrf-item-${item.item_id}-error`"
                    class="ecrf-form-error"
                  >
                    {{ (itemErrors[item.item_id] ?? []).join(" ") }}
                  </p>
                </td>
                <td>
                  <select
                    v-model="entries[item.item_id].missing"
                    :aria-label="`Why ${item.item_id} is missing`"
                    :disabled="locked || saving"
                  >
                    <option value="">Not missing</option>
                    <option
                      v-for="option in MISSING_REASONS"
                      :key="option.reason"
                      :value="option.reason"
                    >
                      {{ option.text }}
                    </option>
                  </select>
                </td>
                <td class="ecrf-item-revision">
                  <template v-if="item.value">
                    {{ item.revision }} by {{ item.recorded_by }} at
                    {{ item.recorded_at }}
                  </template>
                  <template v-else> No value </template>
                </td>
                <td>
                  <div class="ecrf-item-queries">
                    <span
                      v-if="liveOn(item).length"
                      class="ecrf-query-badge"
                      :data-testid="`ecrf-query-badge-${item.item_id}`"
                    >
                      {{ liveOn(item).length }} unresolved
                      {{ liveOn(item).length === 1 ? "query" : "queries" }}
                    </span>
                    <span
                      v-if="liveOn(item).some((q) => q.value_changed)"
                      class="ecrf-query-changed"
                      :data-testid="`ecrf-query-changed-${item.item_id}`"
                    >
                      Value changed since queried
                    </span>
                    <button
                      v-if="canManageQueries && item.observation_id"
                      :id="`ecrf-raise-${item.item_id}`"
                      class="btn btn-secondary"
                      type="button"
                      :disabled="queries.busy"
                      @click="beginRaise(item)"
                    >
                      Raise query
                    </button>
                  </div>
                </td>
              </tr>
              <tr
                v-if="raising?.item_id === item.item_id"
                class="ecrf-raise-row"
              >
                <td colspan="5">
                  <div class="ecrf-form-row">
                    <label for="ecrf-raise-text"
                      >Query on {{ raising.item_id }} (revision
                      {{ raising.revision }})</label
                    >
                    <textarea
                      id="ecrf-raise-text"
                      v-model="raiseText"
                      rows="2"
                    />
                    <button
                      id="ecrf-raise-confirm"
                      class="btn btn-primary"
                      type="button"
                      :disabled="queries.busy || !raiseText.trim()"
                      @click="confirmRaise"
                    >
                      Raise query
                    </button>
                    <button
                      class="btn btn-secondary"
                      type="button"
                      @click="raising = null"
                    >
                      Back
                    </button>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>

        <div v-if="!locked" class="ecrf-form-row">
          <label for="ecrf-save-reason">Reason for change</label>
          <input
            id="ecrf-save-reason"
            v-model="reason"
            type="text"
            autocomplete="off"
            placeholder="Initial data entry"
          />
          <button
            id="ecrf-form-save"
            class="btn btn-primary"
            type="submit"
            :disabled="saving || !dirty || !reason.trim()"
          >
            {{ saving ? "Saving…" : `Save page (${editedItems.length})` }}
          </button>
          <button
            id="ecrf-form-close"
            class="btn btn-secondary"
            type="button"
            :disabled="saving"
            @click="closeForm"
          >
            Close form
          </button>
        </div>
      </form>

      <div
        v-if="outcome"
        data-testid="ecrf-form-outcome"
        :class="['ecrf-form-outcome', `ecrf-form-outcome--${outcome.kind}`]"
        :role="outcome.kind === 'saved' ? 'status' : 'alert'"
      >
        <template v-if="outcome.kind === 'saved'">
          Page saved: {{ outcome.changed }}
          {{ outcome.changed === 1 ? "value" : "values" }} recorded, form
          version {{ outcome.version }}.
        </template>
        <template v-else>
          {{ outcome.message }}
        </template>
        <div
          v-if="outcome.kind === 'locked' && outcome.unsaved.length > 0"
          class="ecrf-locked-unsaved"
          data-testid="ecrf-locked-unsaved"
        >
          <span>Unsaved values not recorded:</span>
          <ul>
            <li v-for="entry in outcome.unsaved" :key="entry.item_id">
              <strong>{{ entry.item_id }}:</strong>
              {{ formatUnsavedEntry(entry) }}
            </li>
          </ul>
        </div>
        <button
          v-if="outcome.kind === 'stale'"
          id="ecrf-form-reload"
          class="btn btn-secondary"
          type="button"
          @click="reload"
        >
          Reload form
        </button>
      </div>

      <section
        id="ecrf-form-queries"
        aria-labelledby="ecrf-form-queries-title"
        :aria-busy="queries.loading"
      >
        <h3 id="ecrf-form-queries-title">Queries</h3>
        <p v-if="queries.loading" role="status">Loading queries…</p>
        <p v-else-if="queries.loadError" class="ecrf-form-error" role="alert">
          The queries could not be loaded: {{ queries.loadError }}
        </p>
        <p
          v-else-if="queries.queries.length === 0"
          data-testid="ecrf-no-queries"
        >
          No queries on this form.
        </p>
        <FormQueryList
          v-else
          :queries="queries.queries"
          :can-manage="canManageQueries"
          :busy="queries.busy"
          :act="queries.act"
        />
        <div
          data-testid="ecrf-query-outcome"
          :class="[
            'ecrf-form-outcome',
            queries.outcome
              ? `ecrf-form-outcome--${queries.outcome.kind === 'done' ? 'saved' : 'failed'}`
              : '',
          ]"
          aria-live="polite"
          role="status"
        >
          <template v-if="queries.outcome">
            {{ queries.outcome.message }}
            <button
              v-if="queries.outcome.kind === 'stale'"
              id="ecrf-query-reload"
              class="btn btn-secondary"
              type="button"
              @click="reloadAfterStale"
            >
              Reload form
            </button>
          </template>
        </div>
      </section>

      <h3>Verifications, queries and answers</h3>
      <p v-if="form.assertions.length === 0" data-testid="ecrf-no-assertions">
        None recorded.
      </p>
      <ul v-else data-testid="ecrf-form-assertions">
        <li v-for="assertion in form.assertions" :key="assertion.assertion_id">
          {{ assertion.kind }} on {{ assertion.target }}
          <template v-if="assertion.revision !== null">
            (revision {{ assertion.revision }})
          </template>
          by {{ assertion.asserted_by }} at {{ assertion.asserted_at }}
          <template v-if="assertion.text"> : “{{ assertion.text }}” </template>
          <em v-if="assertion.retracted_at">
            (retracted {{ assertion.retracted_at }})</em
          >
        </li>
      </ul>

      <section
        id="ecrf-form-signatures-section"
        aria-labelledby="ecrf-form-signatures-title"
      >
        <h3 id="ecrf-form-signatures-title">Signatures</h3>
        <p v-if="form.signatures.length === 0" data-testid="ecrf-no-signatures">
          Not signed. Signing records a kernel attestation over the form's
          current values; a later correction to a signed value supersedes the
          signature.
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
            <table
              v-if="signature.superseded && signature.signed_values?.length"
              class="ecrf-signed-values"
              data-testid="ecrf-signed-values"
            >
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
                  <td>{{ signed.revision }}</td>
                  <td>{{ currentValueText(signed.item_id) }}</td>
                  <td>{{ currentOf(signed.item_id)?.revision ?? "None" }}</td>
                </tr>
              </tbody>
            </table>
          </li>
        </ul>

        <div class="ecrf-form-row">
          <label for="ecrf-sign-meaning">Meaning</label>
          <select
            id="ecrf-sign-meaning"
            v-model="signMeaning"
            :disabled="signing"
          >
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
            v-if="signOutcome.kind === 'changed'"
            id="ecrf-sign-reload"
            class="btn btn-secondary"
            type="button"
            @click="reloadAfterStale"
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
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import {
  formatObservationValue,
  MISSING_REASONS,
  type KernelObservationValue,
} from "../../api/observationValue";
import {
  FORM_SIGN_SEMANTIC_ACTION,
  formSignatureAction,
  type EcrfFormItem,
  type EcrfFormSignedValue,
  type EcrfSignatureMeaning,
} from "../../api/ecrfFormsApi";
import {
  useEcrfForm,
  type EcrfUnsavedItem,
} from "../../composables/useEcrfForm";
import { isLiveQuery, useFormQueries } from "../../composables/useFormQueries";
import { useAuthStore } from "../../stores/auth.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import FormQueryList from "./FormQueryList.vue";

const props = defineProps<{
  /** A form to load on mount, for example from the route. */
  formId?: string | null;
  enrollmentId?: string | null;
  collectionSpecId?: string | null;
  plannedActivityId?: string | null;
  /** Offer raising, closing, reopening and cancelling queries (a monitor); the module still checks. */
  canManageQueries?: boolean;
}>();

const emit = defineEmits<{ (e: "dirty-change", dirty: boolean): void }>();

const {
  form,
  items,
  entries,
  itemErrors,
  loading,
  loadError,
  opening,
  openError,
  saving,
  outcome,
  signing,
  signOutcome,
  canSign,
  locked,
  dirty,
  editedItems,
  isEdited,
  clearDraft,
  load,
  reload,
  open,
  save,
  sign,
  close,
} = useEcrfForm();

const reuseId = ref(props.formId ?? "");
const enrollmentId = ref(props.enrollmentId ?? "");
const collectionSpecId = ref(props.collectionSpecId ?? "");
const plannedActivityId = ref(props.plannedActivityId ?? "");
const collectedAt = ref("");
const reason = ref("");

const canOpen = computed(
  () =>
    enrollmentId.value.length > 0 &&
    collectionSpecId.value.length > 0 &&
    collectedAt.value.length > 0
);

function reuse() {
  if (reuseId.value) void load(reuseId.value);
}

function openNew() {
  if (!canOpen.value) return;
  void open({
    enrollmentId: enrollmentId.value,
    collectionSpecId: collectionSpecId.value,
    plannedActivityId: plannedActivityId.value || null,
    collectedAt: new Date(collectedAt.value).toISOString(),
  });
}

// The form's queries (#5461), read again whenever the form is read, so `value_changed` follows a correction.
// reactive() unwraps the composable's refs for the template.
const queries = reactive(useFormQueries());
const raising = ref<{
  item_id: string;
  observation_id: string;
  revision: number;
} | null>(null);
const raiseText = ref("");

watch(
  () => (form.value ? `${form.value.form_id}@${form.value.version}` : null),
  (loaded) => {
    raising.value = null;
    if (loaded && form.value) void queries.load({ formId: form.value.form_id });
    else queries.clear();
  }
);

function liveOn(item: EcrfFormItem) {
  return form.value
    ? queries.forItem(form.value.form_id, item.item_id).filter(isLiveQuery)
    : [];
}

/** Starts a query on the item's value at the revision this form read returned. */
function beginRaise(item: EcrfFormItem) {
  if (!item.observation_id) return;
  raising.value = {
    item_id: item.item_id,
    observation_id: item.observation_id,
    revision: Number(item.revision),
  };
  raiseText.value = "";
}

async function confirmRaise() {
  const target = raising.value;
  const text = raiseText.value.trim();
  if (!form.value || !target || !text) return;
  const raised = await queries.raise(
    {
      formId: form.value.form_id,
      itemId: target.item_id,
      observationId: target.observation_id,
      revision: target.revision,
    },
    text
  );
  if (raised) raising.value = null;
}

/** The value moved on since the form was read: read the form and its queries again. */
async function reloadAfterStale() {
  queries.outcome = null;
  await reload();
  await queries.reload();
}

// Signing the form (#5463): the user chooses the meaning, the signature dialog verifies them again at the gateway for
// this form's signing URL, and the signature binds the form's content hash as it was read.
const signMeaning = ref<EcrfSignatureMeaning>("approved");
const showSignModal = ref(false);
const authStore = useAuthStore();

const ECRF_INVESTIGATOR_REASONS = [
  "Investigator Certification",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
  "CLINICAL_QC",
  "REVIEW",
];

const signerUsername = computed(() => authStore.identity?.username ?? "");
const signerFullName = computed(() => {
  const identity = authStore.identity;
  if (!identity) return "";
  const fullName = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || identity.username || "";
});

const MEANING_LABELS: Record<string, string> = {
  approved: "Approved",
  responsibility: "Responsibility",
};

function meaningLabel(meaning: string): string {
  return MEANING_LABELS[meaning.toLowerCase()] ?? meaning;
}

/** Why the form cannot be signed now, or null when it can. */
const signHint = computed(() => {
  if (!form.value || signing.value) return null;
  if (dirty.value) return "Save or discard your changes before signing.";
  if (!form.value.content_hash) return "The form has no values to sign yet.";
  return null;
});

function currentOf(itemId: string): EcrfFormItem | undefined {
  return items.value.find((i) => i.item_id === itemId);
}

function currentValueText(itemId: string): string {
  const value = currentOf(itemId)?.value;
  return value ? formatObservationValue(value) : "No value";
}

function changedSince(signed: EcrfFormSignedValue): boolean {
  const current = currentOf(signed.item_id);
  return !current || Number(current.revision) !== Number(signed.revision);
}

/** The signature dialog's callback: it has the step-up token and the signing reason the user chose. */
async function signWithToken(sigToken: string, signingReason: string) {
  const result = await sign(signMeaning.value, sigToken, signingReason);
  if (!result || result.kind !== "signed") {
    throw new Error(result?.message ?? "The form could not be signed.");
  }
  return result;
}

async function submit() {
  await save(reason.value.trim());
  if (outcome.value?.kind === "saved") reason.value = "";
}

function closeForm() {
  if (
    dirty.value &&
    !window.confirm(
      "You have unsaved changes on this form. Discard them and close?"
    )
  ) {
    return;
  }
  clearDraft();
  close();
}

function formatUnsavedEntry(entry: EcrfUnsavedItem): string {
  if (entry.missing) {
    return formatObservationValue({
      type: "missing",
      reason: entry.missing as Extract<
        KernelObservationValue,
        { type?: "missing" }
      >["reason"],
    });
  }
  const typed = entry.text.trim();
  return entry.unit ? `${typed} ${entry.unit}` : typed;
}

onMounted(() => {
  if (props.formId) void load(props.formId);
});

defineExpose({ dirty, form });

// The view guards navigation while a page has unsaved entries.
watch(dirty, (value) => emit("dirty-change", value));
</script>

<style scoped>
.ecrf-form-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
}
.ecrf-form-hint {
  font-size: 0.85rem;
  color: #475569;
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-form-grid {
  display: grid;
  grid-template-columns: minmax(140px, max-content) 1fr;
  gap: 6px 12px;
  align-items: center;
}
.ecrf-form-grid button {
  grid-column: 2;
  justify-self: start;
}
.ecrf-form-summary {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 12px;
  font-size: 0.85rem;
}
.ecrf-form-summary dt {
  font-weight: 600;
}
.ecrf-form-items {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}
.ecrf-form-items th,
.ecrf-form-items td {
  border-bottom: 1px solid var(--border);
  padding: 6px;
  text-align: left;
  vertical-align: top;
}
.ecrf-required {
  color: #b91c1c;
  text-decoration: none;
  margin-left: 2px;
}
.ecrf-item-concept,
.ecrf-item-revision {
  color: #64748b;
  font-size: 0.75rem;
}
.ecrf-item-unit {
  margin-left: 4px;
  color: #475569;
}
.ecrf-item-queries {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}
.ecrf-query-badge {
  padding: 0 8px;
  border-radius: 999px;
  background-color: #fffbeb;
  color: #92400e;
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-query-changed {
  color: #1d4ed8;
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-item-edited {
  background-color: #fffbeb;
}
.ecrf-form-error {
  color: #b91c1c;
  font-size: 0.8rem;
  margin: 2px 0 0;
}
.ecrf-form-complete {
  color: #166534;
  font-weight: 600;
}
.ecrf-form-incomplete {
  color: #92400e;
}
.ecrf-form-locks {
  background-color: #f1f5f9;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
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
.ecrf-locked-unsaved {
  flex-basis: 100%;
  margin-top: 4px;
}
.ecrf-locked-unsaved ul {
  margin: 4px 0 0;
  padding-left: 18px;
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
}
.ecrf-signature--superseded {
  background-color: #f8fafc;
  color: #475569;
}
.ecrf-signature-state {
  margin-left: 8px;
  padding: 0 8px;
  border-radius: 999px;
  background-color: #f0fdf4;
  color: #166534;
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-signature-state--superseded {
  background-color: #fffbeb;
  color: #92400e;
}
.ecrf-signed-values {
  margin-top: 6px;
  border-collapse: collapse;
  font-size: 0.8rem;
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
  background-color: #fffbeb;
}
.ecrf-form-outcome--saved {
  background-color: #f0fdf4;
  color: #166534;
}
.ecrf-form-outcome--stale,
.ecrf-form-outcome--invalid,
.ecrf-form-outcome--locked,
.ecrf-form-outcome--failed {
  background-color: #fef2f2;
  color: #991b1b;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
