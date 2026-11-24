import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";

function createInitialEtmfDocuments() {
  return [
    {
      id: "doc-demo-01",
      filename: "DEMO-01-Protocol-v2.0.pdf",
      zone: "01. Trial Management",
      section: "01.01 Trial Steering Committee",
      artifact_type: "Protocol",
      artifact_code: "01.01.02",
      status: "APPROVED",
      version_index: 1.0,
      site_id: "SITE-101",
      uploaded_at: "2026-07-01T10:00:00Z", // deid-ignore
    },
  ];
}

function createInitialEtmfAuditLogs() {
  return [
    {
      id: "elog-seed-01",
      timestamp: "2026-07-01T10:00:00Z", // deid-ignore
      user_id: "qa_director",
      user_role: "Sponsor Admin",
      action: "INGEST",
      document_id: "doc-demo-01",
      entity_type: "Document",
      details:
        "Ingested document: DEMO-01-Protocol-v2.0.pdf under Zone: 01. Trial Management",
      reason_for_change: "Initial protocol eTMF baseline registration",
    },
  ];
}

function buildEtmfQcQueueItem(
  doc,
  {
    mimeType = "application/pdf",
    sha256Hash = "sha256-demo-qc-doc",
    stagedAt = doc.uploaded_at || demoNowIso(0),
    qcAssignedTo = null,
  } = {}
) {
  return {
    document_id: doc.id,
    study_id: doc.study_id || "CADENCE-101",
    site_id: doc.site_id ?? null,
    filename: doc.filename,
    status: doc.status,
    staged_at: stagedAt,
    staged_by: "cra.monitor",
    qc_assigned_to: qcAssignedTo,
    discrepancy_notes: [],
    intelligence_report: {
      document_id: doc.id,
      filename: doc.filename,
      mime_type: mimeType,
      sha256_hash: sha256Hash,
      confidence_tier: "HIGH",
      qc_recommendation: "AUTO_CLASSIFY",
      primary_classification: {
        zone: 5,
        section: "05.02",
        artifact_code: doc.artifact_code || "05.02.01",
        artifact_type: doc.artifact_type || "FDA Form 1572",
        confidence: 0.98,
      },
      alternative_candidates: [],
    },
  };
}

export function createEtmfDomain(ctx) {
  const { state } = ctx;

  state.etmfDocuments = createInitialEtmfDocuments();
  state.etmfAuditLogs = createInitialEtmfAuditLogs();

  function seed() {
    state.etmfDocuments = createInitialEtmfDocuments();
    state.etmfAuditLogs = createInitialEtmfAuditLogs();
  }

  const methods = {
    listEtmfQcQueue() {
      return state.etmfDocuments
        .filter((d) => d.status === "TECHNICAL_QC")
        .map((d) => buildEtmfQcQueueItem(d));
    },

    signOffEtmfDocument(docId, body = {}) {
      const now = new Date().toISOString();
      const existing = state.etmfDocuments.find((d) => d.id === docId);
      const signed = {
        id: docId,
        filename: existing?.filename || body?.filename || "sample_protocol.pdf",
        zone: existing?.zone || "01. Trial Management",
        section: existing?.section || "01.01 Trial Steering Committee",
        artifact_type: existing?.artifact_type || "Protocol",
        artifact_code: existing?.artifact_code || "01.01.02",
        site_id: existing?.site_id || "SITE-101",
        status: "SIGNED",
        version_index: 1.1,
        uploaded_at: existing?.uploaded_at || now,
        signature_manifestation: {
          signer_id: body?.username || body?.signer_id || "auditor_demo",
          timestamp: now,
          signing_reason: body?.signing_reason || body?.reason || "APPROVAL",
          sha256_hash: "sha256-demo-etmf-signed-789",
          signature: "demo-part11-manifestation-seal",
        },
      };
      if (existing) {
        Object.assign(existing, signed);
      } else {
        state.etmfDocuments.unshift(signed);
      }
      state.etmfAuditLogs.unshift({
        id: `elog-${Date.now()}`,
        timestamp: now,
        user_id: signed.signature_manifestation.signer_id,
        user_role: "Sponsor Admin",
        action: "SIGN",
        document_id: docId,
        entity_type: "Document",
        details: `Digitally signed document ${signed.filename} (v1.1)`,
        reason_for_change: signed.signature_manifestation.signing_reason,
      });
      return signed;
    },

    stageEtmfQcDocument(body = {}) {
      const step = nextDemoStep("etmf-stage-qc");
      const docId = `DOC-DEMO-QC-${step}`;
      const stagedAt = demoNowIso(step);
      const studyId = body?.studyId || body?.study_id || "CADENCE-101";
      const siteId = body?.siteId || body?.site_id || null;
      const filename = body?.filename || "staged_document.pdf";
      const doc = {
        id: docId,
        study_id: studyId,
        site_id: siteId,
        filename,
        zone: 5,
        section: "05.02",
        artifact_type: "FDA Form 1572",
        artifact_code: "05.02.01",
        status: "TECHNICAL_QC",
        version_index: 1,
        uploaded_at: stagedAt,
      };
      state.etmfDocuments.unshift(doc);
      return buildEtmfQcQueueItem(doc, {
        mimeType: body?.mimeType || body?.mime_type || "application/pdf",
        sha256Hash: `sha256-demo-qc-${step}`,
        stagedAt,
        qcAssignedTo: body?.assignedCra || body?.assigned_cra || null,
      });
    },

    reviewEtmfQcDocument(docId, body = {}, fallbackReason = "") {
      const decision = String(body?.decision || "ACCEPT").toUpperCase();
      const nextStatus = decision === "REJECT" ? "REJECTED" : "APPROVED";
      const existing = state.etmfDocuments.find((d) => d.id === docId);
      if (existing) {
        existing.status = nextStatus;
      }
      return (
        existing || {
          id: docId,
          study_id: "CADENCE-101",
          filename: "reviewed_document.pdf",
          status: nextStatus,
          reason_for_change:
            body?.reasonForChange || body?.reason_for_change || fallbackReason,
        }
      );
    },
  };

  return { seed, methods };
}
