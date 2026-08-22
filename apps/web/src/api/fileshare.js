import { apiClient, getBaseUrl } from "./apiClient";

/**
 * Service module for Fileshare and Media Storage microservice.
 * Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
 */
export const fileshareService = {
  /**
   * List files across studies or filtered by study/site/query with pagination.
   */
  async listFiles(params = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.site_id) query.append("site_id", params.site_id);
    if (params.q) query.append("q", params.q);
    if (params.page !== undefined) query.append("page", String(params.page));
    if (params.page_size !== undefined) query.append("page_size", String(params.page_size));

    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/fileshare/files?${queryString}`
      : "/api/v1/fileshare/files";
    return apiClient.get(path);
  },

  /**
   * Retrieve file record metadata.
   */
  async getFileRecord(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}`);
  },

  /**
   * Retrieve presigned short-lived download URL.
   */
  async getDownloadUrl(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}/download-url`);
  },

  /**
   * Create an internal share grant delegation.
   */
  async createShareGrant(fileId, payload) {
    return apiClient.post(`/api/v1/fileshare/files/${fileId}/grants`, payload, {
      changeReason: payload.reason_for_change || "Delegating internal file access",
    });
  },

  /**
   * Revoke an internal share grant.
   */
  async deleteShareGrant(fileId, grantId, options = {}) {
    const reason = options.reason_for_change || "Revoking share grant delegation";
    const query = new URLSearchParams({ reason_for_change: reason });
    return apiClient.delete(
      `/api/v1/fileshare/files/${fileId}/grants/${grantId}?${query.toString()}`,
      { changeReason: reason }
    );
  },

  /**
   * List active internal share grants for a file record.
   */
  async listShareGrants(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}/grants`);
  },

  /**
   * Create an external time-bounded guest link.
   */
  async createGuestLink(fileId, payload) {
    return apiClient.post(`/api/v1/fileshare/files/${fileId}/guest-links`, payload, {
      changeReason: payload.reason_for_change || "Generating external guest link",
    });
  },

  /**
   * Revoke an external guest link.
   */
  async revokeGuestLink(fileId, guestLinkId, options = {}) {
    const reason = options.reason_for_change || "Revoking external guest link";
    const query = new URLSearchParams({ reason_for_change: reason });
    return apiClient.delete(
      `/api/v1/fileshare/files/${fileId}/guest-links/${guestLinkId}?${query.toString()}`,
      { changeReason: reason }
    );
  },

  /**
   * List active guest links for a file record.
   */
  async listGuestLinks(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}/guest-links`);
  },

  /**
   * Get full guest access URL for sharing.
   */
  getGuestShareUrl(token) {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/share/${token}`;
  },

  /**
   * Place a file under retention/legal hold.
   */
  async setHold(fileId, payload) {
    return apiClient.post(`/api/v1/fileshare/files/${fileId}/hold`, payload, {
      changeReason: payload.reason_for_change || "Placing file on retention hold",
    });
  },

  /**
   * Remove retention/legal hold from a file.
   */
  async clearHold(fileId, payload = {}) {
    const reason = payload.reason_for_change || "Removing retention hold";
    const query = new URLSearchParams({ reason_for_change: reason });
    return apiClient.delete(`/api/v1/fileshare/files/${fileId}/hold?${query.toString()}`, {
      changeReason: reason,
    });
  },

  /**
   * Delete a file record (gated on hold).
   */
  async deleteFile(fileId, options = {}) {
    const reason = options.reason_for_change || "Deleting file record";
    const query = new URLSearchParams({ reason_for_change: reason });
    return apiClient.delete(`/api/v1/fileshare/files/${fileId}?${query.toString()}`, {
      changeReason: reason,
    });
  },

  /**
   * List audit log entries for a file record.
   */
  async listAuditLogs(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}/audit-logs`);
  },

  /**
   * Retrieve short-lived presigned HLS master manifest stream URL.
   * Returns 200 with stream_url when ready, or 202 when transcode is pending/in-progress.
   */
  async getStreamUrl(fileId) {
    return apiClient.get(`/api/v1/fileshare/files/${fileId}/stream-url`);
  },

  /**
   * Record video playback checkpoint audit telemetry event (21 CFR Part 11).
   */
  async recordPlaybackAudit(payload) {
    return apiClient.post("/api/v1/fileshare/audit/playback", payload, {
      changeReason: payload.reason_for_change || "Video playback checkpoint audit event",
    });
  },
};

