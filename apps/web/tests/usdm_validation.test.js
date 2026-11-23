import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import {
  DEFAULT_CLINICAL_USDM,
  buildMutationValidationPayload,
  cloneDefaultClinicalUsdm,
  formatZodIssues,
  useClinicalStore,
} from "../src/stores/clinical.js";
import { useAuthStore } from "../src/stores/auth.js";
import { useToastStore } from "../src/stores/toasts.js";
import { soaClient } from "../src/api/soaClient.js";
import { stateTrackingPlugin } from "../src/stores/plugins.js";
import { setupServer } from "msw/node";
import { createWebDemoHandlers } from "../src/mocks/demoHandlers.js";

describe("Shared Zod Schema Client-Side Validation", () => {
  beforeEach(() => {
    const pinia = createPinia();
    pinia.use(stateTrackingPlugin);
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
    window.localStorage.clear();
  });

  it("should successfully validate valid arms and epochs mutations", () => {
    const store = useClinicalStore();

    // Valid arm properties (id/name is string)
    const validArm = store.validateModel("arms", {
      id: "ARM-C",
      name: "Arm C: Dose escalation 20mg",
      armType: "Treatment",
      description: "Escalated active ingredient dose",
    });

    expect(validArm.success).toBe(true);
    expect(validArm.data.id).toBe("ARM-C");
    expect(validArm.data.name).toBe("Arm C: Dose escalation 20mg");

    // Valid epoch properties
    const validEpoch = store.validateModel("epochs", {
      id: "EP-A3",
      name: "Active Phase III",
      epochType: "Treatment",
      sequenceNumber: 3,
    });

    expect(validEpoch.success).toBe(true);
    expect(validEpoch.data.sequenceNumber).toBe(3);
  });

  it("should reject invalid mutations violating the shared Zod schema", () => {
    const store = useClinicalStore();

    // Invalid arm properties: missing required name
    const invalidArm = store.validateModel("arms", {
      id: "ARM-INVALID",
      // name is missing
      armType: 123, // should be string
    });

    expect(invalidArm.success).toBe(false);
    expect(invalidArm.error.issues || invalidArm.error.errors).toBeDefined();

    // Invalid epoch properties: sequenceNumber is string instead of int
    const invalidEpoch = store.validateModel("epochs", {
      id: "EP-INVALID",
      name: "Epoch Invalid",
      sequenceNumber: "first", // should be int/number
    });

    expect(invalidEpoch.success).toBe(false);
  });

  it("should reject and log mutations that violate shared schemas in pushSoAMutation", async () => {
    const store = useClinicalStore();

    // Mock direct API so we don't trigger real network calls
    vi.spyOn(store, "addLedgerBlock").mockResolvedValue({ hash: "mock-hash" });

    // Invalid epoch call should throw an error and set soaError
    await expect(
      store.pushSoAMutation(
        "epochs",
        "EP-INV",
        {
          // name is missing
          epochType: "Screening",
          sequenceNumber: "invalid-int",
        },
        "Compliance change"
      )
    ).rejects.toThrow(
      "Local payload mutation rejected. Shared Zod Schema violation"
    );

    expect(store.soaError).toContain(
      "Local payload mutation rejected. Shared Zod Schema violation"
    );
  });

  it("should accept valid mutations inside pushSoAMutation", async () => {
    const store = useClinicalStore();

    // Mock direct API and local updates
    vi.spyOn(soaClient, "saveArm").mockResolvedValue({});
    vi.spyOn(store, "addLedgerBlock").mockResolvedValue({ hash: "mock-hash" });
    vi.spyOn(store, "fetchSoAProjection").mockResolvedValue({});

    // Valid call
    await store.pushSoAMutation(
      "arms",
      "ARM-NEW",
      {
        name: "Arm New Dose",
        armType: "Treatment",
      },
      "Valid design update"
    );

    expect(store.soaError).toBeNull();
  });

  it("alerts the user of a skip-logic cycle before writing modifications to local storage", async () => {
    const store = useClinicalStore();

    // Introduce a skip-logic cyclic loop in ecrfFields: f1 -> f2 -> f3 -> f1
    store.ecrfFields = [
      {
        id: "f1",
        label: "Field 1",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [{ node_type: "XPATH", value: "f2" }],
        },
      },
      {
        id: "f2",
        label: "Field 2",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [{ node_type: "XPATH", value: "f3" }],
        },
      },
      {
        id: "f3",
        label: "Field 3",
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [{ node_type: "XPATH", value: "f1" }],
        },
      },
    ];

    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    const toastStore = useToastStore();

    // Attempting to write modifications to local storage (addLedgerBlock)
    await expect(
      store.addLedgerBlock("UPDATE_FIELD", { id: "f1" }, "Testing cyclic check")
    ).rejects.toThrow("USDM Graph Validator");

    // Verify persistent error toast was pushed instead of window.alert
    expect(alertSpy).not.toHaveBeenCalled();
    expect(toastStore.toasts[0]).toMatchObject({
      tone: "error",
      title: "Skip-Logic Cycle Alert",
    });
    expect(toastStore.toasts[0].message).toContain("f1 -> f2 -> f3 -> f1");

    // Verify local storage was NOT modified with the new block
    expect(window.localStorage.getItem("ledgerBlocks")).toBeNull();

    alertSpy.mockRestore();
  });

  it("enforces DEFAULT_CLINICAL_USDM immutability and deep-cloned normalization in cloneDefaultClinicalUsdm", () => {
    expect(Object.isFrozen(DEFAULT_CLINICAL_USDM)).toBe(true);
    expect(() => {
      // @ts-expect-error testing runtime freeze
      DEFAULT_CLINICAL_USDM.studyId = "MUTATED-STUDY";
    }).toThrow();

    const cloned = cloneDefaultClinicalUsdm();
    expect(cloned.studyId).toBe("STUDY-USDM-001");
    expect(cloned.forms).toEqual([]);
    expect(cloned.crossovers).toEqual([]);
    expect(cloned.arms[0].armId).toBe("ARM-A");
    expect(cloned.epochs[0].epochId).toBe("EP-SCR");
    expect(cloned.encounters[0].encounterId).toBe("V-SCR");
    expect(cloned.rows[0].activityId).toBe("ACT-DEM");
    expect(cloned.rows[0].cells[0].isApplicable).toBe(true);
    expect(
      Object.getOwnPropertyDescriptor(cloned.arms[0], "armId")?.enumerable
    ).toBe(false);

    // Mutate nested arrays and cells in place; DEFAULT_CLINICAL_USDM and fresh clones must remain untouched
    cloned.arms.push({
      arm_id: "ARM-MUT",
      arm_name: "Mutated Arm",
      sequence: 3,
    });
    cloned.rows[0].cells[0].is_applicable = false;
    cloned.objectives[0].description = "Mutated objective";

    expect(DEFAULT_CLINICAL_USDM.arms).toHaveLength(2);
    expect(DEFAULT_CLINICAL_USDM.rows[0].cells[0].is_applicable).toBe(true);
    expect(DEFAULT_CLINICAL_USDM.objectives[0].description).toContain(
      "Systolic Blood Pressure"
    );

    const freshClone = cloneDefaultClinicalUsdm();
    expect(freshClone.arms).toHaveLength(2);
    expect(freshClone.rows[0].cells[0].is_applicable).toBe(true);
  });

  it("formats Zod issues and builds mutation validation payloads consistently", () => {
    expect(formatZodIssues(null)).toEqual([]);
    expect(
      formatZodIssues({
        issues: [
          { path: ["studyDesigns", 0, "name"], message: "Required" },
          { path: [], message: "Invalid root payload" },
        ],
      })
    ).toEqual(["studyDesigns.0.name: Required", "field: Invalid root payload"]);

    expect(
      formatZodIssues(
        {
          errors: [{ path: [], message: "Invalid study root" }],
        },
        "root"
      )
    ).toEqual(["root: Invalid study root"]);

    expect(
      buildMutationValidationPayload({
        type: "arms",
        id: "ARM-1",
        properties: { name: "Active" },
      })
    ).toEqual({ id: "ARM-1", name: "Active" });

    expect(
      buildMutationValidationPayload({
        type: "concept",
        id: "C99999",
        properties: { preferred_name: "Systolic Blood Pressure" },
      })
    ).toEqual({
      code: "C99999",
      codeSystem: "NCI_Thesaurus",
      codeSystemVersion: "24.03d",
      decode: "Systolic Blood Pressure",
    });

    expect(
      buildMutationValidationPayload({
        type: "link",
        payload: { procedure_id: "ACT-VS", visit_id: "V-SCR" },
      })
    ).toBeNull();
  });

  it("pushes a persistent CYCLE_DETECTED error toast before console.error in both addLedgerBlock and pushSoAMutation without window.alert", async () => {
    const store = useClinicalStore();
    const toastStore = useToastStore();
    store.ecrfFields = [
      {
        id: "f1",
        label: "Field 1",
        type: "text",
        gridSpan: 6,
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [{ node_type: "XPATH", value: "f2" }],
        },
      },
      {
        id: "f2",
        label: "Field 2",
        type: "text",
        gridSpan: 6,
        relevant: {
          node_type: "OPERATOR",
          value: "==",
          children: [{ node_type: "XPATH", value: "f1" }],
        },
      },
    ];

    const callOrder = [];
    const alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    const originalPush = toastStore.push.bind(toastStore);
    const pushSpy = vi
      .spyOn(toastStore, "push")
      .mockImplementation((options) => {
        callOrder.push(
          `toast:${options.tone}:${options.timeoutMs}:${options.title}:${options.message}`
        );
        return originalPush(options);
      });
    const errorSpy = vi.spyOn(console, "error").mockImplementation((msg) => {
      callOrder.push(`error:${msg}`);
    });
    const saveArmSpy = vi.spyOn(soaClient, "saveArm").mockResolvedValue({});

    await expect(
      store.addLedgerBlock("UPDATE_FIELD", { id: "f1" }, "Cycle test ledger")
    ).rejects.toThrow(
      "Local storage persistence blocked by USDM Graph Validator:"
    );

    expect(alertSpy).not.toHaveBeenCalled();
    expect(callOrder[0]).toContain("toast:error:0:Skip-Logic Cycle Alert:");
    expect(callOrder[1]).toContain(
      "error:Local storage persistence blocked by USDM Graph Validator:"
    );
    expect(toastStore.toasts[0]).toMatchObject({
      tone: "error",
      title: "Skip-Logic Cycle Alert",
    });

    callOrder.length = 0;
    toastStore.toasts = [];

    await expect(
      store.pushSoAMutation(
        "arms",
        "ARM-CYCLE",
        { name: "Cycle Arm" },
        "Cycle test mutation"
      )
    ).rejects.toThrow("Graph validation failure:");

    expect(alertSpy).not.toHaveBeenCalled();
    expect(callOrder[0]).toContain("toast:error:0:Skip-Logic Cycle Alert:");
    expect(callOrder[1]).toContain("error:Graph validation failure:");
    expect(toastStore.toasts[0]).toMatchObject({
      tone: "error",
      title: "Skip-Logic Cycle Alert",
    });
    expect(saveArmSpy).not.toHaveBeenCalled();

    pushSpy.mockRestore();
    alertSpy.mockRestore();
    errorSpy.mockRestore();
  });

  it("keeps DEFAULT_CLINICAL_USDM frozen with 4 activity rows while MSW soaState returns ACT-DEM and ACT-VS", async () => {
    expect(Object.isFrozen(DEFAULT_CLINICAL_USDM)).toBe(true);
    expect(DEFAULT_CLINICAL_USDM.rows.map((row) => row.activity_id)).toEqual([
      "ACT-DEM",
      "ACT-VS",
      "ACT-AE",
      "ACT-MED",
    ]);

    const server = setupServer(...createWebDemoHandlers());
    server.listen({ onUnhandledRequest: "bypass" });
    try {
      const response = await fetch(
        "http://localhost/api/v1/studies/STUDY-USDM-001/versions/v1/soa-projection"
      );
      expect(response.status).toBe(200);
      const body = await response.json();
      expect(body.rows.map((row) => row.activity_id)).toEqual([
        "ACT-DEM",
        "ACT-VS",
      ]);
      expect(body.arms.map((arm) => arm.arm_id)).toEqual(["ARM-A", "ARM-B"]);
      expect(body.epochs.map((ep) => ep.epoch_id)).toEqual([
        "EP-SCR",
        "EP-TRT-A",
        "EP-TRT-B",
      ]);
      expect(body.encounters.map((enc) => enc.encounter_id)).toEqual([
        "V-SCR",
        "V-TRT-A1",
        "V-TRT-A2",
        "V-TRT-B1",
      ]);
    } finally {
      server.close();
    }
  });
});
