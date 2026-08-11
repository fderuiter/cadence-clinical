import { apiClient, getBaseUrl } from "./apiClient";

export const auditorService = {
  getAuditLogs(params = {}) {
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
  getExecutionIntegrity() {
    return apiClient.get("/api/v1/execution/audit/integrity");
  },
  getWatermarkedDownloadUrl(documentId) {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/documents/${documentId}/watermark`;
  },
  getBinderExportUrl(studyId, includeHistory = false) {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/studies/${studyId}/binder?include_history=${includeHistory}`;
  },
};
