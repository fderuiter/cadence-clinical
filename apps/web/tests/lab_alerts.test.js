import { describe, it, expect, vi, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { executionService } from "../src/api/execution";
import { stateTrackingPlugin } from "../src/stores/plugins.js";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("Lab Alerts - API Client & Pinia Store Unit Tests", () => {
  let pinia;

  beforeEach(() => {
    mockFetch.mockReset();
    pinia = createPinia();
    pinia.use(stateTrackingPlugin);
    setActivePinia(pinia);
  });

  describe("executionService.listLabAlerts API Client", () => {
    it("should list lab alerts and append query string correctly", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => [{ id: "alert-1", test_code: "VSSBP" }],
      });

      const res = await executionService.listLabAlerts({
        study_id: "STUDY-01",
        subject_id: "SUBJ-001",
        test_code: "VSSBP",
      });

      expect(res).toEqual([{ id: "alert-1", test_code: "VSSBP" }]);
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const [url, options] = mockFetch.mock.calls[0];
      expect(url).toBe(
        "http://localhost:8000/api/v1/execution/lab-alerts?study_id=STUDY-01&subject_id=SUBJ-001&test_code=VSSBP"
      );
      expect(options.method).toBe("GET");
    });

    it("should list lab alerts with no params when empty", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => [],
      });

      const res = await executionService.listLabAlerts();

      expect(res).toEqual([]);
      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe("http://localhost:8000/api/v1/execution/lab-alerts");
    });
  });

  describe("useClinicalStore() - Lab Alerts Actions and State", () => {
    it("should hold default lab alerts states", () => {
      const store = useClinicalStore();
      expect(store.labAlerts).toEqual({});
      expect(store.labAlertsLoading).toBe(false);
      expect(store.labAlertsError).toBeNull();
    });

    it("should fetch, transform and key lab alerts by field ID based on CDASH match on success", async () => {
      const mockAlerts = [
        { id: "alert-1", test_code: "VSSBP", message: "Value out of range" },
        { id: "alert-2", test_code: "BRTHDT", message: "Age check failed" },
      ];

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => mockAlerts,
      });

      const store = useClinicalStore();
      const fetchPromise = store.fetchLabAlerts("STUDY-01", "SUBJ-001");

      // Verify intermediate loading state
      expect(store.labAlertsLoading).toBe(true);
      expect(store.labAlertsError).toBeNull();

      await fetchPromise;

      expect(store.labAlertsLoading).toBe(false);
      expect(store.labAlertsError).toBeNull();

      // Ensure mapping is correct
      // Default ecrfFields has:
      // - vssbp with cdash: "VS.VSSBP"
      // - brthdt with cdash: "DM.BRTHDT"
      expect(store.labAlerts.vssbp).toEqual(mockAlerts[0]);
      expect(store.labAlerts.brthdt).toEqual(mockAlerts[1]);
      expect(store.labAlerts.concept_code).toBeUndefined(); // No alert with CONCEPT_CODE test_code was returned
    });

    it("should fallback to {} on success if returned response is not an array", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => null,
      });

      const store = useClinicalStore();
      await store.fetchLabAlerts("STUDY-01", "SUBJ-001");

      expect(store.labAlertsLoading).toBe(false);
      expect(store.labAlertsError).toBeNull();
      expect(store.labAlerts).toEqual({});
    });

    it("should handle error gracefully, setting error state and console.warn on fetch failure", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
        json: async () => ({ detail: "Database connection failed" }),
      });

      const store = useClinicalStore();
      const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

      await store.fetchLabAlerts("STUDY-01", "SUBJ-001");

      expect(store.labAlertsLoading).toBe(false);
      expect(store.labAlertsError).toBe("Database connection failed");
      expect(warnSpy).toHaveBeenCalled();

      warnSpy.mockRestore();
    });
  });
});
