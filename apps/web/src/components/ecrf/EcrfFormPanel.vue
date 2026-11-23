<template>
  <section id="ecrf-form-instance" class="card ecrf-form-panel">
    <div class="card-title">Case report form</div>

    <EcrfFormOpener
      v-if="!form"
      :form-id="formId"
      :enrollment-id="enrollmentId"
      :collection-spec-id="collectionSpecId"
      :planned-activity-id="plannedActivityId"
      :enrollment-context="enrollmentContext"
      :schedule-loading="scheduleLoading"
      :schedule-error="scheduleError"
      :loading="formState.loading"
      :load-error="formState.loadError"
      :opening="formState.opening"
      :open-error="formState.openError"
      @load-form="void formState.load($event)"
      @open-form="void formState.open($event)"
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
export type { CanvasFormEnrollmentContext } from "./EcrfFormOpener.vue";
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
}

async function focusHighlightedItem(itemId: string) {
  await nextTick();
  document
    .getElementById(`ecrf-row-${itemId}`)
    ?.scrollIntoView?.({ block: "center" });
  const input = document.getElementById(
    `ecrf-item-${itemId}`
  ) as HTMLInputElement | null;
  if (input && !input.disabled) input.focus?.();
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

onMounted(() => {
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
</style>
