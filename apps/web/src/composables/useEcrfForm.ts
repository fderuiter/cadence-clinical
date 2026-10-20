import { computed, reactive, ref } from "vue";
import { ApiError } from "../api/client";
import {
  ecrfFormsApi,
  formErrorCode,
  formFieldErrors,
  REVISION_STALE,
  type EcrfForm,
  type EcrfFormItem,
  type EcrfFormValue,
  type OpenEcrfForm,
} from "../api/ecrfFormsApi";
import {
  formatObservationValue,
  parseFormEntry,
  type KernelObservationValue,
} from "../api/observationValue";

/** The reason recorded when the screen opens a form; the kernel requires one on every command. */
export const OPEN_FORM_REASON = "Form opened for data entry";

/** What the screen tells the user after a save. */
export type EcrfSaveOutcome =
  | { kind: "saved"; version: number; changed: number }
  | { kind: "stale"; message: string }
  | { kind: "invalid"; message: string }
  | { kind: "locked"; message: string }
  | { kind: "failed"; message: string };

/** One item's unsaved entry: typed text, or a reason the value is missing. */
export interface EcrfEntry {
  text: string;
  missing: string;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function newKey(): string {
  return globalThis.crypto.randomUUID();
}

/**
 * One eCRF form instance (ADR-2622): open or reuse it, read it, and save a page of values.
 *
 * Unsaved entries live only here, against the revisions the form was loaded at; nothing is shown as saved until the
 * kernel answers. A save sends each edited item with the revision it was edited from (0 for an item without a value),
 * and the kernel saves all of them or none. A stale revision (409) saves nothing and asks the user to reload; reloading
 * discards the unsaved entries, so nobody overwrites a value they have not seen. A failed request is retried with the
 * same idempotency key, so the kernel commits it at most once.
 */
export function useEcrfForm() {
  const form = ref<EcrfForm | null>(null);
  const loading = ref(false);
  const loadError = ref<string | null>(null);
  const opening = ref(false);
  const openError = ref<string | null>(null);
  const saving = ref(false);
  const outcome = ref<EcrfSaveOutcome | null>(null);
  const entries = reactive<Record<string, EcrfEntry>>({});
  const itemErrors = reactive<Record<string, string[]>>({});
  let pendingOpen: { request: string; formId: string; key: string } | null =
    null;
  let pendingSave: { request: string; key: string } | null = null;

  const items = computed<EcrfFormItem[]>(() => form.value?.items ?? []);
  const locked = computed(() => (form.value?.locks.length ?? 0) > 0);

  function clearRecord(record: Record<string, unknown>) {
    for (const key of Object.keys(record)) delete record[key];
  }

  function resetEntries() {
    clearRecord(entries);
    clearRecord(itemErrors);
    for (const item of items.value) {
      entries[item.item_id] = {
        text:
          item.value && item.value.type !== "missing"
            ? formatEntryText(item.value)
            : "",
        missing:
          item.value?.type === "missing" ? String(item.value.reason) : "",
      };
    }
  }

  /** The text an entry starts from: a quantity without its unit, since the unit is shown beside the input. */
  function formatEntryText(value: KernelObservationValue): string {
    return value.type === "quantity"
      ? `${value.comparator ?? ""}${value.value}`
      : formatObservationValue(value);
  }

  function isEdited(item: EcrfFormItem): boolean {
    const entry = entries[item.item_id];
    if (!entry) return false;
    const current = item.value;
    if (entry.missing) {
      return !(
        current?.type === "missing" && String(current.reason) === entry.missing
      );
    }
    if (current && current.type !== "missing") {
      return entry.text.trim() !== formatEntryText(current).trim();
    }
    return entry.text.trim().length > 0;
  }

  const editedItems = computed(() => items.value.filter(isEdited));
  const dirty = computed(() => editedItems.value.length > 0);

  async function load(formId: string): Promise<void> {
    loading.value = true;
    loadError.value = null;
    try {
      form.value = await ecrfFormsApi.getForm(formId);
      resetEntries();
    } catch (error) {
      form.value = null;
      loadError.value = messageOf(error);
    } finally {
      loading.value = false;
    }
  }

  /** Reloads the form from the kernel and discards unsaved entries. */
  async function reload(): Promise<void> {
    if (!form.value) return;
    outcome.value = null;
    pendingSave = null;
    await load(form.value.form_id);
  }

  /** Opens a new form for a participant's visit and loads it. */
  async function open(
    request: Omit<OpenEcrfForm, "formId" | "idempotencyKey" | "reason">
  ): Promise<void> {
    if (opening.value) return;
    const sent = JSON.stringify(request);
    if (pendingOpen?.request !== sent) {
      pendingOpen = { request: sent, formId: newKey(), key: newKey() };
    }
    opening.value = true;
    openError.value = null;
    outcome.value = null;
    try {
      const state = await ecrfFormsApi.openForm({
        ...request,
        formId: pendingOpen.formId,
        reason: OPEN_FORM_REASON,
        idempotencyKey: pendingOpen.key,
      });
      pendingOpen = null;
      await load(state.form_id);
    } catch (error) {
      // A request that never reached the kernel is retried with the same form id and key.
      if (error instanceof ApiError && error.status !== null)
        pendingOpen = null;
      openError.value = messageOf(error);
    } finally {
      opening.value = false;
    }
  }

  function valueOf(item: EcrfFormItem): KernelObservationValue | string {
    const entry = entries[item.item_id];
    if (entry.missing) {
      return {
        type: "missing",
        reason: entry.missing as Extract<
          KernelObservationValue,
          { type?: "missing" }
        >["reason"],
      };
    }
    const parsed = parseFormEntry(entry.text, item);
    return parsed.ok ? parsed.value : parsed.message;
  }

  /** Saves every edited item in one command, with the reason in the audit trail. */
  async function save(reason: string): Promise<void> {
    const current = form.value;
    if (!current || saving.value || !dirty.value) return;
    clearRecord(itemErrors);
    outcome.value = null;

    const values: EcrfFormValue[] = [];
    for (const item of editedItems.value) {
      const value = valueOf(item);
      if (typeof value === "string") {
        itemErrors[item.item_id] = [value];
        continue;
      }
      values.push({
        item_id: item.item_id,
        value,
        expected_revision: Number(item.revision),
        observation_id: null,
        observed_at: null,
      });
    }
    if (Object.keys(itemErrors).length > 0) {
      outcome.value = {
        kind: "invalid",
        message: "Some values could not be read. Nothing was saved.",
      };
      return;
    }

    const sent = JSON.stringify({ form: current.form_id, values, reason });
    if (pendingSave?.request !== sent) {
      pendingSave = { request: sent, key: newKey() };
    }
    saving.value = true;
    try {
      const saved = await ecrfFormsApi.saveValues({
        formId: current.form_id,
        values,
        reason,
        idempotencyKey: pendingSave.key,
      });
      pendingSave = null;
      await load(current.form_id);
      outcome.value = {
        kind: "saved",
        version: Number(saved.version),
        changed: saved.values.filter((v) => v.changed).length,
      };
    } catch (error) {
      const status = error instanceof ApiError ? error.status : null;
      if (status !== null) pendingSave = null;
      if (status === 409 || formErrorCode(error) === REVISION_STALE) {
        outcome.value = {
          kind: "stale",
          message:
            "Someone saved this form after you opened it, so nothing was saved. Reload the form to see the current values, then enter your changes again.",
        };
      } else if (status === 422) {
        Object.assign(itemErrors, formFieldErrors(error));
        outcome.value = {
          kind: "invalid",
          message: `${messageOf(error)} Nothing was saved.`,
        };
      } else if (status === 423) {
        outcome.value = {
          kind: "locked",
          message: "This form is locked; nothing was saved.",
        };
        await load(current.form_id);
      } else {
        outcome.value = { kind: "failed", message: messageOf(error) };
      }
    } finally {
      saving.value = false;
    }
  }

  /** Closes the form on screen, discarding unsaved entries. */
  function close() {
    form.value = null;
    loadError.value = null;
    outcome.value = null;
    pendingSave = null;
    clearRecord(entries);
    clearRecord(itemErrors);
  }

  return {
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
  };
}
