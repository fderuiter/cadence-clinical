import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { executionService } from "../src/api/execution";
import { stateTrackingPlugin } from "../src/stores/plugins.js";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

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
      mockFetch.mockResolvedValueOnce(
        jsonResponse([{ id: "alert-1", test_code: "VSSBP" }])
      );

      const res = await executionService.listLabAlerts({
        study_id: "STUDY-01",
        subject_id: "SUBJ-001",
        test_code: "VSSBP",
      });

      expect(res).toEqual([{ id: "alert-1", test_code: "VSSBP" }]);
      expect(mockFetch).toHaveBeenCalledTimes(1);

      // Lab alerts are served by the toxicity review queue; the contract (src/api/types.ts) has no
      // /lab-alerts route and no test_code filter, so the client does not forward test_code.
      const request = await recordedRequest(mockFetch, 0);
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/execution/labs/toxicity/review-queue?study_id=STUDY-01&subject_id=SUBJ-001"
      );
      expect(request.method).toBe("GET");
    });

    it("should list lab alerts with no params when empty", async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse([]));

      const res = await executionService.listLabAlerts();

      expect(res).toEqual([]);
      const request = await recordedRequest(mockFetch, 0);
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/execution/labs/toxicity/review-queue"
      );
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

      mockFetch.mockResolvedValueOnce(jsonResponse(mockAlerts));

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
      mockFetch.mockResolvedValueOnce(jsonResponse(null));

      const store = useClinicalStore();
      await store.fetchLabAlerts("STUDY-01", "SUBJ-001");

      expect(store.labAlertsLoading).toBe(false);
      expect(store.labAlertsError).toBeNull();
      expect(store.labAlerts).toEqual({});
    });

    it("should handle error gracefully, setting error state and console.warn on fetch failure", async () => {
      mockFetch.mockResolvedValueOnce(
        jsonResponse(
          { detail: "Database connection failed" },
          { status: 500, statusText: "Internal Server Error" }
        )
      );

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
