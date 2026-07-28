import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";

describe("FDA 21 CFR Part 11 Sync Ledger Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    window.localStorage.clear();
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

  it("background sync worker successfully transmits unsynced blocks to execution sync gateway", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    const syncHandler = vi
      .fn()
      .mockImplementation(() =>
        HttpResponse.json({ status: "success", processed_blocks: 1 })
      );
    mswServer.use(http.post("**/api/v1/execution/queries/sync", syncHandler));

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

    // Run sync
    await store.syncUnsyncedBlocks();

    // Verify fetch was called with Gateway v2 signature and re-authentication tokens
    expect(syncHandler).toHaveBeenCalledTimes(1);
    const { request } = syncHandler.mock.calls[0][0];
    expect(request.url).toContain("/api/v1/execution/queries/sync");
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-Signature-Version")).toBe("2");
    expect(request.headers.get("X-Sig-Token")).toBeDefined();
    expect(request.headers.get("X-Gateway-Signature")).toBeDefined();

    // Verify local block synced flag is set to true
    expect(store.ledgerBlocks[0].synced).toBe(true);

    // Verify synced state saved in localStorage
    const savedBlocks = JSON.parse(window.localStorage.getItem("ledgerBlocks"));
    expect(savedBlocks[0].synced).toBe(true);
  });

  it("handles fetch network failure gracefully without dropping unsynced transactions", async () => {
    const { mswServer, http, HttpResponse } = globalThis;
    mswServer.use(
      http.post("**/api/v1/execution/queries/sync", () => HttpResponse.error())
    );

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

    // Run sync (which catches and logs error)
    await store.syncUnsyncedBlocks();

    // Transaction is not dropped and synced flag remains false
    expect(store.ledgerBlocks[0].synced).toBe(false);

    // Local storage still retains unsynced status
    const savedBlocks = JSON.parse(window.localStorage.getItem("ledgerBlocks"));
    expect(savedBlocks[0].synced).toBe(false);
  });
});
