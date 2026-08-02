import { apiClient } from "./apiClient";

/**
 * Service module for the Event-Driven eTMF (Electronic Trial Master File) microservice.
 * Interfaces with document indexing, completeness metrics, and document ingestion.
 */
export const etmfService = {
  /**
   * Retrieves the list of document metadata.
   */
  getDocuments(params = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.zone !== undefined) query.append("zone", params.zone);
    if (params.search) query.append("search", params.search);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/etmf/documents?${queryString}`
      : "/api/v1/etmf/documents";
    return apiClient.get(path);
  },

  /**
   * Retrieves detail for a single eTMF document.
   */
  getDocument(documentId, options = {}) {
    return apiClient.get(`/api/v1/etmf/documents/${documentId}`, options);
  },

  /**
   * Ingests a new document into the eTMF.
   */
  ingestDocument(body, options = {}) {
    return apiClient.post(`/api/v1/etmf/ingest`, body, options);
  },

  /**
   * Retrieves the eTMF completeness tracking dashboard metrics.
   */
  getCompleteness(params = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.milestone) query.append("milestone", params.milestone);
    if (params.site_id) query.append("site_id", params.site_id);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/etmf/completeness?${queryString}`
      : "/api/v1/etmf/completeness";
    return apiClient.get(path);
  },

  /**
   * Verifies re-supplied credentials to obtain a short-lived signature token (sig_token).
   */
  async verifySignature({ username, password, totp = null, action }) {
    return apiClient.post("/api/v1/auth/signature-verification", {
      username,
      password,
      totp,
      action,
    });
  },

  /**
   * Performs the electronic signature and approval on an eTMF document.
   */
  async signDocument(
    documentId,
    { signingReason },
    { changeReason, sigToken }
  ) {
    return apiClient.post(
      `/api/v1/etmf/documents/${documentId}/sign-off`,
      { signing_reason: signingReason },
      {
        changeReason,
        headers: {
          "X-Sig-Token": sigToken,
        },
      }
    );
  },

  /**
   * Retrieves the status of an eConsent template version archival delivery to eTMF.
   */
  getArchivalStatus(correlationId, params = {}) {
    if (correlationId) {
      return apiClient.get(`/api/v1/econsent/archival-status/${correlationId}`);
    }
    const query = new URLSearchParams();
    if (params.template_id) query.append("template_id", params.template_id);
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    if (params.subject_pseudonym)
      query.append("subject_pseudonym", params.subject_pseudonym);
    return apiClient.get(
      `/api/v1/econsent/archival-status?${query.toString()}`
    );
  },

  /**
   * Retrieves the browsable eTMF taxonomy tree.
   * @param {string} [version] - Optional taxonomy version.
   * @param {Object} [options] - Additional request options.
   * @returns {Promise<Object>} The taxonomy catalog tree.
   */
  getTaxonomy(version, options = {}) {
    const query = new URLSearchParams();
    if (version !== undefined && version !== null) {
      query.append("version", version);
    }
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/etmf/taxonomy?${queryString}`
      : "/api/v1/etmf/taxonomy";
    return apiClient.get(path, options);
  },

  /**
   * Provides automatic classification/auto-filing suggestions for a document.
   * @param {Object} payload - Ingestion parameters including filename.
   * @param {Object} [options] - Additional request options.
   * @returns {Promise<Object>} The auto-classification result.
   */
  autoFile(payload, options = {}) {
    return apiClient.post("/api/v1/etmf/classify", payload, options);
  },

  /**
   * Mutates the taxonomy classification/tags for an existing document.
   * @param {string} documentId - The unique document identifier.
   * @param {Object} payload - The new classification tag details.
   * @param {Object} [options] - Additional options including changeReason.
   * @returns {Promise<Object>} The updated document metadata.
   */
  tagDocument(documentId, payload, options = {}) {
    return apiClient.post(
      `/api/v1/etmf/documents/${documentId}/classify`,
      payload,
      options
    );
  },
};
