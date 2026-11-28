import {
  computed,
  getCurrentInstance,
  onUnmounted,
  reactive,
  ref,
  unref,
  watch,
  type MaybeRef,
} from "vue";
import { ApiError } from "../api/client";
import {
  DUPLICATE_SINGLETON_INSTANCE,
  ecrfFormsApi,
  formErrorCode,
  formFieldErrors,
  NOT_READY_TO_SIGN,
  REVISION_STALE,
  SIGNATURE_MEANING_INVALID,
  TARGET_CONTENT_CHANGED,
  TARGET_NOT_SIGNABLE,
  type EcrfForm,
  type EcrfFormItem,
  type EcrfFormPreviewWarning,
  type EcrfFormSaved,
  type EcrfFormValue,
  type EcrfSignatureMeaning,
  type OpenEcrfForm,
} from "../api/ecrfFormsApi";
import {
  formatObservationValue,
  isUnrecordedValue,
  MISSING_REASONS,
  parseFormEntry,
  type KernelObservationValue,
} from "../api/observationValue";
import { registerDraftSource } from "ui";
import { canSignForms } from "../canvas/authority";
import { useAuthStore } from "../stores/auth.js";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";

/** The reason recorded when the screen opens a form; the kernel requires one on every command. */
export const OPEN_FORM_REASON = "Form opened for data entry";

/** Segment naming backed eCRF form drafts under `cadence_user:<tenant>:<sub>:`. */
export const ECRF_FORM_DRAFT_NAMESPACE = "ecrf_form_draft";

const DRAFT_DEBOUNCE_MS = 500;
const PREVIEW_DEBOUNCE_MS = 250;

/** One unsaved item edit preserved when a 423 lock rejects a save. */
export interface EcrfUnsavedItem {
  item_id: string;
  text: string;
  missing: string;
  unit: string | null;
}

/** One saved browser draft item skipped on load because committed server revisions advanced. */
export interface EcrfStaleDraftItem {
  item_id: string;
  draft_display: string;
  draft_revision: number;
  current_revision: number | null;
}

/** One unapplied browser draft item preserved in read-only state when a locked or frozen form loads. */
export interface EcrfLockedDraftItem {
  item_id: string;
  draft_display: string;
  committed_display: string;
}

/** Metadata recorded when `saveValues` returns an inline hydrated `FormResponse` in 1 RTT. */
export interface EcrfHydratedSave {
  formId: string;
  version: number;
  values: EcrfFormSaved["values"];
}

/** What the screen tells the user after a save. */
export type EcrfSaveOutcome =
  | { kind: "saved"; version: number; changed: number }
  | { kind: "stale"; message: string }
  | { kind: "invalid"; message: string }
  | { kind: "locked"; message: string; unsaved: EcrfUnsavedItem[] }
  | { kind: "failed"; message: string };

/** What the screen tells the user after signing the form. */
export type EcrfSignOutcome =
  | { kind: "signed"; meaning: EcrfSignatureMeaning }
  | { kind: "not_ready"; message: string }
  | { kind: "changed"; message: string }
  | { kind: "denied"; message: string }
  | { kind: "invalid"; message: string }
  | { kind: "failed"; message: string };

/** The message a 409 signature answers with: the form changed after it was read, so nothing was signed. */
export const FORM_CHANGED_MESSAGE =
  "This form changed after you opened it, so it was not signed. Reload the form to see the current values, then sign again.";

/** The message a 403 signature answers with. */
export const SIGN_DENIED_MESSAGE =
  "You are not authorised to sign this form; it was not signed.";

/** One item's unsaved entry: typed text, or a reason the value is missing. */
export interface EcrfEntry {
  text: string;
  missing: string;
}

/** One dirty item persisted in browser draft storage alongside the server revision it was edited from. */
export interface EcrfDraftEntry extends EcrfEntry {
  revision: number;
}

/** Account-scoped browser draft payload for one backed eCRF form instance. */
export interface EcrfFormDraft {
  form_id: string;
  items: Record<string, EcrfDraftEntry>;
}

export interface EcrfStorageScope {
  tenantId: string;
  userId: string;
}

