import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import EcrfView from "../src/views/EcrfView.vue";
import { soaClient } from "../src/api/soaClient";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";

// Mock the API client
vi.mock("../src/api/soaClient", () => {
  return {
    soaClient: {
      verifySignature: vi.fn(),
      batchSignOff: vi.fn(),
    },
  };
});

describe("PI Sign-Off Worklist and Re-authentication Flow", () => {
  let pinia;
  let clinicalStore;
  let authStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clinicalStore = useClinicalStore();
    authStore = useAuthStore();

    // Reset mocks
    vi.clearAllMocks();
  });

  it("renders the PI Sign-Off Worklist card on EcrfView", () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("PI Sign-Off Worklist & Verification");
    expect(wrapper.find("#signoff-target-type").exists()).toBe(true);
    expect(wrapper.find("#signoff-target-id").exists()).toBe(true);
    expect(wrapper.find("#signoff-reason").exists()).toBe(true);
    expect(wrapper.find("#btn-pi-signoff").exists()).toBe(true);
  });

  it("initiates the re-authentication flow when clicking sign-off", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Populate target selection
    await wrapper.find("#signoff-target-type").setValue("FORM");
    await wrapper.find("#signoff-target-id").setValue("FSUB-001");
    await wrapper.find("#signoff-reason").setValue("PI approval and sign-off.");

    // Submit sign-off
    await wrapper.find("#btn-pi-signoff").trigger("click");

    // Modal should be open
    expect(wrapper.find("#reauth-modal").exists()).toBe(true);
    expect(wrapper.find("#reauth-username").element.value).toBe(
      clinicalStore.user.username
    );
  });

  it("obtains a signature token and triggers batch sign-off successfully, then clears credentials", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Mock API success responses
    soaClient.verifySignature.mockResolvedValue({
      sig_token: "mock-jwt-sig-token",
    });
    soaClient.batchSignOff.mockResolvedValue({
      status: "success",
      approved_submission_ids: ["FSUB-001"],
      skipped_submission_ids: [],
    });

    // Open reauth modal
    await wrapper.find("#signoff-target-type").setValue("VISIT");
    await wrapper.find("#signoff-target-id").setValue("V-SCR");
    await wrapper.find("#btn-pi-signoff").trigger("click");

    // Enter credentials
    const passwordInput = wrapper.find("#reauth-password");
    await passwordInput.setValue("valid_password"); // pragma: allowlist secret

    // Mock window.alert to avoid prompt blocks in tests
    const alertMock = vi.spyOn(window, "alert").mockImplementation(() => {});

    // Confirm
    await wrapper.find("#btn-confirm-reauth").trigger("click");

    // Wait for async actions and DOM update
    await new Promise((resolve) => setTimeout(resolve, 10));
    await wrapper.vm.$nextTick();

    // Verify correct API invocation
    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      {
        username: "fderuiter",
        password: "valid_password", // pragma: allowlist secret
        totp: null,
        action: "/api/v1/execution/batch-sign-off",
      },
      authStore.accessToken
    );

    expect(soaClient.batchSignOff).toHaveBeenCalledWith(
      {
        studyId: "STUDY-USDM-001",
        targetType: "VISIT",
        targetIds: ["V-SCR"],
        signingReason: "PI approval and sign-off.",
      },
      {
        userId: "fderuiter",
        roles: "Monitor,Sponsor Admin",
        changeReason: "PI approval and sign-off.",
        sigToken: "mock-jwt-sig-token",
      },
      authStore.accessToken
    );

    // Ensure password is wiped from browser state
    expect(wrapper.vm.reauthPassword).toBe("");
    expect(wrapper.find("#reauth-modal").exists()).toBe(false);

    alertMock.mockRestore();
  });

  it("re-opens/maintains the re-authentication modal with an error message on 401 failure", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Mock API returning 401 failure
    soaClient.verifySignature.mockRejectedValue(
      new Error("REAUTHENTICATION_REQUIRED")
    );

    // Open reauth modal
    await wrapper.find("#signoff-target-type").setValue("SUBJECT");
    await wrapper.find("#signoff-target-id").setValue("SUBJ-001");
    await wrapper.find("#btn-pi-signoff").trigger("click");

    // Enter invalid credentials
    await wrapper.find("#reauth-password").setValue("invalid_password"); // pragma: allowlist secret

    // Confirm
    await wrapper.find("#btn-confirm-reauth").trigger("click");

    // Wait for async actions and DOM update
    await new Promise((resolve) => setTimeout(resolve, 10));
    await wrapper.vm.$nextTick();

    // Modal should still be open
    expect(wrapper.find("#reauth-modal").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Identity verification expired or invalid"
    );

    // Ensure password gets completely wiped
    expect(wrapper.vm.reauthPassword).toBe("");
  });
});
