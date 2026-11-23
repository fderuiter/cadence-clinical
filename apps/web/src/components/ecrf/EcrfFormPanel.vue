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
        :reason="reason"
        :highlight-item-id="highlightItemId"
        :can-verify-source="canVerifySource"
        :can-manage-queries="canManageQueries"
        :queries-busy="queries.busy"
        :raising="raising"
        :raise-text="raiseText"
        :live-on="liveOn"
        :item-observation-link="itemObservationLink"
        @entry-text="setEntryText"
        @entry-missing="setEntryMissing"
        @begin-raise="beginRaise"
        @update:raise-text="raiseText = $event"
        @confirm-raise="confirmRaise"
        @cancel-raise="raising = null"
        @update:reason="reason = $event"
        @submit="submit"
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
        :query-form-link="queryFormLink"
        :query-observation-link="queryObservationLink"
        :assertions="form.assertions"
        :item-observation-link="itemObservationLink"
        @retry-queries="queries.reload"
        @reload-stale="reloadAfterStale"
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
  ref,
  watch,
} from "vue";
import type { RouteLocationRaw } from "vue-router";
import { isUnrecordedValue } from "../../api/observationValue";
import type { EcrfForm, EcrfFormItem } from "../../api/ecrfFormsApi";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
import { canAnswerQueries as hasQueryAnswerRole } from "../../canvas/authority";
import { observationNodeRef } from "../../canvas/nodeRef";
import { useEcrfForm } from "../../composables/useEcrfForm";
import { useEcrfSdv } from "../../composables/useEcrfSdv";
import { isLiveQuery, useFormQueries } from "../../composables/useFormQueries";
import { useAuthStore } from "../../stores/auth.js";
import EcrfAssertionsList from "./EcrfAssertionsList.vue";
import EcrfFormOpener, {
  type CanvasFormEnrollmentContext,
} from "./EcrfFormOpener.vue";
import EcrfItemTable, { type RaisingQueryTarget } from "./EcrfItemTable.vue";
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
      formId: string
    ) => RouteLocationRaw | null;
    formLocation?: (formId: string) => RouteLocationRaw | null;
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
const { form, dirty } = rawForm;
const formState = reactive(rawForm);
const sdv = reactive(
  useEcrfSdv({ form, items: rawForm.items, reload: rawForm.reload })
);

const reason = ref("");
const queries = reactive(useFormQueries());
const raising = ref<RaisingQueryTarget | null>(null);
const raiseText = ref("");

function setEntryText(itemId: string, text: string) {
  if (formState.entries[itemId]) formState.entries[itemId].text = text;
}
function setEntryMissing(itemId: string, missing: string) {
  if (formState.entries[itemId]) formState.entries[itemId].missing = missing;
}

function itemObservationLink(
  observationId: string | null | undefined
): RouteLocationRaw | null {
  if (!hasRouter || !observationId || !form.value) return null;
  if (props.observationLocation) {
    return props.observationLocation(observationId, form.value.form_id);
  }
  const refNode = observationNodeRef(observationId);
  return refNode
    ? { name: "observation", params: { observationRef: refNode } }
    : null;
}

const queryObservationLink = (q: FormQueryResponse) =>
  itemObservationLink(q.observation_id);
const queryFormLink = computed(() =>
  props.formLocation
    ? (q: FormQueryResponse) => props.formLocation!(q.form_id)
    : undefined
);
const effectiveCanAnswerQueries = computed(() =>
  props.canAnswerQueries !== undefined
    ? props.canAnswerQueries
    : authStore.isAuthenticated
      ? hasQueryAnswerRole(authStore)
      : !props.canManageQueries
);

function liveOn(item: EcrfFormItem) {
  return form.value
    ? queries.forItem(form.value.form_id, item.item_id).filter(isLiveQuery)
    : [];
}

function beginRaise(item: EcrfFormItem) {
  raising.value = {
    item_id: item.item_id,
    observation_id: item.observation_id ?? null,
    revision: item.observation_id ? Number(item.revision) : null,
    unrecorded: !item.observation_id || isUnrecordedValue(item.value),
  };
  raiseText.value = "";
}

async function confirmRaise() {
  const target = raising.value;
  const text = raiseText.value.trim();
  if (!form.value || !target || !text) return;
  const wasUnasked = !target.observation_id;
  const raised = await queries.raise(
    {
      formId: form.value.form_id,
      itemId: target.item_id,
      observationId: target.observation_id,
      revision: target.revision,
    },
    text
  );
  if (raised) {
    raising.value = null;
    if (wasUnasked) await formState.reload();
  }
}

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
async function submit() {
  await formState.save(reason.value.trim());
  if (formState.outcome?.kind === "saved") reason.value = "";
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
  formState.clearDraft();
  formState.close();
  emit("closed");
}

watch(
  () => (form.value ? `${form.value.form_id}@${form.value.version}` : null),
  (loaded) => {
    raising.value = null;
    sdv.cancelPendingSdv();
    if (loaded && form.value) {
      emit("form-loaded", form.value);
      void queries.load({ formId: form.value.form_id });
    } else {
      queries.clear();
    }
  }
);

watch(
  [() => form.value?.form_id, () => props.highlightItemId],
  async ([loadedId, itemId]) => {
    if (!loadedId || !itemId) return;
    await nextTick();
    document
      .getElementById(`ecrf-row-${itemId}`)
      ?.scrollIntoView?.({ block: "center" });
    const input = document.getElementById(
      `ecrf-item-${itemId}`
    ) as HTMLInputElement | null;
    if (input && !input.disabled) input.focus?.();
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
</style>
