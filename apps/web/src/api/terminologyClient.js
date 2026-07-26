import { generateGatewaySignature } from "ui";

const GATEWAY_URL =
  import.meta.env?.VITE_GATEWAY_URL || "http://localhost:8000";
const DEFAULT_SECRET = "internal-gateway-secret-12345"; // pragma: allowlist secret

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
 * Generates signed request headers for gateway authentication.
 */
async function getSignedHeaders({
  userId,
  roles,
  changeReason = "",
  secret = DEFAULT_SECRET,
}) {
  const timestamp = String(Date.now() / 1000);
  const signature = await generateGatewaySignature(
    userId,
    roles,
    timestamp,
    "2",
    changeReason,
    secret
  );

  return {
    "Content-Type": "application/json",
    "X-User-Id": userId,
    "X-User-Roles": roles,
    "X-Gateway-Timestamp": timestamp,
    "X-Gateway-Signature": signature,
    "X-Signature-Version": "2",
    ...(changeReason ? { "X-Change-Reason": changeReason } : {}),
  };
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
   * @param {Object} auth - Gateway identity options.
   * @returns {Promise<Object>} The validation report containing state and decode/system fields.
   */
  async validateSingleCode(
    code,
    { userId, roles, changeReason = "Validate code" }
  ) {
    if (!code || !code.trim()) {
      throw new Error("Concept code cannot be empty or whitespace.");
    }

    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const url = `${GATEWAY_URL}/api/v1/terminology/validate/${encodeURIComponent(code)}`;

    try {
      const response = await fetch(url, { headers });
      if (!response.ok) {
        throw new TerminologyNetworkError(
          `Request failed with status ${response.status}`,
          response.status,
          response.statusText
        );
      }
      return await response.json();
    } catch (error) {
      if (error instanceof TerminologyNetworkError) {
        throw error;
      }
      throw new TerminologyNetworkError(
        error.message || "Network error occurred"
      );
    }
  },

  /**
   * Performs text-based autocomplete and search queries on terminology concepts.
   *
   * @param {string} term - The search/autocomplete query term.
   * @param {Object} options - Search configuration and identity.
   * @returns {Promise<Object>} A paginated list of search results.
   */
  async searchTerminology(
    term,
    { fromRecord, pageSize, userId, roles, changeReason = "Search terminology" }
  ) {
    if (!term || !term.trim()) {
      throw new Error("Search term cannot be empty or whitespace.");
    }

    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const queryParams = new URLSearchParams({ term });
    if (fromRecord !== undefined && fromRecord !== null) {
      queryParams.append("from_record", String(fromRecord));
    }
    if (pageSize !== undefined && pageSize !== null) {
      queryParams.append("page_size", String(pageSize));
    }

    const url = `${GATEWAY_URL}/api/v1/terminology/search?${queryParams.toString()}`;

    try {
      const response = await fetch(url, { headers });
      if (!response.ok) {
        throw new TerminologyNetworkError(
          `Request failed with status ${response.status}`,
          response.status,
          response.statusText
        );
      }
      return await response.json();
    } catch (error) {
      if (error instanceof TerminologyNetworkError) {
        throw error;
      }
      throw new TerminologyNetworkError(
        error.message || "Network error occurred"
      );
    }
  },

  /**
   * Fetches an aggregated terminology validation report for an entire clinical study.
   *
   * @param {string} studyId - The unique study identifier.
   * @param {Object} auth - Gateway identity options.
   * @returns {Promise<Object>} The study terminology validation report.
   */
  async getStudyTerminologyValidation(
    studyId,
    { userId, roles, changeReason = "Get study terminology validation" }
  ) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }

    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const url = `${GATEWAY_URL}/api/v1/studies/${encodeURIComponent(studyId)}/terminology-validation`;

    try {
      const response = await fetch(url, { headers });
      if (!response.ok) {
        throw new TerminologyNetworkError(
          `Request failed with status ${response.status}`,
          response.status,
          response.statusText
        );
      }
      return await response.json();
    } catch (error) {
      if (error instanceof TerminologyNetworkError) {
        throw error;
      }
      throw new TerminologyNetworkError(
        error.message || "Network error occurred"
      );
    }
  },

  /**
   * Fetches an aggregated controlled terminology (CT) validation report for an entire clinical study.
   *
   * @param {string} studyId - The unique study identifier.
   * @param {Object} auth - Gateway identity options.
   * @returns {Promise<Object>} The study controlled terminology validation report.
   */
  async getStudyCtValidation(
    studyId,
    { userId, roles, changeReason = "Get study CT validation" }
  ) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }

    const headers = await getSignedHeaders({ userId, roles, changeReason });
    const url = `${GATEWAY_URL}/api/v1/studies/${encodeURIComponent(studyId)}/ct-validation`;

    try {
      const response = await fetch(url, { headers });
      if (!response.ok) {
        throw new TerminologyNetworkError(
          `Request failed with status ${response.status}`,
          response.status,
          response.statusText
        );
      }
      return await response.json();
    } catch (error) {
      if (error instanceof TerminologyNetworkError) {
        throw error;
      }
      throw new TerminologyNetworkError(
        error.message || "Network error occurred"
      );
    }
  },
};
