import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { getFocusableElements } from "ui";
import {
  initializeApp,
  unmountApp,
  state,
  showView,
  openSignatureModal,
  openReconsentModal,
} from "../src/index.js";
import { tabs } from "../src/router/routes.js";
import { useSessionStore } from "../src/stores/session.js";

const settle = async () => {
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 25));
  await nextTick();
};

describe("Subject Portal Focus Traps, Closed Drawer Inertness, and 6-Tab Navigation", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });
    document.body.innerHTML = `<div id="app"></div>`;
    state.currentView = "view-tasks";
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    await initializeApp();
    await settle();
  });

  afterEach(() => {
    const session = useSessionStore();
    session.pinSetup.isOpen = false;
    session.pinUnlock.isOpen = false;
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    unmountApp();
  });

  it("hides closed SyncDrawer and scrim from keyboard tabbing and assistive technology, and traps/restores focus when opened", async () => {
    const trigger = document.getElementById("btn-global-sync-drawer");
    const drawer = document.querySelector(".global-drawer");
    const overlay = document.querySelector(".global-drawer-overlay");

    expect(drawer.hasAttribute("inert")).toBe(true);
    expect(drawer.getAttribute("aria-hidden")).toBe("true");
    expect(drawer.getAttribute("aria-modal")).toBeNull();
    expect(overlay.hasAttribute("inert")).toBe(true);
    expect(overlay.getAttribute("aria-hidden")).toBe("true");
    expect(getFocusableElements(drawer)).toEqual([]);

    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    await settle();

    expect(state.isSyncDrawerOpen).toBe(true);
    expect(drawer.hasAttribute("inert")).toBe(false);
    expect(drawer.getAttribute("aria-hidden")).toBeNull();
    expect(drawer.getAttribute("aria-modal")).toBe("true");
    expect(drawer.contains(document.activeElement)).toBe(true);

    const focusables = getFocusableElements(drawer);
    expect(focusables.length).toBeGreaterThanOrEqual(2);
    const firstEl = focusables[0];
    const lastEl = focusables[focusables.length - 1];

    lastEl.focus();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(firstEl);

    firstEl.focus();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(lastEl);

    // Escape closes the drawer and restores focus to the trigger button
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.isSyncDrawerOpen).toBe(false);
    expect(drawer.hasAttribute("inert")).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it("traps focus inside mandatory PinSetupModal and PinUnlockModal while blocking Escape dismissal", async () => {
    const session = useSessionStore();

    session.pinSetup.isOpen = true;
    await settle();

    const setupModal = document.getElementById("portal-pin-setup-modal");
    expect(setupModal).not.toBeNull();
    expect(setupModal.contains(document.activeElement)).toBe(true);

    const setupEsc = new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    });
    document.dispatchEvent(setupEsc);
    await settle();
    expect(setupEsc.defaultPrevented).toBe(true);
    expect(session.pinSetup.isOpen).toBe(true);

    session.pinSetup.isOpen = false;
    await settle();

    session.pinUnlock.isOpen = true;
    await settle();

    const unlockModal = document.getElementById("portal-pin-unlock-modal");
    expect(unlockModal).not.toBeNull();
    expect(unlockModal.contains(document.activeElement)).toBe(true);

    const unlockEsc = new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    });
    document.dispatchEvent(unlockEsc);
    await settle();
    expect(unlockEsc.defaultPrevented).toBe(true);
    expect(session.pinUnlock.isOpen).toBe(true);
  });

  it("clears typed 21 CFR Part 11 credentials immediately when SignatureModal or ReconsentModal is dismissed with Escape", async () => {
    const trigger = document.getElementById("btn-global-sync-drawer");
    trigger.focus();

    openSignatureModal({ vssbp: "120" }, false);
    await settle();

    const signModal = document.getElementById("portal-sign-modal");
    const signPw = document.getElementById("sign-password");
    expect(state.signatureModalOpen).toBe(true);
    expect(signModal.contains(document.activeElement)).toBe(true);

    signPw.value = "SuperSecretPin123!";
    signPw.dispatchEvent(new Event("input", { bubbles: true }));

    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.signatureModalOpen).toBe(false);
    expect(signPw.value).toBe("");
    expect(document.activeElement).toBe(trigger);

    // Verify ReconsentModal Escape credential wiping as well
    trigger.focus();
    openReconsentModal();
    await settle();

    const reconsentPw = document.getElementById("reconsent-sign-password");
    expect(state.reconsentModalOpen).toBe(true);
    state.reconsentForm.password = "AmendmentSecret456!";
    await nextTick();
    expect(reconsentPw.value).toBe("AmendmentSecret456!");

    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.reconsentModalOpen).toBe(false);
    expect(state.reconsentForm.password).toBe("");
    expect(document.activeElement).toBe(trigger);

    // Also verify standalone DOM #portal-sign-modal traps Tab and Escape via trapTabFocus when Vue shell is unmounted
    unmountApp();
    document.body.innerHTML = `
      <button id="standalone-trigger" type="button">Sign</button>
      <div id="portal-sign-modal" style="display: none;">
        <select id="sign-reason"></select>
        <textarea id="sign-reason-custom"></textarea>
        <input id="sign-username" type="text" />
        <input id="sign-password" type="password" />
        <button id="btn-modal-cancel" type="button">Cancel</button>
        <button id="btn-modal-sign" type="submit">Confirm</button>
      </div>
    `;
    const standaloneTrigger = document.getElementById("standalone-trigger");
    standaloneTrigger.focus();
    openSignatureModal("epro");

    const standaloneModal = document.getElementById("portal-sign-modal");
    const standaloneFocusables = getFocusableElements(standaloneModal);
    expect(document.activeElement).toBe(standaloneFocusables[0]);

    const lastStandalone =
      standaloneFocusables[standaloneFocusables.length - 1];
    lastStandalone.focus();
    window.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(standaloneFocusables[0]);

    window.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(state.signatureModalOpen).toBe(false);
    expect(document.activeElement).toBe(standaloneTrigger);
  });

  it("traps focus inside QuestionnaireView cancel confirmation dialog and restores focus on Escape", async () => {
    state.assignments = [
      {
        id: "assign_cancel_test",
        subject_id: "subj_01",
        instrument_id: "inst_cancel_test",
        instrument_name: "Daily Symptom Diary",
        status: "PENDING",
      },
    ];
    state.instruments = {
      inst_cancel_test: {
        id: "inst_cancel_test",
        name: "Daily Symptom Diary",
        description: "Record symptoms",
        items: {
          symptom_score: {
            label: "Symptom Score",
            type: "numeric",
            required: true,
            min: 0,
            max: 10,
          },
        },
      },
    };
    state.activeQuestionnaire = {
      assignment: state.assignments[0],
      instrument: state.instruments.inst_cancel_test,
      answers: { symptom_score: 4 },
    };
    showView("view-questionnaire");
    await settle();

    const cancelBtn = document.getElementById("btn-cancel-questionnaire");
    expect(cancelBtn).not.toBeNull();
    cancelBtn.focus();
    cancelBtn.click();
    await settle();

    const dialog = document.getElementById("questionnaire-cancel-confirm");
    const keepBtn = document.getElementById("btn-cancel-keep-editing");
    const discardBtn = document.getElementById("btn-cancel-confirm-discard");
    expect(state.questionnaireCancelConfirmOpen).toBe(true);
    expect(dialog).not.toBeNull();
    expect(document.activeElement).toBe(keepBtn);

    // Shift+Tab wraps from Keep Editing to Discard Draft & Leave
    dialog.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(discardBtn);

    // Forward Tab wraps back to Keep Editing
    dialog.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(keepBtn);

    // Escape dismisses confirmation dialog without discarding active questionnaire and restores focus
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.questionnaireCancelConfirmOpen).toBe(false);
    expect(state.activeQuestionnaire).not.toBeNull();
    expect(document.activeElement).toBe(cancelBtn);
    state.activeQuestionnaire = null;
  });

  it("drives participant portal tabs from a single state source in both 4-tab production mode and 6-tab demo mode", async () => {
    expect(tabs).toHaveLength(6);

    // 1. Default non-demo mode renders the 4 backed tabs and wraps keyboard navigation cleanly
    state.session.isDemoMode = false;
    await settle();
    expect(document.querySelectorAll('.nav-tabs [role="tab"]')).toHaveLength(4);

    const tasksTab = document.getElementById("btn-tab-btn-tasks");
    const consentTab = document.getElementById("btn-tab-btn-consent");
    tasksTab.focus();
    tasksTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "End",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-consent");
    expect(document.activeElement).toBe(consentTab);

    // 2. Demo mode renders all 6 tabs with bidirectional ARIA links and roving tabindex navigation
    state.session.isDemoMode = true;
    await settle();

    const tabButtons = Array.from(
      document.querySelectorAll('.nav-tabs [role="tab"]')
    );
    expect(tabButtons).toHaveLength(6);

    // Check every tab <-> tabpanel bidirectional relationship and single-source selection
    for (const tab of tabs) {
      showView(tab.viewId);
      await settle();

      for (const candidate of tabs) {
        const btn = document.getElementById(candidate.buttonId);
        const panel = document.getElementById(candidate.viewId);
        expect(btn).not.toBeNull();
        expect(panel).not.toBeNull();
        expect(btn.getAttribute("aria-controls")).toBe(candidate.viewId);
        expect(panel.getAttribute("aria-labelledby")).toBe(candidate.buttonId);
        expect(panel.getAttribute("role")).toBe("tabpanel");

        const isSelected = candidate.id === tab.id;
        expect(btn.getAttribute("aria-selected")).toBe(String(isSelected));
        expect(btn.getAttribute("tabindex")).toBe(isSelected ? "0" : "-1");
      }
    }

    // Verify ArrowLeft, ArrowRight, End, Home navigation across all 6 tabs
    showView("view-tasks");
    await settle();

    const firstTab = document.getElementById("btn-tab-btn-tasks");
    const lastTab = document.getElementById("btn-tab-btn-lay-summaries");
    firstTab.focus();

    // ArrowLeft from first wraps to 6th tab (Trial Results / Lay Summaries)
    firstTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowLeft",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-lay-summaries");
    expect(document.activeElement).toBe(lastTab);
    expect(lastTab.getAttribute("aria-selected")).toBe("true");

    // ArrowRight from 6th tab wraps back to 1st tab (Tasks)
    lastTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowRight",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-tasks");
    expect(document.activeElement).toBe(firstTab);

    // End jumps to 6th tab; Home jumps back to 1st tab
    firstTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "End",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-lay-summaries");
    expect(document.activeElement).toBe(lastTab);

    lastTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Home",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-tasks");
    expect(document.activeElement).toBe(firstTab);

    state.session.isDemoMode = false;
  });
});
