import { apiClient } from "./apiClient";

/**
 * Custom error class representing network, gateway, or service availability issues.
 * This distinguishes transmission and server failures from logical terminology states (VALID/INVALID/DEGRADED).
 */
export class TerminologyNetworkError extends Error {
  constructor(message, status = null, statusText = null) {
    super(message);
    this.name = "TerminologyNetworkError";
    this.status = status;
    this.statusText = statusText;
  }
}

/**
 * API client for interacting with terminology, validation, and search endpoints through the API Gateway.
 */
export const terminologyClient = {
  /**
   * Validates a single controlled terminology concept code.
   * Returns a report containing the validation state (VALID, INVALID, DEGRADED) and concept details.
   *
   * @param {string} code - The concept code to validate.
   * @param {Object} [options] - Optional identity or request options.
   * @returns {Promise<Object>} The validation report containing state and decode/system fields.
   */
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

  /**
   * Performs text-based autocomplete and search queries on terminology concepts.
   *
   * @param {string} term - The search/autocomplete query term.
   * @param {Object} [options] - Search configuration.
   * @returns {Promise<Object>} A paginated list of search results.
   */
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

  /**
   * Fetches an aggregated terminology validation report for an entire clinical study.
   *
   * @param {string} studyId - The unique study identifier.
   * @param {Object} [options] - Optional request options.
   * @returns {Promise<Object>} The study terminology validation report.
   */
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

  /**
   * Fetches an aggregated controlled terminology (CT) validation report for an entire clinical study.
   *
   * @param {string} studyId - The unique study identifier.
   * @param {Object} [options] - Optional request options.
   * @returns {Promise<Object>} The study controlled terminology validation report.
   */
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
