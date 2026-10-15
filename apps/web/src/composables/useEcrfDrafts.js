import {
  ref,
  computed,
  watch,
  unref,
  onUnmounted,
  getCurrentInstance,
} from "vue";

import { useAuthStore } from "../stores/auth.js";
import { scopedKey, storageScopeOf } from "../utils/userScopedStorage.js";

/**
 * Composable for eCRF local draft persistence, debounced auto-save,
 * and dirty field tracking.
 *
 * Drafts belong to one account (#5369). The key names the tenant and user from
 * the verified sign-in token, then the study, subject and visit:
 * `cadence_user:<tenant>:<user>:ecrf_draft:<studyId>:<subjectId>:<visitId>`.
 * When any of those is missing there is no key, and nothing is saved or
 * restored; no identity, study, subject or visit is ever assumed.
 *
 * @param scopeRef optional ref of `{ tenantId, userId }`; defaults to the
 *   signed-in account from the auth store.
 */
export function useEcrfDrafts(
  studyIdRef,
  subjectIdRef,
  visitIdRef,
  formValuesRef,
  scopeRef = null
) {
  const initialValues = ref({});
  const hasDraft = ref(false);
  const draftRestored = ref(false);
  let debounceTimer = null;

  const scope = scopeRef
    ? computed(() => unref(scopeRef) || null)
    : computed(() => storageScopeOf(useAuthStore()));

  /** The draft key for the current account and form, or null. */
  function getStorageKey() {
    return scopedKey(
      scope.value,
      "ecrf_draft",
      unref(studyIdRef),
      unref(subjectIdRef),
      unref(visitIdRef)
    );
  }

  /**
   * Set the baseline initial values for calculating dirty state.
   */
  function setInitialValues(values = {}) {
    initialValues.value = JSON.parse(JSON.stringify(unref(values) || {}));
  }

  /**
   * Computed map of dirty field IDs: fieldId -> boolean
   */
  const dirtyFields = computed(() => {
    const current = unref(formValuesRef) || {};
    const init = initialValues.value || {};
    const fields = new Set([...Object.keys(current), ...Object.keys(init)]);
    const map = {};

    fields.forEach((fieldId) => {
      const curVal =
        current[fieldId] !== undefined && current[fieldId] !== null
          ? String(current[fieldId])
          : "";
      const initVal =
        init[fieldId] !== undefined && init[fieldId] !== null
          ? String(init[fieldId])
          : "";
      if (curVal !== initVal) {
        map[fieldId] = true;
      }
    });

    return map;
  });

  /**
   * Computed boolean flag indicating whether any field in the form is dirty.
   */
  const isDirty = computed(() => {
    return Object.values(dirtyFields.value).some(Boolean);
  });

  /**
   * Immediately save current form values to localStorage under the composite key.
   */
  function saveDraft() {
    if (typeof localStorage === "undefined") return;
    const key = getStorageKey();
    if (!key) return;
    const values = unref(formValuesRef) || {};
    try {
      localStorage.setItem(key, JSON.stringify(values));
      hasDraft.value = true;
    } catch (e) {
      console.warn("Failed to save eCRF draft to localStorage:", e);
    }
  }

  /**
   * Attempt to restore a draft from localStorage for the current key.
   * Returns true if a draft was found and loaded.
   */
  function restoreDraft() {
    hasDraft.value = false;
    draftRestored.value = false;
    if (typeof localStorage === "undefined") return false;
    const key = getStorageKey();
    if (!key) return false;
    try {
      const raw = localStorage.getItem(key);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (parsed && typeof parsed === "object") {
          const currentObj = unref(formValuesRef);
          if (currentObj && typeof currentObj === "object") {
            Object.assign(currentObj, parsed);
          }
          hasDraft.value = true;
          draftRestored.value = true;
          return true;
        }
      }
    } catch (e) {
      console.warn("Failed to restore eCRF draft from localStorage:", e);
    }
    hasDraft.value = false;
    draftRestored.value = false;
    return false;
  }

  /**
   * Clear draft from localStorage and reset draft state flags.
   */
  function clearDraft() {
    cancelAutoSave();
    if (typeof localStorage !== "undefined") {
      const key = getStorageKey();
      try {
        if (key) localStorage.removeItem(key);
      } catch (e) {
        console.warn("Failed to clear eCRF draft from localStorage:", e);
      }
    }
    hasDraft.value = false;
    draftRestored.value = false;
    setInitialValues(unref(formValuesRef) || {});
  }

  function cancelAutoSave() {
    if (debounceTimer) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
  }

  /**
   * Debounced auto-save function (500ms delay). The key is fixed when the save
   * is scheduled: if the account or form changes before it fires, the pending
   * values are dropped instead of being written under the new key.
   */
  function scheduleAutoSave() {
    cancelAutoSave();
    const scheduledKey = getStorageKey();
    if (!scheduledKey) return;
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      if (getStorageKey() !== scheduledKey) return;
      if (isDirty.value) {
        saveDraft();
      } else {
        if (typeof localStorage !== "undefined") {
          try {
            localStorage.removeItem(scheduledKey);
          } catch {
            // ignore
          }
        }
        hasDraft.value = false;
      }
    }, 500);
  }

  // A different account (or sign-out) never inherits this one's draft state.
  watch(
    () => scopedKey(scope.value, "ecrf_draft"),
    () => {
      cancelAutoSave();
      hasDraft.value = false;
      draftRestored.value = false;
    }
  );

  // Watch form values for changes and trigger debounced auto-save
  watch(
    () => unref(formValuesRef),
    () => {
      scheduleAutoSave();
    },
    { deep: true }
  );

  if (getCurrentInstance()) {
    onUnmounted(cancelAutoSave);
  }

  return {
    getStorageKey,
    initialValues,
    dirtyFields,
    isDirty,
    hasDraft,
    draftRestored,
    setInitialValues,
    saveDraft,
    restoreDraft,
    clearDraft,
  };
}
