import { describe, it, expect, beforeEach, vi } from "vitest";

// Mock global fetch (before the stores and API client are imported)
const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore, getStorageKey } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth.js";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

describe("FDA 21 CFR Part 11 Sync Ledger Store", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
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
    const savedValues = JSON.parse(
      window.localStorage.getItem(getStorageKey("formValues"))
    );
    const savedQueries = JSON.parse(
      window.localStorage.getItem(getStorageKey("formQueries"))
    );
    const savedBlocks = JSON.parse(
      window.localStorage.getItem(getStorageKey("ledgerBlocks"))
    );

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
});
