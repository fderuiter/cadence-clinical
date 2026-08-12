import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";
import { useAuthStore } from "../src/stores/auth.js";
import { soaClient } from "../src/api/soaClient.js";

describe("Shared Zod Schema Client-Side Validation", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
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
    expect(invalidArm.error.errors).toBeDefined();

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
});
