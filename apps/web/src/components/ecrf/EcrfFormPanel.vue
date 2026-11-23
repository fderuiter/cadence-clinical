<template>
  <section id="ecrf-form-instance" class="card ecrf-form-panel">
    <div id="ecrf-form-title" class="card-title" tabindex="-1">
      Case report form
    </div>

    <EcrfFormOpener
      v-if="!form"
      :form-id="formId"
      :enrollment-id="enrollmentId"
      :collection-spec-id="collectionSpecId"
      :planned-activity-id="plannedActivityId"
      :enrollment-context="enrollmentContext"
      :schedule-options="scheduleOptions"
      :schedule-loading="scheduleLoading"
      :schedule-error="scheduleError"
      :loading="formState.loading"
      :load-error="formState.loadError"
      :opening="formState.opening"
      :open-error="formState.openError"
      @load-form="onLoadForm"
      @open-form="onOpenForm"
      @retry-load="formState.retryLoad"
      @retry-schedule="emit('retry-schedule')"
      @enrollment-change="formState.openError = null"
    />

    <template v-else>
      <EcrfSdvToolbar
        :form="form"
        :locked="formState.locked"
        :assertions-locked="formState.assertionsLocked"
        :can-verify-source="canVerifySource"
        :sdv="sdv"
        @lock-changed="reloadAfterLockChange"
        @reload-stale="reloadAfterSdvStale"
      />

      <div
        v-if="formState.draftRestored"
        class="ecrf-draft-banner ecrf-draft-banner--restored"
        data-testid="ecrf-draft-restored-banner"
        role="status"
      >
        <div class="ecrf-draft-banner-body">
          <strong>Unsaved browser draft restored</strong>
          <span>
            Restored unsaved draft
            {{
              formState.restoredDraftItemIds.length === 1
                ? `value for ${formState.restoredDraftItemIds[0]}`
                : `values for ${formState.restoredDraftItemIds.join(", ")}`
            }}
            over committed server entries. Compare against committed values or
            save when ready.
          </span>
        </div>
        <div class="ecrf-draft-banner-actions">
          <button
            id="ecrf-draft-compare-toggle"
            type="button"
            class="btn btn-secondary"
            :aria-pressed="formState.comparingBaseline"
            @click="formState.toggleBaselineComparison()"
          >
            {{
              formState.comparingBaseline
                ? "Hide committed values"
                : "Compare committed values"
            }}
          </button>
          <button
            id="ecrf-draft-discard"
            type="button"
            class="btn btn-secondary"
            @click="formState.discardDraft()"
          >
            Discard draft
          </button>
        </div>
      </div>

      <div
        v-if="formState.staleDraftItems.length > 0"
        class="ecrf-draft-banner ecrf-draft-banner--stale"
        data-testid="ecrf-draft-stale-banner"
        role="alert"
      >
        <div class="ecrf-draft-banner-body">
          <strong>Stale draft entries skipped</strong>
          <span>
            Skipped saved draft
            {{ formState.staleDraftItems.length === 1 ? "entry" : "entries" }}
            because committed server revisions advanced:
          </span>
          <ul class="ecrf-draft-banner-list">
            <li
              v-for="skipped in formState.staleDraftItems"
              :key="skipped.item_id"
            >
              <strong>{{ skipped.item_id }}:</strong> draft
              {{ skipped.draft_display }} (saved at rev
              {{ skipped.draft_revision
              }}<template v-if="skipped.current_revision !== null"
                >, server now at rev {{ skipped.current_revision }}</template
              >)
            </li>
          </ul>
        </div>
        <div class="ecrf-draft-banner-actions">
          <button
            id="ecrf-draft-stale-dismiss"
            type="button"
            class="btn btn-secondary"
            @click="formState.dismissStaleDraftNotice()"
          >
            Dismiss notice
          </button>
        </div>
      </div>

      <div
        v-if="formState.lockedDraftItems.length > 0"
        class="ecrf-draft-banner ecrf-draft-banner--locked"
        data-testid="ecrf-draft-locked-banner"
        role="status"
      >
        <div class="ecrf-draft-banner-body">
          <strong
            >Unapplied browser draft ({{
              formState.lockSummaryLabel
            }}
            form)</strong
          >
          <span>
            This form is read-only, so your saved browser draft was not applied
            to the record. Unapplied draft values are preserved below until you
            clear them:
          </span>
          <ul class="ecrf-draft-banner-list">
            <li
              v-for="unapplied in formState.lockedDraftItems"
              :key="unapplied.item_id"
            >
              <strong>{{ unapplied.item_id }}:</strong> draft
              {{ unapplied.draft_display }} (committed:
              {{ unapplied.committed_display }})
            </li>
          </ul>
        </div>
        <div class="ecrf-draft-banner-actions">
          <button
            id="ecrf-draft-locked-clear"
            type="button"
            class="btn btn-secondary"
            @click="formState.clearLockedDraft()"
          >
            Clear saved draft
          </button>
        </div>
      </div>

      <EcrfItemTable
        :form-state="formState"
        :sdv="sdv"
        :queries="queries"
        :highlight-item-id="highlightItemId"
        :can-verify-source="canVerifySource"
        :can-manage-queries="canManageQueries"
        :item-observation-link="itemObservationLink"
        @entry-text="setEntryText"
        @entry-missing="setEntryMissing"
        @close-form="closeForm"
      />

      <EcrfAssertionsList
        :queries="queries.queries"
        :queries-loading="queries.loading"
        :queries-load-error="queries.loadError"
        :queries-busy="queries.busy"
        :query-outcome="queries.outcome"
        :can-manage-queries="canManageQueries"
        :can-answer-queries="effectiveCanAnswerQueries"
        :assertions-locked="formState.assertionsLocked"
        :act-query="queries.act"
        :act-many-queries="queries.actMany"
        :current-form-titles="currentFormTitles"
        :query-form-link="queryFormLink"
        :query-observation-link="queryObservationLink"
        :assertions="form.assertions"
        :items="formState.items"
        :item-observation-link="itemObservationLink"
        @retry-queries="queries.reload"
        @reload-stale="reloadAfterStale"
        @query-list-click="onQueryListClick"
      />

      <EcrfSignaturesSection
        :form="form"
        :items="formState.items"
        :dirty="dirty"
        :signing="formState.signing"
        :can-sign="formState.canSign"
        :sign-outcome="formState.signOutcome"
        :sign="formState.sign"
        :item-observation-link="itemObservationLink"
        @reload-stale="reloadAfterStale"
      />
    </template>
  </section>
