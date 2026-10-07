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
    // Uses the demo identity and roles (demo builds only, #4523).
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    clinicalStore = useClinicalStore();
    authStore = useAuthStore();
    authStore.setAuth({
      authenticated: true,
      token: "pi-access-token",
      tokenParsed: {
        sub: "pi-user-id",
        preferred_username: "pi_user",
        realm_access: { roles: ["Monitor", "Sponsor Admin"] },
      },
    });

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
    await new Promise((resolve) => setTimeout(resolve, 100));
    await wrapper.vm.$nextTick();

    // Verify correct API invocation
    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      {
        username: "pi_user",
        password: "valid_password", // pragma: allowlist secret
        totp: null,
        action: "/api/v1/execution/batch-sign-off",
        batchId: expect.any(String),
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
        userId: "pi_user",
        roles: "monitor,sponsor_admin",
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
    await wrapper.find("#signoff-target-type").setValue("FORM");
    await wrapper.find("#signoff-target-id").setValue("FSUB-001");
    await wrapper.find("#btn-pi-signoff").trigger("click");

    // Enter invalid credentials
    await wrapper.find("#reauth-password").setValue("invalid_password"); // pragma: allowlist secret

    // Confirm
    await wrapper.find("#btn-confirm-reauth").trigger("click");

    // Wait for async actions and DOM update
    await new Promise((resolve) => setTimeout(resolve, 100));
    await wrapper.vm.$nextTick();

    // Modal should still be open
    expect(wrapper.find("#reauth-modal").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Identity verification expired or invalid"
    );

    // Ensure password gets completely wiped
    expect(wrapper.vm.reauthPassword).toBe("");
  });

  it("renders multi-subject worklist table and aggregate metrics when scope is SUBJECT", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#signoff-target-type").setValue("SUBJECT");

    expect(wrapper.find("#pi-multi-subject-table").exists()).toBe(true);
    expect(wrapper.find("#select-all-eligible").exists()).toBe(true);
    expect(wrapper.find(".subject-checkbox").exists()).toBe(true);
    expect(wrapper.find(".batch-metrics-card").exists()).toBe(true);
    expect(
      Number(wrapper.find("#metric-selected-subjects").text())
    ).toBeGreaterThan(0);
    expect(wrapper.find("#btn-pi-signoff").text()).toContain(
      "Sign Off Selected Subjects"
    );
  });

  it("toggles all subject row checkboxes using the #select-all-eligible header checkbox", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#signoff-target-type").setValue("SUBJECT");
    const headerCheckbox = wrapper.find("#select-all-eligible");
    const initialSubjects = [...wrapper.vm.selectedSubjectIds];

    // Uncheck select-all
    await headerCheckbox.setChecked(false);
    expect(wrapper.vm.selectedSubjectIds).toEqual([]);
    expect(wrapper.find("#metric-selected-subjects").text()).toBe("0");

    // Re-check select-all
    await headerCheckbox.setChecked(true);
    expect(wrapper.vm.selectedSubjectIds).toEqual(initialSubjects);
    expect(Number(wrapper.find("#metric-selected-subjects").text())).toBe(
      initialSubjects.length
    );
  });

  it("executes multi-subject batch sign-off, purges credentials, and displays signature certification card", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    soaClient.verifySignature.mockResolvedValue({
      sig_token: "mock-multi-subject-sig-token",
    });
    soaClient.batchSignOff.mockResolvedValue({
      status: "SUCCESS",
      approved_submission_ids: ["SUBJ-001:FORM", "SUBJ-002:FORM"],
      skipped_submission_ids: [],
      signature_id: "CERT-MULTI-SUBJ-12345",
      timestamp_utc: "2026-10-06T12:00:00Z",
      content_digest:
        "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
      audit_tx: "TX-AUDIT-MULTI-SUBJ-8888",
    });

    await wrapper.find("#signoff-target-type").setValue("SUBJECT");
    const selectedSubjects = [...wrapper.vm.selectedSubjectIds];
    await wrapper.find("#btn-pi-signoff").trigger("click");

    expect(wrapper.find("#reauth-modal").exists()).toBe(true);
    await wrapper.find("#reauth-password").setValue("pi_secret_pass"); // pragma: allowlist secret

    const alertMock = vi.spyOn(window, "alert").mockImplementation(() => {});

    await wrapper.find("#btn-confirm-reauth").trigger("click");

    await new Promise((resolve) => setTimeout(resolve, 100));
    await wrapper.vm.$nextTick();

    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      {
        username: "pi_user",
        password: "pi_secret_pass", // pragma: allowlist secret
        totp: null,
        action: "/api/v1/execution/batch-sign-off",
        batchId: expect.any(String),
      },
      authStore.accessToken
    );

    expect(soaClient.batchSignOff).toHaveBeenCalledWith(
      {
        studyId: "STUDY-USDM-001",
        targetType: "SUBJECT",
        targetIds: selectedSubjects,
        signingReason: "PI approval and sign-off.",
      },
      {
        userId: "pi_user",
        roles: "monitor,sponsor_admin",
        changeReason: "PI approval and sign-off.",
        sigToken: "mock-multi-subject-sig-token",
      },
      authStore.accessToken
    );

    // Verify credentials wiped from memory immediately
    expect(wrapper.vm.reauthPassword).toBe("");

    // Verify Signature Certification Summary Card rendered
    expect(wrapper.find("#signature-cert-card").exists()).toBe(true);
    expect(wrapper.find("#cert-serial-id").text()).toBe(
      "CERT-MULTI-SUBJ-12345"
    );
    expect(wrapper.find("#cert-timestamp").text()).toBe("2026-10-06T12:00:00Z");
    expect(wrapper.find("#cert-content-digest").text()).toBe(
      "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"
    );
    expect(wrapper.find("#cert-audit-tx").text()).toBe(
      "TX-AUDIT-MULTI-SUBJ-8888"
    );

    alertMock.mockRestore();
  });
});
