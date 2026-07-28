import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { apiClient } from "../src/api/apiClient";
import { designerService } from "../src/api/designer";
import { executionService } from "../src/api/execution";
import { etmfService } from "../src/api/etmf";
import { interopService } from "../src/api/interop";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("Gateway API Clients and Service Modules Unit Tests", () => {
  let pinia;
  let authStore;
  let originalEnvBaseUrl;

  beforeEach(() => {
    // Reset fetch mock
    mockFetch.mockReset();

    // Setup Pinia
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();

    // Cache original environment variable if any
    originalEnvBaseUrl = import.meta.env?.VITE_API_BASE_URL;
  });

  afterEach(() => {
    // Restore VITE_API_BASE_URL
    if (import.meta.env) {
      if (originalEnvBaseUrl !== undefined) {
        import.meta.env.VITE_API_BASE_URL = originalEnvBaseUrl;
      } else {
        delete import.meta.env.VITE_API_BASE_URL;
      }
    }
  });

  describe("Generic apiClient HTTP Wrapper", () => {
    it("should default base URL to http://localhost:8000 when VITE_API_BASE_URL is not set", async () => {
      if (import.meta.env) {
        delete import.meta.env.VITE_API_BASE_URL;
      }

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ status: "ok" }),
      });

      const response = await apiClient.get("/health");
      expect(response).toEqual({ status: "ok" });
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe("http://localhost:8000/health");
    });

    it("should honor custom base URL from VITE_API_BASE_URL", async () => {
      if (import.meta.env) {
        import.meta.env.VITE_API_BASE_URL =
          "https://my-secure-gateway.internal.com";
      }

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ status: "custom" }),
      });

      const response = await apiClient.get("/health");
      expect(response).toEqual({ status: "custom" });
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const [url] = mockFetch.mock.calls[0];
      expect(url).toBe("https://my-secure-gateway.internal.com/health");
    });

    it("should attach Bearer tokens automatically from Pinia authStore", async () => {
      authStore.accessToken = "mock-keycloak-jwt-token";
      authStore.isAuthenticated = true;
      authStore.isDemoMode = false;

      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => ({ user: "fderuiter" }),
      });

      await apiClient.get("/me");

      const [, options] = mockFetch.mock.calls[0];
      expect(options.headers["Authorization"]).toBe(
        "Bearer mock-keycloak-jwt-token"
      );
    });

    it("should append X-Change-Reason for mutations when changeReason option is specified", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 201,
        json: async () => ({ id: "123" }),
      });

      await apiClient.post(
        "/api/v1/mutations",
        { name: "test" },
        { changeReason: "Updating record" }
      );

      const [, options] = mockFetch.mock.calls[0];
      expect(options.headers["X-Change-Reason"]).toBe("Updating record");
    });

    it("should not append X-Change-Reason for GET/read operations even if changeReason option is specified", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        status: 200,
        json: async () => [],
      });

      await apiClient.get("/api/v1/data", { changeReason: "Get data" });

      const [, options] = mockFetch.mock.calls[0];
      expect(options.headers["X-Change-Reason"]).toBeUndefined();
    });

    it("should throw ApiError with custom response body details when request fails", async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 403,
        statusText: "Forbidden",
        json: async () => ({ detail: "IMMUTABILITY_VIOLATION" }),
      });

      await expect(apiClient.post("/api/v1/mutate", {})).rejects.toThrow(
        "IMMUTABILITY_VIOLATION"
      );
    });
  });

  describe("Designer Service Module", () => {
    it("routes study, rules, and concepts queries correctly through gateway contracts", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ id: "STUDY-01" }),
      });

      await designerService.getStudy("STUDY-01");
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/studies/STUDY-01",
        expect.objectContaining({ method: "GET" })
      );

      await designerService.createStudyVersion("STUDY-01", { version: "v1" });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/studies/STUDY-01/versions",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ version: "v1" }),
        })
      );

      await designerService.getRules("STUDY-01");
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/studies/STUDY-01/rules",
        expect.objectContaining({ method: "GET" })
      );

      await designerService.getConcepts();
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/mdr/concepts",
        expect.objectContaining({ method: "GET" })
      );
    });
  });

  describe("Execution Service Module", () => {
    it("routes enrollment, consent, queries, and form submission correctly through gateway contracts", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({}),
      });

      await executionService.createSubject({ site_id: "SITE-01" });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/execution/subjects",
        expect.objectContaining({ method: "POST" })
      );

      await executionService.consentSubject("SUB-01", {
        protocol_version: "1.0",
      });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/execution/subjects/SUB-01/consent",
        expect.objectContaining({ method: "POST" })
      );

      await executionService.getQueries();
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/execution/queries",
        expect.objectContaining({ method: "GET" })
      );

      await executionService.submitForm({ form_id: "F-01" });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/execution/form-submissions",
        expect.objectContaining({ method: "POST" })
      );

      await executionService.syncQueries([{ blockIndex: 0 }]);
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/execution/queries/sync",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ blocks: [{ blockIndex: 0 }] }),
        })
      );
    });
  });

  describe("eTMF Service Module", () => {
    it("routes documents list, retrieval, and ingest correctly through gateway contracts", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({}),
      });

      await etmfService.getDocuments();
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/etmf/documents",
        expect.objectContaining({ method: "GET" })
      );

      await etmfService.getDocument("DOC-01");
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/etmf/documents/DOC-01",
        expect.objectContaining({ method: "GET" })
      );

      await etmfService.ingestDocument({ file_name: "crf.pdf" });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/etmf/ingest",
        expect.objectContaining({ method: "POST" })
      );
    });
  });

  describe("Interop Service Module", () => {
    it("routes submit, sync, and instruments endpoints correctly through gateway contracts", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({}),
      });

      await interopService.submitEpro({ questionnaire: "Q-01" });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/interop/epro/submit",
        expect.objectContaining({ method: "POST" })
      );

      await interopService.syncEpro({ bundle: [] });
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/interop/epro/sync",
        expect.objectContaining({ method: "POST" })
      );

      await interopService.getInstruments("SUB-01");
      expect(mockFetch).toHaveBeenLastCalledWith(
        "http://localhost:8000/api/v1/interop/subjects/SUB-01/instruments",
        expect.objectContaining({ method: "GET" })
      );
    });
  });
});
