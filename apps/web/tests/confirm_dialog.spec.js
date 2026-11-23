import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { mount, flushPromises } from "@vue/test-utils";
import { nextTick } from "vue";
import { useConfirmStore } from "../src/stores/confirm.js";
import {
  useConfirmDialog,
  promptDialog,
} from "../src/composables/useConfirmDialog.js";
import ConfirmationDialogHost from "../src/components/ConfirmationDialogHost.vue";
import DocumentsHubView from "../src/views/DocumentsHubView.vue";
import RactMatrixViewer from "../src/components/quality/RactMatrixViewer.vue";
import StatisticalComputingConsole from "../src/components/clinical/StatisticalComputingConsole.vue";
import LabGovernanceConsole from "../src/components/clinical/LabGovernanceConsole.vue";
import SynonymImportDrawer from "../src/components/SynonymImportDrawer.vue";
import IcfSectionEditor from "../src/components/econsent/IcfSectionEditor.vue";
import { useFileshareStore } from "../src/stores/fileshare";

describe("Confirmation Dialog Service & Modal Host", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    document.body.innerHTML = "";
  });

  describe("useConfirmStore & useConfirmDialog", () => {
    it("confirm() sets activeRequest and returns a Promise resolving to true when confirmed", async () => {
      const store = useConfirmStore();
      const promise = store.confirm({
        title: "Delete Document",
        message: "Are you sure you want to delete file.pdf?",
        confirmLabel: "Delete",
        cancelLabel: "Cancel",
        variant: "danger",
      });

      expect(store.activeRequest).not.toBeNull();
      expect(store.activeRequest.title).toBe("Delete Document");
      expect(store.activeRequest.message).toBe(
        "Are you sure you want to delete file.pdf?"
      );
      expect(store.activeRequest.confirmLabel).toBe("Delete");
      expect(store.activeRequest.cancelLabel).toBe("Cancel");
      expect(store.activeRequest.variant).toBe("danger");

      store.resolveConfirm();
      const result = await promise;
      expect(result).toBe(true);
      expect(store.activeRequest).toBeNull();
    });

    it("confirm() returns a Promise resolving to false when cancelled", async () => {
      const store = useConfirmStore();
      const promise = store.confirm({
        title: "Revoke Lock",
        message: "Are you sure?",
      });

      expect(store.activeRequest).not.toBeNull();
      store.cancelConfirm();

      const result = await promise;
      expect(result).toBe(false);
      expect(store.activeRequest).toBeNull();
    });

    it("handles string input and default options in confirm()", async () => {
      const store = useConfirmStore();
      const promise = store.confirm("Simple confirmation message");

      expect(store.activeRequest.title).toBe("Confirm Action");
      expect(store.activeRequest.message).toBe("Simple confirmation message");
      expect(store.activeRequest.confirmLabel).toBe("Confirm");
      expect(store.activeRequest.cancelLabel).toBe("Cancel");
      expect(store.activeRequest.variant).toBe("info");

      store.resolveConfirm();
      await promise;
    });

    it("queues concurrent confirm() and prompt() calls in FIFO order without cancelling earlier requests", async () => {
      const store = useConfirmStore();
      const first = store.confirm({
        title: "First Confirmation",
        message: "Proceed with step 1?",
      });
      const second = store.prompt({
        title: "Second Audit Prompt",
        message: "Provide Part 11 justification.",
        inputLabel: "Justification",
        required: true,
      });
      const third = store.confirm({
        title: "Third Confirmation",
        message: "Finalize step 3?",
        variant: "warning",
      });

      expect(store.activeRequest.title).toBe("First Confirmation");
      expect(store.queue).toHaveLength(2);

      store.resolveConfirm();
      expect(await first).toBe(true);

      expect(store.activeRequest.title).toBe("Second Audit Prompt");
      expect(store.queue).toHaveLength(1);
      store.setInputValue("Protocol amendment alignment");
      store.resolveConfirm();
      expect(await second).toBe("Protocol amendment alignment");

      expect(store.activeRequest.title).toBe("Third Confirmation");
      expect(store.queue).toHaveLength(0);
      store.cancelConfirm();
      expect(await third).toBe(false);
      expect(store.activeRequest).toBeNull();
    });

    it("useConfirmDialog composable delegates correctly to store", async () => {
      const { confirmDialog, confirm } = useConfirmDialog();
      const store = useConfirmStore();

      const promise1 = confirmDialog({ title: "Test 1", message: "Msg 1" });
      expect(store.activeRequest.title).toBe("Test 1");
      store.resolveConfirm();
      expect(await promise1).toBe(true);

      const promise2 = confirm({ title: "Test 2", message: "Msg 2" });
      expect(store.activeRequest.title).toBe("Test 2");
      store.cancelConfirm();
      expect(await promise2).toBe(false);
    });

    it("prompt() validates input, trims accepted value, and resolves to null on cancel", async () => {
      const store = useConfirmStore();
      const promise = promptDialog(
        {
          title: "21 CFR Part 11 Reason",
          message: "Provide reason for change.",
          inputLabel: "Reason for Change",
          required: true,
          validate: (val) =>
            val.length < 5 ? "Minimum 5 characters required." : "",
        },
        "   "
      );

      expect(store.activeRequest.mode).toBe("prompt");
      expect(store.activeRequest.inputLabel).toBe("Reason for Change");

      // Empty/whitespace value fails required check without closing dialog
      expect(store.resolveConfirm()).toBe(false);
      expect(store.activeRequest.validationError).toBe(
        "Reason for Change is required."
      );

      // Custom validator check
      store.setInputValue("abc");
      expect(store.activeRequest.validationError).toBe("");
      expect(store.resolveConfirm()).toBe(false);
      expect(store.activeRequest.validationError).toBe(
        "Minimum 5 characters required."
      );

      // Valid trimmed resolution
      store.setInputValue("  Updated reference range  ");
      expect(store.resolveConfirm()).toBe(true);
      expect(await promise).toBe("Updated reference range");
      expect(store.activeRequest).toBeNull();

      // Cancel resolves to null
      const cancelPromise = store.prompt("Optional note");
      store.cancelConfirm();
      expect(await cancelPromise).toBeNull();
    });
  });

  describe("ConfirmationDialogHost.vue", () => {
    it("renders nothing when store activeRequest is null", () => {
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });
      expect(document.querySelector(".clinical-modal-dialog")).toBeNull();
    });

    it("renders modal with role alertdialog and custom content when request is active", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      store.confirm({
        title: "Delete Document",
        message: "Are you sure you want to delete Lab_Results_001.pdf?",
        confirmLabel: "Yes, Delete",
        cancelLabel: "No, Keep",
        variant: "danger",
      });

      await nextTick();

      const modal = document.querySelector(".clinical-modal-dialog");
      expect(modal).not.toBeNull();
      expect(modal.getAttribute("role")).toBe("alertdialog");

      expect(document.querySelector(".modal-title").textContent.trim()).toBe(
        "Delete Document"
      );
      expect(
        document.querySelector(".confirmation-message").textContent.trim()
      ).toBe("Are you sure you want to delete Lab_Results_001.pdf?");
      expect(document.querySelector(".btn-confirm").textContent.trim()).toBe(
        "Yes, Delete"
      );
      expect(document.querySelector(".btn-cancel").textContent.trim()).toBe(
        "No, Keep"
      );
      expect(document.querySelector(".variant-danger")).not.toBeNull();
    });

    it("supports warning and info variants with appropriate button classes", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      store.confirm({
        title: "Warning Dialog",
        message: "Careful!",
        variant: "warning",
      });
      await nextTick();
      expect(document.querySelector(".variant-warning")).not.toBeNull();
      expect(document.querySelector(".btn-warning")).not.toBeNull();

      store.resolveConfirm();
      await nextTick();

      store.confirm({
        title: "Info Dialog",
        message: "Notice!",
        variant: "info",
      });
      await nextTick();
      expect(document.querySelector(".variant-info")).not.toBeNull();
      expect(document.querySelector(".btn-info")).not.toBeNull();
    });

    it("clicking confirm button resolves store promise to true", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const promise = store.confirm({
        title: "Confirm Action",
        message: "Ok?",
      });
      await nextTick();

      const confirmBtn = document.querySelector(".btn-confirm");
      confirmBtn.click();

      const result = await promise;
      expect(result).toBe(true);
      expect(store.activeRequest).toBeNull();
    });

    it("clicking cancel button resolves store promise to false", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const promise = store.confirm({
        title: "Confirm Action",
        message: "Ok?",
      });
      await nextTick();

      const cancelBtn = document.querySelector(".btn-cancel");
      cancelBtn.click();

      const result = await promise;
      expect(result).toBe(false);
      expect(store.activeRequest).toBeNull();
    });

    it("pressing Escape key cancels the dialog and resolves promise to false", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const promise = store.confirm({
        title: "ESC Test",
        message: "Press ESC",
      });
      await nextTick();

      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      const result = await promise;
      expect(result).toBe(false);
      expect(store.activeRequest).toBeNull();
    });

    it("captures document active element on open and restores focus on close", async () => {
      const button = document.createElement("button");
      button.id = "trigger-btn";
      document.body.appendChild(button);
      button.focus();
      expect(document.activeElement).toBe(button);

      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const promise = store.confirm({
        title: "Focus Test",
        message: "Testing focus",
      });
      await nextTick();

      store.cancelConfirm();
      await promise;
      await nextTick();

      expect(document.activeElement).toBe(button);
      document.body.removeChild(button);
    });

    it("traps focus within the confirmation dialog when Tab is pressed", async () => {
      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      store.confirm({ title: "Focus Trap Test", message: "Testing tab trap" });
      await nextTick();

      const closeBtn = document.querySelector(".modal-close-btn");
      const cancelBtn = document.querySelector(".btn-cancel");
      const confirmBtn = document.querySelector(".btn-confirm");

      confirmBtn.focus();
      const tabEvent = new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
      });
      window.dispatchEvent(tabEvent);

      // Focus wraps to the first focusable element (closeBtn)
      expect(document.activeElement).toBe(closeBtn);

      // Shift+Tab from first element wraps to last focusable element (confirmBtn)
      const shiftTabEvent = new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
      });
      window.dispatchEvent(shiftTabEvent);
      expect(document.activeElement).toBe(confirmBtn);
    });

    it("renders labeled prompt input, surfaces accessible validation errors, submits trimmed value on Enter, and cancels to null on Escape", async () => {
      const trigger = document.createElement("button");
      document.body.appendChild(trigger);
      trigger.focus();

      const store = useConfirmStore();
      mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const promptPromise = store.prompt(
        {
          title: "Lock Bundle",
          message: "Enter justification to lock bundle:",
          inputLabel: "Part 11 Justification",
          inputPlaceholder: "Type justification...",
          confirmLabel: "Lock",
          variant: "warning",
          required: true,
        },
        ""
      );
      await nextTick();

      const label = document.querySelector(".confirmation-prompt-label");
      const input = document.querySelector("#confirm-dialog-prompt-input");
      expect(label).not.toBeNull();
      expect(label.getAttribute("for")).toBe("confirm-dialog-prompt-input");
      expect(label.textContent.trim()).toBe("Part 11 Justification");
      expect(input).not.toBeNull();

      // Attempt empty submit -> renders role="alert" validation error and keeps modal open
      document.querySelector(".btn-confirm").click();
      await nextTick();
      const errorEl = document.querySelector("#confirm-dialog-prompt-error");
      expect(errorEl).not.toBeNull();
      expect(errorEl.getAttribute("role")).toBe("alert");
      expect(input.getAttribute("aria-invalid")).toBe("true");
      expect(input.getAttribute("aria-describedby")).toBe(
        "confirm-dialog-prompt-error"
      );

      // Type value and submit via Enter
      input.value = "  Protocol safety update  ";
      input.dispatchEvent(new Event("input", { bubbles: true }));
      await nextTick();
      expect(document.querySelector("#confirm-dialog-prompt-error")).toBeNull();

      input.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Enter", bubbles: true })
      );
      expect(await promptPromise).toBe("Protocol safety update");
      await nextTick();
      expect(document.activeElement).toBe(trigger);

      // Escape cancels prompt to null
      const escPromise = store.prompt({
        title: "Deactivate Rule",
        message: "Provide reason:",
        inputLabel: "Reason",
      });
      await nextTick();
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      expect(await escPromise).toBeNull();
      document.body.removeChild(trigger);
    });
  });

  describe("Refactored View Components", () => {
    it("DocumentsHubView uses confirmDialog instead of window.confirm", async () => {
      const store = useConfirmStore();
      const fileshareStore = useFileshareStore();
      const deleteSpy = vi
        .spyOn(fileshareStore, "deleteFile")
        .mockResolvedValue(true);

      const testFile = {
        id: "FILE-1",
        filename: "TestDoc.pdf",
        is_on_hold: false,
      };

      const wrapper = mount(DocumentsHubView, {
        global: {
          plugins: [pinia],
          stubs: {
            DocumentsFolderTree: true,
            ShareDrawer: true,
            ActivityFeed: true,
            MediaPreviewModal: true,
            BatchUploadDrawer: true,
            CraQcQueue: true,
          },
        },
      });

      const deletePromise = wrapper.vm.handleDeleteFile(testFile);
      await nextTick();

      expect(store.activeRequest).not.toBeNull();
      expect(store.activeRequest.title).toBe("Delete Document");
      expect(store.activeRequest.message).toContain("TestDoc.pdf");

      store.resolveConfirm();
      await deletePromise;
      await flushPromises();

      expect(deleteSpy).toHaveBeenCalledWith(
        "FILE-1",
        "User deleted document from Documents Hub"
      );
    });

    it("RactMatrixViewer uses confirmDialog in deleteFactor", async () => {
      const store = useConfirmStore();
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockImplementation((url) => {
          if (typeof url === "string" && url.includes("/factors?study_id=")) {
            return Promise.resolve(
              new Response(JSON.stringify([]), { status: 200 })
            );
          }
          if (
            typeof url === "string" &&
            url.includes("/site-risk-profiles?study_id=")
          ) {
            return Promise.resolve(
              new Response(JSON.stringify([]), { status: 200 })
            );
          }
          return Promise.resolve(
            new Response(JSON.stringify({ ok: true }), { status: 200 })
          );
        });

      const wrapper = mount(RactMatrixViewer, {
        global: {
          plugins: [pinia],
        },
      });

      const deletePromise = wrapper.vm.deleteFactor("FACTOR-99");
      await nextTick();

      expect(store.activeRequest).not.toBeNull();
      expect(store.activeRequest.title).toBe("Delete Risk Factor");
      expect(store.activeRequest.message).toBe(
        "Are you sure you want to delete this protocol risk factor?"
      );

      store.resolveConfirm();
      await deletePromise;
      await flushPromises();

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/v1/quality/rbqm/ract/factors/FACTOR-99",
        expect.objectContaining({ method: "DELETE" })
      );
      fetchSpy.mockRestore();
    });

    it("StatisticalComputingConsole uses confirmDialog in revokeToken", async () => {
      const store = useConfirmStore();

      const wrapper = mount(StatisticalComputingConsole, {
        global: {
          plugins: [pinia],
        },
      });

      const revokePromise = wrapper.vm.revokeToken("TOKEN-1234");
      await nextTick();

      expect(store.activeRequest).not.toBeNull();
      expect(store.activeRequest.title).toBe("Revoke Lock Token");
      expect(store.activeRequest.message).toBe(
        "Are you sure you want to revoke token TOKEN-1234?"
      );

      store.resolveConfirm();
      await revokePromise;
      await flushPromises();

      expect(store.activeRequest).toBeNull();
    });

    it("SynonymImportDrawer, LabGovernanceConsole, and IcfSectionEditor use promptDialog instead of window.prompt", async () => {
      const promptSpy = vi.spyOn(window, "prompt").mockReturnValue(null);
      const store = useConfirmStore();

      // 1. SynonymImportDrawer deactivateRule
      const drawer = mount(SynonymImportDrawer, {
        global: { plugins: [pinia] },
      });
      const deactivatePromise = drawer.vm.deactivateRule({
        id: "rule-42",
        verbatim_text: "headache",
      });
      await nextTick();
      expect(store.activeRequest?.mode).toBe("prompt");
      expect(store.activeRequest?.title).toBe("Deactivate Synonym Rule");
      // Part 11 prompts must require manual text entry rather than pre-populated defaultValue
      expect(store.activeRequest?.inputValue).toBe("");
      store.cancelConfirm();
      await deactivatePromise;

      // 2. LabGovernanceConsole openCreateRangeModal
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockImplementation(() =>
          Promise.resolve(new Response(JSON.stringify([]), { status: 200 }))
        );
      const labConsole = mount(LabGovernanceConsole, {
        global: { plugins: [pinia] },
      });
      await flushPromises();
      const rangePromise = labConsole.vm.openCreateRangeModal();
      await nextTick();
      expect(store.activeRequest?.mode).toBe("prompt");
      expect(store.activeRequest?.title).toBe("Register Reference Range");
      expect(store.activeRequest?.inputValue).toBe("");
      store.cancelConfirm();
      await rangePromise;
      fetchSpy.mockRestore();

      // 3. IcfSectionEditor insertImage
      const icfEditor = mount(IcfSectionEditor, {
        props: {
          section: { id: "sec_1", title: "Intro", html: "<p>placebo</p>" },
        },
        global: { plugins: [pinia] },
      });
      document.execCommand = vi.fn();
      const imagePromise = icfEditor.vm.insertImage();
      await nextTick();
      expect(store.activeRequest?.mode).toBe("prompt");
      expect(store.activeRequest?.title).toBe("Insert Consent Diagram");
      store.setInputValue("/assets/custom-diagram.svg");
      store.resolveConfirm();
      await imagePromise;

      expect(promptSpy).not.toHaveBeenCalled();
      promptSpy.mockRestore();
    });

    it("focuses .btn-cancel on confirmations with aria-describedby and refocuses #confirm-dialog-prompt-input on form validation error", async () => {
      const store = useConfirmStore();
      const host = mount(ConfirmationDialogHost, {
        attachTo: document.body,
        global: { plugins: [pinia] },
      });

      const confirmPromise = store.confirm({
        title: "Unsaved eCRF Changes",
        message: "Discard unsaved observations?",
        variant: "warning",
        confirmLabel: "Discard Changes",
        cancelLabel: "Stay on Form",
      });
      await nextTick();
      await nextTick();

      const alertDialog = document.body.querySelector('[role="alertdialog"]');
      expect(alertDialog?.getAttribute("aria-describedby")).toBe(
        "confirm-dialog-message"
      );
      expect(document.activeElement?.classList.contains("btn-cancel")).toBe(
        true
      );
      document.activeElement.click();
      expect(await confirmPromise).toBe(false);

      const promptPromise = store.prompt({
        title: "Audit Justification",
        message: "Enter reason for change.",
        inputLabel: "Reason for Change",
        required: true,
      });
      await nextTick();
      await nextTick();

      const promptForm = document.getElementById("confirm-dialog-prompt-form");
      const promptInput = document.getElementById(
        "confirm-dialog-prompt-input"
      );
      const submitBtn = document.body.querySelector(".btn-confirm");
      expect(promptForm).not.toBeNull();
      expect(document.activeElement).toBe(promptInput);

      // Move focus to submit button and submit empty prompt -> refocuses #confirm-dialog-prompt-input
      submitBtn.focus();
      expect(document.activeElement).toBe(submitBtn);
      submitBtn.click();
      await nextTick();
      await nextTick();

      expect(document.activeElement).toBe(promptInput);
      expect(
        document.getElementById("confirm-dialog-prompt-error")?.textContent
      ).toContain("Reason for Change is required.");

      store.cancelConfirm();
      expect(await promptPromise).toBeNull();
      host.unmount();
    });
  });
});
