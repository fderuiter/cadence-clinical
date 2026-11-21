import { getCurrentScope, onScopeDispose, ref } from "vue";

const draftSources = new Set();

/**
 * Registers an active form or questionnaire draft source so sign-out requests
 * flush pending debounced edits and prompt before discarding unsaved local state.
 *
 * Automatically unregisters when the enclosing Vue effect scope disposes.
 *
 * @param {{
 *   flush?: () => void | Promise<void>,
 *   hasDrafts?: () => boolean,
 * }} source
 * @returns {() => void} Unregister callback.
 */
export function registerDraftSource(source) {
  if (!source || typeof source !== "object") {
    return () => {};
  }
  const entry = {
    flush: typeof source.flush === "function" ? source.flush : null,
    hasDrafts: typeof source.hasDrafts === "function" ? source.hasDrafts : null,
  };
  draftSources.add(entry);

  let disposed = false;
  const unregister = () => {
    if (!disposed) {
      disposed = true;
      draftSources.delete(entry);
    }
  };

  if (getCurrentScope()) {
    onScopeDispose(unregister);
  }

  return unregister;
}

/**
 * Synchronously invokes flush callbacks on all registered draft sources.
 * Useful when involuntary session expiration is about to clear in-memory user claims.
 */
export function flushRegisteredDraftSourcesSync() {
  const entries = Array.from(draftSources);
  for (const entry of entries) {
    if (entry.flush) {
      try {
        void entry.flush();
      } catch {
        // Ignore synchronous flush failures during session cleanup.
      }
    }
  }
}

/**
 * Flushes all currently registered draft sources in registration order.
 *
 * @returns {Promise<void>}
 */
export async function flushRegisteredDraftSources() {
  const entries = Array.from(draftSources);
  for (const entry of entries) {
    if (entry.flush) {
      await entry.flush();
    }
  }
}

/**
 * Checks whether any registered draft source currently holds unsaved browser-only state.
 *
 * @returns {boolean}
 */
export function hasRegisteredUnsavedDrafts() {
  for (const entry of draftSources) {
    if (entry.hasDrafts && entry.hasDrafts()) {
      return true;
    }
  }
  return false;
}

/**
 * Clears registered draft sources between isolated unit tests.
 */
export function resetDraftSourcesForTests() {
  draftSources.clear();
}

/**
 * Shared sign-out guard composable that flushes pending debounced edits and
 * opens a confirmation modal whenever unsaved local drafts are present.
 *
 * @param {{
 *   onConfirmSignOut?: () => void | Promise<void>,
 *   flushExternalDrafts?: () => void | Promise<void>,
 *   hasExternalDrafts?: () => boolean,
 * }} [options]
 */
export function useSignOutGuard(options = {}) {
  const isConfirmOpen = ref(false);
  const isFlushing = ref(false);

  async function requestSignOut() {
    isFlushing.value = true;
    try {
      await flushRegisteredDraftSources();
      if (typeof options.flushExternalDrafts === "function") {
        await options.flushExternalDrafts();
      }
    } finally {
      isFlushing.value = false;
    }

    const hasUnsaved =
      hasRegisteredUnsavedDrafts() || Boolean(options.hasExternalDrafts?.());
    if (hasUnsaved) {
      isConfirmOpen.value = true;
      return { prompted: true, signedOut: false };
    }

    await options.onConfirmSignOut?.();
    return { prompted: false, signedOut: true };
  }

  async function confirmSignOut() {
    isConfirmOpen.value = false;
    await options.onConfirmSignOut?.();
  }

  function cancelSignOut() {
    isConfirmOpen.value = false;
  }

  return {
    isConfirmOpen,
    isFlushing,
    requestSignOut,
    confirmSignOut,
    cancelSignOut,
  };
}
