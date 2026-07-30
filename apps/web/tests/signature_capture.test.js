import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SignatureCaptureModal from "../src/components/SignatureCaptureModal.vue";
import { etmfService } from "../src/api/etmf";

// Mock the eTMF API client
vi.mock("../src/api/etmf", () => {
  return {
    etmfService: {
      verifySignature: vi.fn(),
      signDocument: vi.fn(),
    },
  };
});

describe("SignatureCaptureModal and eTMF Sign-off Flow", () => {
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("does not render when isOpen is false", () => {
    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: false,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#signature-capture-modal").exists()).toBe(false);
  });

  it("renders with standard input fields and controls when isOpen is true", () => {
    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find("#signature-capture-modal").exists()).toBe(true);
    expect(wrapper.find("#sig-username").element.value).toBe("fderuiter");
    expect(wrapper.find("#sig-password").exists()).toBe(true);
    expect(wrapper.find("#sig-totp").exists()).toBe(true);
    expect(wrapper.find("#sig-reason").exists()).toBe(true);
    expect(wrapper.find("#btn-cancel-sig").exists()).toBe(true);
    expect(wrapper.find("#btn-confirm-sig").exists()).toBe(true);
  });

  it("triggers verifySignature and signDocument on happy path, emits success, and clears credentials", async () => {
    // Mock API success responses
    etmfService.verifySignature.mockResolvedValue({
      sig_token: "mock-tmf-signature-token",
    });

    const mockManifestation = {
      signer_id: "fderuiter",
      timestamp: "2026-07-28T12:00:00Z",
      signing_reason: "APPROVAL",
      ip_address: "127.0.0.1",
      sha256_hash: "mock-sha256-hash",
      signature: "mock-signature-base64",
    };

    etmfService.signDocument.mockResolvedValue({
      id: "doc-123",
      status: "SIGNED",
      signature_manifestation: mockManifestation,
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    // Enter details
    await wrapper.find("#sig-password").setValue("valid_password"); // pragma: allowlist secret
    await wrapper.find("#sig-totp").setValue("123456");
    await wrapper.find("#sig-reason").setValue("APPROVAL");

    // Click confirm
    await wrapper.find("#btn-confirm-sig").trigger("click");

    // Verify verifySignature call
    expect(etmfService.verifySignature).toHaveBeenCalledWith({
      username: "fderuiter",
      password: "valid_password", // pragma: allowlist secret
      totp: "123456",
      action: "/api/v1/etmf/documents/doc-123/sign-off",
    });

    // Wait for async calls to finish
    await new Promise((resolve) => setTimeout(resolve, 50));
    await wrapper.vm.$nextTick();

    // Verify signDocument call
    expect(etmfService.signDocument).toHaveBeenCalledWith(
      "doc-123",
      { signingReason: "APPROVAL" },
      {
        changeReason: "Part 11 Document Sign-off: Reason - APPROVAL",
        sigToken: "mock-tmf-signature-token",
      }
    );

    // Verify credentials are cleared
    expect(wrapper.vm.password).toBe("");
    expect(wrapper.vm.totp).toBe("");

    // Verify success event emitted with manifestation info
    expect(wrapper.emitted("success")).toBeTruthy();
    expect(wrapper.emitted("success")[0][0]).toEqual({
      id: "doc-123",
      status: "SIGNED",
      signature_manifestation: mockManifestation,
    });
  });

  it("handles cancel button cleanly by clearing fields and emitting cancel", async () => {
    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#sig-password").setValue("some_pwd"); // pragma: allowlist secret
    await wrapper.find("#sig-totp").setValue("654321");

    await wrapper.find("#btn-cancel-sig").trigger("click");

    expect(wrapper.vm.password).toBe("");
    expect(wrapper.vm.totp).toBe("");
    expect(wrapper.emitted("cancel")).toBeTruthy();
  });

  it("maps ROLE_INSUFFICIENT error correctly to a clear message and wipes credentials", async () => {
    // Mock verifySignature failure with ROLE_INSUFFICIENT
    etmfService.verifySignature.mockRejectedValue({
      response: {
        status: 403,
        data: {
          detail: "ROLE_INSUFFICIENT",
        },
      },
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#sig-password").setValue("admin123"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("CLINICAL_QC");

    await wrapper.find("#btn-confirm-sig").trigger("click");

    await new Promise((resolve) => setTimeout(resolve, 50));
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#sig-error-msg").exists()).toBe(true);
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Forbidden: Your role does not possess permissions to sign documents"
    );

    // Credentials must be wiped
    expect(wrapper.vm.password).toBe("");
  });

  it("maps expired/invalid step-up token error correctly to a clear message and wipes credentials", async () => {
    // Mock verifySignature success but signDocument failing on REAUTHENTICATION_REQUIRED (expired token)
    etmfService.verifySignature.mockResolvedValue({
      sig_token: "expired-token",
    });

    etmfService.signDocument.mockRejectedValue({
      response: {
        status: 401,
        data: {
          detail: "REAUTHENTICATION_REQUIRED",
        },
      },
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#sig-password").setValue("somepassword"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("REVIEW");

    await wrapper.find("#btn-confirm-sig").trigger("click");

    await new Promise((resolve) => setTimeout(resolve, 50));
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#sig-error-msg").exists()).toBe(true);
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Identity verification expired or invalid. Please try again."
    );

    // Credentials must be wiped
    expect(wrapper.vm.password).toBe("");
  });

  it("maps invalid credentials error correctly to a clear message and wipes credentials", async () => {
    // Mock verifySignature failure with 401 Invalid credentials
    etmfService.verifySignature.mockRejectedValue({
      response: {
        status: 401,
        data: {
          detail: "Invalid credentials",
        },
      },
    });

    const wrapper = mount(SignatureCaptureModal, {
      props: {
        isOpen: true,
        username: "fderuiter",
        actionUrl: "/api/v1/etmf/documents/doc-123/sign-off",
      },
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#sig-password").setValue("wrongpassword"); // pragma: allowlist secret
    await wrapper.find("#sig-reason").setValue("AUTHOR");

    await wrapper.find("#btn-confirm-sig").trigger("click");

    await new Promise((resolve) => setTimeout(resolve, 50));
    await wrapper.vm.$nextTick();

    expect(wrapper.find("#sig-error-msg").exists()).toBe(true);
    expect(wrapper.find("#sig-error-msg").text()).toContain(
      "Identity verification failed: Invalid credentials."
    );

    // Credentials must be wiped
    expect(wrapper.vm.password).toBe("");
  });
});
