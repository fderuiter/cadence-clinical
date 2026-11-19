// Bootstrap: restores the session, mounts the shell, signs in, loads the
// participant's data, renders the views and starts the offline sync.
import { initHoverDetection } from "ui";

import { isAuthenticatedSession } from "../api/client.js";
import {
  MOCK_ASSIGNMENTS,
  MOCK_INSTRUMENTS,
  MOCK_NOTIFICATIONS,
} from "../data/mock-data.js";
import { logAuditRecord } from "../features/audit.js";
import { restoreSession, signIn, watchUserIdentity } from "../features/auth.js";
import { loadCompliance, renderCompliance } from "../features/compliance.js";
import { loadNotifications, renderInbox } from "../features/inbox.js";
import { checkPINWrapper } from "../features/pin.js";
import { checkPendingReconsent } from "../features/reconsent.js";
import { syncOfflineQueue } from "../features/sync.js";
import {
  loadAssignedInstruments,
  loadAssignedTasks,
  renderTasks,
} from "../features/tasks.js";
import { state } from "../stores/state.js";
import { getAssignmentsFromDB, getInstrumentsFromDB } from "../sync-queue.js";
import { isDemoBuild, isDemoModeActive } from "../utils/demoMode.js";
import { bindPortalControls } from "./controls.js";
import { mountPortal } from "./mount.js";

// Bootstrap Initialization
export async function initializeApp() {
  await restoreSession();

  // Initialize MSW Mock Gateway Worker only in explicit demo builds or opt-in MSW runs
  if (
    typeof window !== "undefined" &&
    (isDemoBuild() || import.meta.env?.VITE_ENABLE_MSW === "true")
  ) {
    import("../mocks/browser.js")
      .then(({ startMswWorker }) => startMswWorker())
      .catch((err) => console.warn("[MSW] Failed to initialize worker:", err));
  }

  // Mount Vue application
  if (typeof document !== "undefined") {
    const appEl = document.getElementById("app");
    if (appEl) {
      mountPortal(appEl);
    }

    // Dynamic Hover Pointer Capability Detection
    initHoverDetection();
  }

  // Graceful OIDC Keycloak setup
  await signIn();

  // Set participant visual name
  const nameEl = document.getElementById("session-subject-id");
  if (nameEl) {
    nameEl.textContent = state.session.userId || "Not signed in";
  }

  // Initialize genesis compliance ledger
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

  if (isAuthenticatedSession()) {
    const userId = state.session.userId;
    await loadAssignedTasks(userId);
    await loadAssignedInstruments(userId);
    await loadCompliance(userId);
    await loadNotifications(userId);
  } else if (isDemoModeActive()) {
    // Explicit demo build: seed synthetic study data
    state.assignments = JSON.parse(JSON.stringify(MOCK_ASSIGNMENTS));
    state.notifications = JSON.parse(JSON.stringify(MOCK_NOTIFICATIONS));
    state.instruments = JSON.parse(JSON.stringify(MOCK_INSTRUMENTS));
  } else if (state.session.isOfflineMode && state.session.userId) {
    // Offline recovery for a previously authenticated participant: use cached IndexedDB records only
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

  // Draw Initial Views
  renderTasks();
  renderCompliance();
  renderInbox();

  // Check for pending active re-consent only when signed in or in demo mode
  if (isAuthenticatedSession() || isDemoModeActive()) {
    await checkPendingReconsent();
  }

  // Graceful Service Worker Registration
  if (
    typeof navigator !== "undefined" &&
    "serviceWorker" in navigator &&
    !window.__MOCK_TEST_ENV__
  ) {
    window.addEventListener("load", () => {
      navigator.serviceWorker
        .register("/subject-portal/sw.js")
        .then((reg) => {
          console.log("Service Worker registered with scope:", reg.scope);
        })
        .catch((err) => {
          console.error("Service Worker registration failed:", err);
        });
    });
  }

  // Initial sync & sync queue rendering (stipend auto-disbursement runs only in demo builds)
  await syncOfflineQueue(false, { autoDisburse: isDemoModeActive() });

  // Attach online/offline listener
  if (typeof window !== "undefined") {
    window.addEventListener("online", () => {
      console.log("[App] Network online detected. Triggering sync...");
      syncOfflineQueue(false, { autoDisburse: isDemoModeActive() });
    });
  }

  // Sync Now, questionnaire, signature modal, eConsent and retry buttons
  bindPortalControls();

  // Switch database context and prompt PIN unlock/setup when active user identity changes
  watchUserIdentity();

  await checkPINWrapper();

  if (typeof window !== "undefined") {
    window.addEventListener("offline", async () => {
      console.log(
        "[App] Network offline detected. Enforcing PIN wrapper for offline operation..."
      );
      state.session.isOfflineMode = true;
      await checkPINWrapper();
    });
  }
}
