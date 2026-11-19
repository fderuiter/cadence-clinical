// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { isScreenAvailable } from "../router/availability.js";
import { activeTabForView, routeForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

// Activates a view: enforces screen availability outside demo builds, updates
// `.active` classes and single-source tab ARIA attributes, and loads consent
// details when the consent view opens.
export function showView(viewId) {
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
  }

  if (
    targetViewId === "view-consent" &&
    typeof loadConsentDetails === "function"
  ) {
    loadConsentDetails();
  }
}
