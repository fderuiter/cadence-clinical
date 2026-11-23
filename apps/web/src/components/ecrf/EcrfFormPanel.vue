<template>
  <section id="ecrf-form-instance" class="card ecrf-form-panel">
    <div class="ecrf-panel-header">
      <div class="card-title">Case report form</div>
      <button
        v-if="form && !formState.locked"
        id="ecrf-form-close"
        class="btn btn-secondary ecrf-close-btn"
        type="button"
        :disabled="formState.saving"
        @click="closeForm"
      >
        <X :size="15" aria-hidden="true" />
        Close form
      </button>
    </div>

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
        @begin-raise="beginRaise"
        @submit-page="submitSavePage"
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
        :signatures="signatures"
        @reload-stale="reloadAfterStale"
      />

      <ClinicalFormActionBar
        aria-label="Case report form actions"
        :tray-open="isActionTrayOpen"
        :tray-label="activeTrayLabel"
        :status-hint="actionBarStatusHint"
        :status-tone="actionBarStatusTone"
        :status-hint-id="actionBarStatusHintId"
        :status-hint-test-id="actionBarStatusHintTestId"
      >
        <template #tray>
          <!-- Single-item SDV verify / withdraw reason tray -->
          <div v-if="sdv.pendingSdv" class="ecrf-action-tray-row">
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
                sdv.setSdvReason(($event.target as HTMLInputElement).value)
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
              @click="sdv.cancelPendingSdv()"
            >
              Back
            </button>
          </div>

          <!-- Open query inline tray -->
          <div v-if="raising !== null" class="ecrf-action-tray-row">
            <label for="ecrf-raise-item">Item</label>
            <select
              id="ecrf-raise-item"
              :value="raising.item_id"
              @change="
                onRaiseItemChange(($event.target as HTMLSelectElement).value)
              "
            >
              <option
                v-for="formItem in formState.items"
                :key="formItem.item_id"
                :value="formItem.item_id"
              >
                {{ formItem.item_id }}
              </option>
            </select>
            <label for="ecrf-raise-text">
              <template v-if="raising.unrecorded">
                Missing-data query on {{ raising.item_id }} (no value recorded)
              </template>
              <template v-else>
                Query on {{ raising.item_id }} (revision {{ raising.revision }})
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
              @click="raising = null"
            >
              Back
            </button>
          </div>

          <!-- Bulk SDV selection & reason tray -->
          <div
            v-if="canVerifySource && sdv.unverifiedItems.length > 0"
            v-show="showBulkSdvTray"
            class="ecrf-bulk-sdv-tray"
            data-testid="ecrf-bulk-sdv"
          >
            <div class="ecrf-action-tray-row">
              <strong>
                Source verification ({{ sdv.unverifiedItems.length }} unverified
                {{ sdv.unverifiedItems.length === 1 ? "item" : "items" }})
              </strong>
              <button
                id="ecrf-sdv-select-all"
                class="btn btn-secondary"
                type="button"
                :disabled="sdv.sdvBusy || formState.assertionsLocked"
                @click="sdv.selectAllUnverifiedSdv()"
              >
                Select all unverified
              </button>
              <button
                v-if="sdv.selectedSdvItems.length > 0"
                id="ecrf-sdv-clear-selection"
                class="btn btn-secondary"
                type="button"
                :disabled="sdv.sdvBusy"
                @click="sdv.clearSdvSelection()"
              >
                Clear selection
              </button>
              <button
                v-if="
                  activeTray === 'verify' && sdv.selectedSdvItems.length === 0
                "
                class="btn btn-secondary"
                type="button"
                @click="activeTray = null"
              >
                Close
              </button>
            </div>
            <p
              v-if="formState.assertionsLocked"
              class="ecrf-form-hint"
              data-testid="ecrf-sdv-locked-note"
            >
              This scope is Locked, so source verifications cannot be recorded
              or withdrawn until the lock is released.
            </p>
            <div
              v-else-if="sdv.selectedSdvItems.length > 0"
              class="ecrf-action-tray-row"
            >
              <label for="ecrf-bulk-sdv-reason">
                Verification reason ({{ sdv.selectedSdvItems.length }} selected)
              </label>
              <input
                id="ecrf-bulk-sdv-reason"
                :value="sdv.bulkSdvReason"
                type="text"
                autocomplete="off"
                placeholder="Verified against source medical record"
                :disabled="sdv.sdvBusy"
                @input="
                  sdv.setBulkSdvReason(
                    ($event.target as HTMLInputElement).value
                  )
                "
              />
              <button
                id="ecrf-bulk-sdv-submit"
                class="btn btn-primary"
                type="button"
                :disabled="sdv.sdvBusy || !sdv.bulkSdvReason.trim()"
                @click="sdv.submitBulkSdv()"
              >
                {{
                  sdv.sdvBusy
                    ? "Verifying…"
                    : `Verify selected (${sdv.selectedSdvItems.length})`
                }}
              </button>
            </div>
          </div>

          <!-- Save page reason tray -->
          <div
            v-if="!formState.locked"
            v-show="showSaveTray"
            class="ecrf-action-tray-row"
          >
            <label for="ecrf-save-reason">Reason for change</label>
            <input
              id="ecrf-save-reason"
              v-model="saveReason"
              type="text"
              autocomplete="off"
              placeholder="Initial data entry"
            />
          </div>
        </template>

        <button
          v-if="!formState.locked"
          id="ecrf-form-save"
          class="btn btn-primary"
          type="submit"
          form="ecrf-form-page"
          :disabled="formState.saving || !formState.dirty || !saveReason.trim()"
        >
          <Save :size="15" aria-hidden="true" />
          {{
            formState.saving
              ? "Saving…"
              : `Save page (${formState.editedItems.length})`
          }}
        </button>

        <button
          id="ecrf-action-verify"
          class="btn btn-secondary"
          type="button"
          :disabled="
            !canVerifySource ||
            formState.assertionsLocked ||
            sdv.sdvBusy ||
            sdv.unverifiedItems.length === 0
          "
          :aria-expanded="showBulkSdvTray || Boolean(sdv.pendingSdv)"
          @click="toggleVerifyTray"
        >
          <ShieldCheck :size="15" aria-hidden="true" />
          Verify
        </button>

        <button
          id="ecrf-action-open-query"
          class="btn btn-secondary"
          type="button"
          :disabled="
            !canManageQueries ||
            queries.busy ||
            formState.assertionsLocked ||
            formState.items.length === 0
          "
          :aria-expanded="raising !== null"
          @click="toggleOpenQueryTray"
        >
          <MessageSquarePlus :size="15" aria-hidden="true" />
          Open query
        </button>

        <button
          v-if="signatures.hasSignAuthority"
          id="ecrf-form-sign"
          class="btn btn-primary"
          type="button"
          :disabled="!formState.canSign"
          :aria-describedby="signatures.signHint ? 'ecrf-sign-hint' : undefined"
          @click="signatures.showSignModal = true"
        >
          <FileSignature :size="15" aria-hidden="true" />
          {{ formState.signing ? "Signing…" : "Sign form" }}
        </button>
      </ClinicalFormActionBar>
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
import {
  FileSignature,
  MessageSquarePlus,
  Save,
  ShieldCheck,
  X,
} from "lucide-vue-next";
import { ClinicalFormActionBar } from "ui";
import type { RouteLocationRaw } from "vue-router";
import type { EcrfForm, EcrfFormItem } from "../../api/ecrfFormsApi";
import type {
  FormQueryOwnerRole,
  FormQueryPriority,
  FormQueryResponse,
} from "../../api/ecrfQueriesApi";
import { isUnrecordedValue } from "../../api/observationValue";
import { canAnswerQueries as hasQueryAnswerRole } from "../../canvas/authority";
import {
  collectionInstanceRef,
  observationNodeRef,
} from "../../canvas/nodeRef";
import { confirmDialog } from "../../composables/useConfirmDialog.js";
import { useEcrfForm } from "../../composables/useEcrfForm";
import { useEcrfSdv } from "../../composables/useEcrfSdv";
import { useEcrfSignatures } from "../../composables/useEcrfSignatures";
import { useFormQueries } from "../../composables/useFormQueries";
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
const signatures = reactive(
  useEcrfSignatures({
    form,
    items: rawForm.items,
    dirty,
    signing: rawForm.signing,
    sign: (meaning, sigToken, signingReason) =>
      rawForm.sign(meaning, sigToken, signingReason),
  })
);

