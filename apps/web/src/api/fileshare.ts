import { openApiClient, unwrapResponse, getBaseUrl } from "./client";
import type { paths } from "./types";

export interface ListFilesParams {
  study_id?: string;
  site_id?: string;
  q?: string;
  page?: number;
  page_size?: number;
}

export interface ActivityFeedParams {
  event_type?: string;
  from_date?: string;
  to_date?: string;
  page?: number;
  page_size?: number;
}

export const fileshareService = {
  /**
   * List files across studies or filtered by study/site/query with pagination.
   */
  listFiles(params: ListFilesParams = {}, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files", {
        params: {
          query: {
            study_id: params.study_id,
            site_id: params.site_id,
            q: params.q,
            page: params.page,
            page_size: params.page_size,
          },
        },
        ...options,
      })
    );
  },

  /**
   * Retrieve file record metadata.
   */
  getFileRecord(fileId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}", {
        params: { path: { file_id: fileId } },
        ...options,
      })
    );
  },

  /**
   * Retrieve presigned short-lived download URL.
   */
  getDownloadUrl(fileId: string, options: { original?: boolean } = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/download-url", {
        params: {
          path: { file_id: fileId },
          query: { original: options.original },
        },
      })
    );
  },

  /**
   * Retrieve presigned short-lived preview URL (PDF rendition if available).
   */
  getPreviewUrl(fileId: string, options: { original?: boolean } = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/preview-url", {
        params: {
          path: { file_id: fileId },
          query: { original: options.original },
        },
      })
    );
  },

  /**
   * Create an internal share grant delegation.
   */
  createShareGrant(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Delegating internal file access";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/grants", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/grants"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Revoke an internal share grant.
   */
  deleteShareGrant(fileId: string, grantId: string, options: { reason_for_change?: string } = {}) {
    const reason = options.reason_for_change || "Revoking share grant delegation";
    return unwrapResponse(
      openApiClient.DELETE("/api/v1/fileshare/files/{file_id}/grants/{grant_id}", {
        params: {
          path: { file_id: fileId, grant_id: grantId },
          query: { reason_for_change: reason },
        },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /**
   * List active internal share grants for a file record.
   */
  listShareGrants(fileId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/grants", {
        params: { path: { file_id: fileId } },
        ...options,
      })
    );
  },

  /**
   * Create an external time-bounded guest link.
   */
  createGuestLink(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Generating external guest link";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/guest-links", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/guest-links"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Revoke an external guest link.
   */
  revokeGuestLink(fileId: string, guestLinkId: string, options: { reason_for_change?: string } = {}) {
    const reason = options.reason_for_change || "Revoking external guest link";
    return unwrapResponse(
      openApiClient.DELETE("/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}", {
        params: {
          path: { file_id: fileId, guest_link_id: guestLinkId },
          query: { reason_for_change: reason },
        },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /**
   * List active guest links for a file record.
   */
  listGuestLinks(fileId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/guest-links", {
        params: { path: { file_id: fileId } },
        ...options,
      })
    );
  },

  /**
   * Get full guest access URL for sharing.
   */
  getGuestShareUrl(token: string): string {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/share/${token}`;
  },

  /**
   * Place a file under retention/legal hold.
   */
  setHold(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Placing file on retention hold";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/hold", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/hold"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Remove retention/legal hold from a file.
   */
  clearHold(fileId: string, payload: { reason_for_change?: string } = {}) {
    const reason = payload.reason_for_change || "Removing retention hold";
    return unwrapResponse(
      openApiClient.DELETE("/api/v1/fileshare/files/{file_id}/hold", {
        params: {
          path: { file_id: fileId },
          query: { reason_for_change: reason },
        },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /**
   * Delete a file record (gated on hold).
   */
  deleteFile(fileId: string, options: { reason_for_change?: string } = {}) {
    const reason = options.reason_for_change || "Deleting file record";
    return unwrapResponse(
      openApiClient.DELETE("/api/v1/fileshare/files/{file_id}", {
        params: {
          path: { file_id: fileId },
          query: { reason_for_change: reason },
        },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /**
   * List audit log entries for a file record.
   */
  listAuditLogs(fileId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/audit-logs", {
        params: { path: { file_id: fileId } },
        ...options,
      })
    );
  },

  /**
   * Retrieve short-lived presigned HLS master manifest stream URL.
   */
  getStreamUrl(fileId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/stream-url", {
        params: { path: { file_id: fileId } },
        ...options,
      })
    );
  },

  /**
   * Record video playback checkpoint audit telemetry event.
   */
  recordPlaybackAudit(payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Video playback checkpoint audit event";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/audit/playback", {
        body: payload as paths["/api/v1/fileshare/audit/playback"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Formally approve a document with electronic signature and re-authentication.
   */
  approveFile(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.signing_reason || "Electronic signature document approval";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/approve", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/approve"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Reject a document for approval.
   */
  rejectFile(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Reject document for approval";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/reject", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/reject"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Rescind a previous document approval.
   */
  rescindApproval(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Rescinding document approval";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/rescind-approval", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/rescind-approval"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Initiate upload of a new version/revision for a file record.
   */
  uploadRevision(fileId: string, payload: unknown, options: Record<string, unknown> = {}) {
    const reason = (payload as Record<string, string>)?.reason_for_change || "Uploading document revision";
    return unwrapResponse(
      openApiClient.POST("/api/v1/fileshare/files/{file_id}/upload-revision", {
        params: { path: { file_id: fileId } },
        body: payload as paths["/api/v1/fileshare/files/{file_id}/upload-revision"]["post"]["requestBody"]["content"]["application/json"],
        headers: { "X-Change-Reason": reason },
        ...options,
      })
    );
  },

  /**
   * Retrieve chronological, filterable activity feed for a file record.
   */
  getActivityFeed(fileId: string, params: ActivityFeedParams = {}, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/fileshare/files/{file_id}/activity", {
        params: {
          path: { file_id: fileId },
          query: {
            event_type: params.event_type,
            from_date: params.from_date,
            to_date: params.to_date,
            page: params.page,
            page_size: params.page_size,
          },
        },
        ...options,
      })
    );
  },
};
