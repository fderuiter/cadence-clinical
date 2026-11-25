// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { handoffFocus } from "ui/portal-shell";
import {
  defaultPortalViewId,
  isScreenAvailable,
} from "../router/availability.js";
import { activeTabForView, routeForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { isDemoBuild } from "../utils/demoMode.js";

var viewLeaveHandlers;
var viewEnterHandlers;
let activeFocusRequestSeq = 0;

function getLeaveHandlers() {
  if (!viewLeaveHandlers) {
    viewLeaveHandlers = new Map();
  }
  return viewLeaveHandlers;
}

function getEnterHandlers() {
  if (!viewEnterHandlers) {
    viewEnterHandlers = new Map();
  }
  return viewEnterHandlers;
}

export function registerViewLeaveHandler(viewId, handler) {
  if (typeof handler === "function") {
    getLeaveHandlers().set(viewId, handler);
  }
}

export function registerViewEnterHandler(viewId, handler) {
  if (typeof handler === "function") {
    getEnterHandlers().set(viewId, handler);
  }
}

export function unregisterViewEnterHandler(viewId, handler) {
  const handlers = getEnterHandlers();
  if (!handler || handlers.get(viewId) === handler) {
    handlers.delete(viewId);
  }
}

function escapeSelectorId(id) {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return CSS.escape(id);
  }
  return String(id).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
}

function hasActiveModalOrDrawer() {
  return Boolean(
    state.signatureModalOpen ||
    state.reconsentModalOpen ||
    state.isSyncDrawerOpen ||
    state.disburseModalOpen ||
    state.questionnaireCancelConfirmOpen ||
    state.session?.pinSetup?.isOpen ||
    state.session?.pinUnlock?.isOpen
  );
}

function isSignedOutSession() {
  return (
    !state.session?.userId &&
    !state.session?.token &&
    !state.session?.isDemoMode &&
    !state.session?.isOfflineMode
  );
}

function recordMountedView(targetViewId) {
  if (!Array.isArray(state.mountedViewIds)) {
    state.mountedViewIds = [];
  }
  if (targetViewId === "view-unavailable" && isSignedOutSession()) {
    if (state.mountedViewIds.length > 0) {
      state.mountedViewIds = [];
    }
    return;
  }
  const matchedTarget = routeForView(targetViewId);
  const canMount =
    targetViewId === "view-unavailable" ||
    (matchedTarget &&
      (isDemoBuild() ||
        import.meta.env?.MODE === "test" ||
        matchedTarget.name === "wallet"));
  if (canMount && !state.mountedViewIds.includes(targetViewId)) {
    state.mountedViewIds.push(targetViewId);
  }
}

async function focusActiveViewWhenMounted(targetViewId, focusTarget) {
  const seq = ++activeFocusRequestSeq;
  const escapedId = escapeSelectorId(targetViewId);
  const viewTargets = [
    focusTarget || null,
    () => document.querySelector(`#${escapedId} .view-header h2`),
    () => document.querySelector(`#${escapedId} h2`),
    `#${escapedId}`,
  ];

  const immediateEl = await handoffFocus(viewTargets, {
    includeDefaultFallbacks: false,
  });
  if (
    immediateEl ||
    seq !== activeFocusRequestSeq ||
    state.currentView !== targetViewId
  ) {
    return;
  }

  for (let attempt = 0; attempt < 25; attempt++) {
    await new Promise((resolve) => setTimeout(resolve, 16));
    if (
      typeof document === "undefined" ||
      seq !== activeFocusRequestSeq ||
      state.currentView !== targetViewId ||
      hasActiveModalOrDrawer()
    ) {
      return;
    }
    if (document.getElementById(targetViewId)) {
      const mountedEl = await handoffFocus(viewTargets, {
        awaitNextTick: false,
        includeDefaultFallbacks: false,
      });
      if (mountedEl) {
        return;
      }
    }
  }

  if (
    typeof document !== "undefined" &&
    seq === activeFocusRequestSeq &&
    state.currentView === targetViewId &&
    !hasActiveModalOrDrawer()
  ) {
    await handoffFocus(["#main-content"], { awaitNextTick: false });
  }
}