const saveReason = ref("");
const activeTray = ref<"verify" | null>(null);
const raising = ref<RaisingQueryTarget | null>(null);
const raiseText = ref("");
const raiseReason = ref("");
const raiseOwnerRole = ref<FormQueryOwnerRole>("site");
const raisePriority = ref<FormQueryPriority>("normal");

const showBulkSdvTray = computed(
  () =>
    Boolean(props.canVerifySource) &&
    sdv.unverifiedItems.length > 0 &&
    !sdv.pendingSdv &&
    raising.value === null &&
    (activeTray.value === "verify" || sdv.selectedSdvItems.length > 0)
);

const showSaveTray = computed(
  () =>
    !formState.locked &&
    !sdv.pendingSdv &&
    raising.value === null &&
    !showBulkSdvTray.value &&
    dirty.value
);

const isActionTrayOpen = computed(
  () =>
    Boolean(sdv.pendingSdv) ||
    raising.value !== null ||
    showBulkSdvTray.value ||
    showSaveTray.value
);

const activeTrayLabel = computed(() => {
  if (sdv.pendingSdv) return "Source verification reason";
  if (raising.value !== null) return "Open clinical query";
  if (showBulkSdvTray.value) return "Bulk source verification";
  return "Reason for change";
});

const actionBarStatusHint = computed(() => {
  if (!signatures.hasSignAuthority) {
    return "Only an authorised investigator (collection.sign) may sign this form.";
  }
  if (signatures.signHint) {
    return signatures.signHint;
  }
  if (formState.locked) {
    return formState.assertionsLocked
      ? "Scope locked: edits, verifications, and queries are disabled."
      : "Values frozen: source verification and queries remain available.";
  }
  return "";
});

