import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";
import { soaClient } from "../src/api/soaClient.js";
import { mount } from "@vue/test-utils";
import ClinicalSoAMatrix from "../src/components/clinical/ClinicalSoAMatrix.vue";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

beforeEach(() => {
  const pinia = createPinia();
  setActivePinia(pinia);
  if (typeof window !== "undefined" && window.localStorage) {
    window.localStorage.clear();
  }
  mockFetch.mockReset();
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

    // Verify structured Schedule of Activities table
    expect(html).toContain('class="clinical-visit-matrix clinical-soa-matrix"');

    // Verify Arm groupings and colspans
    expect(html).toContain('colspan="2"');
    expect(html).toContain('class="grouped-header arm-header"');
    expect(html).toContain("Active Arm");
    expect(html).toContain("Placebo Arm");

    // Verify Epoch groupings and colspans
    expect(html).toContain('class="grouped-header epoch-header"');
    expect(html).toContain("Treatment Phase A");
    expect(html).toContain("Treatment Phase B");

    // Verify encounter name headers
    expect(html).toContain('class="encounter-header"');
    expect(html).toContain("Week 1");
    expect(html).toContain("Week 2");

    // Verify cell conditional/optional/applicable styles
    expect(html).toContain('class="status-applicable"');
    expect(html).toContain('class="status-conditional"');
    expect(html).toContain('class="status-optional"');
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

  it("omits Arm header row entirely when no arms are specified (common encounters)", () => {
    const soaData = {
      epochs: [{ epoch_id: "EP-COMMON", epoch_name: "Common Epoch" }],
      encounters: [
        {
          encounter_id: "E1",
          encounter_name: "Screening",
          epoch_id: "EP-COMMON",
        },
      ],
      rows: [
        {
          activity_id: "ACT1",
          activity_name: "Informed Consent",
          cells: [{ encounter_id: "E1", is_applicable: true }],
        },
      ],
    };

    const wrapper = mount(ClinicalSoAMatrix, {
      props: { soaData },
    });
    const html = wrapper.html();
    expect(html).not.toContain("grouped-header arm-header");
    expect(html).toContain('colspan="1"');
    expect(html).toContain("Common Epoch");
  });

  it("handles missing/invalid SoA matrix data", () => {
    const wrapperNull = mount(ClinicalSoAMatrix, {
      props: { soaData: null },
    });
    expect(wrapperNull.html()).toContain("Invalid SoA matrix data.");

    const wrapperNoRows = mount(ClinicalSoAMatrix, {
      props: { soaData: {} },
    });
    expect(wrapperNoRows.html()).toContain("Invalid SoA matrix data.");
  });
});

describe("SoA Request Construction & Serialization Unit Tests", () => {
  it("constructs GxP compliant signed requests with expected header contract", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success" }),
    });

    const options = {
      userId: "test-user",
      roles: "sponsor_admin",
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
    const [url, requestOpts] = mockFetch.mock.calls[0];

    expect(url).toContain("/api/v1/studies/STUDY-01/versions/v_draft_01/arms");
    expect(requestOpts.method).toBe("POST");
    expect(requestOpts.headers["X-User-Id"]).toBe("test-user");
    expect(requestOpts.headers["X-User-Roles"]).toBe("sponsor_admin");
    expect(requestOpts.headers["X-Signature-Version"]).toBe("2");
    expect(requestOpts.headers["X-Gateway-Signature"]).toBeDefined();
    expect(requestOpts.headers["X-Gateway-Timestamp"]).toBeDefined();
    expect(requestOpts.headers["X-Change-Reason"]).toBe(
      "Testing signed headers"
    );

    const body = JSON.parse(requestOpts.body);
    expect(body).toEqual({
      id: "ARM-Z",
      properties: { name: "Arm Z" },
    });
  });

  it("serializes nested mutations correctly for PUT requests", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success" }),
    });

    const options = {
      userId: "test-user",
      roles: "sponsor_admin",
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

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, requestOpts] = mockFetch.mock.calls[0];

    expect(url).toContain(
      "/api/v1/studies/STUDY-01/versions/v_draft_01/arms/ARM-Z"
    );
    expect(requestOpts.method).toBe("PUT");
    const body = JSON.parse(requestOpts.body);
    expect(body).toEqual({
      properties: { name: "Arm Z Modified" },
    });
  });
});

