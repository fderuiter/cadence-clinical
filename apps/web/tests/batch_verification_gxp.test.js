import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useRouter } from "vue-router";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth";
import { useNotificationsStore } from "../src/stores/notifications";
import { soaClient } from "../src/api/soaClient";
import EcrfView from "../src/views/EcrfView.vue";

// Mock the router hooks
vi.mock("vue-router", () => {
  const push = vi.fn();
  return {
    useRouter: () => ({
      push,
    }),
    useRoute: () => ({
      query: {
        studyId: "TEST-STUDY",
        siteId: "TEST-SITE",
        subjectId: "SUBJ-002",
        visitId: "Week2",
      },
    }),
  };
});

// Mock the soaClient backend API calls
vi.mock("../src/api/soaClient", () => {
  return {
    soaClient: {
      verifySignature: vi.fn(),
      batchSignOff: vi.fn(),
    },
  };
});

describe("Batch Source Data Verification and GxP Compliance Flow Tests", () => {
  let pinia;
  let clinicalStore;
  let authStore;
  let notificationsStore;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    clinicalStore = useClinicalStore();
    authStore = useAuthStore();
    notificationsStore = useNotificationsStore();

    // Reset router push spy
    useRouter().push.mockClear();

    // Setup Auth Store values
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.user = {
      username: "cra_tester",
      email: "cra_tester@example.com",
    };
    authStore.rawRoles = ["cra"];

    // Setup mocks
    vi.mocked(soaClient.verifySignature).mockReset();
    vi.mocked(soaClient.batchSignOff).mockReset();
  });

  it("successfully parses route query parameters and preserves study/site context on land", () => {
    mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Check store synchronization on mount
    expect(clinicalStore.activeStudyId).toBe("TEST-STUDY");
    expect(clinicalStore.activeSiteId).toBe("TEST-SITE");
    expect(clinicalStore.activeSubjectId).toBe("SUBJ-002");
    expect(clinicalStore.activeVisitId).toBe("Week2");
  });

  it("renders batch SDV selection checkboxes for GxP fields when isAuthorizedForBulkSdv is true", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Verify CRA/authorized role is active
    expect(wrapper.vm.isAuthorizedForBulkSdv).toBe(true);

    // Verify batch selection checkboxes are present
    const batchCheckboxes = wrapper.findAll(".batch-sdv-checkbox");
    expect(batchCheckboxes.length).toBeGreaterThan(0);
  });

  it("triggers dual-factor electronic sign-off and batch SDV successfully when verified within the 60-second window", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Mock successful 21 CFR Part 11 token generation & transaction signoff
    vi.mocked(soaClient.verifySignature).mockResolvedValue({
      sig_token: "valid-gxp-token",
    });
    vi.mocked(soaClient.batchSignOff).mockResolvedValue({
      status: "SUCCESS",
      signature_id: "sig-9999",
      timestamp_utc: "2026-08-03T10:00:00Z",
    });

    // Select vital signs fields for verification
    wrapper.vm.selectedBatchFields = ["vssbp", "vsdpb"];

    // Initiate verification (Opens Re-auth Modal)
    wrapper.vm.initiateBatchVerify();
    expect(wrapper.vm.showReauthModal).toBe(true);
    expect(wrapper.vm.reauthAction).toBe("BULK_SDV");

    // Execute Signature
    wrapper.vm.reauthPassword = "CorrectPassword123"; // pragma: allowlist secret
    await wrapper.vm.confirmReauth();

    // Expect verifySignature and batchSignOff to have been called
    expect(soaClient.verifySignature).toHaveBeenCalled();
    expect(soaClient.batchSignOff).toHaveBeenCalled();

    // Selection must clear on success, and modal closes
    expect(wrapper.vm.selectedBatchFields.length).toBe(0);
    expect(wrapper.vm.showReauthModal).toBe(false);

    // Local SDV status should be verified
    expect(wrapper.vm.sdvStates[wrapper.vm.getSdvKey("vssbp")]).toBe(true);
    expect(wrapper.vm.sdvStates[wrapper.vm.getSdvKey("vsdpb")]).toBe(true);
  });

  it("explicitly rejects bulk-signing transactions and applies compliance lockout if the token age exceeds 60 seconds", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Mock successful 21 CFR Part 11 token generation
    vi.mocked(soaClient.verifySignature).mockResolvedValue({
      sig_token: "expired-gxp-token",
    });

    // Select vital signs fields for verification
    wrapper.vm.selectedBatchFields = ["vssbp"];

    // Initiate verification (Opens Re-auth Modal)
    wrapper.vm.initiateBatchVerify();

    // Set password and simulate compliance 65-second lockout delay
    wrapper.vm.reauthPassword = "CorrectPassword123"; // pragma: allowlist secret
    wrapper.vm.simulateDelay = true;

    // Confirm (Exposing token timeout rejection)
    await wrapper.vm.confirmReauth();

    // Verify error was rendered and transaction was blocked (batchSignOff not called)
    expect(soaClient.batchSignOff).not.toHaveBeenCalled();
    expect(wrapper.vm.reauthError).toContain("Compliance Lockout");
    expect(wrapper.vm.showReauthModal).toBe(true); // modal kept open
  });

  it("automatically clears verification status and dispatches an high-priority alert notification when a verified field is modified", async () => {
    const wrapper = mount(EcrfView, {
      global: {
        plugins: [pinia],
      },
    });

    // Pre-mark field as verified
    const sKey = wrapper.vm.getSdvKey("vssbp");
    wrapper.vm.sdvStates[sKey] = true;

    // Ensure notifications store is clean
    notificationsStore.notifications = [];

    // Trigger value change to commit a modification
    wrapper.vm.commitChange(
      { id: "vssbp", label: "Systolic Blood Pressure" },
      "120",
      "135",
      "Correction of typographical error"
    );

    // Status should be cleared
    expect(wrapper.vm.sdvStates[sKey]).toBe(false);

    // Verify a high priority ALERTS notification has been pushed to the workspace inbox
    const activeAlerts = notificationsStore.notifications.filter(
      (n) => n.category === "ALERTS" && n.priority === "HIGH"
    );
    expect(activeAlerts.length).toBe(1);
    expect(activeAlerts[0].message_content).toContain(
      "Verification cleared automatically"
    );
    expect(activeAlerts[0].related_entity_id).toBe("vssbp");
  });
});
