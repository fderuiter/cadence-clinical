// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { handoffFocus } from "ui";
import { isScreenAvailable } from "../router/availability.js";
import { activeTabForView, routeForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

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
    state.session?.pinSetup?.isOpen ||
    state.session?.pinUnlock?.isOpen
  );
}

// Activates a view: enforces screen availability outside demo builds, updates
// `.active` classes and single-source tab ARIA attributes, and loads consent
// details when the consent view opens.
export function showView(viewId, options = {}) {
  const matchedRoute = routeForView(viewId);
  let targetViewId = viewId;

  if (matchedRoute && !isScreenAvailable(matchedRoute.name)) {
    state.unavailableScreen = String(matchedRoute.name);
    targetViewId = "view-unavailable";
  }

  state.currentView = targetViewId;

  if (typeof document !== "undefined") {
    document.querySelectorAll(".portal-view").forEach((v) => {
      v.classList.toggle("active", v.id === targetViewId);
    });

    const activeTab = activeTabForView(targetViewId);
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
      }
    }

    if (!options.preserveTabFocus && !hasActiveModalOrDrawer()) {
      const escapedId = escapeSelectorId(targetViewId);
      void handoffFocus([
        options.focusTarget || null,
        () => document.querySelector(`#${escapedId} .view-header h2`),
        () => document.querySelector(`#${escapedId} h2`),
        `#${escapedId}`,
        "#main-content",
      ]);
    }
  }

  if (
    targetViewId === "view-consent" &&
    typeof loadConsentDetails === "function"
  ) {
    loadConsentDetails();
  }
}
