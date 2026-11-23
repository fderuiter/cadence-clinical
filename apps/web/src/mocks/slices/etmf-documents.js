import { demoNowIso, nextDemoStep } from "../../utils/demoMode.js";
import { cloneJson, decodeReasonHeader, readJsonBody } from "./shared.js";

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

export function createEtmfDocumentsSliceStore(ctx) {
  const { state } = ctx;

  function reset() {
    state.etmfDocuments = createInitialEtmfDocuments();
    state.etmfAuditLogs = createInitialEtmfAuditLogs();
    state.fileshareFiles = [
      {
        id: "file-demo-01",
        filename: "DEMO-01-Protocol-Synopsis.pdf",
        content_type: "application/pdf",
        size_bytes: 245760,
        study_id: "DEMO-01",
        site_id: "SITE-101",
        uploaded_by: "designer_demo",
        created_at: "2026-07-01T10:00:00Z", // deid-ignore
        is_on_hold: false,
        approval_status: "APPROVED",
        version_number: 1,
      },
    ];
  }

  const methods = {
    listEtmfDocuments() {
      return cloneJson(state.etmfDocuments);
    },

    listEtmfAuditLogs() {
      return {
        items: cloneJson(state.etmfAuditLogs),
        total_count: state.etmfAuditLogs.length,
      };
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
      return cloneJson(signed);
    },

    stageEtmfQcDocument(body = {}) {
      const step = nextDemoStep("etmf-stage-qc");
      const docId = `DOC-DEMO-QC-${step}`;
      const stagedAt = demoNowIso(step);
      const studyId = body?.studyId || body?.study_id || "CADENCE-101";
      const siteId = body?.siteId || body?.site_id || null;
      const filename = body?.filename || "staged_document.pdf";
      state.etmfDocuments.unshift({
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
      });
      return {
        document_id: docId,
        study_id: studyId,
        site_id: siteId,
        filename,
        status: "TECHNICAL_QC",
        staged_at: stagedAt,
        staged_by: "cra.monitor",
        qc_assigned_to: body?.assignedCra || body?.assigned_cra || null,
        discrepancy_notes: [],
        intelligence_report: {
          document_id: docId,
          filename,
          mime_type: body?.mimeType || body?.mime_type || "application/pdf",
          sha256_hash: `sha256-demo-qc-${step}`,
          confidence_tier: "HIGH",
          qc_recommendation: "AUTO_CLASSIFY",
          primary_classification: {
            zone: 5,
            section: "05.02",
            artifact_code: "05.02.01",
            artifact_type: "FDA Form 1572",
            confidence: 0.98,
          },
          alternative_candidates: [],
        },
      };
    },

    reviewEtmfQcDocument(docId, body = {}, fallbackReason = "") {
      const decision = String(body?.decision || "ACCEPT").toUpperCase();
      const nextStatus = decision === "REJECT" ? "REJECTED" : "APPROVED";
      const existing = state.etmfDocuments.find((d) => d.id === docId);
      if (existing) {
        existing.status = nextStatus;
        return cloneJson(existing);
      }
      return {
        id: docId,
        study_id: "CADENCE-101",
        filename: "reviewed_document.pdf",
        status: nextStatus,
        reason_for_change:
          body?.reasonForChange || body?.reason_for_change || fallbackReason,
      };
    },
  };

  return {
    reset,
    methods,
  };
}

