import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SignatureCaptureModal from "../src/components/SignatureCaptureModal.vue";
import { etmfService } from "../src/api/etmf";
import { econsentService } from "../src/api/econsent";
import { useConsentGating } from "../src/composables/useConsentGating";
import { ref } from "vue";

vi.mock("../src/api/etmf", () => ({
  etmfService: {
    verifySignature: vi.fn(),
    signDocument: vi.fn(),
  },
}));

vi.mock("../src/api/econsent", () => ({
  econsentService: {
    verifySignature: vi.fn(),
  },
}));

describe("Participant eConsent Step-Up Signature Verification", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("SignatureCaptureModal sends verification request to /api/v1/auth/signature-verification and wipes password fields immediately", async () => {
    vi.mocked(etmfService.verifySignature).mockResolvedValue({
      sig_token: "valid-60s-stepup-token-abc123",
      expires_in: 60,
    });

    const onSignMock = vi.fn().mockResolvedValue({
      status: "SIGNED",
      sigToken: "valid-60s-stepup-token-abc123",
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "participant.cadence101",
        actionUrl:
          "/api/v1/econsent/templates/icf-001/versions/1/capture-consent",
        onSign: onSignMock,
      },
      global: {
        plugins: [pinia],
      },
    });

    // Fill in credentials
    await wrapper.find("#sig-password").setValue("SecretPassword123!"); // pragma: allowlist secret
    await wrapper.find("#sig-totp").setValue("654321");
    await wrapper.find("#sig-reason").setValue("I agree to participate");

    // Click confirm
    await wrapper.find("#btn-confirm-sig").trigger("click");

    // Check that verifySignature was called with actionUrl and semanticAction
    expect(etmfService.verifySignature).toHaveBeenCalledWith({
      username: "participant.cadence101",
      password: "SecretPassword123!", // pragma: allowlist secret
      totp: "654321",
      action: "/api/v1/econsent/templates/icf-001/versions/1/capture-consent",
      semanticAction: "econsent.consent.capture",
    });

    // Check that password and totp fields are wiped immediately
    expect(wrapper.vm.password).toBe("");
    expect(wrapper.vm.totp).toBe("");

    // Check that onSign received the issued single-use token
    expect(onSignMock).toHaveBeenCalledWith(
      "valid-60s-stepup-token-abc123",
      "I agree to participate",
      expect.objectContaining({
        meaningOfSigning: "I agree to participate",
      })
    );
  });

  it("SignatureCaptureModal displays error when step-up verification fails with REAUTHENTICATION_REQUIRED", async () => {
    vi.mocked(etmfService.verifySignature).mockRejectedValue({
      message: "REAUTHENTICATION_REQUIRED",
      response: {
        status: 401,
        data: { detail: "REAUTHENTICATION_REQUIRED" },
      },
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "participant.cadence101",
        actionUrl:
          "/api/v1/econsent/templates/icf-001/versions/1/capture-consent",
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#sig-password").setValue("wrong_password"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("I agree to participate");
    await wrapper.find("#btn-confirm-sig").trigger("click");

    // Password wiped immediately
    expect(wrapper.vm.password).toBe("");

    // UI displays error instructing re-verification
    await wrapper.vm.$nextTick();
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Identity verification expired or invalid"
    );
  });

  it("useConsentGating handles step-up signature re-authentication token for re-consent", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const activeSubject = ref("SUBJ-002");
    const { handleCompleteReconsent, isReconsentGated } =
      useConsentGating(activeSubject);

    const mockStore = {
      clearReconsentGate: vi.fn().mockResolvedValue(),
      addLedgerBlock: vi.fn().mockResolvedValue({}),
    };

    await handleCompleteReconsent("ECONSENT", mockStore, {
      sigToken: "token-reconsent-456",
      signingReason: "I agree to participate",
    });

    expect(mockStore.clearReconsentGate).toHaveBeenCalledWith(
      "SUBJ-002",
      "ECONSENT",
      expect.stringContaining("re-consent recorded"),
      "token-reconsent-456"
    );
  });
});
