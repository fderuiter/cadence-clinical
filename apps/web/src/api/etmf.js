import { apiClient } from "./apiClient";

export const etmfService = {
  getDocuments(options = {}) {
    const { status, limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (status) params.append("status", status);
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? `/api/v1/etmf/documents?${queryString}`
      : "/api/v1/etmf/documents";
    return apiClient.get(path, rest);
  },
  getDocument(documentId, options = {}) {
    return apiClient.get(`/api/v1/etmf/documents/${documentId}`, options);
  },
  ingestDocument(body, options = {}) {
    return apiClient.post(`/api/v1/etmf/ingest`, body, options);
  },
  getCompleteness(options = {}) {
    const { study_id, site_id, ...rest } = options;
    const params = new URLSearchParams();
    if (study_id) params.append("study_id", study_id);
    if (site_id) params.append("site_id", site_id);
    const queryString = params.toString();
    const path = queryString
      ? `/api/v1/etmf/completeness?${queryString}`
      : "/api/v1/etmf/completeness";
    return apiClient.get(path, rest);
  },
  verifySignature(payload, options = {}) {
    return apiClient.post("/api/v1/auth/signature-verification", payload, options);
  },
  signOff(documentId, payload, options = {}) {
    return apiClient.post(`/api/v1/etmf/documents/${documentId}/sign-off`, payload, options);
  },
  getArchivalStatus(correlationId, options = {}) {
    return apiClient.get(`/api/v1/econsent/archival-status/${correlationId}`, options);
  },
  getArchivalStatuses(options = {}) {
    const { limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? `/api/v1/econsent/archival-status?${queryString}`
      : "/api/v1/econsent/archival-status";
    return apiClient.get(path, rest);
  },
  getTaxonomy(version, options = {}) {
    const path = version ? `/api/v1/etmf/taxonomy?version=${version}` : "/api/v1/etmf/taxonomy";
    return apiClient.get(path, options);
  },
  autoFile(payload, options = {}) {
    return apiClient.post("/api/v1/etmf/classify", payload, options);
  },
  tagDocument(documentId, payload, options = {}) {
    return apiClient.post(`/api/v1/etmf/documents/${documentId}/classify`, payload, options);
  },
};
