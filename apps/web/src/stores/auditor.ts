import { defineStore } from "pinia";
import { auditorService } from "../api/auditor";

export interface DateRange {
  start: string;
  end: string;
}

export interface AuditorFilters {
  dateRange: DateRange;
  user: string;
  eventType: string;
}

export interface InspectionSession {
  expiresAt: string;
}

export interface AuditEvent {
  id?: string;
  timestamp: string;
  user_id?: string;
  user?: string;
  user_role?: string;
  action: string;
  details?: string;
  message?: string;
  document_id?: string | null;
  reason_for_change?: string;
  reasonForChange?: string;
  version_index?: number;
  versionIndex?: number;
}

export const useAuditorStore = defineStore("auditor", {
  state: () => ({
    inspectionSession: {
      expiresAt: "2026-12-31T23:59:59Z",
    } as InspectionSession | null,
    auditEvents: [] as AuditEvent[],
    selectedStudyId: "study_001",
    filters: {
      dateRange: {
        start: "",
        end: "",
      },
      user: "",
      eventType: "",
    } as AuditorFilters,
    loading: false,
    error: null as string | null,
  }),
  actions: {
    setFilters(newFilters: Partial<AuditorFilters>) {
      this.filters = { ...this.filters, ...newFilters };
    },
    async fetchAuditLogs() {
      this.loading = true;
      this.error = null;
      try {
        const params: any = {};
        if (this.filters.user) {
          params.user_id = this.filters.user;
        }
        if (this.filters.eventType) {
          params.action = this.filters.eventType;
        }
        if (this.filters.dateRange?.start) {
          params.start_time = this.filters.dateRange.start;
        }
        if (this.filters.dateRange?.end) {
          params.end_time = this.filters.dateRange.end;
        }
        const res = await auditorService.getAuditLogs(params);
        this.auditEvents = res?.items || [];
      } catch (err: any) {
        this.error = err.message || "Failed to fetch audit logs";
        console.error("fetchAuditLogs error:", err);
      } finally {
        this.loading = false;
      }
    },
    async exportAuditTrail(format: "CSV" | "JSON" | "PDF") {
      const filename = `audit_trail_export.${format.toLowerCase()}`;
      let content = "";
      let mimeType = "text/plain";

      if (format === "JSON") {
        content = JSON.stringify(this.auditEvents, null, 2);
        mimeType = "application/json";
      } else if (format === "CSV") {
        const headers = ["Timestamp", "User", "Action", "Details", "Reason for Change", "Version Index"];
        const rows = this.auditEvents.map((event) => [
          event.timestamp || "",
          event.user_id || event.user || "",
          event.action || "",
          event.details || event.message || "",
          event.reason_for_change || event.reasonForChange || "",
          event.version_index !== undefined ? event.version_index : (event.versionIndex !== undefined ? event.versionIndex : ""),
        ]);
        content = [
          headers.join(","),
          ...rows.map((r) => r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(",")),
        ].join("\n");
        mimeType = "text/csv";
      } else {
        // PDF format Mock representation
        content = `%PDF-1.4\n%...\nAudit Trail Export\nGenerated: ${new Date().toISOString()}\n\n`;
        this.auditEvents.forEach((event) => {
          content += `[${event.timestamp}] User: ${event.user_id || event.user} - Action: ${event.action}\nDetails: ${event.details || event.message}\nReason: ${event.reason_for_change || event.reasonForChange || ""}\n\n`;
        });
        mimeType = "application/pdf";
      }

      const blob = new Blob([content], { type: mimeType });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    },
  },
});
