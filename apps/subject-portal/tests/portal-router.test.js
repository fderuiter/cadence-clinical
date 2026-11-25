// The router keeps the URL and state.currentView in step, and the `state`
// facade reads and writes the Pinia stores.
import "fake-indexeddb/auto";
import { describe, it, expect, beforeAll, beforeEach, vi } from "vitest";

import { initializeApp, showView, state, unmountApp } from "../src/index.js";
import {
  AUTHENTICATED_SCREENS,
  AVAILABLE_SCREENS,
  DEMO_AVAILABLE_SCREENS,
  screenTitle,
} from "../src/router/availability.js";
import { getPortalRouter } from "../src/router/index.js";
import {
  activeTabForView,
  portalRouteDescriptors,
  tabs,
  viewRoutes,
} from "../src/router/routes.js";
import { useSyncStore } from "../src/stores/sync.js";
import { useUiStore } from "../src/stores/ui.js";

const settle = () => new Promise((resolve) => setTimeout(resolve, 20));

describe("Subject portal router and stores", () => {
  let router;

  beforeAll(async () => {
    window.__MOCK_TEST_ENV__ = true;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });
    document.body.innerHTML = `<div id="app"></div>`;
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    state.currentView = "view-tasks";
    await initializeApp();
    router = getPortalRouter();
    await router.isReady();
  });

  beforeEach(async () => {
    state.session.token = null;
    state.session.userId = null;
    state.session.isOfflineMode = true;
    state.session.isDemoMode = true;
    showView("view-tasks");
    await settle();
  });

  it("has one route per view and a tab for every view but the questionnaire", async () => {
    expect(viewRoutes.map((route) => route.meta.viewId)).toEqual([
      "view-tasks",
      "view-questionnaire",
      "view-compliance",
      "view-inbox",
      "view-consent",
      "view-wallet",
      "view-lay-summaries",
    ]);
    expect(tabs.map((tab) => tab.id)).toEqual([
      "tasks",
      "compliance",
      "inbox",
      "consent",
      "wallet",
      "lay-summaries",
    ]);

    const emojiPattern = /\p{Extended_Pictographic}/u;
    for (const tab of tabs) {
      expect(["function", "object"]).toContain(typeof tab.icon);
      expect(emojiPattern.test(tab.label)).toBe(false);
    }

    const previousDemoMode = state.session.isDemoMode;
    state.session.isDemoMode = true;
    await settle();

    for (const tab of tabs) {
      const btn = document.getElementById(tab.buttonId);
      expect(btn).not.toBeNull();
      expect(emojiPattern.test(btn.textContent || "")).toBe(false);
      const svg = btn.querySelector("svg.lucide");
      expect(svg).not.toBeNull();
      expect(svg.getAttribute("aria-hidden")).toBe("true");
      expect(svg.getAttribute("width")).toBe("16");
      expect(svg.getAttribute("height")).toBe("16");
    }

    state.session.isDemoMode = previousDemoMode;
    await settle();
  });

  it("navigates to the view's route when showView is called in demo mode", async () => {
    showView("view-compliance");
    expect(state.currentView).toBe("view-compliance");
    await settle();
    expect(router.currentRoute.value.path).toBe("/compliance");
    expect(
      document
        .querySelector("#tab-btn-compliance button")
        .getAttribute("aria-selected")
    ).toBe("true");
  });

  it("follows a direct write to state.currentView and redirects unavailable views outside demo mode", async () => {
    state.session.userId = "subject_001";
    state.session.token = "jwt_valid_subject_token"; // pragma: allowlist secret
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.currentView = "view-wallet";
    await settle();
    expect(router.currentRoute.value.path).toBe("/wallet");
    expect(state.currentView).toBe("view-wallet");

    state.currentView = "view-consent";
    await settle();
    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(state.currentView).toBe("view-unavailable");

    state.session.token = null;
    await settle();
    expect(router.currentRoute.value.path).toBe("/unavailable");
    expect(state.currentView).toBe("view-unavailable");

    // Signed-in participant outside demo mode: wallet is available and root/unknown paths route to wallet without looping
    state.session.userId = "subj-live-01";
    state.session.token = "tok-live-01";
    state.session.isOfflineMode = false;
    await router.push("/");
    await settle();
    expect(router.currentRoute.value.path).toBe("/wallet");
    expect(state.currentView).toBe("view-wallet");

    await router.push("/unbacked-unknown-path");
    await settle();
    expect(router.currentRoute.value.path).toBe("/wallet");
    expect(state.currentView).toBe("view-wallet");
  });

  it("activates the view of a route the router navigates to", async () => {
    await router.push("/inbox");
    expect(state.currentView).toBe("view-inbox");
    expect(document.getElementById("view-inbox").classList).toContain("active");

    router.back();
    await settle();
    expect(state.currentView).toBe("view-tasks");
  });

  it("keeps the last of two quick view changes", async () => {
    showView("view-consent");
    showView("view-inbox");
    await settle();
    expect(router.currentRoute.value.path).toBe("/inbox");
    expect(state.currentView).toBe("view-inbox");
  });

  it("keeps the current view when the portal is mounted again", async () => {
    unmountApp();
    document.body.innerHTML = `<div id="app"></div>`;
    state.currentView = "view-compliance";
    await initializeApp();
    await settle();
    expect(state.currentView).toBe("view-compliance");
    expect(router.currentRoute.value.path).toBe("/compliance");
  });

  it("reads and writes the Pinia stores through the state facade", () => {
    const sync = useSyncStore();
    state.isSyncDrawerOpen = false;
    sync.toggleSyncDrawer();
    expect(state.isSyncDrawerOpen).toBe(true);

    state.syncStatusText = "Sync failed. 1 submission(s) still queued.";
    expect(sync.isSyncFailed).toBe(true);

    state.currentView = "view-consent";
    expect(useUiStore().currentView).toBe("view-consent");

    state.someUndeclaredKey = 42;
    expect(state.someUndeclaredKey).toBe(42);
  });

  it("sorts glob-discovered portalRouteDescriptors deterministically and derives availability sets", () => {
    expect(portalRouteDescriptors.length).toBe(7);
    for (let i = 1; i < portalRouteDescriptors.length; i++) {
      const prev = portalRouteDescriptors[i - 1];
      const curr = portalRouteDescriptors[i];
      const cmp =
        (prev.order ?? 0) !== (curr.order ?? 0)
          ? (prev.order ?? 0) - (curr.order ?? 0)
          : String(prev.route.name).localeCompare(String(curr.route.name));
      expect(cmp).toBeLessThanOrEqual(0);
    }

    expect(activeTabForView("view-questionnaire")?.id).toBe("tasks");
    expect(activeTabForView("view-wallet")?.id).toBe("wallet");
    expect([...AUTHENTICATED_SCREENS]).toEqual(["wallet"]);
    expect([...AVAILABLE_SCREENS]).toEqual([]);
    expect([...DEMO_AVAILABLE_SCREENS]).toEqual(
      expect.arrayContaining([
        "tasks",
        "questionnaire",
        "compliance",
        "inbox",
        "consent",
        "lay-summaries",
      ])
    );
    expect(screenTitle("wallet")).toBe("Participant Financial Wallet");
    expect(screenTitle("unknown-route")).toBe("This workflow");
  });
});
