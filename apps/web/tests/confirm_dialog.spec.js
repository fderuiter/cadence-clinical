import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { mount, flushPromises } from "@vue/test-utils";
import { nextTick } from "vue";
import { useConfirmStore } from "../src/stores/confirm.js";
import { useConfirmDialog } from "../src/composables/useConfirmDialog.js";
import ConfirmationDialogHost from "../src/components/ConfirmationDialogHost.vue";
import DocumentsHubView from "../src/views/DocumentsHubView.vue";
import RactMatrixViewer from "../src/components/quality/RactMatrixViewer.vue";
import StatisticalComputingConsole from "../src/components/clinical/StatisticalComputingConsole.vue";
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
      expect(store.activeRequest.variant).toBe("danger");

      store.resolveConfirm();
      await promise;
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
  });
});