</template>

<script lang="ts">
export type {
  CanvasFormEnrollmentContext,
  CanvasFormScheduleOptions,
} from "./EcrfFormOpener.vue";
</script>

<script setup lang="ts">
import {
  computed,
  getCurrentInstance,
  nextTick,
  onMounted,
  onUnmounted,
  reactive,
  watch,
} from "vue";
import { handoffFocus } from "ui";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm } from "../../api/ecrfFormsApi";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
import { canAnswerQueries as hasQueryAnswerRole } from "../../canvas/authority";
import {
  collectionInstanceRef,
  observationNodeRef,
} from "../../canvas/nodeRef";
import { confirmDialog } from "../../composables/useConfirmDialog.js";
import { useEcrfForm } from "../../composables/useEcrfForm";
import { useEcrfSdv } from "../../composables/useEcrfSdv";
import { useFormQueries } from "../../composables/useFormQueries";
import { useAuthStore } from "../../stores/auth.js";
import EcrfAssertionsList from "./EcrfAssertionsList.vue";
import EcrfFormOpener, {
  type CanvasFormEnrollmentContext,
  type CanvasFormScheduleOptions,
} from "./EcrfFormOpener.vue";
import EcrfItemTable from "./EcrfItemTable.vue";
import EcrfSdvToolbar from "./EcrfSdvToolbar.vue";
import EcrfSignaturesSection from "./EcrfSignaturesSection.vue";

const props = withDefaults(
  defineProps<{
    formId?: string | null;
    enrollmentId?: string | null;
    collectionSpecId?: string | null;
    plannedActivityId?: string | null;
    enrollmentContext?: CanvasFormEnrollmentContext | null;
    scheduleOptions?: CanvasFormScheduleOptions | null;
    scheduleLoading?: boolean;
    scheduleError?: string | null;
    observationLocation?: (
      observationId: string,
      formId: string,
      itemId?: string | null
    ) => RouteLocationRaw | null;
    /** Builds a route location for a form inside the hosting workspace. */
    formLocation?: (
      formId: string,
      itemId?: string | null
    ) => RouteLocationRaw | null;
    /** Offer raising, closing, reopening and cancelling queries (a monitor or data manager); the module still checks. */
    canManageQueries?: boolean;
    canAnswerQueries?: boolean;
    canVerifySource?: boolean;
    highlightItemId?: string | null;
  }>(),
  { canAnswerQueries: undefined }
);

