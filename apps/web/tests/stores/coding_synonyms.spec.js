import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useCodingStore } from "../../src/stores/coding.js";
import { apiClient } from "../../src/api/apiClient.js";

vi.mock("../../src/api/apiClient.js", () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}));

describe("Coding Store - Bulk Synonym Import & Propagation Engine", () => {
  let store;

  beforeEach(() => {
    setActivePinia(createPinia());
    store = useCodingStore();
    vi.clearAllMocks();
  });

  describe("File Preflight Validation", () => {
    it("validates a synonym file and updates state with summary report", async () => {
      const mockReport = {
        total_rows_parsed: 10,
        valid_rows_count: 8,
        duplicate_rows_count: 1,
        invalid_rows_count: 1,
        can_import: true,
        scope: "STUDY",
        study_id: "STUDY-101",
        invalid_row_failures: [
          {
            line_number: 5,
            verbatim_text: "bad_row",
            error_message: "Missing target code",
          },
        ],
      };

      apiClient.post.mockResolvedValueOnce(mockReport);

      const file = new File(
        ["VerbatimText,TargetCode,TargetTerm\nHeadache,10019211,Headache"],
        "synonyms.csv",
        {
          type: "text/csv",
        }
      );

      const report = await store.validateSynonymFile({
        file,
        scope: "STUDY",
        studyId: "STUDY-101",
      });

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/execution/coding/synonyms/validate",
        expect.any(FormData),
        expect.objectContaining({
          changeReason: expect.stringContaining("synonyms.csv"),
        })
      );
      expect(report).toEqual(mockReport);
      expect(store.synonymPreflightReport).toEqual(mockReport);
      expect(store.synonymImportError).toBeNull();
    });

    it("handles validation error gracefully", async () => {
      apiClient.post.mockRejectedValueOnce(new Error("Network error"));

      const file = new File(["test"], "synonyms.csv", { type: "text/csv" });

      await expect(
        store.validateSynonymFile({ file, scope: "STUDY" })
      ).rejects.toThrow("Network error");

      expect(store.synonymImportError).toBe("Network error");
    });
  });

  describe("Bulk Synonym Import", () => {
    it("imports synonym rules and refreshes active rules and job history", async () => {
      const mockImportResponse = {
        import_job_id: "job-123",
        status: "COMPLETED",
        rules_created_count: 5,
        total_rows_processed: 5,
      };

      apiClient.post.mockResolvedValueOnce(mockImportResponse);
      apiClient.get.mockResolvedValueOnce([
        {
          id: "rule-1",
          verbatim_text: "HEADACHE",
          target_code: "10019211",
          target_term: "Headache",
          dictionary_type: "MEDDRA",
          scope: "STUDY",
          status: "ACTIVE",
        },
      ]); // fetchActiveSynonymRules
      apiClient.get.mockResolvedValueOnce([
        {
          id: "job-123",
          file_name: "synonyms.csv",
          status: "COMPLETED",
        },
      ]); // fetchSynonymImportJobs

      const file = new File(
        ["VerbatimText,TargetCode,TargetTerm\nHEADACHE,10019211,Headache"],
        "synonyms.csv",
        {
          type: "text/csv",
        }
      );

      const result = await store.importSynonymRules({
        file,
        scope: "STUDY",
        studyId: "STUDY-101",
        reasonForChange: "Initial synonym import for Study 101",
      });

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/execution/coding/synonyms/import",
        expect.any(FormData),
        expect.objectContaining({
          changeReason: "Initial synonym import for Study 101",
        })
      );

      expect(result).toEqual(mockImportResponse);
      expect(store.synonymRules).toHaveLength(1);
      expect(store.synonymImportJobs).toHaveLength(1);
      expect(store.synonymPreflightReport).toBeNull();
    });
  });

  describe("Fetching Active Rules and Job Audit History", () => {
    it("fetches active rules for study and populates synonymRules", async () => {
      const mockRules = [
        {
          id: "rule-1",
          verbatim_text: "FEVER",
          target_code: "10016558",
          target_term: "Pyrexia",
          dictionary_type: "MEDDRA",
          scope: "ENTERPRISE",
          status: "ACTIVE",
        },
      ];

      apiClient.get.mockResolvedValueOnce(mockRules);

      const rules = await store.fetchActiveSynonymRules("STUDY-101");

      expect(apiClient.get).toHaveBeenCalledWith(
        "/api/v1/execution/coding/synonyms?studyId=STUDY-101",
        expect.any(Object)
      );
      expect(rules).toEqual(mockRules);
      expect(store.synonymRules).toEqual(mockRules);
    });

    it("fetches synonym import job history", async () => {
      const mockJobs = [
        {
          id: "job-1",
          file_name: "meddra_synonyms.csv",
          total_rows_processed: 15,
          valid_rows_count: 15,
          imported_at: "2026-03-30T10:00:00Z",
        },
      ];

      apiClient.get.mockResolvedValueOnce(mockJobs);

      const jobs = await store.fetchSynonymImportJobs("STUDY-101");

      expect(apiClient.get).toHaveBeenCalledWith(
        "/api/v1/execution/coding/synonyms/jobs?studyId=STUDY-101",
        expect.any(Object)
      );
      expect(jobs).toEqual(mockJobs);
      expect(store.synonymImportJobs).toEqual(mockJobs);
    });
  });

  describe("Deactivating Synonym Rules", () => {
    it("deactivates rule and refreshes active rules", async () => {
      apiClient.delete.mockResolvedValueOnce({
        success: true,
        message: "Deactivated",
      });
      apiClient.get.mockResolvedValueOnce([]); // fetchActiveSynonymRules

      await store.deactivateSynonymRule("rule-1", "Outdated rule", "STUDY-101");

      expect(apiClient.delete).toHaveBeenCalledWith(
        "/api/v1/execution/coding/synonyms/rule-1?reason_for_change=Outdated+rule",
        expect.objectContaining({
          changeReason: "Outdated rule",
        })
      );
      expect(store.synonymRules).toEqual([]);
    });
  });

  describe("Automatic Rule Scope Precedence Propagation", () => {
    it("prioritizes STUDY scope rules over ENTERPRISE scope rules", () => {
      store.assignments = [
        {
          id: "OBS-101",
          verbatim_text: "SEVERE HEADACHE",
          dictionary_type: "MEDDRA",
          status: "UNCODED",
          has_query: false,
          is_blocked: false,
          is_locked: false,
        },
      ];

      store.synonymRules = [
        {
          id: "rule-ent",
          verbatim_text: "SEVERE HEADACHE",
          target_code: "10019211",
          target_term: "Enterprise Headache",
          dictionary_type: "MEDDRA",
          scope: "ENTERPRISE",
          status: "ACTIVE",
        },
        {
          id: "rule-study",
          verbatim_text: "SEVERE HEADACHE",
          target_code: "10019212",
          target_term: "Study Specific Headache",
          dictionary_type: "MEDDRA",
          scope: "STUDY",
          study_id: "STUDY-101",
          status: "ACTIVE",
        },
      ];

      store.applySynonymRulesToQueue();

      const item = store.assignments[0];
      expect(item.status).toBe("AUTO_CODED");
      expect(item.coded_code).toBe("10019212");
      expect(item.coded_term).toBe("Study Specific Headache");

      expect(store.auditLedger).toHaveLength(1);
      expect(store.auditLedger[0].reason_for_change).toContain(
        "study-level synonym rule"
      );
    });

    it("falls back to ENTERPRISE scope rule if no STUDY scope rule matches", () => {
      store.assignments = [
        {
          id: "OBS-102",
          verbatim_text: "FEVER",
          dictionary_type: "MEDDRA",
          status: "UNCODED",
          has_query: false,
          is_blocked: false,
          is_locked: false,
        },
      ];

      store.synonymRules = [
        {
          id: "rule-ent",
          verbatim_text: "FEVER",
          target_code: "10016558",
          target_term: "Pyrexia",
          dictionary_type: "MEDDRA",
          scope: "ENTERPRISE",
          status: "ACTIVE",
        },
      ];

      store.applySynonymRulesToQueue();

      const item = store.assignments[0];
      expect(item.status).toBe("AUTO_CODED");
      expect(item.coded_code).toBe("10016558");
      expect(item.coded_term).toBe("Pyrexia");

      expect(store.auditLedger[0].reason_for_change).toContain(
        "enterprise synonym rule"
      );
    });

    it("does not auto-code blocked or query pending items", () => {
      store.assignments = [
        {
          id: "OBS-103",
          verbatim_text: "FEVER",
          dictionary_type: "MEDDRA",
          status: "QUERY_PENDING",
          has_query: true,
          is_blocked: true,
        },
      ];

      store.synonymRules = [
        {
          id: "rule-ent",
          verbatim_text: "FEVER",
          target_code: "10016558",
          target_term: "Pyrexia",
          dictionary_type: "MEDDRA",
          scope: "ENTERPRISE",
          status: "ACTIVE",
        },
      ];

      store.applySynonymRulesToQueue();

      const item = store.assignments[0];
      expect(item.status).toBe("QUERY_PENDING");
      expect(item.coded_code).toBeUndefined();
    });
  });
});
