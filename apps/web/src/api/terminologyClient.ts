import { openApiClient, unwrapResponse } from "./client";

export class TerminologyNetworkError extends Error {
  status: number | null;
  statusText: string | null;

  constructor(message: string, status: number | null = null, statusText: string | null = null) {
    super(message);
    this.name = "TerminologyNetworkError";
    this.status = status;
    this.statusText = statusText;
  }
}

export interface SearchTerminologyOptions {
  fromRecord?: number;
  pageSize?: number;
  changeReason?: string;
}

export const terminologyClient = {
  async validateSingleCode(code: string, options: { changeReason?: string } = {}) {
    if (!code || !code.trim()) {
      throw new Error("Concept code cannot be empty or whitespace.");
    }

    const changeReason = options.changeReason || "Validate code";

    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/terminology/validate/{code}", {
          params: { path: { code } },
          headers: { "X-Change-Reason": changeReason },
        })
      );
    } catch (error: unknown) {
      const err = error as { message?: string; status?: number; statusText?: string };
      throw new TerminologyNetworkError(
        err.message || "Network error occurred",
        err.status ?? null,
        err.statusText ?? null
      );
    }
  },

  async searchTerminology(term: string, options: SearchTerminologyOptions = {}) {
    if (!term || !term.trim()) {
      throw new Error("Search term cannot be empty or whitespace.");
    }

    const {
      fromRecord,
      pageSize,
      changeReason = "Search terminology",
    } = options;

    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/terminology/search", {
          params: {
            query: {
              term,
              from_record: fromRecord,
              page_size: pageSize,
            },
          },
          headers: { "X-Change-Reason": changeReason },
        })
      );
    } catch (error: unknown) {
      const err = error as { message?: string; status?: number; statusText?: string };
      throw new TerminologyNetworkError(
        err.message || "Network error occurred",
        err.status ?? null,
        err.statusText ?? null
      );
    }
  },

  async getStudyTerminologyValidation(studyId: string, options: { changeReason?: string } = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }

    const changeReason =
      options.changeReason || "Get study terminology validation";

    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/studies/{study_id}/terminology-validation", {
          params: { path: { study_id: studyId } },
          headers: { "X-Change-Reason": changeReason },
        })
      );
    } catch (error: unknown) {
      const err = error as { message?: string; status?: number; statusText?: string };
      throw new TerminologyNetworkError(
        err.message || "Network error occurred",
        err.status ?? null,
        err.statusText ?? null
      );
    }
  },

  async getStudyCtValidation(studyId: string, options: { changeReason?: string } = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }

    const changeReason = options.changeReason || "Get study CT validation";

    try {
      return await unwrapResponse(
        openApiClient.GET("/api/v1/studies/{study_id}/ct-validation", {
          params: { path: { study_id: studyId } },
          headers: { "X-Change-Reason": changeReason },
        })
      );
    } catch (error: unknown) {
      const err = error as { message?: string; status?: number; statusText?: string };
      throw new TerminologyNetworkError(
        err.message || "Network error occurred",
        err.status ?? null,
        err.statusText ?? null
      );
    }
  },
};
