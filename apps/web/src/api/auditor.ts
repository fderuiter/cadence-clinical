import { unwrapResponse, getBaseUrl } from "./client";
import { openApiClient } from "./legacyClient";

export interface AuditLogParams {
  user_id?: string;
  action?: string;
  document_id?: string;
  record_id?: string;
  entity_type?: string;
  start_time?: string;
  start_date?: string;
  end_time?: string;
  end_date?: string;
  limit?: number;
  offset?: number;
}

/**
 * Service module for the Auditor/Inspection operations.
 * Interfaces with eTMF audit logs, execution ledger integrity, and binder exports.
 */
export const auditorService = {
  /**
   * Retrieves eTMF audit log events.
   * Supports filtering and paging.
   */
  getAuditLogs(
    params: AuditLogParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/audit-logs", {
        params: {
          query: {
            user_id: params.user_id,
            action: params.action,
            document_id: params.document_id,
            record_id: params.record_id,
            entity_type: params.entity_type,
            start_time: params.start_time || params.start_date,
            end_time: params.end_time || params.end_date,
            limit: params.limit,
            offset: params.offset,
          },
        },
        ...options,
      })
    );
  },

  /**
   * Retrieves the execution ledger integrity verification.
   */
  getExecutionIntegrity(options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/audit/integrity", {
        ...options,
      })
    );
  },

  /**
   * Generates watermarked content path or options.
   */
  getWatermarkedDownloadUrl(documentId: string): string {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/documents/${documentId}/watermark`;
  },

  /**
   * Initiates ZIP regulatory binder download for a clinical study.
   */
  getBinderExportUrl(studyId: string, includeHistory = false): string {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/studies/${studyId}/binder?include_history=${includeHistory}`;
  },
};