const emit = defineEmits<{
  (e: "dirty-change", dirty: boolean): void;
  (e: "form-loaded", loaded: EcrfForm): void;
  (e: "closed"): void;
  (e: "retry-schedule"): void;
}>();

const hasRouter = Boolean(
  getCurrentInstance()?.appContext.config.globalProperties.$router
);
const authStore = useAuthStore();
const rawForm = useEcrfForm();
const { form, dirty, lastHydratedSave } = rawForm;
const formState = reactive(rawForm);
const sdv = reactive(
  useEcrfSdv({ form, items: rawForm.items, reload: rawForm.reload })
);
const queries = reactive(useFormQueries());

function setEntryText(itemId: string, text: string) {
  if (formState.entries[itemId]) formState.entries[itemId].text = text;
}
function setEntryMissing(itemId: string, missing: string) {
  if (formState.entries[itemId]) formState.entries[itemId].missing = missing;
}

function handoffAfterFormOpen() {
  if (!form.value || props.highlightItemId) return;
  void handoffFocus([
    () =>
      document.querySelector<HTMLElement>(
        "#ecrf-form-page input:not([disabled]), #ecrf-form-page select:not([disabled]), #ecrf-form-page textarea:not([disabled])"
      ),
    () =>
      document.querySelector<HTMLElement>(
        "#ecrf-form-instance input:not([disabled]), #ecrf-form-instance select:not([disabled]), #ecrf-form-instance textarea:not([disabled])"
      ),
    "#canvas-form-workspace-title",
    "#ecrf-form-title",
  ]);
}

async function onLoadForm(id: string) {
  await formState.load(id);
  handoffAfterFormOpen();
}

async function onOpenForm(payload: Parameters<typeof formState.open>[0]) {
  await formState.open(payload);
  handoffAfterFormOpen();
}

function itemObservationLink(
  observationId: string | null | undefined,
  itemId?: string | null
): RouteLocationRaw | null {
  if (!hasRouter || !observationId || !form.value) return null;
  if (props.observationLocation) {
    return props.observationLocation(observationId, form.value.form_id, itemId);
  }
  const refNode = observationNodeRef(observationId);
  if (!refNode) return null;
  const formRef = collectionInstanceRef(form.value.form_id);
  const query: Record<string, string> = {};
  if (formRef) query.form = formRef;
  if (itemId) query.item = itemId;
  return Object.keys(query).length > 0
    ? { name: "observation", params: { observationRef: refNode }, query }
    : { name: "observation", params: { observationRef: refNode } };
}

const queryObservationLink = (q: FormQueryResponse) =>
  itemObservationLink(q.observation_id, q.item_id);
const queryFormLink = computed(() =>
  props.formLocation
    ? (q: FormQueryResponse) => props.formLocation!(q.form_id, q.item_id)
    : undefined
);
const currentFormTitles = computed<Record<string, string>>(() => {
  if (!form.value) return {};
  const title = form.value.collection_title || form.value.collection_spec_id;
  const label = form.value.planned_activity_id
    ? `${title} · ${form.value.planned_activity_id}`
    : title;
  return { [form.value.form_id]: label };
});
const effectiveCanAnswerQueries = computed(() =>
  props.canAnswerQueries !== undefined
    ? props.canAnswerQueries
    : authStore.isAuthenticated
      ? hasQueryAnswerRole(authStore)
      : !props.canManageQueries
);

async function reloadAfterLockChange() {
  await formState.reload();
  await queries.reload();
}
async function reloadAfterStale() {
  queries.outcome = null;
  await formState.reload();
  await queries.reload();
}
async function reloadAfterSdvStale() {
  sdv.cancelPendingSdv();
  sdv.sdvOutcome = null;
  await formState.reload();
  await queries.reload();
}
async function closeForm() {
  if (dirty.value) {
    const confirmed = await confirmDialog({
      title: "Discard Unsaved Changes",
      message: "You have unsaved changes on this form. Discard them and close?",
      confirmLabel: "Discard & Close",
      cancelLabel: "Keep Editing",
      variant: "warning",
    });
    if (!confirmed) return;
  }
  formState.clearDraft();
  formState.close();
  emit("closed");
  void handoffFocus([
    "#ecrf-form-id",
    "#ecrf-collection-spec",
    "#canvas-form-workspace-title",
    "#ecrf-form-title",
  ]);
}

async function focusHighlightedItem(itemId: string) {
  await nextTick();
  document
    .getElementById(`ecrf-row-${itemId}`)
    ?.scrollIntoView?.({ block: "center" });
  const input = document.getElementById(`ecrf-item-${itemId}`) as
    HTMLInputElement | HTMLSelectElement | null;
  if (input && !input.disabled) {
    input.focus?.();
  } else {
    void handoffFocus(["#canvas-form-workspace-title", "#ecrf-form-title"]);
  }
}

