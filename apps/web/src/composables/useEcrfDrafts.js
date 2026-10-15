import {
  ref,
  computed,
  watch,
  unref,
  onUnmounted,
  getCurrentInstance,
} from "vue";

/**
 * Composable for eCRF local draft persistence, debounced auto-save,
 * and dirty field tracking.
 *
 * Storage key format: cadence_ecrf_drafts:<studyId>:<subjectId>:<visitId>
 */
export function useEcrfDrafts(
  studyIdRef,
  subjectIdRef,
  visitIdRef,
  formValuesRef
) {
  const initialValues = ref({});
  const hasDraft = ref(false);
  const draftRestored = ref(false);
  let debounceTimer = null;

  function getStorageKey() {
    const studyId = unref(studyIdRef) || "STUDY-USDM-001";
    const subjectId = unref(subjectIdRef) || "SUBJ-001";
    const visitId = unref(visitIdRef) || "Screening";
    return `cadence_ecrf_drafts:${studyId}:${subjectId}:${visitId}`;
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
    return Object.values(dirtyFields.value).some((v) => v === true);
  });

  /**
   * Immediately save current form values to localStorage under the composite key.
   */
  function saveDraft() {
    if (typeof localStorage === "undefined") return;
    const key = getStorageKey();
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
    if (typeof localStorage === "undefined") return false;
    const key = getStorageKey();
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
    if (debounceTimer) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
    if (typeof localStorage !== "undefined") {
      const key = getStorageKey();
      try {
        localStorage.removeItem(key);
      } catch (e) {
        console.warn("Failed to clear eCRF draft from localStorage:", e);
      }
    }
    hasDraft.value = false;
    draftRestored.value = false;
    setInitialValues(unref(formValuesRef) || {});
  }

  /**
   * Debounced auto-save function (500ms delay)
   */
  function scheduleAutoSave() {
    if (debounceTimer) {
      clearTimeout(debounceTimer);
    }
    debounceTimer = setTimeout(() => {
      if (isDirty.value) {
        saveDraft();
      } else {
        if (typeof localStorage !== "undefined") {
          try {
            localStorage.removeItem(getStorageKey());
          } catch {
            // ignore
          }
        }
        hasDraft.value = false;
      }
    }, 500);
  }

  // Watch form values for changes and trigger debounced auto-save
  watch(
    () => unref(formValuesRef),
    () => {
      scheduleAutoSave();
    },
    { deep: true }
  );

  if (getCurrentInstance()) {
    onUnmounted(() => {
      if (debounceTimer) {
        clearTimeout(debounceTimer);
      }
    });
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
