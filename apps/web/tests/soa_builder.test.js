import { describe, it, expect, beforeEach, vi } from "vitest";

// soaClient is built on openapi-fetch (src/api/client.ts), which calls the global fetch with a single
// Request object. Stub fetch before any module import so no test can reach the real network.
const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { soaClient } from "../src/api/soaClient.js";
import { useAuthStore } from "../src/stores/auth.js";
import { mount } from "@vue/test-utils";
import ClinicalSoAMatrix from "../src/components/clinical/ClinicalSoAMatrix.vue";
import { normalizeUsdm } from "../src/stores/normalization";
import { stateTrackingPlugin } from "../src/stores/plugins.js";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

const TRUSTED_IDENTITY_HEADERS = [
  "X-User-Id",
  "X-User-Roles",
  "X-Gateway-Signature",
  "X-Gateway-Timestamp",
  "X-Signature-Version",
];

/** Asserts the client did not forge any gateway-trusted identity or signature headers. */
function expectNoClientSideTrustedHeaders(headers) {
  for (const name of TRUSTED_IDENTITY_HEADERS) {
    expect(headers.get(name)).toBeNull();
  }
}

/** Routes mocked fetch calls: SoA projection GETs get `projection`, everything else `mutationBody`. */
function routeFetch(mutationBody, projection) {
  mockFetch.mockImplementation(async (input) => {
    const request = input instanceof Request ? input : new Request(input);
    if (
      request.method === "GET" &&
      new URL(request.url).pathname.endsWith("/soa-projection")
    ) {
      return jsonResponse(projection);
    }
    return jsonResponse(mutationBody);
  });
}

beforeEach(() => {
  const pinia = createPinia();
  pinia.use(stateTrackingPlugin);
  setActivePinia(pinia);
  const authStore = useAuthStore();
  authStore.accessToken = "mock-keycloak-jwt-token";
  authStore.isAuthenticated = true;
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

describe("SoA Request Construction & Serialization Unit Tests (Mocking fetch)", () => {
  it("constructs GxP compliant requests through the typed API client", async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ status: "success" }));

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

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const { url, method, headers, body } = await recordedRequest(mockFetch, 0);

    expect(method).toBe("POST");
    expect(new URL(url).pathname).toBe(
      "/api/v1/studies/STUDY-01/versions/v_draft_01/arms"
    );
    expect(body).toEqual({
      id: "ARM-Z",
      properties: { name: "Arm Z" },
    });
    expect(headers.get("X-Change-Reason")).toBe("Testing signed headers");
    expect(headers.get("Authorization")).toBe("Bearer mock-keycloak-jwt-token");

    // Regression check: make sure no client-side trusted identity/signature headers are produced or supplied
    expectNoClientSideTrustedHeaders(headers);
  });

  it("serializes nested mutations correctly for PUT requests", async () => {
    // The generic soaClient.mutateEntity(path-string) helper was replaced by typed per-entity methods
    // (saveArm/saveEpoch/saveVisit/saveProcedure) when the client moved to openapi-fetch; an update is
    // now saveArm(..., { method: "PUT" }).
    mockFetch.mockResolvedValueOnce(jsonResponse({ status: "success" }));

    const options = {
      changeReason: "Testing PUT serialization",
      method: "PUT",
    };

    await soaClient.saveArm(
      "STUDY-01",
      "v_draft_01",
      "ARM-Z",
      { name: "Arm Z Modified" },
      options
    );

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const { url, method, headers, body } = await recordedRequest(mockFetch, 0);

    expect(method).toBe("PUT");
    expect(new URL(url).pathname).toBe(
      "/api/v1/studies/STUDY-01/versions/v_draft_01/arms/ARM-Z"
    );
    expect(body).toEqual({
      properties: { name: "Arm Z Modified" },
    });
    expect(headers.get("X-Change-Reason")).toBe("Testing PUT serialization");
  });
});

