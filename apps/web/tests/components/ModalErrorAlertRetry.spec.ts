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
        verifySignature: vi.fn(async () => ({
          sig_token: "mock-sig-token-123",
        })),
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
      const vm = wrapper.vm as Record<string, any>;
      vm.password = "Secret123!"; // pragma: allowlist secret
      vm.signingReason = "APPROVAL";

      // First attempt fails via mockOnSign
      await vm.confirm();

      expect(mockOnSign).toHaveBeenCalledTimes(1);
      expect(wrapper.find("#sig-error-msg").exists()).toBe(true);
      expect(wrapper.text()).toContain(
        "Signing failed: Network timeout during signature verification"
      );

      // Re-enter password for GxP safety check
      vm.password = "Secret123!"; // pragma: allowlist secret

      // Click Retry button in ModalErrorAlert
      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      expect(retryBtn.exists()).toBe(true);
      await retryBtn.trigger("click");

      // Verify confirm action was re-invoked and succeeded
      expect(mockOnSign).toHaveBeenCalledTimes(2);
    });

    it("focuses #sig-password on Retry when password was wiped after step-up expiry without clobbering the error", async () => {
      const mockOnSign = vi
        .fn()
        .mockRejectedValueOnce(new Error("REAUTHENTICATION_REQUIRED"))
        .mockResolvedValueOnce({ kind: "signed", meaning: "approved" });

      const mockSignatureService = {
        verifySignature: vi.fn(async () => ({
          sig_token: "mock-sig-token-expired",
        })),
      };

      const wrapper = mount(SignatureCaptureModal, {
        attachTo: document.body,
        props: {
          isOpen: true,
          username: "dr_smith",
          signerName: "Dr. Smith", // deid-ignore
          reasons: ["Investigator Certification", "APPROVAL"],
          actionUrl: "/api/v1/execution/forms/form-1/signatures",
          semanticAction: "execution.form.sign",
          onSign: mockOnSign,
        },
        global: {
          provide: {
            signatureService: mockSignatureService,
          },
        },
      });

      const options = wrapper
        .findAll("#sig-reason option")
        .map((o) => o.text());
      expect(options).toContain("Investigator Certification");
      expect(options).not.toContain("I agree to participate");

      await wrapper.find("#sig-password").setValue("Secret123!"); // pragma: allowlist secret
      await wrapper.find("#sig-reason").setValue("Investigator Certification");
      await wrapper.find("#btn-confirm-sig").trigger("click");
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.password).toBe("");
      expect(wrapper.find("#sig-error-msg").text()).toContain(
        "Identity verification expired or invalid"
      );

      // Clicking Retry while password is empty focuses #sig-password and preserves the actionable error
      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      await retryBtn.trigger("click");

      expect(document.activeElement?.id).toBe("sig-password");
      expect(mockOnSign).toHaveBeenCalledTimes(1);
      expect(wrapper.find("#sig-error-msg").text()).toContain(
        "Identity verification expired or invalid"
      );

      // Entering password and clicking Retry completes the signature
      await wrapper.find("#sig-password").setValue("Secret123!"); // pragma: allowlist secret
      await retryBtn.trigger("click");
      await wrapper.vm.$nextTick();

      expect(mockOnSign).toHaveBeenCalledTimes(2);
      expect(wrapper.emitted("success")).toBeTruthy();
      wrapper.unmount();
    });

    it("guards against failed onSign outcome objects without emitting success", async () => {
      const mockOnSign = vi.fn(async () => ({
        kind: "denied",
        message: "You are not authorised to sign this form.",
      }));

      const mockSignatureService = {
        verifySignature: vi.fn(async () => ({
          sig_token: "mock-sig-token-403",
        })),
      };

      const wrapper = mount(SignatureCaptureModal, {
        props: {
          isOpen: true,
          username: "dr_smith",
          actionUrl: "/api/v1/execution/forms/form-1/signatures",
          semanticAction: "execution.form.sign",
          onSign: mockOnSign,
        },
        global: {
          provide: {
            signatureService: mockSignatureService,
          },
        },
      });

      const vm = wrapper.vm as Record<string, any>;
      vm.password = "Secret123!"; // pragma: allowlist secret
      vm.signingReason = "APPROVAL";
      await vm.confirm();

      expect(wrapper.emitted("success")).toBeUndefined();
      expect(wrapper.find("#sig-error-msg").text()).toContain(
        "You are not authorised to sign this form."
      );
    });

    it("renders a form, focuses #sig-reason on Retry when signingReason is empty, and restores focus to #sig-password after 401 busy recovery", async () => {
      const mockSignatureService = {
        verifySignature: vi
          .fn()
          .mockRejectedValueOnce({
            response: { status: 401, data: { detail: "Invalid credentials" } },
          })
          .mockResolvedValueOnce({ sig_token: "sig-token-ok" }),
      };
      const mockOnSign = vi.fn().mockResolvedValue({ status: "SIGNED" });

      const wrapper = mount(SignatureCaptureModal, {
        attachTo: document.body,
        props: {
          isOpen: true,
          username: "dr_smith",
          actionUrl: "/api/v1/execution/forms/form-1/signatures",
          onSign: mockOnSign,
        },
        global: {
          provide: {
            signatureService: mockSignatureService,
          },
        },
      });

      expect(wrapper.find("form#signature-capture-form").exists()).toBe(true);
      const confirmBtn = wrapper.find("#btn-confirm-sig");
      expect(confirmBtn.attributes("type")).toBe("submit");
      expect(confirmBtn.attributes("disabled")).toBeDefined();

      // Submitting via form without password/reason focuses missing field and binds ARIA error attributes
      await wrapper.find("#sig-password").setValue("WrongPass1!"); // pragma: allowlist secret
      expect(confirmBtn.attributes("disabled")).toBeDefined();
      await wrapper.find("form#signature-capture-form").trigger("submit");
      await wrapper.vm.$nextTick();

      expect(wrapper.find("#sig-error-msg").text()).toContain(
        "Signing reason is required."
      );
      expect(wrapper.find("#sig-reason").attributes("aria-invalid")).toBe(
        "true"
      );
      expect(wrapper.find("#sig-reason").attributes("aria-describedby")).toBe(
        "sig-error-msg"
      );
      expect(document.activeElement?.id).toBe("sig-reason");

      // Clicking Retry while signingReason is still empty focuses #sig-reason instead of calling verifySignature
      (document.activeElement as HTMLElement | null)?.blur?.();
      await wrapper
        .find('[data-testid="modal-error-retry-btn"]')
        .trigger("click");
      expect(document.activeElement?.id).toBe("sig-reason");
      expect(mockSignatureService.verifySignature).not.toHaveBeenCalled();

      // Selecting signingReason enables #btn-confirm-sig; 401 failure wipes password and restores focus to #sig-password after busy clears
      await wrapper.find("#sig-reason").setValue("APPROVAL");
      expect(confirmBtn.attributes("disabled")).toBeUndefined();
      await confirmBtn.trigger("click");
      await new Promise((resolve) => setTimeout(resolve, 20));
      await wrapper.vm.$nextTick();

      expect(wrapper.vm.password).toBe("");
      expect(document.activeElement?.id).toBe("sig-password");
      expect(wrapper.find("#sig-password").attributes("aria-invalid")).toBe(
        "true"
      );
      expect(
        wrapper.find("#sig-password").attributes("aria-describedby")
      ).toContain("sig-error-msg");

      wrapper.unmount();
    });
  });

  describe("UpversioningImpactDrawer.vue", () => {
    it("displays ModalErrorAlert when analysis fails and re-triggers delta engine on Retry click", async () => {
      const codingStore = useCodingStore();
      codingStore.impactAnalysis.isOpen = true;
      codingStore.impactAnalysis.error =
        "Upversioning Delta Engine API unreachable";

      const runAnalysisSpy = vi
        .spyOn(codingStore, "runImpactAnalysis")
        .mockResolvedValue(undefined as never);

      const wrapper = mount(UpversioningImpactDrawer);

      expect(wrapper.find(".modal-error-alert").exists()).toBe(true);
      expect(wrapper.text()).toContain(
        "Upversioning Delta Engine API unreachable"
      );

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
      expect(wrapper.text()).toContain(
        "Re-authentication failed: Invalid password"
      );

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

    it("surfaces localError on cleared-credential Retry and clears error on Dismiss while focusing #reauth-credential", async () => {
      const wrapper = mount(ReAuthModal, {
        attachTo: document.body,
        props: {
          isOpen: true,
          errorMessage: "Re-authentication failed: Invalid password",
        },
      });

      const retryBtn = wrapper.find('[data-testid="modal-error-retry-btn"]');
      await retryBtn.trigger("click");

      expect(wrapper.emitted("confirm")).toBeFalsy();
      expect(wrapper.find("#reauth-error-alert").text()).toContain(
        "PIN or password credential is required."
      );
      expect(document.activeElement?.id).toBe("reauth-credential");

      const dismissBtn = wrapper.find(
        '[data-testid="modal-error-dismiss-btn"]'
      );
      await dismissBtn.trigger("click");
      expect(wrapper.emitted("clear-error")).toBeTruthy();
      expect(wrapper.find("#reauth-error-alert").exists()).toBe(false);
      expect(document.activeElement?.id).toBe("reauth-credential");
      wrapper.unmount();
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

      const testFile = new File(["dummy data"], "consent_v2.pdf", {
        type: "application/pdf",
      });
      wrapper.vm.selectedFile = testFile;
      wrapper.vm.reasonForChange = "Updated ICF document protocol amendment";
      wrapper.vm.errorMessage = "Presigned S3 URL allocation timeout";
      await wrapper.vm.$nextTick();

      const alertEl = document.body.querySelector(".modal-error-alert");
      expect(alertEl).not.toBeNull();
      expect(alertEl?.textContent).toContain(
        "Presigned S3 URL allocation timeout"
      );

      // Mock fetch for upload
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          file_id: "file-123",
          upload_url: "https://s3.amazonaws.com/upload",
        }),
      });

      const retryBtn = document.body.querySelector(
        '[data-testid="modal-error-retry-btn"]'
      );
      expect(retryBtn).not.toBeNull();
      expect(retryBtn?.textContent?.trim()).toBe("Retry Upload");

      (retryBtn as HTMLButtonElement).click();
      await wrapper.vm.$nextTick();

      expect(global.fetch).toHaveBeenCalledWith(
        "/api/v1/files/upload/presigned-url",
        expect.anything()
      );
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

      const testFile = new File(["dummy data"], "consent_v2.pdf", {
        type: "application/pdf",
      });
      wrapper.vm.selectedFile = testFile;
      wrapper.vm.errorMessage = "Invalid file type";
      await wrapper.vm.$nextTick();

      const alertEl = document.body.querySelector(".modal-error-alert");
      expect(alertEl).not.toBeNull();

      const dismissBtn = document.body.querySelector(
        '[data-testid="modal-error-dismiss-btn"]'
      );
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