describe("SoA Builder Signed API Client & Store Integration", () => {
  it("generates correct GxP version 2 signed headers for mutations", async () => {
    // 1st fetch: Arm Mutation save
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success", id: "ARM-A" }),
    });
    // 2nd fetch: fetchSoAProjection load
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1" }],
        encounters: [
          { encounter_id: "E1", encounter_name: "Visit 1", epoch_id: "EP-1" },
        ],
        rows: [{ activity_id: "ACT1", activity_name: "Vitals", cells: [] }],
      }),
    });

    const store = useClinicalStore();

    await store.pushSoAMutation(
      "arms",
      "ARM-A",
      { name: "Active Arm" },
      "Configure arm"
    );

    expect(mockFetch).toHaveBeenCalledTimes(2);
    const [url, options] = mockFetch.mock.calls[0];

    expect(url).toContain(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/arms"
    );
    expect(options.method).toBe("POST");
    expect(options.headers["X-User-Id"]).toBe("fderuiter");
    expect(options.headers["X-User-Roles"]).toBe("STUDY_DESIGNER");
    expect(options.headers["X-Signature-Version"]).toBe("2");
    expect(options.headers["X-Gateway-Signature"]).toBeDefined();
    expect(options.headers["X-Change-Reason"]).toBe("Configure arm");

    // Verify local USDM state updated via fetchSoAProjection trigger
    expect(store.currentUsdm.epochs).toHaveLength(1);
    expect(store.currentUsdm.encounters).toHaveLength(1);
    expect(store.currentUsdm.rows).toHaveLength(1);
  });

  it("handles link creation API calls correctly with signed headers", async () => {
    // 1st fetch: Create Link
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success", message: "Link established" }),
    });
    // 2nd fetch: fetchSoAProjection load
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ epochs: [], encounters: [], rows: [] }),
    });

    const store = useClinicalStore();

    const payload = {
      procedure_id: "ACT-VS",
      visit_id: "V-SCR",
      is_applicable: true,
    };
    await store.pushSoALink("visit-procedure", payload, "Link VS to Screening");

    expect(mockFetch).toHaveBeenCalledTimes(2);
    const [url, options] = mockFetch.mock.calls[0];

    expect(url).toContain(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/links/visit-procedure"
    );
    expect(options.method).toBe("POST");
    expect(JSON.parse(options.body)).toEqual(payload);
    expect(options.headers["X-Change-Reason"]).toBe("Link VS to Screening");
  });

  it("gracefully falls back to local browser state and records offline blocks on sync failure", async () => {
    mockFetch.mockRejectedValueOnce(new Error("Network unserviceable"));

    const store = useClinicalStore();

    // Trigger store push action
    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-C",
        { name: "High Dose Arm" },
        "Fork protocol for high dose cohort"
      );

    await expect(triggerMutation()).rejects.toThrow("Network unserviceable");

    // Local ledger still retains offline mutation log
    const ledger = store.ledgerBlocks;
    expect(ledger).toHaveLength(1);
    expect(ledger[0].action).toBe("SOA_MUTATION_OFFLINE_ARMS");
    expect(ledger[0].reason).toBe("Fork protocol for high dose cohort");
    expect(ledger[0].details.id).toBe("ARM-C");
    expect(ledger[0].details.error).toBe("Network unserviceable");
  });
});

describe("SoA Authoring Failure & Immutability Guards", () => {
  it("handles locked version immutability failure (HTTP 403 / IMMUTABILITY_VIOLATION)", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 403,
      json: async () => ({
        detail: "IMMUTABILITY_VIOLATION: Locked Version cannot be modified",
      }),
    });

    const store = useClinicalStore();

    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-LOCKED",
        { name: "Locked Arm" },
        "Try modifying locked arm"
      );

    await expect(triggerMutation()).rejects.toThrow("IMMUTABILITY_VIOLATION");

    // The store state captures the exception as the error state
    expect(store.soaError).toBe(
      "IMMUTABILITY_VIOLATION: Locked Version cannot be modified"
    );

    // Ledgers capture the offline/failed attempt as well
    const ledger = store.ledgerBlocks;
    expect(ledger).toHaveLength(1);
    expect(ledger[0].action).toBe("SOA_MUTATION_OFFLINE_ARMS");
    expect(ledger[0].details.error).toContain("IMMUTABILITY_VIOLATION");
  });

  it("handles invalid input request failure (HTTP 400 Validation Failures)", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 400,
      json: async () => ({
        detail: "Missing mandatory fields properties.name",
      }),
    });

    const store = useClinicalStore();

    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-INVALID",
        {},
        "Invalid empty arm properties"
      );

    await expect(triggerMutation()).rejects.toThrow("Missing mandatory fields");
    expect(store.soaError).toBe("Missing mandatory fields properties.name");
  });

  it("handles missing/invalid gateway signatures failure (HTTP 401)", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      json: async () => ({ detail: "INVALID_OR_MISSING_SIGNATURE" }),
    });

    const store = useClinicalStore();

    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-M",
        { name: "Arm M" },
        "Trigger signature verification failure"
      );

    await expect(triggerMutation()).rejects.toThrow(
      "INVALID_OR_MISSING_SIGNATURE"
    );
    expect(store.soaError).toBe("INVALID_OR_MISSING_SIGNATURE");
  });

  it("handles batch sign-off failures and re-authentication requirements", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      json: async () => ({ detail: "REAUTHENTICATION_REQUIRED" }),
    });

    const triggerBatch = () =>
      soaClient.batchSignOff(
        {
          studyId: "STUDY-01",
          targetType: "FORM",
          targetIds: ["FORM-1"],
          signingReason: "PI Sign-off",
        },
        {
          userId: "pi_user",
          roles: "PRINCIPAL_INVESTIGATOR",
          changeReason: "Sign-off",
          sigToken: "expired-or-invalid-token",
        }
      );

    await expect(triggerBatch()).rejects.toThrow("REAUTHENTICATION_REQUIRED");
  });
});
