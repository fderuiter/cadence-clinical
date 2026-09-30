import { describe, it, expect, beforeEach, vi } from "vitest";

const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../src/stores/auth.js";
import {
  terminologyClient,
  TerminologyNetworkError,
} from "../src/api/terminologyClient.js";

describe("Terminology API Client Unit Tests", () => {
  beforeEach(() => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.accessToken = "mock-keycloak-jwt-token";
    authStore.isAuthenticated = true;
    mockFetch.mockReset();
  });

  const authOptions = {
    userId: "test-user-id",
    roles: "sponsor_dm,cra",
    changeReason: "Testing signed client",
  };

  describe("validateSingleCode", () => {
    it("should reject empty or whitespace codes", async () => {
      await expect(
        terminologyClient.validateSingleCode("", authOptions)
      ).rejects.toThrow("Concept code cannot be empty or whitespace.");

      await expect(
        terminologyClient.validateSingleCode("   ", authOptions)
      ).rejects.toThrow("Concept code cannot be empty or whitespace.");

      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("should successfully validate a concept code, target the gateway, and sign headers", async () => {
      const mockResult = {
        concept_code: "C12345",
        state: "VALID",
        decode: "Adverse Event",
        system: "NCI_Thesaurus",
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockResult), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.validateSingleCode(
        "C12345",
        authOptions
      );

      expect(result).toEqual(mockResult);
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const [callArg, options] = mockFetch.mock.calls[0];
      const url = typeof callArg === "string" ? callArg : callArg.url;
      const getHeader = (name) =>
        typeof callArg === "string"
          ? options?.headers?.[name]
          : callArg.headers.get(name);

      expect(url).toBe(
        "http://localhost:8000/api/v1/terminology/validate/C12345"
      );
      expect(getHeader("Authorization")).toBe("Bearer mock-keycloak-jwt-token");
      expect(getHeader("X-Change-Reason")).toBe("Testing signed client");
      expect(getHeader("X-User-Id")).toBeNull();
      expect(getHeader("X-Gateway-Signature")).toBeNull();
    });

    it("should handle invalid/malformed codes by returning the INVALID state", async () => {
      const mockResult = {
        concept_code: "NOT_A_CODE",
        state: "INVALID",
        error_message: "Concept code not found",
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockResult), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.validateSingleCode(
        "NOT_A_CODE",
        authOptions
      );

      expect(result.state).toBe("INVALID");
      expect(result).toEqual(mockResult);
    });

    it("should handle degraded/unavailable terminology service by returning the DEGRADED state", async () => {
      const mockResult = {
        concept_code: "C12345",
        state: "DEGRADED",
        error_message: "Upstream service timeout",
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockResult), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.validateSingleCode(
        "C12345",
        authOptions
      );

      expect(result.state).toBe("DEGRADED");
      expect(result).toEqual(mockResult);
    });

    it("should throw TerminologyNetworkError when gateway returns a 502/503/500 error", async () => {
      mockFetch.mockResolvedValueOnce(
        new Response(null, {
          status: 502,
          statusText: "Bad Gateway",
          headers: { "Content-Type": "application/json" },
        })
      );

      let thrownError = null;
      try {
        await terminologyClient.validateSingleCode("C12345", authOptions);
      } catch (err) {
        thrownError = err;
      }

      expect(thrownError).toBeInstanceOf(TerminologyNetworkError);
      expect(thrownError.status).toBe(502);
      expect(thrownError.statusText).toBe("Bad Gateway");
    });

    it("should throw TerminologyNetworkError on fetch network connection failure", async () => {
      mockFetch.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      let thrownError = null;
      try {
        await terminologyClient.validateSingleCode("C12345", authOptions);
      } catch (err) {
        thrownError = err;
      }

      expect(thrownError).toBeInstanceOf(TerminologyNetworkError);
      expect(thrownError.message).toContain("Failed to fetch");
    });
  });

  describe("searchTerminology", () => {
    it("should reject empty search terms", async () => {
      await expect(
        terminologyClient.searchTerminology("", authOptions)
      ).rejects.toThrow("Search term cannot be empty or whitespace.");

      expect(mockFetch).not.toHaveBeenCalled();
    });

    it("should successfully query search endpoint with pagination parameters", async () => {
      const mockResult = {
        results: [{ code: "C123", term: "Pain" }],
        total_count: 1,
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockResult), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.searchTerminology("Pain", {
        ...authOptions,
        fromRecord: 10,
        pageSize: 5,
      });

      expect(result).toEqual(mockResult);
      expect(mockFetch).toHaveBeenCalledTimes(1);

      const [callArg] = mockFetch.mock.calls[0];
      const url = typeof callArg === "string" ? callArg : callArg.url;
      const parsedUrl = new URL(url);
      expect(parsedUrl.origin + parsedUrl.pathname).toBe(
        "http://localhost:8000/api/v1/terminology/search"
      );
      expect(parsedUrl.searchParams.get("term")).toBe("Pain");
      expect(parsedUrl.searchParams.get("from_record")).toBe("10");
      expect(parsedUrl.searchParams.get("page_size")).toBe("5");
    });
  });

  describe("getStudyTerminologyValidation", () => {
    it("should reject empty study IDs", async () => {
      await expect(
        terminologyClient.getStudyTerminologyValidation("", authOptions)
      ).rejects.toThrow("Study ID cannot be empty.");
    });

    it("should retrieve validation report for a valid study", async () => {
      const mockReport = {
        study_id: "STUDY-001",
        is_valid: true,
        total_concepts: 10,
        concepts: [],
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockReport), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.getStudyTerminologyValidation(
        "STUDY-001",
        authOptions
      );

      expect(result).toEqual(mockReport);
      const [callArg1] = mockFetch.mock.calls[0];
      const url1 = typeof callArg1 === "string" ? callArg1 : callArg1.url;
      expect(url1).toBe(
        "http://localhost:8000/api/v1/studies/STUDY-001/terminology-validation"
      );
    });
  });

  describe("getStudyCtValidation", () => {
    it("should reject empty study IDs", async () => {
      await expect(
        terminologyClient.getStudyCtValidation("", authOptions)
      ).rejects.toThrow("Study ID cannot be empty.");
    });

    it("should retrieve CT validation report for a valid study", async () => {
      const mockReport = {
        study_id: "STUDY-001",
        is_valid: false,
        total_concepts: 5,
        concepts: [],
      };

      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify(mockReport), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        })
      );

      const result = await terminologyClient.getStudyCtValidation(
        "STUDY-001",
        authOptions
      );

      expect(result).toEqual(mockReport);
      const [callArg2] = mockFetch.mock.calls[0];
      const url2 = typeof callArg2 === "string" ? callArg2 : callArg2.url;
      expect(url2).toBe(
        "http://localhost:8000/api/v1/studies/STUDY-001/ct-validation"
      );
    });
  });
});
