import { apiClient } from "./apiClient";

/**
 * Service module for the Event-Driven eTMF (Electronic Trial Master File) microservice.
 * Interfaces with document indexing, completeness metrics, and document ingestion.
 */
export const etmfService = {
  /**
   * Retrieves the list of document metadata.
   */
  getDocuments(options = {}) {
    return apiClient.get(`/api/v1/etmf/documents`, options);
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
  getCompleteness(options = {}) {
    return apiClient.get(`/api/v1/etmf/completeness`, options);
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
};