// Activates a view: enforces screen availability outside demo builds, invokes
// leave callbacks, updates `.active`/`hidden`/`inert` attributes and
// single-source tab ARIA attributes, hands off focus to the active view, and
// loads consent details when opened.
export function showView(viewId, options = {}) {
  const matchedRoute = routeForView(viewId);
  let targetViewId = viewId;

  if (viewId === "view-tasks" && !isScreenAvailable("tasks")) {
    const fallbackView = defaultPortalViewId();
    state.unavailableScreen = null;
    targetViewId = fallbackView;
  } else if (matchedRoute && !isScreenAvailable(matchedRoute.name)) {
    state.unavailableScreen = String(matchedRoute.name);
    targetViewId = "view-unavailable";
  }

  const previousView = state.currentView;
  if (previousView && previousView !== targetViewId) {
    const leaveHandler = getLeaveHandlers().get(previousView);
    if (typeof leaveHandler === "function") {
      try {
        leaveHandler(targetViewId);
      } catch (err) {
        console.warn(`Error running leave handler for ${previousView}:`, err);
      }
    }
  }

  state.currentView = targetViewId;
  recordMountedView(targetViewId);

  if (typeof document !== "undefined") {
    document.querySelectorAll(".portal-view").forEach((v) => {
      const isActive = v.id === targetViewId;
      v.classList.toggle("active", isActive);
      v.hidden = !isActive;
      if (isActive) {
        v.removeAttribute("hidden");
        v.removeAttribute("inert");
        if ("inert" in v) {
          v.inert = false;
        }
      } else {
        v.setAttribute("hidden", "");
        v.setAttribute("inert", "");
        if ("inert" in v) {
          v.inert = true;
        }
      }
    });

    const questionnaireSection = document.getElementById("view-questionnaire");
    if (questionnaireSection) {
      const hasTasksTab = Boolean(document.getElementById("btn-tab-btn-tasks"));
      questionnaireSection.setAttribute(
        "role",
        hasTasksTab ? "tabpanel" : "region"
      );
      if (!questionnaireSection.hasAttribute("tabindex")) {
        questionnaireSection.setAttribute("tabindex", "0");
      }
      questionnaireSection.setAttribute(
        "aria-labelledby",
        hasTasksTab ? "btn-tab-btn-tasks" : "questionnaire-title"
      );
    }

    const activeTab = activeTabForView(targetViewId);
    const mountedList = Array.isArray(state.mountedViewIds)
      ? state.mountedViewIds
      : [];
    for (const tab of tabs) {
      const isSelected = activeTab?.id === tab.id;
      const item = document.getElementById(tab.itemId);
      if (item) {
        item.classList.toggle("active", isSelected);
      }
      const btn =
        document.getElementById(tab.buttonId) || item?.querySelector("button");
      if (btn) {
        btn.setAttribute("aria-selected", isSelected ? "true" : "false");
        btn.setAttribute("tabindex", isSelected ? "0" : "-1");
        const desiredControlsId =
          tab.id === "tasks" && targetViewId === "view-questionnaire"
            ? "view-questionnaire"
            : tab.viewId;
        if (
          targetViewId === desiredControlsId ||
          mountedList.includes(desiredControlsId) ||
          Boolean(document.getElementById(desiredControlsId))
        ) {
          btn.setAttribute("aria-controls", desiredControlsId);
        } else {
          btn.removeAttribute("aria-controls");
        }
      }
    }

    if (!options.preserveTabFocus && !hasActiveModalOrDrawer()) {
      void focusActiveViewWhenMounted(targetViewId, options.focusTarget);
    }
  }

  const enterHandler = getEnterHandlers().get(targetViewId);
  if (typeof enterHandler === "function") {
    try {
      enterHandler(previousView);
    } catch (err) {
      console.warn(`Error running enter handler for ${targetViewId}:`, err);
    }
  }

  if (
    targetViewId === "view-wallet" &&
    previousView === "view-wallet" &&
    isScreenAvailable("wallet") &&
    !state.session?.isDemoMode &&
    !state.walletLoaded &&
    !state.walletLoading
  ) {
    void import("./wallet.js").then(({ loadParticipantWallet }) =>
      loadParticipantWallet().catch(() => {})
    );
  }
}
