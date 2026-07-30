import { apiClient, getBaseUrl } from "./apiClient";

/**
 * Service module for the Auditor/Inspection operations.
 * Interfaces with eTMF audit logs, execution ledger integrity, and binder exports.
 */
export const auditorService = {
  /**
   * Retrieves eTMF audit log events.
   * Supports filtering and paging.
   */
  getAuditLogs(params = {}) {
    // build query parameters
    const query = new URLSearchParams();
    if (params.user_id) query.append("user_id", params.user_id);
    if (params.action) query.append("action", params.action);
    if (params.document_id) query.append("document_id", params.document_id);
    if (params.start_time) query.append("start_time", params.start_time);
    if (params.end_time) query.append("end_time", params.end_time);
    if (params.limit !== undefined) query.append("limit", params.limit);
    if (params.offset !== undefined) query.append("offset", params.offset);

    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/etmf/audit-logs?${queryString}`
      : "/api/v1/etmf/audit-logs";
    return apiClient.get(path);
  },

  /**
   * Retrieves the execution ledger integrity verification.
   * Prepared for the planned execution audit endpoint.
   */
  getExecutionIntegrity() {
    return apiClient.get("/api/v1/execution/audit/integrity");
  },

  /**
   * Generates watermarked content path or options.
   * Watermarked document viewing is restricted to auditor/inspector roles.
   */
  getWatermarkedDownloadUrl(documentId) {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/documents/${documentId}/watermark`;
  },

  /**
   * Initiates ZIP regulatory binder download for a clinical study.
   */
  getBinderExportUrl(studyId, includeHistory = false) {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/studies/${studyId}/binder?include_history=${includeHistory}`;
  },
};
