import { allowUnbackedAction } from "../../utils/unbackedActions.js";

export const DEMO_TICKET_KPIS = Object.freeze({
  sla_compliance_rate: 96.4,
  mttr_hours: 4.2,
});

export function createInitialMockTickets(): Array<Record<string, any>> {
  return [
    {
      id: "tk-8801",
      reference: "DEV-2026-0041",
      title: "Investigational Product Cold-Chain Excursion at Site 101",
      description:
        "Cold-chain continuous temperature logger logged 9.4°C (protocol limit 2.0-8.0°C) during weekend storage.",
      category: "SUPPLY_EXCURSION",
      gxp_severity: "CRITICAL",
      priority: "CRITICAL",
      status: "OPEN",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      subject_id: null,
      entity_type: "SUPPLY_KIT",
      entity_id: "KIT-9941",
      created_by: "cra.monitor@cadence.clinical",
      created_at: new Date(Date.now() - 3600 * 1000 * 2).toISOString(),
      sla_target_at: new Date(Date.now() + 3600 * 1000 * 2).toISOString(),
      sla_breached: false,
      sla_amber_warned: false,
      version_index: 1,
    },
    {
      id: "tk-8802",
      reference: "DEV-2026-0042",
      title: "Missed Scheduled Blood Draw at Cycle 2 Day 1",
      description:
        "Subject 8812 PK blood sample collection missed due to scheduling conflict.",
      category: "PROTOCOL_DEVIATION",
      gxp_severity: "MAJOR",
      priority: "HIGH",
      status: "IN_PROGRESS",
      study_id: "CADENCE-101",
      site_id: "SITE-102",
      subject_id: "SUBJ-8812",
      entity_type: "SUBJECT",
      entity_id: "SUBJ-8812",
      created_by: "crc.smith@cadence.clinical",
      created_at: new Date(Date.now() - 3600 * 1000 * 8).toISOString(),
      sla_target_at: new Date(Date.now() + 3600 * 1000 * 16).toISOString(),
      sla_breached: false,
      sla_amber_warned: false,
      version_index: 2,
    },
    {
      id: "tk-8803",
      reference: "QRY-2026-0109",
      title: "Inconsistent Diastolic BP Observation in Vital Signs Form",
      description:
        "Systolic 120 recorded with Diastolic 130 mmHg. Out-of-bounds cross-field validation query.",
      category: "DATA_QUERY",
      gxp_severity: "MINOR",
      priority: "MEDIUM",
      status: "WAITING_ON_SITE",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      subject_id: "SUBJ-8815",
      entity_type: "CRF_FORM",
      entity_id: "FORM-VS-003",
      created_by: "dm.lead@cadence.clinical",
      created_at: new Date(Date.now() - 3600 * 1000 * 24).toISOString(),
      sla_target_at: new Date(Date.now() + 3600 * 1000 * 48).toISOString(),
      sla_total_paused_seconds: 3600 * 12,
      sla_breached: false,
      version_index: 2,
    },
    {
      id: "tk-8804",
      reference: "DEV-2026-0038",
      title: "Informed Consent Form Re-Consent Signed on V2 Instead of V3",
      description:
        "Subject 8804 re-consented on outdated paper template version during Protocol Amendment 2 rollout.",
      category: "PROTOCOL_DEVIATION",
      gxp_severity: "MAJOR",
      priority: "HIGH",
      status: "RESOLVED",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      subject_id: "SUBJ-8804",
      entity_type: "SUBJECT",
      entity_id: "SUBJ-8804",
      created_by: "cra.monitor@cadence.clinical",
      created_at: new Date(Date.now() - 3600 * 1000 * 72).toISOString(),
      sla_target_at: new Date(Date.now() - 3600 * 1000 * 24).toISOString(),
      signature_token: "sig_token_hex_a982fe41b3",
      signature_user: "dr_smith_pi",
      signature_timestamp: new Date(
        Date.now() - 3600 * 1000 * 12
      ).toISOString(),
      signature_meaning:
        "I approve the clinical assessment and corrective medical action.",
      root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
      root_cause_summary:
        "Site ICF version binders were updated and CRC re-training completed.",
      resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
      version_index: 4,
    },
  ];
}

export const initialMockTickets = createInitialMockTickets();

export function mergeDemoEscalatedTickets(
  baseTickets: Array<Record<string, any>> = [],
  escalatedTickets: Array<Record<string, any>> = []
): Array<Record<string, any>> {
  if (!Array.isArray(escalatedTickets) || escalatedTickets.length === 0) {
    return baseTickets;
  }
  const seen = new Set(baseTickets.map((t) => t.id || t.reference));
  const extra = escalatedTickets.filter(
    (t) => !seen.has(t.id) && !seen.has(t.reference)
  );
  return [...extra, ...baseTickets];
}

export function buildDemoTicketDetailSeed(
  ticket: Record<string, any>,
  formatCategory: (cat?: string) => string
) {
  return {
    comments: [
      {
        id: "cm-001",
        user_id: ticket.created_by || "cra.monitor@cadence.clinical",
        content: `Logged initial ${formatCategory(ticket.category)}: ${ticket.description}`,
        visibility: "PUBLIC",
        created_at: ticket.created_at,
      },
      {
        id: "cm-002",
        user_id: "sponsor.qa@cadence.clinical",
        content:
          "Quality assessment initiated under ICH E6(R3). Reviewing cold-chain calibration records.",
        visibility: "INTERNAL_SPONSOR",
        created_at: new Date(
          new Date(ticket.created_at).getTime() + 1800 * 1000
        ).toISOString(),
      },
    ],
    attachments: [
      {
        id: "att-001",
        filename: "logger_raw_export.csv",
        file_size_bytes: 42100,
        mime_type: "text/csv",
        sha256_hash:
          "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
        uploaded_by: ticket.created_by || "cra.monitor@cadence.clinical",
        uploaded_at: ticket.created_at,
        deid_scrubbed: true,
      },
    ],
    auditLogs: [
      {
        action: "CREATE",
        user_id: ticket.created_by || "cra.monitor@cadence.clinical",
        timestamp: ticket.created_at,
        change_reason: "Initial ticket registration",
      },
    ],
  };
}

