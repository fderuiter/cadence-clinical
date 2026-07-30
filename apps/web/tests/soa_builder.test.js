import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";
import { soaClient } from "../src/api/soaClient.js";
import { useAuthStore } from "../src/stores/auth.js";
import { apiClient } from "../src/api/apiClient.js";
import { mount } from "@vue/test-utils";
import ClinicalSoAMatrix from "../src/components/clinical/ClinicalSoAMatrix.vue";

// Mock apiClient
vi.mock("../src/api/apiClient.js", () => {
  return {
    apiClient: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

beforeEach(() => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const authStore = useAuthStore();
  authStore.accessToken = "mock-keycloak-jwt-token";
  authStore.isAuthenticated = true;
  authStore.isDemoMode = false;
  if (typeof window !== "undefined" && window.localStorage) {
    window.localStorage.clear();
  }
  vi.resetAllMocks();
});

describe("SoA Matrix Pure Function Unit Tests", () => {
  it("renders a multi-level grouped header with Arms, Epochs, and Encounters with correct colspans", () => {
    const soaData = {
      arms: [
        { arm_id: "ARM-A", arm_name: "Active Arm" },
        { arm_id: "ARM-B", arm_name: "Placebo Arm" },
      ],
      epochs: [
        { epoch_id: "EP-A", epoch_name: "Treatment Phase A", arm_id: "ARM-A" },
        { epoch_id: "EP-B", epoch_name: "Treatment Phase B", arm_id: "ARM-B" },
      ],
      encounters: [
        { encounter_id: "E1", encounter_name: "Week 1", epoch_id: "EP-A" },
        { encounter_id: "E2", encounter_name: "Week 2", epoch_id: "EP-A" },
        { encounter_id: "E3", encounter_name: "Week 1", epoch_id: "EP-B" },
      ],
      rows: [
        {
          activity_id: "ACT1",
          activity_name: "Blood Draw",
          cells: [
            { encounter_id: "E1", is_applicable: true, details: "Mandatory" },
            { encounter_id: "E2", is_applicable: true, details: "Conditional" },
            { encounter_id: "E3", is_applicable: true, details: "Optional" },
          ],
        },
      ],
    };

    const wrapper = mount(ClinicalSoAMatrix, {
      props: { soaData },
    });
    const html = wrapper.html();

    expect(html).toContain('class="clinical-visit-matrix clinical-soa-matrix"');
    expect(html).toContain('colspan="2"');
    expect(html).toContain('class="grouped-header arm-header"');
    expect(html).toContain("Active Arm");
    expect(html).toContain("Placebo Arm");
    expect(html).toContain("Treatment Phase A");
    expect(html).toContain("Week 1");
    expect(html).toContain("Mandatory");
  });

  it("handles empty encounters list gracefully with an error banner", () => {
    const soaData = {
      epochs: [],
      encounters: [],
      rows: [],
    };
    const wrapper = mount(ClinicalSoAMatrix, {
      props: { soaData },
    });
    const html = wrapper.html();
    expect(html).toContain("No encounters defined for SoA matrix.");
  });
});

describe("SoA Request Construction & Serialization Unit Tests (Mocking apiClient)", () => {
  it("constructs GxP compliant requests using apiClient boundary", async () => {
    apiClient.post.mockResolvedValueOnce({ status: "success" });

    const options = {
      changeReason: "Testing signed headers",
    };

    await soaClient.saveArm(
      "STUDY-01",
      "v_draft_01",
      "ARM-Z",
      { name: "Arm Z" },
      options
    );

    expect(apiClient.post).toHaveBeenCalledTimes(1);
    const [path, body, opts] = apiClient.post.mock.calls[0];

    expect(path).toBe("/api/v1/studies/STUDY-01/versions/v_draft_01/arms");
    expect(body).toEqual({
      id: "ARM-Z",
      properties: { name: "Arm Z" },
    });
    expect(opts.changeReason).toBe("Testing signed headers");

    // Regression check: make sure no client-side trusted identity/signature headers are produced or supplied
    if (opts.headers) {
      expect(opts.headers["X-User-Id"]).toBeUndefined();
      expect(opts.headers["X-User-Roles"]).toBeUndefined();
      expect(opts.headers["X-Gateway-Signature"]).toBeUndefined();
      expect(opts.headers["X-Gateway-Timestamp"]).toBeUndefined();
      expect(opts.headers["X-Signature-Version"]).toBeUndefined();
    }
  });

  it("serializes nested mutations correctly for PUT requests", async () => {
    apiClient.put.mockResolvedValueOnce({ status: "success" });

    const options = {
      changeReason: "Testing PUT serialization",
      method: "PUT",
    };

    await soaClient.mutateEntity(
      "STUDY-01",
      "v_draft_01",
      "arms",
      "ARM-Z",
      { name: "Arm Z Modified" },
      options
    );

    expect(apiClient.put).toHaveBeenCalledTimes(1);
    const [path, body, opts] = apiClient.put.mock.calls[0];

    expect(path).toBe(
      "/api/v1/studies/STUDY-01/versions/v_draft_01/arms/ARM-Z"
    );
    expect(body).toEqual({
      properties: { name: "Arm Z Modified" },
    });
    expect(opts.changeReason).toBe("Testing PUT serialization");
  });
});

describe("SoA Builder Store Integration", () => {
  it("successfully passes mutation details with changeReason to apiClient", async () => {
    // mock first save
    apiClient.post.mockResolvedValueOnce({ status: "success", id: "ARM-A" });
    // mock projection fetch
    apiClient.get.mockResolvedValueOnce({
      epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1" }],
      encounters: [
        { encounter_id: "E1", encounter_name: "Visit 1", epoch_id: "EP-1" },
      ],
      rows: [{ activity_id: "ACT1", activity_name: "Vitals", cells: [] }],
    });

    const store = useClinicalStore();

    await store.pushSoAMutation(
      "arms",
      "ARM-A",
      { name: "Active Arm" },
      "Configure arm"
    );

    expect(apiClient.post).toHaveBeenCalledTimes(1);
    const [path, , opts] = apiClient.post.mock.calls[0];

    expect(path).toBe(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/arms"
    );
    expect(opts.changeReason).toBe("Configure arm");

    // Regression: make sure no other client side trusted headers are present in opts
    if (opts.headers) {
      expect(opts.headers["X-User-Id"]).toBeUndefined();
      expect(opts.headers["X-User-Roles"]).toBeUndefined();
      expect(opts.headers["X-Gateway-Signature"]).toBeUndefined();
      expect(opts.headers["X-Gateway-Timestamp"]).toBeUndefined();
      expect(opts.headers["X-Signature-Version"]).toBeUndefined();
    }
  });

  it("handles link creation API calls correctly with changeReason", async () => {
    apiClient.post.mockResolvedValueOnce({
      status: "success",
      message: "Link established",
    });
    apiClient.get.mockResolvedValueOnce({
      epochs: [],
      encounters: [],
      rows: [],
    });

    const store = useClinicalStore();

    const payload = {
      procedure_id: "ACT-VS",
      visit_id: "V-SCR",
      is_applicable: true,
    };
    await store.pushSoALink("visit-procedure", payload, "Link VS to Screening");

    expect(apiClient.post).toHaveBeenCalledTimes(1);
    const [path, body, opts] = apiClient.post.mock.calls[0];

    expect(path).toBe(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/links/visit-procedure"
    );
    expect(body).toEqual(payload);
    expect(opts.changeReason).toBe("Link VS to Screening");
  });

  it("handles locked version immutability failures gracefully", async () => {
    apiClient.post.mockRejectedValueOnce(
      new Error("IMMUTABILITY_VIOLATION: Locked Version cannot be modified")
    );

    const store = useClinicalStore();

    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-LOCKED",
        { name: "Locked Arm" },
        "Try modifying locked arm"
      );

    await expect(triggerMutation()).rejects.toThrow("IMMUTABILITY_VIOLATION");
    expect(store.soaError).toBe(
      "IMMUTABILITY_VIOLATION: Locked Version cannot be modified"
    );
  });
});
