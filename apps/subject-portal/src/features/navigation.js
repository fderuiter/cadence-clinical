// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { activeTabForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

// Activates a view: state (which the router follows into the URL), synchronous
// DOM reflection for immediate callers, and loading the consent form when the
// consent view opens.
export function showView(viewId) {
  state.currentView = viewId;

  if (typeof document !== "undefined") {
    document.querySelectorAll(".portal-view").forEach((v) => {
      v.classList.toggle("active", v.id === viewId);
    });

    const activeTab = activeTabForView(viewId);
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

  if (viewId === "view-consent" && typeof loadConsentDetails === "function") {
    loadConsentDetails();
  }
}
