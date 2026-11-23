<template>
  <form id="ecrf-form-page" @submit.prevent="submit">
    <div
      class="ecrf-table-scroll"
      data-testid="ecrf-form-items-scroll"
      role="region"
      aria-label="Case report form items"
      tabindex="0"
    >
      <table class="ecrf-form-items" data-testid="ecrf-form-items">
        <thead>
          <tr>
            <th scope="col">Item</th>
            <th scope="col">Value</th>
            <th scope="col">Or missing because</th>
            <th scope="col">Revision</th>
            <th scope="col">Verification</th>
            <th scope="col">Queries</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="item in formState.items" :key="item.item_id">
            <tr
              :id="`ecrf-row-${item.item_id}`"
              :data-item="item.item_id"
              :data-highlighted="
                highlightItemId === item.item_id ? 'true' : undefined
              "
              :class="{
                'ecrf-item-edited': formState.isEdited(item),
                'ecrf-item-highlighted': highlightItemId === item.item_id,
              }"
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
                  <NodeRefChip
                    v-if="normalizeLookupNodeRef(item.concept)"
                    :node-ref="normalizeLookupNodeRef(item.concept)!"
                  />
                  <template v-else>{{ item.concept }}</template>
                </div>
              </th>
              <td>
                <select
                  v-if="item.codelist_options?.length"
                  :id="`ecrf-item-${item.item_id}`"
                  :value="formState.entries[item.item_id]?.text ?? ''"
                  :disabled="
                    formState.locked ||
                    formState.saving ||
                    !!formState.entries[item.item_id]?.missing
                  "
                  :aria-invalid="!!formState.itemErrors[item.item_id]"
                  :aria-describedby="
                    formState.comparingBaseline && formState.isEdited(item)
                      ? `ecrf-committed-baseline-${item.item_id} ecrf-item-${item.item_id}-error`
                      : `ecrf-item-${item.item_id}-error`
                  "
                  @change="
                    emit(
                      'entry-text',
                      item.item_id,
                      ($event.target as HTMLSelectElement).value
                    )
                  "
                >
                  <option value="">Select…</option>
                  <option
                    v-if="
                      formState.entries[item.item_id]?.text &&
                      !item.codelist_options.some(
                        (option) =>
                          option.code === formState.entries[item.item_id]?.text
                      )
                    "
                    :value="formState.entries[item.item_id]?.text"
                  >
                    {{ formState.entries[item.item_id]?.text }}
                  </option>
                  <option
                    v-for="option in item.codelist_options"
                    :key="option.code"
                    :value="option.code"
                  >
                    {{
                      option.display && option.display !== option.code
                        ? `${option.display} (${option.code})`
                        : option.code
                    }}
                  </option>
                </select>
                <input
                  v-else
                  :id="`ecrf-item-${item.item_id}`"
                  :value="formState.entries[item.item_id]?.text ?? ''"
                  type="text"
                  autocomplete="off"
                  :disabled="
                    formState.locked ||
                    formState.saving ||
                    !!formState.entries[item.item_id]?.missing
                  "
                  :aria-invalid="!!formState.itemErrors[item.item_id]"
                  :aria-describedby="
                    formState.comparingBaseline && formState.isEdited(item)
                      ? `ecrf-committed-baseline-${item.item_id} ecrf-item-${item.item_id}-error`
                      : `ecrf-item-${item.item_id}-error`
                  "
                  @input="
                    emit(
                      'entry-text',
                      item.item_id,
                      ($event.target as HTMLInputElement).value
                    )
                  "
                />
                <span v-if="item.unit" class="ecrf-item-unit">{{
                  item.unit
                }}</span>
                <div
                  v-if="formState.comparingBaseline && formState.isEdited(item)"
                  :id="`ecrf-committed-baseline-${item.item_id}`"
                  :data-testid="`ecrf-committed-baseline-${item.item_id}`"
                  class="ecrf-committed-baseline"
                >
                  <span class="ecrf-committed-baseline-label"
                    >Committed:{{ " " }}</span
                  >
                  <span class="ecrf-committed-baseline-value">{{
                    formState.formatCommittedBaseline(item)
                  }}</span>
                </div>
                <p
                  :id="`ecrf-item-${item.item_id}-error`"
                  class="ecrf-form-error"
                >
                  {{ (formState.itemErrors[item.item_id] ?? []).join(" ") }}
                </p>
                <ul
                  v-if="formState.previewWarnings[item.item_id]?.length"
                  :id="`ecrf-item-${item.item_id}-warnings`"
                  :data-testid="`ecrf-preview-warnings-${item.item_id}`"
                  class="ecrf-preview-warnings"
                >
                  <li
                    v-for="warn in formState.previewWarnings[item.item_id]"
                    :key="warn.rule_id"
                  >
                    <strong>[{{ warn.severity }}] {{ warn.rule_id }}:</strong>
                    {{ warn.message }}
                  </li>
                </ul>
              </td>
              <td>
                <select
                  :value="formState.entries[item.item_id]?.missing ?? ''"
                  :aria-label="`Why ${item.item_id} is missing`"
                  :disabled="formState.locked || formState.saving"
                  @change="
                    emit(
                      'entry-missing',
                      item.item_id,
                      ($event.target as HTMLSelectElement).value
                    )
                  "
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
                <template v-if="item.value && !isUnrecordedValue(item.value)">
                  <component
                    :is="routerLinkTag"
                    v-if="
                      itemObservationLink(item.observation_id, item.item_id)
                    "
                    :id="`ecrf-item-observation-${item.item_id}`"
                    class="ecrf-observation-link"
                    :to="
                      itemObservationLink(item.observation_id, item.item_id)!
                    "
                  >
                    {{ item.revision }}
                  </component>
                  <template v-else>
                    {{ item.revision }}
                  </template>
                  by {{ item.recorded_by }} at {{ item.recorded_at }}
                </template>
                <template v-else> No value </template>
              </td>
              <td>
                <div class="ecrf-item-sdv">
                  <div class="ecrf-sdv-badges">
                    <span
                      v-if="formState.isEdited(item)"
                      class="clinical-badge clinical-badge--unsaved ecrf-unsaved-badge"
                      :data-testid="`ecrf-unsaved-badge-${item.item_id}`"
                    >
                      Unsaved
                    </span>
                    <span
                      v-if="sdv.activeVerification(item)"
                      class="clinical-badge clinical-badge--verified ecrf-sdv-badge ecrf-sdv-badge--verified"
                      :data-testid="`ecrf-sdv-status-${item.item_id}`"
                    >
                      Verified (rev {{ item.revision }})
                    </span>
                    <span
                      v-else-if="sdv.needsReverification(item)"
                      class="clinical-badge clinical-badge--warning ecrf-sdv-badge ecrf-sdv-badge--reverify"
                      :data-testid="`ecrf-sdv-status-${item.item_id}`"
                    >
                      Needs re-verification
                    </span>
                    <span
                      v-else-if="hasRecordedValue(item)"
                      class="clinical-badge clinical-badge--draft ecrf-sdv-badge ecrf-sdv-badge--unverified"
                      :data-testid="`ecrf-sdv-status-${item.item_id}`"
                    >
                      Unverified
                    </span>
                    <span
                      v-else
                      class="ecrf-sdv-none"
                      :data-testid="`ecrf-sdv-status-${item.item_id}`"
                    >
                      No value
                    </span>
                  </div>
                  <div
                    v-if="canVerifySource && hasRecordedValue(item)"
                    class="ecrf-sdv-actions"
                  >
                    <template v-if="!sdv.activeVerification(item)">
                      <input
                        :id="`ecrf-sdv-select-${item.item_id}`"
                        type="checkbox"
                        :checked="sdv.selectedSdvItems.includes(item.item_id)"
                        :value="item.item_id"
                        :disabled="sdv.sdvBusy || formState.assertionsLocked"
                        :aria-label="`Select ${item.item_id} for source verification`"
                        @change="
                          sdv.toggleSdvItem(
                            item.item_id,
                            ($event.target as HTMLInputElement).checked
                          )
                        "
                      />
                      <button
                        :id="`ecrf-verify-${item.item_id}`"
                        class="btn btn-secondary"
                        type="button"
                        :disabled="sdv.sdvBusy || formState.assertionsLocked"
                        @click="sdv.beginVerify(item)"
                      >
                        Verify<span class="sr-only"> {{ item.item_id }}</span>
                      </button>
                    </template>
                    <button
                      v-else
                      :id="`ecrf-unverify-${item.item_id}`"
                      class="btn btn-secondary"
                      type="button"
                      :disabled="sdv.sdvBusy || formState.assertionsLocked"
                      @click="
                        sdv.beginRetract(item, sdv.activeVerification(item)!)
                      "
                    >
                      Withdraw verification<span class="sr-only">
                        for {{ item.item_id }}</span
                      >
                    </button>
                  </div>
                </div>
              </td>
              <td>
                <div class="ecrf-item-queries">
                  <span
                    v-if="liveOn(item).length"
                    class="clinical-badge clinical-badge--queried ecrf-query-badge"
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
                    v-if="canManageQueries"
                    :id="`ecrf-raise-${item.item_id}`"
                    class="btn btn-secondary"
                    type="button"
                    :disabled="queries.busy || formState.assertionsLocked"
                    @click="beginRaise(item)"
                  >
                    Raise query<span class="sr-only">
                      on {{ item.item_id }}</span
                    >
                  </button>
                </div>
              </td>
            </tr>
            <tr
              v-if="sdv.pendingSdv?.item_id === item.item_id"
              class="ecrf-sdv-row"
            >
              <td colspan="6">
                <div class="ecrf-form-row">
                  <label for="ecrf-sdv-reason">
                    <template v-if="sdv.pendingSdv.mode === 'verify'">
                      Reason for verifying
                      {{ sdv.pendingSdv.item_id }} (revision
                      {{ sdv.pendingSdv.revision }})
                    </template>
                    <template v-else>
                      Reason for withdrawing verification on
                      {{ sdv.pendingSdv.item_id }}
                    </template>
                  </label>
                  <input
                    id="ecrf-sdv-reason"
                    :value="sdv.sdvReason"
                    type="text"
                    autocomplete="off"
                    :disabled="sdv.sdvBusy || formState.assertionsLocked"
                    @input="
                      sdv.setSdvReason(
                        ($event.target as HTMLInputElement).value
                      )
                    "
                  />
                  <button
                    id="ecrf-sdv-confirm"
                    class="btn btn-primary"
                    type="button"
                    :disabled="
                      sdv.sdvBusy ||
                      formState.assertionsLocked ||
                      !sdv.sdvReason.trim()
                    "
                    @click="sdv.confirmSdv()"
                  >
                    {{
                      sdv.pendingSdv.mode === "verify"
                        ? "Confirm verification"
                        : "Withdraw verification"
                    }}
                  </button>
                  <button
                    id="ecrf-sdv-cancel"
                    class="btn btn-secondary"
                    type="button"
                    :disabled="sdv.sdvBusy"
                    @click="sdv.cancelPendingSdv(true)"
                  >
                    Back
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="raising?.item_id === item.item_id" class="ecrf-raise-row">
              <td colspan="6">
                <div class="ecrf-form-row">
                  <label for="ecrf-raise-text">
                    <template v-if="raising.unrecorded">
                      Missing-data query on {{ raising.item_id }} (no value
                      recorded)
                    </template>
                    <template v-else>
                      Query on {{ raising.item_id }} (revision
                      {{ raising.revision }})
                    </template>
                  </label>
                  <textarea id="ecrf-raise-text" v-model="raiseText" rows="2" />
                  <label for="ecrf-raise-reason">Reason for change</label>
                  <input
                    id="ecrf-raise-reason"
                    v-model="raiseReason"
                    type="text"
                    autocomplete="off"
                  />
                  <label for="ecrf-raise-owner">Owner</label>
                  <select id="ecrf-raise-owner" v-model="raiseOwnerRole">
                    <option value="site">Site</option>
                    <option value="monitor">Monitor</option>
                    <option value="data_manager">Data manager</option>
                  </select>
                  <label for="ecrf-raise-priority">Priority</label>
                  <select id="ecrf-raise-priority" v-model="raisePriority">
                    <option value="normal">Normal</option>
                    <option value="high">High</option>
                    <option value="low">Low</option>
                  </select>
                  <button
                    id="ecrf-raise-confirm"
                    class="btn btn-primary"
                    type="button"
                    :disabled="
                      queries.busy || !raiseText.trim() || !raiseReason.trim()
                    "
                    @click="confirmRaise"
                  >
                    Raise query
                  </button>
                  <button
                    id="ecrf-raise-cancel"
                    class="btn btn-secondary"
                    type="button"
                    @click="cancelRaise"
                  >
                    Back
                  </button>
                </div>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>

    <div
      v-if="allPreviewWarnings.length > 0"
      id="ecrf-preview-banner"
      data-testid="ecrf-preview-banner"
      class="ecrf-preview-banner"
      role="status"
      aria-live="polite"
    >
      <strong>
        Pre-commit edit check preview ({{ allPreviewWarnings.length }}
        {{ allPreviewWarnings.length === 1 ? "warning" : "warnings" }}):
      </strong>
      <span>
        Saving commits these clinical values immediately; any unresolved
        discrepancy will raise an automated query for review.
      </span>
      <ul
        v-if="formState.previewWarnings.__form__?.length"
        class="ecrf-preview-warnings"
      >
        <li
          v-for="warn in formState.previewWarnings.__form__"
          :key="warn.rule_id"
        >
          <strong>[{{ warn.severity }}] {{ warn.rule_id }}:</strong>
          {{ warn.message }}
        </li>
      </ul>
    </div>

    <div v-if="!formState.locked" class="ecrf-form-row">
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
        :disabled="formState.saving || !formState.dirty || !reason.trim()"
      >
        {{
          formState.saving
            ? "Saving…"
            : `Save page (${formState.editedItems.length})`
        }}
      </button>
      <button
        id="ecrf-form-close"
        class="btn btn-secondary"
        type="button"
        :disabled="formState.saving"
        @click="emit('close-form')"
      >
        Close form
      </button>
    </div>
  </form>

  <div
    v-if="formState.outcome"
    data-testid="ecrf-form-outcome"
    :class="[
      'ecrf-form-outcome',
      `ecrf-form-outcome--${formState.outcome.kind}`,
    ]"
    :role="formState.outcome.kind === 'saved' ? 'status' : 'alert'"
  >
    <template v-if="formState.outcome.kind === 'saved'">
      Page saved: {{ formState.outcome.changed }}
      {{ formState.outcome.changed === 1 ? "value" : "values" }} recorded, form
      version {{ formState.outcome.version }}.
    </template>
    <template v-else>
      {{ formState.outcome.message }}
    </template>
    <div
      v-if="
        formState.outcome.kind === 'locked' &&
        formState.outcome.unsaved.length > 0
      "
      class="ecrf-locked-unsaved"
      data-testid="ecrf-locked-unsaved"
    >
      <span>Unsaved values not recorded:</span>
      <ul>
        <li v-for="entry in formState.outcome.unsaved" :key="entry.item_id">
          <strong>{{ entry.item_id }}:</strong>
          {{ formatUnsavedEntry(entry) }}
        </li>
      </ul>
    </div>
    <button
      v-if="formState.outcome.kind === 'stale'"
      id="ecrf-form-reload"
      class="btn btn-secondary"
      type="button"
      @click="formState.reload()"
    >
      Reload form
    </button>
  </div>
