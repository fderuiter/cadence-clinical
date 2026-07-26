import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical.js";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("SoA Builder Signed API Client & Store Integration", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    window.localStorage.clear();
    mockFetch.mockReset();
  });

  it("generates correct GxP version 2 signed headers for mutations", async () => {
    // 1st fetch: Arm Mutation save
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ status: "success", id: "ARM-A" }),
    });
    // 2nd fetch: fetchSoAProjection load
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({ epochs: [], encounters: [], rows: [] }),
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
