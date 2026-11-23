<template>
  <form id="ecrf-form-page" @submit.prevent="emit('submit-page')">
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
          <tr
            v-for="item in formState.items"
            :id="`ecrf-row-${item.item_id}`"
            :key="item.item_id"
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
              <label class="ecrf-item-label" :for="`ecrf-item-${item.item_id}`">
                <span class="ecrf-item-code">{{ item.item_id }}</span>
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
            <td data-cell-label="Value">
              <div class="ecrf-value-control">
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
                  :aria-describedby="`ecrf-item-${item.item_id}-error`"
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
                  :aria-describedby="`ecrf-item-${item.item_id}-error`"
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
            <td data-cell-label="Or missing because">
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
            <td class="ecrf-item-revision" data-cell-label="Revision">
              <template v-if="item.value && !isUnrecordedValue(item.value)">
                <component
                  :is="routerLinkTag"
                  v-if="itemObservationLink(item.observation_id, item.item_id)"
                  :id="`ecrf-item-observation-${item.item_id}`"
                  class="ecrf-observation-link"
                  :to="itemObservationLink(item.observation_id, item.item_id)!"
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
            <td data-cell-label="Verification">
              <div class="ecrf-item-sdv">
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
            <td data-cell-label="Queries">
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
                  @click="emit('begin-raise', item)"
                >
                  Raise query<span class="sr-only"> on {{ item.item_id }}</span>
                </button>
              </div>
            </td>
          </tr>
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
import { computed, getCurrentInstance, type UnwrapNestedRefs } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  formatObservationValue,
  isUnrecordedValue,
  MISSING_REASONS,
  type KernelObservationValue,
} from "../../api/observationValue";
import type { EcrfFormItem } from "../../api/ecrfFormsApi";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
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
  (e: "begin-raise", item: EcrfFormItem): void;
  (e: "submit-page"): void;
}>();

const instance = getCurrentInstance();
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

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
.ecrf-table-scroll {
  position: relative;
  overflow-x: auto;
  max-width: 100%;
  padding-block: 4px;
  padding-inline: 0;
}
.ecrf-table-scroll:focus-visible {
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
  border-radius: 4px;
}
.ecrf-form-items {
  width: 100%;
  min-width: 56rem;
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
  min-width: 8rem;
}
.ecrf-form-items > thead > tr > th:nth-child(2),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(2) {
  min-width: 12rem;
}
.ecrf-form-items > thead > tr > th:nth-child(3),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(3) {
  min-width: 10rem;
}
.ecrf-form-items > thead > tr > th:nth-child(4),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(4) {
  min-width: 9rem;
}
.ecrf-form-items > thead > tr > th:nth-child(5),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(5) {
  min-width: 9rem;
}
.ecrf-form-items > thead > tr > th:nth-child(6),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(6) {
  min-width: 8rem;
}
.ecrf-item-label {
  font-weight: 600;
}
.ecrf-item-code,
.ecrf-observation-link {
  font-family: var(--font-mono);
}
.ecrf-value-control {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.ecrf-value-control select {
  min-width: 0;
  max-width: 100%;
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
.ecrf-item-edited {
  background-color: var(--semantic-color-warning-bg-soft);
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
.ecrf-sdv-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
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
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--warning);
  color: var(--semantic-color-warning-dark);
  font-size: 0.85rem;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

/* Tablet & desktop horizontal scroll: pin row header column (R4) */
@media (min-width: 481px) {
  .ecrf-form-items > thead > tr > th:first-child {
    position: sticky;
    left: 0;
    z-index: 3;
    background-color: var(--semantic-color-surface);
    box-shadow: inset -1px 0 0 var(--border);
  }
  .ecrf-form-items > tbody > tr > th[scope="row"] {
    position: sticky;
    left: 0;
    z-index: 2;
    background-color: var(--semantic-color-surface);
    box-shadow: inset -1px 0 0 var(--border);
  }
  .ecrf-form-items > tbody > tr.ecrf-item-edited > th[scope="row"] {
    background-color: var(--semantic-color-warning-bg-soft);
  }
  .ecrf-form-items > tbody > tr.ecrf-item-highlighted > th[scope="row"] {
    background-color: var(--semantic-color-info-bg);
  }
}

/* Mobile 320px-480px: progressive item cards preserving single semantic table DOM (R5) */
@media (max-width: 480px) {
  .ecrf-table-scroll {
    overflow-x: visible;
    padding: 0;
    width: 100%;
    max-width: 100%;
  }
  .ecrf-form-items {
    display: block;
    width: 100%;
    min-width: 0;
  }
  .ecrf-form-items > thead {
    display: none;
  }
  .ecrf-form-items > tbody {
    display: flex;
    flex-direction: column;
    gap: 12px;
    width: 100%;
    min-width: 0;
  }
  .ecrf-form-items > tbody > tr {
    display: flex;
    flex-direction: column;
    gap: 10px;
    width: 100%;
    min-width: 0;
    padding: 12px;
    border: 1px solid var(--border);
    border-radius: 8px;
    background-color: var(--semantic-color-surface);
    box-sizing: border-box;
    scroll-margin-bottom: 164px;
  }
  .ecrf-form-items > tbody > tr.ecrf-item-edited {
    background-color: var(--semantic-color-warning-bg-soft);
  }
  .ecrf-form-items > tbody > tr.ecrf-item-highlighted {
    background-color: var(--semantic-color-info-bg);
  }
  .ecrf-form-items > tbody > tr > th,
  .ecrf-form-items > tbody > tr > td {
    display: flex;
    flex-direction: column;
    gap: 4px;
    width: 100%;
    min-width: 0 !important;
    padding: 0;
    border-bottom: none;
    box-sizing: border-box;
  }
  .ecrf-form-items > tbody > tr > th[scope="row"] {
    padding-bottom: 6px;
    border-bottom: 1px solid var(--border);
  }
  .ecrf-form-items > tbody > tr > td[data-cell-label]::before {
    content: attr(data-cell-label);
    font-size: 0.72rem;
    font-weight: 600;
    color: var(--semantic-color-text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.03em;
  }
  .ecrf-value-control {
    width: 100%;
    min-width: 0;
  }
  .ecrf-value-control input[type="text"],
  .ecrf-form-items select {
    flex: 1 1 auto;
    width: 100%;
    max-width: 100%;
    min-width: 0;
    min-height: 44px;
    box-sizing: border-box;
  }
  .ecrf-item-revision {
    overflow-wrap: anywhere;
    word-break: break-word;
  }
  .ecrf-sdv-actions button,
  .ecrf-item-queries button {
    min-height: 44px;
    width: 100%;
  }
  .ecrf-item-queries {
    align-items: stretch;
  }
}
</style>
