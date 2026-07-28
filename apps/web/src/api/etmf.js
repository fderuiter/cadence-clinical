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
};
