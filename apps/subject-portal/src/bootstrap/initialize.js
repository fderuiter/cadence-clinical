// Bootstrap: restores the session, signs in, hydrates participant tasks & drafts,
// mounts the shell so initial route guards see authenticated state, renders views,
// and starts offline sync.
import { initHoverDetection } from "ui/portal-shell";

import { isAuthenticatedSession } from "../api/client.js";
import { restoreSession, signIn, watchUserIdentity } from "../features/auth.js";
import { showView } from "../features/navigation.js";
import {
  hydrateSiteSupportContact,
  maskParticipantIdentifier,
} from "../features/site-support.js";
import { isScreenAvailable } from "../router/availability.js";
import {
  getPortalRouter,
  isPortalRouterMounted,
  resetPortalRouterForTests,
  resolveDefaultPortalRoute,
  syncActiveRouteEntities,
  waitForPortalRouterMount,
} from "../router/index.js";
import { state } from "../stores/state.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import { hasTrackedLocalQueueActivity } from "../utils/localQueueActivity.js";
import { checkOnline, resetSyncRetryInfo } from "../utils/networkStatus.js";
import { getMountedPortalContainer, mountPortal, unmountApp } from "./mount.js";

let walletModuleLoaded = false;

// Bootstrap Initialization
export async function initializeApp() {
  if (typeof document !== "undefined" && isPortalRouterMounted()) {
    const previousContainer = getMountedPortalContainer();
    const currentAppEl = document.getElementById("app");
    if (
      !previousContainer ||
      previousContainer.isConnected === false ||
      (currentAppEl && previousContainer !== currentAppEl)
    ) {
      unmountApp();
      resetPortalRouterForTests();
    }
  }
  resetSyncRetryInfo();
  if (walletModuleLoaded || import.meta.env?.MODE === "test") {
    const { resetWalletHydrationAttempt } =
      await import("../features/wallet.js");
    walletModuleLoaded = true;
    resetWalletHydrationAttempt();
  }
  hydrateSiteSupportContact();
  await restoreSession();

  // Initialize and await MSW Mock Gateway Worker in explicit demo builds or opt-in MSW runs before network calls or mounting
  const shouldEnableMsw =
    import.meta.env.MODE === "demo" ||
    import.meta.env.VITE_DEMO_MODE === "true" ||
    import.meta.env.VITE_ENABLE_MSW === "true";

  if (
    typeof window !== "undefined" &&
    !window.__MOCK_TEST_ENV__ &&
    shouldEnableMsw
  ) {
    try {
      const { startMswWorker } = await import("../mocks/browser.js");
      await startMswWorker();
    } catch (err) {
      console.warn("[MSW] Failed to initialize worker:", err);
    }
  }

  // Hydrate OIDC / session credentials BEFORE mounting Vue & evaluating initial route guards
  await signIn();

  // Reconcile pre-mounted router if a caller mounted before initializeApp()
  if (isPortalRouterMounted()) {
    const existingRouter = getPortalRouter();
    await existingRouter.isReady();
    const activeRoute = existingRouter.currentRoute.value;
    if (activeRoute.name === "unavailable") {
      const requestedScreen = activeRoute.query?.screen
        ? String(activeRoute.query.screen)
        : "";
      if (requestedScreen && isScreenAvailable(requestedScreen)) {
        state.unavailableScreen = null;
        await existingRouter.replace({
          name: requestedScreen,
          query: { ...activeRoute.query, screen: undefined },
        });
      } else if (!requestedScreen) {
        const defaultRoute = resolveDefaultPortalRoute();
        if (defaultRoute.name !== "unavailable") {
          state.unavailableScreen = null;
          await existingRouter.replace(defaultRoute);
        }
      }
    } else if (activeRoute.name && !isScreenAvailable(activeRoute.name)) {
      if (activeRoute.path === "/" && activeRoute.name === "tasks") {
        state.unavailableScreen = null;
        await existingRouter.replace(resolveDefaultPortalRoute());
      } else {
        const blocked = String(activeRoute.name);
        state.unavailableScreen = blocked;
        await existingRouter.replace({
          name: "unavailable",
          query: { ...activeRoute.query, screen: blocked },
        });
      }
    }
  } else if (
    !isDemoModeActive() &&
    isScreenAvailable("wallet") &&
    (state.currentView === "view-tasks" ||
      (state.currentView === "view-unavailable" &&
        (!state.unavailableScreen ||
          state.unavailableScreen === "tasks" ||
          state.unavailableScreen === "wallet")))
  ) {
    state.unavailableScreen = null;
    showView("view-wallet");
  }

  // Hydrate available screens' data BEFORE mountPortal so cold reloads see hydrated state
  // while sending zero requests to unmounted interop/econsent endpoints outside demo builds.
  let initialWalletHydration = null;
  if (isAuthenticatedSession()) {
    const userId = state.session.userId;
    if (
      isScreenAvailable("tasks") &&
      (isDemoBuild() || import.meta.env?.MODE === "test")
    ) {
      const { loadAssignedTasks } = await import("../features/tasks.js");
      await loadAssignedTasks(userId);
    } else {
      state.assignments = [];
    }
    if (
      (isScreenAvailable("tasks") || isScreenAvailable("questionnaire")) &&
      (isDemoBuild() || import.meta.env?.MODE === "test")
    ) {
      const { loadAssignedInstruments } = await import("../features/tasks.js");
      await loadAssignedInstruments(userId);
    } else {
      state.instruments = {};
    }
    if (
      isScreenAvailable("compliance") &&
      (isDemoBuild() || import.meta.env?.MODE === "test")
    ) {
      const { loadCompliance } = await import("../features/compliance.js");
      await loadCompliance(userId);
    } else {
      state.compliance = null;
    }
    if (
      isScreenAvailable("inbox") &&
      (isDemoBuild() || import.meta.env?.MODE === "test")
    ) {
      const { loadNotifications } = await import("../features/inbox.js");
      await loadNotifications(userId);
    } else {
      state.notifications = [];
    }
    if (
      isScreenAvailable("wallet") &&
      !state.walletLoaded &&
      !state.walletLoading
    ) {
      const { hasAttemptedWalletHydration, loadParticipantWallet } =
        await import("../features/wallet.js");
      walletModuleLoaded = true;
      if (!hasAttemptedWalletHydration()) {
        initialWalletHydration = loadParticipantWallet().catch((err) => {
          console.warn(
            "[Wallet] Initial wallet hydration warning:",
            err?.message
          );
        });
      }
    }
  } else if (
    isDemoModeActive() &&
    (isDemoBuild() || import.meta.env?.MODE === "test")
  ) {
    const { MOCK_ASSIGNMENTS, MOCK_INSTRUMENTS, MOCK_NOTIFICATIONS } =
      await import("../data/mock-data.js");
    state.assignments = JSON.parse(JSON.stringify(MOCK_ASSIGNMENTS));
    state.notifications = JSON.parse(JSON.stringify(MOCK_NOTIFICATIONS));
    state.instruments = JSON.parse(JSON.stringify(MOCK_INSTRUMENTS));
  } else if (state.session.isOfflineMode && state.session.userId) {
    // Offline recovery for a previously authenticated participant: use cached IndexedDB records only
    const { getAssignmentsFromDB, getInstrumentsFromDB } =
      await import("../sync-queue.js");
    const cachedAssignments = await getAssignmentsFromDB();
    const cachedInstruments = await getInstrumentsFromDB();
    state.assignments = Array.isArray(cachedAssignments)
      ? cachedAssignments
      : [];
    state.instruments = {};
    if (Array.isArray(cachedInstruments)) {
      cachedInstruments.forEach((inst) => {
        state.instruments[inst.id] = inst;
      });
    }
    state.notifications = [];
    state.compliance = null;
  } else {
    // Unauthenticated production session: zero synthetic tasks or overdue alerts
    state.assignments = [];
    state.notifications = [];
    state.instruments = {};
    state.compliance = null;
  }

  if (
    (isDemoBuild() || import.meta.env?.MODE === "test") &&
    (Boolean(state.session.userId) ||
      isDemoModeActive() ||
      import.meta.env?.MODE === "test")
  ) {
    const { hydrateDraftPresence } = await import("../features/tasks.js");
    await hydrateDraftPresence(state.session.userId);
  } else {
    state.draftPresence = {};
  }

  // Mount Vue application & router AFTER session and task state are ready
  let portalRouter = null;
  if (typeof document !== "undefined") {
    const appEl = document.getElementById("app");
    if (appEl) {
      mountPortal(appEl);
      portalRouter = getPortalRouter();
      await portalRouter.isReady().catch(() => {});
      await waitForPortalRouterMount();
    }

    // Dynamic Hover Pointer Capability Detection
    initHoverDetection();
  }

  // Set participant visual name (masked if email/PII)
  const nameEl = document.getElementById("session-subject-id");
  if (nameEl) {
    nameEl.textContent =
      maskParticipantIdentifier(state.session.userId) || "Not signed in";
  }

  // Initialize genesis compliance ledger
  const { logAuditRecord, renderLedger } = await import("../features/audit.js");
  await logAuditRecord(
    "GENESIS",
    {
      platform: import.meta.env.VITE_BRAND_NAME
        ? `${import.meta.env.VITE_BRAND_NAME} MyPortal`
        : "Cadence MyPortal",
      roles: state.session.roles,
    },
    "Patient companion portal session securely booted."
  );

  // Draw Initial Views when demo/standalone containers are mounted
  if (isDemoBuild() || import.meta.env?.MODE === "test") {
    const [
      { renderTasks },
      { renderCompliance },
      { renderInbox },
      { bindPortalControls },
    ] = await Promise.all([
      import("../features/tasks.js"),
      import("../features/compliance.js"),
      import("../features/inbox.js"),
      import("./controls.js"),
    ]);
    renderTasks();
    renderCompliance();
    renderInbox();
    bindPortalControls();
  }
  renderLedger();

  // Restore active route entities (e.g. cold reload on /questionnaire?assignmentId=... or peek query params)
  if (portalRouter) {
    await syncActiveRouteEntities(portalRouter.currentRoute.value);
  }

  // Check for pending active re-consent only when the consent screen is available and signed in or in demo mode
  if (
    (isDemoBuild() || import.meta.env?.MODE === "test") &&
    isScreenAvailable("consent") &&
    (isAuthenticatedSession() || isDemoModeActive())
  ) {
    const { checkPendingReconsent, resolveReconsentStudyId } =
      await import("../features/reconsent.js");
    if (!resolveReconsentStudyId() && initialWalletHydration) {
      await Promise.race([
        initialWalletHydration,
        new Promise((resolve) => setTimeout(resolve, 1500)),
      ]);
    }
    await checkPendingReconsent();
  }

  // Graceful Service Worker Registration (skipped when startMswWorker already registered unified sw.js?msw=1)
  if (
    typeof navigator !== "undefined" &&
    "serviceWorker" in navigator &&
    !window.__MOCK_TEST_ENV__ &&
    !shouldEnableMsw
  ) {
    const swBase = (import.meta.env.BASE_URL || "/subject-portal/").replace(
      /\/?$/,
      "/"
    );
    const registerPortalServiceWorker = () => {
      navigator.serviceWorker
        .register(`${swBase}sw.js`, { scope: swBase })
        .then((reg) => {
          console.log("Service Worker registered with scope:", reg.scope);
        })
        .catch((err) => {
          console.error("Service Worker registration failed:", err);
        });
    };
    if (typeof document !== "undefined" && document.readyState === "complete") {
      registerPortalServiceWorker();
    } else {
      window.addEventListener("load", registerPortalServiceWorker, {
        once: true,
      });
    }
  }

  // Switch database context and prompt PIN unlock/setup before running startup sync
  watchUserIdentity();

  const shouldHydratePinAndSync =
    Boolean(state.session.userId) ||
    isAuthenticatedSession() ||
    isDemoModeActive() ||
    hasTrackedLocalQueueActivity();

  if (shouldHydratePinAndSync) {
    const [{ checkPINWrapper }, { syncOfflineQueue }] = await Promise.all([
      import("../features/pin.js"),
      import("../features/sync.js"),
    ]);
    await checkPINWrapper();
    await syncOfflineQueue(false, {
      autoDisburse: isAuthenticatedSession() || isDemoModeActive(),
    });
  } else {
    state.syncStatusText = checkOnline()
      ? "Online. All submissions synchronized."
      : "Offline. Submissions will be saved locally.";
  }

  // Attach online/offline listener
  if (typeof window !== "undefined") {
    window.addEventListener("online", () => {
      if (!isScreenAvailable("tasks")) {
        return;
      }
      console.log("[App] Network online detected. Triggering sync...");
      void import("../features/sync.js").then(({ syncOfflineQueue }) =>
        syncOfflineQueue(false, {
          autoDisburse: isAuthenticatedSession() || isDemoModeActive(),
        })
      );
    });

    window.addEventListener("offline", async () => {
      console.log(
        "[App] Network offline detected. Enforcing PIN wrapper for offline operation..."
      );
      state.session.isOfflineMode = true;
      if (state.session.userId || isDemoModeActive()) {
        const { checkPINWrapper } = await import("../features/pin.js");
        await checkPINWrapper();
      }
    });
  }
}