function onQueryListClick(event: MouseEvent) {
  const target = (event.target as Element | null)?.closest?.(
    '[data-testid^="query-form-link-"]'
  );
  if (!target || !form.value) return;
  const testId = target.getAttribute("data-testid") || "";
  const queryId = testId.slice("query-form-link-".length);
  const matched = queries.queries.find((q) => q.query_id === queryId);
  if (matched?.item_id && matched.form_id === form.value.form_id) {
    void focusHighlightedItem(matched.item_id);
  }
}

watch(
  () => (form.value ? `${form.value.form_id}@${form.value.version}` : null),
  (loaded) => {
    sdv.cancelPendingSdv();
    if (loaded && form.value) {
      emit("form-loaded", form.value);
      const hydrated = lastHydratedSave.value;
      if (
        hydrated &&
        hydrated.formId === form.value.form_id &&
        hydrated.version === Number(form.value.version)
      ) {
        lastHydratedSave.value = null;
        queries.applySavedValues(form.value.form_id, hydrated.values);
      } else {
        void queries.load({ formId: form.value.form_id });
      }
    } else {
      queries.clear();
    }
  }
);

watch(
  [() => form.value?.form_id, () => props.highlightItemId],
  async ([loadedId, itemId]) => {
    if (!loadedId || !itemId) return;
    await focusHighlightedItem(itemId);
  },
  { immediate: true }
);

function handleDemoReset() {
  const openId = form.value?.form_id;
  if (!openId) return;
  formState.clearDraft(openId);
  void formState.load(openId);
}

onMounted(() => {
  if (typeof window !== "undefined") {
    window.addEventListener("cadence:demo-reset", handleDemoReset);
  }
  if (props.formId) void formState.load(props.formId);
});
watch(
  () => props.formId,
  (next, prev) => {
    if (next && form.value?.form_id !== next) void formState.load(next);
    else if (!next && prev) formState.dismiss();
  }
);

defineExpose({ dirty, form });
watch(dirty, (value) => emit("dirty-change", value));
onUnmounted(() => {
  if (typeof window !== "undefined") {
    window.removeEventListener("cadence:demo-reset", handleDemoReset);
  }
  if (dirty.value) emit("dirty-change", false);
});
</script>

<style scoped>
.ecrf-form-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
  min-width: 0;
  max-width: 100%;
}
.ecrf-form-panel > * {
  min-width: 0;
  max-width: 100%;
}
.ecrf-form-panel
  :deep(
    input:not([type="checkbox"]):not([type="radio"]):not(.clinical-input):not(
        .sr-only
      ):not(.visually-hidden)
  ),
.ecrf-form-panel
  :deep(select:not(.clinical-input):not(.sr-only):not(.visually-hidden)) {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 4px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-sizing: border-box;
}
.ecrf-form-panel
  :deep(input[type="checkbox"]:not(.sr-only):not(.visually-hidden)),
.ecrf-form-panel
  :deep(input[type="radio"]:not(.sr-only):not(.visually-hidden)) {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  margin: 0;
  cursor: pointer;
}
.ecrf-form-panel :deep(button:not(.sr-only):not(.visually-hidden)) {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
}
.ecrf-draft-banner {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  padding: 10px 14px;
  border-radius: var(--radius-md, 8px);
  font-size: 0.875rem;
  font-variant-numeric: tabular-nums;
  color: var(--semantic-color-text);
}
.ecrf-draft-banner--restored {
  background-color: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  border-left: 4px solid var(--semantic-color-interactive-primary);
}
.ecrf-draft-banner--stale {
  background-color: var(--semantic-color-edited-row-bg);
  border: 1px solid var(--semantic-color-edited-row-border);
  border-left: 4px solid var(--semantic-color-edited-row-border);
}
.ecrf-draft-banner--locked {
  background-color: var(--semantic-color-surface-subtle);
  border: 1px solid var(--semantic-color-border-control);
  border-left: 4px solid var(--semantic-color-edited-row-border);
}
.ecrf-draft-banner-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1 1 18rem;
  min-width: 0;
}
.ecrf-draft-banner-list {
  margin: 4px 0 0;
  padding-left: 18px;
}
.ecrf-draft-banner-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.ecrf-draft-banner-actions .btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
  padding: 4px 10px;
  border-radius: var(--radius-sm, 6px);
  font-weight: 600;
  cursor: pointer;
}
</style>
