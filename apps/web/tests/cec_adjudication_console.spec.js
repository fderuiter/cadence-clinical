import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CecAdjudicationConsole from "../src/components/clinical/CecAdjudicationConsole.vue";
import { apiClient } from "../src/api/apiClient";

vi.mock("../src/api/apiClient", () => {
  return {
    apiClient: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

describe("CecAdjudicationConsole - Chair Adjudication 21 CFR Part 11 Sign-Off", () => {
  let pinia;

  beforeEach(() => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("requires password re-authentication before submitting chair adjudication", async () => {
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});

    const wrapper = mount(CecAdjudicationConsole, {
      global: {
        plugins: [pinia],
      },
    });

    // Switch role to CHAIR and select a dossier
    wrapper.vm.currentRole = "CHAIR";
    wrapper.vm.activeMainTab = "tie_breaker";
    wrapper.vm.selectedDossierId = "DOSSIER-101";

    await wrapper.vm.$nextTick();

    // Set form fields without password
    wrapper.vm.chairForm.password = "";
    wrapper.vm.chairForm.final_classification = "ISCHEMIC_STROKE";

    await wrapper.vm.submitChairAdjudication();

    expect(alertSpy).toHaveBeenCalledWith(
      expect.stringContaining("re-authentication password")
    );
    expect(apiClient.post).not.toHaveBeenCalled();

    alertSpy.mockRestore();
  });

  it("calls /api/v1/auth/signature-verification for X-Sig-Token and submits chair adjudication with X-Sig-Token header", async () => {
    apiClient.post.mockImplementation((url, payload, options) => {
      if (url === "/api/v1/auth/signature-verification") {
        return Promise.resolve({
          sig_token: "test-sig-token-12345",
          expires_in: 60,
        });
      }
      if (url.includes("/chair-adjudicate")) {
        return Promise.resolve({
          determination_id: "DET-98765",
          dossier_id: "DOSSIER-MACE-001",
          chair_id: "dr_chair",
          chair_name: "Dr. Adjudication Chair",
          final_classification: "ISCHEMIC_STROKE",
          final_subtype: "THROMBOTIC",
          final_adjudicated_onset_date: "2026-03-15",
          binding_reader: "CHAIR_CUSTOM_DETERMINATION",
          criteria_confirmed: ["CT_SCAN_CONFIRMED"],
          adjudication_rationale:
            "Definitive CT imaging confirms acute thrombotic infarction.",
          electronic_signature: {
            signer_id: "dr_chair",
            signer_name: "Dr. Adjudication Chair",
            signer_role: "CEC_CHAIR",
            signing_reason:
              "I confirm this clinical endpoint adjudication determination is final",
            payload_hash: "abcd1234efgh5678",
            signed_at: "2026-09-30T10:00:00Z",
          },
          finalized_at: "2026-09-30T10:00:00Z",
        });
      }
      return Promise.resolve({});
    });

    const wrapper = mount(CecAdjudicationConsole, {
      global: {
        plugins: [pinia],
      },
    });

    wrapper.vm.currentRole = "CHAIR";
    wrapper.vm.activeMainTab = "tie_breaker";

    await wrapper.vm.$nextTick();

    wrapper.vm.chairForm.signer_name = "Dr. Adjudication Chair";
    wrapper.vm.chairForm.password = "SignOffPassword123!";
    wrapper.vm.chairForm.final_classification = "ISCHEMIC_STROKE";
    wrapper.vm.chairForm.final_subtype = "THROMBOTIC";
    wrapper.vm.chairForm.final_adjudicated_onset_date = "2026-03-15";
    wrapper.vm.chairForm.adjudication_rationale =
      "Definitive CT imaging confirms acute thrombotic infarction.";

    await wrapper.vm.submitChairAdjudication();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/auth/signature-verification",
      expect.objectContaining({
        username: "Dr. Adjudication Chair",
        password: "SignOffPassword123!",
        action:
          "/api/v1/execution/cec/dossiers/DOSSIER-MACE-001/chair-adjudicate",
        semantic_action: "execution.cec.chair_adjudicate",
      })
    );

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/cec/dossiers/DOSSIER-MACE-001/chair-adjudicate",
      expect.objectContaining({
        final_classification: "ISCHEMIC_STROKE",
        signature_token: "test-sig-token-12345",
      }),
      {
        headers: {
          "X-Sig-Token": "test-sig-token-12345",
        },
      }
    );

    expect(wrapper.vm.activeDossier.workflow_status).toBe("FINALIZED_BY_CHAIR");
    expect(wrapper.vm.activeDossier.is_discordant).toBe(false);
    expect(wrapper.vm.activeDossier.final_consensus_classification).toBe(
      "ISCHEMIC_STROKE"
    );
  });

  it("handles HTTP 401 / REAUTHENTICATION_REQUIRED error during signature verification or submission", async () => {
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});

    apiClient.post.mockRejectedValue({
      status: 401,
      data: {
        code: "REAUTHENTICATION_REQUIRED",
        message:
          "Step-up electronic signature re-authentication token missing or invalid",
      },
    });

    const wrapper = mount(CecAdjudicationConsole, {
      global: {
        plugins: [pinia],
      },
    });

    wrapper.vm.currentRole = "CHAIR";
    wrapper.vm.selectedDossierId = "DOSSIER-101";
    wrapper.vm.activeMainTab = "tie_breaker";

    await wrapper.vm.$nextTick();

    wrapper.vm.chairForm.signer_name = "Dr. Adjudication Chair";
    wrapper.vm.chairForm.password = "WrongPassword";

    await wrapper.vm.submitChairAdjudication();

    expect(alertSpy).toHaveBeenCalledWith(
      expect.stringContaining("re-authentication failed")
    );

    alertSpy.mockRestore();
  });
});
