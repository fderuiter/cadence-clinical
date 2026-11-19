import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import {
  ClinicalDrawer,
  ClinicalModal,
  ClinicalQueryPanel,
  FileUploadModal,
} from "ui";
import ContextualHelpDrawer from "../src/components/ContextualHelpDrawer.vue";
import OnboardingTour from "../src/components/OnboardingTour.vue";
import DictionaryBrowserModal from "../src/components/DictionaryBrowserModal.vue";
import ConfirmationDialogHost from "../src/components/ConfirmationDialogHost.vue";
import ToastHost from "../src/components/ToastHost.vue";
import { useKnowledgeStore } from "../src/stores/knowledge.js";
import { useOnboardingStore } from "../src/stores/onboarding.js";
import { useCodingStore } from "../src/stores/coding.js";
import { useConfirmStore } from "../src/stores/confirm.js";
import { useToastStore } from "../src/stores/toasts.js";
import { knowledgeService } from "../src/api/knowledge.js";

describe("Centralized Focus Containment & Reactive Inert Primitives", () => {
  let pinia;
  let router;
  let mountedWrappers = [];

  const trackWrapper = (wrapper) => {
    mountedWrappers.push(wrapper);
    return wrapper;
  };

  beforeEach(async () => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
    router = createRouter({
      history: createWebHistory(),
      routes: [
        { path: "/", component: { template: "<div>Home</div>" } },
        { path: "/ecrf", component: { template: "<div>eCRF</div>" } },
      ],
    });
    await router.push("/ecrf");
    await router.isReady();
  });

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      const w = mountedWrappers.pop();
      try {
        w.unmount();
      } catch {
        // ignore already unmounted wrapper
      }
    }
    document.body.innerHTML = "";
  });

  it("marks background siblings inert & aria-hidden while keeping ToastHost live alerts active", async () => {
    const appShell = document.createElement("div");
    appShell.id = "background-shell";
    const triggerBtn = document.createElement("button");
    triggerBtn.id = "open-confirm-btn";
    triggerBtn.textContent = "Delete Record";
    appShell.appendChild(triggerBtn);
    document.body.appendChild(appShell);

    const toastHostMount = document.createElement("div");
    document.body.appendChild(toastHostMount);
    const toastWrapper = trackWrapper(
      mount(ToastHost, {
        attachTo: toastHostMount,
        global: { plugins: [pinia] },
      })
    );

    const confirmHostMount = document.createElement("div");
    document.body.appendChild(confirmHostMount);
    trackWrapper(
      mount(ConfirmationDialogHost, {
        attachTo: confirmHostMount,
        global: { plugins: [pinia] },
      })
    );

    const toastStore = useToastStore();
    toastStore.push({
      tone: "error",
      title: "Critical lab alert",
      message: "Potassium out of range",
      timeoutMs: 0,
    });
    await nextTick();

    triggerBtn.focus();
    expect(document.activeElement).toBe(triggerBtn);

    const confirmStore = useConfirmStore();
    const confirmPromise = confirmStore.confirm({
      title: "Confirm Deletion",
      message: "Remove draft entry?",
      variant: "danger",
      confirmLabel: "Delete",
      cancelLabel: "Keep",
    });
    await nextTick();
    await nextTick();

    // Background page sibling is marked inert + aria-hidden
    expect(appShell.hasAttribute("inert")).toBe(true);
    expect(appShell.getAttribute("aria-hidden")).toBe("true");

    // ToastHost remains non-inert and audible to assistive technologies
    const toastHostEl = toastWrapper.find(".toast-host").element;
    expect(toastHostEl.hasAttribute("inert")).toBe(false);
    expect(toastHostEl.getAttribute("aria-hidden")).toBeNull();
    expect(toastHostEl.getAttribute("aria-live")).toBe("assertive");
    expect(toastWrapper.find('[role="alert"]').text()).toContain(
      "Critical lab alert"
    );

    // ConfirmationDialogHost delegates to ClinicalModal with alertdialog role and initial-focus=".btn-confirm"
    const dialogEl = document.body.querySelector('[role="alertdialog"]');
    expect(dialogEl).not.toBeNull();
    expect(document.activeElement?.classList.contains("btn-confirm")).toBe(
      true
    );

    // Escape cancels the dialog, clears background inert, and restores focus to triggerBtn
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    const confirmed = await confirmPromise;
    await nextTick();

    expect(confirmed).toBe(false);
    expect(appShell.hasAttribute("inert")).toBe(false);
    expect(appShell.getAttribute("aria-hidden")).toBeNull();
    expect(document.activeElement).toBe(triggerBtn);
  });

  it("supports stacked ContextualHelpDrawer + TicketCreateModal LIFO Escape and Enter/Space search card activation", async () => {
    vi.spyOn(knowledgeService, "resolveContextualHelp").mockResolvedValue({
      primary_article: {
        id: "sop-primary",
        title: "SOP-101: Data Entry",
        category: "EDC",
        version: "1.0",
        body_markdown: "Primary guidance",
      },
      related_articles: [
        {
          id: "sop-rel-1",
          title: "SOP-102: Query Resolution",
          category: "EDC",
          body_markdown: "Related guidance",
        },
      ],
    });
    vi.spyOn(knowledgeService, "searchArticles").mockResolvedValue([
      {
        id: "sop-search-1",
        title: "SOP-201: SAE Reporting",
        category: "Safety",
        summary: "Report within 24 hours",
        body_markdown: "SAE guidance",
      },
    ]);

    const helpTrigger = document.createElement("button");
    helpTrigger.id = "help-trigger-btn";
    document.body.appendChild(helpTrigger);
    helpTrigger.focus();

    const mountContainer = document.createElement("div");
    document.body.appendChild(mountContainer);

    const knowledgeStore = useKnowledgeStore();
    await knowledgeStore.fetchContextualHelp("/ecrf", "site_crc");

    const wrapper = trackWrapper(
      mount(ContextualHelpDrawer, {
        attachTo: mountContainer,
        global: { plugins: [pinia, router] },
      })
    );

    knowledgeStore.openDrawer();
    await nextTick();
    await nextTick();

    const drawerDialog = wrapper.find('[role="dialog"]');
    expect(drawerDialog.exists()).toBe(true);
    expect(drawerDialog.attributes("aria-modal")).toBe("true");

    // Keyboard activation (Enter) on related card
    const relatedCard = wrapper.find(".related-card");
    expect(relatedCard.attributes("role")).toBe("button");
    expect(relatedCard.attributes("tabindex")).toBe("0");
    await relatedCard.trigger("keydown.enter");
    expect(knowledgeStore.displayedArticle.id).toBe("sop-rel-1");

    // Search and keyboard activation (Space) on search-result-card
    knowledgeStore.searchResults = [
      {
        id: "sop-search-1",
        title: "SOP-201: SAE Reporting",
        category: "Safety",
        summary: "Report within 24 hours",
      },
    ];
    await wrapper.find(".btn-icon-toggle").trigger("click");
    const searchInput = wrapper.find("#help-search-input");
    await searchInput.setValue("SAE");
    await nextTick();

    const searchCard = wrapper.find(".search-result-card");
    expect(searchCard.exists()).toBe(true);
    expect(searchCard.attributes("role")).toBe("button");
    await searchCard.trigger("keydown.space");
    expect(knowledgeStore.displayedArticle.id).toBe("sop-search-1");

    // Open stacked TicketCreateModal from inside ContextualHelpDrawer
    await wrapper.find(".btn-escalate").trigger("click");
    await nextTick();
    await nextTick();
    expect(knowledgeStore.isEscalating).toBe(true);

    // First Escape closes ONLY TicketCreateModal while ContextualHelpDrawer stays open
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(knowledgeStore.isEscalating).toBe(false);
    expect(knowledgeStore.isOpen).toBe(true);

    // Second Escape closes ContextualHelpDrawer and restores focus to helpTrigger
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(knowledgeStore.isOpen).toBe(false);
    expect(document.activeElement).toBe(helpTrigger);
  });

  it("exposes dialog role/label on OnboardingTour, trapping modal steps while keeping anchored steps non-modal", async () => {
    const pageField = document.createElement("a");
    pageField.id = "nav-studies";
    pageField.href = "#studies";
    pageField.textContent = "Studies";
    pageField.getBoundingClientRect = () => ({
      top: 80,
      bottom: 112,
      left: 120,
      right: 220,
      width: 100,
      height: 32,
    });
    document.body.appendChild(pageField);

    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);

    const onboardingStore = useOnboardingStore();
    onboardingStore.startTour(); // Step 1 has no targetSelector -> modal step

    const wrapper = trackWrapper(
      mount(OnboardingTour, {
        attachTo: mountNode,
        global: { plugins: [pinia, router] },
      })
    );
    await nextTick();
    await nextTick();

    const popover = wrapper.find(".onboarding-popover-card");
    expect(popover.attributes("role")).toBe("dialog");
    expect(popover.attributes("aria-labelledby")).toBe("onboarding-tour-title");
    expect(popover.attributes("aria-modal")).toBe("true");
    expect(pageField.hasAttribute("inert")).toBe(true);

    // Advance to anchored step 2 (#nav-studies)
    onboardingStore.currentStep = 2;
    await nextTick();
    await nextTick();

    expect(popover.attributes("role")).toBe("dialog");
    expect(popover.attributes("aria-modal")).toBeUndefined();
    expect(pageField.hasAttribute("inert")).toBe(false);

    // Escape dismisses anchored tour step via useEscapeClose
    document.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    await nextTick();
    expect(onboardingStore.isActive).toBe(false);
  });

  it("exposes accessible close button and Enter/Space keyboard card selection on DictionaryBrowserModal", async () => {
    const codingStore = useCodingStore();
    codingStore.browserModal.isOpen = true;
    codingStore.browserModal.dictionaryType = "MEDDRA";
    codingStore.dictionarySearchResults = [
      {
        term: "Headache",
        code: "10019211",
        score: 0.98,
        soc_name: "Nervous system disorders",
        soc_code: "10029205",
      },
      {
        term: "Migraine",
        code: "10027599",
        score: 0.91,
        soc_name: "Nervous system disorders",
        soc_code: "10029205",
      },
    ];

    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);
    const wrapper = trackWrapper(
      mount(DictionaryBrowserModal, {
        attachTo: mountNode,
        global: { plugins: [pinia] },
      })
    );
    await nextTick();

    const dialog = wrapper.find('[role="dialog"]');
    expect(dialog.attributes("aria-modal")).toBe("true");
    expect(dialog.attributes("aria-labelledby")).toBe(
      "dictionary-browser-modal-title"
    );

    const closeBtn = wrapper.find(".btn-close");
    expect(closeBtn.attributes("aria-label")).toBe("Close dictionary browser");

    const cards = wrapper.findAll(".result-item-card");
    expect(cards.length).toBe(2);
    expect(cards[0].attributes("role")).toBe("button");
    expect(cards[0].attributes("tabindex")).toBe("0");

    await cards[0].trigger("keydown.enter");
    expect(cards[0].attributes("aria-pressed")).toBe("true");
    expect(wrapper.find(".concept-summary-banner").text()).toContain(
      "10019211"
    );

    await cards[1].trigger("keydown.space");
    expect(cards[1].attributes("aria-pressed")).toBe("true");
    expect(wrapper.find(".concept-summary-banner").text()).toContain(
      "10027599"
    );
  });

  it("locks Escape dismissal on FileUploadModal while uploading and permits unmodified Tab exit from ClinicalQueryPanel", async () => {
    const mountNode = document.createElement("div");
    document.body.appendChild(mountNode);

    const onCloseSpy = vi.fn();
    const uploadWrapper = trackWrapper(
      mount(FileUploadModal, {
        attachTo: mountNode,
        props: {
          modelValue: true,
          studyId: "study-001",
          onClose: onCloseSpy,
        },
      })
    );
    await nextTick();
    await nextTick();

    // Enter active upload state to lock dismissal (21 CFR Part 11 safeguard)
    uploadWrapper.vm.status = "uploading";
    await nextTick();

    // Escape is blocked while uploading
    const escWhileUploading = new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    });
    document.dispatchEvent(escWhileUploading);
    expect(escWhileUploading.defaultPrevented).toBe(true);
    expect(onCloseSpy).not.toHaveBeenCalled();

    // Returning to idle unlocks Escape dismissal
    uploadWrapper.vm.status = "idle";
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(onCloseSpy).toHaveBeenCalledTimes(1);
    uploadWrapper.unmount();

    // Inline ClinicalQueryPanel allows unmodified Tab navigation (no focus trap) while supporting Escape close
    const panelCloseSpy = vi.fn();
    const panelMount = document.createElement("div");
    document.body.appendChild(panelMount);
    const panelWrapper = trackWrapper(
      mount(ClinicalQueryPanel, {
        attachTo: panelMount,
        props: {
          id: "query-panel-vssysbp",
          fieldId: "VS.SYSBP",
          fieldLabel: "Systolic BP",
          queries: [
            {
              id: "q-1",
              status: "OPEN",
              message: "Verify reading",
              author: "CRA",
              createdAt: "2026-09-01T10:00:00Z",
            },
          ],
        },
        attrs: {
          onClose: panelCloseSpy,
        },
      })
    );
    await nextTick();

    const tabEvt = new KeyboardEvent("keydown", {
      key: "Tab",
      bubbles: true,
      cancelable: true,
    });
    panelWrapper.element.dispatchEvent(tabEvt);
    expect(tabEvt.defaultPrevented).toBe(false);
  });

  it("renders ClinicalDrawer and ClinicalModal without nested <main> landmarks", async () => {
    const drawerNode = document.createElement("div");
    const modalNode = document.createElement("div");
    document.body.appendChild(drawerNode);
    document.body.appendChild(modalNode);

    trackWrapper(
      mount(ClinicalDrawer, {
        attachTo: drawerNode,
        props: {
          open: true,
          title: "Inspection Drawer",
          teleport: false,
        },
        slots: {
          default: "<p>Drawer content</p>",
        },
      })
    );
    trackWrapper(
      mount(ClinicalModal, {
        attachTo: modalNode,
        props: {
          modelValue: true,
          title: "Inspection Modal",
          teleport: false,
        },
        slots: {
          default: "<p>Modal content</p>",
        },
      })
    );
    await nextTick();

    expect(document.body.querySelectorAll("main").length).toBe(0);
    expect(document.body.querySelectorAll('[role="dialog"]').length).toBe(2);
  });
});