describe("SoA Builder Store Integration", () => {
  it("successfully passes mutation details with changeReason to the API", async () => {
    routeFetch(
      { status: "success", id: "ARM-A" },
      {
        epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1" }],
        encounters: [
          { encounter_id: "E1", encounter_name: "Visit 1", epoch_id: "EP-1" },
        ],
        rows: [{ activity_id: "ACT1", activity_name: "Vitals", cells: [] }],
      }
    );

    const store = useClinicalStore();

    await store.pushSoAMutation(
      "arms",
      "ARM-A",
      { name: "Active Arm" },
      "Configure arm"
    );

    // One mutation POST followed by the SoA projection refresh GET
    expect(mockFetch).toHaveBeenCalledTimes(2);
    const mutation = await recordedRequest(mockFetch, 0);

    expect(mutation.method).toBe("POST");
    expect(new URL(mutation.url).pathname).toBe(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/arms"
    );
    expect(mutation.headers.get("X-Change-Reason")).toBe("Configure arm");

    // Regression: make sure no other client side trusted headers are present on the mutation
    expectNoClientSideTrustedHeaders(mutation.headers);

    const projection = await recordedRequest(mockFetch, 1);
    expect(projection.method).toBe("GET");
    expect(new URL(projection.url).pathname).toBe(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/soa-projection"
    );
    expect(store.currentUsdm.encounters[0].encounter_id).toBe("E1");
  });

  it("handles link creation API calls correctly with changeReason", async () => {
    routeFetch(
      { status: "success", message: "Link established" },
      { epochs: [], encounters: [], rows: [] }
    );

    const store = useClinicalStore();

    const payload = {
      procedure_id: "ACT-VS",
      visit_id: "V-SCR",
      is_applicable: true,
    };
    await store.pushSoALink("visit-procedure", payload, "Link VS to Screening");

    expect(mockFetch).toHaveBeenCalledTimes(2);
    const { url, method, headers, body } = await recordedRequest(mockFetch, 0);

    expect(method).toBe("POST");
    expect(new URL(url).pathname).toBe(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/links/visit-procedure"
    );
    expect(body).toEqual(payload);
    expect(headers.get("X-Change-Reason")).toBe("Link VS to Screening");
  });

  it("handles locked version immutability failures gracefully", async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse(
        {
          detail: "IMMUTABILITY_VIOLATION: Locked Version cannot be modified",
        },
        { status: 409 }
      )
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
    // The failed mutation must not trigger a projection refresh
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});

describe("SoA Normalization & Missing/Null Optional Fields Tests", () => {
  it("normalizes malformed/null USDM fields into safe default typed structures", () => {
    const rawData = {
      studyId: null,
      epochs: null,
      encounters: [{ encounter_id: "E1" }],
      rows: [{ activity_id: "A1", cells: null }],
    };

    const normalized = normalizeUsdm(rawData);

    expect(normalized.studyId).toBe("STUDY-USDM-001");
    expect(normalized.epochs).toEqual([]);
    expect(normalized.encounters[0]).toEqual({
      encounter_id: "E1",
      encounter_name: "",
      epoch_id: "",
      sequence: 0,
      arm_id: null,
    });
    expect(normalized.rows[0].activity_id).toBe("A1");
    expect(normalized.rows[0].cells).toEqual([]);
  });

  it("ClinicalSoAMatrix component renders empty formatted cells instead of crashing or showing Invalid SoA error when optional fields are null", () => {
    const malformedSoaData = {
      epochs: [],
      encounters: [{ encounter_id: "E1", encounter_name: "Visit 1" }],
      rows: [{ activity_id: "ACT1", activity_name: "Vitals", cells: null }],
    };

    const wrapper = mount(ClinicalSoAMatrix, {
      props: { soaData: malformedSoaData },
    });

    const html = wrapper.html();
    expect(html).not.toContain("Invalid SoA matrix data.");
    expect(html).toContain('class="clinical-visit-matrix clinical-soa-matrix"');
    expect(html).toContain("Vitals");
    expect(html).toContain("Visit 1");
    expect(html).toContain("-"); // Renders empty formatted cell
  });
});