const actionBarStatusTone = computed(() =>
  actionBarStatusHint.value ? "gated" : "muted"
);

const actionBarStatusHintId = computed(() => {
  if (!signatures.hasSignAuthority) return "ecrf-sign-unauthorized";
  if (signatures.signHint) return "ecrf-sign-hint";
  return "";
});

const actionBarStatusHintTestId = computed(() => {
  if (!signatures.hasSignAuthority) return "ecrf-sign-unauthorized";
  if (signatures.signHint) return "ecrf-sign-hint";
  return "";
});

function setEntryText(itemId: string, text: string) {
  if (formState.entries[itemId]) formState.entries[itemId].text = text;
}
function setEntryMissing(itemId: string, missing: string) {
  if (formState.entries[itemId]) formState.entries[itemId].missing = missing;
}

function buildRaiseTarget(item: EcrfFormItem): RaisingQueryTarget {
  return {
    item_id: item.item_id,
    observation_id: item.observation_id ?? null,
    revision: item.observation_id ? Number(item.revision) : null,
    unrecorded: !item.observation_id || isUnrecordedValue(item.value),
  };
}

function beginRaise(item: EcrfFormItem) {
  sdv.cancelPendingSdv();
  activeTray.value = null;
  raising.value = buildRaiseTarget(item);
  raiseText.value = "";
  raiseReason.value = "";
  raiseOwnerRole.value = "site";
  raisePriority.value = "normal";
}

function onRaiseItemChange(nextItemId: string) {
  const matched = formState.items.find((it) => it.item_id === nextItemId);
  if (matched) {
    raising.value = buildRaiseTarget(matched);
  }
}

function toggleVerifyTray() {
  raising.value = null;
  sdv.cancelPendingSdv();
  activeTray.value = activeTray.value === "verify" ? null : "verify";
}

function toggleOpenQueryTray() {
  if (raising.value !== null) {
    raising.value = null;
    return;
  }
  const preferred =
    (props.highlightItemId
      ? formState.items.find((it) => it.item_id === props.highlightItemId)
      : undefined) ?? formState.items[0];
  if (preferred) {
    beginRaise(preferred);
  }
}

async function confirmRaise() {
  const target = raising.value;
  const text = raiseText.value.trim();
  const changeReason = raiseReason.value.trim();
  if (!form.value || !target || !text || !changeReason) return;
  const wasUnasked = !target.observation_id;
  const options = {
    ...(raiseOwnerRole.value !== "site"
      ? { ownerRole: raiseOwnerRole.value }
      : {}),
    ...(raisePriority.value !== "normal"
      ? { priority: raisePriority.value }
      : {}),
  };
  const raised = await queries.raise(
    {
      formId: form.value.form_id,
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
    if (wasUnasked) await formState.reload();
  }
}

async function submitSavePage() {
  await formState.save(saveReason.value.trim());
  if (formState.outcome?.kind === "saved") {
    saveReason.value = "";
  }
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
    raising.value = null;
    activeTray.value = null;
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
  () => sdv.selectedSdvItems.length,
  (count) => {
    if (count === 0 && activeTray.value === "verify") {
      activeTray.value = null;
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
.ecrf-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.ecrf-panel-header .card-title {
  margin-bottom: 0;
}
.ecrf-form-panel
  :deep(
    :where(
      input:not([type="checkbox"]):not([type="radio"]):not(.clinical-input):not(
          .sr-only
        ):not(.visually-hidden),
      select:not(.clinical-input):not(.sr-only):not(.visually-hidden)
    )
  ) {
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
  :deep(
    :where(
      input[type="checkbox"]:not(.sr-only):not(.visually-hidden),
      input[type="radio"]:not(.sr-only):not(.visually-hidden)
    )
  ) {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  margin: 0;
  cursor: pointer;
}
.ecrf-form-panel :deep(:where(button:not(.sr-only):not(.visually-hidden))) {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
}
.ecrf-form-panel .ecrf-close-btn,
.ecrf-form-panel .ecrf-action-tray-row button,
.ecrf-form-panel :deep(.clinical-form-action-bar__secondary button),
.ecrf-form-panel :deep(.clinical-form-action-bar__actions button) {
  min-height: var(--form-action-bar-target-min, 44px);
  min-width: var(--form-action-bar-target-min, 44px);
}
.ecrf-close-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.ecrf-action-tray-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.ecrf-action-tray-row input[type="text"],
.ecrf-action-tray-row textarea {
  flex: 1 1 12rem;
  min-width: 0;
}
.ecrf-bulk-sdv-tray {
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.85rem;
}
.ecrf-form-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}
@media (max-width: 640px) {
  .ecrf-action-tray-row {
    flex-direction: column;
    align-items: stretch;
  }
  .ecrf-action-tray-row input[type="text"],
  .ecrf-action-tray-row textarea,
  .ecrf-action-tray-row select,
  .ecrf-action-tray-row button {
    width: 100%;
    flex: 1 1 auto;
  }
}
</style>
