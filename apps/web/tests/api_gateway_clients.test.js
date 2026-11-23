import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth";
import { apiClient } from "../src/api/apiClient";
import { designerService } from "../src/api/designer";
import { etmfService } from "../src/api/etmf";
import { interopService } from "../src/api/interop";
import apiService from "../src/services/api";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

const lastRequest = () =>
  recordedRequest(mockFetch, mockFetch.mock.calls.length - 1);

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

      mockFetch.mockResolvedValueOnce(jsonResponse({ status: "ok" }));

      const response = await apiClient.get("/health");
      expect(response).toEqual({ status: "ok" });
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const { url } = await recordedRequest(mockFetch, 0);
      expect(url).toBe("http://localhost:8000/health");
    });

    it("should honor custom base URL from VITE_API_BASE_URL", async () => {
      if (import.meta.env) {
        import.meta.env.VITE_API_BASE_URL =
          "https://my-secure-gateway.internal.com";
      }

      mockFetch.mockResolvedValueOnce(jsonResponse({ status: "custom" }));

      const response = await apiClient.get("/health");
      expect(response).toEqual({ status: "custom" });
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const { url } = await recordedRequest(mockFetch, 0);
      expect(url).toBe("https://my-secure-gateway.internal.com/health");
    });

    it("should attach Bearer tokens automatically from Pinia authStore", async () => {
      authStore.accessToken = "mock-keycloak-jwt-token";
      authStore.isAuthenticated = true;

      mockFetch.mockResolvedValueOnce(jsonResponse({ user: "fderuiter" }));

      await apiClient.get("/me");

      const { headers } = await recordedRequest(mockFetch, 0);
      expect(headers.get("Authorization")).toBe(
        "Bearer mock-keycloak-jwt-token"
      );
    });

    it("should append X-Change-Reason for mutations when changeReason option is specified", async () => {
      mockFetch.mockResolvedValueOnce(
        jsonResponse({ id: "123" }, { status: 201 })
      );

      await apiClient.post(
        "/api/v1/mutations",
        { name: "test" },
        { changeReason: "Updating record" }
      );

      const { headers } = await recordedRequest(mockFetch, 0);
      expect(headers.get("X-Change-Reason")).toBe("Updating record");
    });

    it("should not append X-Change-Reason for GET/read operations even if changeReason option is specified", async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse([]));

      await apiClient.get("/api/v1/data", { changeReason: "Get data" });

      const { headers } = await recordedRequest(mockFetch, 0);
      expect(headers.has("X-Change-Reason")).toBe(false);
    });

    it("should throw ApiError with custom response body details when request fails", async () => {
      mockFetch.mockResolvedValueOnce(
        jsonResponse(
          { detail: "IMMUTABILITY_VIOLATION" },
          { status: 403, statusText: "Forbidden" }
        )
      );

      await expect(apiClient.post("/api/v1/mutate", {})).rejects.toThrow(
        "IMMUTABILITY_VIOLATION"
      );
    });
  });

  describe("Designer Service Module", () => {
    it("routes study, rules, and concepts queries correctly through gateway contracts", async () => {
      let request;
      // A Response body can only be read once, so build a fresh one per call.
      mockFetch.mockImplementation(async () =>
        jsonResponse({ id: "STUDY-01" })
      );

      await designerService.getStudy("STUDY-01");
      request = await lastRequest();
      expect(request.url).toBe("http://localhost:8000/api/v1/studies/STUDY-01");
      expect(request.method).toBe("GET");

      await designerService.createStudyVersion("STUDY-01", { version: "v1" });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/studies/STUDY-01/versions"
      );
      expect(request.method).toBe("POST");
      expect(request.body).toEqual({ version: "v1" });

      await designerService.getRules("STUDY-01");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/studies/STUDY-01/rules"
      );
      expect(request.method).toBe("GET");

      await designerService.getConcepts();
      request = await lastRequest();
      expect(request.url).toBe("http://localhost:8000/api/v1/mdr/concepts");
      expect(request.method).toBe("GET");
    });
  });

  describe("eTMF Service Module", () => {
    it("routes documents list, retrieval, and ingest correctly through gateway contracts", async () => {
      let request;
      // A Response body can only be read once, so build a fresh one per call.
      mockFetch.mockImplementation(async () => jsonResponse({}));

      await etmfService.getDocuments();
      request = await lastRequest();
      expect(request.url).toBe("http://localhost:8000/api/v1/etmf/documents");
      expect(request.method).toBe("GET");

      await etmfService.getDocument("DOC-01");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/documents/DOC-01"
      );
      expect(request.method).toBe("GET");

      await etmfService.ingestDocument({ file_name: "crf.pdf" });
      request = await lastRequest();
      expect(request.url).toBe("http://localhost:8000/api/v1/etmf/ingest");
      expect(request.method).toBe("POST");
    });

    it("routes taxonomy retrieval, taxonomy updates, QC submission, and binary downloads through mapped .NET contracts", async () => {
      let request;
      authStore.accessToken = "etmf-bearer-token";
      authStore.isAuthenticated = true;
      mockFetch.mockImplementation(async () => jsonResponse({}));

      // 1. getTaxonomy without version
      await etmfService.getTaxonomy();
      request = await lastRequest();
      expect(request.url).toBe("http://localhost:8000/api/v1/etmf/taxonomy");
      expect(request.method).toBe("GET");

      // 2. getTaxonomy with version
      await etmfService.getTaxonomy("v3.2.0-complete");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/taxonomy?version=v3.2.0-complete"
      );
      expect(request.method).toBe("GET");

      // 3. getDocuments with study_id, site_id & zone scope
      await etmfService.getDocuments({
        study_id: "CADENCE-101",
        site_id: "SITE-01",
        zone: "5",
      });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/documents?study_id=CADENCE-101&site_id=SITE-01&zone=5"
      );
      expect(request.method).toBe("GET");

      // 4. tagDocument mapped to PATCH /api/v1/etmf/documents/{document_id}/taxonomy
      const tagPayload = {
        zone: 5,
        section: "05.02",
        artifact_code: "05.02.03",
        artifact_type: "Investigator CV",
      };
      await etmfService.tagDocument("DOC-01", tagPayload, {
        changeReason: "Manual re-classification",
      });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/documents/DOC-01/taxonomy"
      );
      expect(request.method).toBe("PATCH");
      expect(request.body).toEqual({
        zone: 5,
        section: "05.02",
        artifactCode: "05.02.03",
        artifactType: "Investigator CV",
        reasonForChange: "Manual re-classification",
      });
      expect(request.headers.get("X-Change-Reason")).toBe(
        "Manual re-classification"
      );

      // 5. submitDocumentForQc mapped to POST /api/v1/etmf/documents/{document_id}/submit-qc
      await etmfService.submitDocumentForQc("DOC-01", {
        reason_for_change: "Submitting draft protocol to Technical QC",
      });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/documents/DOC-01/submit-qc"
      );
      expect(request.method).toBe("POST");
      expect(request.headers.get("X-Change-Reason")).toBe(
        "Submitting draft protocol to Technical QC"
      );

      // 6. downloadDocumentContent & downloadExportJob include Authorization: Bearer
      mockFetch.mockResolvedValueOnce(
        new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46]), {
          status: 200,
          headers: {
            "Content-Type": "application/pdf",
            "X-Content-SHA256": "abc123sha256",
          },
        })
      );
      const streamRes = await etmfService.downloadDocumentContent("DOC-01");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/documents/DOC-01/content"
      );
      expect(request.headers.get("Authorization")).toBe(
        "Bearer etmf-bearer-token"
      );
      expect(streamRes.sha256).toBe("abc123sha256");

      mockFetch.mockResolvedValueOnce(
        new Response(new Uint8Array([0x50, 0x4b, 0x03, 0x04]), {
          status: 200,
          headers: {
            "Content-Type": "application/zip",
          },
        })
      );
      const exportArchive = await etmfService.downloadExportJob("EXP-01");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/etmf/export-jobs/EXP-01/download"
      );
      expect(request.headers.get("Authorization")).toBe(
        "Bearer etmf-bearer-token"
      );
      expect(exportArchive.blob.size).toBe(4);
    });
  });

  describe("Interop Service Module", () => {
    it("routes submit, sync, and instruments endpoints correctly through gateway contracts", async () => {
      let request;
      // A Response body can only be read once, so build a fresh one per call.
      mockFetch.mockImplementation(async () => jsonResponse({}));

      await interopService.submitEpro({ questionnaire: "Q-01" });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/interop/epro/submit"
      );
      expect(request.method).toBe("POST");

      await interopService.syncEpro({ bundle: [] });
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/interop/epro/sync"
      );
      expect(request.method).toBe("POST");

      await interopService.getInstruments("SUB-01");
      request = await lastRequest();
      expect(request.url).toBe(
        "http://localhost:8000/api/v1/interop/subjects/SUB-01/instruments"
      );
      expect(request.method).toBe("GET");
    });
  });

  describe("services/api wrapper", () => {
    it("should re-export apiClient correctly as the default export", () => {
      expect(apiService).toBe(apiClient);
    });
  });

  describe("createOpenApiClient circular module initialization resilience", () => {
    it("dynamically rewrites default baseUrl when VITE_API_BASE_URL is populated after client creation", async () => {
      const { createOpenApiClient } = await import("../src/api/client");
      if (import.meta.env) {
        delete import.meta.env.VITE_API_BASE_URL;
      }

      const defaultClient = createOpenApiClient();
      const explicitClient = createOpenApiClient("https://pinned.example.org");

      if (import.meta.env) {
        import.meta.env.VITE_API_BASE_URL = "http://localhost:3000";
      }

      mockFetch.mockImplementation(async () => jsonResponse({ status: "ok" }));

      await defaultClient.GET("/health");
      const defaultReq = await lastRequest();
      expect(defaultReq.url).toBe("http://localhost:3000/health");

      await defaultClient.POST("/api/v1/etmf/ingest", {
        body: { file_name: "source.pdf" },
        changeReason: "Typographical Error: source reads 128",
      });
      const postReq = await lastRequest();
      expect(postReq.url).toBe("http://localhost:3000/api/v1/etmf/ingest");
      expect(postReq.method).toBe("POST");
      expect(postReq.body).toEqual({ file_name: "source.pdf" });
      expect(postReq.headers.get("X-Change-Reason")).toBe(
        "Typographical Error: source reads 128"
      );

      await explicitClient.GET("/health");
      const explicitReq = await lastRequest();
      expect(explicitReq.url).toBe("https://pinned.example.org/health");
    });
  });
});