export function createEtmfDocumentsSliceHandlers(
  store,
  { http, HttpResponse }
) {
  return [
    http.get("*/api/v1/etmf/taxonomy", () =>
      HttpResponse.json({
        version: "DIA-TMF-v3.2.0",
        zones: [
          {
            code: "01",
            name: "Trial Management",
            artifacts: [{ code: "01.01.01", name: "Trial Master File Plan" }],
          },
          {
            code: "05",
            name: "Site Management",
            artifacts: [
              { code: "05.02.02", name: "Delegation of Authority Log" },
            ],
          },
        ],
      })
    ),
    http.get("*/api/v1/etmf/documents", () =>
      HttpResponse.json(store.listEtmfDocuments())
    ),
    http.get(
      "*/api/v1/etmf/documents/:documentId/watermark",
      ({ params }) =>
        new HttpResponse(
          `CONFIDENTIAL AUDITOR WATERMARKED COPY — Document ${decodeURIComponent(String(params.documentId))}`,
          {
            status: 200,
            headers: { "Content-Type": "text/plain; charset=utf-8" },
          }
        )
    ),
    http.post(
      "*/api/v1/etmf/documents/:documentId/sign-off",
      async ({ params, request }) => {
        const docId = decodeURIComponent(String(params.documentId));
        const body = await readJsonBody(request);
        return HttpResponse.json(store.signOffEtmfDocument(docId, body));
      }
    ),
    http.get("*/api/v1/etmf/audit-logs", () =>
      HttpResponse.json(store.listEtmfAuditLogs())
    ),
    http.get("*/api/v1/etmf/completeness", ({ request }) => {
      const url = new URL(request.url);
      const firstDoc = store.state.etmfDocuments[0];
      return HttpResponse.json({
        study_id: url.searchParams.get("study_id") || "DEMO-01",
        milestone: url.searchParams.get("milestone") || "INITIATION",
        site_id: url.searchParams.get("site_id") || null,
        is_complete: true,
        present_count: 1,
        expected_count: 1,
        present_artifacts: ["Protocol"],
        per_artifact_detail: [
          {
            artifact_type: "Protocol",
            scope: "study",
            status: "PRESENT",
            document_id: firstDoc?.id || "doc-demo-01",
            version_index: firstDoc?.version_index ?? 1.0,
          },
        ],
      });
    }),
    http.get("*/api/v1/etmf/intelligence/qc-queue", () =>
      HttpResponse.json(
        store.state.etmfDocuments
          .filter((d) => d.status === "TECHNICAL_QC")
          .map((d) => ({
            document_id: d.id,
            study_id: d.study_id || "CADENCE-101",
            site_id: d.site_id || null,
            filename: d.filename,
            status: d.status,
            staged_at: d.uploaded_at || demoNowIso(0),
            staged_by: "cra.monitor",
            qc_assigned_to: null,
            discrepancy_notes: [],
            intelligence_report: {
              document_id: d.id,
              filename: d.filename,
              mime_type: "application/pdf",
              sha256_hash: "sha256-demo-qc-doc",
              confidence_tier: "HIGH",
              qc_recommendation: "AUTO_CLASSIFY",
              primary_classification: {
                zone: 5,
                section: "05.02",
                artifact_code: d.artifact_code || "05.02.01",
                artifact_type: d.artifact_type || "FDA Form 1572",
                confidence: 0.98,
              },
              alternative_candidates: [],
            },
          }))
      )
    ),
    http.post("*/api/v1/etmf/intelligence/classify", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json({
        document_id: null,
        filename: body?.filename || "document.pdf",
        mime_type: body?.mimeType || body?.mime_type || "application/pdf",
        sha256_hash: "sha256-demo-classified",
        confidence_tier: "HIGH",
        qc_recommendation: "AUTO_CLASSIFY",
        primary_classification: {
          zone: 5,
          section: "05.02",
          artifact_code: "05.02.01",
          artifact_type: "FDA Form 1572",
          confidence: 0.98,
        },
        alternative_candidates: [],
      });
    }),
    http.post("*/api/v1/etmf/intelligence/stage-qc", async ({ request }) => {
      const body = await readJsonBody(request);
      return HttpResponse.json(store.stageEtmfQcDocument(body), {
        status: 201,
      });
    }),
    http.post(
      "*/api/v1/etmf/intelligence/qc-review/:docId",
      async ({ params, request }) => {
        const docId = decodeURIComponent(String(params.docId));
        const body = await readJsonBody(request);
        return HttpResponse.json(
          store.reviewEtmfQcDocument(docId, body, decodeReasonHeader(request))
        );
      }
    ),
    http.get("*/api/v1/etmf/export-jobs", () => HttpResponse.json([])),
    http.get("*/api/v1/fileshare/files", () =>
      HttpResponse.json(store.state.fileshareFiles)
    ),
  ];
}
