import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";
import { useAuthStore } from "../src/stores/auth.js";

// Mock global fetch
const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("FDA 21 CFR Part 11 Sync Ledger Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    window.localStorage.clear();
    mockFetch.mockReset();
  });

  it("persists locally added ledger blocks, form values, and queries into localStorage", async () => {
    const store = useClinicalStore();

    // Mutate state
    store.formValues.vssbp = "220";
    store.formQueries["vssbp"] = {
      status: "OPEN",
      message: "Unusually high BP reading",
      createdBy: "fderuiter",
      createdAt: "2026-07-24",
    };

    // Add ledger block (which triggers localStorage write)
    const block = await store.addLedgerBlock(
      "QUERY_CREATE",
      {
        fieldId: "vssbp",
        studyId: "STUDY-USDM-001",
        subjectId: "SUBJ-001",
        visitId: "Screening",
        domain: "VS",
        testCode: "VSSBP",
        query: store.formQueries["vssbp"],
      },
      "Raised discrepancy"
    );

    expect(block.action).toBe("QUERY_CREATE");
    expect(block.synced).toBe(false);

    // Verify localStorage persistence
    const savedValues = JSON.parse(window.localStorage.getItem("formValues"));
    const savedQueries = JSON.parse(window.localStorage.getItem("formQueries"));
    const savedBlocks = JSON.parse(window.localStorage.getItem("ledgerBlocks"));

    expect(savedValues.vssbp).toBe("220");
    expect(savedQueries.vssbp.status).toBe("OPEN");
    expect(savedBlocks).toHaveLength(1);
    expect(savedBlocks[0].action).toBe("QUERY_CREATE");
  });

  it("initializes Pinia store from persistent state in localStorage", () => {
    // Write fake persistent state to localStorage
    window.localStorage.setItem("formValues", JSON.stringify({ pulse: "95" }));
    window.localStorage.setItem(
      "formQueries",
      JSON.stringify({
        pulse: { status: "ANSWERED", message: "Pulse issue" },
      })
    );
    window.localStorage.setItem(
      "ledgerBlocks",
      JSON.stringify([{ index: 0, action: "QUERY_RESPOND", synced: false }])
    );

    // Initialize new store instance
    const store = useClinicalStore();

    expect(store.formValues.pulse).toBe("95");
    expect(store.formQueries.pulse.status).toBe("ANSWERED");
    expect(store.ledgerBlocks).toHaveLength(1);
    expect(store.ledgerBlocks[0].action).toBe("QUERY_RESPOND");
  });

  it("background sync worker successfully transmits unsynced blocks using apiClient and executionService without browser signatures", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success", processed_blocks: 1 }),
    });

    const store = useClinicalStore();

    // Add block
    await store.addLedgerBlock(
      "QUERY_CREATE",
      {
        fieldId: "vssbp",
        query: { status: "OPEN", message: "High BP" },
      },
      "Create query"
    );

    expect(store.ledgerBlocks[0].synced).toBe(false);

    // Run sync without a step-up token
    await store.syncUnsyncedBlocks();

    // Verify fetch was called with standard Bearer authorization and change-reason headers
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toContain("/api/v1/execution/queries/sync");
    expect(options.method).toBe("POST");
    expect(options.headers["Authorization"]).toBe(
      "Bearer mock-keycloak-jwt-token"
    );
    expect(options.headers["X-Change-Reason"]).toBe(
      "Background sync of clinical query ledger blocks"
    );
    expect(options.headers["X-Sig-Token"]).toBeUndefined();
    expect(options.headers["X-Signature-Version"]).toBeUndefined();
    expect(options.headers["X-Gateway-Signature"]).toBeUndefined();
    expect(options.headers["X-Gateway-Timestamp"]).toBeUndefined();
    expect(options.headers["X-User-Id"]).toBeUndefined();
    expect(options.headers["X-User-Roles"]).toBeUndefined();

    // Verify local block synced flag is set to true
    expect(store.ledgerBlocks[0].synced).toBe(true);

    // Verify synced state saved in localStorage
    const savedBlocks = JSON.parse(window.localStorage.getItem("ledgerBlocks"));
    expect(savedBlocks[0].synced).toBe(true);
  });

  it("propagates step-up token as X-Sig-Token when passed to syncUnsyncedBlocks", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success", processed_blocks: 1 }),
    });

    const store = useClinicalStore();

    // Add block
    await store.addLedgerBlock(
      "QUERY_CREATE",
      {
        fieldId: "vssbp",
        query: { status: "OPEN", message: "High BP" },
      },
      "Create query"
    );

    // Run sync with step-up token
    await store.syncUnsyncedBlocks("mock-gateway-issued-token");

    // Verify fetch was called
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [, options] = mockFetch.mock.calls[0];

    // X-Sig-Token should be passed
    expect(options.headers["X-Sig-Token"]).toBe("mock-gateway-issued-token");
  });

  it("handles fetch network failure gracefully without dropping unsynced transactions and surfaces the error", async () => {
    mockFetch.mockRejectedValueOnce(new Error("Network disconnect"));

    const store = useClinicalStore();

    await store.addLedgerBlock(
      "QUERY_CREATE",
      {
        fieldId: "pulse",
        query: { status: "OPEN", message: "Pulse rate zero" },
      },
      "Raised query"
    );

    expect(store.ledgerBlocks[0].synced).toBe(false);

    // Run sync (which catches, logs, and rethrows the error)
    await expect(store.syncUnsyncedBlocks()).rejects.toThrow(
      "Network disconnect"
    );

    // Transaction is not dropped and synced flag remains false
    expect(store.ledgerBlocks[0].synced).toBe(false);

    // Local storage still retains unsynced status
    const savedBlocks = JSON.parse(window.localStorage.getItem("ledgerBlocks"));
    expect(savedBlocks[0].synced).toBe(false);
  });
});
