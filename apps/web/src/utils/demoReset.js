import { getActivePinia } from "pinia";
import { resetClinicalSession } from "../auth/sessionCoordinator.js";
import { clearActiveStudyScope } from "../stores/studyScopeBridge";
import { isDemoBuild } from "./demoMode.js";
import { purgeUserScopedStorage } from "./userScopedStorage.js";

/** @type {Set<() => void>} */
const demoStoreResetHooks = new Set();

/**
 * Registers a synchronous callback invoked whenever the demo sandbox resets
 * (for example `() => webDemoStore.reset()`). Using a registration hook keeps
 * `demoStore.js` out of non-demo production cold-boot chunks.
 *
 * @param {() => void} hook
 * @returns {() => void}
 */
export function registerDemoStoreReset(hook) {
  if (typeof hook !== "function") {
    return () => {};
  }
  demoStoreResetHooks.add(hook);
  return () => {
    demoStoreResetHooks.delete(hook);
  };
}

function clearDemoLocalStorageKeys() {
  if (typeof window === "undefined" || !window.localStorage) {
    return;
  }
  try {
    const keysToRemove = [];
    for (let i = 0; i < window.localStorage.length; i += 1) {
      const key = window.localStorage.key(i);
      if (!key) continue;
      if (
        key === "demo_documents" ||
        key === "demo_notifications" ||
        key.startsWith("demo_notifications_") ||
        key.startsWith("ecrf_draft_") ||
        key === "subjects" ||
        key === "formValues" ||
        key === "formQueries" ||
        key === "ledgerBlocks" ||
        key === "currentUsdm" ||
        key === "cadence_clinical_subjects"
      ) {
        keysToRemove.push(key);
      }
    }
    for (const key of keysToRemove) {
      window.localStorage.removeItem(key);
    }
  } catch {
    // Storage may be unavailable in restricted sandboxes
  }
}

function resetMountedStoresSync(pinia) {
  const activePinia = pinia || getActivePinia();
  const registry = activePinia && activePinia._s;

  if (registry) {
    const notificationsStore = registry.get("notifications");
    if (
      notificationsStore &&
      typeof notificationsStore.resetDemoStorage === "function"
    ) {
      notificationsStore.resetDemoStorage();
    }

    const etmfStore = registry.get("etmf");
    if (etmfStore && typeof etmfStore.resetDemoStorage === "function") {
      etmfStore.resetDemoStorage();
    }

    const clinicalStore = registry.get("clinical");
    if (clinicalStore && typeof clinicalStore.$reset === "function") {
      clinicalStore.$reset();
    } else {
      resetClinicalSession(activePinia);
    }

    const studyCanvasStore = registry.get("studyCanvas");
    if (studyCanvasStore) {
      if (typeof studyCanvasStore.$reset === "function") {
        studyCanvasStore.$reset();
      }
      if (typeof studyCanvasStore.clearSessionCaches === "function") {
        studyCanvasStore.clearSessionCaches();
      }
    }
  } else {
    resetClinicalSession(activePinia);
  }
}

/**
 * Synchronously resets registered demo stores, demo localStorage keys,
 * user-scoped drafts, and mounted Pinia stores without eagerly importing
 * lazy clinical or mock modules.
 *
 * @param {{ pinia?: import("pinia").Pinia }} [options]
 */
export function resetDemoSandboxSync(options = {}) {
  if (!isDemoBuild()) {
    return;
  }

  for (const hook of demoStoreResetHooks) {
    try {
      hook();
    } catch {
      // Ignore individual reset listener errors
    }
  }

  clearDemoLocalStorageKeys();
  purgeUserScopedStorage(null);
  clearActiveStudyScope();
  resetMountedStoresSync(options.pinia);
}

/**
 * Full demo sandbox reset invoked by `#btn-reset-demo`, demo logout, and fresh
 * demo sign-in. Restores `webDemoStore`, notifications, eTMF documents,
 * user-scoped drafts, `clinicalStore`, and `studyCanvasStore` to initial state.
 *
 * @param {{ pinia?: import("pinia").Pinia }} [options]
 * @returns {Promise<void>}
 */
export async function resetDemoSandbox(options = {}) {
  if (!isDemoBuild()) {
    return;
  }

  const activePinia = options.pinia || getActivePinia();

  // First run synchronous cleanup so storage and synced study scope are clean before store.$reset() re-reads them
  clearDemoLocalStorageKeys();
  purgeUserScopedStorage(null);
  clearActiveStudyScope();

  if (isDemoBuild()) {
    try {
      const { webDemoStore } = await import("../mocks/demoStore.js");
      if (webDemoStore && typeof webDemoStore.reset === "function") {
        webDemoStore.reset();
      }
    } catch {
      // Fall back to registered hooks below
    }

    for (const hook of demoStoreResetHooks) {
      try {
        hook();
      } catch {
        // Ignore individual reset listener errors
      }
    }

    try {
      const { useNotificationsStore } =
        await import("../stores/notifications.js");
      const notificationsStore = activePinia
        ? useNotificationsStore(activePinia)
        : useNotificationsStore();
      if (typeof notificationsStore.resetDemoStorage === "function") {
        notificationsStore.resetDemoStorage();
      }
    } catch {
      // Pinia not active
    }

    try {
      const { useEtmfStore } = await import("../stores/etmf.js");
      const etmfStore = activePinia
        ? useEtmfStore(activePinia)
        : useEtmfStore();
      if (typeof etmfStore.resetDemoStorage === "function") {
        etmfStore.resetDemoStorage();
      }
    } catch {
      // Pinia not active
    }

    try {
      const { useStudyCanvasStore } = await import("../stores/studyCanvas.ts");
      const studyCanvasStore = activePinia
        ? useStudyCanvasStore(activePinia)
        : useStudyCanvasStore();
      if (typeof studyCanvasStore.$reset === "function") {
        studyCanvasStore.$reset();
      }
      if (typeof studyCanvasStore.clearSessionCaches === "function") {
        studyCanvasStore.clearSessionCaches();
      }
    } catch {
      // Pinia not active
    }

    try {
      const { clearNodeResolutionCache } =
        await import("../canvas/nodeResolver.ts");
      if (typeof clearNodeResolutionCache === "function") {
        clearNodeResolutionCache();
      }
    } catch {
      // Optional cache reset
    }
  }

  // Reset clinical store only if already mounted so cold-boot deferral stays intact;
  // otherwise reset clinicalScope via sessionCoordinator.
  const registry = activePinia && activePinia._s;
  const clinicalStore = registry ? registry.get("clinical") : null;
  if (clinicalStore && typeof clinicalStore.$reset === "function") {
    clinicalStore.$reset();
  } else {
    resetClinicalSession(activePinia);
  }

  if (
    typeof window !== "undefined" &&
    typeof window.dispatchEvent === "function"
  ) {
    window.dispatchEvent(new CustomEvent("cadence:demo-reset"));
  }
}
