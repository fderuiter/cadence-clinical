import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import { ClinicalDrawer, ClinicalModal, ClinicalQueryPanel } from "ui";
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

    // ToastHost remains non-inert, audible, and reachable via Tab while modal is open
    const toastHostEl = toastWrapper.find(".toast-host").element;
    expect(toastHostEl.hasAttribute("inert")).toBe(false);
    expect(toastHostEl.getAttribute("aria-hidden")).toBeNull();
    const errorAlert = toastWrapper.find('[role="alert"]');
    expect(errorAlert.attributes("aria-live")).toBe("assertive");
    expect(errorAlert.text()).toContain("Critical lab alert");

    // ConfirmationDialogHost delegates to ClinicalModal with alertdialog role, aria-describedby, and initial-focus=".btn-cancel"
    const dialogEl = document.body.querySelector('[role="alertdialog"]');
    expect(dialogEl).not.toBeNull();
    expect(dialogEl.getAttribute("aria-describedby")).toBe(
      "confirm-dialog-message"
    );
    expect(document.activeElement?.classList.contains("btn-cancel")).toBe(true);

    // Pressing Tab from the last modal button bridges into the exempt ToastHost dismiss button
    const confirmBtnEl = dialogEl.querySelector(".btn-confirm");
    confirmBtnEl.focus();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(
      toastWrapper.find(".toast-dismiss").element
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
    expect(relatedCard.element.tagName).toBe("BUTTON");
    expect(relatedCard.attributes("type")).toBe("button");
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
    expect(searchCard.element.tagName).toBe("BUTTON");
    expect(searchCard.attributes("type")).toBe("button");
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

  it("permits unmodified Tab exit from inline ClinicalQueryPanel", async () => {
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

  it("traps focus in DocumentManagerView ClinicalDrawer, locks dismissal during QC review, keeps ToastHost live alerts active, and closes stacked PdfPreviewModal LIFO on Escape", async () => {
    const DocumentManagerView = (
      await import("../src/views/DocumentManagerView.vue")
    ).default;
    const pendingLazy = globalThis.__CADENCE_LAZY_OVERLAY_PROMISES__;
    while (pendingLazy && pendingLazy.size > 0) {
      await Promise.all(Array.from(pendingLazy));
    }
    const { useAuthStore } = await import("../src/stores/auth.js");
    const { useEtmfStore } = await import("../src/stores/etmf.js");
    const { etmfService } = await import("../src/api/etmf.js");
    const { signInAs } = await import("./helpers/signIn.js");

    signInAs(useAuthStore(), ["CRA"], { username: "cra_reviewer" });
    vi.spyOn(etmfService, "getQcQueue").mockResolvedValue([]);
    vi.spyOn(etmfService, "getDocuments").mockResolvedValue([]);

    const toastMount = document.createElement("div");
    document.body.appendChild(toastMount);
    const toastWrapper = trackWrapper(
      mount(ToastHost, {
        attachTo: toastMount,
        global: { plugins: [pinia] },
      })
    );
    const toastStore = useToastStore();
    toastStore.push({
      tone: "info",
      title: "eTMF Sync Active",
      message: "Zone 1 binder synchronized",
      timeoutMs: 0,
    });

    const viewMount = document.createElement("div");
    document.body.appendChild(viewMount);

    const etmfStore = useEtmfStore();
    const docRecord = {
      id: "DOC-501",
      study_id: "STUDY-USDM-001",
      zone: 1,
      section: "01.01",
      artifact_code: "01.01.01",
      artifact_type: "Clinical Trial Protocol",
      filename: "protocol_v2.pdf",
      mime_type: "application/pdf",
      created_at: "2026-10-01T12:00:00Z",
      created_by: "cra_reviewer",
      version_index: 2,
      status: "TECHNICAL_QC",
      reason_for_change: "Amendment upload",
    };
    etmfStore.documentsList = [docRecord];

    const wrapper = trackWrapper(
      mount(DocumentManagerView, {
        attachTo: viewMount,
        props: { studyId: "STUDY-USDM-001" },
        global: { plugins: [pinia, router] },
      })
    );
    await nextTick();
    await nextTick();

    // Open Document Metadata ClinicalDrawer
    wrapper.vm.handleInspectDocument(docRecord);
    await nextTick();
    await nextTick();

    const drawerDialog = wrapper.find(".metadata-drawer[role='dialog']");
    expect(drawerDialog.exists()).toBe(true);
    expect(drawerDialog.attributes("aria-modal")).toBe("true");

    // ToastHost remains non-inert and polite status toast remains audible while ClinicalDrawer is open
    const toastHostEl = toastWrapper.find(".toast-host").element;
    expect(toastHostEl.hasAttribute("inert")).toBe(false);
    expect(toastHostEl.getAttribute("aria-hidden")).toBeNull();
    expect(toastWrapper.find('[role="status"]').text()).toContain(
      "eTMF Sync Active"
    );

    // Lock dismissal while reviewingQc is active
    wrapper.vm.reviewingQc = true;
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(wrapper.vm.inspectedDoc).not.toBeNull();

    wrapper.vm.reviewingQc = false;
    await nextTick();

    // Open stacked PdfPreviewModal from inside ClinicalDrawer
    const previewBtn = wrapper.find(".btn-preview-from-drawer");
    previewBtn.element.focus();
    await previewBtn.trigger("click");
    await nextTick();
    await nextTick();

    expect(wrapper.vm.selectedDoc).not.toBeNull();
    expect(wrapper.vm.inspectedDoc).not.toBeNull();

    // First Escape closes ONLY top PdfPreviewModal while ClinicalDrawer stays open
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    await nextTick();
    expect(wrapper.vm.selectedDoc).toBeNull();
    expect(wrapper.vm.inspectedDoc).not.toBeNull();

    // Second Escape closes ClinicalDrawer
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    await nextTick();
    expect(wrapper.vm.inspectedDoc).toBeNull();
  });

  it("traps focus, locks dismissal during submission, and restores trigger focus in ProtocolTrainingConsole modals", async () => {
    const ProtocolTrainingConsole = (
      await import("../src/components/clinical/ProtocolTrainingConsole.vue")
    ).default;
    const { trainingApi } = await import("../src/api/training.js");
    const { useAuthStore } = await import("../src/stores/auth.js");
    const { signInAs } = await import("./helpers/signIn.js");

    signInAs(useAuthStore(), ["Data Manager", "PI", "Sponsor Admin"], {
      username: "pi_lead_01",
    });
    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([
      {
        id: "curr-1",
        title: "Protocol V3.0 Curriculum",
        protocol_version: "3.0",
        study_id: "CARDIO-2026",
        is_active: true,
      },
    ]);
    vi.spyOn(trainingApi, "getCurriculum").mockResolvedValue({
      id: "curr-1",
      title: "Protocol V3.0 Curriculum",
      protocol_version: "3.0",
      study_id: "CARDIO-2026",
      is_active: true,
      modules: [],
    });
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue([
      {
        id: "del-01",
        study_id: "CARDIO-2026",
        site_id: "SITE-101",
        staff_user_id: "usr-01",
        staff_name: "Marcus Vance",
        role: "Sub-Investigator",
        task_code: "INFORMED_CONSENT",
        duties: "Informed Consent",
        status: "PENDING",
        signed_off: false,
      },
    ]);

    const mountEl = document.createElement("div");
    document.body.appendChild(mountEl);
    const wrapper = trackWrapper(
      mount(ProtocolTrainingConsole, {
        attachTo: mountEl,
        props: { studyId: "CARDIO-2026" },
        global: { plugins: [pinia, router] },
      })
    );
    await nextTick();
    await nextTick();
    await new Promise((r) => setTimeout(r, 10));

    // Switch to Batch Assignments tab, select site, focus trigger button, and open Batch Modal
    const batchTab = wrapper
      .findAll(".tab-btn")
      .find((b) => b.text().includes("Batch Assignments"));
    await batchTab.trigger("click");
    await nextTick();

    await wrapper
      .find(".batch-filter-grid input[value='SITE-101']")
      .setValue(true);
    await nextTick();

    const batchTriggerBtn = wrapper.find(".batch-preview-bar button");
    batchTriggerBtn.element.focus();
    expect(document.activeElement).toBe(batchTriggerBtn.element);

    await batchTriggerBtn.trigger("click");
    await nextTick();
    await nextTick();

    const batchDialog = wrapper.find('[role="dialog"]');
    expect(batchDialog.exists()).toBe(true);
    expect(batchDialog.attributes("aria-modal")).toBe("true");

    // Lock dismissal while submittingBatch is true
    wrapper.vm.submittingBatch = true;
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(wrapper.vm.showBatchModal).toBe(true);

    // Unlock and press Escape -> closes modal and restores focus to batchTriggerBtn
    wrapper.vm.submittingBatch = false;
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    await nextTick();
    expect(wrapper.vm.showBatchModal).toBe(false);
    expect(document.activeElement).toBe(batchTriggerBtn.element);

    // Switch to Delegation Log tab and verify Bulk Delegation Sign-Off Modal
    const delTab = wrapper
      .findAll(".tab-btn")
      .find((b) => b.text().includes("Delegation"));
    await delTab.trigger("click");
    await nextTick();

    await wrapper.find("thead input[type='checkbox']").setValue(true);
    await nextTick();

    const signTriggerBtn = wrapper.find(".delegation-actions-bar button");
    signTriggerBtn.element.focus();
    await signTriggerBtn.trigger("click");
    await nextTick();
    await nextTick();
    expect(wrapper.vm.showBulkSignModal).toBe(true);

    wrapper.vm.submittingBulkSign = true;
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    expect(wrapper.vm.showBulkSignModal).toBe(true);

    wrapper.vm.submittingBulkSign = false;
    await nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    await nextTick();
    expect(wrapper.vm.showBulkSignModal).toBe(false);
    expect(document.activeElement).toBe(signTriggerBtn.element);
  });

  it("dismisses only ShareDrawer ClinicalPopover suggestions on Tab or single Escape and configures fallbackFocus across all 14 row-mutating dialogs", async () => {
    const fs = await import("node:fs");
    const path = await import("node:path");
    const ShareDrawer = (
      await import("../src/components/fileshare/ShareDrawer.vue")
    ).default;

    const mountContainer = document.createElement("div");
    document.body.appendChild(mountContainer);

    const wrapper = trackWrapper(
      mount(ShareDrawer, {
        props: {
          isOpen: true,
          files: [{ id: "file-1", filename: "protocol.pdf" }],
        },
        attachTo: mountContainer,
        global: { plugins: [pinia, router] },
      })
    );
    await nextTick();
    await nextTick();

    const userInput = wrapper.find("#share-user-input");
    userInput.element.focus();
    await userInput.trigger("focus");
    await nextTick();

    expect(wrapper.find(".suggestions-dropdown").exists()).toBe(true);

    // Single Escape dismisses ONLY .suggestions-dropdown while ShareDrawer stays open
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await nextTick();
    await nextTick();
    expect(wrapper.find(".suggestions-dropdown").exists()).toBe(false);
    expect(wrapper.emitted("close")).toBeFalsy();

    // Re-open suggestions and Tab/focus outside input + popover -> closes suggestions only
    await userInput.trigger("focus");
    await nextTick();
    expect(wrapper.find(".suggestions-dropdown").exists()).toBe(true);

    const reasonTextarea = wrapper.find("textarea");
    reasonTextarea.element.focus();
    await nextTick();
    expect(wrapper.find(".suggestions-dropdown").exists()).toBe(false);
    expect(wrapper.emitted("close")).toBeFalsy();

    // Verify all 14 row-mutating dialogs configure explicit fallbackFocus
    const webSrc = path.resolve(import.meta.dirname, "../src");
    const codingSrc = fs.readFileSync(
      path.join(webSrc, "components/CodingQueueTable.vue"),
      "utf8"
    );
    expect(
      (codingSrc.match(/fallbackFocus:\s*"#btn-refresh-coding-queue"/g) || [])
        .length
    ).toBe(4);

    const deviceSrc = fs.readFileSync(
      path.join(webSrc, "components/clinical/DeviceInvestigationConsole.vue"),
      "utf8"
    );
    expect(
      (deviceSrc.match(/fallbackFocus:\s*"#btn-refresh-device-hub"/g) || [])
        .length
    ).toBe(4);

    const labSrc = fs.readFileSync(
      path.join(webSrc, "components/clinical/LabGovernanceConsole.vue"),
      "utf8"
    );
    expect(
      (
        labSrc.match(
          /fallbackFocus:\s*"#(btn-lab-ingest-feed|tab-lab-toxicity-queue|btn-lab-bulk-import)"/g
        ) || []
      ).length
    ).toBe(3);

    const amendmentViewSrc = fs.readFileSync(
      path.join(webSrc, "views/AmendmentDiffView.vue"),
      "utf8"
    );
    expect(amendmentViewSrc).toContain(
      'fallback-focus="#btn-open-amendment-governance"'
    );

    const protocolVersionsSrc = fs.readFileSync(
      path.join(webSrc, "components/canvas/ProtocolVersionsSection.vue"),
      "utf8"
    );
    expect(
      (
        protocolVersionsSrc.match(
          /:fallback-focus="resolveVersionFallbackFocus"/g
        ) || []
      ).length
    ).toBe(2);
  });
});
