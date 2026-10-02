import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SignatureCaptureModal from "../../src/features/signatures/components/SignatureCaptureModal.vue";
import UpversioningImpactDrawer from "../../src/components/UpversioningImpactDrawer.vue";
import ReAuthModal from "../../src/components/shared/ReAuthModal.vue";
import { FileUploadModal } from "ui";
import { useCodingStore } from "../../src/stores/coding";

describe("ModalErrorAlert Retry Actions Across Overlays (#4523)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    document.body.innerHTML = "";
  });

  describe("SignatureCaptureModal.vue", () => {
    it("displays ModalErrorAlert on failure and re-invokes signature confirmation when Retry is clicked", async () => {
      let attemptCount = 0;
      const mockOnSign = vi.fn(async () => {
        attemptCount++;
        if (attemptCount === 1) {
          throw new Error("Network timeout during signature verification");
        }
        return { status: "SUCCESS" };
      });

      const mockSignatureService = {
        verifySignature: vi.fn(async () => ({ sig_token: "mock-sig-token-123" })),
      };

      const wrapper = mount(SignatureCaptureModal, {
        props: {
          isOpen: true,
          username: "dr_smith",
          signerName: "Dr. Smith",
          actionUrl: "/api/v1/etmf/documents/doc-99/sign-off",
          onSign: mockOnSign,
        },
        global: {
          provide: {
            signatureService: mockSignatureService,
          },
        },
      });

      // Fill in required fields
      wrapper.vm.password = "Secret123!";
      wrapper.vm.signingReason = "APPROVAL";

      // First attempt fails via mockOnSign
      await wrapper.vm.confirm();

      expect(mockOnSign).toHaveBeenCalledTimes(1);
      expect(wrapper.find("#sig-error-msg").exists()).toBe(true);
      expect(wrapper.text()).toContain("Signing failed: Network timeout during signature verification");

      // Re-enter password for GxP safety check
      wrapper.vm.password = "Secret123!";

      // Click Retry button in ModalErrorAlert
      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      expect(retryBtn.exists()).toBe(true);
      await retryBtn.trigger("click");

      // Verify confirm action was re-invoked and succeeded
      expect(mockOnSign).toHaveBeenCalledTimes(2);
    });
  });

  describe("UpversioningImpactDrawer.vue", () => {
    it("displays ModalErrorAlert when analysis fails and re-triggers delta engine on Retry click", async () => {
      const codingStore = useCodingStore();
      codingStore.impactAnalysis.isOpen = true;
      codingStore.impactAnalysis.error = "Upversioning Delta Engine API unreachable";

      const runAnalysisSpy = vi.spyOn(codingStore, "runImpactAnalysis").mockResolvedValue();

      const wrapper = mount(UpversioningImpactDrawer);

      expect(wrapper.find(".modal-error-alert").exists()).toBe(true);
      expect(wrapper.text()).toContain("Upversioning Delta Engine API unreachable");

      runAnalysisSpy.mockClear();

      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      expect(retryBtn.exists()).toBe(true);
      expect(retryBtn.text()).toBe("Attempt Again");

      await retryBtn.trigger("click");

      expect(runAnalysisSpy).toHaveBeenCalledTimes(1);
    });
  });

  describe("ReAuthModal.vue", () => {
    it("displays ModalErrorAlert when re-authentication fails and re-emits confirm on Retry click", async () => {
      const wrapper = mount(ReAuthModal, {
        props: {
          isOpen: true,
          errorMessage: "Re-authentication failed: Invalid password",
        },
      });

      expect(wrapper.find(".modal-error-alert").exists()).toBe(true);
      expect(wrapper.text()).toContain("Re-authentication failed: Invalid password");

      // Set credential
      wrapper.vm.credentialVal = "ValidPass123";

      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      expect(retryBtn.exists()).toBe(true);
      await retryBtn.trigger("click");

      expect(wrapper.emitted("confirm")).toBeTruthy();
      expect(wrapper.emitted("confirm")?.[0][0]).toEqual({
        credential: "ValidPass123",
        signing_reason: "APPROVAL",
      });
    });
  });

  describe("FileUploadModal.vue", () => {
    it("displays ModalErrorAlert when upload fails and re-triggers startUpload on Retry click", async () => {
      const wrapper = mount(FileUploadModal, {
        props: {
          modelValue: true,
          studyId: "STUDY-2026",
        },
        attachTo: document.body,
      });

      const testFile = new File(["dummy data"], "consent_v2.pdf", { type: "application/pdf" });
      wrapper.vm.selectedFile = testFile;
      wrapper.vm.reasonForChange = "Updated ICF document protocol amendment";
      wrapper.vm.errorMessage = "Presigned S3 URL allocation timeout";
      await wrapper.vm.$nextTick();

      const alertEl = document.body.querySelector(".modal-error-alert");
      expect(alertEl).not.toBeNull();
      expect(alertEl?.textContent).toContain("Presigned S3 URL allocation timeout");

      // Mock fetch for upload
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ file_id: "file-123", upload_url: "https://s3.amazonaws.com/upload" }),
      });

      const retryBtn = document.body.querySelector('[data-testid="modal-error-retry-btn"]');
      expect(retryBtn).not.toBeNull();
      expect(retryBtn?.textContent?.trim()).toBe("Retry Upload");

      (retryBtn as HTMLButtonElement).click();
      await wrapper.vm.$nextTick();

      expect(global.fetch).toHaveBeenCalledWith("/api/v1/files/upload/presigned-url", expect.anything());
      wrapper.unmount();
    });

    it("clears error and resets dropzone state when Dismiss is clicked in FileUploadModal", async () => {
      const wrapper = mount(FileUploadModal, {
        props: {
          modelValue: true,
          studyId: "STUDY-2026",
        },
        attachTo: document.body,
      });

      const testFile = new File(["dummy data"], "consent_v2.pdf", { type: "application/pdf" });
      wrapper.vm.selectedFile = testFile;
      wrapper.vm.errorMessage = "Invalid file type";
      await wrapper.vm.$nextTick();

      const alertEl = document.body.querySelector(".modal-error-alert");
      expect(alertEl).not.toBeNull();

      const dismissBtn = document.body.querySelector('[data-testid="modal-error-dismiss-btn"]');
      expect(dismissBtn).not.toBeNull();

      (dismissBtn as HTMLButtonElement).click();
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.errorMessage).toBe("");
      expect(wrapper.vm.selectedFile).toBe(null);
      expect(document.body.querySelector(".modal-error-alert")).toBeNull();
      wrapper.unmount();
    });
  });
});
