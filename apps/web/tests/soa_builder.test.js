import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";
import { soaClient } from "../src/api/soaClient.js";
import { createClinicalSoAMatrix } from "ui";

beforeEach(() => {
  const pinia = createPinia();
  setActivePinia(pinia);
  if (typeof window !== "undefined" && window.localStorage) {
    window.localStorage.clear();
  }
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

    const html = createClinicalSoAMatrix(soaData);

    // Verify structured Schedule of Activities table
    expect(html).toContain('class="clinical-visit-matrix clinical-soa-matrix"');

    // Verify Arm groupings and colspans
    expect(html).toContain(
      '<th scope="col" colspan="2" class="grouped-header arm-header">Active Arm</th>'
    );
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header arm-header">Placebo Arm</th>'
    );

    // Verify Epoch groupings and colspans
    expect(html).toContain(
      '<th scope="col" colspan="2" class="grouped-header epoch-header">Treatment Phase A</th>'
    );
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header epoch-header">Treatment Phase B</th>'
    );

    // Verify encounter name headers
    expect(html).toContain(
      '<th scope="col" class="encounter-header">Week 1</th>'
    );
    expect(html).toContain(
      '<th scope="col" class="encounter-header">Week 2</th>'
    );

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
    const html = createClinicalSoAMatrix(soaData);
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

    const html = createClinicalSoAMatrix(soaData);
    expect(html).not.toContain("grouped-header arm-header");
    expect(html).toContain(
      '<th scope="col" colspan="1" class="grouped-header epoch-header">Common Epoch</th>'
    );
  });

  it("handles missing/invalid SoA matrix data", () => {
    const htmlNull = createClinicalSoAMatrix(null);
    expect(htmlNull).toContain("Invalid SoA matrix data.");

    const htmlNoRows = createClinicalSoAMatrix({});
    expect(htmlNoRows).toContain("Invalid SoA matrix data.");
  });
});

describe("SoA Request Construction & Serialization Unit Tests", () => {
  it("constructs GxP compliant signed requests with expected header contract", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    const saveArmSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({ status: "success" })
    );
    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", saveArmSpy)
    );

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

    expect(saveArmSpy).toHaveBeenCalledTimes(1);
    const { request } = saveArmSpy.mock.calls[0][0];

    expect(request.url).toContain("/api/v1/studies/STUDY-01/versions/v_draft_01/arms");
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-User-Id")).toBe("test-user");
    expect(request.headers.get("X-User-Roles")).toBe("sponsor_admin");
    expect(request.headers.get("X-Signature-Version")).toBe("2");
    expect(request.headers.get("X-Gateway-Signature")).toBeDefined();
    expect(request.headers.get("X-Gateway-Timestamp")).toBeDefined();
    expect(request.headers.get("X-Change-Reason")).toBe(
      "Testing signed headers"
    );

    const body = await request.json();
    expect(body).toEqual({
      id: "ARM-Z",
      properties: { name: "Arm Z" },
    });
  });

  it("serializes nested mutations correctly for PUT requests", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    const mutateSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({ status: "success" })
    );
    mswServer.use(
      http.put("**/api/v1/studies/:studyId/versions/:versionId/arms/:armId", mutateSpy)
    );

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

    expect(mutateSpy).toHaveBeenCalledTimes(1);
    const { request } = mutateSpy.mock.calls[0][0];

    expect(request.url).toContain(
      "/api/v1/studies/STUDY-01/versions/v_draft_01/arms/ARM-Z"
    );
    expect(request.method).toBe("PUT");
    const body = await request.json();
    expect(body).toEqual({
      properties: { name: "Arm Z Modified" },
    });
  });
});

