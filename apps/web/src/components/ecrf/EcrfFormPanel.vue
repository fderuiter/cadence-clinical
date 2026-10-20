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
        <dd data-testid="ecrf-form-id">{{ form.form_id }}</dd>
        <dt>Collects</dt>
        <dd>{{ form.collection_spec_id }}</dd>
        <dt>Visit</dt>
        <dd>{{ form.planned_activity_id ?? "Unscheduled" }}</dd>
        <dt>Collected</dt>
        <dd>{{ form.collected_at }}</dd>
        <dt>Version</dt>
        <dd data-testid="ecrf-form-version">{{ form.version }}</dd>
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
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="item in items"
              :key="item.item_id"
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
                <template v-else>No value</template>
              </td>
            </tr>
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
            @click="close"
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
        <template v-else>{{ outcome.message }}</template>
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

      <h3>Verifications, queries and answers</h3>
      <p v-if="form.assertions.length === 0" data-testid="ecrf-no-assertions">
        None recorded.
      </p>
      <ul v-else data-testid="ecrf-form-assertions">
        <li v-for="assertion in form.assertions" :key="assertion.assertion_id">
          {{ assertion.kind }} on {{ assertion.target }}
          <template v-if="assertion.revision !== null">
            (revision {{ assertion.revision }})</template
          >
          by {{ assertion.asserted_by }} at {{ assertion.asserted_at }}
          <template v-if="assertion.text">: “{{ assertion.text }}”</template>
          <em v-if="assertion.retracted_at">
            (retracted {{ assertion.retracted_at }})</em
          >
        </li>
      </ul>

      <h3>Signatures</h3>
      <p v-if="form.signatures.length === 0" data-testid="ecrf-no-signatures">
        Not signed. Form signatures are recorded as kernel attestations; signing
        from this screen is not available yet.
      </p>
      <ul v-else data-testid="ecrf-form-signatures">
        <li
          v-for="signature in form.signatures"
          :key="signature.attestation_id"
        >
          {{ signature.meaning }} by {{ signature.signed_by }} at
          {{ signature.signed_at }}: {{ signature.reason }}
        </li>
      </ul>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { MISSING_REASONS } from "../../api/observationValue";
import { useEcrfForm } from "../../composables/useEcrfForm";

const props = defineProps<{
  /** A form to load on mount, for example from the route. */
  formId?: string | null;
  enrollmentId?: string | null;
  collectionSpecId?: string | null;
  plannedActivityId?: string | null;
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
  locked,
  dirty,
  editedItems,
  isEdited,
  load,
  reload,
  open,
  save,
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

async function submit() {
  await save(reason.value.trim());
  if (outcome.value?.kind === "saved") reason.value = "";
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
</style>