/** Formats a draft or rejected-on-lock item entry for human-readable clinical review. */
export function formatUnsavedDraftEntry(entry: EcrfUnsavedItem): string {
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

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function newKey(): string {
  return globalThis.crypto.randomUUID();
}

function browserStorage(): Storage | null {
  try {
    return typeof window !== "undefined" ? window.localStorage || null : null;
  } catch {
    return null;
  }
}

/**
 * One eCRF form instance (ADR-2622): open or reuse it, read it, save a page of values, and sign it (#5463).
 *
 * Unsaved entries are kept against the revisions the form was loaded at (and persisted as an account-scoped browser
 * draft under `cadence_user:<tenant>:<sub>:ecrf_form_draft:<formId>`, never as a server-side draft); nothing is shown
 * as saved until the kernel answers. A save sends each edited item with the revision it was edited from (0 for an item
 * without a value), and the kernel saves all of them or none. A stale revision (409) surfaces per-item errors and asks
 * the user to reload; reloading retains edits whose server revision did not advance while resetting conflicting items,
 * so nobody overwrites a value they have not seen. A failed request is retried with the same idempotency key, so the
 * kernel commits it at most once.
 */
export function useEcrfForm(
  scopeRef?: MaybeRef<EcrfStorageScope | null> | null
) {
  const form = ref<EcrfForm | null>(null);
  const loading = ref(false);
  const loadError = ref<string | null>(null);
  const opening = ref(false);
  const openError = ref<string | null>(null);
  const saving = ref(false);
  const outcome = ref<EcrfSaveOutcome | null>(null);
  const lastHydratedSave = ref<EcrfHydratedSave | null>(null);
  const entries = reactive<Record<string, EcrfEntry>>({});
  const itemErrors = reactive<Record<string, string[]>>({});
  const previewWarnings = reactive<Record<string, EcrfFormPreviewWarning[]>>(
    {}
  );
  const previewing = ref(false);
  let previewTimer: ReturnType<typeof setTimeout> | null = null;
  let previewAbortController: AbortController | null = null;
  let previewSeq = 0;
  let pendingOpen: { request: string; formId: string; key: string } | null =
    null;
  let pendingSave: { request: string; key: string } | null = null;
  const signing = ref(false);
  const signOutcome = ref<EcrfSignOutcome | null>(null);
  let pendingSign: {
    request: string;
    signatureId: string;
    key: string;
  } | null = null;
  let draftTimer: ReturnType<typeof setTimeout> | null = null;
  let pendingDraft: { formId: string; key: string } | null = null;
  let loadedScopeKey: string | null = null;
  let draftCleared = false;
  let draftSaved = false;
  let lastRequestedFormId: string | null = null;
  let loadRequest = 0;

  const restoredDraftItemIds = ref<string[]>([]);
  const comparingBaseline = ref(false);
  const staleDraftItems = ref<EcrfStaleDraftItem[]>([]);
  const lockedDraftItems = ref<EcrfLockedDraftItem[]>([]);

  const items = computed<EcrfFormItem[]>(() => form.value?.items ?? []);
  const locked = computed(() => (form.value?.locks.length ?? 0) > 0);
  const canRecord = computed(() => form.value?.can_record !== false);
  const captureBlocked = computed(
    () => form.value !== null && form.value.can_record === false
  );
  const captureExplanation = computed(
    () => form.value?.capture_explanation ?? null
  );
  const inputLocked = computed(() => locked.value || captureBlocked.value);
  const lockSummaryLabel = computed(() => {
    if (!locked.value && captureBlocked.value) {
      return "Qualification required";
    }
    const kinds = [
      ...new Set((form.value?.locks ?? []).map((held) => held.kind)),
    ];
    return kinds.join(" / ") || "Locked";
  });
  const assertionsLocked = computed(() =>
    (form.value?.locks ?? []).some(
      (held) => held.kind.toLowerCase() === "locked"
    )
  );

  function currentScope(): EcrfStorageScope | null {
    if (scopeRef !== undefined && scopeRef !== null) {
      return unref(scopeRef) || null;
    }
    try {
      return storageScopeOf(useAuthStore());
    } catch {
      return null;
    }
  }

  /** Account-scoped localStorage key for `formId`, or null when unauthenticated or missing claims. */
  function getDraftStorageKey(
    formId: string | null | undefined = form.value?.form_id
  ): string | null {
    if (!formId) return null;
    return scopedKey(currentScope(), ECRF_FORM_DRAFT_NAMESPACE, formId);
  }

  function cancelDraftSave() {
    if (draftTimer !== null) {
      clearTimeout(draftTimer);
      draftTimer = null;
    }
    pendingDraft = null;
  }

  function removeDraftKey(key: string) {
    const ls = browserStorage();
    if (!ls) return;
    try {
      ls.removeItem(key);
    } catch {
      // Ignore unavailable storage or security errors.
    }
  }

  /** Cancels any pending auto-save and removes the saved browser draft for `formId`. */
  function clearDraft(formId: string | null | undefined = form.value?.form_id) {
    cancelDraftSave();
    draftSaved = false;
    draftCleared = true;
    if (!formId || formId === form.value?.form_id) {
      restoredDraftItemIds.value = [];
      comparingBaseline.value = false;
      lockedDraftItems.value = [];
    }
    const key = getDraftStorageKey(formId);
    if (!key) return;
    removeDraftKey(key);
  }

  function clearRecord(record: Record<string, unknown>) {
    for (const key of Object.keys(record)) delete record[key];
  }

  function baselineEntryFor(item: EcrfFormItem): EcrfEntry {
    const unrecorded = isUnrecordedValue(item.value);
    return {
      text:
        !unrecorded && item.value && item.value.type !== "missing"
          ? formatEntryText(item.value)
          : "",
      missing:
        !unrecorded && item.value?.type === "missing"
          ? String(item.value.reason)
          : "",
    };
  }

  function resetEntries() {
    clearRecord(entries);
    clearRecord(itemErrors);
    clearRecord(previewWarnings);
    for (const item of items.value) {
      entries[item.item_id] = baselineEntryFor(item);
    }
  }

  /** The text an entry starts from: a quantity without its unit, since the unit is shown beside the input. */
  function formatEntryText(value: KernelObservationValue): string {
    return value.type === "quantity"
      ? `${value.comparator ?? ""}${value.value}`
      : formatObservationValue(value);
  }

  function missingReasonLabel(reasonCode: string): string {
    const found = MISSING_REASONS.find((opt) => opt.reason === reasonCode);
    return found ? found.text : reasonCode;
  }

  /** Human-readable committed server baseline for inline draft comparison and locked-form recovery banners. */
  function formatCommittedBaseline(item: EcrfFormItem): string {
    if (!item.value || isUnrecordedValue(item.value)) {
      return "No recorded value";
    }
    if (item.value.type === "missing") {
      return `Missing (${missingReasonLabel(String(item.value.reason))})`;
    }
    return formatObservationValue(item.value);
  }

  function formatDraftEntryDisplay(
    saved: Partial<EcrfEntry>,
    item?: EcrfFormItem
  ): string {
    const missing = typeof saved.missing === "string" ? saved.missing : "";
    if (missing) {
      return `Missing (${missingReasonLabel(missing)})`;
    }
    const text = typeof saved.text === "string" ? saved.text.trim() : "";
    if (!text) return "Blank";
    return item?.unit ? `${text} ${item.unit}` : text;
  }

  const initialTextByItemId = computed(() => {
    const byId = new Map<string, string>();
    for (const item of items.value) {
      const current = item.value;
      if (
        !isUnrecordedValue(current) &&
        current &&
        current.type !== "missing"
      ) {
        byId.set(item.item_id, formatEntryText(current).trim());
      }
    }
    return byId;
  });

  function isEdited(item: EcrfFormItem): boolean {
    const entry = entries[item.item_id];
    if (!entry) return false;
    const current = item.value;
    if (isUnrecordedValue(current)) {
      return Boolean(entry.missing) || entry.text.trim().length > 0;
    }
    if (entry.missing) {
      return !(
        current?.type === "missing" && String(current.reason) === entry.missing
      );
    }
    if (current && current.type !== "missing") {
      const initialText =
        initialTextByItemId.value.get(item.item_id) ??
        formatEntryText(current).trim();
      return entry.text.trim() !== initialText;
    }
    return entry.text.trim().length > 0;
  }

  const editedItems = computed(() => items.value.filter(isEdited));
  const dirty = computed(() => editedItems.value.length > 0);
  const draftRestored = computed(
    () => restoredDraftItemIds.value.length > 0 && dirty.value
  );

  function toggleBaselineComparison() {
    comparingBaseline.value = !comparingBaseline.value;
  }

  /** Discards restored/unsaved draft edits and reverts form entries to committed server values. */
  function discardDraft() {
    clearDraft(form.value?.form_id);
    cancelPreview();
    outcome.value = null;
    resetEntries();
  }

  /** Reverts a single edited item back to its committed server value and syncs persisted draft state. */
  function revertItem(itemId: string) {
    const item = items.value.find((candidate) => candidate.item_id === itemId);
    if (!item) return;
    entries[itemId] = baselineEntryFor(item);
    delete itemErrors[itemId];
    delete previewWarnings[itemId];
    restoredDraftItemIds.value = restoredDraftItemIds.value.filter(
      (id) => id !== itemId
    );
    const currentId = form.value?.form_id;
    if (!dirty.value) {
      clearDraft(currentId);
      cancelPreview();
    } else if (currentId && !locked.value) {
      cancelDraftSave();
      const key = getDraftStorageKey(currentId);
      if (key) {
        persistDraft(currentId, key);
      }
    }
  }

  function dismissStaleDraftNotice() {
    staleDraftItems.value = [];
  }

  /** Explicitly removes preserved unapplied draft values for a locked or frozen form. */
  function clearLockedDraft() {
    clearDraft(form.value?.form_id);
    lockedDraftItems.value = [];
  }

  /** Writes the current dirty entries and their base revisions under `key`. */
  function persistDraft(formId: string, key: string) {
    const ls = browserStorage();
    if (!ls) return;
    const draftItems: Record<string, EcrfDraftEntry> = {};
    for (const item of editedItems.value) {
      const entry = entries[item.item_id];
      if (!entry) continue;
      draftItems[item.item_id] = {
        text: entry.text,
        missing: entry.missing,
        revision: Number(item.revision),
      };
    }
    if (Object.keys(draftItems).length === 0) {
      removeDraftKey(key);
      draftSaved = false;
      return;
    }
    const payload: EcrfFormDraft = { form_id: formId, items: draftItems };
    try {
      ls.setItem(key, JSON.stringify(payload));
      draftSaved = true;
    } catch {
      // Skip quota or storage exceptions without interrupting data entry.
    }
  }

  /** Restores saved draft items whose revision matches the loaded form, surfacing stale skips and preserving locked drafts. */
  function restoreDraft(loaded: EcrfForm) {
    draftSaved = false;
    restoredDraftItemIds.value = [];
    comparingBaseline.value = false;
    staleDraftItems.value = [];
    lockedDraftItems.value = [];

    const key = getDraftStorageKey(loaded.form_id);
    if (!key) return;
    const ls = browserStorage();
    if (!ls) return;

    let raw: string | null;
    try {
      raw = ls.getItem(key);
    } catch {
      return;
    }
    if (!raw) return;

    let parsed: Partial<EcrfFormDraft> | null;
    try {
      parsed = JSON.parse(raw) as Partial<EcrfFormDraft>;
    } catch {
      removeDraftKey(key);
      return;
    }
    if (
      !parsed ||
      typeof parsed !== "object" ||
      !parsed.items ||
      typeof parsed.items !== "object"
    ) {
      removeDraftKey(key);
      return;
    }

    const byId = new Map(loaded.items.map((item) => [item.item_id, item]));

    if ((loaded.locks?.length ?? 0) > 0 || loaded.can_record === false) {
      const unapplied: EcrfLockedDraftItem[] = [];
      for (const [itemId, saved] of Object.entries(parsed.items)) {
        if (!saved || typeof saved !== "object") continue;
        const savedText = typeof saved.text === "string" ? saved.text : "";
        const savedMissing =
          typeof saved.missing === "string" ? saved.missing : "";
        const item = byId.get(itemId);
        const committedEntry = entries[itemId];
        const differs = item
          ? savedMissing !== (committedEntry?.missing ?? "") ||
            savedText.trim() !== (committedEntry?.text ?? "").trim()
          : Boolean(savedMissing || savedText.trim());
        if (!differs) continue;
        unapplied.push({
          item_id: itemId,
          draft_display: formatDraftEntryDisplay(saved, item),
          committed_display: item
            ? formatCommittedBaseline(item)
            : "Not present on form",
        });
      }
      if (unapplied.length === 0) {
        removeDraftKey(key);
        return;
      }
      lockedDraftItems.value = unapplied;
      return;
    }

    let discardedAny = false;
    const skippedStale: EcrfStaleDraftItem[] = [];
    const restoredIds: string[] = [];
    for (const [itemId, saved] of Object.entries(parsed.items)) {
      if (!saved || typeof saved !== "object") {
        discardedAny = true;
        continue;
      }
      const item = byId.get(itemId);
      if (!item || Number(saved.revision) !== Number(item.revision)) {
        discardedAny = true;
        skippedStale.push({
          item_id: itemId,
          draft_display: formatDraftEntryDisplay(saved, item),
          draft_revision: Number.isFinite(Number(saved.revision))
            ? Number(saved.revision)
            : 0,
          current_revision: item ? Number(item.revision) : null,
        });
        continue;
      }
      entries[itemId] = {
        text: typeof saved.text === "string" ? saved.text : "",
        missing: typeof saved.missing === "string" ? saved.missing : "",
      };
      if (isEdited(item)) {
        restoredIds.push(itemId);
      }
    }

    staleDraftItems.value = skippedStale;
    restoredDraftItemIds.value = restoredIds;

    if (!dirty.value) {
      removeDraftKey(key);
      draftSaved = false;
      restoredDraftItemIds.value = [];
    } else {
      draftSaved = true;
      if (discardedAny) {
        persistDraft(loaded.form_id, key);
      }
    }
  }

  function applyScheduledDraft(scheduledFormId: string, scheduledKey: string) {
    if (
      !form.value ||
      form.value.form_id !== scheduledFormId ||
      getDraftStorageKey(form.value.form_id) !== scheduledKey
    ) {
      return;
    }
    if (dirty.value && !inputLocked.value) {
      persistDraft(scheduledFormId, scheduledKey);
    } else {
      removeDraftKey(scheduledKey);
      draftSaved = false;
    }
  }

  /** Immediately persists or cleans up any pending debounced browser draft for the active form. */
  function flushDraft() {
    if (draftTimer !== null && pendingDraft !== null) {
      const { formId: scheduledFormId, key: scheduledKey } = pendingDraft;
      cancelDraftSave();
      applyScheduledDraft(scheduledFormId, scheduledKey);
      return;
    }
    cancelDraftSave();
    const current = form.value;
    if (!current || inputLocked.value || !dirty.value || draftCleared) return;
    if (
      !loadedScopeKey ||
      scopedKey(currentScope(), ECRF_FORM_DRAFT_NAMESPACE) !== loadedScopeKey
    ) {
      return;
    }
    const key = getDraftStorageKey(current.form_id);
    if (!key) return;
    persistDraft(current.form_id, key);
  }

  function scheduleDraftSave() {
    cancelDraftSave();
    const current = form.value;
    if (!current || inputLocked.value) return;
    const scheduledKey = getDraftStorageKey(current.form_id);
    if (!scheduledKey) return;
    if (!dirty.value && !draftSaved) return;
    const scheduledFormId = current.form_id;
    pendingDraft = { formId: scheduledFormId, key: scheduledKey };

    draftTimer = setTimeout(() => {
      draftTimer = null;
      pendingDraft = null;
      applyScheduledDraft(scheduledFormId, scheduledKey);
    }, DRAFT_DEBOUNCE_MS);
  }

  watch(
    () => scopedKey(currentScope(), ECRF_FORM_DRAFT_NAMESPACE),
    () => {
      cancelDraftSave();
      loadedScopeKey = null;
      draftSaved = false;
    }
  );

  function abortActivePreview() {
    if (previewAbortController !== null) {
      previewAbortController.abort();
      previewAbortController = null;
    }
  }

  function isAbortError(error: unknown): boolean {
    return (
      (error instanceof DOMException && error.name === "AbortError") ||
      (error instanceof Error && error.name === "AbortError")
    );
  }

  function cancelPreview() {
    if (previewTimer !== null) {
      clearTimeout(previewTimer);
      previewTimer = null;
    }
    abortActivePreview();
    previewSeq++;
    previewing.value = false;
  }

  /** Evaluates pinned study edit-check rule packs in memory for current draft edits without mutating server state. */
  async function runPreview(): Promise<void> {
    if (previewTimer !== null) {
      clearTimeout(previewTimer);
      previewTimer = null;
    }
    abortActivePreview();

    const current = form.value;
    if (
      !current ||
      inputLocked.value ||
      !dirty.value ||
      typeof ecrfFormsApi.previewValues !== "function"
    ) {
      previewSeq++;
      clearRecord(previewWarnings);
      previewing.value = false;
      return;
    }

    const draftValues: EcrfFormValue[] = [];
    for (const item of editedItems.value) {
      const val = valueOf(item);
      if (typeof val === "string") continue;
      draftValues.push({
        item_id: item.item_id,
        value: val,
        expected_revision: Number(item.revision),
        observation_id: null,
        observed_at: null,
      });
    }
    if (draftValues.length === 0) {
      previewSeq++;
      clearRecord(previewWarnings);
      previewing.value = false;
      return;
    }

    const controller = new AbortController();
    previewAbortController = controller;
    const seq = ++previewSeq;
    previewing.value = true;
    try {
      const response = await ecrfFormsApi.previewValues(
        current.form_id,
        draftValues,
        { signal: controller.signal }
      );
      if (controller.signal.aborted || seq !== previewSeq) return;
      clearRecord(previewWarnings);
      for (const warning of response.warnings ?? []) {
        const key = warning.item_id || "__form__";
        if (!previewWarnings[key]) previewWarnings[key] = [];
        previewWarnings[key].push(warning);
      }
    } catch (error) {
      if (controller.signal.aborted || isAbortError(error)) return;
      if (seq === previewSeq) {
        clearRecord(previewWarnings);
      }
    } finally {
      if (previewAbortController === controller) {
        previewAbortController = null;
      }
      if (seq === previewSeq) {
        previewing.value = false;
      }
    }
  }

  function schedulePreview() {
    cancelPreview();
    if (!form.value || inputLocked.value || !dirty.value) {
      clearRecord(previewWarnings);
      return;
    }
    previewTimer = setTimeout(() => {
      previewTimer = null;
      void runPreview();
    }, PREVIEW_DEBOUNCE_MS);
  }

  watch(
    entries,
    () => {
      draftCleared = false;
      if (
        (restoredDraftItemIds.value.length > 0 || comparingBaseline.value) &&
        !dirty.value
      ) {
        restoredDraftItemIds.value = [];
        comparingBaseline.value = false;
      }
      scheduleDraftSave();
      schedulePreview();
    },
    { deep: true }
  );

  const unregisterDraftSource = registerDraftSource({
    flush: flushDraft,
    hasDrafts: () => Boolean(form.value && !inputLocked.value && dirty.value),
  });

  if (getCurrentInstance()) {
    onUnmounted(() => {
      cancelPreview();
      flushDraft();
      unregisterDraftSource();
    });
  }

  const hasSignAuthority = computed(() => {
    if (typeof form.value?.can_sign === "boolean") {
      return form.value.can_sign;
    }
    try {
      const auth = useAuthStore();
      return auth.isAuthenticated ? canSignForms(auth) : true;
    } catch {
      return true;
    }
  });

  const hasReadinessBlockers = computed(
    () => (form.value?.readiness_blockers?.length ?? 0) > 0
  );

  /** A form can be signed as read: the caller holds `collection.sign`, it has values (a content hash), no readiness blockers, and no unsaved entries. */
  const canSign = computed(
    () =>
      hasSignAuthority.value &&
      !!form.value?.content_hash &&
      !hasReadinessBlockers.value &&
      !dirty.value &&
      !saving.value &&
      !signing.value
  );

  async function loadInternal(
    formId: string,
    restoreFromStorage: boolean
  ): Promise<void> {
    cancelDraftSave();
    cancelPreview();
    lastRequestedFormId = formId;
    const request = ++loadRequest;
    loading.value = true;
    loadError.value = null;
    try {
      const loaded = await ecrfFormsApi.getForm(formId);
      if (request !== loadRequest) return;
      loadedScopeKey = scopedKey(currentScope(), ECRF_FORM_DRAFT_NAMESPACE);
      draftCleared = false;
      lastHydratedSave.value = null;
      restoredDraftItemIds.value = [];
      comparingBaseline.value = false;
      staleDraftItems.value = [];
      lockedDraftItems.value = [];
      form.value = loaded;
      resetEntries();
      if (restoreFromStorage) {
        restoreDraft(loaded);
      }
    } catch (error) {
      if (request !== loadRequest) return;
      loadedScopeKey = null;
      restoredDraftItemIds.value = [];
      comparingBaseline.value = false;
      staleDraftItems.value = [];
      lockedDraftItems.value = [];
      form.value = null;
      clearRecord(entries);
      clearRecord(itemErrors);
      clearRecord(previewWarnings);
      loadError.value = messageOf(error);
    } finally {
      if (request === loadRequest) {
        loading.value = false;
      }
    }
  }

  async function load(formId: string): Promise<void> {
    if (form.value && form.value.form_id !== formId) {
      flushDraft();
    }
    await loadInternal(formId, true);
  }

  /** Repeats the last requested form read after a load failure. */
  async function retryLoad(): Promise<void> {
    if (!lastRequestedFormId || loading.value) return;
    await load(lastRequestedFormId);
  }

  /** Reloads the form from the kernel, keeping non-stale edits whose revision did not change and resetting conflicts. */
  async function reload(): Promise<void> {
    if (!form.value) return;
    const currentId = form.value.form_id;
    const prevRestoredIds = new Set(restoredDraftItemIds.value);
    const prevComparing = comparingBaseline.value;
    const staleFields =
      outcome.value?.kind === "stale"
        ? new Set(Object.keys(itemErrors))
        : new Set<string>();
    const editedBeforeReload: Array<{
      itemId: string;
      entry: EcrfEntry;
      revision: number;
      priorItem: EcrfFormItem;
    }> = [];
    for (const item of editedItems.value) {
      const entry = entries[item.item_id];
      if (!entry) continue;
      editedBeforeReload.push({
        itemId: item.item_id,
        entry: { text: entry.text, missing: entry.missing },
        revision: Number(item.revision),
        priorItem: item,
      });
    }

    outcome.value = null;
    signOutcome.value = null;
    pendingSave = null;
    pendingSign = null;
    await loadInternal(currentId, false);

    if (!form.value) return;
    const byId = new Map(items.value.map((item) => [item.item_id, item]));
    if (inputLocked.value) {
      clearDraft(currentId);
      const unapplied: EcrfLockedDraftItem[] = [];
      for (const saved of editedBeforeReload) {
        const nextItem = byId.get(saved.itemId);
        const committedEntry = entries[saved.itemId];
        const differs = nextItem
          ? saved.entry.missing !== (committedEntry?.missing ?? "") ||
            saved.entry.text.trim() !== (committedEntry?.text ?? "").trim()
          : Boolean(saved.entry.missing || saved.entry.text.trim());
        if (!differs) continue;
        unapplied.push({
          item_id: saved.itemId,
          draft_display: formatDraftEntryDisplay(
            saved.entry,
            nextItem ?? saved.priorItem
          ),
          committed_display: nextItem
            ? formatCommittedBaseline(nextItem)
            : "Not present on form",
        });
      }
      lockedDraftItems.value = unapplied;
      return;
    }

    const skippedStale: EcrfStaleDraftItem[] = [];
    const survivingRestored: string[] = [];
    for (const saved of editedBeforeReload) {
      const nextItem = byId.get(saved.itemId);
      if (
        staleFields.has(saved.itemId) ||
        !nextItem ||
        Number(nextItem.revision) !== saved.revision
      ) {
        skippedStale.push({
          item_id: saved.itemId,
          draft_display: formatDraftEntryDisplay(
            saved.entry,
            nextItem ?? saved.priorItem
          ),
          draft_revision: saved.revision,
          current_revision: nextItem ? Number(nextItem.revision) : null,
        });
        continue;
      }
      entries[saved.itemId] = { ...saved.entry };
      if (prevRestoredIds.has(saved.itemId) && isEdited(nextItem)) {
        survivingRestored.push(saved.itemId);
      }
    }
    staleDraftItems.value = skippedStale;
    restoredDraftItemIds.value = survivingRestored;
    comparingBaseline.value = prevComparing && survivingRestored.length > 0;

    const key = getDraftStorageKey(currentId);
    if (key) {
      if (dirty.value) {
        persistDraft(currentId, key);
      } else {
        removeDraftKey(key);
        draftSaved = false;
      }
    }
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
      if (formErrorCode(error) === DUPLICATE_SINGLETON_INSTANCE) {
        const detail =
          error instanceof ApiError
            ? (error.data as { detail?: unknown } | null)?.detail
            : null;
        openError.value =
          typeof detail === "string" && detail.trim().length > 0
            ? detail
            : "An instance of this non-repeating form is already open for this visit. Open the existing form instead.";
      } else {
        openError.value = messageOf(error);
      }
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
    if (current.can_record === false) {
      outcome.value = {
        kind: "failed",
        message:
          current.capture_explanation?.claim ||
          "You lack an active Kernel delegation or qualification to record values on this form.",
      };
      return;
    }
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

    cancelPreview();
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
      staleDraftItems.value = [];
      clearDraft(current.form_id);
      const hydratedForm = (saved as EcrfFormSaved & { form?: EcrfForm | null })
        .form;
      if (
        hydratedForm &&
        hydratedForm.form_id === current.form_id &&
        Number(hydratedForm.version) === Number(saved.version)
      ) {
        loadRequest++;
        loadedScopeKey = scopedKey(currentScope(), ECRF_FORM_DRAFT_NAMESPACE);
        draftCleared = false;
        const versionChanged =
          Number(hydratedForm.version) !== Number(current.version);
        lastHydratedSave.value = versionChanged
          ? {
              formId: hydratedForm.form_id,
              version: Number(hydratedForm.version),
              values: saved.values,
            }
          : null;
        form.value = hydratedForm;
        cancelPreview();
        clearRecord(previewWarnings);
        resetEntries();
      } else {
        lastHydratedSave.value = null;
        await loadInternal(current.form_id, false);
      }
      outcome.value = {
        kind: "saved",
        version: Number(saved.version),
        changed: saved.values.filter((v) => v.changed).length,
      };
    } catch (error) {
      const status = error instanceof ApiError ? error.status : null;
      if (status !== null) pendingSave = null;
      if (status === 409 || formErrorCode(error) === REVISION_STALE) {
        Object.assign(itemErrors, formFieldErrors(error));
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
        const unsaved: EcrfUnsavedItem[] = editedItems.value.map((item) => ({
          item_id: item.item_id,
          text: entries[item.item_id]?.text ?? "",
          missing: entries[item.item_id]?.missing ?? "",
          unit: item.unit ?? null,
        }));
        clearDraft(current.form_id);
        await load(current.form_id);
        outcome.value = {
          kind: "locked",
          message: "This form is locked; nothing was saved.",
          unsaved,
        };
      } else if (status === 403) {
        const explanationClaim =
          error instanceof ApiError
            ? (
                error.data as {
                  explanation?: { claim?: unknown };
                } | null
              )?.explanation?.claim
            : null;
        outcome.value = {
          kind: "failed",
          message:
            typeof explanationClaim === "string" &&
            explanationClaim.trim().length > 0
              ? explanationClaim
              : messageOf(error),
        };
      } else {
        outcome.value = { kind: "failed", message: messageOf(error) };
      }
    } finally {
      saving.value = false;
    }
  }

  /**
   * Signs the form's values as they were read, with a gateway step-up token, then reads the form again.
   *
   * The signature binds the form's `content_hash`: if anyone saved a value since the form was read the kernel refuses
   * it (409) and nothing is signed. A token the gateway refused (401) or a request that never reached the kernel is
   * thrown, so the signature dialog can ask for the credentials again; every other answer is the returned outcome.
   */
  async function sign(
    meaning: EcrfSignatureMeaning,
    sigToken: string,
    reason: string
  ): Promise<EcrfSignOutcome | null> {
    const current = form.value;
    const contentHash = current?.content_hash;
    if (!current || !contentHash || signing.value || dirty.value) return null;
    signOutcome.value = null;

    const sent = JSON.stringify({
      form: current.form_id,
      meaning,
      contentHash,
      reason,
    });
    if (pendingSign?.request !== sent) {
      pendingSign = { request: sent, signatureId: newKey(), key: newKey() };
    }
    signing.value = true;
    try {
      await ecrfFormsApi.signForm(
        current.form_id,
        { meaning, contentHash, signatureId: pendingSign.signatureId },
        sigToken,
        reason,
        pendingSign.key
      );
      pendingSign = null;
      await load(current.form_id);
      signOutcome.value = { kind: "signed", meaning };
    } catch (error) {
      const status = error instanceof ApiError ? error.status : null;
      if (status !== null) pendingSign = null;
      const code = formErrorCode(error);
      if (status === null) throw error;
      if (status === 401) {
        throw new Error(`REAUTHENTICATION_REQUIRED: ${messageOf(error)}`, {
          cause: error,
        });
      }
      if (code === NOT_READY_TO_SIGN) {
        const fieldMessages = Object.values(formFieldErrors(error)).flat();
        const detail = messageOf(error);
        const blockerSummary =
          fieldMessages.length > 0 &&
          !fieldMessages.every((msg) => detail.includes(msg))
            ? `${detail} (${fieldMessages.join("; ")})`
            : detail;
        signOutcome.value = {
          kind: "not_ready",
          message: blockerSummary,
        };
      } else if (code === TARGET_NOT_SIGNABLE) {
        signOutcome.value = {
          kind: "invalid",
          message: "This form has no values to sign; it was not signed.",
        };
      } else if (status === 409 || code === TARGET_CONTENT_CHANGED) {
        signOutcome.value = { kind: "changed", message: FORM_CHANGED_MESSAGE };
      } else if (status === 403) {
        signOutcome.value = { kind: "denied", message: SIGN_DENIED_MESSAGE };
      } else if (status === 422 || code === SIGNATURE_MEANING_INVALID) {
        signOutcome.value = {
          kind: "invalid",
          message: `${messageOf(error)} The form was not signed.`,
        };
      } else {
        signOutcome.value = { kind: "failed", message: messageOf(error) };
      }
    } finally {
      signing.value = false;
    }
    return signOutcome.value;
  }

  function resetState() {
    loadRequest++;
    loadedScopeKey = null;
    lastHydratedSave.value = null;
    restoredDraftItemIds.value = [];
    comparingBaseline.value = false;
    staleDraftItems.value = [];
    lockedDraftItems.value = [];
    form.value = null;
    loading.value = false;
    loadError.value = null;
    outcome.value = null;
    signOutcome.value = null;
    pendingSign = null;
    pendingSave = null;
    cancelPreview();
    clearRecord(entries);
    clearRecord(itemErrors);
    clearRecord(previewWarnings);
  }

  /** Flushes any pending browser draft and clears in-memory state without deleting saved browser drafts. */
  function dismiss() {
    flushDraft();
    lastRequestedFormId = null;
    resetState();
  }

  /** Closes the form on screen, discarding unsaved entries and clearing any saved draft. */
  function close() {
    clearDraft(form.value?.form_id);
    lastRequestedFormId = null;
    resetState();
  }

  return {
    form,
    items,
    entries,
    itemErrors,
    previewWarnings,
    previewing,
    runPreview,
    loading,
    loadError,
    opening,
    openError,
    saving,
    outcome,
    lastHydratedSave,
    signing,
    signOutcome,
    canSign,
    canRecord,
    captureBlocked,
    captureExplanation,
    inputLocked,
    locked,
    lockSummaryLabel,
    assertionsLocked,
    dirty,
    editedItems,
    isEdited,
    draftRestored,
    restoredDraftItemIds,
    comparingBaseline,
    toggleBaselineComparison,
    discardDraft,
    revertItem,
    staleDraftItems,
    dismissStaleDraftNotice,
    lockedDraftItems,
    clearLockedDraft,
    formatCommittedBaseline,
    getDraftStorageKey,
    flushDraft,
    clearDraft,
    load,
    retryLoad,
    reload,
    open,
    save,
    sign,
    dismiss,
    close,
  };
}