</template>

<script setup lang="ts">
import {
  computed,
  getCurrentInstance,
  ref,
  watch,
  type UnwrapNestedRefs,
} from "vue";
import { handoffFocus } from "ui";
import type { RouteLocationRaw } from "vue-router";
import {
  formatObservationValue,
  isUnrecordedValue,
  MISSING_REASONS,
  type KernelObservationValue,
} from "../../api/observationValue";
import type { EcrfFormItem } from "../../api/ecrfFormsApi";
import type {
  FormQueryOwnerRole,
  FormQueryPriority,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import type {
  EcrfUnsavedItem,
  useEcrfForm,
} from "../../composables/useEcrfForm";
import {
  hasRecordedValue,
  type useEcrfSdv,
} from "../../composables/useEcrfSdv";
import {
  isLiveQuery,
  type useFormQueries,
} from "../../composables/useFormQueries";
import { normalizeLookupNodeRef } from "../../canvas/nodeResolver";
import NodeRefChip from "../canvas/NodeRefChip.vue";

export interface RaisingQueryTarget {
  item_id: string;
  observation_id: string | null;
  revision: number | null;
  unrecorded: boolean;
}

const props = defineProps<{
  formState: UnwrapNestedRefs<ReturnType<typeof useEcrfForm>>;
  sdv: UnwrapNestedRefs<ReturnType<typeof useEcrfSdv>>;
  queries: UnwrapNestedRefs<ReturnType<typeof useFormQueries>>;
  highlightItemId?: string | null;
  canVerifySource?: boolean;
  canManageQueries?: boolean;
  itemObservationLink: (
    observationId: string | null | undefined,
    itemId?: string | null
  ) => RouteLocationRaw | null;
}>();

const emit = defineEmits<{
  (e: "entry-text", itemId: string, text: string): void;
  (e: "entry-missing", itemId: string, missing: string): void;
  (e: "close-form"): void;
}>();

const instance = getCurrentInstance();
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const reason = ref("");
const raising = ref<RaisingQueryTarget | null>(null);
const raiseText = ref("");
const raiseReason = ref("");
const raiseOwnerRole = ref<FormQueryOwnerRole>("site");
const raisePriority = ref<FormQueryPriority>("normal");

watch(
  () =>
    props.formState.form
      ? `${props.formState.form.form_id}@${props.formState.form.version}`
      : null,
  () => {
    raising.value = null;
  }
);

const EMPTY_LIVE_QUERIES: FormQueryResponse[] = Object.freeze(
  []
) as unknown as FormQueryResponse[];

const liveQueriesByItemId = computed(() => {
  const byItem = new Map<string, FormQueryResponse[]>();
  const currentForm = props.formState.form;
  if (!currentForm) return byItem;
  for (const item of props.formState.items) {
    if (byItem.has(item.item_id)) continue;
    const itemQueries = props.queries.forItem(
      currentForm.form_id,
      item.item_id
    );
    if (itemQueries.length === 0) continue;
    const live = itemQueries.filter(isLiveQuery);
    if (live.length > 0) {
      byItem.set(item.item_id, live);
    }
  }
  return byItem;
});

function liveOn(item: EcrfFormItem): FormQueryResponse[] {
  return liveQueriesByItemId.value.get(item.item_id) ?? EMPTY_LIVE_QUERIES;
}

function beginRaise(item: EcrfFormItem) {
  raising.value = {
    item_id: item.item_id,
    observation_id: item.observation_id ?? null,
    revision: item.observation_id ? Number(item.revision) : null,
    unrecorded: !item.observation_id || isUnrecordedValue(item.value),
  };
  raiseText.value = "";
  raiseReason.value = "";
  raiseOwnerRole.value = "site";
  raisePriority.value = "normal";
  void handoffFocus("#ecrf-raise-text", { scrollIfOffscreen: true });
}

function cancelRaise() {
  const itemId = raising.value?.item_id ?? null;
  raising.value = null;
  raiseText.value = "";
  raiseReason.value = "";
  void handoffFocus([
    itemId ? `#ecrf-raise-${itemId}` : null,
    itemId ? `#ecrf-item-${itemId}` : null,
    "#canvas-form-workspace-title",
    "#ecrf-form-title",
  ]);
}

async function confirmRaise() {
  const target = raising.value;
  const text = raiseText.value.trim();
  const changeReason = raiseReason.value.trim();
  if (!props.formState.form || !target || !text || !changeReason) return;
  const wasUnasked = !target.observation_id;
  const options = {
    ...(raiseOwnerRole.value !== "site"
      ? { ownerRole: raiseOwnerRole.value }
      : {}),
    ...(raisePriority.value !== "normal"
      ? { priority: raisePriority.value }
      : {}),
  };
  const raised = await props.queries.raise(
    {
      formId: props.formState.form.form_id,
      itemId: target.item_id,
      observationId: target.observation_id,
      revision: target.revision,
    },
    text,
    changeReason,
    Object.keys(options).length > 0 ? options : undefined
  );
  if (raised) {
    raising.value = null;
    if (wasUnasked) await props.formState.reload();
    void handoffFocus([
      `#ecrf-raise-${target.item_id}`,
      `#ecrf-item-${target.item_id}`,
      "#ecrf-form-queries-title",
      "#canvas-form-workspace-title",
      "#ecrf-form-title",
    ]);
  }
}

async function submit() {
  await props.formState.save(reason.value.trim());
  if (props.formState.outcome?.kind === "saved") reason.value = "";
}

const allPreviewWarnings = computed(() =>
  Object.values(props.formState.previewWarnings).flat()
);

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
</script>

<style scoped>
#ecrf-form-page {
  min-width: 0;
  max-width: 100%;
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-table-scroll {
  position: relative;
  overflow-x: auto;
  max-width: 100%;
  min-width: 0;
  padding: 4px;
}
.ecrf-table-scroll:focus-visible {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
  border-radius: 4px;
}
.ecrf-form-items {
  width: 100%;
  min-width: 52rem;
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
.ecrf-form-items > thead > tr > th:nth-child(1),
.ecrf-form-items > tbody > tr > th:not([colspan]):nth-child(1) {
  min-width: 7.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(2),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(2) {
  min-width: 11rem;
}
.ecrf-form-items > thead > tr > th:nth-child(3),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(3) {
  min-width: 9.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(4),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(4) {
  min-width: 8rem;
}
.ecrf-form-items > thead > tr > th:nth-child(5),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(5) {
  min-width: 8.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(6),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(6) {
  min-width: 7.5rem;
}
.ecrf-required {
  color: var(--semantic-color-error);
  text-decoration: none;
  margin-left: 2px;
}
.ecrf-item-concept,
.ecrf-item-revision {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}
.ecrf-item-unit {
  margin-left: 4px;
  color: var(--semantic-color-text-secondary);
}
.ecrf-item-queries {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}
.ecrf-query-changed {
  color: var(--semantic-color-interactive-primary);
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-item-edited,
.ecrf-item-edited > th,
.ecrf-item-edited > td {
  background-color: var(--semantic-color-edited-row-bg);
  color: var(--semantic-color-text);
}
.ecrf-item-edited {
  border-left: 4px solid var(--semantic-color-edited-row-border);
}
.ecrf-item-edited > th:first-child,
.ecrf-item-edited > td:first-child {
  border-left: 4px solid var(--semantic-color-edited-row-border);
}
.ecrf-committed-baseline {
  margin-top: 4px;
  font-size: 0.75rem;
  font-variant-numeric: tabular-nums;
  color: var(--semantic-color-text-secondary);
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.ecrf-committed-baseline-label {
  font-weight: 600;
  color: var(--semantic-color-text);
}
.ecrf-item-highlighted {
  background-color: var(--semantic-color-info-bg);
  outline: 2px solid var(--semantic-color-interactive-primary);
}
.ecrf-item-sdv {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}
.ecrf-sdv-badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}
.ecrf-sdv-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}
.ecrf-sdv-none {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}
.ecrf-form-error {
  color: var(--semantic-color-error);
  font-size: 0.8rem;
  margin: 2px 0 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
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
.ecrf-form-outcome--saved {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.ecrf-form-outcome--stale,
.ecrf-form-outcome--invalid,
.ecrf-form-outcome--locked,
.ecrf-form-outcome--failed {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
.ecrf-preview-warnings {
  margin: 4px 0 0;
  padding-left: 16px;
  font-size: 0.8rem;
  color: var(--warning);
}
.ecrf-preview-banner {
  padding: 8px 12px;
  margin-bottom: 10px;
  border-radius: 6px;
  border: 1px solid var(--warning);
  background: var(--warning-bg);
  color: var(--warning);
  font-size: 0.85rem;
}
</style>
