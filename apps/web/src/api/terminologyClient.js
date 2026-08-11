import { apiClient } from "./apiClient";
import { TerminologyNetworkError } from "shared-api-client";

export { TerminologyNetworkError };

export const terminologyClient = {
  async validateSingleCode(code, options = {}) {
    if (!code || !code.trim()) {
      throw new Error("Concept code cannot be empty or whitespace.");
    }
    const changeReason = options.changeReason || "Validate code";
    try {
      return await apiClient.get(
        `/api/v1/terminology/validate/${encodeURIComponent(code)}`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async searchTerminology(term, options = {}) {
    if (!term || !term.trim()) {
      throw new Error("Search term cannot be empty or whitespace.");
    }
    const {
      fromRecord,
      pageSize,
      changeReason = "Search terminology",
    } = options;
    const queryParams = new URLSearchParams({ term });
    if (fromRecord !== undefined && fromRecord !== null) {
      queryParams.append("from_record", String(fromRecord));
    }
    if (pageSize !== undefined && pageSize !== null) {
      queryParams.append("page_size", String(pageSize));
    }
    try {
      return await apiClient.get(
        `/api/v1/terminology/search?${queryParams.toString()}`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyTerminologyValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason =
      options.changeReason || "Get study terminology validation";
    try {
      return await apiClient.get(
        `/api/v1/studies/${encodeURIComponent(studyId)}/terminology-validation`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyCtValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason = options.changeReason || "Get study CT validation";
    try {
      return await apiClient.get(
        `/api/v1/studies/${encodeURIComponent(studyId)}/ct-validation`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
};