export function applyDemoTicketTransition(
  ticketsList: Array<Record<string, any>>,
  {
    ticketId,
    newStatus,
    root_cause_category,
    root_cause_summary,
    resolution_code,
  }: {
    ticketId: string;
    newStatus: string;
    root_cause_category?: string;
    root_cause_summary?: string;
    resolution_code?: string;
  }
) {
  const index = ticketsList.findIndex((t) => t.id === ticketId);
  if (index === -1) return null;
  ticketsList[index].status = newStatus;
  if (root_cause_category) {
    ticketsList[index].root_cause_category = root_cause_category;
  }
  if (root_cause_summary) {
    ticketsList[index].root_cause_summary = root_cause_summary;
  }
  if (resolution_code) {
    ticketsList[index].resolution_code = resolution_code;
  }
  return { ...ticketsList[index] };
}

export function applyDemoBatchStatusTransition(
  ticketsList: Array<Record<string, any>>,
  targetIds: string[],
  statusToApply: string
): boolean {
  if (!allowUnbackedAction("Batch updating ticket status")) return false;
  ticketsList.forEach((t) => {
    if (targetIds.includes(t.id)) {
      if (t.status === "CLOSED" || t.signature_token) {
        return;
      }
      t.status = statusToApply;
      t.version_index = (t.version_index || 1) + 1;
      t.audit_logs = t.audit_logs || [];
      t.audit_logs.push({
        action: "BATCH_STATUS_UPDATE",
        user_id: "active.user@cadence.clinical",
        timestamp: new Date().toISOString(),
        change_reason: `Batch status transition to ${statusToApply}`,
      });
    }
  });
  return true;
}

export function applyDemoBatchSeverityUpdate(
  ticketsList: Array<Record<string, any>>,
  targetIds: string[],
  severityToApply: string
): boolean {
  if (!allowUnbackedAction("Batch updating ticket severity")) return false;
  ticketsList.forEach((t) => {
    if (targetIds.includes(t.id)) {
      if (t.status === "CLOSED") {
        return;
      }
      t.gxp_severity = severityToApply;
      t.version_index = (t.version_index || 1) + 1;
      t.audit_logs = t.audit_logs || [];
      t.audit_logs.push({
        action: "BATCH_SEVERITY_UPDATE",
        user_id: "active.user@cadence.clinical",
        timestamp: new Date().toISOString(),
        change_reason: `Batch severity update to ${severityToApply}`,
      });
    }
  });
  return true;
}

export function buildDemoTicketComment({
  content,
  visibility,
  reason_for_change,
}: {
  content: string;
  visibility?: string;
  reason_for_change?: string;
}) {
  return {
    id: "cm-" + Date.now(),
    user_id: "active.user@cadence.clinical",
    content,
    visibility,
    created_at: new Date().toISOString(),
    reason_for_change,
  };
}

export function buildDemoTicketAttachment({
  file,
  reason_for_change,
}: {
  file: File;
  reason_for_change?: string;
}) {
  if (!allowUnbackedAction("Attaching a file to a ticket")) return null;
  return {
    id: "att-" + Date.now(),
    filename: file.name,
    file_size_bytes: file.size,
    mime_type: file.type || "application/octet-stream",
    sha256_hash:
      "a4f891b2c3d4e5f67890123456789abcdef0123456789abcdef0123456789abc", // pragma: allowlist secret
    uploaded_by: "active.user@cadence.clinical",
    uploaded_at: new Date().toISOString(),
    deid_scrubbed: true,
    reason_for_change,
  };
}

export function applyDemoTicketSignature(
  ticketsList: Array<Record<string, any>>,
  {
    ticketId,
    signature_token,
    meaning,
  }: {
    ticketId: string;
    signature_token: string;
    meaning: string;
  }
) {
  const index = ticketsList.findIndex((t) => t.id === ticketId);
  if (index === -1) return null;
  ticketsList[index].signature_token = signature_token;
  ticketsList[index].signature_meaning = meaning;
  ticketsList[index].signature_timestamp = new Date().toISOString();
  ticketsList[index].signature_user = "active.user@cadence.clinical";
  return { ...ticketsList[index] };
}

export function exportDemoAuditTrail(
  ticketsList: Array<Record<string, any>>,
  format: "csv" | "json"
): boolean {
  if (!allowUnbackedAction("Exporting the ticket audit trail")) return false;
  const data = JSON.stringify(ticketsList, null, 2);
  const blob = new Blob([data], {
    type: format === "json" ? "application/json" : "text/csv",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `tickets_part11_audit_trail.${format}`;
  a.click();
  URL.revokeObjectURL(url);
  return true;
}

export function buildDemoResolvePayload(ticketId: string) {
  return {
    ticketId,
    newStatus: "RESOLVED",
    root_cause_category: "PROCESS_WORKFLOW_DEFICIT",
    root_cause_summary:
      "Clinical team addressed protocol checklist with site coordinator.",
    resolution_code: "CORRECTIVE_ACTION_IMPLEMENTED",
    reason_for_change:
      "Resolving ticket following comprehensive clinical assessment.",
  };
}