describe("SoA Builder Signed API Client & Store Integration", () => {
  it("generates correct GxP version 2 signed headers for mutations", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    const armMutationSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({ status: "success", id: "ARM-A" })
    );
    const soaProjectionSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({
        epochs: [{ epoch_id: "EP-1", epoch_name: "Epoch 1" }],
        encounters: [
          { encounter_id: "E1", encounter_name: "Visit 1", epoch_id: "EP-1" },
        ],
        rows: [{ activity_id: "ACT1", activity_name: "Vitals", cells: [] }],
      })
    );

    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", armMutationSpy),
      http.get("**/api/v1/studies/:studyId/versions/:versionId/soa-projection", soaProjectionSpy)
    );

    const store = useClinicalStore();

    await store.pushSoAMutation(
      "arms",
      "ARM-A",
      { name: "Active Arm" },
      "Configure arm"
    );

    expect(armMutationSpy).toHaveBeenCalledTimes(1);
    expect(soaProjectionSpy).toHaveBeenCalledTimes(1);

    const { request } = armMutationSpy.mock.calls[0][0];

    expect(request.url).toContain(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/arms"
    );
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-User-Id")).toBe("fderuiter");
    expect(request.headers.get("X-User-Roles")).toBe("STUDY_DESIGNER");
    expect(request.headers.get("X-Signature-Version")).toBe("2");
    expect(request.headers.get("X-Gateway-Signature")).toBeDefined();
    expect(request.headers.get("X-Change-Reason")).toBe("Configure arm");

    // Verify local USDM state updated via fetchSoAProjection trigger
    expect(store.currentUsdm.epochs).toHaveLength(1);
    expect(store.currentUsdm.encounters).toHaveLength(1);
    expect(store.currentUsdm.rows).toHaveLength(1);
  });

  it("handles link creation API calls correctly with signed headers", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    const linkSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({ status: "success", message: "Link established" })
    );
    const soaProjectionSpy = vi.fn().mockImplementation(() =>
      HttpResponse.json({ epochs: [], encounters: [], rows: [] })
    );

    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/links/visit-procedure", linkSpy),
      http.get("**/api/v1/studies/:studyId/versions/:versionId/soa-projection", soaProjectionSpy)
    );

    const store = useClinicalStore();

    const payload = {
      procedure_id: "ACT-VS",
      visit_id: "V-SCR",
      is_applicable: true,
    };
    await store.pushSoALink("visit-procedure", payload, "Link VS to Screening");

    expect(linkSpy).toHaveBeenCalledTimes(1);
    const { request } = linkSpy.mock.calls[0][0];

    expect(request.url).toContain(
      "/api/v1/studies/STUDY-USDM-001/versions/v_draft_01/links/visit-procedure"
    );
    expect(request.method).toBe("POST");
    expect(await request.json()).toEqual(payload);
    expect(request.headers.get("X-Change-Reason")).toBe("Link VS to Screening");
  });

  it("gracefully falls back to local browser state and records offline blocks on sync failure", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", () =>
        HttpResponse.error()
      )
    );

    const store = useClinicalStore();

    // Trigger store push action
    const triggerMutation = () =>
      store.pushSoAMutation(
        "arms",
        "ARM-C",
        { name: "High Dose Arm" },
        "Fork protocol for high dose cohort"
      );

    await expect(triggerMutation()).rejects.toThrow("Failed to fetch");

    // Local ledger still retains offline mutation log
    const ledger = store.ledgerBlocks;
    expect(ledger).toHaveLength(1);
    expect(ledger[0].action).toBe("SOA_MUTATION_OFFLINE_ARMS");
    expect(ledger[0].reason).toBe("Fork protocol for high dose cohort");
    expect(ledger[0].details.id).toBe("ARM-C");
    expect(ledger[0].details.error).toBe("Failed to fetch");
  });
});

describe("SoA Authoring Failure & Immutability Guards", () => {
  it("handles locked version immutability failure (HTTP 403 / IMMUTABILITY_VIOLATION)", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", () =>
        HttpResponse.json(
          { detail: "IMMUTABILITY_VIOLATION: Locked Version cannot be modified" },
          { status: 403 }
        )
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
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", () =>
        HttpResponse.json(
          { detail: "Missing mandatory fields properties.name" },
          { status: 400 }
        )
      )
    );

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
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/studies/:studyId/versions/:versionId/arms", () =>
        HttpResponse.json(
          { detail: "INVALID_OR_MISSING_SIGNATURE" },
          { status: 401 }
        )
      )
    );

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
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/execution/batch-sign-off", () =>
        HttpResponse.json(
          { detail: "REAUTHENTICATION_REQUIRED" },
          { status: 401 }
        )
      )
    );

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
