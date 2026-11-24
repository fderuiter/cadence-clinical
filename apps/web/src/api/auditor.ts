import { openApiClient, unwrapResponse, getBaseUrl } from "./client";
import { kernelApi } from "./kernelApi";
import { isDemoBuild } from "../utils/demoMode";

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
  scope?: string;
}

/**
 * Service module for Auditor/Inspection operations.
 * In live (.NET kernel) builds, routes audit queries to GET /api/v1/kernel/activity
 * and never calls unbacked /api/v1/etmf/audit-logs or /api/v1/execution/audit/integrity routes.
 */
export const auditorService = {
  async getAuditLogs(
    params: AuditLogParams = {},
    options: Record<string, unknown> = {}
  ) {
    if (!isDemoBuild()) {
      const limit = params.limit ?? 50;
      const offset = params.offset ?? 0;
      const page = await kernelApi.activity({
        scope: params.scope,
        actor: params.user_id || undefined,
        from: params.start_time || params.start_date || undefined,
        before: params.end_time || params.end_date || undefined,
        limit,
      });
      const items = (page.entries ?? []).map((entry) => ({
        id: entry.event_id,
        audit_entry_ref: entry.audit_entry_ref ?? null,
        attestation_ref: entry.attestation_ref ?? null,
        timestamp: entry.recorded,
        user_id: entry.actor_id,
        user_role: "",
        action: entry.mask ? "RESTRICTED" : entry.type,
        entity_type: entry.subject_kind ?? entry.family,
        record_id: entry.subject ?? "",
        document_id: entry.subject ?? "",
        reason_for_change: entry.reason_for_change ?? null,
        details: entry.reason_for_change ?? "",
        mask: entry.mask ?? null,
      }));
      return {
        items,
        total_count: items.length,
        limit,
        offset,
        has_more: Boolean(page.next),
        next: page.next ?? null,
      };
    }

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

  async getExecutionIntegrity(options: Record<string, unknown> = {}) {
    if (!isDemoBuild()) {
      return {
        verified: null,
        status: "UNAVAILABLE",
        message:
          "Whole-ledger execution integrity verification is served per kernel audit entry via GET /api/v1/kernel/audit-entries/{auditRef}.",
      };
    }

    return unwrapResponse(
      openApiClient.GET("/api/v1/execution/audit/integrity", {
        ...options,
      })
    );
  },

  getWatermarkedDownloadUrl(documentId: string): string {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/documents/${documentId}/watermark`;
  },

  getBinderExportUrl(studyId: string, includeHistory = false): string {
    const baseUrl = getBaseUrl();
    return `${baseUrl}/api/v1/etmf/studies/${studyId}/binder?include_history=${includeHistory}`;
  },
};
